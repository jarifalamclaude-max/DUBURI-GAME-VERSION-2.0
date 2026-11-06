// The styled elements of style.css built in code: .btn, .mi (pause menu item),
// .bind (re-bind row), .ctab, .hb (HUD buttons), .arrow, .sw (colour swatch),
// .linkbtn, kbd / .pad-btn chips, <select>, <input>, <input type=checkbox>,
// the .panel with its header and scrolling body, and rich text lines with inline
// keys and icons (wrapping like inline HTML).

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Duburi.Game
{
    /// .panel: header + scrolling body (content is a VerticalLayoutGroup)
    public sealed class PanelParts
    {
        public RectTransform panel, header, scroll, content;
        public ScrollRect scrollRect;
        public Image headerRule;
        public PanelLayout layout;
    }

    /// a line of inline content (text runs, keys, button chips, icons) that wraps like HTML
    public sealed class Rich
    {
        public readonly RectTransform rect;
        public readonly float size;
        public readonly Color color;
        public readonly UiFont font;

        public Rich(Transform parent, float size, Color color, UiFont font = UiFont.Regular, TextAnchor align = TextAnchor.UpperLeft, float lineGap = 0)
        {
            this.size = size; this.color = color; this.font = font;
            rect = Ui.Node("rich", parent);
            var f = rect.gameObject.AddComponent<Flow>();
            f.hgap = Mathf.Round(size * 0.27f * 10) / 10;
            f.vgap = lineGap;
            f.childAlignment = align;
        }

        public Rich T(string s, Color? c = null, UiFont? f = null, float? sz = null) { Ui.Pieces(rect, s, sz ?? size, c ?? color, f ?? font, true); return this; }
        public Rich B(string s, Color? c = null) => T(s, c, UiFont.Bold);
        public Rich Kbd(string s) { Ui.Kbd(rect, s); return this; }
        public Rich Pad(string s) { Ui.PadChip(rect, s); return this; }
    }

    public static partial class Ui
    {
        // ---------------------------------------------------------------- inline pieces
        /// text (split into words when `words`) and icons for the symbols the fonts lack
        public static void Pieces(Transform parent, string s, float size, Color c, UiFont f, bool words)
        {
            if (string.IsNullOrEmpty(s)) return;
            var buf = new System.Text.StringBuilder();
            void Flush()
            {
                if (buf.Length == 0) return;
                string run = buf.ToString();
                buf.Length = 0;
                if (!words) { if (run.Trim().Length > 0) Label(parent, run, size, c, f, TextAnchor.MiddleLeft, false); return; }
                foreach (var w in run.Split(' ')) if (w.Length > 0) Label(parent, w, size, c, f, TextAnchor.MiddleLeft, false);
            }
            for (int i = 0; i < s.Length; i++)
            {
                string key = IconKey(s, i, out int len);
                if (key == null) { buf.Append(s[i]); continue; }
                Flush();
                IconImage(parent, key, size, c);
                i += len - 1;
            }
            Flush();
        }

        /// a single-line label that may contain icons (keeps spaces; no wrapping)
        public static RectTransform Line(Transform parent, string s, float size, Color c, UiFont f = UiFont.Regular, TextAnchor align = TextAnchor.MiddleLeft, float gap = 0)
        {
            var rt = Node("line", parent);
            var h = HBox(rt, null, gap, align);
            h.childForceExpandHeight = false;
            Pieces(rt, s, size, c, f, false);
            return rt;
        }

        static RectTransform Chip(Transform parent, string s, float size, RectOffset pad, float radius, Color bgc, Color text)
        {
            var rt = Box(parent, "chip", bgc, radius, Css.line);
            var h = HBox(rt, pad, 0, TextAnchor.MiddleCenter);
            Pieces(rt, s, size, text, UiFont.Mono, false);
            return rt;
        }

        /// kbd { mono 12px; background rgba(255,255,255,.1); border 1px; radius 4; padding 1px 6px }
        public static RectTransform Kbd(Transform parent, string s, float size = 12, Color? text = null) => Chip(parent, s, size, Pad(1, 6), 4, White(0.1f), text ?? Css.text);

        /// .pad-btn { mono 12px; padding 1px 7px; radius 10; background rgba(255,255,255,.12) }
        public static RectTransform PadChip(Transform parent, string s) => Chip(parent, s, 12, Pad(1, 7), 10, White(0.12f), Css.text);

        static Color White(float a) => Css.White(a);

        // ---------------------------------------------------------------- buttons
        // a click runs outside the App's frame (uGUI event): an error is reported with the button's name
        // (WebGL builds have no stack traces)
        static Button MakeButton(RectTransform rt, Graphic target, Action onClick, string what = null)
        {
            var b = rt.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.targetGraphic = target;
            if (onClick != null)
            {
                string where = "click '" + (what ?? rt.name) + "'";
                b.onClick.AddListener(() =>
                {
                    try { onClick(); }
                    catch (Exception e) { Faults.Report(where, e); }
                });
            }
            return b;
        }

        static Image FocusOutline(RectTransform rt, float radius)
        {
            // outline: 2px solid var(--acc); outline-offset: 2px
            var o = Backing(rt, "outline", Rounded(radius + 4, 2), Css.acc, -4);
            o.transform.SetAsLastSibling();
            o.enabled = false;
            return o;
        }

        /// .btn (primary: .btn.primary)
        public static Button Btn(Transform parent, string label, Action onClick, bool primary = false, float fontSize = 16, RectOffset pad = null)
        {
            var rt = Node("btn", parent);
            var fill = Img(rt, Rounded(8), primary ? Css.acc : White(0.07f), true);
            var border = primary ? null : Backing(rt, "border", Rounded(8, 1), Css.line);
            HBox(rt, pad ?? Pad(9, 18), 0, TextAnchor.MiddleCenter);
            var line = Line(rt, label, fontSize, primary ? Css.dark : Css.text, UiFont.SemiBold, TextAnchor.MiddleCenter);
            var skin = rt.gameObject.AddComponent<Skin>();
            skin.Init(fill, border, null, primary ? Css.acc : White(0.07f), primary ? Css.accHover : White(0.14f), Css.line, Css.line);
            skin.fillF = skin.fillN;
            skin.outline = FocusOutline(rt, 8);
            skin.Apply();
            return MakeButton(rt, fill, onClick, label);
        }

        /// .mi pause menu item: <b>label</b><small>sub</small>; cls "", "primary", "warn", "bad"
        public static Button MenuItem(Transform parent, string label, string sub, string cls, Action onClick)
        {
            var rt = Node("mi", parent);
            var hit = Img(rt, null, new Color(0, 0, 0, 0), true);
            var fill = Backing(rt, "fill", Rounded(10), White(0.05f));
            var border = Backing(rt, "border", Rounded(10, 1), Css.line);
            border.transform.SetSiblingIndex(1);
            var v = VBox(rt, Pad(10, 14), 2);
            Color c = cls == "primary" ? Css.acc : cls == "warn" ? Css.warm : cls == "bad" ? Css.bad : Css.text;
            Label(rt, label, 17, c, UiFont.Bold, TextAnchor.MiddleLeft, true, "b");
            if (!string.IsNullOrEmpty(sub)) Label(rt, sub, 12, Css.mut, UiFont.Regular, TextAnchor.MiddleLeft, true, "small");
            var skin = rt.gameObject.AddComponent<Skin>();
            skin.Init(fill, border, null, White(0.05f), White(0.13f), Css.line, Css.acc);
            skin.hoverLikeFocus = true;
            skin.group = v;
            skin.padShift = 6;
            skin.Apply();
            return MakeButton(rt, hit, onClick, label);
        }

        /// .bind: label on the left, the key in a kbd on the right; preset rows have <b>label</b><small>desc</small>
        public static Button Bind(Transform parent, string label, string desc, string key, bool on, Action onClick, out RectTransform kbdHolder)
        {
            var rt = Node("bind", parent);
            var fill = Img(rt, Rounded(8), White(0.04f), true);
            var border = Backing(rt, "border", Rounded(8, 1), Css.line);
            HBox(rt, Pad(7, 10), 10, TextAnchor.MiddleLeft);
            var span = Node("span", rt);
            VBox(span, null, 0);
            LE(span, -1, -1, 1);
            if (desc != null)
            {
                Label(span, label, 14, on ? Css.acc : Css.text, UiFont.Bold);
                Label(span, desc, 11, Css.mut);
            }
            else Label(span, label, 14, Css.text);
            kbdHolder = Node("kbd-holder", rt);
            HBox(kbdHolder, null, 0, TextAnchor.MiddleRight);
            KbdFixed(kbdHolder, key, on ? Css.acc : Css.text); // .bind.on kbd { color: var(--acc) }
            var skin = rt.gameObject.AddComponent<Skin>();
            skin.Init(fill, border, null, White(0.04f), White(0.1f), Css.line, Css.acc);
            skin.hoverLikeFocus = true;
            skin.Apply();
            return MakeButton(rt, fill, onClick, label);
        }

        /// .bind kbd { min-width: 70px; text-align: center; font-size: 13px }
        public static RectTransform KbdFixed(Transform holder, string key, Color color)
        {
            var k = Kbd(holder, key, 13, color);
            LE(k, -1, -1, -1, -1, 70);
            return k;
        }

        /// .ctab (on = the shown device)
        public static Button CTab(Transform parent, string label, string small, bool on, Action onClick)
        {
            var rt = Node("ctab", parent);
            var fill = Img(rt, Rounded(18), on ? Css.acc : White(0.05f), true);
            var border = on ? null : Backing(rt, "border", Rounded(18, 1), Css.line);
            HBox(rt, Pad(5, 12), 4, TextAnchor.MiddleCenter);
            Color c = on ? Css.dark : Css.text;
            Label(rt, label, 13, c, UiFont.Regular, TextAnchor.MiddleCenter, false);
            if (small != null) Label(rt, small, 11, new Color(c.r, c.g, c.b, 0.7f), UiFont.Regular, TextAnchor.MiddleCenter, false);
            var skin = rt.gameObject.AddComponent<Skin>();
            skin.Init(fill, border, null, fill.color, fill.color, Css.line, Css.line);
            skin.outline = FocusOutline(rt, 18);
            skin.Apply();
            return MakeButton(rt, fill, onClick, label);
        }

        /// .hb HUD button (quit: .hb.quit)
        public static Button HudButton(Transform parent, string label, bool quit, Action onClick)
        {
            var rt = Node("hb", parent);
            var fill = Img(rt, Rounded(6), White(0.1f), true);
            var border = Backing(rt, "border", Rounded(6, 1), quit ? Css.Rgba(255, 93, 93, 0.5f) : Css.line);
            HBox(rt, Pad(3, 9), 0, TextAnchor.MiddleCenter);
            Color tc = quit ? Colors.Hex("#ffb3b3") : Css.text;
            var line = Line(rt, label, 12, tc, UiFont.SemiBold, TextAnchor.MiddleCenter, 3);
            var skin = rt.gameObject.AddComponent<Skin>();
            skin.Init(fill, border, null, White(0.1f), quit ? Css.Rgba(255, 93, 93, 0.3f) : White(0.22f), border.color, border.color);
            if (quit)
            {
                // .hb.quit:hover { color: #fff } (text and icon)
                var graphics = line.GetComponentsInChildren<Graphic>();
                rt.gameObject.AddComponent<HoverTint>().Init(skin, graphics, tc, Color.white);
            }
            return MakeButton(rt, fill, onClick, label);
        }

        /// .arrow ‹ ›
        public static Button Arrow(Transform parent, string label, Action onClick)
        {
            var rt = Node("arrow", parent);
            var fill = Img(rt, Rounded(6), new Color(1, 1, 1, 0), true);
            var border = Backing(rt, "border", Rounded(6, 1), Css.line);
            LE(rt, 32, 48, 0, 0, 32, 48);
            var t = Label(rt, label, 22, Css.text, UiFont.Regular, TextAnchor.MiddleCenter, false);
            Stretch(t.rectTransform, 0, 0, 0, 3);
            var skin = rt.gameObject.AddComponent<Skin>();
            skin.Init(fill, border, null, new Color(1, 1, 1, 0), White(0.1f), Css.line, Css.line);
            return MakeButton(rt, fill, onClick, label);
        }

        /// .sw colour swatch (on: white border + dark ring)
        public static Button Swatch(Transform parent, Color c, bool on, Action onClick)
        {
            var rt = Node("sw", parent);
            LE(rt, 22, 22, 0, 0, 22, 22);
            if (on) Backing(rt, "ring", Ring(26, 2), new Color(0, 0, 0, 0x88 / 255f), -2);
            var fill = Img(rt, Circle(22), c, true);
            if (on) Backing(rt, "border", Ring(22, 2), Color.white).transform.SetAsLastSibling();
            return MakeButton(rt, fill, onClick);
        }

        /// .linkbtn: underlined amber text
        public static Button LinkBtn(Transform parent, string label, Action onClick)
        {
            var rt = Node("linkbtn", parent);
            var hit = Img(rt, null, new Color(0, 0, 0, 0), true);
            HBox(rt, Pad(0, 0, 0, 6), 0, TextAnchor.MiddleLeft);
            var t = Label(rt, label, 12, Css.acc, UiFont.Regular, TextAnchor.MiddleLeft, false);
            Rule(t.transform, Css.acc, 1, 0.12f);
            return MakeButton(rt, hit, onClick, label);
        }

        // ---------------------------------------------------------------- form controls
        static DefaultControls.Resources FormResources() => new DefaultControls.Resources
        {
            standard = Rounded(6), background = Rounded(6), inputField = Rounded(6), knob = Circle(16),
            checkmark = Icon("check"), dropdown = Icon("caret"), mask = Rounded(6),
        };

        static void FontAll(GameObject go, float size, Color c)
        {
            foreach (var t in go.GetComponentsInChildren<Text>(true))
            {
                t.font = GetFont(UiFont.Regular);
                t.fontSize = Mathf.RoundToInt(size);
                t.color = c;
            }
        }

        /// <select> (.slot select, .opts select): background #0b2738, 1px border, radius 6, padding 6px 8px, 14px
        public static Dropdown Select(Transform parent, IList<string> options, int value, Action<int> onChange)
        {
            var go = DefaultControls.CreateDropdown(FormResources());
            go.layer = Layer;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var dd = go.GetComponent<Dropdown>();
            FontAll(go, 14, Css.text);
            var bg = go.GetComponent<Image>();
            bg.color = Css.field;
            Backing(rt, "border", Rounded(6, 1), Css.line);
            dd.transition = Selectable.Transition.ColorTint;
            var cb = dd.colors; cb.normalColor = Color.white; cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f); cb.pressedColor = Color.white; cb.selectedColor = Color.white; cb.disabledColor = new Color(1, 1, 1, 0.5f); cb.colorMultiplier = 1; dd.colors = cb;
            dd.navigation = new Navigation { mode = Navigation.Mode.None };
            var cap = dd.captionText.rectTransform;
            cap.offsetMin = new Vector2(8, 4); cap.offsetMax = new Vector2(-24, -4);
            var arrow = rt.Find("Arrow") as RectTransform;
            if (arrow != null) { arrow.sizeDelta = new Vector2(12, 12); arrow.anchoredPosition = new Vector2(-12, 0); arrow.GetComponent<Image>().color = Css.mut; }
            // the drop-down list
            var tmpl = dd.template;
            tmpl.GetComponent<Image>().color = Css.field;
            Backing(tmpl, "border", Rounded(6, 1), Css.line).transform.SetAsLastSibling();
            var item = tmpl.GetComponentInChildren<Toggle>(true);
            if (item != null)
            {
                ((RectTransform)item.transform).sizeDelta = new Vector2(0, 26);
                var ic = item.colors; ic.normalColor = Css.field; ic.highlightedColor = Colors.Hex("#17425c"); ic.pressedColor = Colors.Hex("#1d5373"); ic.selectedColor = Colors.Hex("#17425c"); ic.colorMultiplier = 1; item.colors = ic;
                if (item.targetGraphic != null) item.targetGraphic.color = Color.white;
                if (item.graphic != null) item.graphic.color = Css.acc;
                item.navigation = new Navigation { mode = Navigation.Mode.None };
            }
            var sb = tmpl.GetComponentInChildren<Scrollbar>(true);
            if (sb != null)
            {
                sb.GetComponent<Image>().color = new Color(0, 0, 0, 0.25f);
                if (sb.handleRect != null) sb.handleRect.GetComponent<Image>().color = White(0.3f);
                sb.navigation = new Navigation { mode = Navigation.Mode.None };
            }
            dd.options.Clear();
            foreach (var o in options) dd.options.Add(new Dropdown.OptionData(o));
            tmpl.sizeDelta = new Vector2(0, Mathf.Min(options.Count * 26 + 8, 240));
            dd.SetValueWithoutNotify(Mathf.Clamp(value, 0, options.Count - 1));
            dd.RefreshShownValue();
            if (onChange != null) dd.onValueChanged.AddListener(i => onChange(i));
            LE(rt, -1, 32, -1, -1, 60, 32);
            return dd;
        }

        /// <input> (.slot input)
        public static InputField TextField(Transform parent, string value, int maxLength, bool enabled, Action<string> onInput)
        {
            var go = DefaultControls.CreateInputField(FormResources());
            go.layer = Layer;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var f = go.GetComponent<InputField>();
            FontAll(go, 14, Css.text);
            go.GetComponent<Image>().color = Css.field;
            Backing(rt, "border", Rounded(6, 1), Css.line);
            f.transition = Selectable.Transition.ColorTint;
            var cb = f.colors; cb.normalColor = Color.white; cb.highlightedColor = Color.white; cb.selectedColor = Color.white; cb.pressedColor = Color.white; cb.disabledColor = new Color(1, 1, 1, 0.55f); cb.colorMultiplier = 1; f.colors = cb;
            f.navigation = new Navigation { mode = Navigation.Mode.None };
            f.characterLimit = maxLength;
            f.lineType = InputField.LineType.SingleLine;
            f.selectionColor = new Color(Css.blue.r, Css.blue.g, Css.blue.b, 0.45f);
            f.caretColor = Css.text;
            f.customCaretColor = true;
            if (f.placeholder != null) ((Text)f.placeholder).text = "";
            f.textComponent.rectTransform.offsetMin = new Vector2(8, 4);
            f.textComponent.rectTransform.offsetMax = new Vector2(-8, -4);
            f.textComponent.supportRichText = false;
            f.SetTextWithoutNotify(value);
            f.interactable = enabled;
            if (onInput != null) f.onValueChanged.AddListener(s => onInput(s));
            LE(rt, -1, 32, -1, -1, 0, 32);
            return f;
        }

        /// <label class="chk"><input type="checkbox"> text</label>
        public static Toggle Check(Transform parent, string label, bool on, Action<bool> onChange, float size = 15)
        {
            var rt = Node("chk", parent);
            var hit = Img(rt, null, new Color(0, 0, 0, 0), true);
            HBox(rt, null, 8, TextAnchor.MiddleLeft);
            var box = Box(rt, "box", Css.field, 3, Css.mut);
            LE(box, 16, 16, 0, 0, 16, 16);
            var mark = Node("mark", box);
            Stretch(mark, 2, 2, 2, 2);
            var markImg = Img(mark, Icon("check"), Css.acc);
            Label(rt, label, size, Css.text, UiFont.Regular, TextAnchor.MiddleLeft, false);
            var t = rt.gameObject.AddComponent<Toggle>();
            t.transition = Selectable.Transition.None;
            t.navigation = new Navigation { mode = Navigation.Mode.None };
            t.targetGraphic = hit;
            t.graphic = markImg;
            t.toggleTransition = Toggle.ToggleTransition.None;
            t.SetIsOnWithoutNotify(on);
            markImg.canvasRenderer.SetAlpha(on ? 1 : 0);
            if (onChange != null) t.onValueChanged.AddListener(v => onChange(v));
            return t;
        }

        // ---------------------------------------------------------------- panel + scrolling
        /// .scroll { overflow: auto } with a VerticalLayoutGroup content
        public static RectTransform ScrollView(Transform parent, RectOffset pad, float gap, out RectTransform content, out ScrollRect sr)
        {
            var root = Node("scroll", parent);
            sr = root.gameObject.AddComponent<ScrollRect>();
            var vp = Node("viewport", root);
            Stretch(vp);
            vp.gameObject.AddComponent<RectMask2D>();
            Img(vp, null, new Color(0, 0, 0, 0), true); // catches the wheel / drag
            vp.gameObject.AddComponent<LayoutPass>();
            content = Node("content", vp);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = new Vector2(0, 0); content.anchoredPosition = Vector2.zero;
            VBox(content, pad, gap);
            Fit(content, false, true);
            // thin scrollbar on the right
            var bar = Node("scrollbar", root);
            bar.anchorMin = new Vector2(1, 0); bar.anchorMax = new Vector2(1, 1); bar.pivot = new Vector2(1, 0.5f);
            bar.sizeDelta = new Vector2(6, -8); bar.anchoredPosition = new Vector2(-3, 0);
            var track = Img(bar, Rounded(3), White(0.04f), true);
            var area = Node("area", bar); Stretch(area);
            var handle = Node("handle", area); Stretch(handle);
            var hImg = Img(handle, Rounded(3), White(0.22f), true);
            var sb = bar.gameObject.AddComponent<Scrollbar>();
            sb.handleRect = handle; sb.targetGraphic = hImg;
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.transition = Selectable.Transition.None;
            sb.navigation = new Navigation { mode = Navigation.Mode.None };
            sr.content = content; sr.viewport = vp;
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 40;
            sr.inertia = true;
            sr.verticalScrollbar = sb;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return root;
        }

        /// .panel (maxWidth: 1180 menus / controls, 1000 overlays); header is a wrapping flex row
        public static PanelParts Panel(Transform parent, float maxWidth, bool withHeader, RectOffset bodyPad = null, float bodyGap = 0)
        {
            var P = new PanelParts();
            var panel = Box(parent, "panel", Css.panel, 14, Css.line, 1, true);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(maxWidth, 400);
            DropShadow(panel, 14, 60, 20, new Color(0, 0, 0, 0.5f));
            P.panel = panel;
            P.layout = panel.gameObject.AddComponent<PanelLayout>();
            P.layout.maxWidth = maxWidth;
            if (withHeader)
            {
                P.header = Node("header", panel);
                var f = P.header.gameObject.AddComponent<Flow>();
                f.padding = Pad(14, 18);
                f.hgap = 12; f.vgap = 8;
                f.childAlignment = TextAnchor.MiddleLeft;
                P.headerRule = Img(Rule(P.header, Css.line, 1, 0).transform, null, Css.line);
                P.headerRule.rectTransform.pivot = new Vector2(0.5f, 0);
            }
            P.scroll = ScrollView(panel, bodyPad ?? Pad(4, 18, 18, 18), bodyGap, out P.content, out P.scrollRect);
            P.layout.header = P.header;
            P.layout.scroll = P.scroll;
            P.layout.content = P.content;
            return P;
        }

        /// header h2 (24px bold)
        public static Text H2(Transform parent, string s) => Label(parent, s, 24, Css.text, UiFont.Bold, TextAnchor.MiddleLeft, false, "h2");

        /// .panel h3 { margin: 18px 0 10px; font-size: 18px; color: var(--acc) }
        public static Text H3(Transform parent, string s, float top = 18, float bottom = 10)
        {
            var holder = Node("h3", parent);
            VBox(holder, Pad((int)top, 0, (int)bottom, 0));
            return Label(holder, s, 18, Css.acc, UiFont.Bold);
        }

        /// h4 (bold, default 16px)
        public static Text H4(Transform parent, string s, Color? c = null, float top = 0, float bottom = 6, float size = 16)
        {
            var holder = Node("h4", parent);
            VBox(holder, Pad((int)top, 0, (int)bottom, 0));
            return Label(holder, s, size, c ?? Css.text, UiFont.Bold);
        }

        /// flexible spacer in a Flow / HBox (.sp { flex: 1 })
        public static RectTransform Spacer(Transform parent)
        {
            var rt = Node("sp", parent);
            LE(rt, 0, 0, 1);
            return rt;
        }

        /// an empty vertical gap (margins between blocks)
        public static RectTransform Gap(Transform parent, float h)
        {
            var rt = Node("gap", parent);
            LE(rt, 0, h, -1, -1, 0, h);
            return rt;
        }

        /// the DUBURI logo on a white rounded plate (.hdr-logo, .hud-clock img)
        public static RectTransform Logo(Transform parent, float height, float radius)
        {
            var tex = Art.Logo;
            float aspect = tex != null && tex.height > 0 ? tex.width / (float)tex.height : 2.4f;
            var rt = Node("logo", parent);
            Img(rt, Rounded(radius), Color.white);
            var inner = Node("img", rt);
            Stretch(inner, 1, 1, 1, 1);
            Raw(inner, tex);
            LE(rt, height * aspect, height, 0, 0, height * aspect, height);
            rt.sizeDelta = new Vector2(height * aspect, height);
            return rt;
        }
    }

    /// colour change of a button's text / icons on hover (.hb.quit:hover)
    public sealed class HoverTint : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        Graphic[] graphics;
        Color normal, hover;
        public void Init(Skin s, Graphic[] g, Color n, Color h) { graphics = g; normal = n; hover = h; }
        void Set(Color c)
        {
            if (graphics == null) return;
            for (int i = 0; i < graphics.Length; i++) if (graphics[i] != null) graphics[i].color = c;
        }
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => Set(hover);
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => Set(normal);
    }
}
