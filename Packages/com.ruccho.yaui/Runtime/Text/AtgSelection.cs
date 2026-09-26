using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace Yaui.Text
{
    /// <summary>
    /// ATG's caret and selection queries (the internal <c>TextSelectionService</c>, used by UI Toolkit's text
    /// fields) on the last generation of a text generation info. Positions are in the space of the generation:
    /// origin at the top-left, Y down, in canvas units. Resolved apart from <see cref="AtgInternals"/>, so that
    /// texts still work if only these do not match.
    /// </summary>
    internal static class AtgSelection
    {
        public delegate void WordBoundsFunc(IntPtr info, int index, out int start, out int end);

        private static bool _initialized;

        public static bool Available { get; private set; }

        /// <summary>The caret before a character: x, and the bottom of its line.</summary>
        public static Func<IntPtr, int, Vector2> CursorPosition;

        public static Func<IntPtr, int, float> CharacterHeight;
        public static Func<IntPtr, int, int, Rect[]> HighlightRectangles;
        public static Func<IntPtr, Vector2, int> IndexFromPosition;
        public static Func<IntPtr, int, int> PreviousCodePoint;
        public static Func<IntPtr, int, int> NextCodePoint;
        public static Func<IntPtr, int, int> StartOfNextWord;
        public static Func<IntPtr, int, int> EndOfPreviousWord;
        public static Func<IntPtr, int, int> FirstIndexOnLine;
        public static Func<IntPtr, int, int> LastIndexOnLine;
        public static Func<IntPtr, int, int> LineNumber;
        public static WordBoundsFunc WordBounds;

        public static bool Initialize()
        {
            if (_initialized) return Available;

            _initialized = true;
            try
            {
                var type = typeof(FontAsset).Assembly.GetType("UnityEngine.TextCore.Text.TextSelectionService") ??
                           throw new MissingMemberException("TextSelectionService not found.");
                CursorPosition = Delegate<Func<IntPtr, int, Vector2>>(type, "GetCursorPositionFromLogicalIndex");
                CharacterHeight = Delegate<Func<IntPtr, int, float>>(type, "GetCharacterHeightFromIndex");
                HighlightRectangles = Delegate<Func<IntPtr, int, int, Rect[]>>(type, "GetHighlightRectangles");
                IndexFromPosition = Delegate<Func<IntPtr, Vector2, int>>(type, "GetCursorLogicalIndexFromPosition");
                PreviousCodePoint = Delegate<Func<IntPtr, int, int>>(type, "PreviousCodePointIndex");
                NextCodePoint = Delegate<Func<IntPtr, int, int>>(type, "NextCodePointIndex");
                StartOfNextWord = Delegate<Func<IntPtr, int, int>>(type, "GetStartOfNextWord");
                EndOfPreviousWord = Delegate<Func<IntPtr, int, int>>(type, "GetEndOfPreviousWord");
                FirstIndexOnLine = Delegate<Func<IntPtr, int, int>>(type, "GetFirstCharacterIndexOnLine");
                LastIndexOnLine = Delegate<Func<IntPtr, int, int>>(type, "GetLastCharacterIndexOnLine");
                LineNumber = Delegate<Func<IntPtr, int, int>>(type, "GetLineNumber");
                WordBounds = Delegate<WordBoundsFunc>(type, "ComputeWordBounds");
                Available = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[YAUI] Text selection is not available on this Unity version: {e.Message}");
                Available = false;
            }

            return Available;
        }

        private static T Delegate<T>(Type type, string name) where T : Delegate
        {
            var invoke = typeof(T).GetMethod("Invoke")!;
            var parameters = Array.ConvertAll(invoke.GetParameters(), p => p.ParameterType);
            var method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
                null, parameters, null) ?? throw new MissingMethodException(type.FullName, name);
            if (method.ReturnType != invoke.ReturnType)
                throw new MissingMethodException($"{type.FullName}.{name} returns {method.ReturnType}.");

            return (T)System.Delegate.CreateDelegate(typeof(T), method);
        }
    }
}