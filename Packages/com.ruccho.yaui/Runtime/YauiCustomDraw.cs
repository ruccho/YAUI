using System;
using System.Collections.Generic;
using UnityEngine;
using Yaui.Core;

namespace Yaui
{
    /// <summary>Where the meshes of a <see cref="YauiCustomDraw"/> are drawn among the element's primitives.</summary>
    public enum YauiCustomDrawPosition
    {
        /// <summary>Right after the element's own primitives, under its children. Hidden with the mask graphic.</summary>
        AfterSelf,

        /// <summary>After the element's children, on top of them (inside the element's mask).</summary>
        AfterChildren
    }

    /// <summary>
    /// Draws meshes of its own in the draw order of the element on the same GameObject, like a ParticleSystem
    /// (<see cref="YauiParticle"/>). Each mesh is a draw call of its own; the primitives around it split.
    /// </summary>
    /// <remarks>
    /// Materials whose shader has the "Overlay" and "World" passes of YAUI shaders (built on Shaders/Yaui.hlsl, with
    /// <c>YauiMeshVertex</c>) follow the node on the GPU: its transform, inherited opacity, tint, clips and masks.
    /// Other materials are drawn with their first forward pass and a model matrix computed on the CPU: they follow
    /// the transform only, and on world space panels a frame late. The Z of meshes is flattened onto the panel.
    /// One custom draw per element.
    /// </remarks>
    [ExecuteAlways]
    [RequireComponent(typeof(YauiElement))]
    public abstract class YauiCustomDraw : MonoBehaviour
    {
        [SerializeField] private YauiCustomDrawPosition position;

        private YauiElement element;

        /// <summary>The draws recorded at the last collection.</summary>
        [NonSerialized] internal readonly YauiDrawList Draws = new();

        public YauiCustomDrawPosition Position
        {
            get => position;
            set
            {
                position = value;
                Element.OnCustomDrawChanged();
            }
        }

        /// <summary>The element whose draw order the meshes join.</summary>
        protected YauiElement Element => element != null ? element : element = GetComponent<YauiElement>();

        internal YauiElement OwnerElement => Element;

        protected virtual void OnEnable()
        {
            Element.SetCustomDraw(this);
            YauiSystem.AddCustomDraw(this);
        }

        protected virtual void OnDisable()
        {
            YauiSystem.RemoveCustomDraw(this);
            Draws.Clear();
            if (element != null) element.ClearCustomDraw(this);
        }

        protected virtual void OnValidate()
        {
            if (isActiveAndEnabled) Element.OnCustomDrawChanged();
        }

        /// <summary>Main thread, at collection: records the draws of this frame.</summary>
        internal void Collect()
        {
            Draws.Clear();
            var owner = Element;
            if (owner == null || owner.PanelState == null || owner.CustomDraw != this) return;

            try
            {
                OnCollectDraws(Draws);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
        }

        /// <summary>
        /// Main thread, once a frame right before rendering, after the layout and the transforms: updates the meshes
        /// and records them. The layout of the element (<see cref="YauiElement.LayoutRect"/>) is current.
        /// </summary>
        protected abstract void OnCollectDraws(YauiDrawList draws);
    }

    /// <summary>The meshes a <see cref="YauiCustomDraw"/> draws in a frame, in order.</summary>
    public sealed class YauiDrawList
    {
        internal struct Item
        {
            public Mesh Mesh;
            public Material Material;
            public Matrix4x4 LocalMatrix;
            public int Submesh;
        }

        internal readonly List<Item> Items = new();

        public int Count => Items.Count;

        /// <summary>
        /// Draws a mesh. <paramref name="localMatrix"/> maps the mesh to the local space of the element's box (canvas
        /// units, origin at the top-left, Y down). The mesh is read when it renders, later in the frame.
        /// </summary>
        public void DrawMesh(Mesh mesh, Material material, Matrix4x4 localMatrix, int submesh = 0)
        {
            if (mesh == null || material == null) return;

            Items.Add(new Item { Mesh = mesh, Material = material, LocalMatrix = localMatrix, Submesh = submesh });
        }

        internal void Clear()
        {
            Items.Clear();
        }
    }
}