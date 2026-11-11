// W-Guard - custom controls (buttons, sidebar, header)
// © 2026 WAEL - https://github.com/waelapp
// W-Guard is free software, licensed under the GNU GPL v3.0 - see LICENSE. The name "W-Guard" and its logo belong to WAEL.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WGuard
{
    static class Glyph
    {
        public const string Home = "", Drive = "", History = "", Game = "", Apps = "",
            Shield = "", Pulse = "", Broom = "", Tiles = "", Settings = "", Info = "",
            Search = "", Refresh = "", Delete = "", Download = "", Check = "", Warning = "",
            Error = "", Undo = "", Save = "", Folder = "", Globe = "", Play = "", Stop = "";
    }

    // themed message window (light or dark), with no system sound
    static class Msg
    {
        public static DialogResult Show(IWin32Window owner, string text, string title, MessageBoxButtons b, MessageBoxIcon i)
        {
            return Open(owner, new MsgForm(text, title, b == MessageBoxButtons.YesNo, i == MessageBoxIcon.Error ? MsgForm.Kind.Error : i == MessageBoxIcon.Warning ? MsgForm.Kind.Warning : i == MessageBoxIcon.Question ? MsgForm.Kind.Question : MsgForm.Kind.Info, null));
        }
        public static void Info(IWin32Window owner, string text) { Show(owner, text, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information); }
        public static void Success(IWin32Window owner, string text) { Open(owner, new MsgForm(text, "", false, MsgForm.Kind.Success, null)); }
        // a short message with the details folded under "Show details"
        public static void Problem(IWin32Window owner, string text, string title, string details) { Open(owner, new MsgForm(text, title, false, MsgForm.Kind.Warning, details)); }
        static DialogResult Open(IWin32Window owner, MsgForm f) { using (f) return owner == null ? f.ShowDialog() : f.ShowDialog(owner); }
    }

    class MsgForm : Form
    {
        public enum Kind { Info, Success, Warning, Error, Question }
        readonly string title, text, details;
        readonly Kind kind;
        readonly bool yesNo;
        const int W = 460, Pad = 26;
        Rectangle textRect, titleRect, iconRect, toggleRect, detailsRect;
        bool expanded, hoverToggle;
        FlatButton yes, no;

        public MsgForm(string text, string title, bool yesNo, Kind kind, string details)
        {
            this.text = text ?? ""; this.title = title ?? ""; this.kind = kind; this.yesNo = yesNo; this.details = details ?? "";
            if (this.title == AppInfo.Name || this.title.Length == 0)
                this.title = kind == Kind.Success ? L.T("تم بنجاح", "Done") : kind == Kind.Error ? L.T("حدث خطأ", "Something went wrong") : kind == Kind.Warning ? L.T("تنبيه", "Heads up") : kind == Kind.Question ? L.T("تأكيد", "Please confirm") : AppInfo.Name;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Theme.Card;
            Font = Theme.Body;
            KeyPreview = true;
            Text = AppInfo.Name;

            yes = new FlatButton(yesNo ? L.T("نعم، متابعة", "Yes, continue") : L.T("حسناً", "OK"), kind == Kind.Error ? FlatButton.Kind.Danger : FlatButton.Kind.Primary, "");
            yes.Click += (s, e) => { DialogResult = yesNo ? DialogResult.Yes : DialogResult.OK; Close(); };
            Controls.Add(yes);
            if (yesNo)
            {
                no = new FlatButton(L.T("لا", "No"), FlatButton.Kind.Secondary, "");
                no.Click += (s, e) => { DialogResult = DialogResult.No; Close(); };
                Controls.Add(no);
            }
            Layout2();

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { DialogResult = yesNo ? DialogResult.No : DialogResult.OK; Close(); }
                else if (e.KeyCode == Keys.Enter) { DialogResult = yesNo ? (ActiveControl == no ? DialogResult.No : DialogResult.Yes) : DialogResult.OK; Close(); }
                else if (e.Control && e.KeyCode == Keys.C) { try { Clipboard.SetText(this.title + "\n" + this.text + (this.details.Length > 0 ? "\n\n" + this.details : "")); } catch { } }
            };
            MouseMove += (s, e) => { bool h = this.details.Length > 0 && MR(toggleRect).Contains(e.Location); if (h != hoverToggle) { hoverToggle = h; Cursor = h ? Cursors.Hand : Cursors.Default; Invalidate(); } };
            MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                if (this.details.Length > 0 && MR(toggleRect).Contains(e.Location)) { expanded = !expanded; Layout2(); Invalidate(); return; }
                // a borderless window can still be dragged by its body
                Native.ReleaseCapture(); Native.SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
            };
        }

        void Layout2()
        {
            int tx = Pad + 56, tw = W - tx - Pad;
            int th = TextRenderer.MeasureText(title, Theme.H2, new Size(tw, 0), TextFormatFlags.WordBreak).Height;
            int bh = text.Length == 0 ? 0 : TextRenderer.MeasureText(text, Theme.Body, new Size(tw, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            iconRect = new Rectangle(Pad, Pad, 42, 42);
            titleRect = new Rectangle(tx, Pad + 2, tw, th);
            textRect = new Rectangle(tx, titleRect.Bottom + 6, tw, bh);
            int y = Math.Max(iconRect.Bottom, textRect.Bottom) + 16;
            if (details.Length > 0)
            {
                toggleRect = new Rectangle(tx, y, 170, 24);
                y = toggleRect.Bottom + 6;
                if (expanded)
                {
                    int dh = TextRenderer.MeasureText(details, Theme.Small, new Size(tw - 24, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
                    detailsRect = new Rectangle(tx, y, tw, Math.Min(dh, 260) + 20);
                    y = detailsRect.Bottom + 6;
                }
            }
            int by = y + 12;
            ClientSize = new Size(W, by + 38 + Pad);
            int x = W - Pad;
            yes.Location = new Point(x - yes.Width, by); x -= yes.Width + 10;
            if (no != null) no.Location = new Point(x - no.Width, by);
            if (L.Ar) foreach (Control c in Controls) c.Left = W - c.Left - c.Width;
            if (IsHandleCreated && Owner != null) Location = new Point(Location.X, Math.Max(0, Owner.Top + (Owner.Height - Height) / 2));
        }

        public void ExpandDetails() { if (details.Length > 0) { expanded = true; Layout2(); } }

        protected override void OnShown(EventArgs e) { base.OnShown(e); (no ?? yes).Focus(); }   // for Yes/No the safe answer is the default

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } } // drop shadow

        Rectangle MR(Rectangle r) { return Theme.M(r, ClientSize.Width); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            Color fg, bg; string glyph;
            switch (kind)
            {
                case Kind.Success: fg = Theme.Ok; bg = Theme.OkBg; glyph = ""; break;
                case Kind.Warning: fg = Theme.WarnDot; bg = Theme.WarnBg; glyph = ""; break;
                case Kind.Error: fg = Theme.Danger; bg = Theme.DangerBg; glyph = ""; break;
                case Kind.Question: fg = Theme.Primary; bg = Theme.TealTint; glyph = ""; break;
                default: fg = Theme.Primary; bg = Theme.TealTint; glyph = ""; break;
            }
            var ir = MR(iconRect);
            using (var b = new SolidBrush(bg)) g.FillEllipse(b, ir);
            TextRenderer.DrawText(g, glyph, Theme.IconBig, ir, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            Theme.Txt(g, title, Theme.H2, Theme.Text, MR(titleRect), Theme.Align.Start, false, true);
            if (text.Length > 0) Theme.Txt(g, text, Theme.Body, Theme.Muted, MR(textRect), Theme.Align.Start, false, true);
            if (details.Length > 0)
            {
                // "Show details" with a chevron; the list opens under it
                var tr = MR(toggleRect);
                string t = expanded ? L.T("إخفاء التفاصيل", "Hide details") : L.T("عرض التفاصيل", "Show details");
                string chev = expanded ? "" : "";
                int cw = 18;
                var chevR = L.Ar ? new Rectangle(tr.Right - cw, tr.Y, cw, tr.Height) : new Rectangle(tr.X, tr.Y, cw, tr.Height);
                var txtR = L.Ar ? new Rectangle(tr.X, tr.Y, tr.Width - cw - 4, tr.Height) : new Rectangle(tr.X + cw + 4, tr.Y, tr.Width - cw - 4, tr.Height);
                TextRenderer.DrawText(g, chev, Theme.IconSmall, chevR, Theme.Link, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                using (var f = new Font(Theme.Bold, hoverToggle ? FontStyle.Underline : FontStyle.Regular))
                    Theme.Txt(g, t, f, Theme.Link, txtR, Theme.Align.Start);
                if (expanded)
                {
                    var dr = MR(detailsRect);
                    Theme.FillRound(g, Theme.Bg, dr, 10);
                    Theme.StrokeRound(g, Theme.Border, dr, 10);
                    var inner = new Rectangle(dr.X + 12, dr.Y + 10, dr.Width - 24, dr.Height - 20);
                    Theme.Txt(g, details, Theme.Small, Theme.Text, inner, Theme.Align.Start, false, true);
                }
            }
        }

        protected override void OnLoad(EventArgs e) { Theme.Style(this); base.OnLoad(e); }
    }

    class FlatButton : Control
    {
        public enum Kind { Primary, Secondary, Danger, Ghost }
        public Kind Style;
        public string GlyphText;
        bool hover, down;

        public FlatButton(string text, Kind kind, string glyph)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = Theme.Bold;
            Style = kind;
            GlyphText = glyph;
            Height = 38;
            Margin = new Padding(5, 0, 5, 0);
            TabStop = true;
            Text = text;
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); FitWidth(); Invalidate(); }
        void FitWidth()
        {
            int w = TextRenderer.MeasureText(Text ?? "", Font).Width + 34 + (string.IsNullOrEmpty(GlyphText) ? 0 : 26);
            Width = Math.Max(w, 90);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty);
            base.OnKeyUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color bg, fg, border = Color.Empty;
            switch (Style)
            {
                case Kind.Primary: bg = hover ? Theme.PrimaryHover : Theme.Primary; fg = Color.White; break;
                case Kind.Danger: bg = hover ? Theme.DangerHover : Theme.Danger; fg = Color.White; break;
                case Kind.Ghost: bg = hover ? Theme.TealTint : Color.Transparent; fg = Theme.Primary; break;
                default: bg = hover ? Theme.Bg : Theme.Card; fg = Theme.Text; border = Theme.Border; break;
            }
            if (!Enabled) { bg = Style == Kind.Ghost ? Color.Transparent : Theme.Track; fg = Theme.Faint; border = Color.Empty; }
            if (down) bg = ControlPaint.Dark(bg, 0.05f);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            if (bg.A > 0) Theme.FillRound(g, bg, r, 10);
            if (!border.IsEmpty) Theme.StrokeRound(g, border, r, 10);
            if (Focused && ShowFocusCues) Theme.StrokeRound(g, Theme.Teal, new RectangleF(1.5f, 1.5f, Width - 3.5f, Height - 3.5f), 9);

            int tw = TextRenderer.MeasureText(Text ?? "", Font).Width;
            int gw = string.IsNullOrEmpty(GlyphText) ? 0 : 24;
            int total = tw + gw;
            int x0 = (Width - total) / 2;
            if (gw > 0)
            {
                var gr = Theme.M(new Rectangle(x0, 0, 20, Height), Width);
                TextRenderer.DrawText(g, GlyphText, Theme.Icon, gr, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            var tr = Theme.M(new Rectangle(x0 + gw, 0, tw + 2, Height), Width);
            Theme.Txt(g, Text, Font, fg, tr, Theme.Align.Center);
        }
    }

    // ------------------------------------------------------------------ sidebar
    class NavItem
    {
        public string Id, Glyph, Text, Badge = "";
        public Color BadgeColor = Theme.Danger;
        public bool GapBefore;
        public NavItem(string id, string glyph, string text) { Id = id; Glyph = glyph; Text = text; }
    }

    class NavBar : Control
    {
        public readonly List<NavItem> Items = new List<NavItem>();
        public int Selected;
        int hover = -1;
        bool linkHover;
        public event Action<string> Navigate;
        const int HeaderH = 92, ItemH = 42, Gap = 3, Pad = 12;
        static readonly Font BrandFont = new Font("Segoe UI Semibold", 14f);
        static readonly Font LinkFont = new Font("Segoe UI", 9f, FontStyle.Underline);

        public NavBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Navy;
            Width = 236;
        }

        Rectangle ItemRect(int i)
        {
            int y = HeaderH;
            for (int k = 0; k <= i; k++) { if (Items[k].GapBefore) y += 18; if (k < i) y += ItemH + Gap; }
            return new Rectangle(Pad, y, Width - Pad * 2, ItemH);
        }
        Rectangle LinkRect { get { return Theme.M(new Rectangle(22, Height - 34, Width - 44, 20), Width); } }

        public void Select(string id)
        {
            for (int i = 0; i < Items.Count; i++) if (Items[i].Id == id) { Selected = i; Invalidate(); return; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int W = Width;
            Logo.Draw(g, Theme.M(new Rectangle(22, 26, 40, 40), W), Theme.Navy2);
            Theme.Txt(g, AppInfo.Name, BrandFont, Color.White, Theme.M(new Rectangle(70, 24, W - 84, 26), W), Theme.Align.Start);
            Theme.Txt(g, L.T("الإصدار ", "Version ") + AppInfo.VersionText, Theme.Small, Theme.Faint, Theme.M(new Rectangle(70, 48, W - 84, 20), W), Theme.Align.Start);
            using (var pen = new Pen(Theme.NavyLine)) g.DrawLine(pen, 18, HeaderH - 10, W - 18, HeaderH - 10);

            for (int i = 0; i < Items.Count; i++)
            {
                var it = Items[i];
                var r = ItemRect(i);
                if (it.GapBefore) using (var pen = new Pen(Theme.NavyLine)) g.DrawLine(pen, 18, r.Y - 10, W - 18, r.Y - 10);
                bool sel = i == Selected, hov = i == hover;
                if (sel) Theme.FillRound(g, Theme.Navy2, r, 10);
                else if (hov) Theme.FillRound(g, Color.FromArgb(22, 255, 255, 255), r, 10);
                if (sel) Theme.FillRound(g, Theme.Teal, Theme.M(new Rectangle(r.X, r.Y + 11, 3, r.Height - 22), W), 1.5f);
                Color fg = sel ? Color.White : Theme.NavText;
                TextRenderer.DrawText(g, it.Glyph, Theme.Icon, Theme.M(new Rectangle(r.X + 14, r.Y, 24, r.Height), W), sel ? Theme.Teal : Theme.Faint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                int bw = 0;
                if (!string.IsNullOrEmpty(it.Badge))
                {
                    bw = Math.Max(22, TextRenderer.MeasureText(it.Badge, Theme.Small).Width + 8);
                    var br = Theme.M(new Rectangle(r.Right - 12 - bw, r.Y + 11, bw, 20), W);
                    Theme.FillRound(g, it.BadgeColor, br, 10);
                    TextRenderer.DrawText(g, it.Badge, Theme.Small, br, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                Theme.Txt(g, it.Text, sel ? Theme.Bold : Theme.Body, fg, Theme.M(new Rectangle(r.X + 46, r.Y, r.Width - 46 - 16 - bw, r.Height), W), Theme.Align.Start);
            }

            using (var pen = new Pen(Theme.NavyLine)) g.DrawLine(pen, 18, Height - 92, W - 18, Height - 92);
            Theme.Txt(g, "© " + AppInfo.Year + " " + AppInfo.Owner, Theme.Bold, Theme.NavText, Theme.M(new Rectangle(22, Height - 82, W - 44, 22), W), Theme.Align.Start);
            Theme.Txt(g, L.T("مفتوح المصدر · GPL-3.0", "Open source · GPL-3.0"), Theme.Small, Theme.Faint, Theme.M(new Rectangle(22, Height - 60, W - 44, 20), W), Theme.Align.Start);
            var lr = LinkRect;
            TextRenderer.DrawText(g, AppInfo.LinkText, linkHover ? LinkFont : Theme.Small, lr, Theme.Teal,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | (L.Ar ? TextFormatFlags.Right : TextFormatFlags.Left));
        }

        int HitItem(Point p)
        {
            for (int i = 0; i < Items.Count; i++) if (ItemRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitItem(e.Location);
            bool lh = LinkRect.Contains(e.Location);
            if (h != hover || lh != linkHover) { hover = h; linkHover = lh; Invalidate(); }
            Cursor = (h >= 0 || lh) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; linkHover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (LinkRect.Contains(e.Location)) { U.Open(AppInfo.Link); return; }
            int h = HitItem(e.Location);
            if (h >= 0) { Selected = h; Invalidate(); if (Navigate != null) Navigate(Items[h].Id); }
            base.OnMouseClick(e);
        }
    }

    // ------------------------------------------------------------------ page header
    class HeaderBar : Panel
    {
        public string Title = "", Subtitle = "";
        public readonly FlowLayoutPanel Actions;

        public HeaderBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 92;
            BackColor = Theme.Bg;
            Padding = new Padding(22, 26, 22, 0);
            Actions = new FlowLayoutPanel
            {
                Dock = L.Ar ? DockStyle.Left : DockStyle.Right,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Padding = new Padding(0)
            };
            Controls.Add(Actions);
        }

        public void Set(string title, string sub, Control[] actions)
        {
            Title = title; Subtitle = sub;
            Actions.SuspendLayout();
            Actions.Controls.Clear();
            if (actions != null) foreach (var c in actions) Actions.Controls.Add(c);
            Actions.ResumeLayout();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int aw = Actions.Width + 30;
            int w = Width - 56 - aw;
            Theme.Txt(e.Graphics, Title, Theme.Title, Theme.Text, Theme.M(new Rectangle(28, 18, w, 34), Width), Theme.Align.Start);
            Theme.Txt(e.Graphics, Subtitle, Theme.Body, Theme.Muted, Theme.M(new Rectangle(28, 54, w, 24), Width), Theme.Align.Start);
        }
    }

    interface IPage
    {
        string Title { get; }
        string Subtitle { get; }
        Control[] HeaderActions { get; }
        void OnShown();
    }
}
