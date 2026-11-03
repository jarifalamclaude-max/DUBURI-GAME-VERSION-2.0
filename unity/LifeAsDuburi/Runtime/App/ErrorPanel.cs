// A small on-screen error message for playtests (the browser game's "could not start" box and the
// browser console, in one): every exception logged anywhere (Application.logMessageReceived) shows
// its message and the first stack line in a red box at the bottom of the screen, with a repeat
// count, so the team can take a screenshot. Click / tap it to hide it. It has its own canvas
// (sorting order 1000) so it still shows when the game UI itself broke. The log callback only
// stores the text; the box is built and updated from Boot's LateUpdate (never inside a log call,
// which may come from the middle of a canvas rebuild).

using System;
using UnityEngine;
using UnityEngine.UI;

namespace Duburi.Game
{
    public sealed class ErrorPanel : IDisposable
    {
        const int MaxChars = 600;

        readonly Transform parent;
        GameObject go;
        Text text;
        string message;
        int count;
        bool dirty, broken, disposed;

        public ErrorPanel(Transform parent)
        {
            this.parent = parent;
            Application.logMessageReceived += OnLog;
        }

        /// the number of errors reported so far
        public int Count => count;

        void OnLog(string condition, string stackTrace, LogType type)
        {
            // exceptions, asserts, and the errors the game reports itself ("Life as Duburi: ... [in <where>]",
            // followed by the exception text)
            bool ours = type == LogType.Error && condition != null && condition.StartsWith(Prefix, StringComparison.Ordinal);
            if (type != LogType.Exception && type != LogType.Assert && !ours) return;
            try
            {
                if (ours)
                {
                    int nl = condition.IndexOf('\n');
                    string head = (nl < 0 ? condition : condition.Substring(0, nl)).Substring(Prefix.Length);
                    string at = nl < 0 ? null : FirstAtLine(condition.Substring(nl + 1));
                    Set(head + (at != null ? "\n  " + at : ""));
                    return;
                }
                string first = FirstLine(stackTrace);
                Set(condition + (first != null ? "\n  at " + first : ""));
            }
            catch (Exception) { /* never throw from a log callback */ }
        }

        /// show a message of our own (e.g. "could not start")
        public void Show(string msg) => Set(msg);

        /// the same error again (the App logs a repeating exception only once)
        public void Repeat()
        {
            count++;
            dirty = true;
        }

        void Set(string msg)
        {
            count++;
            message = msg != null && msg.Length > MaxChars ? msg.Substring(0, MaxChars) + "…" : msg;
            dirty = true;
        }

        const string Prefix = "Life as Duburi: ";

        // the first "at ..." line of an exception's text (none in WebGL builds without stack traces)
        static string FirstAtLine(string text)
        {
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("at ", StringComparison.Ordinal)) return line;
            }
            return null;
        }

        static string FirstLine(string stack)
        {
            if (string.IsNullOrEmpty(stack)) return null;
            int start = 0;
            while (start < stack.Length)
            {
                int end = stack.IndexOf('\n', start);
                if (end < 0) end = stack.Length;
                string line = stack.Substring(start, end - start).Trim();
                if (line.Length > 0) return line;
                start = end + 1;
            }
            return null;
        }

        /// apply a new message (Boot.LateUpdate)
        public void Update()
        {
            if (!dirty || disposed || broken) return;
            dirty = false;
            try
            {
                if (go == null) Build();
                text.text = "Life as Duburi hit an error" + (count > 1 ? " (" + count + "×)" : "") +
                            " — please take a screenshot for the team. Click to hide.\n" + message;
                if (!go.activeSelf) go.SetActive(true);
            }
            catch (Exception)
            {
                broken = true; // the panel itself cannot be shown: do not loop on it
            }
        }

        void Build()
        {
            go = new GameObject("error panel") { layer = Ui.Layer };
            if (parent != null) go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            var box = new GameObject("box", typeof(RectTransform)) { layer = Ui.Layer };
            var rt = (RectTransform)box.transform;
            rt.SetParent(go.transform, false);
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(12, 12);
            rt.offsetMax = new Vector2(-12, 12);
            var bg = box.AddComponent<Image>();
            bg.color = new Color(0.38f, 0.03f, 0.05f, 0.94f);
            var vl = box.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(14, 14, 10, 10);
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            var fit = box.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var button = box.AddComponent<Button>();
            button.targetGraphic = bg;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => { if (go != null) go.SetActive(false); });

            var tgo = new GameObject("text", typeof(RectTransform)) { layer = Ui.Layer };
            tgo.transform.SetParent(rt, false);
            text = tgo.AddComponent<Text>();
            Font font = null;
            try { font = Ui.GetFont(UiFont.Mono); } catch (Exception) { /* fall back below */ }
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.font = font;
            text.fontSize = 15;
            text.color = Color.white;
            text.supportRichText = false;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Application.logMessageReceived -= OnLog;
            if (go != null) UnityEngine.Object.Destroy(go);
        }
    }
}
