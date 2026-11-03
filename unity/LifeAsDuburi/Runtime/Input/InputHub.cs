// Port of the Input class of src/ui/input.js on the Input System package
// (never the legacy UnityEngine.Input). Keys are reported as browser
// KeyboardEvent.code strings (KeyCodes.cs); gamepads as the W3C "standard"
// gamepad mapping:
//   buttons 0..16: south, east, west, north, L1, R1, L2, R2, select, start,
//                  L3, R3, d-pad up, down, left, right, home
//   axes 0..3:     left x, left y (DOWN positive), right x, right y (DOWN positive)
// Pads keep a stable index 0..3 while they are connected (like
// navigator.getGamepads()). Poll() once at the start of every frame, EndFrame()
// at its end.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.DualShock;

namespace Duburi.Game
{
    public sealed class PadState
    {
        public int index;
        public string id = "";
        public bool connected;
        public readonly float[] axes = new float[4];
        public readonly bool[] pressed = new bool[17];
        public readonly float[] values = new float[17];
    }

    public struct PadPress
    {
        public int pad;
        public int button;
        public PadPress(int pad, int button) { this.pad = pad; this.button = button; }
    }

    public sealed class InputRead
    {
        public float surge, sway, heave, yaw, roll, pitch;
        public readonly List<string> actions = new List<string>();

        internal void Clear()
        {
            surge = sway = heave = yaw = roll = pitch = 0f;
            actions.Clear();
        }

        internal float Get(string axis)
        {
            switch (axis)
            {
                case "surge": return surge;
                case "sway": return sway;
                case "heave": return heave;
                case "yaw": return yaw;
                case "roll": return roll;
                case "pitch": return pitch;
                default: return 0f;
            }
        }

        internal void Set(string axis, float v)
        {
            switch (axis)
            {
                case "surge": surge = v; break;
                case "sway": sway = v; break;
                case "heave": heave = v; break;
                case "yaw": yaw = v; break;
                case "roll": roll = v; break;
                case "pitch": pitch = v; break;
            }
        }
    }

    public sealed class InputHub : IDisposable
    {
        public const int MaxPads = 4;
        const int PadButtons = 17;

        public readonly HashSet<string> Down = new HashSet<string>();     // keys held now
        public readonly HashSet<string> Pressed = new HashSet<string>();  // keys pressed since EndFrame()
        public event Action<int> PadConnected;                            // pad index

        readonly PadState[] slots = new PadState[MaxPads];
        readonly Gamepad[] slotDevice = new Gamepad[MaxPads];
        readonly List<PadState> connectedPads = new List<PadState>(MaxPads);
        readonly List<int> connectQueue = new List<int>(MaxPads);
        readonly Dictionary<string, bool[]> padPrev = new Dictionary<string, bool[]>();
        readonly bool[][] navPrev = new bool[MaxPads][];
        readonly bool[] navNow = new bool[PadButtons];
        readonly List<PadPress> presses = new List<PadPress>(8);
        readonly Dictionary<string, InputRead> reads = new Dictionary<string, InputRead>();
        string firstPressed;
        bool disposed;

        public InputHub()
        {
            for (int i = 0; i < MaxPads; i++)
            {
                slots[i] = new PadState { index = i };
                navPrev[i] = new bool[PadButtons];
            }
            InputSystem.onDeviceChange += OnDeviceChange;
            Application.focusChanged += OnFocusChanged;
            var all = Gamepad.all;
            for (int i = 0; i < all.Count; i++) Attach(all[i]);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            InputSystem.onDeviceChange -= OnDeviceChange;
            Application.focusChanged -= OnFocusChanged;
        }

        // window blur: forget held keys
        void OnFocusChanged(bool focused)
        {
            if (!focused) Down.Clear();
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (!(device is Gamepad pad)) return;
            switch (change)
            {
                case InputDeviceChange.Added:
                case InputDeviceChange.Reconnected:
                case InputDeviceChange.Enabled:
                    Attach(pad);
                    break;
                case InputDeviceChange.Removed:
                case InputDeviceChange.Disconnected:
                case InputDeviceChange.Disabled:
                    Detach(pad);
                    break;
            }
        }

        // give a new pad the lowest free index, like the browser does
        void Attach(Gamepad pad)
        {
            if (pad == null || !pad.added || !pad.enabled) return;
            int free = -1;
            for (int i = 0; i < MaxPads; i++)
            {
                if (slotDevice[i] == pad) return;
                if (free < 0 && slotDevice[i] == null) free = i;
            }
            if (free < 0) return;
            slotDevice[free] = pad;
            var s = slots[free];
            s.connected = true;
            s.id = PadId(pad);
            Array.Clear(s.axes, 0, s.axes.Length);
            Array.Clear(s.pressed, 0, s.pressed.Length);
            Array.Clear(s.values, 0, s.values.Length);
            Array.Clear(navPrev[free], 0, PadButtons);
            padPrev.Remove(DeviceName(free));
            RebuildConnected();
            if (!connectQueue.Contains(free)) connectQueue.Add(free);
        }

        void Detach(Gamepad pad)
        {
            for (int i = 0; i < MaxPads; i++)
            {
                if (slotDevice[i] != pad) continue;
                slotDevice[i] = null;
                slots[i].connected = false;
                connectQueue.Remove(i);
                RebuildConnected();
            }
        }

        void RebuildConnected()
        {
            connectedPads.Clear();
            for (int i = 0; i < MaxPads; i++) if (slotDevice[i] != null) connectedPads.Add(slots[i]);
        }

        // An id that Controls.PadStyle can recognise: the layout ("DualShock4GamepadHID",
        // "DualSenseGamepadHID", "XInputControllerWindows", "WebGLGamepad"), the
        // manufacturer and the product (on WebGL the browser's Gamepad.id, which
        // carries the USB vendor id, 054c for Sony).
        static string PadId(Gamepad pad)
        {
            var d = pad.description;
            var id = pad.layout + " " + d.manufacturer + " " + d.product;
            if (pad is DualShockGamepad) id = "DualShock " + id;
            return id.Trim();
        }

        static string DeviceName(int index)
        {
            switch (index) { case 0: return "pad0"; case 1: return "pad1"; case 2: return "pad2"; default: return "pad3"; }
        }

        static int PadIndex(string device)
        {
            switch (device) { case "pad0": return 0; case "pad1": return 1; case "pad2": return 2; case "pad3": return 3; default: return -1; }
        }

        // once at the start of every frame
        public void Poll()
        {
            PollKeyboard();
            for (int i = 0; i < MaxPads; i++)
            {
                var dev = slotDevice[i];
                if (dev == null) continue;
                if (!dev.added) { Detach(dev); continue; }
                ReadPad(dev, slots[i]);
            }
            if (connectQueue.Count > 0)
            {
                for (int q = 0; q < connectQueue.Count; q++) PadConnected?.Invoke(connectQueue[q]);
                connectQueue.Clear();
            }
        }

        void PollKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null) { Down.Clear(); return; }
            int nKeys = kb.allKeys.Count;
            for (int i = 0; i < KeyCodes.Count; i++)
            {
                var key = KeyCodes.KeyAt(i);
                int k = (int)key - 1;
                if (k < 0 || k >= nKeys) continue;
                var kc = kb[key];
                if (kc == null) continue; // a key this keyboard layout does not have
                var code = KeyCodes.CodeAt(i);
                if (kc.isPressed || kc.wasPressedThisFrame)
                {
                    // a press shorter than a frame still counts (wasPressedThisFrame)
                    if (!Down.Contains(code) || kc.wasPressedThisFrame)
                    {
                        if (Pressed.Add(code) && firstPressed == null) firstPressed = code;
                    }
                    if (kc.isPressed) Down.Add(code); else Down.Remove(code);
                }
                else Down.Remove(code);
            }
        }

        static void ReadPad(Gamepad g, PadState s)
        {
            // raw stick values: the JS applies its own dead zone in Read()
            var l = g.leftStick.ReadUnprocessedValue();
            var r = g.rightStick.ReadUnprocessedValue();
            s.axes[0] = Clamp1(l.x);
            s.axes[1] = Clamp1(-l.y);
            s.axes[2] = Clamp1(r.x);
            s.axes[3] = Clamp1(-r.y);
            Btn(s, 0, g.buttonSouth);
            Btn(s, 1, g.buttonEast);
            Btn(s, 2, g.buttonWest);
            Btn(s, 3, g.buttonNorth);
            Btn(s, 4, g.leftShoulder);
            Btn(s, 5, g.rightShoulder);
            Btn(s, 6, g.leftTrigger);
            Btn(s, 7, g.rightTrigger);
            Btn(s, 8, g.selectButton);
            Btn(s, 9, g.startButton);
            Btn(s, 10, g.leftStickButton);
            Btn(s, 11, g.rightStickButton);
            Btn(s, 12, g.dpad.up);
            Btn(s, 13, g.dpad.down);
            Btn(s, 14, g.dpad.left);
            Btn(s, 15, g.dpad.right);
            s.pressed[16] = false; // the home button is not exposed by the Input System's Gamepad layouts
            s.values[16] = 0f;
        }

        static void Btn(PadState s, int i, ButtonControl b)
        {
            if (b == null) { s.pressed[i] = false; s.values[i] = 0f; return; }
            float v = b.ReadValue();
            s.values[i] = v < 0f ? 0f : v > 1f ? 1f : v;
            s.pressed[i] = b.isPressed;
        }

        static float Clamp1(float v) => v < -1f ? -1f : v > 1f ? 1f : float.IsNaN(v) ? 0f : v;

        public bool Consume(string code) => code != null && Pressed.Remove(code);

        public void EndFrame()
        {
            Pressed.Clear();
            firstPressed = null;
        }

        // forget cached bindings after the controls screen changed them
        public void ReloadBindings() => Controls.ClearCache();

        // a key code pressed this frame, for re-binding
        public string FirstKeyPressed()
        {
            if (firstPressed != null && Pressed.Contains(firstPressed)) return firstPressed;
            foreach (var c in Pressed) return c;
            return null;
        }

        // connected pads
        public IReadOnlyList<PadState> Pads() => connectedPads;

        // "pad0".."pad3" (null if not connected)
        public PadState Pad(string device)
        {
            int i = PadIndex(device);
            return i >= 0 && slotDevice[i] != null ? slots[i] : null;
        }

        static float Dz(float v) => Math.Abs(v) < 0.12f ? 0f : (v - Math.Sign(v) * 0.12f) / 0.88f;

        static bool B(PadState pad, int i) => i >= 0 && i < PadButtons && (pad.values[i] > 0.5f || pad.pressed[i]);

        // "surge+" -> "surge" (cached: no per-frame strings)
        static readonly Dictionary<string, string> AxisOf = new Dictionary<string, string>();

        static int Bi(Bindings b, string id) => b.buttons.TryGetValue(id, out var i) ? i : -1;

        // JS read(): axes + edge-triggered actions. The returned object is reused per device.
        public InputRead Read(string device)
        {
            if (!reads.TryGetValue(device, out var o)) { o = new InputRead(); reads[device] = o; }
            o.Clear();
            if (device == "kbA" || device == "kbB")
            {
                var km = Controls.Cached(device);
                foreach (var kv in km.axes)
                {
                    if (kv.Value == null || !Down.Contains(kv.Value)) continue;
                    var id = kv.Key;
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!AxisOf.TryGetValue(id, out var ax)) { ax = id.Substring(0, id.Length - 1); AxisOf[id] = ax; }
                    float s = id[id.Length - 1] == '+' ? 1f : -1f;
                    o.Set(ax, o.Get(ax) + s);
                }
                o.surge = Mathf.Clamp(o.surge, -1f, 1f); o.sway = Mathf.Clamp(o.sway, -1f, 1f);
                o.heave = Mathf.Clamp(o.heave, -1f, 1f); o.yaw = Mathf.Clamp(o.yaw, -1f, 1f);
                o.roll = Mathf.Clamp(o.roll, -1f, 1f); o.pitch = Mathf.Clamp(o.pitch, -1f, 1f);
                if (km.fine != null && Down.Contains(km.fine))
                {
                    o.surge *= 0.35f; o.sway *= 0.35f; o.heave *= 0.35f;
                    o.yaw *= 0.35f; o.roll *= 0.35f; o.pitch *= 0.35f;
                }
                foreach (var a in Controls.Actions)
                    if (km.actions.TryGetValue(a.id, out var code) && code != null && Pressed.Contains(code)) o.actions.Add(a.id);
                return o;
            }
            int pi = PadIndex(device);
            if (pi >= 0)
            {
                var pad = Pad(device);
                if (pad == null) return o;
                var bnd = Controls.Cached(device);
                var a = pad.axes;
                foreach (var kv in Controls.StickPresets[bnd.sticks].map)
                {
                    int i = kv.Value[0], s = kv.Value[1];
                    int inv = bnd.invertY && (i == 1 || i == 3) ? -1 : 1;
                    o.Set(kv.Key, s * inv * Dz(i >= 0 && i < a.Length ? a[i] : 0f));
                }
                o.roll = (B(pad, Bi(bnd, "rollRight")) ? 1 : 0) - (B(pad, Bi(bnd, "rollLeft")) ? 1 : 0);
                o.pitch = (B(pad, Bi(bnd, "pitchUp")) ? 1 : 0) - (B(pad, Bi(bnd, "pitchDown")) ? 1 : 0);
                // triggers also work for up / down
                if (bnd.triggersHeave)
                {
                    float lt = pad.values[6], rt = pad.values[7];
                    if (Math.Abs(o.heave) < 0.05f) o.heave = rt - lt;
                }
                if (!padPrev.TryGetValue(device, out var prev)) { prev = new bool[PadButtons]; padPrev[device] = prev; }
                foreach (var act in Controls.Actions)
                {
                    int i = Bi(bnd, act.id);
                    if (i >= 0 && i < PadButtons && pad.pressed[i] && !prev[i]) o.actions.Add(act.id);
                }
                Array.Copy(pad.pressed, prev, PadButtons);
            }
            return o;
        }

        // New gamepad button presses on every connected pad since the last call
        // (menus and the join lobby). The returned list is reused.
        public List<PadPress> PadPresses()
        {
            presses.Clear();
            for (int p = 0; p < connectedPads.Count; p++)
            {
                var pad = connectedPads[p];
                var prev = navPrev[pad.index];
                for (int i = 0; i < PadButtons; i++) navNow[i] = pad.pressed[i] || pad.values[i] > 0.5f;
                // left stick as a d-pad for menus
                float ax = pad.axes[0], ay = pad.axes[1];
                navNow[12] = navNow[12] || ay < -0.6f; navNow[13] = navNow[13] || ay > 0.6f;
                navNow[14] = navNow[14] || ax < -0.6f; navNow[15] = navNow[15] || ax > 0.6f;
                for (int i = 0; i < PadButtons; i++) if (navNow[i] && !prev[i]) presses.Add(new PadPress(pad.index, i));
                Array.Copy(navNow, prev, PadButtons);
            }
            return presses;
        }
    }
}
