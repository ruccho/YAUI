using System;
using System.Collections.Generic;
using Yaui.Core;
using System.Runtime.InteropServices;
using Unity.Jobs;
using Unity.Profiling;

namespace Yaui.Text
{
    /// <summary>
    /// Text generation within the frame pipeline (planning/design-core.md):
    /// <list type="number">
    /// <item><see cref="Schedule"/> at the early submission (start of PreLateUpdate) and again at the deadline
    /// (start of PostLateUpdate): changed texts are prepared on the main thread and generated in parallel on worker
    /// threads, with the width of their previous layout.</item>
    /// <item>The layout job depends on <see cref="Handle"/>; measuring a text at another width regenerates it on the
    /// layout thread (<see cref="YauiText"/>).</item>
    /// <item><see cref="Finish"/> after the layout is applied: regenerates texts whose final width needs it, adds
    /// the missing glyphs of all of them at once, and writes the glyph primitives.</item>
    /// </list>
    /// </summary>
    internal static class TextPipeline
    {
        private static readonly ProfilerMarker FinishMarker = new("Yaui.Text.Finish");
        private static readonly ProfilerMarker ScheduleMarker = new("Yaui.Text.Schedule");

        private const int MaxParallelism = 2;

        /// <summary>Texts whose content or look changed since the last scheduling.</summary>
        private static readonly List<YauiText> Dirty = new();

        /// <summary>Texts to convert into primitives at <see cref="Finish"/>.</summary>
        private static readonly List<YauiText> Render = new();

        private static readonly List<YauiText> InFlight = new();
        private static readonly List<AtgText> ResolveBuffer = new();

        // Batches of this frame (early and deadline), kept alive until the jobs complete.
        private static readonly List<Batch> Batches = new();
        private static readonly Stack<Batch> BatchPool = new();

        private static JobHandle _handle;

        private sealed class Batch
        {
            public readonly List<AtgText> Texts = new();
            public readonly List<float> Widths = new();
            public GCHandle TextsHandle;
            public GCHandle WidthsHandle;
        }

        /// <summary>Registered texts.</summary>
        private static readonly HashSet<YauiText> Live = new();

        // Set by TextCore's notifications, possibly off the main thread.
        private static volatile bool _fontsChanged;
        private static Delegate _fontPropertyHandler;
        private static object _fontPropertyEvent;
        private static Action<UnityEngine.Texture, UnityEngine.TextCore.Text.FontAsset> _textureChangedHandler;

        /// <summary>
        /// Counts changes of font assets. Text generators of an older epoch are replaced: their generation info
        /// caches results with the glyph positions of the old atlases.
        /// </summary>
        public static int FontEpoch { get; private set; }

        public static void Register(YauiText text)
        {
            Live.Add(text);
        }

        public static void Unregister(YauiText text)
        {
            Live.Remove(text);
        }

        /// <summary>
        /// Main thread: listens to TextCore's notifications of changed font assets (atlases cleared, e.g. by the
        /// editor, or textures replaced). Both are internal and reached through reflection.
        /// </summary>
        public static void Subscribe()
        {
            try
            {
                var assembly = typeof(UnityEngine.TextCore.Text.FontAsset).Assembly;
                var events = assembly.GetType("UnityEngine.TextCore.Text.TextEventManager");
                _fontPropertyEvent = events?.GetField("FONT_PROPERTY_EVENT",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static)?.GetValue(null);
                if (_fontPropertyEvent != null)
                {
                    _fontPropertyHandler = new Action<bool, UnityEngine.Object>(OnFontPropertyChanged);
                    _fontPropertyEvent.GetType().GetMethod("Add", new[] { typeof(Action<bool, UnityEngine.Object>) })
                        ?.Invoke(_fontPropertyEvent, new object[] { _fontPropertyHandler });
                }

                var textureChanged = TextureChangedField;
                if (textureChanged != null)
                {
                    _textureChangedHandler = OnFontTextureChanged;
                    textureChanged.SetValue(null, Delegate.Combine((Delegate)textureChanged.GetValue(null),
                        _textureChangedHandler));
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[YAUI] Changes of font assets are not detected: {e.Message}");
            }
        }

        public static void Unsubscribe()
        {
            try
            {
                if (_fontPropertyEvent != null && _fontPropertyHandler != null)
                    _fontPropertyEvent.GetType().GetMethod("Remove", new[] { typeof(Action<bool, UnityEngine.Object>) })
                        ?.Invoke(_fontPropertyEvent, new object[] { _fontPropertyHandler });

                var textureChanged = TextureChangedField;
                if (textureChanged != null && _textureChangedHandler != null)
                    textureChanged.SetValue(null, Delegate.Remove((Delegate)textureChanged.GetValue(null),
                        _textureChangedHandler));
            }
            catch (Exception)
            {
                // Unsubscribing is best effort.
            }

            _fontPropertyEvent = null;
            _fontPropertyHandler = null;
            _textureChangedHandler = null;
            Live.Clear();
        }

        private static System.Reflection.FieldInfo TextureChangedField =>
            typeof(UnityEngine.TextCore.Text.FontAsset).GetField("OnFontAssetTextureChanged",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static);

        private static void OnFontPropertyChanged(bool changed, UnityEngine.Object asset)
        {
            FontsChanged();
        }

        private static void OnFontTextureChanged(UnityEngine.Texture texture, UnityEngine.TextCore.Text.FontAsset asset)
        {
            FontsChanged();
        }

        private static void FontsChanged()
        {
            _fontsChanged = true;
            YauiSystem.RequestUpdate();
        }

        /// <summary>The text jobs of this frame. The layout depends on it.</summary>
        public static JobHandle Handle => _handle;

        public static void MarkDirty(YauiText text)
        {
            if (!text.GenerationDirty)
            {
                text.GenerationDirty = true;
                Dirty.Add(text);
            }
        }

        public static void MarkRender(YauiText text)
        {
            if (!text.RenderPending)
            {
                text.RenderPending = true;
                Render.Add(text);
            }
        }

        /// <summary>Main thread: prepares the changed texts and starts generating them on worker threads.</summary>
        public static void Schedule()
        {
            if (_fontsChanged)
            {
                // Glyphs of cleared or rebuilt font atlases are gone or moved: every text generates again.
                _fontsChanged = false;
                FontEpoch++;
                Pools.ClearTexts();
                foreach (var text in Live) MarkDirty(text);
            }

            if (Dirty.Count == 0) return;

            if (!AtgText.IsSupported)
            {
                // The public generator runs on the main thread, now.
                foreach (var text in Dirty)
                {
                    text.GenerationDirty = false;
                    if (text.PrepareFallback()) MarkRender(text);
                }

                Dirty.Clear();
                return;
            }

            using var _ = ScheduleMarker.Auto();

            // A text changed again after the early scheduling is still being generated.
            foreach (var text in Dirty)
                if (text.GenerationInFlight)
                {
                    Complete();
                    break;
                }

            var batch = BatchPool.Count > 0 ? BatchPool.Pop() : new Batch();
            foreach (var text in Dirty)
            {
                text.GenerationDirty = false;
                if (!text.PrepareGeneration(out var atg, out var width)) continue;

                text.GenerationInFlight = true;
                InFlight.Add(text);
                batch.Texts.Add(atg);
                batch.Widths.Add(width);
                MarkRender(text);
            }

            Dirty.Clear();
            if (batch.Texts.Count == 0)
            {
                BatchPool.Push(batch);
                return;
            }

            AtgText.PrepareForJobs();
            batch.TextsHandle = GCHandle.Alloc(batch.Texts);
            batch.WidthsHandle = GCHandle.Alloc(batch.Widths);
            Batches.Add(batch);
            var job = new GenerateJob { Texts = batch.TextsHandle, Widths = batch.WidthsHandle };
            // ATG serializes generation with an internal lock, so more workers only contend for it. Two let the
            // main thread help while it waits, without much contention.
            var batchSize = Math.Max(1, (batch.Texts.Count + MaxParallelism - 1) / MaxParallelism);
            _handle = JobHandle.CombineDependencies(_handle,
                job.ScheduleParallel(batch.Texts.Count, batchSize, default));
            JobHandle.ScheduleBatchedJobs();
        }

        /// <summary>Main thread: waits for the text jobs.</summary>
        public static void Complete()
        {
            _handle.Complete();
            _handle = default;
            foreach (var text in InFlight) text.GenerationInFlight = false;

            InFlight.Clear();
            foreach (var batch in Batches)
            {
                batch.TextsHandle.Free();
                batch.WidthsHandle.Free();
                batch.Texts.Clear();
                batch.Widths.Clear();
                BatchPool.Push(batch);
            }

            Batches.Clear();
        }

        /// <summary>Main thread, after the layout is applied: writes the glyphs of the changed texts.</summary>
        public static void Finish()
        {
            if (Render.Count == 0) return;

            using var _ = FinishMarker.Auto();
            ResolveBuffer.Clear();
            foreach (var text in Render)
                if (text.PrepareRender(out var atg))
                    ResolveBuffer.Add(atg);

            AtgText.ResolveMissingGlyphs(ResolveBuffer);
            foreach (var text in Render)
            {
                text.RenderPending = false;
                text.WriteGlyphs();
            }

            Render.Clear();
            ResolveBuffer.Clear();
        }

        public static void Forget(YauiText text)
        {
            Dirty.Remove(text);
            Render.Remove(text);
            text.GenerationDirty = false;
            text.RenderPending = false;
        }

        private struct GenerateJob : IJobFor
        {
            public GCHandle Texts;
            public GCHandle Widths;

            public void Execute(int index)
            {
                ((List<AtgText>)Texts.Target)[index].Generate(((List<float>)Widths.Target)[index]);
            }
        }
    }
}