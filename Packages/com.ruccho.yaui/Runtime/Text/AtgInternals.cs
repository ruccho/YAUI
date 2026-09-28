using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.Text;
#if UNITY_6000_4_OR_NEWER
using TextAssetId = UnityEngine.EntityId;

#else
using TextAssetId = System.Int32;
#endif

namespace Yaui.Text
{
    // Mirrors of internal TextCore structs. Field names must match the originals: the layouts are compared with the
    // real types at startup. The generation settings, which change with every version, are in AtgSettings.cs.

    /// <summary>6000.6 and later also have <c>hasMultipleColors</c> in the padding after <see cref="isElided"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeTextInfo
    {
        public IntPtr m_MeshInfosPtr;
        public int meshInfoCount;
        public int totalWidth;
        public int totalHeight;
        public bool isElided;
    }

    /// <summary>The id of the text asset is an <c>int</c> before 6000.4.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AtgMeshInfo
    {
        public IntPtr m_TextElementInfosPtr;
        public int m_TextElementCount;
        public TextAssetId textAssetId;
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
    /// (<see cref="RuntimeMethodHandle.GetFunctionPointer"/>) called with the mirror structs above, or with the
    /// mirror of the generation settings of the running version through <see cref="Backend"/>.
    /// </summary>
    /// <remarks>
    /// Differences between the supported versions (6000.3 to 6000.7):
    /// before 6000.6 the text is a string and rich text is parsed in managed code into spans;
    /// before 6000.7 there is no <c>flipYAxis</c> (positions are Y down) and no OS font fallback resolver (OS fonts
    /// are fallbacks of the text settings instead);
    /// before 6000.4 the ids of the text assets are instance ids.
    /// </remarks>
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

        /// <summary>The methods that take the generation settings.</summary>
        public static AtgBackend Backend;

        // Function pointers. Instance methods take the instance as the first argument.
        public static delegate*<object, NativeTextInfo, ref Dictionary<TextAssetId, HashSet<uint>>, bool>
            HasMissingGlyphs;

        // 6000.7 and later (OSFontFallbackResolver), otherwise null.
        public static delegate*<object, Dictionary<TextAssetId, HashSet<uint>>, bool> ResolveFallbacks;
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
        public static Action CreateHbFaceIfNeeded;
        public static Action UpdateFontAssetsInUpdateQueue;
        public static Func<FontAsset, IntPtr> GetNativeFontAsset;
        public static Func<FontAsset, uint, Glyph> GetGlyphInCache;
        public static Func<TextSettings, IntPtr> GetNativeTextSettings;

        /// <summary>Adds glyphs to a dynamic font asset, without populating its font features where possible.</summary>
        public static Func<FontAsset, List<uint>, bool> TryAddGlyphs;

        public static Func<TextSettings, Font, FontAsset> GetCachedFontAsset;

        /// <summary>Loads the assets that the rich text tags of a text refer to.</summary>
        public static PreloadFunc PreloadAssets;

        public delegate void PreloadFunc(in AtgSettings settings, TextSettings textSettings);

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

        /// <summary>The text asset (font or sprite asset) of a mesh of a generation.</summary>
        public static UnityEngine.Object FindTextAsset(TextAssetId id)
        {
#if UNITY_6000_4_OR_NEWER
            return Resources.EntityIdToObject(id);
#else
            return Resources.InstanceIDToObject(id);
#endif
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

            var errors = new StringBuilder();
            ValidateLayout(typeof(NativeTextInfo), textInfoType, errors, true);
            ValidateLayout(typeof(AtgMeshInfo), meshInfoType, errors);
            ValidateLayout(typeof(NativeTextElementInfo), elementType, errors);
            ValidateLayout(typeof(TextCoreVertex), vertexType, errors);
            if (errors.Length > 0) throw new InvalidOperationException("Layout mismatch:" + errors);

            Backend = CreateBackend(textLibType, settingsType, textInfoType);

            var missingGlyphs = typeof(Dictionary<TextAssetId, HashSet<uint>>);
            HasMissingGlyphs =
                (delegate*<object, NativeTextInfo, ref Dictionary<TextAssetId, HashSet<uint>>, bool>)Pointer(
                    GetMethod(textLibType, "HasMissingGlyphs", AnyInstance, typeof(bool),
                        textInfoType, missingGlyphs.MakeByRefType()));

            _textInfoListType = typeof(List<>).MakeGenericType(textInfoType);
            var resolve = asm.GetType("UnityEngine.TextCore.Text.OSFontFallbackResolver")
                ?.GetMethod("Resolve", AnyStatic, null, new[] { _textInfoListType, missingGlyphs }, null);
            if (resolve != null && resolve.ReturnType == typeof(bool))
            {
                ResolveFallbacks = (delegate*<object, Dictionary<TextAssetId, HashSet<uint>>, bool>)Pointer(resolve);
                TextInfoListAdd = (delegate*<object, NativeTextInfo, void>)Pointer(
                    GetMethod(_textInfoListType, "Add", AnyInstance, typeof(void), textInfoType));
                TextInfoListClear = (delegate*<object, void>)Pointer(
                    GetMethod(_textInfoListType, "Clear", AnyInstance, typeof(void)));
            }

            TextLib = GetTextLib(textLibType);

            CreateGenerationInfo = Delegate<Func<bool, IntPtr>>(generationInfoType, "Create", AnyStatic);
            DestroyGenerationInfo = Delegate<Action<IntPtr>>(generationInfoType, "Destroy", AnyStatic);
            CreateHbFaceIfNeeded = Delegate<Action>(typeof(FontAsset), "CreateHbFaceIfNeeded", AnyStatic);
            UpdateFontAssetsInUpdateQueue = Delegate<Action>(typeof(FontAsset), "UpdateFontAssetsInUpdateQueue",
                AnyStatic);
            GetNativeFontAsset = Delegate<Func<FontAsset, IntPtr>>(typeof(FontAsset), "get_nativeFontAsset",
                AnyInstance);
            GetGlyphInCache = Delegate<Func<FontAsset, uint, Glyph>>(typeof(FontAsset), "GetGlyphInCache",
                AnyInstance);
            GetNativeTextSettings = Delegate<Func<TextSettings, IntPtr>>(typeof(TextSettings),
                "get_nativeTextSettings", AnyInstance);

            // 6000.6 and later can skip populating the font features.
            var tryAddGlyphs = TryDelegate<Func<FontAsset, List<uint>, bool, bool>>(typeof(FontAsset), "TryAddGlyphs",
                AnyInstance);
            if (tryAddGlyphs != null)
                TryAddGlyphs = (fontAsset, glyphs) => tryAddGlyphs(fontAsset, glyphs, false);
            else
                TryAddGlyphs = Delegate<Func<FontAsset, List<uint>, bool>>(typeof(FontAsset), "TryAddGlyphs",
                    AnyInstance);

            // 6000.7 added whether the font asset is for IMGUI.
            var getCachedFontAsset = TryDelegate<Func<TextSettings, Font, bool, FontAsset>>(typeof(TextSettings),
                "GetCachedFontAsset", AnyInstance);
            if (getCachedFontAsset != null)
                GetCachedFontAsset = (settings, font) => getCachedFontAsset(settings, font, false);
            else
                GetCachedFontAsset = Delegate<Func<TextSettings, Font, FontAsset>>(typeof(TextSettings),
                    "GetCachedFontAsset", AnyInstance);

            PreloadAssets = ResolvePreload(asm);

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

        /// <summary>The first mirror of the generation settings whose layout matches the running version.</summary>
        private static AtgBackend CreateBackend(Type textLibType, Type settingsType, Type textInfoType)
        {
            var errors = new StringBuilder();
            if (Matches(typeof(NativeSettings6000_7)))
                return new AtgBackend<NativeSettings6000_7>(textLibType, settingsType, textInfoType);
            if (Matches(typeof(NativeSettings6000_6)))
                return new AtgBackend<NativeSettings6000_6>(textLibType, settingsType, textInfoType);
            if (Matches(typeof(NativeSettings6000_5)))
                return new AtgBackend<NativeSettings6000_5>(textLibType, settingsType, textInfoType);
            if (Matches(typeof(NativeSettings6000_4)))
                return new AtgBackend<NativeSettings6000_4>(textLibType, settingsType, textInfoType);
            if (Matches(typeof(NativeSettings6000_3)))
                return new AtgBackend<NativeSettings6000_3>(textLibType, settingsType, textInfoType);

            throw new InvalidOperationException("No mirror matches NativeTextGenerationSettings:" + errors);

            bool Matches(Type mirror)
            {
                var mismatches = new StringBuilder();
                ValidateLayout(mirror, settingsType, mismatches);
                if (mismatches.Length == 0) return true;

                errors.Append($" [{mirror.Name}]").Append(mismatches);
                return false;
            }
        }

        /// <summary>
        /// The TextLib instance. 6000.7 shares the one of the public TextGenerator; earlier versions create one (the
        /// native library is shared, and the ICU data applies if it is the first).
        /// </summary>
        private static object GetTextLib(Type textLibType)
        {
            var icuData = LoadIcuData();
            var loadIcuData = textLibType.GetMethod("TryLoadICUData", AnyStatic, null, new[] { typeof(byte[]) }, null);
            var getTextLib = textLibType.Assembly.GetType("UnityEngine.TextCore.Generation.TextGenerator")
                ?.GetMethod("GetTextLib", AnyStatic, null, Type.EmptyTypes, null);

            object textLib;
            if (getTextLib != null && getTextLib.ReturnType == textLibType)
            {
                textLib = getTextLib.Invoke(null, null);
            }
            else
            {
                var bytes = icuData != null ? icuData.bytes : GetEditorIcuData(textLibType);
                textLib = Activator.CreateInstance(textLibType, AnyInstance, null,
                    new object[] { bytes ?? Array.Empty<byte>() }, null);
            }

            // A TextLib created earlier (by UI Toolkit) did not see it.
            if (icuData != null) loadIcuData?.Invoke(null, new object[] { icuData.bytes });

            return textLib;
        }

        /// <summary>The editor's ICU data, as UI Toolkit finds it.</summary>
        private static byte[] GetEditorIcuData(Type textLibType)
        {
            if (!Application.isEditor) return null;

            var getAsset = textLibType.GetField("GetICUAssetEditorDelegate", AnyStatic)?.GetValue(null) as
                Func<UnityEngine.TextAsset>;
            var asset = getAsset?.Invoke();
            if (asset == null)
                foreach (var candidate in Resources.FindObjectsOfTypeAll<UnityEngine.TextAsset>())
                    if (candidate.name == YauiTextData.IcuDataName)
                    {
                        asset = candidate;
                        break;
                    }

            return asset != null ? asset.bytes : null;
        }

        /// <summary>
        /// 6000.6 and later: <c>NativeRichTextAssetRegistry.PreloadAssetsFromText</c>. Earlier:
        /// <c>RichTextTagParser.Preload*FromTags</c>.
        /// </summary>
        private static PreloadFunc ResolvePreload(Assembly asm)
        {
            var preloadFromText = TryDelegate<Action<IntPtr, int, IntPtr>>(
                asm.GetType("UnityEngine.TextCore.NativeRichTextAssetRegistry"), "PreloadAssetsFromText", AnyStatic);
            if (preloadFromText != null)
                return (in AtgSettings s, TextSettings _) =>
                    preloadFromText(s.textBufferPtr, s.textBufferLength, s.textSettings);

            var parser = asm.GetType("UnityEngine.TextCore.RichTextTagParser");
            var fonts = TryDelegate<Action<string, TextSettings>>(parser, "PreloadFontAssetsFromTags", AnyStatic);
            var sprites = TryDelegate<Action<string, TextSettings>>(parser, "PreloadSpriteAssetsFromTags", AnyStatic);
            var gradients = TryDelegate<Action<string, TextSettings>>(parser, "PreloadGradientAssetsFromTags",
                AnyStatic);
            return (in AtgSettings s, TextSettings textSettings) =>
            {
                fonts?.Invoke(s.text, textSettings);
                sprites?.Invoke(s.text, textSettings);
                gradients?.Invoke(s.text, textSettings);
            };
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

        internal static MethodInfo GetMethod(Type type, string name, BindingFlags flags, Type returnType,
            params Type[] parameters)
        {
            var method = type.GetMethod(name, flags, null, parameters, null)
                         ?? throw new MissingMethodException(type.FullName, name);
            if (method.ReturnType != returnType)
                throw new MissingMethodException(
                    $"{type.FullName}.{name} returns {method.ReturnType}, expected {returnType}.");

            return method;
        }

        internal static IntPtr Pointer(MethodInfo method)
        {
            return method.MethodHandle.GetFunctionPointer();
        }

        /// <summary>Binds a method whose signature matches <typeparamref name="T"/> (open instance for instance methods).</summary>
        private static T Delegate<T>(Type type, string name, BindingFlags flags) where T : Delegate
        {
            return TryDelegate<T>(type, name, flags) ?? throw new MissingMethodException(type.FullName, name);
        }

        private static T TryDelegate<T>(Type type, string name, BindingFlags flags) where T : Delegate
        {
            if (type == null) return null;

            var invoke = typeof(T).GetMethod("Invoke")!;
            var parameters = Array.ConvertAll(invoke.GetParameters(), p => p.ParameterType);
            if ((flags & BindingFlags.Instance) != 0) parameters = parameters[1..];

            var method = type.GetMethod(name, flags, null, parameters, null);
            if (method == null || method.ReturnType != invoke.ReturnType) return null;

            return (T)System.Delegate.CreateDelegate(typeof(T), method);
        }

        /// <summary>
        /// Checks that <paramref name="mirror"/> has the same size and field offsets and sizes as
        /// <paramref name="real"/>. With <paramref name="allowUnmirrored"/>, the real type may have more fields (in
        /// padding: the sizes still match), which are then not read or written.
        /// </summary>
        private static void ValidateLayout(Type mirror, Type real, StringBuilder errors, bool allowUnmirrored = false)
        {
            var mirrorSize = UnsafeUtility.SizeOf(mirror);
            var realSize = UnsafeUtility.SizeOf(real);
            if (mirrorSize != realSize) errors.Append($" {real.Name} is {realSize} bytes (mirror {mirrorSize});");

            var realFields = real.GetFields(AnyInstance);
            var mirrorFields = mirror.GetFields(AnyInstance);
            if (!allowUnmirrored && realFields.Length != mirrorFields.Length)
                errors.Append($" {real.Name} has {realFields.Length} fields (mirror {mirrorFields.Length});");

            foreach (var mirrorField in mirrorFields)
            {
                var realField = real.GetField(mirrorField.Name, AnyInstance);
                if (realField == null)
                {
                    errors.Append($" {real.Name}.{mirrorField.Name} not found;");
                    continue;
                }

                // References (the text and the spans) are mirrored as references of any type.
                var mirrorIsReference = !mirrorField.FieldType.IsValueType;
                var realIsReference = !realField.FieldType.IsValueType;
                var realOffset = UnsafeUtility.GetFieldOffset(realField);
                var mirrorOffset = UnsafeUtility.GetFieldOffset(mirrorField);
                var realFieldSize = realIsReference ? IntPtr.Size : UnsafeUtility.SizeOf(realField.FieldType);
                var mirrorFieldSize = mirrorIsReference ? IntPtr.Size : UnsafeUtility.SizeOf(mirrorField.FieldType);
                if (realOffset != mirrorOffset || realFieldSize != mirrorFieldSize ||
                    realIsReference != mirrorIsReference)
                    errors.Append(
                        $" {real.Name}.{mirrorField.Name} at {realOffset} ({realFieldSize} bytes), mirror at {mirrorOffset} ({mirrorFieldSize} bytes);");
            }
        }
    }
}