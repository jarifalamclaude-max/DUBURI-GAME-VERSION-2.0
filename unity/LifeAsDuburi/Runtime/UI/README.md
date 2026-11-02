# Runtime/UI — menus, HUD, controls screen, overlays (namespace `Duburi.Game`)

uGUI built entirely in code: no prefabs, no scene objects, no TextMeshPro. Text is
`UnityEngine.UI.Text` with the fonts in `Resources/Duburi/Fonts` (Saira Semi Condensed for
UI text, IBM Plex Mono for numbers and keys, like `style.css`). Every shape the CSS / SVG
draws is generated at runtime (`Ui.Rounded` 9-sliced rounded rects, `Raster` for the
controller outline, the minimap and the icons). Glyphs the two fonts do not have
(✕ ○ □ △ ● ▶ ◀ ▲ ▼ ⏸ ▮ ▯ 🏆) are drawn as icons automatically wherever text is built
with `Ui.Pieces` / `Ui.Line` / `Rich` / chips (button names from `Controls.ButtonName`
included).

Uses only: Core, `Runtime/App/Json.cs` + `Colors.cs`, and the contract members of Input
(`InputHub`, `Controls`, `PadState`, `Bindings`), Sound (`Sound`) and Render (`Art`,
`BotThumbs`, `GraphicsQuality`) in `tools/stubs/Contracts`.

## Files

| file | what |
|---|---|
| `UiRoot.cs` | the canvas, CanvasScaler, EventSystem, layers, split lines, resize event |
| `Menu.cs` | `menu.js` `Menu` (title, lobby, help, saved setup) + `KeyRepeat` |
| `HelpText.cs` | `rulesHTML()`, `controlsHTML()`, `resultsHTML(game)` |
| `Hud.cs` | `hud.js` `HUD` |
| `ControlsUi.cs` | `controls.js`: `PadDrawing` (`controllerSVG` + `highlightPad`), `ControlsUi.KeyboardGrid` (`keyboardHTML`), `ControlsUi.QuickCard` (`quickCardHTML`) |
| `Overlays.cs` | `main.js` `overlay()`, navigation, `openPause`, `openControls`, `endCapture`, `confirmQuit`, `showResults` |
| `Ui.cs`, `Widgets.cs`, `Layouts.cs`, `Raster.cs` | the style.css kit: colours (`Css`), fonts, sprites, `.btn` / `.mi` / `.bind` / `.ctab` / `.hb` / chips / select / input / checkbox / `.panel`, layout groups (`Flow`, `AutoGrid`, `Table`, `PanelLayout`, `AspectBox`), `Skin` (hover / focus), `Anim` (blink, toast, fade), anti-aliased rasteriser |

## Setting it up (App)

```csharp
using CoreGame = Duburi.Core.Game;

ui = UiRoot.Create(transform);                       // one Screen Space Overlay canvas + EventSystem (mouse / touch only)
overlays = new Overlays(ui, input, sound);
menu = new Menu(ui, input, sound, cfg => Start(cfg), dev => overlays.OpenControlsFromMenu(menu, dev));
menu.Show("title");
```

Per frame (`main.js frame()`), after `input.Poll()` and before the game reads input:

```csharp
overlays.Tick();                          // capture keys, nav keys (consumes every key while an overlay is open), navPoll, live pad drawing
if (state == "game") {
    if (!overlays.IsOpen && input.Consume("Escape")) OpenPause(viewPlayers[0]);   // main.js onKey, last line
    ... gameFrame ...; hud.Update(dt);
} else menu.Poll();                       // menu.js onKey + poll()
input.EndFrame();
```

`UiRoot.TextFieldFocused` is true while a lobby name field has the keyboard (menu.js ignores
keys typed into an INPUT); the game is not running then, so only the menu needs it (it checks
it itself).

## Public API

### `UiRoot` (MonoBehaviour)
| member | |
|---|---|
| `static UiRoot Create(Transform parent = null)` | canvas (Screen Space Overlay, sorting 10), `CanvasScaler` ScaleWithScreenSize (1600×900, match 0.5; 1280×720 on tablets, 1024×576 on phones), `GraphicRaycaster`, an `EventSystem` with `InputSystemUIInputModule` (default actions, `sendNavigationEvents = false`) unless one exists |
| `MenuLayer, HudLayer, SplitLayer, OverlayLayer` | layers in the browser's DOM order |
| `Size`, `Scale`, `ViewportWidth` (static) | canvas size in units (1 unit ≈ 1 CSS px), screen px per unit |
| `Rect ToCanvas(Rect px)` | screen-pixel rect with a TOP-left origin → canvas units |
| `void SetSplit(int views, bool wide)` | the split-screen lines (`.split-v` / `-h` / `-q`): call from `layoutViews` with `views.length` and `W >= H` |
| `event Action Resized` | canvas size changed (fired from LateUpdate) |
| `static bool TextFieldFocused` | a text field has the keyboard |

### `Menu`
| member | JS |
|---|---|
| `Menu(UiRoot ui, InputHub input, Sound sound, Action<MatchSetup> onStart, Action<string> onControls)` | `new Menu(root, { thumb, onStart, sound, input, onControls })`; thumbnails come from `BotThumbs.Get` |
| `MatchSetup Setup` | `this.setup` (loaded from PlayerPrefs `life-as-duburi-setup-v2`) |
| `void Show(string screen = "title")`, `Hide()`, `string Screen`, `bool Visible` | `show`, `hide`, `screen`, `visible` |
| `void Poll()` | `poll()` + the keyboard part of `onKey` (call every frame while not in a match) |
| `void Flash(string text)` | `flash` (2.2 s) |
| `void Controls(string dev = null)` | `controls(dev)` |
| `void Start()`, `Assign(i, type)`, `Join(type)` | `start`, `assign`, `join` |

`onStart` receives a **copy of the whole setup** (`MatchSetup`, 4 slots); the App builds
`GameOptions` from the slots whose `type != "off"`, in slot order, like `menu.js start()`:
`{ scenario, runMinutes: minutes, coinFlip, depth, players: [{ name, bot, color, device: type }] }`
(see Core/README.md "Starting a match"), and keeps it for "Restart match" / "Play again".
The menu flashes "Controller n connected …" itself when `InputHub.PadConnected` fires while it is visible.

### `Hud`
| member | JS |
|---|---|
| `Hud(UiRoot ui, CoreGame game, string[] cams, Action onPause, Action onQuit, InputHub input = null, IReadOnlyList<string> devices = null)` | `new HUD(root, game, { cams, onPause, onQuit })`; with `input` + `devices` it also does main.js's `hud.highlightCard(i, el => highlightPad(el, input.pad(d)))` every frame |
| `void Layout(IList<Rect> rectsPx)` | `layout(rects)`: one rect per player in screen pixels, top-left origin (`{0,0,0,0}` for a player without a view); sets `.small` / `.tiny`, moves the clock to the centre for split screen |
| `void Refresh()` | re-apply the `@media` rules after a resize without new rects |
| `void SetPanelHidden(int i, bool hidden)` | `P.el.hidden = …` (buildViews) |
| `void SetBoard(Rect? rectPx)` | `setBoard(rect)` (leaderboard over the TV quarter, null hides) |
| `void Toast(int i, string text, double pts, string kind)` | `toast` |
| `void Update(float dt)` | `update(dt)` (every frame; full refresh at 20 Hz) |
| `void ShowCard(int i, string device, PadState pad, float sec = 12)` | `showCard(i, quickCardHTML(d, pad), 12)` |
| `void HideCard(int i)`, `HighlightCard(int i, PadState pad)` | `hideCard`, `highlightCard(i, el => highlightPad(el, pad))` |
| `void Dispose()` | `dispose` |

### `Overlays`
| member | JS (main.js) |
|---|---|
| `Overlays(UiRoot ui, InputHub input, Sound sound)` | |
| `IReadOnlyList<string> Devices { set; }` | `this.devices` (navPoll owner rule) — set at every `start` |
| `bool IsOpen`, `bool Capturing`, `bool ControlsOpen`, `RectTransform Current` | `this.nav`, `this.capture`, `this.ctrl`, `this.ov` |
| `void Tick()` | onKey (capture + nav part), `navPoll()`, `highlightPad(this.ov, …)` |
| `PanelParts Overlay(Action<PanelParts> build, int owner = -1, Action onBack = null, float maxWidth = 1000, bool header = true)` + `AddNav(Button)` | `overlay(html, { owner, onBack })` with its `[data-nav]` items |
| `void Close()` | `closeOverlay()` |
| `NavFocus(i)`, `NavMove(d)`, `NavActivate()`, `NavBack()` | same |
| `void OpenPause(CoreGame g, int pi, string device, int humans, Action<string> onAction)` | `openPause(pi)` UI part (below) |
| `void OpenControls(string device, int owner, Action back, int focus = 0)` | `openControls` |
| `void OpenControlsFromMenu(Menu menu, string dev)` | `openControlsFromMenu` |
| `void ConfirmQuit(Action onNo, Action onYes)` | `confirmQuit` UI part |
| `void ShowResults(CoreGame g, Action<string> onAction)` | `showResults` UI part |

**OpenPause** — before calling, do main.js's part: return unless `state == "game" && game != null && !game.over`;
`paused = true; hud.HideCard(p.index); sound.Ensure();` with `p = g.players[pi] ?? g.players[0]`,
`device = devices[pi]`, `humans` = players with a non-"cpu" device that have not left. `onAction`:

| action | App does (main.js) |
|---|---|
| `"resume"` (also Esc / Tab / Backspace / ○ / B / Options) | `resume()`: `paused = false; overlays.Close(); input.Pressed.Clear();` and `input.Read(d)` for every pad device |
| `"skip"` / `"end"` / `"forfeit"` | `g.Action(pi, "skip" / "endRun" / "forfeit")`, then `resume()` |
| `"leave"` | `leave(pi)` |
| `"restart"` | `start(cfg)` |
| `"quit"` | `toMenu()` |
| `"quality"` | UI already called `GraphicsQuality.Set(next)`; App re-applies anything scene-specific (main.js: `applyQuality`, `setupShadows`, material refresh); the menu reopens itself |
| `"mute"` | UI already toggled `sound.SetMuted(!sound.Muted)`; the menu reopens itself |

"Controls & customise" is handled inside (opens `OpenControls(device == "cpu" ? "kbA" : device, pi, back to the pause menu)`).
Items and their rules are main.js's: Resume, Quit match, Skip task (`running` only, `: Task`),
End run (`running` only), Forfeit run, Leave match (`humans > 1` only), Restart match,
Graphics: <level>, Controls & customise (sub-line empty for a CPU), Sound: on/off; the nav hint,
and collapsible Rules / Controls.

**ConfirmQuit(onNo, onYes)** — App first: return unless in a running match, `paused = true`.
`onNo` = `resume()`, `onYes` = `toMenu()`.

**ShowResults(g, onAction)** — App sets `overShown = true` (main.js shows it 2.5 s after `g.over`).
`"again"` → `start(cfg)`, `"lobby"` (also back) → `toMenu()`, `"menu"` → `toMenu(); menu.Show("title")`.

**Re-binding** (inside `OpenControls`): choosing a row starts a capture ("press a button…" /
"press a key…", green, blinking); a pad capture takes the next button of that pad except 16
(cancelled after 8 s), a keyboard capture the next key (Escape cancels). A binding already used by
another action is swapped (`endCapture`), saved with `Controls.SaveBindings`, then
`input.ReloadBindings(); input.Pressed.Clear()` and the screen reopens on the same row.

### Helpers others may use
`HelpText.Rules(parent)`, `HelpText.Controls(parent)`, `HelpText.Results(parent, game)`;
`PadDrawing.Build(parent, device, style, compact)` + `.Highlight(pad)`; `ControlsUi.KeyboardGrid`,
`ControlsUi.QuickCard`; `Ui.*` builders, `Css` colours, `Rich` inline text.

## JS → C#

| JS (main.js / menu.js / hud.js / controls.js) | C# |
|---|---|
| `new Menu(this.ui, {...})` | `new Menu(ui, input, sound, onStart, onControls)` |
| `menu.show(s)` / `menu.hide()` / `menu.visible` / `menu.screen` | `Show(s)` / `Hide()` / `Visible` / `Screen` |
| `menu.poll()` + Menu `keydown` listener | `menu.Poll()` |
| `menu.flash(t)` | `menu.Flash(t)` |
| `loadSetup()` / `saveSetup()` / `defaultSetup()` | inside `Menu` (PlayerPrefs) / `MenuSetup.DefaultSetup()` (Core) |
| `rulesHTML()` / `controlsHTML()` / `resultsHTML(g)` | `HelpText.Rules` / `Controls` / `Results` |
| `new HUD(this.ui, game, {...})` | `new Hud(ui, game, cams, onPause, onQuit, input, devices)` |
| `hud.layout(rects)` / `hud.setBoard(r)` | `hud.Layout(rectsPx)` / `hud.SetBoard(rPx)` |
| `hud.toast(...)` / `hud.update(dt)` / `hud.dispose()` | `Toast` / `Update` / `Dispose` |
| `hud.showCard(i, quickCardHTML(d, pad), 12)` | `hud.ShowCard(i, d, pad, 12)` |
| `hud.hideCard(i)` / `hud.highlightCard(i, fn)` | `HideCard(i)` / `HighlightCard(i, pad)` (or automatic with `input` + `devices`) |
| `P.el.hidden = !viewPlayers.includes(i)` | `hud.SetPanelHidden(i, hidden)` |
| `controllerSVG(d, {style, compact})` / `highlightPad(root, pad)` | `PadDrawing.Build(...)` / `drawing.Highlight(pad)` |
| `keyboardHTML(d)` / `quickCardHTML(d, pad)` / `controlsScreenHTML(d, ...)` | `ControlsUi.KeyboardGrid` / `ControlsUi.QuickCard` / built by `OpenControls` |
| `this.overlay(html, {owner, onBack})` / `closeOverlay()` | `overlays.Overlay(build, owner, onBack)` + `AddNav` / `overlays.Close()` |
| `navFocus` / `navMove` / `navActivate` / `navBack` / `navPoll` | `NavFocus` / `NavMove` / `NavActivate` / `NavBack` / inside `Tick()` |
| `onKey` (capture, nav) | `overlays.Tick()` |
| `onKey` (`Escape` → `openPause`) | App: `if (!overlays.IsOpen && input.Consume("Escape"))` |
| `openPause(pi)` | App guard + `overlays.OpenPause(g, pi, devices[pi], humans, onAction)` |
| `openControls(dev, owner, back, focus)` / `openControlsFromMenu(dev)` | `overlays.OpenControls(...)` / `overlays.OpenControlsFromMenu(menu, dev)` |
| `endCapture(code)` | inside `Overlays` |
| `confirmQuit()` | App guard + `overlays.ConfirmQuit(Resume, ToMenu)` |
| `showResults()` | `overlays.ShowResults(game, onAction)` |
| `this.splitLines` (`.split-v/h/q`) | `ui.SetSplit(n, W >= H)` |
| `window resize` | `ui.Resized` (HUD: call `Layout` again with the new rects) |
| `gamepadconnected` flash | automatic in `Menu` |
| `this.sfx(name)` | `sound.Sfx(name)` |

## Differences from the browser version (deliberate)
* Keyboard navigation keys repeat when held (the browser repeats `keydown`); `KeyRepeat` gives 0.5 s delay, 25/s.
* Enter / Space on an overlay always runs `navActivate` (with its select sound); in the browser a
  focused button got the native click instead (same effect, no sound).
* Moving the focus with keys / pad scrolls the item into view (the browser used `preventScroll`,
  which left long lists such as the controls screen unreachable without a mouse); hovering never scrolls.
* No `letter-spacing`, `backdrop-filter` or `text-decoration: underline` in uGUI: underline and
  line-through are drawn as thin lines; letter spacing and the panel blur are left out.
* `<select>` is a uGUI `Dropdown` and `<details>` a click-to-open section; both mouse / touch only, like their browser counterparts (they were never `[data-nav]`).
* The 🏆 of the results and the symbol glyphs are drawn icons (the fonts have no such glyphs).
* Saved setup: a slot field with a wrong type falls back to the default slot's value (JS would keep the wrong value).
