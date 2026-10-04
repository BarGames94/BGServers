// BG Launcher - ventana principal.
// Lee launcher.ini (dirección del hosting), descarga config.json y manifest.json,
// actualiza solo los archivos que cambiaron y arranca el juego.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("BG Launcher")]
[assembly: System.Reflection.AssemblyProduct("BG Launcher")]
[assembly: System.Reflection.AssemblyCompany("BarGames Servers")]
[assembly: System.Reflection.AssemblyVersion("1.4.2")]
[assembly: System.Reflection.AssemblyFileVersion("1.4.2")]

namespace BGLauncher
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Util.InitNet();
            // Restos de una autoactualización anterior
            try { string old = Application.ExecutablePath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
            Application.Run(new LauncherForm());
        }
    }

    class Zone
    {
        public Rectangle r; public Action click; public string tip;
        public Zone(Rectangle r, Action click, string tip) { this.r = r; this.click = click; this.tip = tip; }
    }

    enum St { Checking, Downloading, Ready, Error, Busy }

    public partial class LauncherForm : Form
    {
        const int W = 960, H = 580;
        readonly string root = Application.StartupPath;
        readonly string dataDir;
        string baseUrl = "";
        LauncherConfig cfg = new LauncherConfig();
        Image bg, logo;
        Color accent, accent2;
        readonly List<Zone> zones = new List<Zone>();
        Zone hover;
        St state = St.Checking;
        string status = "Iniciando…", detail = "";
        double progress = -1;           // -1 = sin barra
        int online = -1;                // -1 desconocido, 0 offline, 1 online
        int players = -1;               // jugadores conectados (-1 = sin dato)
        List<News> news = new List<News>();
        System.Threading.Timer pingTimer;
        readonly Font fTitle = new Font("Segoe UI Semibold", 22f), fSub = new Font("Segoe UI", 10f), fH = new Font("Segoe UI Semibold", 10.5f),
            fTxt = new Font("Segoe UI", 10f), fSmall = new Font("Segoe UI", 8.5f), fPlay = new Font("Segoe UI Black", 20f), fBtn = new Font("Segoe UI Semibold", 9.5f);

        public LauncherForm()
        {
            dataDir = Path.Combine(root, ".launcher");
            Text = "Launcher";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(W, H);
            DoubleBuffered = true;
            BackColor = Color.FromArgb(7, 9, 13);
            try { var ico = Path.Combine(root, "Main.ico"); Icon = File.Exists(ico) ? new Icon(ico) : Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            SetColors();
            // Animación de la barra mientras busca actualizaciones
            var anim = new System.Windows.Forms.Timer { Interval = 40 };
            anim.Tick += delegate { if (state == St.Busy || state == St.Checking || state == St.Downloading) Invalidate(new Rectangle(0, H - 76, 710, 56)); };
            anim.Start();
            InitPhase2();
            Load += delegate { Start(false); };
            FormClosed += delegate { if (pingTimer != null) pingTimer.Dispose(); };
        }

        void SetColors()
        {
            accent = ParseColor(cfg.accent, Color.FromArgb(245, 183, 59));
            accent2 = ParseColor(cfg.accent2, Color.FromArgb(47, 123, 255));
        }

        static Color ParseColor(string s, Color def)
        {
            try { return string.IsNullOrEmpty(s) ? def : ColorTranslator.FromHtml(s); } catch { return def; }
        }

        // ------------------------------------------------------------------ Actualización
        void Start(bool repair)
        {
            if (state == St.Downloading || (state == St.Busy)) return;
            var ini = Util.ReadIni(Path.Combine(root, "launcher.ini"), "Launcher");
            ini.TryGetValue("Url", out baseUrl);
            if (string.IsNullOrEmpty(baseUrl))
            {
                SetState(St.Error, "Falta launcher.ini", "Pon el launcher dentro de la carpeta del cliente, junto a launcher.ini.");
                return;
            }
            if (!baseUrl.EndsWith("/")) baseUrl += "/";
            SetState(St.Busy, repair ? "Revisando todos los archivos…" : "Buscando actualizaciones…", "");
            var t = new Thread(delegate () { Work(repair); });
            t.IsBackground = true;
            t.Start();
        }

        void Work(bool repair)
        {
            try
            {
                // 1. Configuración y diseño
                string cfgText = null, cfgCache = Path.Combine(dataDir, "config.json");
                try { using (var w = Util.Web()) cfgText = w.DownloadString(Util.NoCache(baseUrl + "config.json")); }
                catch { if (File.Exists(cfgCache)) cfgText = File.ReadAllText(cfgCache); else throw new Exception("No hay conexión con el servidor de actualizaciones."); }
                var c = Util.Json().Deserialize<LauncherConfig>(cfgText);
                Directory.CreateDirectory(dataDir);
                File.WriteAllText(cfgCache, cfgText);
                Image nbg = LoadImage(c.background), nlogo = LoadImage(c.logo);
                var nnews = LoadNews(c);
                Ui(delegate
                {
                    cfg = c; SetColors(); news = nnews;
                    if (nbg != null) bg = nbg;
                    if (nlogo != null) logo = nlogo;
                    Text = c.name;
                    StartPing();
                });

                // Licencia: PRO o plan gratis
                var li = License.Check(c.license, License.Host(baseUrl), dataDir, typeof(LauncherForm).Assembly.GetName().Version.ToString(3));
                Ui(delegate { lic = li; nextLevels = DateTime.MinValue; Invalidate(); });

                // 2. ¿Hay launcher nuevo?
                if (SelfUpdate(c)) return;

                // 3. Lista de archivos
                string manText;
                using (var w = Util.Web()) manText = w.DownloadString(Util.NoCache(baseUrl + "manifest.json"));
                var man = Util.Json().Deserialize<Manifest>(manText);
                var preserve = new HashSet<string>(man.preserve ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                var cache = new HashCache(Path.Combine(dataDir, "hashes.txt"));

                foreach (var d in man.delete ?? new List<string>())
                {
                    try { string full = Util.SafeLocal(root, d); if (File.Exists(full)) { File.Delete(full); cache.Forget(d); } } catch { }
                }

                var todo = new List<FileEntry>();
                int missing = 0, changed = 0, n = 0;
                foreach (var f in man.files)
                {
                    n++;
                    if (repair && n % 50 == 0) { int nn = n; Ui(delegate { progress = nn / (double)man.files.Count; detail = nn + " / " + man.files.Count; Invalidate(); }); }
                    string full = Util.SafeLocal(root, f.p);
                    if (preserve.Contains(f.p)) { if (!File.Exists(full) && f.u == 1) todo.Add(f); continue; }
                    if (f.u == 1)
                    {
                        if (cache.Get(root, f.p, repair) != f.h) todo.Add(f);
                    }
                    else if (repair)
                    {
                        string h = cache.Get(root, f.p, true);
                        if (h == null) missing++; else if (h != f.h) changed++;
                    }
                }

                // 4. Descargas
                if (todo.Count > 0)
                {
                    long total = 0, done = 0;
                    foreach (var f in todo) total += f.s;
                    int k = 0;
                    foreach (var f in todo)
                    {
                        k++;
                        string label = "Descargando " + k + " de " + todo.Count + ": " + Path.GetFileName(f.p);
                        Ui(delegate { state = St.Downloading; status = label; Invalidate(); });
                        long before = done;
                        Download(f, man.version, delegate (long got)
                        {
                            long now = before + got;
                            Ui(delegate { progress = total > 0 ? now / (double)total : 1; detail = Util.Size(now) + " / " + Util.Size(total); Invalidate(); });
                        });
                        done = before + f.s;
                        cache.Forget(f.p);
                        cache.Get(root, f.p, true);
                    }
                }
                cache.Save();

                string msg = todo.Count > 0 ? "Actualizado: " + todo.Count + " archivo(s)." : "El cliente está al día.";
                string det = "Versión " + man.version;
                if (repair && (missing > 0 || changed > 0))
                    det = (missing > 0 ? missing + " archivo(s) del cliente no están" : "") + (missing > 0 && changed > 0 ? " y " : "") +
                          (changed > 0 ? changed + " modificado(s)" : "") + ". Si el juego falla, descarga el cliente completo de nuevo.";
                CheckRequirements();
                Ui(delegate { SetState(St.Ready, msg, det); nextLevels = DateTime.MinValue; });
            }
            catch (Exception ex)
            {
                string m = ex.Message;
                Ui(delegate { SetState(St.Error, "No se pudo actualizar", m); });
            }
        }

        void Download(FileEntry f, int version, Action<long> onProgress)
        {
            string full = Util.SafeLocal(root, f.p);
            string tmp = full + ".bgdl";
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            string url = baseUrl + "files/" + Util.UrlPath(f.p) + "?v=" + version;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "BGLauncher";
            req.Timeout = 30000; req.ReadWriteTimeout = 60000;
            using (var resp = req.GetResponse())
            using (var src = resp.GetResponseStream())
            using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            {
                var buf = new byte[1 << 16]; long got = 0; int r; var last = DateTime.MinValue;
                while ((r = src.Read(buf, 0, buf.Length)) > 0)
                {
                    dst.Write(buf, 0, r); got += r;
                    if ((DateTime.Now - last).TotalMilliseconds > 120) { onProgress(got); last = DateTime.Now; }
                }
                onProgress(got);
            }
            if (Util.Md5File(tmp) != f.h) { File.Delete(tmp); throw new Exception("El archivo " + f.p + " llegó dañado. Vuelve a intentarlo."); }
            try { if (File.Exists(full)) File.Delete(full); }
            catch { File.Delete(tmp); throw new Exception("No se puede reemplazar " + f.p + ". Cierra el juego y vuelve a intentarlo."); }
            File.Move(tmp, full);
        }

        bool SelfUpdate(LauncherConfig c)
        {
            Version want;
            if (string.IsNullOrEmpty(c.launcherVersion) || !Version.TryParse(c.launcherVersion, out want)) return false;
            var have = typeof(LauncherForm).Assembly.GetName().Version;
            if (want <= have) return false;
            Ui(delegate { SetState(St.Busy, "Actualizando el launcher a la versión " + c.launcherVersion + "…", ""); });
            byte[] b;
            using (var w = Util.Web()) b = w.DownloadData(baseUrl + "launcher/Launcher.exe?v=" + c.launcherVersion);
            if (!string.IsNullOrEmpty(c.launcherMd5) && Util.Md5Bytes(b) != c.launcherMd5.ToLowerInvariant())
                throw new Exception("El launcher nuevo llegó dañado.");
            string exe = Application.ExecutablePath, old = exe + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);            // Windows deja renombrar un .exe en marcha
            File.WriteAllBytes(exe, b);
            Process.Start(exe);
            Ui(delegate { Close(); });
            return true;
        }

        Image LoadImage(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string local = Path.Combine(dataDir, "img_" + Path.GetFileName(name));
            try { using (var w = Util.Web()) w.DownloadFile(baseUrl + Util.UrlPath(name), local + ".tmp"); if (File.Exists(local)) File.Delete(local); File.Move(local + ".tmp", local); }
            catch { }
            try { if (File.Exists(local)) return Image.FromStream(new MemoryStream(File.ReadAllBytes(local))); } catch { }
            return null;
        }

        List<News> LoadNews(LauncherConfig c)
        {
            var list = new List<News>(c.news ?? new List<News>());
            if (string.IsNullOrEmpty(c.newsUrl)) return list;
            try
            {
                string txt; using (var w = Util.Web()) txt = w.DownloadString(c.newsUrl);
                var arr = Util.Json().DeserializeObject(txt) as object[];
                var wp = new List<News>();
                if (arr != null)
                    foreach (var o in arr)
                    {
                        var d = o as Dictionary<string, object>; if (d == null) continue;
                        // WordPress: title = { rendered: "..." }, link.  WebEngine (bg-launcher.php?action=news): title, url
                        object to = d.ContainsKey("title") ? d["title"] : null;
                        var t = to as Dictionary<string, object>;
                        string title = t != null ? (t.ContainsKey("rendered") ? Convert.ToString(t["rendered"]) : "") : Convert.ToString(to ?? "");
                        title = WebUtility.HtmlDecode(title);
                        if (title.Trim() == "") continue;
                        string date = d.ContainsKey("date") ? Convert.ToString(d["date"]) : "";
                        DateTime dt;
                        if (date.Contains("T") && DateTime.TryParse(date, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt)) date = dt.ToString("dd/MM/yyyy");
                        string link = d.ContainsKey("link") ? Convert.ToString(d["link"]) : (d.ContainsKey("url") ? Convert.ToString(d["url"]) : "");
                        wp.Add(new News { title = title, date = date, url = link });
                    }
                wp.AddRange(list);
                return wp;
            }
            catch { return list; }
        }

        void StartPing()
        {
            if (pingTimer != null) pingTimer.Dispose();
            if (string.IsNullOrEmpty(cfg.statusHost)) { online = -1; return; }
            string host = cfg.statusHost; int port = cfg.statusPort; string pUrl = cfg.playersUrl;
            pingTimer = new System.Threading.Timer(delegate
            {
                int ok = 0, n = -1;
                try
                {
                    using (var tc = new TcpClient())
                    {
                        var ar = tc.BeginConnect(host, port, null, null);
                        if (ar.AsyncWaitHandle.WaitOne(3000) && tc.Connected) ok = 1;
                    }
                }
                catch { }
                // Jugadores conectados (opcional: WebEngine u otra API que devuelva {"online": N})
                if (ok == 1 && !string.IsNullOrEmpty(pUrl))
                {
                    try
                    {
                        string txt; using (var w = Util.Web()) txt = w.DownloadString(pUrl);
                        var d = Util.Json().DeserializeObject(txt) as Dictionary<string, object>;
                        if (d != null && d.ContainsKey("online")) n = Convert.ToInt32(d["online"]);
                    }
                    catch { }
                }
                Ui(delegate { online = ok; players = n; Invalidate(); });
            }, null, 0, 30000);
        }

        void Ui(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch { }
        }

        void SetState(St s, string st, string det)
        {
            state = s; status = st; detail = det;
            progress = s == St.Ready ? 1 : (s == St.Error ? -1 : progress);
            Invalidate();
        }

        // ------------------------------------------------------------------ Acciones
        void Play() { StartGame(null); }

        void Options()
        {
            using (var f = new OptionsForm(cfg, root, accent, prefs))
                if (f.ShowDialog(this) == DialogResult.OK) Store.SavePrefs(dataDir, prefs);
        }

        void Repair()
        {
            if (state == St.Downloading || state == St.Busy) return;
            if (MessageBox.Show(this, "Se revisarán todos los archivos del cliente y se volverán a descargar los que estén dañados.\nPuede tardar uno o dos minutos. ¿Seguir?", cfg.name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                progress = 0; Start(true);
            }
        }

        static void Open(string url)
        {
            if (string.IsNullOrEmpty(url) || !(url.StartsWith("http://") || url.StartsWith("https://"))) return;
            try { Process.Start(url); } catch { }
        }

        // ------------------------------------------------------------------ Dibujo
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            zones.Clear();

            // Fondo: imagen a pantalla completa + degradados para que se lea el texto
            if (bg != null)
            {
                float s = Math.Max(W / (float)bg.Width, H / (float)bg.Height);
                float iw = bg.Width * s, ih = bg.Height * s;
                g.DrawImage(bg, (W - iw) / 2, (H - ih) / 2, iw, ih);
            }
            else
            {
                using (var b = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(18, 25, 38), Color.FromArgb(7, 9, 13), 60f)) g.FillRectangle(b, 0, 0, W, H);
            }
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(215, 7, 9, 13), Color.FromArgb(40, 7, 9, 13), 0f)) g.FillRectangle(b, 0, 0, W, H);
            using (var b = new LinearGradientBrush(new Rectangle(0, H - 170, W, 170), Color.FromArgb(0, 7, 9, 13), Color.FromArgb(240, 7, 9, 13), 90f)) g.FillRectangle(b, 0, H - 170, W, 170);

            // Cabecera
            int x = 32;
            if (logo != null)
            {
                float s = Math.Min(260f / logo.Width, 64f / logo.Height);
                g.DrawImage(logo, x, 24, logo.Width * s, logo.Height * s);
            }
            else
            {
                g.DrawString(cfg.name, fTitle, Brushes.White, x - 2, 20);
                if (!string.IsNullOrEmpty(cfg.subtitle)) using (var b = new SolidBrush(Color.FromArgb(170, 230, 233, 239))) g.DrawString(cfg.subtitle, fSub, b, x, 62);
            }

            // Estado del servidor
            if (online >= 0)
            {
                string t = online == 1 ? "Servidor online" + (players >= 0 && Pro ? "  ·  " + players + (players == 1 ? " jugador" : " jugadores") : "") : "Servidor offline";
                var sz = g.MeasureString(t, fBtn);
                var r = new RectangleF(W - 118 - sz.Width - 30, 22, sz.Width + 34, 28);
                using (var p = Round(Rectangle.Round(r), 14)) using (var b = new SolidBrush(Color.FromArgb(150, 13, 18, 25))) g.FillPath(b, p);
                using (var b = new SolidBrush(online == 1 ? Color.FromArgb(34, 197, 94) : Color.FromArgb(255, 59, 59))) g.FillEllipse(b, r.X + 12, r.Y + 10, 8, 8);
                g.DrawString(t, fBtn, Brushes.White, r.X + 24, r.Y + 5);
            }

            // Minimizar y cerrar
            Btn(g, new Rectangle(W - 92, 20, 32, 32), "—", false, delegate { WindowState = FormWindowState.Minimized; }, null);
            Btn(g, new Rectangle(W - 52, 20, 32, 32), "✕", false, Close, null);

            PaintSocials(g);

            // Noticias
            int ny = 120;
            using (var b = new SolidBrush(accent)) g.DrawString("NOTICIAS", fH, b, x, ny);
            ny += 28;
            if (news.Count == 0)
            {
                using (var b = new SolidBrush(Color.FromArgb(150, 230, 233, 239))) g.DrawString("No hay noticias por ahora.", fTxt, b, x, ny);
            }
            for (int i = 0; i < Math.Min(news.Count, 6); i++)
            {
                var n = news[i];
                var r = new Rectangle(x - 10, ny - 6, 400, 50);
                bool hot = hover != null && hover.r == r;
                using (var p = Round(r, 10)) using (var b = new SolidBrush(Color.FromArgb(hot ? 170 : 110, 13, 18, 25))) g.FillPath(b, p);
                using (var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    g.DrawString(n.title, fTxt, Brushes.White, new RectangleF(x, ny, 380, 20), fmt);
                using (var b = new SolidBrush(Color.FromArgb(150, 230, 233, 239))) g.DrawString(n.date, fSmall, b, x, ny + 21);
                string url = n.url;
                if (!string.IsNullOrEmpty(url)) zones.Add(new Zone(r, delegate { Open(url); }, null));
                ny += 58;
            }

            PaintAccounts(g);
            PaintRequirements(g);

            // Barra inferior: botones
            int by = H - 150;
            int bx = x;
            bx += Btn(g, new Rectangle(bx, by, 0, 34), "Opciones", true, Options, null) + 10;
            bx += Btn(g, new Rectangle(bx, by, 0, 34), "Reparar", true, Repair, null) + 10;
            foreach (var l in cfg.links ?? new List<Link>())
            {
                string u = l.url;
                bx += Btn(g, new Rectangle(bx, by, 0, 34), l.text, true, delegate { Open(u); }, null) + 10;
                if (bx > 640) break;
            }

            // Estado y progreso
            Color stc = state == St.Error ? Color.FromArgb(255, 120, 120) : Color.White;
            using (var b = new SolidBrush(stc)) g.DrawString(status, fH, b, x, H - 92);
            using (var b = new SolidBrush(Color.FromArgb(160, 230, 233, 239)))
            using (var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter })
                g.DrawString(detail, fSmall, b, new RectangleF(x, H - 68, 570, 20), fmt);
            PaintProgress(g, new Rectangle(x, H - 44, 660, 18));

            // Plan gratis: marca BGServers
            if (lic == null || !lic.IsPro)
            {
                string pb = "Powered by BGServers";
                var szb = g.MeasureString(pb, fSmall);
                var rb = new Rectangle(W - 32 - (int)szb.Width - 40, H - 136, (int)szb.Width + 40, 22);
                bool hb = hover != null && hover.r == rb;
                using (var p = Round(rb, 11)) using (var b = new SolidBrush(Color.FromArgb(hb ? 200 : 140, 13, 18, 25))) g.FillPath(b, p);
                Brand.DrawMark(g, new Rectangle(rb.X + 5, rb.Y + 3, 16, 16));
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var b = new SolidBrush(Color.FromArgb(hb ? 255 : 190, 230, 233, 239)))
                    g.DrawString(pb, fSmall, b, new Rectangle(rb.X + 20, rb.Y, rb.Width - 20, rb.Height), sf);
                zones.Add(new Zone(rb, delegate { Open("https://bargameservers.com/bg-launcher/"); }, null));
            }

            // JUGAR
            var play = new Rectangle(W - 262, H - 104, 230, 72);
            bool can = CanLaunch();
            bool hp = can && hover != null && hover.r == play;
            using (var p = Round(play, 14))
            {
                Color c1 = can ? (hp ? ControlPaint.Light(accent, .25f) : accent) : Color.FromArgb(70, 80, 95);
                using (var b = new LinearGradientBrush(play, c1, ControlPaint.Dark(c1, .12f), 90f)) g.FillPath(b, p);
            }
            string pt = state == St.Downloading ? "ACTUALIZANDO" : (can ? "JUGAR" : "ESPERA…");
            using (var fp = state == St.Downloading || !can ? new Font("Segoe UI Black", 14f) : (Font)fPlay.Clone())
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var b = new SolidBrush(can ? Color.FromArgb(20, 14, 2) : Color.FromArgb(200, 210, 220)))
                g.DrawString(pt, fp, b, play, sf);
            if (can) zones.Add(new Zone(play, Play, null));

            // Borde de la ventana
            using (var pen = new Pen(Color.FromArgb(60, 255, 255, 255))) g.DrawRectangle(pen, 0, 0, W - 1, H - 1);
        }

        // Dibuja un botón y registra su zona. Si r.Width = 0 se ajusta al texto. Devuelve el ancho.
        int Btn(Graphics g, Rectangle r, string text, bool pill, Action click, string tip)
        {
            if (r.Width == 0) r.Width = (int)g.MeasureString(text, fBtn).Width + 30;
            bool hot = hover != null && hover.r == r;
            using (var p = Round(r, pill ? r.Height / 2 : 8))
            {
                using (var b = new SolidBrush(hot ? Color.FromArgb(200, accent2) : Color.FromArgb(pill ? 150 : 0, 18, 25, 38))) g.FillPath(b, p);
                if (pill) using (var pen = new Pen(Color.FromArgb(hot ? 0 : 90, 255, 255, 255))) g.DrawPath(pen, p);
            }
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(text, fBtn, Brushes.White, r, sf);
            zones.Add(new Zone(r, click, tip));
            return r.Width;
        }

        static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = Math.Max(1, Math.Min(rad * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ------------------------------------------------------------------ Ratón
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Zone z = null;
            foreach (var q in zones) if (q.r.Contains(e.Location)) z = q;
            if (z != hover) { hover = z; Cursor = z != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { if (hover != null) { hover = null; Invalidate(); } }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            foreach (var q in zones) if (q.r.Contains(e.Location)) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 2, 0); // arrastrar la ventana
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Zone z = null;
            foreach (var q in zones) if (q.r.Contains(e.Location)) z = q;
            if (z != null && z.click != null) z.click();
        }
    }
}
