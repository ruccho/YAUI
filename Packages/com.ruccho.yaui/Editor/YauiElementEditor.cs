using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Yaui.Core;

namespace Yaui.Editor
{
    /// <summary>
    /// The inspector of elements: the common layout properties with dedicated controls (flex buttons, a box model
    /// for margin, border and padding, sizes as text), and Scene view handles for the size of any element and the
    /// position of absolutely positioned ones. The Transform tools are hidden while an element is selected (the
    /// Transform is ignored except for the hierarchy).
    /// </summary>
    [CustomEditor(typeof(YauiElement), true)]
    [CanEditMultipleObjects]
    internal class YauiElementEditor : UnityEditor.Editor
    {
        private static readonly Color OutlineColor = new(0.3f, 0.7f, 1f, 1f);
        private static readonly Color MarginColor = new(0.98f, 0.8f, 0.6f, 0.35f);
        private static readonly Color BorderColor = new(0.99f, 0.87f, 0.6f, 0.5f);
        private static readonly Color PaddingColor = new(0.76f, 0.87f, 0.6f, 0.5f);
        private static readonly Color ContentColor = new(0.55f, 0.72f, 0.85f, 0.5f);
        private static readonly Vector3[] Corners = new Vector3[4];

        private static bool _showAllLayout;

        private bool _toolsWereHidden;

        private SerializedProperty _layout;
        private SerializedProperty _box;

        private void OnEnable()
        {
            _toolsWereHidden = Tools.hidden;
            Tools.hidden = true;
            _layout = serializedObject.FindProperty("layout");
            _box = serializedObject.FindProperty("box");
        }

        private void OnDisable()
        {
            Tools.hidden = _toolsWereHidden;
        }

        private SerializedProperty Layout(string name)
        {
            return _layout.FindPropertyRelative(name);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawLayout();
            EditorGUILayout.Space();
            var iterator = serializedObject.GetIterator();
            for (var enter = true; iterator.NextVisible(enter); enter = false)
                if (iterator.name is not ("m_Script" or "layout") && !DrawProperty(iterator))
                    EditorGUILayout.PropertyField(iterator, true);

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Draws a property of a derived element in its own way, or returns false for the default field.</summary>
        protected virtual bool DrawProperty(SerializedProperty property)
        {
            return false;
        }

        private void DrawLayout()
        {
            EditorGUILayout.LabelField("Layout", EditorStyles.boldLabel);
            LayoutGui.EnumButtons("Position", Layout(nameof(LayoutStyle.position)),
                new[] { (int)PositionType.Relative, (int)PositionType.Absolute }, new[] { "Relative", "Absolute" });
            if (Layout(nameof(LayoutStyle.position)).intValue == (int)PositionType.Absolute ||
                Layout(nameof(LayoutStyle.position)).hasMultipleDifferentValues)
                DrawEdges("Inset", Layout(nameof(LayoutStyle.inset)));

            EditorGUILayout.Space(2f);
            LayoutGui.EnumButtons("Direction", Layout(nameof(LayoutStyle.direction)),
                new[]
                {
                    (int)FlexDirection.Row, (int)FlexDirection.Column, (int)FlexDirection.RowReverse,
                    (int)FlexDirection.ColumnReverse
                },
                new[] { "Row →", "Column ↓", "Row ←", "Column ↑" });
            LayoutGui.EnumButtons("Wrap", Layout(nameof(LayoutStyle.wrap)),
                new[] { (int)FlexWrap.NoWrap, (int)FlexWrap.Wrap, (int)FlexWrap.WrapReverse },
                new[] { "No Wrap", "Wrap", "Reverse" });
            LayoutGui.EnumButtons("Justify", Layout(nameof(LayoutStyle.justifyContent)),
                new[]
                {
                    (int)FlexJustify.FlexStart, (int)FlexJustify.Center, (int)FlexJustify.FlexEnd,
                    (int)FlexJustify.SpaceBetween, (int)FlexJustify.SpaceAround, (int)FlexJustify.SpaceEvenly
                },
                new[] { "Start", "Center", "End", "Between", "Around", "Evenly" });
            LayoutGui.EnumButtons("Align Items", Layout(nameof(LayoutStyle.alignItems)),
                new[]
                {
                    (int)FlexAlign.FlexStart, (int)FlexAlign.Center, (int)FlexAlign.FlexEnd, (int)FlexAlign.Stretch,
                    (int)FlexAlign.Baseline
                },
                new[] { "Start", "Center", "End", "Stretch", "Baseline" });
            LayoutGui.EnumButtons("Align Self", Layout(nameof(LayoutStyle.alignSelf)),
                new[]
                {
                    (int)FlexAlign.Auto, (int)FlexAlign.FlexStart, (int)FlexAlign.Center, (int)FlexAlign.FlexEnd,
                    (int)FlexAlign.Stretch
                },
                new[] { "Auto", "Start", "Center", "End", "Stretch" });

            EditorGUILayout.Space(2f);
            SizeRow("Size", Layout(nameof(LayoutStyle.width)), Layout(nameof(LayoutStyle.height)));
            SizeRow("Min", Layout(nameof(LayoutStyle.minWidth)), Layout(nameof(LayoutStyle.minHeight)));
            SizeRow("Max", Layout(nameof(LayoutStyle.maxWidth)), Layout(nameof(LayoutStyle.maxHeight)));
            FlexRow();
            EditorGUILayout.PropertyField(Layout(nameof(LayoutStyle.gap)));

            EditorGUILayout.Space(2f);
            DrawBoxModel();

            _showAllLayout = EditorGUILayout.Foldout(_showAllLayout, "All Layout Properties", true);
            if (_showAllLayout)
            {
                EditorGUI.indentLevel++;
                var iterator = _layout.Copy();
                var end = _layout.GetEndProperty();
                iterator.NextVisible(true);
                while (!SerializedProperty.EqualContents(iterator, end))
                {
                    EditorGUILayout.PropertyField(iterator, true);
                    if (!iterator.NextVisible(false)) break;
                }

                EditorGUI.indentLevel--;
            }
        }

        private static void SizeRow(string label, SerializedProperty width, SerializedProperty height)
        {
            var rect = EditorGUILayout.GetControlRect();
            rect = EditorGUI.PrefixLabel(rect, new GUIContent(label, "auto, 120 (canvas units) or 50%"));
            var half = (rect.width - 4f) * 0.5f;
            var labelWidth = EditorGUIUtility.labelWidth;
            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            EditorGUIUtility.labelWidth = 14f;
            LayoutGui.LengthField(new Rect(rect.x, rect.y, half, rect.height), width, new GUIContent("W"));
            LayoutGui.LengthField(new Rect(rect.x + half + 4f, rect.y, half, rect.height), height, new GUIContent("H"));
            EditorGUIUtility.labelWidth = labelWidth;
            EditorGUI.indentLevel = indent;
        }

        private void FlexRow()
        {
            var rect = EditorGUILayout.GetControlRect();
            rect = EditorGUI.PrefixLabel(rect, new GUIContent("Grow / Shrink / Basis"));
            var third = (rect.width - 8f) / 3f;
            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            EditorGUI.PropertyField(new Rect(rect.x, rect.y, third, rect.height), Layout(nameof(LayoutStyle.grow)),
                GUIContent.none);
            EditorGUI.PropertyField(new Rect(rect.x + third + 4f, rect.y, third, rect.height),
                Layout(nameof(LayoutStyle.shrink)), GUIContent.none);
            LayoutGui.LengthField(new Rect(rect.x + (third + 4f) * 2f, rect.y, third, rect.height),
                Layout(nameof(LayoutStyle.basis)));
            EditorGUI.indentLevel = indent;
        }

        private static void DrawEdges(string label, SerializedProperty edges)
        {
            var rect = EditorGUILayout.GetControlRect();
            rect = EditorGUI.PrefixLabel(rect, new GUIContent(label, "Left, top, right, bottom"));
            var quarter = (rect.width - 12f) / 4f;
            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            string[] names = { nameof(Edges.left), nameof(Edges.top), nameof(Edges.right), nameof(Edges.bottom) };
            for (var i = 0; i < 4; i++)
                LayoutGui.LengthField(new Rect(rect.x + (quarter + 4f) * i, rect.y, quarter, rect.height),
                    edges.FindPropertyRelative(names[i]));

            EditorGUI.indentLevel = indent;
        }

        /// <summary>Margin, border and padding as nested boxes, like the box model of browser developer tools.</summary>
        private void DrawBoxModel()
        {
            const float field = 44f;
            const float row = 18f;
            var rect = GUILayoutUtility.GetRect(0f, row * 7f + 8f, GUILayout.ExpandWidth(true));
            rect = new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 8f);
            var margin = Layout(nameof(LayoutStyle.margin));
            var padding = Layout(nameof(LayoutStyle.padding));
            var border = _box.FindPropertyRelative(nameof(BoxStyle.borderWidth));

            EditorGUI.DrawRect(rect, MarginColor);
            var borderRect = Inset(rect, field, row);
            EditorGUI.DrawRect(borderRect, BorderColor);
            var paddingRect = Inset(borderRect, field, row);
            EditorGUI.DrawRect(paddingRect, PaddingColor);
            var contentRect = Inset(paddingRect, field, row);
            EditorGUI.DrawRect(contentRect, ContentColor);

            var small = EditorStyles.miniLabel;
            GUI.Label(new Rect(rect.x + 2f, rect.y, 60f, row), "margin", small);
            GUI.Label(new Rect(borderRect.x + 2f, borderRect.y, 60f, row), "border", small);
            GUI.Label(new Rect(paddingRect.x + 2f, paddingRect.y, 60f, row), "padding", small);

            EdgeFields(rect, borderRect, margin, field, row);
            EdgeFields(paddingRect, contentRect, padding, field, row);

            // The border is one width for all sides.
            EditorGUI.showMixedValue = border.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            var width = EditorGUI.DelayedFloatField(
                new Rect(borderRect.center.x - field * 0.5f, borderRect.y + 1f, field, row - 2f), border.floatValue);
            if (EditorGUI.EndChangeCheck()) border.floatValue = Mathf.Max(0f, width);

            EditorGUI.showMixedValue = false;
        }

        private static Rect Inset(Rect rect, float x, float y)
        {
            return new Rect(rect.x + x, rect.y + y, rect.width - x * 2f, rect.height - y * 2f);
        }

        /// <summary>Fields for the four edges in the band between <paramref name="outer"/> and <paramref name="inner"/>.</summary>
        private static void EdgeFields(Rect outer, Rect inner, SerializedProperty edges, float field, float row)
        {
            var w = field - 4f;
            var h = row - 2f;
            LayoutGui.LengthField(new Rect(outer.center.x - w * 0.5f, outer.y + 1f, w, h),
                edges.FindPropertyRelative(nameof(Edges.top)));
            LayoutGui.LengthField(new Rect(outer.center.x - w * 0.5f, inner.yMax + 1f, w, h),
                edges.FindPropertyRelative(nameof(Edges.bottom)));
            LayoutGui.LengthField(new Rect(outer.x + 2f, outer.center.y - h * 0.5f, w, h),
                edges.FindPropertyRelative(nameof(Edges.left)));
            LayoutGui.LengthField(new Rect(inner.xMax + 2f, outer.center.y - h * 0.5f, w, h),
                edges.FindPropertyRelative(nameof(Edges.right)));
        }

        private void OnSceneGUI()
        {
            var element = (YauiElement)target;
            if (element.NodeSlot <= 0 || !YauiSystem.IsInitialized) return;

            var panel = element.GetComponentInParent<YauiPanel>();
            if (panel == null) return;

            // The node's world transform in canvas space, from the last propagation, then to the Scene view.
            var node = YauiSystem.Nodes.Gpu.Read(element.NodeSlot);
            var size = (float2)element.LayoutRect.size;
            var linear = new float2x2(node.Matrix.x, node.Matrix.y, node.Matrix.z, node.Matrix.w);
            var translation = node.Translation;
            var toWorld = panel.SceneViewCanvasToWorld;
            var toCanvas = toWorld.inverse;

            Vector3 World(float2 local)
            {
                var canvas = math.mul(linear, local) + translation;
                return toWorld.MultiplyPoint3x4(new Vector3(canvas.x, canvas.y, 0f));
            }

            float2 Local(Vector3 world)
            {
                var canvas = toCanvas.MultiplyPoint3x4(world);
                return math.mul(math.inverse(linear), new float2(canvas.x, canvas.y) - translation);
            }

            Corners[0] = World(0f);
            Corners[1] = World(new float2(size.x, 0f));
            Corners[2] = World(size);
            Corners[3] = World(new float2(0f, size.y));
            Handles.DrawSolidRectangleWithOutline(Corners, new Color(0.3f, 0.7f, 1f, 0.05f), OutlineColor);

            // Size: the right edge, the bottom edge and the corner.
            var handleSize = HandleUtility.GetHandleSize(Corners[2]) * 0.06f;
            Handles.color = OutlineColor;
            var resized = size;
            resized = SizeHandle(World(new float2(size.x, size.y * 0.5f)), handleSize, Local, resized, true, false);
            resized = SizeHandle(World(new float2(size.x * 0.5f, size.y)), handleSize, Local, resized, false, true);
            resized = SizeHandle(Corners[2], handleSize, Local, resized, true, true);
            if (math.any(resized != size))
            {
                // The inspector's serializedObject must not be used here; the handles edit the target through their own.
                using var handleObject = new SerializedObject(element);
                var handleLayout = handleObject.FindProperty("layout");
                if (resized.x != size.x)
                    LayoutGui.Write(handleLayout.FindPropertyRelative(nameof(LayoutStyle.width)),
                        Length.Points(Mathf.Round(math.max(resized.x, 0f))));

                if (resized.y != size.y)
                    LayoutGui.Write(handleLayout.FindPropertyRelative(nameof(LayoutStyle.height)),
                        Length.Points(Mathf.Round(math.max(resized.y, 0f))));

                handleObject.ApplyModifiedProperties();
            }

            // Position: absolutely positioned elements move by their insets.
            if (element.Layout.position != PositionType.Absolute) return;

            var center = World(size * 0.5f);
            EditorGUI.BeginChangeCheck();
            var moved = Handles.FreeMoveHandle(center, handleSize * 1.5f, Vector3.zero, Handles.RectangleHandleCap);
            if (!EditorGUI.EndChangeCheck()) return;

            var from = toCanvas.MultiplyPoint3x4(center);
            var to = toCanvas.MultiplyPoint3x4(moved);
            var delta = new float2(Mathf.Round(to.x - from.x), Mathf.Round(to.y - from.y));
            using var insetObject = new SerializedObject(element);
            var inset = insetObject.FindProperty("layout").FindPropertyRelative(nameof(LayoutStyle.inset));
            MoveInset(inset.FindPropertyRelative(nameof(Edges.left)), inset.FindPropertyRelative(nameof(Edges.right)),
                delta.x);
            MoveInset(inset.FindPropertyRelative(nameof(Edges.top)), inset.FindPropertyRelative(nameof(Edges.bottom)),
                delta.y);
            insetObject.ApplyModifiedProperties();
        }

        private delegate float2 ToLocal(Vector3 world);

        private static float2 SizeHandle(Vector3 position, float handleSize, ToLocal toLocal, float2 size, bool x,
            bool y)
        {
            EditorGUI.BeginChangeCheck();
            var moved = Handles.FreeMoveHandle(position, handleSize, Vector3.zero, Handles.DotHandleCap);
            if (!EditorGUI.EndChangeCheck()) return size;

            var local = toLocal(moved);
            return new float2(x ? local.x : size.x, y ? local.y : size.y);
        }

        /// <summary>Moves by the start inset if it is set in canvas units, otherwise by the end inset.</summary>
        private static void MoveInset(SerializedProperty start, SerializedProperty end, float delta)
        {
            if (delta == 0f) return;

            var s = LayoutGui.Read(start);
            var e = LayoutGui.Read(end);
            if (s.unit == LengthUnit.Point || e.unit != LengthUnit.Point)
                LayoutGui.Write(start, Length.Points((s.unit == LengthUnit.Point ? s.value : 0f) + delta));
            else
                LayoutGui.Write(end, Length.Points(e.value - delta));
        }
    }
}