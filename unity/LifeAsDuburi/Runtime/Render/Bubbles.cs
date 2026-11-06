// src/render/bubbles.js: thruster / payload bubbles over a ring-buffer pool, simulated on the CPU
// (preallocated arrays, no per-frame allocations) and drawn by one world-space ParticleSystem as small
// silvery bubbles with a bright rim, a clear centre and a highlight (a soft round texture made at run
// time in place of the JS fragment shader).
//
// Positions / velocities passed to Emit are three.js world coordinates like the JS (metres, m/s;
// Y up, water surface at Y ~ 0), so main.js ports literally; EmitWorld takes Unity world coordinates.
// The ParticleSystem is NOT under the mirrored World root: particles are written in Unity world space
// (three (x, y, z) -> Unity (x, y, -z)).
//
//   var bubbles = new Bubbles(GraphicsQuality.Spec.bubbles);
//   bubbles.Emit(x, y, z, vx, vy, vz, count, size);   // size = bubble diameter, m
//   bubbles.Update(dt, time, waveY);                   // once per frame, waveY(x, z) = surface Y (three)
//   bubbles.ApplyForCamera(cam, viewportPx);           // per view (drawView: setViewportHeight)
//
// Point sizes: the JS point size is the bubble diameter in perspective, clamped to 2.5..96 px, and a
// bubble smaller than 2.5 px fades by its area. Here the billboards are the bubble diameter, the
// renderer clamps them to 2.5..96 px of the viewport, and the area fade uses the nearest view camera
// recorded by ApplyForCamera. Like the JS, the pool draws after the water surface for cameras under
// water and before it for cameras above (render queue per camera, RenderHooks), and takes the water's
// tint from the fog the camera sees.

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Duburi.Game
{
    public sealed class Bubbles
    {
        const float TAU_WASH = 0.25f;   // s, decay of the thruster wash velocity
        const float TAU_RISE = 0.2f;    // s, approach to the terminal rise speed
        const float SURFACE_GAP = 0.02f; // m, bubbles pop this far below the surface
        const float P_ATM = 10.3f;      // m of water per atmosphere (bubble growth with ascent)
        const float MIN_PX = 2.5f, MAX_PX = 96f;
        const int MAX_VIEWERS = 8;
        const int TEX = 64;
        /// texture rgb = (0.75 + 0.5 rim + 0.6 hl) / RGB_SCALE; the material colour carries RGB_SCALE
        const float RGB_SCALE = 1.85f;

        static readonly Func<float, float, float> Flat = (x, z) => 0f;

        public readonly int max;

        // per-bubble state (three.js space)
        readonly float[] px, py, pz;     // drawn position (with the wobble)
        readonly float[] size, alpha, seed;
        readonly float[] bse;            // position without the wobble (x, y, z)
        readonly float[] vel;            // wash velocity (decays)
        readonly float[] rise, vt, age, life, d0, z0;
        readonly float[] wob;            // amp1, freq1, amp2, freq2
        int head, alive, hi = -1;
        bool dirty;
        Func<float, float, float> waveFn = Flat;
        uint rs = 0x9e3779b9;

        // drawing
        GameObject go;
        ParticleSystem ps;
        ParticleSystemRenderer pr;
        Material mat;
        Texture2D tex;
        readonly ParticleSystem.Particle[] parts;
        int lastSet;
        readonly int baseQueue;
        float manualH;
        static readonly Color UColor = Colors.Rgb(0xe8f6ff).linear;
        Color env = Colors.Rgb(0x3aa6c9).linear;

        // view cameras of the last frame (three.js position, h / (2 tan(fov / 2)))
        readonly Camera[] viewCam = new Camera[MAX_VIEWERS];
        readonly Vector3[] viewPos = new Vector3[MAX_VIEWERS];
        readonly float[] viewScale = new float[MAX_VIEWERS];
        int viewers;

        /// createBubbles(scene, { max })
        public Bubbles(int max = 2000)
        {
            this.max = max = Mathf.Max(1, max);
            px = new float[max]; py = new float[max]; pz = new float[max];
            size = new float[max]; alpha = new float[max]; seed = new float[max];
            bse = new float[max * 3]; vel = new float[max * 3];
            rise = new float[max]; vt = new float[max]; age = new float[max]; life = new float[max];
            d0 = new float[max]; z0 = new float[max]; wob = new float[max * 4];
            parts = new ParticleSystem.Particle[max];

            tex = MakeTexture();
            mat = Mats.Create(new Mat3
            {
                kind = MatKind.Points, map = tex, color = Base(), transparent = true, opacity = 1, depthWrite = false, name = "bubbles",
            });
            baseQueue = mat.renderQueue;

            go = new GameObject("bubbles");
            ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = max;
            main.startLifetime = 1e6f;
            main.startSpeed = 0;
            main.gravityModifier = 0;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            pr = go.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.sortMode = ParticleSystemSortMode.None;
            pr.shadowCastingMode = ShadowCastingMode.Off;
            pr.receiveShadows = false;
            pr.sharedMaterial = mat;
            SetSizeClamp(800);
            ps.Play();

            RenderHooks.Ensure();
            RenderHooks.BeginCamera += OnBeginCamera;
        }

        /// the ParticleSystem (JS points)
        public ParticleSystem Points => ps;

        /// live bubbles (JS count)
        public int Count => alive;

        // small deterministic PRNG (mulberry32)
        float Rnd()
        {
            unchecked
            {
                rs += 0x6d2b79f5;
                uint t = (rs ^ (rs >> 15)) * (1u | rs);
                t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
                return (float)((t ^ (t >> 14)) / 4294967296.0);
            }
        }

        void Kill(int i)
        {
            life[i] = 0;
            alpha[i] = 0;
        }

        /// emit(x, y, z, vx, vy, vz, count, size): three.js position (m) and velocity (m/s); fractional
        /// counts are rounded stochastically (Emit(..., rate * dt) works); size = bubble diameter (m)
        public void Emit(float x, float y, float z, float vx = 0, float vy = 0, float vz = 0, float count = 1, float size = 0.01f)
        {
            int n = Mathf.FloorToInt(count);
            if (Rnd() < count - n) n++;
            if (n <= 0 || !(size > 0)) return;
            float jit = 0.006f + size;
            for (int k = 0; k < n; k++)
            {
                float x0 = x + (Rnd() - 0.5f) * 2 * jit, y0 = y + (Rnd() - 0.5f) * 2 * jit, z0p = z + (Rnd() - 0.5f) * 2 * jit;
                float wy = waveFn(x0, z0p);
                if (y0 >= wy - SURFACE_GAP) continue; // above / at the surface: nothing to show
                int i = head;
                head = (head + 1) % max; // ring: the oldest bubble is recycled when full
                if (life[i] <= 0) alive++;
                float s = Rnd();
                float d = size * (0.6f + 0.7f * Rnd());
                bse[i * 3] = x0; bse[i * 3 + 1] = y0; bse[i * 3 + 2] = z0p;
                float vj = 0.85f + 0.3f * Rnd();
                vel[i * 3] = vx * vj + (Rnd() - 0.5f) * 0.08f;
                vel[i * 3 + 1] = vy * vj + (Rnd() - 0.5f) * 0.08f;
                vel[i * 3 + 2] = vz * vj + (Rnd() - 0.5f) * 0.08f;
                vt[i] = (0.15f + 0.15f * Mathf.Min(1, d / 0.01f)) * (0.92f + 0.16f * Rnd()); // ~0.15-0.3 m/s
                rise[i] = 0;
                age[i] = 0;
                life[i] = 6.5f + 1.5f * Rnd(); // <= 8 s
                d0[i] = d;
                z0[i] = wy - y0;
                wob[i * 4] = 0.004f + 0.5f * d; // fast zig-zag (m)
                wob[i * 4 + 1] = 2 * Mathf.PI * (1.6f + 1.2f * s);
                wob[i * 4 + 2] = 0.012f + 0.01f * Rnd(); // slow drift (m)
                wob[i * 4 + 3] = 2 * Mathf.PI * (0.3f + 0.4f * Rnd());
                px[i] = x0; py[i] = y0; pz[i] = z0p;
                this.size[i] = d; alpha[i] = 0; seed[i] = s; // fades in during update
                if (i > hi) hi = i;
                dirty = true;
            }
        }

        /// Emit with a Unity world position / velocity
        public void EmitWorld(Vector3 unityPos, Vector3 unityVel, float count = 1, float size = 0.01f) =>
            Emit(unityPos.x, unityPos.y, -unityPos.z, unityVel.x, unityVel.y, -unityVel.z, count, size);

        /// update(dt, time, waveY): waveY(x, z) = surface height at three.js (x, z), null = flat at 0.
        /// time is accepted for API symmetry; the motion runs on each bubble's age.
        public void Update(float dt, float time = 0, Func<float, float, float> waveY = null)
        {
            waveFn = waveY ?? Flat;
            if (!(dt >= 0)) dt = 0;
            int nv = viewers;
            viewers = 0; // the views of the next frame register again
            if (alive == 0 && !dirty) { Upload(0); return; }
            float wash = Mathf.Exp(-dt / TAU_WASH), riseK = 1 - Mathf.Exp(-dt / TAU_RISE);
            int top = -1, n = 0;
            for (int i = 0, end = hi; i <= end; i++)
            {
                if (life[i] <= 0) continue;
                float a = (age[i] += dt);
                if (a >= life[i]) { Kill(i); dirty = true; continue; }
                int j = i * 3;
                float vx = (vel[j] *= wash), vy = (vel[j + 1] *= wash), vz = (vel[j + 2] *= wash);
                float r = (rise[i] += (vt[i] - rise[i]) * riseK);
                float bx = (bse[j] += vx * dt), by = (bse[j + 1] += (vy + r) * dt), bz = (bse[j + 2] += vz * dt);
                float wy = waveFn(bx, bz);
                if (by >= wy - SURFACE_GAP) { Kill(i); dirty = true; continue; } // popped at the surface
                // sideways wobble (zig-zag + slow spiral), ramping in as the wash dies down
                int w = i * 4;
                float ph = seed[i] * 6.2832f, ramp = Mathf.Min(1, a * 2);
                float ox = (wob[w] * Mathf.Sin(wob[w + 1] * a + ph) + wob[w + 2] * Mathf.Sin(wob[w + 3] * a + ph * 1.7f)) * ramp;
                float oz = (wob[w] * Mathf.Cos(wob[w + 1] * a * 0.83f + ph) + wob[w + 2] * Mathf.Cos(wob[w + 3] * a + ph * 2.3f)) * ramp;
                // grows as the pressure drops on the way up (and a little with age)
                float dep = Mathf.Max(0, wy - by);
                float grow = Mathf.Pow((P_ATM + z0[i]) / (P_ATM + dep), 1f / 3f) * (1 + 0.02f * a);
                px[i] = bx + ox; py[i] = by; pz[i] = bz + oz;
                size[i] = d0[i] * grow;
                alpha[i] = Mathf.Min(1, a / 0.08f) * Mathf.Min(1, (life[i] - a) / 0.6f);
                // write the live bubble into the particle buffer (Unity world: z flipped)
                float fade = 1;
                if (nv > 0)
                {
                    // a ring can't be resolved below ~2.5 px: keep a small dot and fade it by area
                    float best = 0;
                    for (int c = 0; c < nv; c++)
                    {
                        float dx = px[i] - viewPos[c].x, dy = py[i] - viewPos[c].y, dz = pz[i] - viewPos[c].z;
                        float dist = Mathf.Max(Mathf.Sqrt(dx * dx + dy * dy + dz * dz), 0.01f);
                        float p = size[i] * viewScale[c] / dist;
                        if (p > best) best = p;
                    }
                    fade = Mathf.Min(1, best * best / (MIN_PX * MIN_PX));
                }
                ref var q = ref parts[n];
                q.position = new Vector3(px[i], py[i], -pz[i]);
                q.velocity = Vector3.zero;
                q.startSize = size[i];
                q.startColor = new Color32(255, 255, 255, (byte)Mathf.Clamp(Mathf.RoundToInt(alpha[i] * fade * 255f), 0, 255));
                q.startLifetime = 1e6f;
                q.remainingLifetime = 1e6f;
                q.rotation = 0;
                q.randomSeed = (uint)i;
                top = i; n++;
            }
            alive = n;
            hi = top;
            if (dirty || n > 0 || lastSet > 0) Upload(n);
            dirty = false;
        }

        void Upload(int n)
        {
            if (ps == null || (n == 0 && lastSet == 0)) return;
            ps.SetParticles(parts, n);
            lastSet = n;
        }

        /// clear(): every bubble gone
        public void Clear()
        {
            for (int i = 0; i < max; i++) { life[i] = 0; alpha[i] = 0; }
            alive = 0; hi = -1; head = 0; dirty = false;
            Upload(0);
        }

        /// setViewportHeight(px): point-size reference in device pixels (rect.h * pixel ratio);
        /// 0 = the camera's own viewport at draw time (the default)
        public void SetViewportHeight(float viewportPx) => manualH = viewportPx > 0 ? viewportPx : 0;

        /// drawView, per camera: the camera draws the bubbles (draw order, tint, size clamp) and its
        /// distance fades the sub-pixel bubbles at the next Update. viewportPx = the view's height in
        /// pixels (0: SetViewportHeight or the camera's pixel height).
        public void ApplyForCamera(Camera cam, float viewportPx = 0)
        {
            if (cam == null || mat == null) return;
            RenderHooks.State(cam); // make sure the camera gets the per-camera switch
            float h = viewportPx > 0 ? viewportPx : manualH > 0 ? manualH : cam.pixelHeight;
            if (!(h > 0)) h = 800;
            int k = 0;
            while (k < viewers && viewCam[k] != cam) k++;
            if (k == viewers)
            {
                if (viewers == MAX_VIEWERS) return;
                viewers++;
            }
            viewCam[k] = cam;
            viewPos[k] = Space.FromUnity(cam.transform.position);
            viewScale[k] = h * 0.5f / Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * 0.5f);
        }

        // per camera: draw after the water surface (queue +2) under water, before it above; tint from
        // the fog this camera sees; 2.5..96 px points
        void OnBeginCamera(Camera cam, RenderHooks.CamState st)
        {
            if (mat == null || pr == null || cam == null || st == null) return;
            Vector3 c = Space.FromUnity(cam.transform.position);
            bool under = c.y < waveFn(c.x, c.z);
            Mats.SetQueue(mat, baseQueue + (under ? 3 : 1));
            if (st.scenery != null)
            {
                Color fog = st.scenery.FogColor(st.under).linear;
                if (fog != env) { env = fog; Mats.SetBaseColorLinear(mat, Base()); }
            }
            float h = manualH > 0 ? manualH : cam.pixelHeight;
            SetSizeClamp(h);
        }

        void SetSizeClamp(float h)
        {
            if (pr == null) return;
            if (!(h > 0)) h = 800;
            float mn = MIN_PX / h, mx = MAX_PX / h;
            if (pr.minParticleSize != mn) pr.minParticleSize = mn;
            if (pr.maxParticleSize != mx) pr.maxParticleSize = mx;
        }

        // vec3 base = mix(uColor, uEnv * 2.5, 0.35), times the texture's rgb scale
        Color Base()
        {
            Color b = Color.Lerp(UColor, env * 2.5f, 0.35f) * RGB_SCALE;
            b.a = 1;
            return b;
        }

        // the JS fragment shader as a texture: bright rim, clear centre, sky highlight (linear values)
        static Texture2D MakeTexture()
        {
            var t = new Texture2D(TEX, TEX, TextureFormat.RGBA32, true, true)
            {
                name = "bubble", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear,
            };
            var pix = new Color32[TEX * TEX];
            for (int j = 0; j < TEX; j++)
                for (int i = 0; i < TEX; i++)
                {
                    float x = (i + 0.5f) / TEX * 2 - 1, y = (j + 0.5f) / TEX * 2 - 1; // y up (row 0 = bottom)
                    float r = Mathf.Sqrt(x * x + y * y);
                    if (r > 1) { pix[j * TEX + i] = new Color32(255, 255, 255, 0); continue; }
                    float edge = 1 - SmoothStep(0.8f, 1.0f, r);
                    float rim = SmoothStep(0.35f, 0.85f, r) * edge;                     // bright rim, clear centre
                    float hx = x + 0.3f, hy = y - 0.35f;
                    float hl = 1 - SmoothStep(0.0f, 0.3f, Mathf.Sqrt(hx * hx + hy * hy)); // sky highlight
                    float rgb = Mathf.Min(1, (0.75f + 0.5f * rim + 0.6f * hl) / RGB_SCALE);
                    float a = Mathf.Min(1, 0.08f + 0.8f * rim * rim + 0.75f * hl);
                    byte v = (byte)Mathf.RoundToInt(rgb * 255);
                    pix[j * TEX + i] = new Color32(v, v, v, (byte)Mathf.RoundToInt(a * 255));
                }
            t.SetPixels32(pix);
            t.Apply(true, true);
            return t;
        }

        static float SmoothStep(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3 - 2 * t);
        }

        /// dispose(): removes the pool and its material / texture
        public void Dispose()
        {
            RenderHooks.BeginCamera -= OnBeginCamera;
            if (go != null) UnityEngine.Object.Destroy(go);
            if (mat != null) UnityEngine.Object.Destroy(mat);
            if (tex != null) UnityEngine.Object.Destroy(tex);
            go = null; ps = null; pr = null; mat = null; tex = null;
            alive = 0; hi = -1;
        }
    }
}
