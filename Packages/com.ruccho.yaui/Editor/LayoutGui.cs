using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace Yaui.Editor
{
    /// <summary>
    /// Editing of lengths as text: "auto", "120" (canvas units) or "50%".
    /// </summary>
    internal static class LayoutGui
    {
        public static string Format(Length length)
        {
            return length.unit switch
            {
                LengthUnit.Auto => "auto",
                LengthUnit.Percent => length.value.ToString("0.##", CultureInfo.InvariantCulture) + "%",
                _ => length.value.ToString("0.##", CultureInfo.InvariantCulture)
            };
        }

        public static bool TryParse(string text, out Length length)
        {
            text = text.Trim();
            length = Length.Auto;
            if (text.Length == 0 || string.Equals(text, "auto", System.StringComparison.OrdinalIgnoreCase)) return true;

            var percent = text.EndsWith("%");
            if (percent) text = text.Substring(0, text.Length - 1).Trim();

            if (text.EndsWith("px")) text = text.Substring(0, text.Length - 2).Trim();

            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return false;

            length = percent ? Length.Percent(value) : Length.Points(value);
            return true;
        }

        public static Length Read(SerializedProperty property)
        {
            return new Length(property.FindPropertyRelative(nameof(Length.value)).floatValue,
                (LengthUnit)property.FindPropertyRelative(nameof(Length.unit)).enumValueIndex);
        }

        public static void Write(SerializedProperty property, Length length)
        {
            property.FindPropertyRelative(nameof(Length.value)).floatValue = length.value;
            property.FindPropertyRelative(nameof(Length.unit)).enumValueIndex = (int)length.unit;
        }

        private static bool HasMixed(SerializedProperty property)
        {
            return property.FindPropertyRelative(nameof(Length.value)).hasMultipleDifferentValues ||
                   property.FindPropertyRelative(nameof(Length.unit)).hasMultipleDifferentValues;
        }

        /// <summary>A text field for a <see cref="Length"/>.</summary>
        public static void LengthField(Rect rect, SerializedProperty property, GUIContent label = null)
        {
            EditorGUI.showMixedValue = HasMixed(property);
            EditorGUI.BeginChangeCheck();
            var text = label != null
                ? EditorGUI.DelayedTextField(rect, label, Format(Read(property)))
                : EditorGUI.DelayedTextField(rect, Format(Read(property)));
            if (EditorGUI.EndChangeCheck() && TryParse(text, out var length)) Write(property, length);

            EditorGUI.showMixedValue = false;
        }

        public static void LengthField(SerializedProperty property, string label)
        {
            LengthField(EditorGUILayout.GetControlRect(), property, new GUIContent(label));
        }

        /// <summary>Buttons for some values of an enum; the others remain in the full inspector.</summary>
        public static void EnumButtons(string label, SerializedProperty property, int[] values, string[] names)
        {
            var rect = EditorGUILayout.GetControlRect();
            rect = EditorGUI.PrefixLabel(rect, new GUIContent(label));
            var current = property.hasMultipleDifferentValues ? -1 : System.Array.IndexOf(values, property.intValue);
            EditorGUI.BeginChangeCheck();
            var selected = GUI.Toolbar(rect, current, names, EditorStyles.miniButton);
            if (EditorGUI.EndChangeCheck() && selected >= 0) property.intValue = values[selected];
        }
    }

    [CustomPropertyDrawer(typeof(Length))]
    internal class LengthDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            LayoutGui.LengthField(position, property, label);
            EditorGUI.EndProperty();
        }
    }
}