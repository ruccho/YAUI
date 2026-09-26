using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.Text;

namespace Yaui.Text
{
    // Mirrors of internal TextCore structs (Unity 6000.7). Field names must match the originals: the layouts are
    // compared with the real types at startup. Enum fields are mirrored as their underlying integer types.

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeTextGenerationSettings
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
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeTextInfo
    {
        public IntPtr m_MeshInfosPtr;
        public int meshInfoCount;
        public int totalWidth;
        public int totalHeight;
        public bool isElided;
        public bool hasMultipleColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AtgMeshInfo
    {
        public IntPtr m_TextElementInfosPtr;
        public int m_TextElementCount;
        public EntityId textAssetId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TextCoreVertex
    {
        public Vector3 position;
        public Color32 color;
        public Vector2 uv0;
        public Vector2 uv2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeTextElementInfo
    {
        public int glyphID;
        public TextCoreVertex bottomLeft;
        public TextCoreVertex topLeft;
        public TextCoreVertex topRight;
        public TextCoreVertex bottomRight;
    }

    /// <summary>
    /// Internal TextCore APIs used by ATG, resolved with reflection once. Methods with only public types in their
    /// signatures become typed delegates. Methods that pass internal structs become managed function pointers
    /// (<see cref="RuntimeMethodHandle.GetFunctionPointer"/>) called with the mirror structs above.
    /// </summary>
    internal static unsafe class AtgInternals
    {
        private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static bool _initialized;

        public static bool Available { get; private set; }

        /// <summary>Why <see cref="Available"/> is false.</summary>
        public static string Error { get; private set; }

        // Internal objects, typed as object.
        public static object TextLib;

        // Function pointers. Instance methods take the instance as the first argument.
        public static delegate*<NativeTextGenerationSettings> GetDefaultSettings;
        public static delegate*<object, NativeTextGenerationSettings, IntPtr, ref bool, NativeTextInfo> GenerateText;
        public static delegate*<object, NativeTextInfo, ref Dictionary<EntityId, HashSet<uint>>, bool> HasMissingGlyphs;

        public static delegate*<object, NativeTextInfo, NativeTextGenerationSettings, ref List<List<List<int>>>, bool,
            void> ProcessMeshInfos;

        public static delegate*<object, Dictionary<EntityId, HashSet<uint>>, bool> ResolveFallbacks;
        public static delegate*<object, NativeTextInfo, void> TextInfoListAdd;
        public static delegate*<object, void> TextInfoListClear;

        /// <summary>A <c>List&lt;NativeTextInfo&gt;</c> for <see cref="ResolveFallbacks"/>.</summary>
        public static object CreateTextInfoList()
        {
            return Activator.CreateInstance(_textInfoListType);
        }

        private static Type _textInfoListType;

        // Typed delegates.
        public static Func<bool, IntPtr> CreateGenerationInfo;
        public static Action<IntPtr> DestroyGenerationInfo;
        public static Action<IntPtr, int, IntPtr> PreloadAssetsFromText;
        public static Action CreateHbFaceIfNeeded;
        public static Action UpdateFontAssetsInUpdateQueue;
        public static Func<FontAsset, IntPtr> GetNativeFontAsset;
        public static Func<FontAsset, List<uint>, bool, bool> TryAddGlyphs;
        public static Func<FontAsset, uint, Glyph> GetGlyphInCache;
        public static Func<TextSettings, IntPtr> GetNativeTextSettings;
        public static Func<TextSettings, Font, bool, FontAsset> GetCachedFontAsset;

        // Enum values of NativeTextGenerationSettings.
        public static int HorizontalLeft, HorizontalCenter, HorizontalRight, HorizontalJustified;
        public static int VerticalTop, VerticalMiddle, VerticalBottom;
        public static int OverflowClip, OverflowEllipsis, LanguageLtr, FontStyleNormal, FontWeightRegular;

        /// <summary>Main thread: resolves everything once. Returns <see cref="Available"/>.</summary>
        public static bool Initialize()
        {
            if (_initialized) return Available;

            _initialized = true;
            try
            {
                Resolve();
                Available = true;
            }
            catch (Exception e)
            {
                Error = e.Message;
                Available = false;
                Debug.LogWarning(
                    $"[YAUI] Text is unavailable: the internal TextCore APIs of this Unity version do not match ({e.Message})");
            }

            return Available;
        }

        private static void Resolve()
        {
            var asm = typeof(FontAsset).Assembly;
            var textLibType = GetType(asm, "UnityEngine.TextCore.Text.TextLib");
            var settingsType = GetType(asm, "UnityEngine.TextCore.NativeTextGenerationSettings");
            var textInfoType = GetType(asm, "UnityEngine.TextCore.Text.NativeTextInfo");
            var meshInfoType = GetType(asm, "UnityEngine.TextCore.Text.ATGMeshInfo");
            var elementType = GetType(asm, "UnityEngine.TextCore.Text.NativeTextElementInfo");
            var vertexType = GetType(asm, "UnityEngine.TextCore.Text.TextCoreVertex");
            var generationInfoType = GetType(asm, "UnityEngine.TextCore.Text.TextGenerationInfo");
            var fallbackType = GetType(asm, "UnityEngine.TextCore.Text.OSFontFallbackResolver");
            var registryType = GetType(asm, "UnityEngine.TextCore.NativeRichTextAssetRegistry");
            var generatorType = typeof(UnityEngine.TextCore.Generation.TextGenerator);

            var errors = new StringBuilder();
            ValidateLayout(typeof(NativeTextGenerationSettings), settingsType, errors);
            ValidateLayout(typeof(NativeTextInfo), textInfoType, errors);
            ValidateLayout(typeof(AtgMeshInfo), meshInfoType, errors);
            ValidateLayout(typeof(NativeTextElementInfo), elementType, errors);
            ValidateLayout(typeof(TextCoreVertex), vertexType, errors);
            if (errors.Length > 0) throw new InvalidOperationException("Layout mismatch:" + errors);

            var missingGlyphs = typeof(Dictionary<EntityId, HashSet<uint>>);
            var indicesByMesh = typeof(List<List<List<int>>>);

            GetDefaultSettings = (delegate*<NativeTextGenerationSettings>)Pointer(
                GetMethod(settingsType, "get_Default", AnyStatic, settingsType));
            GenerateText = (delegate*<object, NativeTextGenerationSettings, IntPtr, ref bool, NativeTextInfo>)Pointer(
                GetMethod(textLibType, "GenerateText", AnyInstance, textInfoType,
                    settingsType, typeof(IntPtr), typeof(bool).MakeByRefType()));
            HasMissingGlyphs =
                (delegate*<object, NativeTextInfo, ref Dictionary<EntityId, HashSet<uint>>, bool>)Pointer(
                    GetMethod(textLibType, "HasMissingGlyphs", AnyInstance, typeof(bool),
                        textInfoType, missingGlyphs.MakeByRefType()));
            ProcessMeshInfos =
                (delegate*<object, NativeTextInfo, NativeTextGenerationSettings, ref List<List<List<int>>>, bool, void>)
                Pointer(GetMethod(textLibType, "ProcessMeshInfos", AnyInstance, typeof(void),
                    textInfoType, settingsType, indicesByMesh.MakeByRefType(), typeof(bool)));

            _textInfoListType = typeof(List<>).MakeGenericType(textInfoType);
            ResolveFallbacks = (delegate*<object, Dictionary<EntityId, HashSet<uint>>, bool>)Pointer(
                GetMethod(fallbackType, "Resolve", AnyStatic, typeof(bool), _textInfoListType, missingGlyphs));
            TextInfoListAdd = (delegate*<object, NativeTextInfo, void>)Pointer(
                GetMethod(_textInfoListType, "Add", AnyInstance, typeof(void), textInfoType));
            TextInfoListClear = (delegate*<object, void>)Pointer(
                GetMethod(_textInfoListType, "Clear", AnyInstance, typeof(void)));

            var icuData = LoadIcuData();
            var getTextLib = GetMethod(generatorType, "GetTextLib", AnyStatic, textLibType);
            TextLib = getTextLib.Invoke(null, null);
            if (icuData != null)
                // A TextLib created earlier (by UI Toolkit) did not see it.
                textLibType.GetMethod("TryLoadICUData", AnyStatic, null, new[] { typeof(byte[]) }, null)
                    ?.Invoke(null, new object[] { icuData.bytes });

            CreateGenerationInfo = Delegate<Func<bool, IntPtr>>(generationInfoType, "Create", AnyStatic);
            DestroyGenerationInfo = Delegate<Action<IntPtr>>(generationInfoType, "Destroy", AnyStatic);
            PreloadAssetsFromText = Delegate<Action<IntPtr, int, IntPtr>>(registryType, "PreloadAssetsFromText",
                AnyStatic);
            CreateHbFaceIfNeeded = Delegate<Action>(typeof(FontAsset), "CreateHbFaceIfNeeded", AnyStatic);
            UpdateFontAssetsInUpdateQueue = Delegate<Action>(typeof(FontAsset), "UpdateFontAssetsInUpdateQueue",
                AnyStatic);
            GetNativeFontAsset = Delegate<Func<FontAsset, IntPtr>>(typeof(FontAsset), "get_nativeFontAsset",
                AnyInstance);
            TryAddGlyphs = Delegate<Func<FontAsset, List<uint>, bool, bool>>(typeof(FontAsset), "TryAddGlyphs",
                AnyInstance);
            GetGlyphInCache = Delegate<Func<FontAsset, uint, Glyph>>(typeof(FontAsset), "GetGlyphInCache",
                AnyInstance);
            GetNativeTextSettings = Delegate<Func<TextSettings, IntPtr>>(typeof(TextSettings),
                "get_nativeTextSettings", AnyInstance);
            GetCachedFontAsset = Delegate<Func<TextSettings, Font, bool, FontAsset>>(typeof(TextSettings),
                "GetCachedFontAsset", AnyInstance);

            int EnumValue(string field, string name)
            {
                return Convert.ToInt32(Enum.Parse(settingsType.GetField(field, AnyInstance)!.FieldType, name));
            }

            HorizontalLeft = EnumValue("horizontalAlignment", "Left");
            HorizontalCenter = EnumValue("horizontalAlignment", "Center");
            HorizontalRight = EnumValue("horizontalAlignment", "Right");
            HorizontalJustified = EnumValue("horizontalAlignment", "Justified");
            VerticalTop = EnumValue("verticalAlignment", "Top");
            VerticalMiddle = EnumValue("verticalAlignment", "Middle");
            VerticalBottom = EnumValue("verticalAlignment", "Bottom");
            OverflowClip = EnumValue("overflow", "Clip");
            OverflowEllipsis = EnumValue("overflow", "Ellipsis");
            LanguageLtr = EnumValue("languageDirection", "LTR");
            FontStyleNormal = EnumValue("fontStyle", "Normal");
            FontWeightRegular = EnumValue("fontWeight", "Regular");
        }

        /// <summary>
        /// Players: loads the ICU data (line breaking, word boundaries) for ATG. The editor finds it among its
        /// built-in resources; players only if a loaded asset references it, which <see cref="YauiTextData"/> in
        /// Resources does. Loaded before the first TextLib, which then finds it.
        /// </summary>
        private static UnityEngine.TextAsset LoadIcuData()
        {
            if (Application.isEditor) return null;

            var data = Resources.Load<YauiTextData>(YauiTextData.ResourcePath);
            if (data == null || data.IcuData == null)
            {
                Debug.LogWarning("[YAUI] The ICU data is missing: texts use minimal line breaking rules.");
                return null;
            }

            return data.IcuData;
        }

        private static Type GetType(Assembly asm, string name)
        {
            return asm.GetType(name) ?? throw new MissingMemberException($"Type {name} not found.");
        }

        private static MethodInfo GetMethod(Type type, string name, BindingFlags flags, Type returnType,
            params Type[] parameters)
        {
            var method = type.GetMethod(name, flags, null, parameters, null)
                         ?? throw new MissingMethodException(type.FullName, name);
            if (method.ReturnType != returnType)
                throw new MissingMethodException(
                    $"{type.FullName}.{name} returns {method.ReturnType}, expected {returnType}.");

            return method;
        }

        private static IntPtr Pointer(MethodInfo method)
        {
            return method.MethodHandle.GetFunctionPointer();
        }

        /// <summary>Binds a method whose signature matches <typeparamref name="T"/> (open instance for instance methods).</summary>
        private static T Delegate<T>(Type type, string name, BindingFlags flags) where T : Delegate
        {
            var invoke = typeof(T).GetMethod("Invoke")!;
            var parameters = Array.ConvertAll(invoke.GetParameters(), p => p.ParameterType);
            if ((flags & BindingFlags.Instance) != 0) parameters = parameters[1..];

            var method = type.GetMethod(name, flags, null, parameters, null)
                         ?? throw new MissingMethodException(type.FullName, name);
            return (T)System.Delegate.CreateDelegate(typeof(T), method);
        }

        /// <summary>Checks that <paramref name="mirror"/> has the same size and field offsets and sizes as <paramref name="real"/>.</summary>
        private static void ValidateLayout(Type mirror, Type real, StringBuilder errors)
        {
            var mirrorSize = UnsafeUtility.SizeOf(mirror);
            var realSize = UnsafeUtility.SizeOf(real);
            if (mirrorSize != realSize) errors.Append($" {real.Name} is {realSize} bytes (mirror {mirrorSize});");

            var realFields = real.GetFields(AnyInstance);
            var mirrorFields = mirror.GetFields(AnyInstance);
            if (realFields.Length != mirrorFields.Length)
                errors.Append($" {real.Name} has {realFields.Length} fields (mirror {mirrorFields.Length});");

            foreach (var mirrorField in mirrorFields)
            {
                var realField = real.GetField(mirrorField.Name, AnyInstance);
                if (realField == null)
                {
                    errors.Append($" {real.Name}.{mirrorField.Name} not found;");
                    continue;
                }

                var realOffset = UnsafeUtility.GetFieldOffset(realField);
                var mirrorOffset = UnsafeUtility.GetFieldOffset(mirrorField);
                var realFieldSize = UnsafeUtility.SizeOf(realField.FieldType);
                var mirrorFieldSize = UnsafeUtility.SizeOf(mirrorField.FieldType);
                if (realOffset != mirrorOffset || realFieldSize != mirrorFieldSize)
                    errors.Append(
                        $" {real.Name}.{mirrorField.Name} at {realOffset} ({realFieldSize} bytes), mirror at {mirrorOffset} ({mirrorFieldSize} bytes);");
            }
        }
    }
}