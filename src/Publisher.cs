// BG Publicador - herramienta del admin.
// Crea la lista de archivos del cliente, detecta lo que cambió, lo deja listo en una carpeta
// "Para subir" y (opcional) lo sube al hosting por FTP. También edita el diseño del launcher.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("BG Publicador")]
[assembly: System.Reflection.AssemblyProduct("BG Launcher")]
[assembly: System.Reflection.AssemblyCompany("BarGames Servers")]
[assembly: System.Reflection.AssemblyVersion("1.2.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.2.0")]

namespace BGLauncher
{
    public class Project
    {
        public string clientDir = "";
        public string outDir = "";
        public string publicUrl = "";
        public bool useFtp = false;
        public string ftpHost = "";
        public int ftpPort = 21;
        public string ftpUser = "";
        public string ftpPass = "";       // cifrada con la protección de Windows (solo se puede leer en este PC y usuario)
        public string ftpDir = "";
        public bool ftpTls = true;
        public string core = "Main.exe\r\nMain.dll\r\nmain.premium\r\nCamera.dll";
        public string preserve = "Settings.ini";
        public string exclude = "Launcher.exe\r\nlauncher.ini\r\n.launcher/*\r\n*.bgdl\r\nScreenShots/*\r\nLogs/*\r\n*.log\r\nMuError*";
    }

    static class PubProgram
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Util.InitNet();
            string a = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (a == "/base" || a == "/publicar")
                using (var f = new PublisherForm()) return f.RunHeadless(a == "/base");
            if (a == "/launcher")
                using (var f = new PublisherForm()) { f.headless = true; File.WriteAllText(Path.Combine(Application.StartupPath, "publicador.log"), ""); f.PublishLauncherPublic(); return f.failed ? 1 : 0; }
            // Publicador.exe /subir ruta/relativa  -> sube un archivo de "Para subir" tal cual (imágenes extra, etc.)
            if (a == "/subir" && args.Length > 1)
                using (var f = new PublisherForm()) return f.RunHeadlessUpload(args[1].Replace('\\', '/'));
            if (a == "/probarftp" || a == "/diseno")
                using (var f = new PublisherForm()) return f.RunHeadlessFtp(a == "/diseno");
            Application.Run(new PublisherForm());
            return 0;
        }
    }

    public class PublisherForm : Form
    {
        readonly string projFile = Path.Combine(Application.StartupPath, "proyecto.json");
        Project pj = new Project();
        LauncherConfig cfg = new LauncherConfig();

        TextBox tClient, tOut, tUrl, tFtpHost, tFtpPort, tFtpUser, tFtpPass, tFtpDir, tCore, tPreserve, tExclude, tLog;
        CheckBox chFtp, chTls, chClose, chAccounts;
        TextBox dLic, dName, dSub, dAccent, dAccent2, dBg, dLogo, dExe, dArgs, dHost, dPort, dLinks, dNews, dNewsUrl;
        PictureBox pBg, pLogo;
        readonly Dictionary<string, TextBox> dSocial = new Dictionary<string, TextBox>();
        Dictionary<string, Point> socialPos = new Dictionary<string, Point>();   // posición de cada logo (-1 = fila por defecto)
        ProgressBar bar;
        Label lStatus;
        readonly List<Button> actions = new List<Button>();

        public PublisherForm()
        {
            Text = "BG Publicador - Launcher";
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(900, 720);
            MinimumSize = new Size(820, 660);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
            tabs.TabPages.Add(TabProject());
            tabs.TabPages.Add(TabPublish());
            tabs.TabPages.Add(TabDesign());
            Controls.Add(tabs);

            var foot = new Panel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(10, 6, 10, 6) };
            bar = new ProgressBar { Dock = DockStyle.Right, Width = 260 };
            lStatus = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Text = "Listo." };
            foot.Controls.Add(lStatus); foot.Controls.Add(bar);
            Controls.Add(foot);

            // Cabecera con la marca BarGames Servers
            var head = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Brand.Dark };
            head.Paint += delegate(object s, PaintEventArgs e)
            {
                var g = e.Graphics;
                var hp = (Control)s;
                int m = hp.Height - 20;
                Brand.DrawMark(g, new Rectangle(14, 10, m, m));
                var tf = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
                using (var f1 = new Font("Segoe UI Black", 14f))
                using (var f2 = new Font("Segoe UI", 8.5f))
                {
                    var s1 = TextRenderer.MeasureText(g, "BG ", f1, Size.Empty, tf);
                    var s2 = TextRenderer.MeasureText(g, "Ag", f2, Size.Empty, tf);
                    int x = 14 + m + 12, y = (hp.Height - s1.Height - s2.Height - 2) / 2;
                    TextRenderer.DrawText(g, "BG ", f1, new Point(x, y), Color.White, tf);
                    TextRenderer.DrawText(g, "PUBLICADOR", f1, new Point(x + s1.Width, y), Brand.Orange, tf);
                    TextRenderer.DrawText(g, "BarGames Servers · Launcher para tu servidor de Mu Online", f2, new Point(x + 1, y + s1.Height + 2), Color.FromArgb(154, 165, 181), tf);
                }
            };
            Controls.Add(head);

            LoadProject();
            FormClosing += delegate { SaveProject(); };
        }

        // ------------------------------------------------------------------ Pestañas
        TabPage TabProject()
        {
            var p = new TabPage("1. Proyecto");
            var t = Grid(p);
            Info(t, "Configura una vez: dónde está tu cliente, dónde se prepara la copia para el hosting y cómo se sube.");
            tClient = Row(t, "Carpeta del cliente", true, "La carpeta donde está Main.exe.");
            tOut = Row(t, "Carpeta \"Para subir\"", true, "Aquí se prepara la copia exacta de lo que va al hosting.");
            tUrl = Row(t, "Dirección pública", false, "Ej.: https://tuweb.com/launcher/louis/  (la carpeta del hosting vista desde internet)");
            chFtp = CheckRow(t, "Subir automáticamente por FTP");
            tFtpHost = Row(t, "Servidor FTP", false, "Ej.: ftp.tuweb.com o la IP del hosting (lo ves en cPanel > Cuentas FTP).");
            tFtpPort = Row(t, "Puerto", false, null);
            tFtpUser = Row(t, "Usuario FTP", false, null);
            tFtpPass = Row(t, "Contraseña FTP", false, "Se guarda cifrada en este PC.");
            tFtpPass.UseSystemPasswordChar = true;
            tFtpDir = Row(t, "Carpeta en el FTP", false, "Ej.: /public_html/launcher/louis");
            chTls = CheckRow(t, "FTP seguro (TLS) - recomendado");
            var bt = Action(t, "🔌  Probar conexión FTP", delegate { Run("Probar FTP", FtpTest); });
            bt.Width = 300;
            var b = Action(t, "📥  Instalar el launcher en el cliente", InstallLauncher);
            b.Width = 300;
            return p;
        }

        TabPage TabPublish()
        {
            var p = new TabPage("2. Publicar");
            var t = Grid(p);
            Info(t, "Primera vez: \"Crear versión base\" (el cliente tal cual lo subes a Mega/MediaFire). Después, cada vez que cambies algo: \"Publicar actualización\".");
            var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            row.Controls.Add(MakeAction("① Crear versión base", delegate { Publish(true); }, 220));
            row.Controls.Add(MakeAction("② Publicar actualización", delegate { Publish(false); }, 220));
            row.Controls.Add(MakeAction("📂 Abrir \"Para subir\"", delegate { OpenDir(pj.outDir); }, 180));
            t.Controls.Add(row); t.SetColumnSpan(row, 2);
            tCore = Multi(t, "Siempre en el hosting", "Archivos que el launcher puede reparar aunque no cambien (uno por línea).", 74);
            tPreserve = Multi(t, "Del jugador", "Solo se crean si faltan; nunca se pisan (configuración del jugador).", 40);
            tExclude = Multi(t, "Ignorar", "No entran en la lista. Admite * (ej.: ScreenShots/*).", 74);
            tLog = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Height = 170, Font = new Font("Consolas", 9f), BackColor = Color.White };
            t.Controls.Add(tLog); t.SetColumnSpan(tLog, 2);
            return p;
        }

        TabPage TabDesign()
        {
            var p = new TabPage("3. Diseño");
            var t = Grid(p);
            Info(t, "Lo que cambies aquí lo ven todos los jugadores la próxima vez que abran el launcher (no hay que repartir nada).");
            dLic = Row(t, "Licencia PRO", false, "BGL-XXXX-XXXX-XXXX-XXXX. Vacío = plan gratis (con \"Powered by BGServers\").");
            dName = Row(t, "Nombre del servidor", false, null);
            dSub = Row(t, "Subtítulo", false, "Ej.: Season 6 · Exp x100");
            dAccent = Row(t, "Color principal", false, "Botón JUGAR. Formato #RRGGBB, ej. #f5b73b");
            dAccent2 = Row(t, "Color secundario", false, "Barra de progreso y botones. Ej. #2f7bff");
            dBg = ImageRow(t, "Imagen de fondo", "fondo", "JPG o PNG, ideal 960x580 o más.", out pBg);
            dLogo = ImageRow(t, "Logo (opcional)", "logo", "PNG con fondo transparente. Si no hay, se escribe el nombre.", out pLogo);
            dExe = Row(t, "Ejecutable del juego", false, null);
            dArgs = Row(t, "Parámetros", false, "Normalmente vacío.");
            chClose = CheckRow(t, "Cerrar el launcher al pulsar JUGAR");
            chAccounts = CheckRow(t, "\"Mis cuentas\": guardar cuentas y entrar con un clic (PRO)");
            Hint(t, "Usa el AutoLogin del Settings.ini del cliente (Louis y la mayoría de files Season 6). Desmárcalo si tu cliente guarda las cuentas a su manera (ej. MSPRO): el resto del launcher funciona igual.");
            dHost = Row(t, "IP del servidor", false, "Para mostrar Online/Offline (IP pública del ConnectServer).");
            dPort = Row(t, "Puerto del ConnectServer", false, "Normalmente 44405.");
            foreach (var s in new[] { "WhatsApp", "Discord", "TikTok", "Facebook", "YouTube", "Instagram", "Telegram" })
                dSocial[s.ToLowerInvariant()] = Row(t, s, false, s == "WhatsApp" ? "Redes sociales: salen con su logo. Vacío = no sale." : null);
            var place = Action(t, "🖱  Colocar redes sociales en el launcher…", PlaceSocials);
            place.Width = 320;
            dLinks = Multi(t, "Botones", "Uno por línea:  Texto | https://enlace", 60);
            dNews = Multi(t, "Noticias", "Una por línea:  Título | fecha | https://enlace", 60);
            dNewsUrl = Row(t, "Noticias automáticas", false, "WebEngine: https://tuweb.com/api/bg-launcher.php?action=news  ·  WordPress: https://tuweb.com/wp-json/wp/v2/posts?per_page=5");
            var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            row.Controls.Add(MakeAction("💾 Guardar y publicar diseño", PublishDesign, 240));
            row.Controls.Add(MakeAction("🚀 Publicar launcher nuevo", PublishLauncher, 220));
            row.Controls.Add(MakeAction("✎ Opciones avanzadas", delegate { SaveDesignLocal(); Process.Start("notepad.exe", "\"" + Path.Combine(pj.outDir, "config.json") + "\""); }, 190));
            t.Controls.Add(row); t.SetColumnSpan(row, 2);
            return p;
        }

        // ------------------------------------------------------------------ Ayudas de interfaz
        TableLayoutPanel Grid(TabPage p)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(14) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            p.Controls.Add(t);
            return t;
        }

        void Info(TableLayoutPanel t, string text)
        {
            var l = new Label { Text = text, AutoSize = true, MaximumSize = new Size(820, 0), ForeColor = Color.FromArgb(70, 80, 95), Margin = new Padding(0, 0, 0, 10) };
            t.Controls.Add(l); t.SetColumnSpan(l, 2);
        }

        TextBox Row(TableLayoutPanel t, string label, bool browse, string help)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 0) });
            var panel = new Panel { Dock = DockStyle.Fill, Height = help == null ? 30 : 48, Margin = new Padding(0, 2, 0, 2) };
            var tb = new TextBox { Location = new Point(0, 2), Width = 520, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
            panel.Controls.Add(tb);
            if (browse)
            {
                tb.Width = 470;
                var bt = new Button { Text = "…", Location = new Point(476, 0), Width = 44, Height = 27 };
                bool isFile = label.StartsWith("Imagen") || label.StartsWith("Logo");
                bt.Click += delegate
                {
                    if (isFile)
                    {
                        using (var d = new OpenFileDialog { Filter = "Imágenes|*.jpg;*.jpeg;*.png" }) if (d.ShowDialog(this) == DialogResult.OK) tb.Text = d.FileName;
                    }
                    else using (var d = new FolderBrowserDialog { SelectedPath = tb.Text }) if (d.ShowDialog(this) == DialogResult.OK) tb.Text = d.SelectedPath;
                };
                panel.Controls.Add(bt);
            }
            if (help != null) panel.Controls.Add(new Label { Text = help, AutoSize = true, Location = new Point(0, 29), ForeColor = Color.Gray, Font = new Font("Segoe UI", 8.5f) });
            t.Controls.Add(panel);
            return tb;
        }

        // Imagen del diseño: botón para elegirla, vista previa y se copia sola a "Para subir"
        TextBox ImageRow(TableLayoutPanel t, string label, string fixedName, string help, out PictureBox preview)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 8, 0, 0) });
            var panel = new Panel { Dock = DockStyle.Fill, Height = 112, Margin = new Padding(0, 2, 0, 4) };
            var pb = new PictureBox { Location = new Point(0, 2), Size = new Size(170, 102), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(18, 25, 38) };
            var tb = new TextBox { Location = new Point(182, 2), Width = 300, ReadOnly = true };
            var pick = new Button { Text = "📁  Elegir imagen…", Location = new Point(182, 32), Size = new Size(150, 30) };
            var clear = new Button { Text = "Quitar", Location = new Point(338, 32), Size = new Size(80, 30) };
            panel.Controls.Add(pb); panel.Controls.Add(tb); panel.Controls.Add(pick); panel.Controls.Add(clear);
            panel.Controls.Add(new Label { Text = help, AutoSize = true, Location = new Point(182, 70), ForeColor = Color.Gray, Font = new Font("Segoe UI", 8.5f) });
            panel.Controls.Add(new Label { Text = "Se sube al pulsar \"Guardar y publicar diseño\".", AutoSize = true, Location = new Point(182, 88), ForeColor = Color.Gray, Font = new Font("Segoe UI", 8.5f) });
            pick.Click += delegate
            {
                SaveProject();
                if (string.IsNullOrEmpty(pj.outDir)) { Err("Primero elige la carpeta \"Para subir\" en la pestaña Proyecto."); return; }
                using (var d = new OpenFileDialog { Filter = "Imágenes (JPG, PNG)|*.jpg;*.jpeg;*.png", Title = label })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    try
                    {
                        Directory.CreateDirectory(pj.outDir);
                        foreach (var old in Directory.GetFiles(pj.outDir, fixedName + ".*")) File.Delete(old);
                        string rel = fixedName + Path.GetExtension(d.FileName).ToLowerInvariant();
                        File.Copy(d.FileName, Path.Combine(pj.outDir, rel), true);
                        tb.Text = rel;
                        ShowPreview(pb, rel);
                        Status(label + " lista. Pulsa \"Guardar y publicar diseño\" para subirla.", 0);
                    }
                    catch (Exception ex) { Err(ex.Message); }
                }
            };
            clear.Click += delegate { tb.Text = ""; ShowPreview(pb, ""); };
            t.Controls.Add(panel);
            preview = pb;
            return tb;
        }

        void ShowPreview(PictureBox pb, string rel)
        {
            if (pb == null) return;
            var old = pb.Image; pb.Image = null; if (old != null) old.Dispose();
            try
            {
                if (string.IsNullOrEmpty(rel) || string.IsNullOrEmpty(pj.outDir)) return;
                string f = Path.IsPathRooted(rel) ? rel : Path.Combine(pj.outDir, rel);
                if (File.Exists(f)) pb.Image = Image.FromStream(new MemoryStream(File.ReadAllBytes(f)));
            }
            catch { }
        }

        TextBox Multi(TableLayoutPanel t, string label, string help, int h)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 8, 0, 0) });
            var panel = new Panel { Dock = DockStyle.Fill, Height = h + 22, Margin = new Padding(0, 2, 0, 2) };
            var tb = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Location = new Point(0, 2), Width = 520, Height = h };
            panel.Controls.Add(tb);
            panel.Controls.Add(new Label { Text = help, AutoSize = true, Location = new Point(0, h + 4), ForeColor = Color.Gray, Font = new Font("Segoe UI", 8.5f) });
            t.Controls.Add(panel);
            return tb;
        }

        CheckBox CheckRow(TableLayoutPanel t, string label)
        {
            t.Controls.Add(new Label());
            var c = new CheckBox { Text = label, AutoSize = true, Margin = new Padding(0, 6, 0, 6) };
            t.Controls.Add(c);
            return c;
        }

        // Texto de ayuda gris debajo de la fila anterior
        void Hint(TableLayoutPanel t, string text)
        {
            t.Controls.Add(new Label());
            t.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = Color.Gray, Font = new Font("Segoe UI", 8.5f), Margin = new Padding(18, 0, 0, 8) });
        }
        AutoLoginCfg savedAutoLogin;   // la configuración de "Mis cuentas" si se desmarca y se vuelve a marcar

        Button Action(TableLayoutPanel t, string text, Action a)
        {
            t.Controls.Add(new Label());
            var b = MakeAction(text, a, 260);
            t.Controls.Add(b);
            return b;
        }

        Button MakeAction(string text, Action a, int w)
        {
            var b = new Button { Text = text, Width = w, Height = 36, Margin = new Padding(0, 8, 10, 8) };
            b.Click += delegate { SaveProject(); a(); };
            actions.Add(b);
            return b;
        }

        void OpenDir(string d) { if (Directory.Exists(d)) Process.Start("explorer.exe", "\"" + d + "\""); }

        // ------------------------------------------------------------------ Proyecto
        void LoadProject()
        {
            try { if (File.Exists(projFile)) pj = Util.Json().Deserialize<Project>(File.ReadAllText(projFile)); } catch { }
            tClient.Text = pj.clientDir; tOut.Text = pj.outDir; tUrl.Text = pj.publicUrl;
            chFtp.Checked = pj.useFtp; tFtpHost.Text = pj.ftpHost; tFtpPort.Text = pj.ftpPort.ToString(); tFtpUser.Text = pj.ftpUser;
            tFtpPass.Text = Unprotect(pj.ftpPass); tFtpDir.Text = pj.ftpDir; chTls.Checked = pj.ftpTls;
            tCore.Text = pj.core; tPreserve.Text = pj.preserve; tExclude.Text = pj.exclude;
            LoadDesign();
        }

        void SaveProject()
        {
            pj.clientDir = tClient.Text.Trim(); pj.outDir = tOut.Text.Trim(); pj.publicUrl = tUrl.Text.Trim();
            pj.useFtp = chFtp.Checked; pj.ftpHost = tFtpHost.Text.Trim(); int port; pj.ftpPort = int.TryParse(tFtpPort.Text, out port) ? port : 21;
            pj.ftpUser = tFtpUser.Text.Trim(); pj.ftpPass = Protect(tFtpPass.Text); pj.ftpDir = tFtpDir.Text.Trim(); pj.ftpTls = chTls.Checked;
            pj.core = tCore.Text; pj.preserve = tPreserve.Text; pj.exclude = tExclude.Text;
            try { File.WriteAllText(projFile, Util.Json().Serialize(pj), new UTF8Encoding(false)); } catch { }
        }

        static string Protect(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser));
        }

        static string Unprotect(string s)
        {
            try { return string.IsNullOrEmpty(s) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }

        static List<string> Lines(string s)
        {
            var l = new List<string>();
            foreach (var x in (s ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) if (x.Trim() != "") l.Add(x.Trim().Replace('\\', '/'));
            return l;
        }

        static Regex Glob(string g)
        {
            return new Regex("^" + Regex.Escape(g).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase);
        }

        bool Check()
        {
            if (!File.Exists(Path.Combine(pj.clientDir, "Main.exe")) && !File.Exists(Path.Combine(pj.clientDir, cfg.exe ?? "Main.exe")))
            { Err("La carpeta del cliente no tiene " + (cfg.exe ?? "Main.exe") + "."); return false; }
            if (string.IsNullOrEmpty(pj.outDir)) { Err("Elige la carpeta \"Para subir\"."); return false; }
            if (Path.GetFullPath(pj.outDir).StartsWith(Path.GetFullPath(pj.clientDir), StringComparison.OrdinalIgnoreCase))
            { Err("La carpeta \"Para subir\" no puede estar dentro del cliente."); return false; }
            Directory.CreateDirectory(pj.outDir);
            return true;
        }

        // Modo sin ventana: Publicador.exe /base  o  Publicador.exe /publicar  (usa proyecto.json; deja publicador.log)
        public bool headless;
        public bool failed;
        public int RunHeadless(bool baseVersion)
        {
            headless = true;
            File.WriteAllText(Path.Combine(Application.StartupPath, "publicador.log"), "", new UTF8Encoding(false));
            Publish(baseVersion);
            return failed ? 1 : 0;
        }

        void Err(string m)
        {
            if (headless) { failed = true; Log("ERROR: " + m); return; }
            MessageBox.Show(this, m, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void Busy(bool on)
        {
            foreach (var b in actions) b.Enabled = !on;
            UseWaitCursor = on;
        }

        void Status(string s, double p)
        {
            if (headless) return;
            BeginInvoke(new Action(delegate
            {
                lStatus.Text = s;
                bar.Value = p < 0 ? 0 : Math.Min(1000, (int)(p * 1000));
                bar.Maximum = 1000;
            }));
        }

        void Log(string s)
        {
            if (headless) { File.AppendAllText(Path.Combine(Application.StartupPath, "publicador.log"), s + "\r\n", new UTF8Encoding(false)); return; }
            BeginInvoke(new Action(delegate { tLog.AppendText(s + "\r\n"); }));
        }

        void Run(string what, Action work)
        {
            if (headless)
            {
                try { work(); } catch (Exception ex) { failed = true; Log("ERROR: " + ex.Message); }
                return;
            }
            Busy(true); tLog.Clear();
            var th = new Thread(delegate ()
            {
                try { work(); }
                catch (Exception ex) { Log("ERROR: " + ex.Message); Status("Error: " + ex.Message, 0); BeginInvoke(new Action(delegate { Err(what + ":\n" + ex.Message); })); }
                finally { BeginInvoke(new Action(delegate { Busy(false); })); }
            });
            th.IsBackground = true; th.Start();
        }

        // ------------------------------------------------------------------ Publicar
        void Publish(bool baseVersion)
        {
            if (!Check()) return;
            string manPath = Path.Combine(pj.outDir, "manifest.json");
            if (baseVersion && File.Exists(manPath) && !headless &&
                MessageBox.Show(this, "Ya hay una versión publicada. Crear una versión base nueva empieza de cero: úsalo solo cuando subas un cliente completo nuevo a Mega/MediaFire.\n\n¿Seguir?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (!baseVersion && !File.Exists(manPath)) { Err("Primero crea la versión base (botón ①)."); return; }
            if (!File.Exists(Path.Combine(pj.outDir, "config.json"))) SaveDesignLocal();

            Run("Publicar", delegate
            {
                var excl = new List<Regex>(); foreach (var g in Lines(pj.exclude)) excl.Add(Glob(g));
                var core = new HashSet<string>(Lines(pj.core), StringComparer.OrdinalIgnoreCase);
                var prev = baseVersion ? null : Util.Json().Deserialize<Manifest>(File.ReadAllText(manPath));
                var prevMap = prev != null ? prev.Map() : new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);

                // 1. Recorrer el cliente
                Status("Leyendo el cliente…", 0);
                var all = new List<string>();
                foreach (var f in Directory.GetFiles(pj.clientDir, "*", SearchOption.AllDirectories))
                {
                    string rel = Util.Rel(pj.clientDir, f);
                    bool skip = false; foreach (var r in excl) if (r.IsMatch(rel)) { skip = true; break; }
                    if (!skip) all.Add(rel);
                }
                all.Sort(StringComparer.OrdinalIgnoreCase);
                Log("Archivos del cliente: " + all.Count);
                WarnAutoLogin();

                // 2. Huellas (con caché para no releer 1 GB cada vez)
                var cache = new HashCache(Path.Combine(Application.StartupPath, "huellas-cliente.txt"));
                var man = new Manifest { version = prev != null ? prev.version + 1 : 1, date = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
                man.preserve = Lines(pj.preserve);
                var upload = new List<FileEntry>();
                long upBytes = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    if (i % 40 == 0) Status("Calculando huellas " + (i + 1) + " / " + all.Count, i / (double)all.Count);
                    string rel = all[i];
                    string full = Util.SafeLocal(pj.clientDir, rel);
                    var e = new FileEntry { p = rel, s = new FileInfo(full).Length, h = cache.Get(pj.clientDir, rel, false) };
                    FileEntry old;
                    bool changed = !prevMap.TryGetValue(rel, out old) || (old.c ?? old.h) != e.h;
                    if (!changed) { e.h = old.h; e.c = old.c; e.s = old.s; }   // conserva la huella de la copia limpia (Settings.ini)
                    bool wasUp = old != null && old.u == 1 && !changed;
                    bool isCore = core.Contains(rel) || man.preserve.Contains(rel);
                    if (baseVersion) e.u = isCore ? 1 : 0;
                    else e.u = (changed || wasUp || isCore) ? 1 : 0;
                    // ¿Hay que copiarlo a "Para subir"? (nuevo, cambiado, o marcado y aún no está)
                    string dst = Util.SafeLocal(Path.Combine(pj.outDir, "files"), rel);
                    if (e.u == 1 && (changed || baseVersion || !File.Exists(dst) || new FileInfo(dst).Length != e.s))
                    {
                        if (!baseVersion && changed) Log((old == null ? "  + nuevo:      " : "  * cambiado:   ") + rel);
                        upload.Add(e); upBytes += e.s;
                    }
                    man.files.Add(e);
                }
                cache.Save();

                // 3. Borrados
                if (prev != null)
                {
                    var now = new HashSet<string>(all, StringComparer.OrdinalIgnoreCase);
                    foreach (var d in prev.delete ?? new List<string>()) if (!now.Contains(d)) man.delete.Add(d);
                    foreach (var f in prev.files) if (!now.Contains(f.p) && !man.delete.Contains(f.p)) { man.delete.Add(f.p); Log("  - borrado:     " + f.p); }
                }

                if (!baseVersion && upload.Count == 0 && prev != null && man.delete.Count == (prev.delete ?? new List<string>()).Count)
                {
                    Status("No hay cambios: el cliente es igual que la versión " + prev.version + ".", 1);
                    Log("No hay nada nuevo que publicar.");
                    return;
                }

                // 4. Copiar a "Para subir"
                int k = 0;
                foreach (var e in upload)
                {
                    k++; Status("Copiando " + k + " / " + upload.Count + ": " + e.p, k / (double)upload.Count);
                    string dst = Util.SafeLocal(Path.Combine(pj.outDir, "files"), e.p);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(Util.SafeLocal(pj.clientDir, e.p), dst, true);
                    if (man.preserve.Contains(e.p) && CleanAutoLogin(dst)) { e.c = e.h; e.h = Util.Md5File(dst); e.s = new FileInfo(dst).Length; Log("  (cuenta del AutoLogin quitada de la copia de " + e.p + ")"); }
                }
                foreach (var d in man.delete)
                {
                    try { string f = Util.SafeLocal(Path.Combine(pj.outDir, "files"), d); if (File.Exists(f)) File.Delete(f); } catch { }
                }
                File.WriteAllText(manPath, Util.Json().Serialize(man), new UTF8Encoding(false));
                Log((baseVersion ? "Versión base" : "Actualización") + " " + man.version + ": " + upload.Count + " archivo(s) para subir (" + Util.Size(upBytes) + ").");

                // 5. Subir
                if (pj.useFtp)
                {
                    var list = new List<string>();
                    foreach (var e in upload) list.Add("files/" + e.p);
                    if (baseVersion || !RemoteHasConfig()) list.AddRange(DesignFiles());
                    list.Add("manifest.json");      // siempre lo último: así nadie ve la lista antes que los archivos
                    FtpUpload(list);
                    foreach (var d in man.delete) FtpDelete("files/" + d);
                    Status("Publicada la versión " + man.version + ". Los launchers se actualizarán al abrirse.", 1);
                    Log("Listo: subido al hosting.");
                }
                else
                {
                    Status("Versión " + man.version + " preparada. Sube el contenido de \"Para subir\" a tu hosting.", 1);
                    Log("Sube a tu hosting (cPanel > Administrador de archivos) los archivos nuevos de la carpeta \"Para subir\" y, el último, manifest.json.");
                }
            });
        }

        bool RemoteHasConfig()
        {
            try { using (var w = Util.Web()) { w.DownloadString(Util.NoCache(Url("config.json"))); return true; } } catch { return false; }
        }

        string Url(string rel) { string u = pj.publicUrl; if (!u.EndsWith("/")) u += "/"; return u + Util.UrlPath(rel); }

        // La copia que va al hosting nunca lleva la cuenta guardada del admin
        static bool CleanAutoLogin(string ini)
        {
            if (!ini.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) return false;
            var d = Util.ReadIni(ini, "AutoLogin");
            string u, pw; d.TryGetValue("User", out u); d.TryGetValue("Password", out pw);
            if ((u ?? "").Trim() == "" && (pw ?? "").Trim() == "") return false;
            Util.WriteIni(ini, "AutoLogin", "User", "\"\"");
            Util.WriteIni(ini, "AutoLogin", "Password", "\"\"");
            Util.WriteIni(ini, "AutoLogin", "SaveData", "0");
            return true;
        }

        void WarnAutoLogin()
        {
            try
            {
                string ini = Path.Combine(pj.clientDir, "Settings.ini");
                var d = Util.ReadIni(ini, "AutoLogin");
                string u, pw;
                if ((d.TryGetValue("User", out u) && u.Trim() != "") || (d.TryGetValue("Password", out pw) && pw.Trim() != ""))
                    Log("⚠ AVISO: Settings.ini tiene una cuenta guardada en [AutoLogin]. Bórrala (User y Password vacíos) antes de subir el cliente o publicarlo.");
            }
            catch { }
        }

        // ------------------------------------------------------------------ Diseño
        void LoadDesign()
        {
            string f = Path.Combine(pj.outDir ?? "", "config.json");
            try { if (!string.IsNullOrEmpty(pj.outDir) && File.Exists(f)) cfg = Util.Json().Deserialize<LauncherConfig>(File.ReadAllText(f)); } catch { }
            if (cfg.resolutions == null || cfg.resolutions.Count == 0) Defaults(cfg);
            dLic.Text = cfg.license ?? "";
            dName.Text = cfg.name; dSub.Text = cfg.subtitle; dAccent.Text = cfg.accent; dAccent2.Text = cfg.accent2;
            dBg.Text = cfg.background; dLogo.Text = cfg.logo; dExe.Text = cfg.exe; dArgs.Text = cfg.args; chClose.Checked = cfg.closeOnPlay; chAccounts.Checked = cfg.autoLogin != null;
            dHost.Text = cfg.statusHost; dPort.Text = cfg.statusPort.ToString(); dNewsUrl.Text = cfg.newsUrl;
            var sb = new StringBuilder(); foreach (var l in cfg.links) sb.Append(l.text + " | " + l.url + "\r\n"); dLinks.Text = sb.ToString();
            foreach (var kv in dSocial) kv.Value.Text = "";
            socialPos = new Dictionary<string, Point>();
            foreach (var s in cfg.socials ?? new List<Social>())
                if (s != null && s.type != null && dSocial.ContainsKey(s.type.ToLowerInvariant()))
                {
                    dSocial[s.type.ToLowerInvariant()].Text = s.url;
                    socialPos[s.type.ToLowerInvariant()] = new Point(s.x, s.y);
                }
            ShowPreview(pBg, cfg.background); ShowPreview(pLogo, cfg.logo);
            sb.Clear(); foreach (var n in cfg.news) sb.Append(n.title + " | " + n.date + " | " + n.url + "\r\n"); dNews.Text = sb.ToString();
        }

        // Opciones típicas de un cliente Season 6 (MuEmu / Louis / MSPRO)
        static void Defaults(LauncherConfig c)
        {
            c.resolutions = new List<string> { "640x480", "800x600", "1024x768", "1280x1024", "1366x768", "1440x900", "1600x900", "1680x1050", "1920x1080" };
            c.languages = new List<Lang> { new Lang { code = "Spn", name = "Español" }, new Lang { code = "Eng", name = "English" }, new Lang { code = "Por", name = "Português" } };
            c.iniOptions = new List<IniOption>
            {
                new IniOption { label = "Niebla", section = "Graphics", key = "Fog" },
                new IniOption { label = "Efectos Tron", section = "Graphics", key = "TronEffects" },
                new IniOption { label = "Barra de vida de jugadores", section = "Options", key = "HealthBarPlayer" },
                new IniOption { label = "Barra de vida de monstruos", section = "Options", key = "HealthBarMonster" },
                new IniOption { label = "Ocultar alas (menos lag)", section = "AntiLag", key = "DisableWings" },
                new IniOption { label = "Ocultar efectos de skills (menos lag)", section = "AntiLag", key = "DisableSkillEffect" },
                new IniOption { label = "Ocultar brillos (menos lag)", section = "AntiLag", key = "DisableGlowEffect" },
                new IniOption { label = "Reducir memoria", section = "AntiLag", key = "ReduceMemory" }
            };
            c.requirements = new List<Requirement>
            {
                new Requirement { name = "Visual C++ 2015-2022 (x86)", regKey = @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x86", url = "https://aka.ms/vs/17/release/vc_redist.x86.exe" },
                new Requirement { name = "Visual C++ 2010 (x86)", regKey = @"SOFTWARE\Microsoft\VisualStudio\10.0\VC\VCRedist\x86", file = "msvcr100.dll",
                    url = "https://download.microsoft.com/download/1/6/5/165255E7-1014-4D0A-B094-B6A430A6BFFC/vcredist_x86.exe", args = "/passive /norestart" }
            };
            c.autoLogin = new AutoLoginCfg();
        }

        // Guarda config.json en "Para subir" y copia las imágenes. Devuelve los archivos a subir.
        List<string> SaveDesignLocal()
        {
            Directory.CreateDirectory(pj.outDir);
            cfg.license = dLic.Text.Trim().ToUpperInvariant();
            cfg.name = dName.Text.Trim(); cfg.subtitle = dSub.Text.Trim(); cfg.accent = dAccent.Text.Trim(); cfg.accent2 = dAccent2.Text.Trim();
            cfg.exe = dExe.Text.Trim() == "" ? "Main.exe" : dExe.Text.Trim(); cfg.args = dArgs.Text.Trim(); cfg.closeOnPlay = chClose.Checked;
            // "Mis cuentas": se conserva la configuración de AutoLogin que hubiera; desmarcado = sin cuentas
            if (chAccounts.Checked) { if (cfg.autoLogin == null) cfg.autoLogin = savedAutoLogin ?? new AutoLoginCfg(); }
            else { if (cfg.autoLogin != null) savedAutoLogin = cfg.autoLogin; cfg.autoLogin = null; }
            cfg.statusHost = dHost.Text.Trim(); int port; cfg.statusPort = int.TryParse(dPort.Text, out port) ? port : 44405; cfg.newsUrl = dNewsUrl.Text.Trim();
            cfg.background = Img(dBg.Text.Trim(), "fondo"); cfg.logo = Img(dLogo.Text.Trim(), "logo");
            dBg.Text = cfg.background; dLogo.Text = cfg.logo;
            cfg.links = new List<Link>();
            foreach (var l in Lines(dLinks.Text)) { var a = l.Split('|'); if (a.Length >= 2) cfg.links.Add(new Link { text = a[0].Trim(), url = a[1].Trim() }); }
            cfg.socials = new List<Social>();
            foreach (var kv in dSocial)
            {
                string u = kv.Value.Text.Trim();
                if (u == "") continue;
                Point p; if (!socialPos.TryGetValue(kv.Key, out p)) p = new Point(-1, -1);
                cfg.socials.Add(new Social { type = kv.Key, url = u.StartsWith("http") ? u : "https://" + u, x = p.X, y = p.Y });
            }
            cfg.news = new List<News>();
            foreach (var l in Lines(dNews.Text)) { var a = l.Split('|'); cfg.news.Add(new News { title = a[0].Trim(), date = a.Length > 1 ? a[1].Trim() : "", url = a.Length > 2 ? a[2].Trim() : "" }); }
            File.WriteAllText(Path.Combine(pj.outDir, "config.json"), Pretty(Util.Json().Serialize(cfg)), new UTF8Encoding(false));
            return DesignFiles();
        }

        // Editor visual: arrastrar los logos sobre una vista previa del launcher
        void PlaceSocials()
        {
            var types = new List<string>();
            foreach (var kv in dSocial) if (kv.Value.Text.Trim() != "") types.Add(kv.Key);
            if (types.Count == 0) { Err("Primero pon el enlace de al menos una red social."); return; }
            Func<string, string> abs = delegate (string v)
            {
                if (string.IsNullOrEmpty(v)) return "";
                return Path.IsPathRooted(v) ? v : Path.Combine(pj.outDir ?? "", v);
            };
            using (var f = new SocialPlacer(types, socialPos, abs(dBg.Text.Trim()), abs(dLogo.Text.Trim())))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                foreach (var kv in f.Pos) socialPos[kv.Key] = kv.Value;
                Status("Posiciones guardadas. Pulsa \"Guardar y publicar diseño\" para que lo vean los jugadores.", 0);
            }
        }

        List<string> DesignFiles()
        {
            var l = new List<string> { "config.json" };
            if (!string.IsNullOrEmpty(cfg.background)) l.Insert(0, cfg.background);
            if (!string.IsNullOrEmpty(cfg.logo)) l.Insert(0, cfg.logo);
            return l;
        }

        // Si se eligió una imagen del PC, se copia a "Para subir" con un nombre fijo
        string Img(string v, string name)
        {
            if (v == "" || !File.Exists(v)) return v;
            string ext = Path.GetExtension(v).ToLowerInvariant();
            string dst = Path.Combine(pj.outDir, name + ext);
            if (!string.Equals(Path.GetFullPath(v), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase)) File.Copy(v, dst, true);
            return name + ext;
        }

        static string Pretty(string json)
        {
            var sb = new StringBuilder(); int ind = 0; bool q = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\')) q = !q;
                if (q) { sb.Append(c); continue; }
                if (c == '{' || c == '[') { sb.Append(c).Append("\r\n").Append(new string(' ', ++ind * 2)); }
                else if (c == '}' || c == ']') { sb.Append("\r\n").Append(new string(' ', --ind * 2)).Append(c); }
                else if (c == ',') { sb.Append(c).Append("\r\n").Append(new string(' ', ind * 2)); }
                else if (c == ':') sb.Append(": ");
                else sb.Append(c);
            }
            return sb.ToString();
        }

        void PublishDesign()
        {
            if (string.IsNullOrEmpty(pj.outDir)) { Err("Primero elige la carpeta \"Para subir\" en la pestaña Proyecto."); return; }
            List<string> files;
            try { files = SaveDesignLocal(); } catch (Exception ex) { Err(ex.Message); return; }
            if (!pj.useFtp) { Status("Diseño guardado. Sube config.json (y las imágenes) de la carpeta \"Para subir\" a tu hosting.", 1); return; }
            Run("Publicar diseño", delegate { FtpUpload(files); Status("Diseño publicado.", 1); Log("Diseño publicado."); });
        }

        public void PublishLauncherPublic() { PublishLauncher(); }

        void PublishLauncher()
        {
            string exe = Path.Combine(Application.StartupPath, "Launcher.exe");
            if (!File.Exists(exe)) { Err("No encuentro Launcher.exe junto al Publicador."); return; }
            string ver = FileVersionInfo.GetVersionInfo(exe).FileVersion;
            if (!headless && MessageBox.Show(this, "Se publicará el launcher versión " + ver + ". Todos los jugadores lo recibirán al abrir el suyo. ¿Seguir?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            Directory.CreateDirectory(Path.Combine(pj.outDir, "launcher"));
            File.Copy(exe, Path.Combine(pj.outDir, "launcher", "Launcher.exe"), true);
            cfg.launcherVersion = ver; cfg.launcherMd5 = Util.Md5File(exe);
            var files = SaveDesignLocal();
            files.Insert(0, "launcher/Launcher.exe");
            if (!pj.useFtp) { Status("Launcher " + ver + " preparado. Sube launcher/Launcher.exe y después config.json.", 1); return; }
            Run("Publicar launcher", delegate { FtpUpload(files); Status("Launcher " + ver + " publicado.", 1); Log("Launcher " + ver + " publicado."); });
        }

        // ------------------------------------------------------------------ Instalar en el cliente
        void InstallLauncher()
        {
            string exe = Path.Combine(Application.StartupPath, "Launcher.exe");
            if (!File.Exists(exe)) { Err("No encuentro Launcher.exe junto al Publicador."); return; }
            if (!Directory.Exists(pj.clientDir)) { Err("Elige la carpeta del cliente."); return; }
            if (string.IsNullOrEmpty(pj.publicUrl)) { Err("Escribe la dirección pública."); return; }
            File.Copy(exe, Path.Combine(pj.clientDir, "Launcher.exe"), true);
            File.WriteAllText(Path.Combine(pj.clientDir, "launcher.ini"), "[Launcher]\r\nUrl = " + pj.publicUrl + "\r\n", new UTF8Encoding(false));
            MessageBox.Show(this, "Launcher instalado en el cliente:\n" + pj.clientDir + "\n\nLos jugadores abren Launcher.exe en vez de Main.exe.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ------------------------------------------------------------------ FTP
        FtpWebRequest Ftp(string rel, string method)
        {
            string dir = pj.ftpDir.Trim('/');
            string path = (dir == "" ? "" : dir + "/") + rel;
            var parts = path.Split('/');
            for (int i = 0; i < parts.Length; i++) parts[i] = Uri.EscapeDataString(parts[i]);
            var r = (FtpWebRequest)WebRequest.Create("ftp://" + pj.ftpHost + ":" + pj.ftpPort + "/" + string.Join("/", parts));
            r.Method = method;
            r.Credentials = new NetworkCredential(pj.ftpUser, Unprotect(pj.ftpPass));
            r.EnableSsl = pj.ftpTls;
            r.UsePassive = true; r.UseBinary = true; r.KeepAlive = true;
            r.Timeout = 30000; r.ReadWriteTimeout = 120000;
            return r;
        }

        readonly HashSet<string> madeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void FtpMkdirs(string relFile)
        {
            var parts = relFile.Split('/');
            string cur = "";
            for (int i = 0; i < parts.Length - 1; i++)
            {
                cur = cur == "" ? parts[i] : cur + "/" + parts[i];
                if (madeDirs.Contains(cur)) continue;
                try { using (Ftp(cur, WebRequestMethods.Ftp.MakeDirectory).GetResponse()) { } } catch { } // ya existe
                madeDirs.Add(cur);
            }
        }

        void FtpUpload(List<string> rels)
        {
            if (string.IsNullOrEmpty(pj.ftpHost)) throw new Exception("Falta el servidor FTP.");
            madeDirs.Clear();
            // La carpeta base también puede no existir
            string baseDir = pj.ftpDir.Trim('/');
            if (baseDir != "") { string saved = pj.ftpDir; pj.ftpDir = ""; FtpMkdirs(baseDir + "/x"); pj.ftpDir = saved; madeDirs.Clear(); }
            long total = 0, done = 0;
            foreach (var r in rels) total += new FileInfo(Path.Combine(pj.outDir, r.Replace('/', '\\'))).Length;
            int k = 0;
            foreach (var r in rels)
            {
                k++;
                string local = Path.Combine(pj.outDir, r.Replace('/', '\\'));
                Status("Subiendo " + k + " / " + rels.Count + ": " + r, total > 0 ? done / (double)total : 0);
                FtpMkdirs(r);
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        var req = Ftp(r, WebRequestMethods.Ftp.UploadFile);
                        using (var src = File.OpenRead(local))
                        using (var dst = req.GetRequestStream())
                        {
                            var buf = new byte[1 << 16]; int n; long sent = 0;
                            while ((n = src.Read(buf, 0, buf.Length)) > 0)
                            {
                                dst.Write(buf, 0, n); sent += n;
                                if (sent % (1 << 20) < n) Status("Subiendo " + k + " / " + rels.Count + ": " + r, total > 0 ? (done + sent) / (double)total : 0);
                            }
                        }
                        using (req.GetResponse()) { }
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (attempt >= 3) throw new Exception("No se pudo subir " + r + ": " + ex.Message);
                        Thread.Sleep(1500);
                    }
                }
                done += new FileInfo(local).Length;
                Log("  ↑ " + r);
            }
        }

        // Sube un archivo de prueba, comprueba que se ve en la dirección pública y lo borra
        void FtpTest()
        {
            if (string.IsNullOrEmpty(pj.ftpHost) || string.IsNullOrEmpty(pj.ftpUser)) throw new Exception("Rellena servidor, usuario y contraseña del FTP.");
            Status("Conectando al FTP…", 0);
            Directory.CreateDirectory(pj.outDir);
            string name = "bg-prueba-" + DateTime.Now.Ticks + ".txt";
            string local = Path.Combine(pj.outDir, name);
            File.WriteAllText(local, "BG Launcher: prueba de FTP " + DateTime.Now);
            try
            {
                FtpUpload(new List<string> { name });
                Log("✔ FTP: conexión y subida correctas.");
                Status("Comprobando la dirección pública…", .6);
                try
                {
                    using (var w = Util.Web()) w.DownloadString(Util.NoCache(Url(name)));
                    Log("✔ La carpeta del FTP coincide con la dirección pública " + pj.publicUrl);
                    Status("Todo correcto: el FTP y la dirección pública apuntan a la misma carpeta.", 1);
                }
                catch (Exception ex)
                {
                    Log("✘ El archivo se subió, pero no se ve en " + Url(name) + " (" + ex.Message + ").");
                    Log("  Revisa \"Carpeta en el FTP\": debe ser la carpeta que corresponde a la dirección pública.");
                    throw new Exception("El FTP funciona, pero la carpeta del FTP no corresponde a la dirección pública.");
                }
            }
            finally
            {
                FtpDelete(name);
                try { File.Delete(local); } catch { }
            }
        }

        public int RunHeadlessUpload(string rel)
        {
            headless = true;
            File.WriteAllText(Path.Combine(Application.StartupPath, "publicador.log"), "", new UTF8Encoding(false));
            if (!pj.useFtp) { Log("ERROR: el FTP no está activado."); return 1; }
            try { Util.SafeLocal(pj.outDir, rel); } catch (Exception ex) { Log("ERROR: " + ex.Message); return 1; }
            if (!File.Exists(Path.Combine(pj.outDir, rel.Replace('/', '\\')))) { Log("ERROR: no existe " + rel + " en \"Para subir\"."); return 1; }
            Run("Subir", delegate { FtpUpload(new List<string> { rel }); Log("Subido: " + Url(rel)); });
            return failed ? 1 : 0;
        }

        public int RunHeadlessFtp(bool design)
        {
            headless = true;
            File.WriteAllText(Path.Combine(Application.StartupPath, "publicador.log"), "", new UTF8Encoding(false));
            if (!pj.useFtp) { Log("ERROR: el FTP no está activado en proyecto.json."); return 1; }
            Run("FTP", delegate
            {
                if (design) { FtpUpload(SaveDesignLocal()); Log("Diseño publicado por FTP."); }
                else FtpTest();
            });
            return failed ? 1 : 0;
        }

        void FtpDelete(string rel)
        {
            try { using (Ftp(rel, WebRequestMethods.Ftp.DeleteFile).GetResponse()) { } Log("  ✕ " + rel); } catch { }
        }
    }
}
