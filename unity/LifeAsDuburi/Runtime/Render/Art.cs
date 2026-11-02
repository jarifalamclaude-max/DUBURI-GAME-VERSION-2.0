// src/render/art.js: the two marine animals on the 2025 course (reef shark, sawfish), used for the
// vinyl prints on the gate, bin, board and octagon — prints from the competition team handbook
// figures (Resources/Duburi/Art) — plus the canvas drawing of the animals and the HUD icons.

using System.Collections.Generic;
using UnityEngine;

namespace Duburi.Game
{
    public static class Art
    {
        // IMAGES: key -> Resources/Duburi/Art file
        static readonly Dictionary<string, string> IMAGES = new Dictionary<string, string>
        {
            ["shark"] = "shark", ["sawfish"] = "sawfish", ["binShark"] = "bin-shark", ["binSawfish"] = "bin-sawfish",
            ["boardSharkTop"] = "board-shark-top", ["boardSawfishTop"] = "board-sawfish-top",
        };

        static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
        static Texture2D logo;

        /// DUBURI logo (Resources/Duburi/Art/logo)
        public static Texture2D Logo
        {
            get
            {
                if (logo == null)
                {
                    logo = Resources.Load<Texture2D>("Duburi/Art/logo");
                    if (logo != null) logo.anisoLevel = 4;
                }
                return logo;
            }
        }

        /// imageTexture(key): "shark", "sawfish", "binShark", "binSawfish", "boardSharkTop", "boardSawfishTop"
        public static Texture2D Image(string key)
        {
            if (key != null && texCache.TryGetValue(key, out var t) && t != null) return t;
            if (key == null || !IMAGES.TryGetValue(key, out var file)) return null;
            t = Resources.Load<Texture2D>("Duburi/Art/" + file);
            if (t == null) { Debug.LogWarning("Life as Duburi: missing image Resources/Duburi/Art/" + file); return null; }
            t.anisoLevel = 4;
            texCache[key] = t;
            return t;
        }

        /// gate / octagon print (12 in square) and the bin halves
        public static Texture2D AnimalTexture(string animal, string where = "gate")
        {
            if (where == "bin") return Image(animal == "shark" ? "binShark" : "binSawfish");
            return Image(animal);
        }

        /// small icon for the HUD / menus (the bin prints, like the JS)
        public static Texture2D AnimalIcon(string animal) => Image(animal == "shark" ? "binShark" : "binSawfish");

        /// drawAnimal(g, animal, cx, cy, size, flip): side view facing right, 'size' px long
        public static void DrawAnimal(Canvas2D g, string animal, float cx, float cy, float size, bool flip = false)
        {
            g.Save();
            g.Translate(cx, cy);
            if (flip) g.Scale(-1, 1);
            float s = size / 200f;
            g.Scale(s, s);
            if (animal == "shark")
            {
                // grey reef shark, side view, facing right
                g.BeginPath();
                g.MoveTo(95, 0);
                g.QuadraticCurveTo(70, -26, 0, -24);
                g.LineTo(-10, -30); g.LineTo(-22, -62); g.LineTo(-36, -24); // dorsal fin
                g.QuadraticCurveTo(-60, -18, -72, -8);
                g.LineTo(-100, -40); g.LineTo(-92, 0); g.LineTo(-106, 34); g.LineTo(-74, 8); // tail
                g.QuadraticCurveTo(-40, 22, 0, 22);
                g.LineTo(-6, 26); g.LineTo(-30, 46); g.LineTo(-14, 22); // pectoral fin
                g.QuadraticCurveTo(60, 24, 95, 0);
                g.Fill(Colors.Hex("#6f7f8c"));
                g.BeginPath(); g.MoveTo(88, 4); g.QuadraticCurveTo(50, 20, -40, 14); g.QuadraticCurveTo(20, 8, 88, 4); g.Fill(Colors.Hex("#e9eef2"));
                g.BeginPath(); g.Arc(62, -6, 4, 0, Mathf.PI * 2); g.Fill(Colors.Hex("#111"));
                for (int i = 0; i < 4; i++) { g.BeginPath(); g.MoveTo(40 - i * 6, -10); g.LineTo(37 - i * 6, 6); g.Stroke(Colors.Hex("#4a5560"), 2); }
                g.BeginPath(); g.MoveTo(-100, -40); g.LineTo(-92, 0); g.LineTo(-84, -14); g.Fill(Colors.Hex("#2c3640"));
            }
            else
            {
                // sawfish: flat body, long toothed rostrum, facing right
                Color body = Colors.Hex("#9a7f5f");
                g.BeginPath();
                g.MoveTo(40, -6);
                g.QuadraticCurveTo(10, -22, -30, -18);
                g.LineTo(-38, -40); g.LineTo(-50, -18);
                g.QuadraticCurveTo(-75, -12, -86, -6);
                g.LineTo(-104, -30); g.LineTo(-98, 0); g.LineTo(-104, 24); g.LineTo(-86, 6);
                g.QuadraticCurveTo(-30, 18, 40, 6);
                g.Fill(body);
                // pectoral fins
                g.BeginPath(); g.MoveTo(10, 6); g.LineTo(-14, 34); g.LineTo(-20, 8); g.Fill(body);
                // saw
                g.FillRect(36, -4, 66, 8, Colors.Hex("#b39a78"));
                Color tooth = Colors.Hex("#f4efe4");
                for (int x = 42; x < 102; x += 8)
                {
                    g.BeginPath(); g.MoveTo(x, -4); g.LineTo(x + 2, -11); g.LineTo(x + 4, -4); g.Fill(tooth);
                    g.BeginPath(); g.MoveTo(x, 4); g.LineTo(x + 2, 11); g.LineTo(x + 4, 4); g.Fill(tooth);
                }
                g.BeginPath(); g.Arc(28, -8, 3.5f, 0, Mathf.PI * 2); g.Fill(Colors.Hex("#111"));
            }
            g.Restore();
        }
    }
}
