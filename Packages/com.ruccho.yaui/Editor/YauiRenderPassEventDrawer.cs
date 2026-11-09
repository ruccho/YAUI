using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Yaui.Editor
{
    /// <summary>
    /// Draws an <see cref="int"/> render pass event (<see cref="YauiRenderPassEventAttribute"/>): a number field that
    /// takes any value, a menu of the named events, and a description such as "AfterRenderingPostProcessing - 25".
    /// In a 2D Renderer, it also tells where the renderer runs the pass.
    /// </summary>
    [CustomPropertyDrawer(typeof(YauiRenderPassEventAttribute))]
    internal sealed class YauiRenderPassEventDrawer : PropertyDrawer
    {
        private static (string Name, int Value)[] s_Events;

        private static (string Name, int Value)[] Events
        {
            get
            {
                if (s_Events != null) return s_Events;

                var events = new List<(string, int)>();
                var seen = new HashSet<int>();
                foreach (RenderPassEvent value in Enum.GetValues(typeof(RenderPassEvent)))
                {
                    // Obsolete aliases share their values; keep the first name of each value.
                    var name = Enum.GetName(typeof(RenderPassEvent), value);
                    var field = typeof(RenderPassEvent).GetField(name);
                    if (field != null && Attribute.IsDefined(field, typeof(ObsoleteAttribute))) continue;
                    if (seen.Add((int)value)) events.Add((name, (int)value));
                }

                events.Sort((a, b) => a.Item2.CompareTo(b.Item2));
                s_Events = events.ToArray();
                return s_Events;
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 2f + EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.Integer)
            {
                EditorGUI.LabelField(position, label.text, "Use YauiRenderPassEvent with an int.");
                return;
            }

            var line = position;
            line.height = EditorGUIUtility.singleLineHeight;

            EditorGUI.BeginProperty(position, label, property);
            var field = EditorGUI.PrefixLabel(line, label);
            const float menuWidth = 22f;
            var valueRect = new Rect(field.x, field.y, field.width - menuWidth - 2f, field.height);
            var menuRect = new Rect(field.xMax - menuWidth, field.y, menuWidth, field.height);

            EditorGUI.BeginChangeCheck();
            var value = EditorGUI.DelayedIntField(valueRect, property.intValue);
            if (EditorGUI.EndChangeCheck()) property.intValue = value;

            if (GUI.Button(menuRect, GUIContent.none, EditorStyles.popup)) ShowMenu(property);

            line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            var hint = EditorGUI.IndentedRect(new Rect(field.x, line.y, field.width, line.height));
            EditorGUI.LabelField(hint, Describe(property.intValue, Is2D(property)), EditorStyles.miniLabel);
            EditorGUI.EndProperty();
        }

        private static void ShowMenu(SerializedProperty property)
        {
            var menu = new GenericMenu();
            var current = property.intValue;
            foreach (var (name, value) in Events)
            {
                var target = property.serializedObject;
                var path = property.propertyPath;
                menu.AddItem(new GUIContent($"{name} ({value})"), value == current, () =>
                {
                    target.Update();
                    target.FindProperty(path).intValue = value;
                    target.ApplyModifiedProperties();
                });
            }

            menu.ShowAsContext();
        }

        /// <summary>The value relative to the nearest named event at or below it.</summary>
        internal static string Describe(int value, bool is2D)
        {
            var events = Events;
            var nearest = events[0];
            foreach (var e in events)
                if (e.Value <= value)
                    nearest = e;

            var offset = value - nearest.Value;
            var text = offset == 0 ? nearest.Name :
                offset > 0 ? $"{nearest.Name} + {offset}" : $"{nearest.Name} - {-offset}";
            return is2D ? $"{text}, 2D Renderer: {Stage2D(value)}" : text;
        }

        /// <summary>Where the 2D Renderer runs a pass of this event (it groups the events of other passes).</summary>
        private static string Stage2D(int value)
        {
            if (value <= (int)RenderPassEvent.BeforeRenderingTransparents) return "before rendering";
            if (value <= (int)RenderPassEvent.BeforeRenderingPostProcessing) return "before post-processing";
            if (value <= (int)RenderPassEvent.AfterRenderingPostProcessing) return "after post-processing";
            return "after the final blit";
        }

        /// <summary>Whether the property belongs to a feature of a 2D Renderer (the main asset of its file).</summary>
        private static bool Is2D(SerializedProperty property)
        {
            var path = AssetDatabase.GetAssetPath(property.serializedObject.targetObject);
            if (string.IsNullOrEmpty(path)) return false;

            // By name: Renderer2DData is in the URP 2D assembly, which this assembly does not reference.
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            return asset != null && asset.GetType().Name == "Renderer2DData";
        }
    }
}
