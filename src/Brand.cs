// BG Launcher - marca BarGames Servers: el icono "BG" (cuadrado azul con franja naranja), dibujado sin imágenes.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace BGLauncher
{
    static class Brand
    {
        public static readonly Color Blue = Color.FromArgb(59, 124, 255);
        public static readonly Color Orange = Color.FromArgb(245, 165, 36);
        public static readonly Color Dark = Color.FromArgb(10, 13, 19);

        // Icono BG en el cuadrado r (lo ideal: cuadrado)
        public static void DrawMark(Graphics g, Rectangle r)
        {
            var oldSmooth = g.SmoothingMode; var oldText = g.TextRenderingHint;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float rad = r.Width * 0.22f;
            using (var path = new GraphicsPath())
            {
                float d = rad * 2;
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                using (var b = new SolidBrush(Blue)) g.FillPath(b, path);
                var clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                int stripe = (int)System.Math.Round(r.Height * 0.13f);
                using (var b = new SolidBrush(Orange)) g.FillRectangle(b, r.X, r.Bottom - stripe, r.Width, stripe);
                g.Clip = clip;
            }
            using (var f = new Font("Segoe UI Black", r.Height * 0.36f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("BG", f, Brushes.White, new RectangleF(r.X, r.Y - r.Height * 0.05f, r.Width, r.Height), sf);
            g.SmoothingMode = oldSmooth; g.TextRenderingHint = oldText;
        }
    }
}
