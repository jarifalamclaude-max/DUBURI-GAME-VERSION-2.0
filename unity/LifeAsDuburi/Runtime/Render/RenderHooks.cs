// Per-camera render state. The JS changes the scene for each viewport just before it renders it
// (scenery.applyForCamera swaps fog / background / god rays, the bubbles pick their draw order and
// point size, post gets the view's under-water grade). Unity renders every enabled camera itself, after
// the frame's scripts, so the App's per-view calls (Scenery.ApplyForCamera, Bubbles.ApplyForCamera)
// only record what each camera needs in its CamState, and the global parts (RenderSettings fog,
// material tints and queues) are switched here when URP starts rendering that camera
// (RenderPipelineManager.beginCameraRendering). Cameras without a state (Scene view, thumbnails)
// are left alone.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Duburi.Game
{
    public static class RenderHooks
    {
        /// what the App's per-view calls decided for one camera
        public sealed class CamState
        {
            /// the camera is under the (wavy) water surface
            public bool under;
            /// metres below the surface (negative above it)
            public float depth;
            /// camera position in three.js space
            public Vector3 threePos;
            /// vertical field of view (degrees) and viewport height (pixels) when it was applied
            public float fov, pixelHeight;
            /// the scenery that applied it (null: none)
            public Scenery scenery;
            /// renderers this camera does not draw (main.js hides your own vehicle in the onboard views)
            public Renderer[] hide;
            internal Renderer[] hidden;
        }

        /// raised for every camera that has a state, right before URP renders it. Each handler runs on
        /// its own: one that throws is reported once (with its name: WebGL builds have no stack traces)
        /// and does not stop the others or flood the log every frame.
        public static event Action<Camera, CamState> BeginCamera
        {
            add { if (value != null && !handlers.Contains(value)) handlers.Add(value); }
            remove { handlers.Remove(value); }
        }
        static readonly List<Action<Camera, CamState>> handlers = new List<Action<Camera, CamState>>();
        static readonly HashSet<string> reported = new HashSet<string>();

        static readonly Dictionary<Camera, CamState> states = new Dictionary<Camera, CamState>();
        static readonly List<Camera> dead = new List<Camera>();
        static bool hooked;

        /// hooks RenderPipelineManager.beginCameraRendering once
        public static void Ensure()
        {
            if (hooked) return;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            hooked = true;
        }

        /// the state of a camera (created on first use)
        public static CamState State(Camera cam)
        {
            Ensure();
            if (!states.TryGetValue(cam, out var st))
            {
                Prune();
                st = new CamState();
                states[cam] = st;
            }
            return st;
        }

        public static bool TryGetState(Camera cam, out CamState st)
        {
            st = null;
            return cam != null && states.TryGetValue(cam, out st);
        }

        /// drop a camera (ViewCamera.Dispose)
        public static void Forget(Camera cam)
        {
            if (cam != null) states.Remove(cam);
            Prune();
        }

        /// a scenery was disposed: no camera uses it any more
        public static void Forget(Scenery s)
        {
            foreach (var st in states.Values) if (st.scenery == s) st.scenery = null;
        }

        static void Prune()
        {
            dead.Clear();
            foreach (var cam in states.Keys) if (cam == null) dead.Add(cam);
            for (int i = 0; i < dead.Count; i++) states.Remove(dead[i]);
            dead.Clear();
        }

        static void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null || !states.TryGetValue(cam, out var st)) return;
            if (st.hide != null)
            {
                for (int i = 0; i < st.hide.Length; i++) if (st.hide[i] != null) st.hide[i].forceRenderingOff = true;
                st.hidden = st.hide;
            }
            for (int i = 0; i < handlers.Count; i++)
            {
                var h = handlers[i];
                try { h(cam, st); }
                catch (Exception e)
                {
                    string key = e.GetType().Name + ": " + e.Message + " [in camera render: " + h.Method.DeclaringType?.Name + "." + h.Method.Name + "]";
                    if (reported.Add(key)) Debug.LogError("Life as Duburi: " + key + "\n" + e);
                }
            }
        }

        static void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null || !states.TryGetValue(cam, out var st) || st.hidden == null) return;
            for (int i = 0; i < st.hidden.Length; i++) if (st.hidden[i] != null) st.hidden[i].forceRenderingOff = false;
            st.hidden = null;
        }

        // Enter Play Mode without a domain reload keeps statics: start clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            if (hooked)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            }
            hooked = false;
            handlers.Clear();
            reported.Clear();
            states.Clear();
        }
    }
}
