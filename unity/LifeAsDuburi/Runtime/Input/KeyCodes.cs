// Browser KeyboardEvent.code strings <-> Input System keys. The browser game
// stored KeyboardEvent.code values ("KeyW", "ArrowUp", "ShiftLeft"); the saved
// bindings and the UI keep using them, InputHub reads the keys through this map.
// Every physical key of a standard keyboard that has both a KeyboardEvent.code
// and a UnityEngine.InputSystem.Key is listed (the OEM / IME keys have no fixed
// browser code and are left out).

using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Duburi.Game
{
    public static class KeyCodes
    {
        // code, key, code, key, ...
        static readonly object[] Table =
        {
            "Space", Key.Space,
            "Enter", Key.Enter,
            "Tab", Key.Tab,
            "Backquote", Key.Backquote,
            "Quote", Key.Quote,
            "Semicolon", Key.Semicolon,
            "Comma", Key.Comma,
            "Period", Key.Period,
            "Slash", Key.Slash,
            "Backslash", Key.Backslash,
            "BracketLeft", Key.LeftBracket,
            "BracketRight", Key.RightBracket,
            "Minus", Key.Minus,
            "Equal", Key.Equals,
            "Digit0", Key.Digit0,
            "Digit1", Key.Digit1,
            "Digit2", Key.Digit2,
            "Digit3", Key.Digit3,
            "Digit4", Key.Digit4,
            "Digit5", Key.Digit5,
            "Digit6", Key.Digit6,
            "Digit7", Key.Digit7,
            "Digit8", Key.Digit8,
            "Digit9", Key.Digit9,
            "KeyA", Key.A,
            "KeyB", Key.B,
            "KeyC", Key.C,
            "KeyD", Key.D,
            "KeyE", Key.E,
            "KeyF", Key.F,
            "KeyG", Key.G,
            "KeyH", Key.H,
            "KeyI", Key.I,
            "KeyJ", Key.J,
            "KeyK", Key.K,
            "KeyL", Key.L,
            "KeyM", Key.M,
            "KeyN", Key.N,
            "KeyO", Key.O,
            "KeyP", Key.P,
            "KeyQ", Key.Q,
            "KeyR", Key.R,
            "KeyS", Key.S,
            "KeyT", Key.T,
            "KeyU", Key.U,
            "KeyV", Key.V,
            "KeyW", Key.W,
            "KeyX", Key.X,
            "KeyY", Key.Y,
            "KeyZ", Key.Z,
            "ShiftLeft", Key.LeftShift,
            "ShiftRight", Key.RightShift,
            "AltLeft", Key.LeftAlt,
            "AltRight", Key.RightAlt,
            "ControlLeft", Key.LeftCtrl,
            "ControlRight", Key.RightCtrl,
            "MetaLeft", Key.LeftMeta,
            "MetaRight", Key.RightMeta,
            "ContextMenu", Key.ContextMenu,
            "Escape", Key.Escape,
            "ArrowLeft", Key.LeftArrow,
            "ArrowRight", Key.RightArrow,
            "ArrowUp", Key.UpArrow,
            "ArrowDown", Key.DownArrow,
            "Backspace", Key.Backspace,
            "PageDown", Key.PageDown,
            "PageUp", Key.PageUp,
            "Home", Key.Home,
            "End", Key.End,
            "Insert", Key.Insert,
            "Delete", Key.Delete,
            "CapsLock", Key.CapsLock,
            "NumLock", Key.NumLock,
            "PrintScreen", Key.PrintScreen,
            "ScrollLock", Key.ScrollLock,
            "Pause", Key.Pause,
            "NumpadEnter", Key.NumpadEnter,
            "NumpadDivide", Key.NumpadDivide,
            "NumpadMultiply", Key.NumpadMultiply,
            "NumpadAdd", Key.NumpadPlus,
            "NumpadSubtract", Key.NumpadMinus,
            "NumpadDecimal", Key.NumpadPeriod,
            "NumpadEqual", Key.NumpadEquals,
            "Numpad0", Key.Numpad0,
            "Numpad1", Key.Numpad1,
            "Numpad2", Key.Numpad2,
            "Numpad3", Key.Numpad3,
            "Numpad4", Key.Numpad4,
            "Numpad5", Key.Numpad5,
            "Numpad6", Key.Numpad6,
            "Numpad7", Key.Numpad7,
            "Numpad8", Key.Numpad8,
            "Numpad9", Key.Numpad9,
            "F1", Key.F1,
            "F2", Key.F2,
            "F3", Key.F3,
            "F4", Key.F4,
            "F5", Key.F5,
            "F6", Key.F6,
            "F7", Key.F7,
            "F8", Key.F8,
            "F9", Key.F9,
            "F10", Key.F10,
            "F11", Key.F11,
            "F12", Key.F12,
            "F13", Key.F13,
            "F14", Key.F14,
            "F15", Key.F15,
            "F16", Key.F16,
            "F17", Key.F17,
            "F18", Key.F18,
            "F19", Key.F19,
            "F20", Key.F20,
            "F21", Key.F21,
            "F22", Key.F22,
            "F23", Key.F23,
            "F24", Key.F24,
        };

        static readonly Dictionary<string, Key> ToKeyMap = new Dictionary<string, Key>();
        static readonly string[] ToCodeMap;

        static KeyCodes()
        {
            int max = 0;
            for (int i = 1; i < Table.Length; i += 2) if ((int)(Key)Table[i] > max) max = (int)(Key)Table[i];
            ToCodeMap = new string[max + 1];
            for (int i = 0; i < Table.Length; i += 2)
            {
                var code = (string)Table[i];
                var key = (Key)Table[i + 1];
                ToKeyMap[code] = key;
                ToCodeMap[(int)key] = code;
            }
        }

        public static int Count => Table.Length / 2;
        public static string CodeAt(int i) => (string)Table[2 * i];
        public static Key KeyAt(int i) => (Key)Table[2 * i + 1];

        // KeyboardEvent.code -> Key (Key.None when unknown)
        public static Key ToKey(string code) => code != null && ToKeyMap.TryGetValue(code, out var k) ? k : Key.None;

        // Key -> KeyboardEvent.code (null when the key has no browser code)
        public static string ToCode(Key key)
        {
            int i = (int)key;
            return i > 0 && i < ToCodeMap.Length ? ToCodeMap[i] : null;
        }
    }
}
