// The pure parts of src/ui/menu.js: the game's name and the default match setup
// (what the lobby starts with and what is remembered under the PlayerPrefs /
// localStorage key "life-as-duburi-setup-v2", same JSON field names).

namespace Duburi.Core
{
    /// one of the four lobby slots
    public sealed class SlotSetup
    {
        public string type;  // "off" | input device id ("kbA", "kbB", "pad0".., "cpu")
        public string name;
        public string bot;   // BOTS key
        public string color; // "#rrggbb"
        public SlotSetup Clone() => (SlotSetup)MemberwiseClone();
    }

    /// the setup object of menu.js (JSON: { scenario, minutes, coinFlip, depth, slots: [4] })
    public sealed class MatchSetup
    {
        public string scenario;
        public double minutes;
        public bool coinFlip;
        public double depth; // 0 = the scenario's own depth (DEPTH_CHOICES)
        public SlotSetup[] slots;

        public MatchSetup Clone()
        {
            var c = (MatchSetup)MemberwiseClone();
            if (slots != null)
            {
                c.slots = new SlotSetup[slots.Length];
                for (int i = 0; i < slots.Length; i++) c.slots[i] = slots[i]?.Clone();
            }
            return c;
        }
    }

    public static class MenuSetup
    {
        public const string GAME_NAME = "Life as Duburi";

        public static MatchSetup DefaultSetup()
        {
            var C = Bots.PLAYER_COLORS;
            return new MatchSetup
            {
                scenario = "pool", minutes = 15, coinFlip = true, depth = 0,
                slots = new[]
                {
                    new SlotSetup { type = "kbA", name = "Player 1", bot = "duburi", color = C[0] },
                    new SlotSetup { type = "off", name = "Player 2", bot = "dubomini", color = C[4] },
                    new SlotSetup { type = "off", name = "Player 3", bot = "kraken", color = C[2] },
                    new SlotSetup { type = "off", name = "Player 4", bot = "manta", color = C[3] },
                },
            };
        }
    }
}
