// Keyboard (two layouts on one keyboard) and gamepads. Each player is bound
// to one device; InputHub.Read(device) returns the analogue axes and the action
// buttons that were pressed since the last read (edge-triggered).
// Every binding can be changed by the player (controls screen); the changes
// are kept per device in PlayerPrefs (the browser kept them in localStorage,
// same key and JSON). Gamepads default to the ArduSub / QGroundControl stick
// layout: left stick = heave + yaw, right = surge + sway.
// Port of src/ui/input.js (data, bindings, names); the device reading is in InputHub.cs.

using System.Collections.Generic;
using UnityEngine;

namespace Duburi.Game
{
    public sealed class DeviceDef
    {
        public string id; public string label;
        public DeviceDef() { }
        public DeviceDef(string id, string label) { this.id = id; this.label = label; }
    }

    public sealed class ActionDef
    {
        public string id; public string label;
        public ActionDef() { }
        public ActionDef(string id, string label) { this.id = id; this.label = label; }
    }

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

        public Bindings Clone()
        {
            return new Bindings
            {
                isPad = isPad,
                axes = new Dictionary<string, string>(axes),
                actions = new Dictionary<string, string>(actions),
                fine = fine,
                sticks = sticks,
                invertY = invertY,
                triggersHeave = triggersHeave,
                buttons = new Dictionary<string, int>(buttons),
            };
        }

        // the JSON object the browser version stores for this device
        internal Dictionary<string, object> ToJson()
        {
            var o = new Dictionary<string, object>();
            if (isPad)
            {
                o["sticks"] = sticks;
                o["invertY"] = invertY;
                o["triggersHeave"] = triggersHeave;
                var bt = new Dictionary<string, object>();
                foreach (var kv in buttons) bt[kv.Key] = kv.Value;
                o["buttons"] = bt;
            }
            else
            {
                var ax = new Dictionary<string, object>();
                foreach (var kv in axes) ax[kv.Key] = kv.Value;
                var ac = new Dictionary<string, object>();
                foreach (var kv in actions) ac[kv.Key] = kv.Value;
                o["axes"] = ax;
                o["actions"] = ac;
                o["fine"] = fine;
            }
            return o;
        }
    }

    public static class Controls
    {
        public static readonly DeviceDef[] Devices =
        {
            new DeviceDef("kbA", "Keyboard — left (WASD)"),
            new DeviceDef("kbB", "Keyboard — right (arrows)"),
            new DeviceDef("pad0", "Gamepad 1"),
            new DeviceDef("pad1", "Gamepad 2"),
            new DeviceDef("pad2", "Gamepad 3"),
            new DeviceDef("pad3", "Gamepad 4"),
            new DeviceDef("cpu", "CPU (autopilot)"),
        };

        // one-shot actions (buttons / keys)
        public static readonly ActionDef[] Actions =
        {
            new ActionDef("marker", "Drop marker"),
            new ActionDef("torpedo", "Fire torpedo"),
            new ActionDef("grip", "Gripper open / close"),
            new ActionDef("camera", "Camera: chase / front / down"),
            new ActionDef("depthHold", "Depth hold on / off"),
            new ActionDef("skip", "Skip the current task"),
            new ActionDef("endRun", "End run (keep points)"),
            new ActionDef("restart", "Forfeit: new run"),
            new ActionDef("pause", "Pause menu"),
        };

        // held buttons on a gamepad that drive an axis
        public static readonly ActionDef[] PadHolds =
        {
            new ActionDef("rollLeft", "Roll left"),
            new ActionDef("rollRight", "Roll right"),
            new ActionDef("pitchUp", "Pitch nose up"),
            new ActionDef("pitchDown", "Pitch nose down"),
        };

        // keyboard axes: "<axis><sign>"
        public static readonly ActionDef[] KeyAxes =
        {
            new ActionDef("surge+", "Forward"), new ActionDef("surge-", "Back"),
            new ActionDef("yaw-", "Turn left"), new ActionDef("yaw+", "Turn right"),
            new ActionDef("sway-", "Strafe left"), new ActionDef("sway+", "Strafe right"),
            new ActionDef("heave+", "Up"), new ActionDef("heave-", "Down"),
            new ActionDef("roll-", "Roll left"), new ActionDef("roll+", "Roll right"),
            new ActionDef("pitch+", "Pitch up"), new ActionDef("pitch-", "Pitch down"),
        };

        // stick layouts: axis -> { gamepad axis index, sign }
        public static readonly string[] StickPresetOrder = { "ardusub", "game", "racing" };
        public static readonly Dictionary<string, StickPreset> StickPresets = new Dictionary<string, StickPreset>
        {
            ["ardusub"] = Preset("ardusub", "ArduSub / QGC", "left stick: up-down + turn · right stick: forward + strafe",
                "heave", 1, -1, "yaw", 0, 1, "surge", 3, -1, "sway", 2, 1),
            ["game"] = Preset("game", "Game", "left stick: forward + strafe · right stick: up-down + turn",
                "surge", 1, -1, "sway", 0, 1, "heave", 3, -1, "yaw", 2, 1),
            ["racing"] = Preset("racing", "Racing", "left stick: forward + turn · right stick: up-down + strafe",
                "surge", 1, -1, "yaw", 0, 1, "heave", 3, -1, "sway", 2, 1),
        };

        static StickPreset Preset(string id, string label, string desc, params object[] m)
        {
            var p = new StickPreset { id = id, label = label, desc = desc };
            for (int i = 0; i < m.Length; i += 3) p.map[(string)m[i]] = new[] { (int)m[i + 1], (int)m[i + 2] };
            return p;
        }

        static Bindings DefaultKeys(string device)
        {
            var b = new Bindings { isPad = false };
            if (device == "kbB")
            {
                Fill(b.axes, "surge+", "ArrowUp", "surge-", "ArrowDown", "yaw+", "ArrowRight", "yaw-", "ArrowLeft", "sway+", "KeyL", "sway-", "KeyJ",
                    "heave+", "KeyI", "heave-", "KeyK", "roll+", "KeyO", "roll-", "KeyU", "pitch+", "KeyY", "pitch-", "KeyH");
                Fill(b.actions, "marker", "KeyN", "torpedo", "KeyM", "grip", "Comma", "camera", "Digit7", "depthHold", "Digit8",
                    "endRun", "Digit9", "restart", "Digit0", "skip", "Minus", "pause", "Backspace");
                b.fine = "ShiftRight";
            }
            else // kbA (also the fallback for unknown keyboard devices)
            {
                Fill(b.axes, "surge+", "KeyW", "surge-", "KeyS", "yaw+", "KeyD", "yaw-", "KeyA", "sway+", "KeyE", "sway-", "KeyQ",
                    "heave+", "KeyR", "heave-", "KeyF", "roll+", "KeyX", "roll-", "KeyZ", "pitch+", "KeyT", "pitch-", "KeyG");
                Fill(b.actions, "marker", "KeyV", "torpedo", "KeyB", "grip", "KeyC", "camera", "Digit1", "depthHold", "Digit2",
                    "endRun", "Digit3", "restart", "Digit4", "skip", "Digit5", "pause", "Tab");
                b.fine = "ShiftLeft";
            }
            return b;
        }

        // standard-mapping button indices
        static Bindings DefaultPad()
        {
            var b = new Bindings { isPad = true, sticks = "ardusub", invertY = false, triggersHeave = true };
            b.buttons["marker"] = 0; b.buttons["torpedo"] = 1; b.buttons["grip"] = 2; b.buttons["camera"] = 3;
            b.buttons["rollLeft"] = 4; b.buttons["rollRight"] = 5; b.buttons["depthHold"] = 8; b.buttons["pause"] = 9;
            b.buttons["endRun"] = 10; b.buttons["restart"] = 11; b.buttons["pitchUp"] = 12; b.buttons["pitchDown"] = 13; b.buttons["skip"] = 15;
            return b;
        }

        static void Fill(Dictionary<string, string> d, params string[] kv)
        {
            for (int i = 0; i < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
        }

        const string Store = "life-as-duburi-controls-v1";

        static Dictionary<string, object> LoadAll()
        {
            try
            {
                var o = Json.Obj(Json.Parse(PlayerPrefs.GetString(Store, "")));
                if (o != null) return o;
            }
            catch (System.Exception) { /* storage unavailable */ }
            return new Dictionary<string, object>();
        }

        static void StoreAll(Dictionary<string, object> all)
        {
            try { PlayerPrefs.SetString(Store, Json.Stringify(all)); PlayerPrefs.Save(); }
            catch (System.Exception) { /* ignore */ }
        }

        internal static bool IsPadDevice(string device) => device != null && device.StartsWith("pad", System.StringComparison.Ordinal);

        // the bindings of one device (defaults + the player's changes)
        public static Bindings GetBindings(string device)
        {
            var saved = Json.Obj(Json.Get(LoadAll(), device));
            if (IsPadDevice(device))
            {
                var b = DefaultPad();
                if (saved != null)
                {
                    if (saved.TryGetValue("sticks", out var s)) b.sticks = s as string;
                    if (saved.TryGetValue("invertY", out var iy)) b.invertY = Truthy(iy);
                    if (saved.TryGetValue("triggersHeave", out var th)) b.triggersHeave = Truthy(th);
                    var bt = Json.Obj(Json.Get(saved, "buttons"));
                    if (bt != null)
                        foreach (var kv in bt)
                            b.buttons[kv.Key] = kv.Value is double d ? (int)d : -1;
                }
                if (b.sticks == null || !StickPresets.ContainsKey(b.sticks)) b.sticks = "ardusub";
                return b;
            }
            var k = DefaultKeys(device);
            if (saved != null)
            {
                Merge(k.axes, Json.Obj(Json.Get(saved, "axes")));
                Merge(k.actions, Json.Obj(Json.Get(saved, "actions")));
                // the JS forgot the re-bound fine-control key here; the controls screen can change it, so keep it
                if (saved.TryGetValue("fine", out var f) && (f == null || f is string)) k.fine = f as string;
            }
            return k;
        }

        static bool Truthy(object v) => v is bool b ? b : v is double d ? d != 0 && !double.IsNaN(d) : v is string s ? s.Length > 0 : v != null;

        static void Merge(Dictionary<string, string> into, Dictionary<string, object> from)
        {
            if (from == null) return;
            foreach (var kv in from) into[kv.Key] = kv.Value as string;
        }

        public static void SaveBindings(string device, Bindings b)
        {
            var all = LoadAll();
            all[device] = b.ToJson();
            StoreAll(all);
            BindingCache.Remove(device);
        }

        public static void ResetBindings(string device)
        {
            var all = LoadAll();
            all.Remove(device);
            StoreAll(all);
            BindingCache.Remove(device);
        }

        static readonly Dictionary<string, Bindings> BindingCache = new Dictionary<string, Bindings>();
        internal static Bindings Cached(string device)
        {
            if (!BindingCache.TryGetValue(device, out var b)) { b = GetBindings(device); BindingCache[device] = b; }
            return b;
        }
        internal static void ClearCache() => BindingCache.Clear();

        // ---- names for the HUD / controls screen ----
        public static string PadStyle(PadState pad)
        {
            var id = (pad != null && pad.id != null ? pad.id : "").ToLowerInvariant();
            foreach (var w in PsWords) if (id.Contains(w)) return "ps";
            return "xbox";
        }
        static readonly string[] PsWords = { "054c", "sony", "dualsense", "dualshock", "playstation", "wireless controller" };

        static readonly string[] PsNames = { "✕", "○", "□", "△", "L1", "R1", "L2", "R2", "Create", "Options", "L3", "R3", "D-pad ↑", "D-pad ↓", "D-pad ←", "D-pad →", "PS" };
        static readonly string[] XbNames = { "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "View", "Menu", "LS", "RS", "D-pad ↑", "D-pad ↓", "D-pad ←", "D-pad →", "Guide" };

        public static string ButtonName(int index, string style = "ps")
        {
            if (index < 0) return "—";
            var names = style == "ps" ? PsNames : XbNames;
            return index < names.Length ? names[index] : "Button " + index;
        }

        static readonly Dictionary<string, string> KeyNames = new Dictionary<string, string>
        {
            ["ArrowUp"] = "↑", ["ArrowDown"] = "↓", ["ArrowLeft"] = "←", ["ArrowRight"] = "→", ["Comma"] = ",", ["Period"] = ".",
            ["Minus"] = "-", ["Equal"] = "=", ["Slash"] = "/", ["Semicolon"] = ";", ["Quote"] = "'", ["BracketLeft"] = "[",
            ["BracketRight"] = "]", ["Backslash"] = "\\", ["Backquote"] = "`", ["Space"] = "Space", ["ShiftLeft"] = "Left Shift",
            ["ShiftRight"] = "Right Shift", ["ControlLeft"] = "Left Ctrl", ["ControlRight"] = "Right Ctrl", ["AltLeft"] = "Left Alt",
            ["AltRight"] = "Right Alt", ["Enter"] = "Enter", ["Backspace"] = "Backspace", ["Tab"] = "Tab",
        };

        public static string KeyName(string code)
        {
            if (string.IsNullOrEmpty(code)) return "—";
            if (KeyNames.TryGetValue(code, out var n)) return n;
            if (code.StartsWith("Key", System.StringComparison.Ordinal)) return code.Substring(3);
            if (code.StartsWith("Digit", System.StringComparison.Ordinal)) return code.Substring(5);
            if (code.StartsWith("Numpad", System.StringComparison.Ordinal)) return "Num " + code.Substring(6);
            return code;
        }

        static string Label(string id)
        {
            foreach (var a in Actions) if (a.id == id) return a.label;
            foreach (var a in PadHolds) if (a.id == id) return a.label;
            return null;
        }

        static int Btn(Bindings b, string id) => b.buttons.TryGetValue(id, out var i) ? i : -1;
        static string Get(Dictionary<string, string> d, string id) => d.TryGetValue(id, out var v) ? v : null;

        // { key, what } rows for the help screens, from the current bindings
        public static List<string[]> HelpRows(string device, string style = "ps")
        {
            var rows = new List<string[]>();
            if (IsPadDevice(device))
            {
                var b = GetBindings(device);
                var P = StickPresets[b.sticks];
                rows.Add(new[] { "Sticks", P.label + ": " + P.desc + (b.invertY ? " (Y inverted)" : "") });
                if (b.triggersHeave) rows.Add(new[] { ButtonName(6, style) + " / " + ButtonName(7, style), "down / up" });
                foreach (var h in PadHolds) rows.Add(new[] { ButtonName(Btn(b, h.id), style), Label(h.id) });
                foreach (var a in Actions) rows.Add(new[] { ButtonName(Btn(b, a.id), style), Label(a.id) });
                return rows;
            }
            var k = GetBindings(device);
            for (int i = 0; i < KeyAxes.Length; i += 2)
                rows.Add(new[]
                {
                    KeyName(Get(k.axes, KeyAxes[i].id)) + " / " + KeyName(Get(k.axes, KeyAxes[i + 1].id)),
                    KeyAxes[i].label.ToLowerInvariant() + " / " + KeyAxes[i + 1].label.ToLowerInvariant(),
                });
            foreach (var a in Actions) rows.Add(new[] { KeyName(Get(k.actions, a.id)), a.label });
            rows.Add(new[] { KeyName(k.fine), "hold for fine control" });
            return rows;
        }
    }
}
