// BG Launcher - redes sociales con sus logos y barra de progreso.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace BGLauncher
{
    public partial class LauncherForm
    {
        readonly Dictionary<string, Image> iconCache = new Dictionary<string, Image>();
        static readonly Dictionary<string, string> SocialNames = new Dictionary<string, string>
        {
            { "whatsapp", "WhatsApp" }, { "discord", "Discord" }, { "tiktok", "TikTok" }, { "facebook", "Facebook" },
            { "youtube", "YouTube" }, { "instagram", "Instagram" }, { "telegram", "Telegram" }
        };

        Image SocialIcon(string type)
        {
            Image img;
            if (iconCache.TryGetValue(type, out img)) return img;
            string[] d;
            if (!Icons.Data.TryGetValue(type, out d)) return null;
            img = Image.FromStream(new MemoryStream(Convert.FromBase64String(d[1])));
            iconCache[type] = img;
            return img;
        }

        // Fila de redes sociales arriba a la derecha (debajo del estado del servidor)
        void PaintSocials(Graphics g)
        {
            var list = new List<Social>();
            foreach (var s in cfg.socials ?? new List<Social>())
                if (s != null && !string.IsNullOrEmpty(s.url) && Icons.Data.ContainsKey((s.type ?? "").ToLowerInvariant())) list.Add(s);
            if (list.Count == 0) return;

            const int size = 34, gap = 8;
            int xr = W - 24, y = 62;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var s = list[i];
                string type = s.type.ToLowerInvariant();
                bool placed = s.x >= 0 && s.y >= 0;   // colocado a mano en el Publicador
                var r = placed ? new Rectangle(Math.Min(s.x, W - size), Math.Min(s.y, H - size), size, size) : new Rectangle(xr - size, y, size, size);
                bool hot = hover != null && hover.r == r;
                Color brand = ColorTranslator.FromHtml(Icons.Data[type][0]);
                if (hot) brand = ControlPaint.Light(brand, .25f);
                using (var b = new SolidBrush(brand)) g.FillEllipse(b, r);
                using (var pen = new Pen(Color.FromArgb(hot ? 200 : 70, 255, 255, 255), 1.2f)) g.DrawEllipse(pen, r);
                var img = SocialIcon(type);
                if (img != null) g.DrawImage(img, new Rectangle(r.X + 8, r.Y + 8, size - 16, size - 16));
                if (hot)
                {
                    string name = SocialNames.ContainsKey(type) ? SocialNames[type] : type;
                    var sz = g.MeasureString(name, fSmall);
                    var tr = new RectangleF(r.X + size / 2f - sz.Width / 2f - 8, r.Bottom + 6, sz.Width + 16, 20);
                    using (var p = Round(Rectangle.Round(tr), 10)) using (var b = new SolidBrush(Color.FromArgb(220, 13, 18, 25))) g.FillPath(b, p);
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString(name, fSmall, Brushes.White, tr, sf);
                }
                string url = s.url;
                zones.Add(new Zone(r, delegate { Open(url); }, null));
                if (!placed) xr -= size + gap;
            }
        }

        // Barra de progreso enmarcada, con degradado, brillo y porcentaje
        void PaintProgress(Graphics g, Rectangle pr)
        {
            int rad = pr.Height / 2;
            // Marco
            using (var p = Round(pr, rad))
            {
                using (var b = new LinearGradientBrush(pr, Color.FromArgb(210, 8, 11, 16), Color.FromArgb(210, 20, 27, 38), 90f)) g.FillPath(b, p);
                using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1f)) g.DrawPath(pen, p);
            }
            var inner = new Rectangle(pr.X + 3, pr.Y + 3, pr.Width - 6, pr.Height - 6);
            int irad = inner.Height / 2;
            bool ready = state == St.Ready;
            Color c1 = ready ? Color.FromArgb(22, 163, 74) : accent2;
            Color c2 = ready ? Color.FromArgb(74, 222, 128) : ControlPaint.Light(accent2, .45f);
            long ticks = DateTime.Now.Ticks / 10000;   // milisegundos

            if (progress > 0)
            {
                int w = Math.Max(inner.Height, (int)(inner.Width * Math.Min(1, progress)));
                var fill = new Rectangle(inner.X, inner.Y, w, inner.Height);
                using (var p = Round(fill, irad))
                {
                    // Resplandor
                    using (var glow = new Pen(Color.FromArgb(70, c2), 4f)) g.DrawPath(glow, p);
                    using (var b = new LinearGradientBrush(fill, c1, c2, 0f)) g.FillPath(b, p);
                    // Brillo superior
                    var top = new Rectangle(fill.X, fill.Y, fill.Width, fill.Height / 2);
                    using (var b = new SolidBrush(Color.FromArgb(55, 255, 255, 255))) { var old = g.Clip; g.SetClip(p, CombineMode.Intersect); g.FillRectangle(b, top); g.Clip = old; }
                    // Reflejo que se mueve mientras descarga
                    if (state == St.Downloading)
                    {
                        int sx = fill.X + (int)(ticks / 4 % (fill.Width + 120)) - 120;
                        var sh = new Rectangle(sx, fill.Y, 120, fill.Height);
                        using (var b = new LinearGradientBrush(new Rectangle(sx - 1, fill.Y, 122, fill.Height), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 0f))
                        {
                            var blend = new ColorBlend { Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) }, Positions = new[] { 0f, .5f, 1f } };
                            b.InterpolationColors = blend;
                            var old = g.Clip; g.SetClip(p, CombineMode.Intersect); g.FillRectangle(b, sh); g.Clip = old;
                        }
                    }
                }
                // Porcentaje
                if (!ready)
                {
                    string pct = ((int)Math.Round(Math.Min(1, progress) * 100)) + " %";
                    var sz = g.MeasureString(pct, fH);
                    using (var b = new SolidBrush(c2)) g.DrawString(pct, fH, b, pr.Right - sz.Width + 4, pr.Y - sz.Height - 2);
                }
                else
                {
                    var sz = g.MeasureString("✓", fH);
                    using (var b = new SolidBrush(c2)) g.DrawString("✓", fH, b, pr.Right - sz.Width + 2, pr.Y - sz.Height - 2);
                }
            }
            else if (state == St.Busy || state == St.Checking)
            {
                // Indeterminado: un tramo que va y viene
                int seg = 140, span = inner.Width - seg;
                double t = (ticks % 1800) / 1800.0;
                double pos = t < .5 ? t * 2 : 2 - t * 2;
                var fill = new Rectangle(inner.X + (int)(span * pos), inner.Y, seg, inner.Height);
                using (var p = Round(fill, irad))
                using (var b = new LinearGradientBrush(new Rectangle(fill.X - 1, fill.Y, fill.Width + 2, fill.Height), Color.Transparent, Color.Transparent, 0f))
                {
                    b.InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(0, c1), c2, Color.FromArgb(0, c1) }, Positions = new[] { 0f, .5f, 1f } };
                    g.FillPath(b, p);
                }
            }
        }
    }
}
