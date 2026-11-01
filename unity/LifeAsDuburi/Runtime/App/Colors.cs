// Colours written the way the browser version writes them ('#ffb547' strings
// and 0x2f9fc4 numbers) turned into Unity colours.

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
}
