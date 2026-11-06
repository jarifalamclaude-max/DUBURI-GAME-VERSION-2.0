// CONTRACT STUB (compile-check only, never shipped). The real code lives in
// LifeAsDuburi/Runtime/Render. JS originals: src/render/art.js, graphics.js and
// the bot thumbnails of src/main.js (thumb / renderThumb).
using UnityEngine;

namespace Duburi.Game
{
    public static class Art
    {
        public static Texture2D Logo => null;                       // DUBURI logo
        public static Texture2D Image(string key) => null;          // "shark", "sawfish", "binShark", "binSawfish", "boardSharkTop", "boardSawfishTop"
        public static Texture2D AnimalIcon(string animal) => null;  // "shark" | "sawfish" (HUD / menu icon)
    }

    public static class BotThumbs
    {
        public static Texture Get(string botId, Color color) => null; // 240x150, cached per bot + colour
    }

    public static class GraphicsQuality
    {
        public static readonly string[] Order = { "ultra", "high", "medium", "low" };
        public static string Current => "high";
        public static string Label(string id) => id;   // "High", "Medium", "Low"
        public static void Set(string id) { }          // remember (PlayerPrefs) + apply global settings
    }
}
