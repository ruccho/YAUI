using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace Yaui.Text
{
    /// <summary>
    /// The settings of a generation, independent of the Unity version: every field of the internal
    /// <c>NativeTextGenerationSettings</c> of any supported version. A <see cref="AtgBackend"/> copies them into the
    /// mirror of the running version. Enum fields are their underlying integer types.
    /// </summary>
    internal struct AtgSettings
    {
        public IntPtr fontAsset;
        public IntPtr textSettings;

        // 6000.6 and later: the text in a pinned buffer, rich text parsed natively.
        public IntPtr textBufferPtr;
        public int textBufferLength;

        // Before 6000.6: the text as a string, and the spans of parsed rich text (a TextSpan[]).
        public string text;
        public object textSpans;

        public int screenWidth;
        public int screenHeight;
        public bool wordWrapEnabled;
        public int overflow;
        public int languageDirection;
        public int vertexPadding;
        public int horizontalAlignment;
        public int verticalAlignment;
        public int preProcessFlags;
        public int fontSize;
        public bool bestFit;
        public int maxFontSize;
        public int minFontSize;
        public int fontStyle;
        public int fontWeight;
        public int characterSpacing;
        public int wordSpacing;
        public int paragraphSpacing;
        public int lineSpacing;
        public Color32 color;
        public bool disableAdvancedFontFeatures;
        public bool richTextEnabled;
        public int hoveredTag;
        public int pixelsPerPointFixed64;
        public bool minContentMeasure;
        public bool flipYAxis;
    }

    /// <summary>A mirror of one layout of the internal <c>NativeTextGenerationSettings</c>.</summary>
    internal interface ISettingsMirror
    {
        /// <summary>Whether the text is passed in a buffer (6000.6 and later) rather than as a string.</summary>
        bool UsesTextBuffer { get; }

        /// <summary>Whether it has <c>flipYAxis</c> (6000.7 and later). Without it, positions are Y down.</summary>
        bool HasFlipYAxis { get; }

        void From(in AtgSettings s);

        /// <summary>Copies back the text and spans that rich text parsing replaced (string layouts only).</summary>
        void CopyParsedText(ref AtgSettings s);
    }

    // Mirrors of the internal NativeTextGenerationSettings. Field names must match the originals: the layouts are
    // compared with the real type at startup, and the first that matches is used. References (the string and the
    // TextSpan[]) are mirrored as references.

    /// <summary>6000.7.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeSettings6000_7 : ISettingsMirror
    {
        public IntPtr fontAsset;
        public IntPtr textSettings;
        public IntPtr textBufferPtr;
        public int textBufferLength;
        public int screenWidth;
        public int screenHeight;
        public bool wordWrapEnabled;
        public int overflow;
        public int languageDirection;
        public int vertexPadding;
        public int horizontalAlignment;
        public int verticalAlignment;
        public int preProcessFlags;
        public int fontSize;
        public bool bestFit;
        public int maxFontSize;
        public int minFontSize;
        public int fontStyle;
        public int fontWeight;
        public int characterSpacing;
        public int wordSpacing;
        public int paragraphSpacing;
        public int lineSpacing;
        public Color32 color;
        public bool disableAdvancedFontFeatures;
        public bool richTextEnabled;
        public int hoveredTag;
        public int pixelsPerPointFixed64;
        public bool minContentMeasure;
        public bool flipYAxis;

        public bool UsesTextBuffer => true;
        public bool HasFlipYAxis => true;

        public void From(in AtgSettings s)
        {
            fontAsset = s.fontAsset;
            textSettings = s.textSettings;
            textBufferPtr = s.textBufferPtr;
            textBufferLength = s.textBufferLength;
            screenWidth = s.screenWidth;
            screenHeight = s.screenHeight;
            wordWrapEnabled = s.wordWrapEnabled;
            overflow = s.overflow;
            languageDirection = s.languageDirection;
            vertexPadding = s.vertexPadding;
            horizontalAlignment = s.horizontalAlignment;
            verticalAlignment = s.verticalAlignment;
            preProcessFlags = s.preProcessFlags;
            fontSize = s.fontSize;
            bestFit = s.bestFit;
            maxFontSize = s.maxFontSize;
            minFontSize = s.minFontSize;
            fontStyle = s.fontStyle;
            fontWeight = s.fontWeight;
            characterSpacing = s.characterSpacing;
            wordSpacing = s.wordSpacing;
            paragraphSpacing = s.paragraphSpacing;
            lineSpacing = s.lineSpacing;
            color = s.color;
            disableAdvancedFontFeatures = s.disableAdvancedFontFeatures;
            richTextEnabled = s.richTextEnabled;
            hoveredTag = s.hoveredTag;
            pixelsPerPointFixed64 = s.pixelsPerPointFixed64;
            minContentMeasure = s.minContentMeasure;
            flipYAxis = s.flipYAxis;
        }

        public void CopyParsedText(ref AtgSettings s)
        {
        }
    }

    /// <summary>6000.6.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeSettings6000_6 : ISettingsMirror
    {
        public IntPtr fontAsset;
        public IntPtr textSettings;
        public IntPtr textBufferPtr;
        public int textBufferLength;
        public int screenWidth;
        public int screenHeight;
        public bool wordWrapEnabled;
        public int overflow;
        public int languageDirection;
        public int vertexPadding;
        public int horizontalAlignment;
        public int verticalAlignment;
        public int preProcessFlags;
        public int fontSize;
        public bool bestFit;
        public int maxFontSize;
        public int minFontSize;
        public int fontStyle;
        public int fontWeight;
        public int characterSpacing;
        public int wordSpacing;
        public int paragraphSpacing;
        public Color32 color;
        public bool disableAdvancedFontFeatures;
        public bool richTextEnabled;
        public int hoveredTag;
        public int pixelsPerPointFixed64;

        public bool UsesTextBuffer => true;
        public bool HasFlipYAxis => false;

        public void From(in AtgSettings s)
        {
            fontAsset = s.fontAsset;
            textSettings = s.textSettings;
            textBufferPtr = s.textBufferPtr;
            textBufferLength = s.textBufferLength;
            screenWidth = s.screenWidth;
            screenHeight = s.screenHeight;
            wordWrapEnabled = s.wordWrapEnabled;
            overflow = s.overflow;
            languageDirection = s.languageDirection;
            vertexPadding = s.vertexPadding;
            horizontalAlignment = s.horizontalAlignment;
            verticalAlignment = s.verticalAlignment;
            preProcessFlags = s.preProcessFlags;
            fontSize = s.fontSize;
            bestFit = s.bestFit;
            maxFontSize = s.maxFontSize;
            minFontSize = s.minFontSize;
            fontStyle = s.fontStyle;
            fontWeight = s.fontWeight;
            characterSpacing = s.characterSpacing;
            wordSpacing = s.wordSpacing;
            paragraphSpacing = s.paragraphSpacing;
            color = s.color;
            disableAdvancedFontFeatures = s.disableAdvancedFontFeatures;
            richTextEnabled = s.richTextEnabled;
            hoveredTag = s.hoveredTag;
            pixelsPerPointFixed64 = s.pixelsPerPointFixed64;
        }

        public void CopyParsedText(ref AtgSettings s)
        {
        }
    }

    /// <summary>6000.5.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeSettings6000_5 : ISettingsMirror
    {
        public IntPtr fontAsset;
        public IntPtr textSettings;
        public string text;
        public int screenWidth;
        public int screenHeight;
        public bool wordWrapEnabled;
        public int overflow;
        public int languageDirection;
        public int vertexPadding;
        public int horizontalAlignment;
        public int verticalAlignment;
        public int fontSize;
        public bool bestFit;
        public int maxFontSize;
        public int minFontSize;
        public int fontStyle;
        public int fontWeight;
        public object textSpans;
        public Color32 color;
        public int characterSpacing;
        public int wordSpacing;
        public int paragraphSpacing;
        public int preProcessFlags;
        public bool disableAdvancedFontFeatures;
        public bool richTextEnabled;

        public bool UsesTextBuffer => false;
        public bool HasFlipYAxis => false;

        public void From(in AtgSettings s)
        {
            fontAsset = s.fontAsset;
            textSettings = s.textSettings;
            text = s.text;
            screenWidth = s.screenWidth;
            screenHeight = s.screenHeight;
            wordWrapEnabled = s.wordWrapEnabled;
            overflow = s.overflow;
            languageDirection = s.languageDirection;
            vertexPadding = s.vertexPadding;
            horizontalAlignment = s.horizontalAlignment;
            verticalAlignment = s.verticalAlignment;
            fontSize = s.fontSize;
            bestFit = s.bestFit;
            maxFontSize = s.maxFontSize;
            minFontSize = s.minFontSize;
            fontStyle = s.fontStyle;
            fontWeight = s.fontWeight;
            textSpans = s.textSpans;
            color = s.color;
            characterSpacing = s.characterSpacing;
            wordSpacing = s.wordSpacing;
            paragraphSpacing = s.paragraphSpacing;
            preProcessFlags = s.preProcessFlags;
            disableAdvancedFontFeatures = s.disableAdvancedFontFeatures;
            richTextEnabled = s.richTextEnabled;
        }

        public void CopyParsedText(ref AtgSettings s)
        {
            s.text = text;
            s.textSpans = textSpans;
        }
    }

    /// <summary>6000.4, and 6000.3 before <c>disableAdvancedFontFeatures</c> was added in a patch release.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeSettings6000_4 : ISettingsMirror
    {
        public IntPtr fontAsset;
        public IntPtr textSettings;
        public string text;
        public int screenWidth;
        public int screenHeight;
        public bool wordWrapEnabled;
        public int overflow;
        public int languageDirection;
        public int vertexPadding;
        public int horizontalAlignment;
        public int verticalAlignment;
        public int fontSize;
        public bool bestFit;
        public int maxFontSize;
        public int minFontSize;
        public int fontStyle;
        public int fontWeight;
        public object textSpans;
        public Color32 color;
        public int characterSpacing;
        public int wordSpacing;
        public int paragraphSpacing;
        public int preProcessFlags;

        public bool UsesTextBuffer => false;
        public bool HasFlipYAxis => false;

        public void From(in AtgSettings s)
        {
            fontAsset = s.fontAsset;
            textSettings = s.textSettings;
            text = s.text;
            screenWidth = s.screenWidth;
            screenHeight = s.screenHeight;
            wordWrapEnabled = s.wordWrapEnabled;
            overflow = s.overflow;
            languageDirection = s.languageDirection;
            vertexPadding = s.vertexPadding;
            horizontalAlignment = s.horizontalAlignment;
            verticalAlignment = s.verticalAlignment;
            fontSize = s.fontSize;
            bestFit = s.bestFit;
            maxFontSize = s.maxFontSize;
            minFontSize = s.minFontSize;
            fontStyle = s.fontStyle;
            fontWeight = s.fontWeight;
            textSpans = s.textSpans;
            color = s.color;
            characterSpacing = s.characterSpacing;
            wordSpacing = s.wordSpacing;
            paragraphSpacing = s.paragraphSpacing;
            preProcessFlags = s.preProcessFlags;
        }

        public void CopyParsedText(ref AtgSettings s)
        {
            s.text = text;
            s.textSpans = textSpans;
        }
    }

    /// <summary>6000.3 from the patch release that added <c>disableAdvancedFontFeatures</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeSettings6000_3 : ISettingsMirror
    {
        public IntPtr fontAsset;
        public IntPtr textSettings;
        public string text;
        public int screenWidth;
        public int screenHeight;
        public bool wordWrapEnabled;
        public int overflow;
        public int languageDirection;
        public int vertexPadding;
        public int horizontalAlignment;
        public int verticalAlignment;
        public int fontSize;
        public bool bestFit;
        public int maxFontSize;
        public int minFontSize;
        public int fontStyle;
        public int fontWeight;
        public object textSpans;
        public Color32 color;
        public bool disableAdvancedFontFeatures;
        public int characterSpacing;
        public int wordSpacing;
        public int paragraphSpacing;
        public int preProcessFlags;

        public bool UsesTextBuffer => false;
        public bool HasFlipYAxis => false;

        public void From(in AtgSettings s)
        {
            fontAsset = s.fontAsset;
            textSettings = s.textSettings;
            text = s.text;
            screenWidth = s.screenWidth;
            screenHeight = s.screenHeight;
            wordWrapEnabled = s.wordWrapEnabled;
            overflow = s.overflow;
            languageDirection = s.languageDirection;
            vertexPadding = s.vertexPadding;
            horizontalAlignment = s.horizontalAlignment;
            verticalAlignment = s.verticalAlignment;
            fontSize = s.fontSize;
            bestFit = s.bestFit;
            maxFontSize = s.maxFontSize;
            minFontSize = s.minFontSize;
            fontStyle = s.fontStyle;
            fontWeight = s.fontWeight;
            textSpans = s.textSpans;
            color = s.color;
            disableAdvancedFontFeatures = s.disableAdvancedFontFeatures;
            characterSpacing = s.characterSpacing;
            wordSpacing = s.wordSpacing;
            paragraphSpacing = s.paragraphSpacing;
            preProcessFlags = s.preProcessFlags;
        }

        public void CopyParsedText(ref AtgSettings s)
        {
            s.text = text;
            s.textSpans = textSpans;
        }
    }

    /// <summary>
    /// The TextLib methods that take the settings by value, bound to the mirror of the running version (see
    /// <see cref="AtgInternals"/>).
    /// </summary>
    internal abstract class AtgBackend
    {
        public abstract bool UsesTextBuffer { get; }
        public abstract bool FlipsY { get; }

        public abstract NativeTextInfo GenerateText(object textLib, in AtgSettings settings, IntPtr generationInfo,
            ref bool wasCached);

        /// <summary><paramref name="colorsByMesh"/> is only used before 6000.6.</summary>
        public abstract void ProcessMeshInfos(object textLib, NativeTextInfo textInfo, in AtgSettings settings,
            ref List<List<List<int>>> indicesByMesh, ref List<bool> colorsByMesh, bool uvsAreGenerated);

        /// <summary>
        /// Main thread, before 6000.6: replaces <see cref="AtgSettings.text"/> with the text without tags and sets
        /// <see cref="AtgSettings.textSpans"/>. Does nothing if the parser is not available: the tags are then shown.
        /// </summary>
        public abstract void ParseRichText(ref AtgSettings settings, TextSettings textSettings);
    }

    internal sealed unsafe class AtgBackend<T> : AtgBackend where T : struct, ISettingsMirror
    {
        private readonly delegate*<object, T, IntPtr, ref bool, NativeTextInfo> _generateText;

        // 6000.6 and later.
        private readonly delegate*<object, NativeTextInfo, T, ref List<List<List<int>>>, bool, void> _processMeshInfos;

        // Before 6000.6: with the colors of the meshes.
        private readonly delegate*<object, NativeTextInfo, T, ref List<List<List<int>>>, ref List<bool>, bool, void>
            _processMeshInfosWithColors;

        // Before 6000.6: RichTextTagParser.CreateTextGenerationSettingsArray, or null. 6000.5 has no hyperlink color
        // and takes the hovered link instead.
        private readonly delegate*<ref T, object, Color, float, TextSettings, void> _createTextSpans;
        private readonly delegate*<ref T, object, float, TextSettings, int, void> _createTextSpansHovered;
        private readonly object _links;

        public AtgBackend(Type textLibType, Type settingsType, Type textInfoType)
        {
            const BindingFlags anyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            const BindingFlags anyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            var indicesByMesh = typeof(List<List<List<int>>>).MakeByRefType();
            _generateText = (delegate*<object, T, IntPtr, ref bool, NativeTextInfo>)AtgInternals.Pointer(
                AtgInternals.GetMethod(textLibType, "GenerateText", anyInstance, textInfoType, settingsType,
                    typeof(IntPtr), typeof(bool).MakeByRefType()));

            var process = textLibType.GetMethod("ProcessMeshInfos", anyInstance, null,
                new[] { textInfoType, settingsType, indicesByMesh, typeof(bool) }, null);
            if (process != null)
                _processMeshInfos =
                    (delegate*<object, NativeTextInfo, T, ref List<List<List<int>>>, bool, void>)AtgInternals.Pointer(
                        process);
            else
                _processMeshInfosWithColors =
                    (delegate*<object, NativeTextInfo, T, ref List<List<List<int>>>, ref List<bool>, bool, void>)
                    AtgInternals.Pointer(AtgInternals.GetMethod(textLibType, "ProcessMeshInfos", anyInstance,
                        typeof(void), textInfoType, settingsType, indicesByMesh,
                        typeof(List<bool>).MakeByRefType(), typeof(bool)));

            if (default(T).UsesTextBuffer) return;

            // Rich text is optional: without the parser, tags are shown as they are.
            var parser = textLibType.Assembly.GetType("UnityEngine.TextCore.RichTextTagParser");
            var createSpans = parser?.GetMethod("CreateTextGenerationSettingsArray", anyStatic);
            var parameters = createSpans?.GetParameters();
            if (parameters is not { Length: 5 } || parameters[0].ParameterType != settingsType.MakeByRefType() ||
                createSpans.ReturnType != typeof(void)) return;

            var types = Array.ConvertAll(parameters, p => p.ParameterType);
            if (types[2] == typeof(Color) && types[3] == typeof(float) && types[4] == typeof(TextSettings))
                _createTextSpans = (delegate*<ref T, object, Color, float, TextSettings, void>)AtgInternals.Pointer(
                    createSpans);
            else if (types[2] == typeof(float) && types[3] == typeof(TextSettings) && types[4] == typeof(int))
                _createTextSpansHovered = (delegate*<ref T, object, float, TextSettings, int, void>)
                    AtgInternals.Pointer(createSpans);
            else
                return;

            _links = Activator.CreateInstance(types[1]);
        }

        public override bool UsesTextBuffer => default(T).UsesTextBuffer;
        public override bool FlipsY => default(T).HasFlipYAxis;

        public override NativeTextInfo GenerateText(object textLib, in AtgSettings settings, IntPtr generationInfo,
            ref bool wasCached)
        {
            var mirror = default(T);
            mirror.From(settings);
            return _generateText(textLib, mirror, generationInfo, ref wasCached);
        }

        public override void ProcessMeshInfos(object textLib, NativeTextInfo textInfo, in AtgSettings settings,
            ref List<List<List<int>>> indicesByMesh, ref List<bool> colorsByMesh, bool uvsAreGenerated)
        {
            var mirror = default(T);
            mirror.From(settings);
            if (_processMeshInfos != null)
            {
                _processMeshInfos(textLib, textInfo, mirror, ref indicesByMesh, uvsAreGenerated);
                return;
            }

            colorsByMesh ??= new List<bool>();
            colorsByMesh.Clear();
            _processMeshInfosWithColors(textLib, textInfo, mirror, ref indicesByMesh, ref colorsByMesh,
                uvsAreGenerated);
        }

        public override void ParseRichText(ref AtgSettings settings, TextSettings textSettings)
        {
            if (_createTextSpans == null && _createTextSpansHovered == null) return;

            var mirror = default(T);
            mirror.From(settings);
            if (_createTextSpans != null)
                _createTextSpans(ref mirror, _links, new Color(0.2f, 0.4f, 1f), 1f, textSettings);
            else
                _createTextSpansHovered(ref mirror, _links, 1f, textSettings, -1);

            mirror.CopyParsedText(ref settings);
        }
    }
}