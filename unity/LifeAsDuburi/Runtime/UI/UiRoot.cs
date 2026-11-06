// The one Screen Space Overlay canvas (= the browser's <div class="ui"> over the
// WebGL canvas) with its layers, the EventSystem for mouse / touch, and the
// split-screen divider lines. Keyboard and gamepad navigation are the game's own
// (Overlays / Menu read InputHub like main.js does), so uGUI navigation events are off.
//
// Units: the CanvasScaler scales a reference resolution to the screen
// (ScaleWithScreenSize, match 0.5), so one canvas unit plays the part of one CSS
// pixel; phones and tablets get a smaller reference so text stays readable.

using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Duburi.Game
{
    public sealed class UiRoot : MonoBehaviour
    {
        public static UiRoot Instance { get; private set; }

        /// canvas width in units (@media (max-width) checks)
        public static float ViewportWidth => Instance != null ? Instance.Size.x : 1600;

        public Canvas Canvas { get; private set; }
        public CanvasScaler Scaler { get; private set; }
        public RectTransform Rect { get; private set; }
        public EventSystem Events { get; private set; }

        /// layers in the browser's DOM order: menu, HUD, split lines, overlays
        public RectTransform MenuLayer { get; private set; }
        public RectTransform HudLayer { get; private set; }
        public RectTransform SplitLayer { get; private set; }
        public RectTransform OverlayLayer { get; private set; }

        /// canvas size in units
        public Vector2 Size => Rect != null ? Rect.rect.size : new Vector2(1600, 900);
        /// screen pixels per canvas unit
        public float Scale => Canvas != null && Canvas.scaleFactor > 0 ? Canvas.scaleFactor : 1;

        /// the canvas size changed (window resize, rotation); fired from LateUpdate
        public event Action Resized;

        Image splitV, splitH;
        Vector2 lastSize;
        int lastW, lastH;

        public static UiRoot Create(Transform parent = null)
        {
            var go = new GameObject("UI") { layer = Ui.Layer };
            if (parent != null) go.transform.SetParent(parent, false);
            var ui = go.AddComponent<UiRoot>();
            ui.Build();
            return ui;
        }

        void Build()
        {
            Instance = this;
            Canvas = gameObject.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 10;
            Canvas.pixelPerfect = false;
            Scaler = gameObject.AddComponent<CanvasScaler>();
            Scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            Scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            Scaler.matchWidthOrHeight = 0.5f;
            Scaler.referencePixelsPerUnit = 100;
            UpdateReference();
            gameObject.AddComponent<GraphicRaycaster>();
            Rect = (RectTransform)transform;

            // mouse / touch only: with no actions assigned the module takes DefaultInputActions in
            // OnEnable (InputSystemUIInputModule.AssignDefaultActions); move / submit / cancel are
            // not sent because sendNavigationEvents is off
            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(transform.parent, false);
                Events = es.AddComponent<EventSystem>();
                Events.sendNavigationEvents = false;
                es.AddComponent<InputSystemUIInputModule>();
            }
            else
            {
                Events = EventSystem.current;
                Events.sendNavigationEvents = false;
            }

            MenuLayer = Layer("menu");
            HudLayer = Layer("hud");
            SplitLayer = Layer("split");
            OverlayLayer = Layer("overlays");
            splitV = SplitLine(true);
            splitH = SplitLine(false);
            SetSplit(1, true);
        }

        RectTransform Layer(string name)
        {
            var rt = Ui.Node(name, transform);
            Ui.Stretch(rt);
            return rt;
        }

        // .split-v / .split-h: a 2px black line through the middle
        Image SplitLine(bool vertical)
        {
            var rt = Ui.Node(vertical ? "split-v" : "split-h", SplitLayer);
            if (vertical) { rt.anchorMin = new Vector2(0.5f, 0); rt.anchorMax = new Vector2(0.5f, 1); rt.sizeDelta = new Vector2(2, 0); }
            else { rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(1, 0.5f); rt.sizeDelta = new Vector2(0, 2); }
            rt.anchoredPosition = Vector2.zero;
            return Ui.Img(rt, null, Color.black);
        }

        /// split-screen lines for n views (main.js layoutViews): 2 views side by side when the
        /// screen is wider than tall (wide), else stacked; 3-4 views in quarters; 1 view none
        public void SetSplit(int views, bool wide)
        {
            if (splitV == null || splitH == null) return;
            splitV.enabled = views > 2 || (views == 2 && wide);
            splitH.enabled = views > 2 || (views == 2 && !wide);
        }

        /// a rect in screen pixels with a TOP-left origin (main.js view rects) -> canvas units
        public Rect ToCanvas(Rect px)
        {
            float s = Scale;
            return new Rect(px.x / s, px.y / s, px.width / s, px.height / s);
        }

        void UpdateReference()
        {
            // desktop: 1600 x 900 units; a phone (short side under ~5 in) or tablet gets a smaller
            // reference so the CSS pixel sizes stay readable
            int w = Screen.width, h = Screen.height;
            float dpi = Screen.dpi > 0 ? Screen.dpi : 96;
            float shortIn = Mathf.Min(w, h) / dpi;
            Vector2 res = new Vector2(1600, 900);
            if (Application.isMobilePlatform || shortIn < 5f)
                res = shortIn < 5f ? new Vector2(1024, 576) : new Vector2(1280, 720);
            if (Scaler.referenceResolution != res) Scaler.referenceResolution = res;
            lastW = w; lastH = h;
        }

        // Update / LateUpdate run outside the App's frame: an error is reported with its place (WebGL
        // builds have no stack traces) and does not stop the other resize listeners
        void Update()
        {
            if (Scaler == null || (Screen.width == lastW && Screen.height == lastH)) return;
            try { UpdateReference(); }
            catch (Exception e) { lastW = Screen.width; lastH = Screen.height; Faults.Report("ui reference size", e); }
        }

        void LateUpdate()
        {
            if (Rect == null) return;
            var s = Rect.rect.size;
            if (s == lastSize) return;
            lastSize = s;
            var listeners = Resized;
            if (listeners == null) return;
            foreach (var d in listeners.GetInvocationList()) // only when the size changed
            {
                var h = (Action)d;
                try { h(); }
                catch (Exception e) { Faults.Report("screen resize: " + h.Method.DeclaringType?.Name + "." + h.Method.Name, e); }
            }
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// true while the player types into a text field (menu.js: keys in an INPUT do nothing);
        /// the App / Input should not treat those keys as game keys
        public static bool TextFieldFocused
        {
            get
            {
                var es = EventSystem.current;
                var sel = es != null ? es.currentSelectedGameObject : null;
                if (sel == null) return false;
                var f = sel.GetComponent<InputField>();
                return f != null && f.isFocused;
            }
        }
    }
}
