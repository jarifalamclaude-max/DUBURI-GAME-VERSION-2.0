// Colours written the way the browser version writes them ('#ffb547' strings
// and 0x2f9fc4 numbers) turned into Unity colours.
//
// Also Faults, the one way every module reports an exception caught in a Unity callback (it lives in
// this small shared file because the per-module compile checks build each module with App/Colors.cs).

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Duburi.Game
{
    public static class Colors
    {
        // "#rgb", "#rrggbb" or "#rrggbbaa"; white when the text is not a colour
        public static Color Hex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.white;
            string h = hex[0] == '#' ? hex.Substring(1) : hex;
            if (h.Length == 3) h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            if ((h.Length != 6 && h.Length != 8) || !uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
                return Color.white;
            if (h.Length == 6) v = (v << 8) | 0xff;
            return new Color32((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }

        // 0xrrggbb
        public static Color Rgb(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        public static string ToHex(Color c)
        {
            Color32 k = c;
            return "#" + k.r.ToString("x2") + k.g.ToString("x2") + k.b.ToString("x2");
        }

        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }
    }

    /// Exceptions caught in Unity callbacks the App does not drive itself (uGUI layout / clicks,
    /// MonoBehaviour updates, Input System device callbacks, per-camera render hooks). WebGL builds have
    /// no stack traces, so each report names where it happened ("[in ...]"); the same error from the same
    /// place is logged once (a per-frame fault does not flood the browser console) and counted after that.
    /// The ErrorPanel (Editor and Development builds) shows these reports; release builds only log them.
    public static class Faults
    {
        /// every error the game logs itself starts with this
        public const string Prefix = "Life as Duburi: ";
        const int MaxKeys = 256;

        static readonly HashSet<string> seen = new HashSet<string>();

        /// the same error happened again (already logged): the error panel counts it
        public static event Action Repeated;

        /// "NullReferenceException: Object reference ... [in where]"
        public static string Key(Exception e, string where) =>
            (e != null ? e.GetType().Name + ": " + e.Message : "error") + " [in " + (string.IsNullOrEmpty(where) ? "?" : where) + "]";

        /// logs the exception once per kind and place (Debug.LogError, so it also reaches the browser
        /// console); true when it was logged now, false for a repeat
        public static bool Report(string where, Exception e)
        {
            string key = Key(e, where);
            if (seen.Contains(key))
            {
                try { Repeated?.Invoke(); } catch (Exception) { /* a counter must never throw on */ }
                return false;
            }
            if (seen.Count >= MaxKeys) seen.Clear();
            seen.Add(key);
            Debug.LogError(Prefix + key + "\n" + e);
            return true;
        }

        // Enter Play Mode without a domain reload keeps statics: start clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            seen.Clear();
            Repeated = null;
        }
    }
}
