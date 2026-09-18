using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;

namespace IPBanFrontend
{
    sealed class TrendChartPanel : Panel
    {
        static readonly Color Grid = Color.FromArgb(226, 232, 240);
        static readonly Color Axis = Color.FromArgb(148, 163, 184);
        static readonly Color Below = Color.FromArgb(13, 148, 136);
        static readonly Color Above = Color.FromArgb(194, 65, 12);
        static readonly Color AvgLine = Color.FromArgb(15, 118, 110);
        static readonly Color Muted = Color.FromArgb(100, 116, 139);
        static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

        List<TrendPoint> _points = new List<TrendPoint>();
        double _avg;
        string _yTitle = "Pogingen";
        TrendGrain _grain = TrendGrain.Day;
        int _hover = -1;
        readonly ToolTip _tip = new ToolTip { InitialDelay = 80, ReshowDelay = 80 };

        public TrendChartPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            ResizeRedraw = true;
            Padding = new Padding(52, 26, 18, 36);
            MouseMove += OnMove;
            MouseLeave += (s, e) =>
            {
                if (_hover < 0) return;
                _hover = -1;
                _tip.Hide(this);
                Invalidate();
            };
        }

        public void SetData(IList<TrendPoint> points, double average, string yTitle, TrendGrain grain)
        {
            _points = points != null ? new List<TrendPoint>(points) : new List<TrendPoint>();
            _avg = average;
            _yTitle = yTitle ?? "Pogingen";
            _grain = grain;
            _hover = -1;
            Invalidate();
        }

        public void SavePng(string path)
        {
            var w = Math.Max(900, Width);
            var h = Math.Max(420, Height);
            using (var bmp = RenderBitmap(_points, _avg, _yTitle, _grain, w, h))
                bmp.Save(path, ImageFormat.Png);
        }

        public static Bitmap RenderBitmap(IList<TrendPoint> points, double average, string yTitle,
            TrendGrain grain, int width, int height)
        {
            var bmp = new Bitmap(Math.Max(400, width), Math.Max(240, height));
            using (var g = Graphics.FromImage(bmp))
                PaintChart(g, bmp.Width, bmp.Height, points ?? new List<TrendPoint>(),
                    average, yTitle ?? "Pogingen", grain, -1);
            return bmp;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintChart(e.Graphics, Width, Height, _points, _avg, _yTitle, _grain, _hover);
        }

        static void PaintChart(Graphics g, int width, int height, IList<TrendPoint> points,
            double avg, string yTitle, TrendGrain grain, int hover)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Color.White);

            var pad = new Padding(52, 26, 18, 36);
            var plot = new RectangleF(
                pad.Left,
                pad.Top,
                Math.Max(1, width - pad.Horizontal),
                Math.Max(1, height - pad.Vertical));

            using (var border = new Pen(Grid))
                g.DrawRectangle(border, 0, 0, width - 1, height - 1);

            if (plot.Width < 20 || plot.Height < 20)
                return;

            using (var axisFont = new Font("Segoe UI", 8.25f))
            using (var titleFont = new Font("Segoe UI Semibold", 8.5f))
            using (var muted = new SolidBrush(Muted))
            using (var gridPen = new Pen(Grid))
            using (var axisPen = new Pen(Axis))
            {
                var max = 1.0;
                foreach (var p in points)
                    if (p.Value > max) max = p.Value;
                if (avg > max) max = avg;
                max = NiceMax(max);

                for (var i = 0; i <= 4; i++)
                {
                    var y = plot.Bottom - (float)(plot.Height * i / 4.0);
                    g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
                    var label = ((int)(max * i / 4.0)).ToString("n0", Nl);
                    var sz = g.MeasureString(label, axisFont);
                    g.DrawString(label, axisFont, muted, plot.Left - sz.Width - 6, y - sz.Height / 2);
                }

                g.DrawLine(axisPen, plot.Left, plot.Top, plot.Left, plot.Bottom);
                g.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
                g.DrawString(yTitle, titleFont, muted, 8, 4);

                if (points.Count == 0)
                {
                    var empty = "Nog geen data";
                    var es = g.MeasureString(empty, titleFont);
                    g.DrawString(empty, titleFont, muted,
                        plot.Left + (plot.Width - es.Width) / 2,
                        plot.Top + (plot.Height - es.Height) / 2);
                    return;
                }

                var n = points.Count;
                var slot = plot.Width / (float)n;
                var barW = Math.Max(2f, slot * 0.62f);

                for (var i = 0; i < n; i++)
                {
                    var p = points[i];
                    var h = (float)(plot.Height * (p.Value / max));
                    var x = plot.Left + i * slot + (slot - barW) / 2;
                    var y = plot.Bottom - h;
                    var color = p.Value <= avg ? Below : Above;
                    if (i == hover) color = ControlPaint.Dark(color);
                    using (var br = new SolidBrush(color))
                        g.FillRectangle(br, x, y, barW, Math.Max(1f, h));
                }

                var avgY = plot.Bottom - (float)(plot.Height * (avg / max));
                using (var avgPen = new Pen(AvgLine, 2f) { DashStyle = DashStyle.Dash })
                    g.DrawLine(avgPen, plot.Left, avgY, plot.Right, avgY);
                var avgLbl = "gem. " + avg.ToString("0.0", Nl);
                using (var avgBr = new SolidBrush(AvgLine))
                {
                    var asz = g.MeasureString(avgLbl, axisFont);
                    g.DrawString(avgLbl, axisFont, avgBr,
                        plot.Right - asz.Width, Math.Max(plot.Top, avgY - asz.Height - 2));
                }

                DrawAxisDates(g, axisFont, muted, axisPen, plot, points, grain, slot);

                using (var legendFont = new Font("Segoe UI", 8f))
                using (var b1 = new SolidBrush(Below))
                using (var b2 = new SolidBrush(Above))
                {
                    var lx = plot.Right - 228;
                    g.FillRectangle(b1, lx, 6, 10, 10);
                    g.DrawString("onder gemiddelde", legendFont, muted, lx + 14, 3);
                    g.FillRectangle(b2, lx + 124, 6, 10, 10);
                    g.DrawString("erboven", legendFont, muted, lx + 138, 3);
                }
            }
        }

        static void DrawAxisDates(Graphics g, Font font, Brush muted, Pen axisPen,
            RectangleF plot, IList<TrendPoint> points, TrendGrain grain, float slot)
        {
            var n = points.Count;
            var ticks = PreferredTicks(points, grain);
            float lastRight = plot.Left - 8;

            using (var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Near,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                foreach (var i in ticks)
                {
                    if (i < 0 || i >= n) continue;
                    var text = AxisLabel(points[i], grain);
                    if (string.IsNullOrEmpty(text)) continue;
                    var sz = g.MeasureString(text, font);
                    var cx = plot.Left + i * slot + slot / 2;
                    var left = cx - sz.Width / 2;
                    var right = cx + sz.Width / 2;
                    if (left < lastRight + 8)
                        continue;
                    if (right > plot.Right + 4)
                        continue;

                    g.DrawLine(axisPen, cx, plot.Bottom, cx, plot.Bottom + 4);
                    g.DrawString(text, font, muted, new RectangleF(left, plot.Bottom + 5, sz.Width + 1, sz.Height + 1), sf);
                    lastRight = right;
                }
            }
        }

        static List<int> PreferredTicks(IList<TrendPoint> points, TrendGrain grain)
        {
            var n = points.Count;
            var ticks = new List<int>();
            if (n == 0) return ticks;
            ticks.Add(0);
            for (var i = 1; i < n - 1; i++)
            {
                var t = points[i].Start;
                    var want = false;
                switch (grain)
                {
                    case TrendGrain.Hour:
                        want = t.Hour % 6 == 0;
                        break;
                    case TrendGrain.Day:
                        want = t.Day == 1 || t.DayOfWeek == DayOfWeek.Monday;
                        break;
                    case TrendGrain.Week:
                    case TrendGrain.Month:
                        want = true;
                        break;
                }
                if (want) ticks.Add(i);
            }
            if (n > 1) ticks.Add(n - 1);
            return ticks;
        }

        static string AxisLabel(TrendPoint p, TrendGrain grain)
        {
            var t = p.Start;
            switch (grain)
            {
                case TrendGrain.Hour:
                    return t.Hour == 0
                        ? t.ToString("d MMM", Nl)
                        : t.ToString("HH", Nl) + "u";
                case TrendGrain.Day:
                case TrendGrain.Week:
                    return t.ToString("d MMM", Nl);
                default:
                    return t.ToString("MMM yyyy", Nl);
            }
        }

        static string TipText(TrendPoint p, TrendGrain grain)
        {
            var t = p.Start;
            string when;
            switch (grain)
            {
                case TrendGrain.Hour:
                    when = t.ToString("dddd d MMMM HH:mm", Nl);
                    break;
                case TrendGrain.Week:
                    when = "week van " + t.ToString("d MMMM yyyy", Nl);
                    break;
                case TrendGrain.Month:
                    when = t.ToString("MMMM yyyy", Nl);
                    break;
                default:
                    when = t.ToString("dddd d MMMM yyyy", Nl);
                    break;
            }
            return when + ": " + p.Value.ToString("n0", Nl) + " pogingen";
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            var pad = Padding;
            var plot = new RectangleF(
                pad.Left, pad.Top,
                Math.Max(1, Width - pad.Horizontal),
                Math.Max(1, Height - pad.Vertical));
            if (_points.Count == 0 || !plot.Contains(e.Location))
            {
                if (_hover >= 0) { _hover = -1; Invalidate(); _tip.Hide(this); }
                return;
            }
            var slot = plot.Width / (float)_points.Count;
            var i = (int)((e.X - plot.Left) / slot);
            if (i < 0 || i >= _points.Count) return;
            if (i == _hover) return;
            _hover = i;
            _tip.Show(TipText(_points[i], _grain), this, e.X + 12, e.Y + 16, 2800);
            Invalidate();
        }

        static double NiceMax(double max)
        {
            if (max <= 1) return 1;
            var mag = Math.Pow(10, Math.Floor(Math.Log10(max)));
            var n = max / mag;
            var nice = n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10;
            return nice * mag;
        }
    }
}
