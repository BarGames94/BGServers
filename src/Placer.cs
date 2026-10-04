// BG Publicador - editor visual para colocar los logos de redes sociales sobre el launcher.
// Arrastra cada logo a donde quieras; clic derecho = vuelve a la fila de arriba a la derecha.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace BGLauncher
{
    public class SocialPlacer : Form
    {
        const int W = 960, H = 580, IconSize = 34;
        const float S = 0.8f;                                     // escala de la vista previa
        readonly List<string> types;
        public readonly Dictionary<string, Point> Pos;            // x,y en coordenadas del launcher; (-1,-1) = fila por defecto
        readonly Image bg, logo;
        readonly Dictionary<string, Image> icons = new Dictionary<string, Image>();
        readonly Panel canvas;
        string drag; Point dragOff;

        // Zonas del launcher para orientarse
        static readonly Tuple<Rectangle, string>[] Guides =
        {
            Tuple.Create(new Rectangle(32, 24, 260, 64), "Logo"),
            Tuple.Create(new Rectangle(640, 20, 240, 32), "Estado del servidor"),
            Tuple.Create(new Rectangle(868, 20, 72, 32), "— ✕"),
            Tuple.Create(new Rectangle(22, 114, 400, 290), "Noticias"),
            Tuple.Create(new Rectangle(608, 112, 320, 220), "Mis cuentas (PRO)"),
            Tuple.Create(new Rectangle(32, 430, 640, 34), "Opciones · Reparar · Botones"),
            Tuple.Create(new Rectangle(32, 486, 660, 80), "Estado y barra de carga"),
            Tuple.Create(new Rectangle(698, 476, 230, 72), "JUGAR"),
        };

        public SocialPlacer(List<string> types, Dictionary<string, Point> current, string bgPath, string logoPath)
        {
            this.types = types;
            Pos = new Dictionary<string, Point>();
            foreach (var t in types) Pos[t] = current.ContainsKey(t) ? current[t] : new Point(-1, -1);
            bg = LoadImg(bgPath); logo = LoadImg(logoPath);
            foreach (var t in types)
            {
                string[] d;
                if (Icons.Data.TryGetValue(t, out d)) icons[t] = Image.FromStream(new MemoryStream(Convert.FromBase64String(d[1])));
            }

            Text = "Colocar redes sociales";
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new System.Drawing.Size((int)(W * S) + 24, (int)(H * S) + 100);

            Controls.Add(new Label { Text = "Arrastra cada logo a donde quieras. Clic derecho en un logo = vuelve a la fila de arriba a la derecha.", AutoSize = true, Location = new Point(12, 10) });
            canvas = new DoubleBufferedPanel { Location = new Point(12, 34), Size = new System.Drawing.Size((int)(W * S), (int)(H * S)), BackColor = Color.Black };
            canvas.Paint += PaintCanvas;
            canvas.MouseDown += Down; canvas.MouseMove += OnDrag; canvas.MouseUp += delegate { drag = null; };
            Controls.Add(canvas);

            int by = canvas.Bottom + 12;
            var reset = new Button { Text = "Todos a la fila de arriba", Location = new Point(12, by), Size = new System.Drawing.Size(190, 32) };
            reset.Click += delegate { foreach (var t in types) Pos[t] = new Point(-1, -1); canvas.Invalidate(); };
            var ok = new Button { Text = "Guardar posiciones", Location = new Point(ClientSize.Width - 290, by), Size = new System.Drawing.Size(160, 32), DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancelar", Location = new Point(ClientSize.Width - 122, by), Size = new System.Drawing.Size(110, 32), DialogResult = DialogResult.Cancel };
            Controls.Add(reset); Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            FormClosed += delegate { if (bg != null) bg.Dispose(); if (logo != null) logo.Dispose(); };
        }

        class DoubleBufferedPanel : Panel { public DoubleBufferedPanel() { DoubleBuffered = true; } }

        static Image LoadImg(string path)
        {
            try { return !string.IsNullOrEmpty(path) && File.Exists(path) ? Image.FromStream(new MemoryStream(File.ReadAllBytes(path))) : null; } catch { return null; }
        }

        // Posición real de cada logo (igual que en el launcher: los no colocados van en fila arriba a la derecha)
        Dictionary<string, Rectangle> IconRects()
        {
            var r = new Dictionary<string, Rectangle>();
            int xr = W - 24;
            for (int i = types.Count - 1; i >= 0; i--)
            {
                var p = Pos[types[i]];
                if (p.X >= 0 && p.Y >= 0) r[types[i]] = new Rectangle(p.X, p.Y, IconSize, IconSize);
                else { r[types[i]] = new Rectangle(xr - IconSize, 62, IconSize, IconSize); xr -= IconSize + 8; }
            }
            return r;
        }

        void PaintCanvas(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(S, S);
            if (bg != null)
            {
                float s = Math.Max(W / (float)bg.Width, H / (float)bg.Height);
                g.DrawImage(bg, (W - bg.Width * s) / 2, (H - bg.Height * s) / 2, bg.Width * s, bg.Height * s);
            }
            else using (var b = new SolidBrush(Color.FromArgb(18, 25, 38))) g.FillRectangle(b, 0, 0, W, H);
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(200, 7, 9, 13), Color.FromArgb(30, 7, 9, 13), 0f)) g.FillRectangle(b, 0, 0, W, H);
            if (logo != null) { float s = Math.Min(260f / logo.Width, 64f / logo.Height); g.DrawImage(logo, 32, 24, logo.Width * s, logo.Height * s); }

            using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1.5f) { DashStyle = DashStyle.Dash })
            using (var fill = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
            using (var f = new Font("Segoe UI", 10f))
                foreach (var gd in Guides)
                {
                    g.FillRectangle(fill, gd.Item1); g.DrawRectangle(pen, gd.Item1);
                    g.DrawString(gd.Item2, f, Brushes.White, gd.Item1.X + 6, gd.Item1.Y + 4);
                }

            foreach (var kv in IconRects())
            {
                Color c = ColorTranslator.FromHtml(Icons.Data[kv.Key][0]);
                if (kv.Key == drag) c = ControlPaint.Light(c, .3f);
                using (var b = new SolidBrush(c)) g.FillEllipse(b, kv.Value);
                using (var pen = new Pen(Color.FromArgb(kv.Key == drag ? 255 : 120, 255, 255, 255), kv.Key == drag ? 2.5f : 1.2f)) g.DrawEllipse(pen, kv.Value);
                Image img; if (icons.TryGetValue(kv.Key, out img)) g.DrawImage(img, new Rectangle(kv.Value.X + 8, kv.Value.Y + 8, IconSize - 16, IconSize - 16));
            }
        }

        Point ToLauncher(Point p) { return new Point((int)(p.X / S), (int)(p.Y / S)); }

        string Hit(Point lp)
        {
            foreach (var kv in IconRects()) if (kv.Value.Contains(lp)) return kv.Key;
            return null;
        }

        void Down(object sender, MouseEventArgs e)
        {
            var lp = ToLauncher(e.Location);
            string t = Hit(lp);
            if (t == null) return;
            if (e.Button == MouseButtons.Right) { Pos[t] = new Point(-1, -1); canvas.Invalidate(); return; }
            var r = IconRects()[t];
            drag = t; dragOff = new Point(lp.X - r.X, lp.Y - r.Y);
            Pos[t] = r.Location;   // a partir de ahora tiene posición propia
            canvas.Invalidate();
        }

        void OnDrag(object sender, MouseEventArgs e)
        {
            var lp = ToLauncher(e.Location);
            canvas.Cursor = drag != null || Hit(lp) != null ? Cursors.Hand : Cursors.Default;
            if (drag == null) return;
            int x = Math.Max(0, Math.Min(W - IconSize, lp.X - dragOff.X));
            int y = Math.Max(0, Math.Min(H - IconSize, lp.Y - dragOff.Y));
            Pos[drag] = new Point(x / 2 * 2, y / 2 * 2);
            canvas.Invalidate();
        }
    }
}
