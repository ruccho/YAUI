using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace Yaui.Showcase
{
    /// <summary>Particle systems for <see cref="YauiParticle"/>, and the input the showcases share.</summary>
    public static class Fx
    {
        /// <summary>A material of the Yaui/Particle shader with a soft dot.</summary>
        public static Material Material(Shader shader, bool additive)
        {
            var material = new Material(shader != null ? shader : Shader.Find("Yaui/Particle"))
            {
                mainTexture = IconFactory.CreateGlow()
            };
            material.SetFloat("_Additive", additive ? 1f : 0f);
            return material;
        }

        /// <summary>
        /// A stopped system that emits nothing by itself: YauiParticle draws systems that simulate in local space,
        /// in canvas units around the center of the element, with Y up.
        /// </summary>
        public static ParticleSystem NewSystem(GameObject go, Material material)
        {
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startSpeed = 0f;
            main.startLifetime = 0.5f;
            main.startSize = 10f;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            return system;
        }

        /// <summary>Particles appear and fade out over their lifetime.</summary>
        public static void Fade(ParticleSystem system)
        {
            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
        }

        /// <summary>Particles shrink to nothing over their lifetime.</summary>
        public static void Shrink(ParticleSystem system)
        {
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        }

        /// <summary>An EventSystem with the Input System's UI module, if the scene has none.</summary>
        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;

            var events = new GameObject("EventSystem", typeof(EventSystem));
            events.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        /// <summary>Whether the pointer is in use: the showcases stop their automatic demo for a while.</summary>
        public static bool PointerInUse()
        {
            var mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.isPressed || mouse.scroll.ReadValue() != Vector2.zero)) return true;

            var touch = Touchscreen.current;
            return touch != null && touch.primaryTouch.press.isPressed;
        }
    }
}
