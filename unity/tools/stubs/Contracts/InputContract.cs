// CONTRACT STUB (compile-check only, never shipped). The real code lives in
// LifeAsDuburi/Runtime/Input and must provide at least these members with these
// exact signatures; other modules may use only these members of Input.
// JS original: src/ui/input.js. Device ids: "kbA", "kbB", "pad0".."pad3", "cpu".
// Key codes are browser KeyboardEvent.code strings ("KeyW", "ArrowUp", "ShiftLeft").
// Pad buttons / axes use the W3C "standard" gamepad mapping indices (0..16, 0..3).
using System;
using System.Collections.Generic;

namespace Duburi.Game
{
    public sealed class DeviceDef { public string id; public string label; }
    public sealed class ActionDef { public string id; public string label; }

    public sealed class StickPreset
    {
        public string id, label, desc;
        public Dictionary<string, int[]> map = new Dictionary<string, int[]>(); // axis -> { padAxisIndex, sign }
    }

    // one device's bindings, the same fields as the JSON the browser version stores
    public sealed class Bindings
    {
        public bool isPad;
        public Dictionary<string, string> axes = new Dictionary<string, string>();    // keyboard: "surge+" -> "KeyW"
        public Dictionary<string, string> actions = new Dictionary<string, string>(); // keyboard: "marker" -> "KeyV"
        public string fine;                                                           // keyboard: "ShiftLeft"
        public string sticks = "ardusub";                                             // pad
        public bool invertY;                                                          // pad
        public bool triggersHeave = true;                                             // pad
        public Dictionary<string, int> buttons = new Dictionary<string, int>();       // pad: "marker" -> 0
        public Bindings Clone() => this;
    }

    public static class Controls
    {
        public static readonly DeviceDef[] Devices = new DeviceDef[0];
        public static readonly ActionDef[] Actions = new ActionDef[0];
        public static readonly ActionDef[] PadHolds = new ActionDef[0];
        public static readonly ActionDef[] KeyAxes = new ActionDef[0];
        public static readonly string[] StickPresetOrder = new string[0];
        public static readonly Dictionary<string, StickPreset> StickPresets = new Dictionary<string, StickPreset>();
        public static Bindings GetBindings(string device) => new Bindings();
        public static void SaveBindings(string device, Bindings b) { }
        public static void ResetBindings(string device) { }
        public static string PadStyle(PadState pad) => "ps"; // "ps" | "xbox"
        public static string ButtonName(int index, string style = "ps") => "";
        public static string KeyName(string code) => "";
        public static List<string[]> HelpRows(string device, string style = "ps") => new List<string[]>(); // { key, what }
    }

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
    }

    public sealed class InputHub
    {
        public readonly HashSet<string> Down = new HashSet<string>();     // keys held now
        public readonly HashSet<string> Pressed = new HashSet<string>();  // keys pressed since EndFrame()
        public event Action<int> PadConnected;                            // pad index
        public void Poll() { PadConnected?.Invoke(-1); }                  // once at the start of every frame
        public InputRead Read(string device) => new InputRead();          // JS read(): axes + edge-triggered actions
        public List<PadPress> PadPresses() => new List<PadPress>();       // JS padPresses(): new presses on any pad
        public IReadOnlyList<PadState> Pads() => new List<PadState>();    // connected pads
        public PadState Pad(string device) => null;                       // "pad0".."pad3" (null if not connected)
        public bool Consume(string code) => false;
        public void EndFrame() { }
        public void ReloadBindings() { }
        public string FirstKeyPressed() => null;                          // a key code pressed this frame, for re-binding
    }
}
