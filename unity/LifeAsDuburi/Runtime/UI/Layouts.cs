// Layout groups for the CSS layouts uGUI does not have: flex-wrap rows (Flow),
// grid auto-fit columns with stretched rows (AutoGrid), HTML tables with
// content-sized columns (Table / TableRow), the .panel box that grows with its
// content up to the screen height and scrolls beyond it (PanelLayout), and a
// box with a fixed aspect ratio for the SVG-sized drawings (AspectBox).

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Duburi.Game
{
    static class LayoutUtil
    {
        /// decoration (backgrounds, rules): not a cell / column at all
        public static bool Ignored(Transform t)
        {
            var le = t.GetComponent<LayoutElement>();
            return le != null && le.enabled && le.ignoreLayout;
        }

        /// column count of a table row: every child that is not decoration (inactive cells are hidden columns)
        public static int Cols(Transform row)
        {
            int n = 0;
            for (int i = 0; i < row.childCount; i++) if (!Ignored(row.GetChild(i))) n++;
            return n;
        }
    }

    /// display: flex; flex-wrap: wrap; align-items: center (childAlignment picks justify-content)
    public sealed class Flow : LayoutGroup
    {
        public float hgap, vgap;
        readonly List<float> xs = new List<float>(), ws = new List<float>(), rowH = new List<float>(), rowW = new List<float>();
        readonly List<int> rowOf = new List<int>();

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            float min = 0, pref = 0;
            for (int i = 0; i < rectChildren.Count; i++)
            {
                var c = rectChildren[i];
                float w = LayoutUtility.GetPreferredWidth(c);
                min = Mathf.Max(min, Mathf.Min(w, LayoutUtility.GetMinWidth(c) > 0 ? LayoutUtility.GetMinWidth(c) : w));
                pref += w + (i > 0 ? hgap : 0);
            }
            SetLayoutInputForAxis(min + padding.horizontal, pref + padding.horizontal, -1, 0);
        }

        void Arrange()
        {
            float avail = rectTransform.rect.width - padding.horizontal;
            xs.Clear(); ws.Clear(); rowOf.Clear(); rowW.Clear();
            float x = 0; int row = 0;
            for (int i = 0; i < rectChildren.Count; i++)
            {
                float w = Mathf.Min(LayoutUtility.GetPreferredWidth(rectChildren[i]), Mathf.Max(0, avail));
                if (x > 0 && x + w > avail + 0.5f) { rowW.Add(x - hgap); row++; x = 0; }
                xs.Add(x); ws.Add(w); rowOf.Add(row);
                x += w + hgap;
            }
            rowW.Add(Mathf.Max(0, x - hgap));
            // flex-grow: children with a flexible width share what is left of their row
            int n = rectChildren.Count;
            for (int i = 0; i < n;)
            {
                int r = rowOf[i], j = i;
                float flex = 0;
                while (j < n && rowOf[j] == r) { flex += Mathf.Max(0, LayoutUtility.GetFlexibleWidth(rectChildren[j])); j++; }
                float left = avail - rowW[r];
                if (flex > 0 && left > 0)
                {
                    float shift = 0;
                    for (int k = i; k < j; k++)
                    {
                        xs[k] += shift;
                        float f = Mathf.Max(0, LayoutUtility.GetFlexibleWidth(rectChildren[k]));
                        if (f > 0) { float add = left * f / flex; ws[k] += add; shift += add; }
                    }
                    rowW[r] = avail;
                }
                i = j;
            }
        }

        public override void CalculateLayoutInputVertical()
        {
            Arrange();
            rowH.Clear();
            for (int i = 0; i < rectChildren.Count; i++)
            {
                int r = rowOf[i];
                while (rowH.Count <= r) rowH.Add(0);
                rowH[r] = Mathf.Max(rowH[r], LayoutUtility.GetPreferredHeight(rectChildren[i]));
            }
            float h = padding.vertical;
            for (int r = 0; r < rowH.Count; r++) h += rowH[r] + (r > 0 ? vgap : 0);
            SetLayoutInputForAxis(h, h, -1, 1);
        }

        public override void SetLayoutHorizontal()
        {
            Arrange();
            float avail = rectTransform.rect.width - padding.horizontal;
            float align = GetAlignmentOnAxis(0);
            for (int i = 0; i < rectChildren.Count; i++)
            {
                float off = (avail - rowW[rowOf[i]]) * align;
                SetChildAlongAxis(rectChildren[i], 0, padding.left + off + xs[i], ws[i]);
            }
        }

        public override void SetLayoutVertical()
        {
            float y = padding.top;
            int r = -1;
            for (int i = 0; i < rectChildren.Count; i++)
            {
                while (r < rowOf[i]) { if (r >= 0) y += rowH[r] + vgap; r++; }
                float h = LayoutUtility.GetPreferredHeight(rectChildren[i]);
                SetChildAlongAxis(rectChildren[i], 1, y + (rowH[r] - h) * 0.5f, h);
            }
        }
    }

    /// display: grid; grid-template-columns: repeat(auto-fit, minmax(minCell, 1fr)) — or fixed
    /// fractional columns (weights, with optional minimum widths) that collapse to one column
    /// when the screen is narrower than collapseBelow (@media max-width). Items stretch to the row height.
    public sealed class AutoGrid : LayoutGroup
    {
        public float minCell = 240, gap = 12, rowGap = -1;
        public float[] weights, minCol;
        public float collapseBelow;
        float[] colX = new float[8], colW = new float[8];
        int cols = 1;
        readonly List<float> rowH = new List<float>();

        void Columns()
        {
            float avail = Mathf.Max(0, rectTransform.rect.width - padding.horizontal);
            int n = rectChildren.Count;
            bool collapse = collapseBelow > 0 && UiRoot.ViewportWidth < collapseBelow;
            if (weights != null && !collapse)
            {
                cols = weights.Length;
                float sum = 0; foreach (var w in weights) sum += w;
                float free = avail - gap * (cols - 1);
                for (int c = 0; c < cols; c++) colW[c] = free * weights[c] / sum;
                if (minCol != null)
                {
                    // minmax(min, fr): a column below its minimum takes the minimum, the rest share what is left
                    float fixedW = 0, restWeight = 0;
                    for (int c = 0; c < cols; c++) { if (colW[c] < minCol[c]) { colW[c] = minCol[c]; fixedW += minCol[c]; } else restWeight += weights[c]; }
                    if (fixedW > 0)
                        for (int c = 0; c < cols; c++) if (colW[c] > minCol[c] || minCol[c] <= 0) colW[c] = Mathf.Max(0, (free - fixedW) * weights[c] / Mathf.Max(1e-5f, restWeight));
                }
            }
            else if (weights != null) { cols = 1; colW[0] = avail; }
            else
            {
                cols = Mathf.Max(1, Mathf.FloorToInt((avail + gap) / (minCell + gap)));
                cols = Mathf.Clamp(Mathf.Min(cols, Mathf.Max(1, n)), 1, colW.Length); // auto-fit collapses empty tracks
                float w = (avail - gap * (cols - 1)) / cols;
                for (int c = 0; c < cols; c++) colW[c] = Mathf.Max(0, w);
            }
            float x = padding.left;
            for (int c = 0; c < cols; c++) { colX[c] = x; x += colW[c] + gap; }
        }

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            float min = weights != null ? 0 : minCell;
            SetLayoutInputForAxis(min + padding.horizontal, min + padding.horizontal, 1, 0);
        }

        public override void CalculateLayoutInputVertical()
        {
            Columns();
            rowH.Clear();
            for (int i = 0; i < rectChildren.Count; i++)
            {
                int r = i / cols;
                while (rowH.Count <= r) rowH.Add(0);
                rowH[r] = Mathf.Max(rowH[r], LayoutUtility.GetPreferredHeight(rectChildren[i]));
            }
            float rg = rowGap >= 0 ? rowGap : gap;
            float h = padding.vertical;
            for (int r = 0; r < rowH.Count; r++) h += rowH[r] + (r > 0 ? rg : 0);
            SetLayoutInputForAxis(h, h, -1, 1);
        }

        public override void SetLayoutHorizontal()
        {
            Columns();
            for (int i = 0; i < rectChildren.Count; i++) { int c = i % cols; SetChildAlongAxis(rectChildren[i], 0, colX[c], colW[c]); }
        }

        public override void SetLayoutVertical()
        {
            float rg = rowGap >= 0 ? rowGap : gap;
            float y = padding.top;
            for (int r = 0; r < rowH.Count; r++)
            {
                for (int i = r * cols; i < Mathf.Min(rectChildren.Count, (r + 1) * cols); i++) SetChildAlongAxis(rectChildren[i], 1, y, rowH[r]);
                y += rowH[r] + rg;
            }
        }
    }

    /// an HTML table (auto layout): columns as wide as their widest cell, extra width shared
    /// in proportion (or given to `flexCol`), rows as tall as their tallest cell.
    /// Children are TableRows; a row's cells are its children in column order (inactive cells = hidden columns).
    public sealed class Table : LayoutGroup
    {
        public float cellPadX = 6;
        public int flexCol = -1;
        internal float[] natural = new float[0], final = new float[0];

        int ColumnCount()
        {
            int n = 0;
            for (int i = 0; i < rectChildren.Count; i++) n = Mathf.Max(n, LayoutUtil.Cols(rectChildren[i]));
            return n;
        }

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            int n = ColumnCount();
            if (natural.Length != n) { natural = new float[n]; final = new float[n]; }
            for (int c = 0; c < n; c++) natural[c] = 0;
            for (int r = 0; r < rectChildren.Count; r++)
            {
                var row = rectChildren[r];
                int c = -1;
                for (int i = 0; i < row.childCount; i++)
                {
                    var cell = row.GetChild(i);
                    if (LayoutUtil.Ignored(cell)) continue;
                    c++;
                    if (!cell.gameObject.activeSelf) continue;
                    natural[c] = Mathf.Max(natural[c], LayoutUtility.GetPreferredWidth((RectTransform)cell) + 2 * cellPadX);
                }
            }
            float sum = 0; for (int c = 0; c < n; c++) sum += natural[c];
            SetLayoutInputForAxis(Mathf.Min(sum, 200) + padding.horizontal, sum + padding.horizontal, 1, 0);
        }

        public override void SetLayoutHorizontal()
        {
            float avail = rectTransform.rect.width - padding.horizontal;
            int n = natural.Length;
            float sum = 0; for (int c = 0; c < n; c++) sum += natural[c];
            float extra = avail - sum;
            for (int c = 0; c < n; c++)
            {
                if (extra >= 0 && flexCol >= 0) final[c] = natural[c] + (c == flexCol ? extra : 0);
                else final[c] = sum > 0 ? natural[c] * avail / sum : 0;
            }
            for (int r = 0; r < rectChildren.Count; r++) SetChildAlongAxis(rectChildren[r], 0, padding.left, avail);
        }

        public override void CalculateLayoutInputVertical()
        {
            float h = padding.vertical;
            for (int r = 0; r < rectChildren.Count; r++) h += LayoutUtility.GetPreferredHeight(rectChildren[r]);
            SetLayoutInputForAxis(h, h, -1, 1);
        }

        public override void SetLayoutVertical()
        {
            float y = padding.top;
            for (int r = 0; r < rectChildren.Count; r++)
            {
                float h = LayoutUtility.GetPreferredHeight(rectChildren[r]);
                SetChildAlongAxis(rectChildren[r], 1, y, h);
                y += h;
            }
        }
    }

    public sealed class TableRow : LayoutGroup
    {
        Table table;
        Table T => table != null ? table : (table = transform.parent.GetComponent<Table>());

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            SetLayoutInputForAxis(0, 0, 1, 0);
        }

        public override void SetLayoutHorizontal()
        {
            var t = T;
            if (t == null) return;
            float x = 0;
            int c = -1;
            for (int i = 0; i < transform.childCount; i++)
            {
                var cell = transform.GetChild(i);
                if (LayoutUtil.Ignored(cell)) continue;
                c++;
                if (!cell.gameObject.activeSelf) continue;
                float w = c < t.final.Length ? t.final[c] : 0;
                SetChildAlongAxis((RectTransform)cell, 0, x + t.cellPadX, Mathf.Max(0, w - 2 * t.cellPadX));
                x += w;
            }
        }

        public override void CalculateLayoutInputVertical()
        {
            float h = 0;
            for (int c = 0; c < transform.childCount; c++)
            {
                var cell = transform.GetChild(c);
                if (LayoutUtil.Ignored(cell) || !cell.gameObject.activeSelf) continue;
                h = Mathf.Max(h, LayoutUtility.GetPreferredHeight((RectTransform)cell));
            }
            h += padding.vertical;
            SetLayoutInputForAxis(h, h, -1, 1);
        }

        public override void SetLayoutVertical()
        {
            float h = rectTransform.rect.height - padding.vertical;
            for (int c = 0; c < transform.childCount; c++)
            {
                var cell = transform.GetChild(c);
                if (LayoutUtil.Ignored(cell) || !cell.gameObject.activeSelf) continue;
                SetChildAlongAxis((RectTransform)cell, 1, padding.top, h);
            }
        }
    }

    /// .panel: width min(maxWidth, 100% - 2 margin), height = header + content up to
    /// 100vh - 2 margin; the content scrolls beyond that. Children: header (may be null) and the scroll view.
    public sealed class PanelLayout : LayoutGroup
    {
        public float maxWidth = 1180, margin = 16;
        public RectTransform header, scroll, content;
        Vector2 lastParent;

        RectTransform Parent => (RectTransform)rectTransform.parent;

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            SetLayoutInputForAxis(0, 0, 0, 0);
        }

        public override void CalculateLayoutInputVertical() => SetLayoutInputForAxis(0, 0, 0, 1);

        public override void SetLayoutHorizontal()
        {
            float w = Mathf.Max(0, Mathf.Min(maxWidth, Parent.rect.width - 2 * margin));
            m_Tracker.Add(this, rectTransform, DrivenTransformProperties.SizeDelta);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
            if (header != null) SetChildAlongAxis(header, 0, 0, w);
            if (scroll != null) SetChildAlongAxis(scroll, 0, 0, w);
        }

        public override void SetLayoutVertical()
        {
            float hh = header != null ? LayoutUtility.GetPreferredHeight(header) : 0;
            float ch = content != null ? LayoutUtility.GetPreferredHeight(content) : 0;
            float h = Mathf.Max(hh, Mathf.Min(hh + ch, Parent.rect.height - 2 * margin));
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
            if (header != null) SetChildAlongAxis(header, 1, 0, hh);
            if (scroll != null) SetChildAlongAxis(scroll, 1, hh, h - hh);
        }

        void LateUpdate()
        {
            var p = Parent.rect.size;
            if (p != lastParent) { lastParent = p; SetDirty(); }
        }
    }

    /// lets the layout pass continue below an element that is not a layout group itself
    /// (the scroll viewport), so the content is sized in the same pass as its panel
    public sealed class LayoutPass : UIBehaviour, ILayoutGroup
    {
        public void SetLayoutHorizontal() { }
        public void SetLayoutVertical() { }
    }

    /// a box whose height follows its width (an SVG viewBox drawn at width: 100%); the
    /// drawing child is designed at designW x designH units and scaled to fit
    public sealed class AspectBox : UIBehaviour, ILayoutElement
    {
        public float designW = 800, designH = 430, maxWidth = 100000;
        public RectTransform drawing;

        public void CalculateLayoutInputHorizontal() { }
        public void CalculateLayoutInputVertical() { }
        public float minWidth => 0;
        public float preferredWidth => Mathf.Min(designW, maxWidth);
        public float flexibleWidth => 1;
        public float minHeight => 0;
        public float preferredHeight => Mathf.Min(((RectTransform)transform).rect.width, maxWidth) * designH / designW;
        public float flexibleHeight => -1;
        public int layoutPriority => 1;

        protected override void OnRectTransformDimensionsChange() => Rescale();
        protected override void OnEnable() { base.OnEnable(); Rescale(); }

        public void Rescale()
        {
            if (drawing == null) return;
            var r = ((RectTransform)transform).rect;
            float k = Mathf.Max(0.01f, Mathf.Min(r.width, maxWidth) / designW);
            drawing.localScale = new Vector3(k, k, 1);
        }
    }
}
