// BG Launcher - fase 2: requisitos (Visual C++), DEP, cuentas guardadas, instancias, bandeja y avisos de nivel.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BGLauncher
{
    // ---------------------------------------------------------------- Datos del jugador
    public class Account { public string alias; public string user; public string pass; }
    public class Prefs { public bool alerts = true; }

    static class Store
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BGLauncher-cuentas-v1");

        // Las cuentas se guardan cifradas con la protección de Windows: solo este usuario de este PC puede leerlas
        public static List<Account> LoadAccounts(string dir)
        {
            try
            {
                string f = Path.Combine(dir, "cuentas.dat");
                if (!File.Exists(f)) return new List<Account>();
                var b = ProtectedData.Unprotect(File.ReadAllBytes(f), Entropy, DataProtectionScope.CurrentUser);
                return Util.Json().Deserialize<List<Account>>(Encoding.UTF8.GetString(b)) ?? new List<Account>();
            }
            catch { return new List<Account>(); }
        }

        public static void SaveAccounts(string dir, List<Account> list)
        {
            Directory.CreateDirectory(dir);
            var b = ProtectedData.Protect(Encoding.UTF8.GetBytes(Util.Json().Serialize(list)), Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(Path.Combine(dir, "cuentas.dat"), b);
        }

        public static Prefs LoadPrefs(string dir)
        {
            try { return Util.Json().Deserialize<Prefs>(File.ReadAllText(Path.Combine(dir, "preferencias.json"))) ?? new Prefs(); } catch { return new Prefs(); }
        }

        public static void SavePrefs(string dir, Prefs p)
        {
            try { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "preferencias.json"), Util.Json().Serialize(p)); } catch { }
        }
    }

    // ---------------------------------------------------------------- Firma digital (Authenticode)
    static class Authenticode
    {
        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, IntPtr data);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct FileInfo { public uint cbStruct; public IntPtr path; public IntPtr hFile; public IntPtr known; }

        [StructLayout(LayoutKind.Sequential)]
        struct TrustData
        {
            public uint cbStruct; public IntPtr policy; public IntPtr sip; public uint ui; public uint revocation;
            public uint union; public IntPtr file; public uint stateAction; public IntPtr state; public IntPtr url;
            public uint provFlags; public uint uiContext;
        }

        static readonly Guid Verify = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        // true si el archivo está firmado, la firma es válida y el firmante es Microsoft
        public static bool IsMicrosoft(string path)
        {
            IntPtr p = Marshal.StringToHGlobalUni(path), fi = IntPtr.Zero, td = IntPtr.Zero;
            try
            {
                var f = new FileInfo { cbStruct = (uint)Marshal.SizeOf(typeof(FileInfo)), path = p };
                fi = Marshal.AllocHGlobal(Marshal.SizeOf(f)); Marshal.StructureToPtr(f, fi, false);
                var d = new TrustData { cbStruct = (uint)Marshal.SizeOf(typeof(TrustData)), ui = 2, revocation = 0, union = 1, file = fi, provFlags = 0x10 };
                td = Marshal.AllocHGlobal(Marshal.SizeOf(d)); Marshal.StructureToPtr(d, td, false);
                if (WinVerifyTrust(IntPtr.Zero, Verify, td) != 0) return false;
                var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
                return cert.Subject.Contains("O=Microsoft Corporation");
            }
            catch { return false; }
            finally
            {
                Marshal.FreeHGlobal(p);
                if (fi != IntPtr.Zero) Marshal.FreeHGlobal(fi);
                if (td != IntPtr.Zero) Marshal.FreeHGlobal(td);
            }
        }
    }

    class Instance { public Process p; public Account acc; }

    // ---------------------------------------------------------------- Ventana principal: funciones de la fase 2
    public partial class LauncherForm
    {
        List<Account> accounts = new List<Account>();
        Prefs prefs = new Prefs();
        readonly List<Instance> instances = new List<Instance>();
        readonly Dictionary<string, string> accInfo = new Dictionary<string, string>();       // usuario -> "Nombre · nivel 350"
        readonly Dictionary<string, Dictionary<string, int[]>> accLevels = new Dictionary<string, Dictionary<string, int[]>>();
        readonly HashSet<string> accBad = new HashSet<string>();                                // contraseña incorrecta: no volver a preguntar
        List<Requirement> missing = new List<Requirement>();
        LicenseInfo lic;                    // null o caducada = plan gratis
        bool Pro { get { return lic != null && lic.IsPro; } }
        string reqBusy;                     // nombre del requisito que se está instalando
        DateTime launchLock = DateTime.MinValue;
        byte[] iniBackup; bool iniHad; string iniPath; DateTime iniRestoreAt;
        NotifyIcon tray;
        System.Windows.Forms.Timer tick;
        DateTime nextLevels = DateTime.MinValue;

        void InitPhase2()
        {
            RecoverPendingIni();
            accounts = Store.LoadAccounts(dataDir);
            prefs = Store.LoadPrefs(dataDir);
            tick = new System.Windows.Forms.Timer { Interval = 2000 };
            tick.Tick += delegate { Tick(); };
            tick.Start();
            FormClosed += delegate
            {
                RestoreIni();
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
            };
        }

        // Cada 2 segundos: instancias cerradas, devolver el Settings.ini y pedir niveles
        void Tick()
        {
            int before = instances.Count;
            instances.RemoveAll(delegate (Instance i) { try { return i.p.HasExited; } catch { return true; } });
            if (iniBackup != null && (DateTime.Now >= iniRestoreAt || !instances.Exists(delegate (Instance i) { return i.acc != null; })))
                RestoreIni();
            if (Visible && (instances.Count != before || DateTime.Now < launchLock.AddSeconds(3))) Invalidate();
            if (Pro && DateTime.Now >= nextLevels && !string.IsNullOrEmpty(cfg.accountApi) && accounts.Count > 0 && (instances.Count > 0 || Visible))
            {
                nextLevels = DateTime.Now.AddSeconds(instances.Count > 0 ? 120 : 600);
                RefreshLevels();
            }
        }

        // ------------------------------------------------------------ Requisitos (Visual C++...)
        void CheckRequirements()
        {
            var miss = new List<Requirement>();
            foreach (var r in cfg.requirements ?? new List<Requirement>()) if (!ReqOk(r)) miss.Add(r);
            Ui(delegate { missing = miss; Invalidate(); });
        }

        bool ReqOk(Requirement r)
        {
            bool any = false;
            if (!string.IsNullOrEmpty(r.file))
            {
                any = true;
                string sys32 = Environment.SystemDirectory, sysX86 = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
                if (File.Exists(Path.Combine(root, r.file)) || File.Exists(Path.Combine(sysX86, r.file)) || File.Exists(Path.Combine(sys32, r.file))) return true;
            }
            if (!string.IsNullOrEmpty(r.regKey))
            {
                any = true;
                foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
                {
                    try
                    {
                        using (var b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                        using (var k = b.OpenSubKey(r.regKey))
                        {
                            object v = k == null ? null : k.GetValue(string.IsNullOrEmpty(r.regValue) ? "Installed" : r.regValue);
                            if (v != null && Convert.ToInt32(v) == 1) return true;
                        }
                    }
                    catch { }
                }
            }
            return !any;
        }

        void InstallRequirement(Requirement r)
        {
            if (reqBusy != null) return;
            if (string.IsNullOrEmpty(r.url) || !r.url.StartsWith("https://")) { MessageBox.Show(this, "No hay instalador configurado para " + r.name + ".", cfg.name); return; }
            if (MessageBox.Show(this, "Falta " + r.name + ", necesario para el juego.\n\nSe descargará el instalador OFICIAL de Microsoft y Windows te pedirá permiso para instalarlo. ¿Seguir?", cfg.name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            reqBusy = r.name; Invalidate();
            var th = new Thread(delegate ()
            {
                string err = null;
                try
                {
                    string dir = Path.Combine(Path.GetTempPath(), "BGLauncher");
                    Directory.CreateDirectory(dir);
                    string file = Path.Combine(dir, "instalador-" + Math.Abs(r.url.GetHashCode()) + ".exe");
                    using (var w = Util.Web()) w.DownloadFile(r.url, file);
                    if (!Authenticode.IsMicrosoft(file)) { File.Delete(file); throw new Exception("El instalador descargado no está firmado por Microsoft. Por seguridad no se ejecuta."); }
                    var p = Process.Start(new ProcessStartInfo(file, r.args ?? "") { Verb = "runas", UseShellExecute = true });
                    p.WaitForExit();
                    try { File.Delete(file); } catch { }
                }
                catch (Win32Exception) { err = "Instalación cancelada."; }
                catch (Exception ex) { err = ex.Message; }
                Ui(delegate
                {
                    reqBusy = null;
                    if (err != null) MessageBox.Show(this, err, cfg.name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    new Thread(CheckRequirements) { IsBackground = true }.Start();
                });
            });
            th.IsBackground = true; th.Start();
        }

        // ------------------------------------------------------------ DEP
        static void AddDep(string exe)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"))
            {
                string v = (k.GetValue(exe) as string ?? "").Trim();
                if (v.IndexOf("DisableNXShowUI", StringComparison.OrdinalIgnoreCase) >= 0) return;
                v = v == "" ? "~ DisableNXShowUI" : (v.StartsWith("~") ? v : "~ " + v) + " DisableNXShowUI";
                k.SetValue(exe, v);
            }
        }

        // ------------------------------------------------------------ Jugar (con o sin cuenta)
        bool CanLaunch() { return (state == St.Ready || state == St.Error) && DateTime.Now >= launchLock; }

        void StartGame(Account a)
        {
            if (!CanLaunch()) return;
            string exe;
            try { exe = Util.SafeLocal(root, cfg.exe); } catch { exe = Path.Combine(root, "Main.exe"); }
            if (!File.Exists(exe)) { MessageBox.Show(this, "No encuentro " + cfg.exe + " en la carpeta del cliente.\nUsa \"Reparar\" o descarga el cliente completo.", cfg.name, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            int max = Pro ? Math.Max(1, cfg.maxInstances) : 1;      // plan gratis: un solo juego
            if (instances.Count >= max) { MessageBox.Show(this, "Ya tienes " + instances.Count + " juego(s) abierto(s). El máximo es " + max + ".", cfg.name, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (missing.Count > 0 && MessageBox.Show(this, "Falta " + missing[0].name + ". Sin él el juego puede no abrir.\n¿Jugar igualmente?", cfg.name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (cfg.dep) { try { AddDep(exe); } catch { } }

            if (a != null && cfg.autoLogin != null)
            {
                try
                {
                    var al = cfg.autoLogin;
                    string ini = Util.SafeLocal(root, al.file);
                    if (iniBackup == null) { iniPath = ini; iniHad = File.Exists(ini); iniBackup = iniHad ? File.ReadAllBytes(ini) : new byte[0]; SavePendingIni(); }
                    Func<string, string> q = delegate (string s) { return al.quote ? "\"" + s + "\"" : s; };
                    Util.WriteIni(ini, al.section, al.user, q(a.user));
                    Util.WriteIni(ini, al.section, al.pass, q(a.pass));
                    if (!string.IsNullOrEmpty(al.save)) Util.WriteIni(ini, al.section, al.save, "1");
                    iniRestoreAt = DateTime.Now.AddMinutes(3);    // el Settings.ini vuelve a su estado (sin la contraseña) a los 3 min o al cerrar el juego
                }
                catch (Exception ex) { MessageBox.Show(this, "No se pudo preparar la cuenta: " + ex.Message, cfg.name); return; }
            }
            try
            {
                var p = Process.Start(new ProcessStartInfo(exe, cfg.args ?? "") { WorkingDirectory = root, UseShellExecute = true });
                instances.Add(new Instance { p = p, acc = a });
                launchLock = DateTime.Now.AddSeconds(8);           // un juego cada 8 s: le da tiempo a leer su cuenta
                Invalidate();
                if (cfg.tray) ToTray();
                else if (cfg.closeOnPlay && a == null && instances.Count == 1) Close();
            }
            catch (Exception ex) { MessageBox.Show(this, "No se pudo abrir el juego:\n" + ex.Message, cfg.name, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        void RestoreIni()
        {
            if (iniBackup == null) return;
            try { if (iniHad) File.WriteAllBytes(iniPath, iniBackup); else if (File.Exists(iniPath)) File.Delete(iniPath); }
            catch { return; }       // no se pudo (archivo en uso): se conserva la copia para reintentar en el próximo arranque
            iniBackup = null;
            ClearPendingIni();
        }

        // La copia del Settings.ini original también se guarda en disco: si el launcher se cierra a la fuerza
        // (antivirus, corte de luz, administrador de tareas) la contraseña no se queda en el archivo para siempre.
        string PendingIniFile { get { return Path.Combine(dataDir, "ini.pendiente"); } }

        void SavePendingIni()
        {
            try
            {
                Directory.CreateDirectory(dataDir);
                var o = new Dictionary<string, object> { { "path", iniPath }, { "had", iniHad }, { "data", Convert.ToBase64String(iniBackup) } };
                File.WriteAllText(PendingIniFile, Util.Json().Serialize(o), new UTF8Encoding(false));
            }
            catch { }
        }

        void ClearPendingIni() { try { if (File.Exists(PendingIniFile)) File.Delete(PendingIniFile); } catch { } }

        // Al abrir el launcher: si quedó una copia pendiente de una sesión anterior, devolver el Settings.ini a su estado original
        void RecoverPendingIni()
        {
            try
            {
                if (!File.Exists(PendingIniFile)) return;
                var o = Util.Json().DeserializeObject(File.ReadAllText(PendingIniFile)) as Dictionary<string, object>;
                if (o != null && o.ContainsKey("path") && o.ContainsKey("data"))
                {
                    string path = Convert.ToString(o["path"]);
                    // Solo dentro de la carpeta del cliente
                    Util.SafeLocal(root, Util.Rel(root, Path.GetFullPath(path)));
                    byte[] data = Convert.FromBase64String(Convert.ToString(o["data"]));
                    bool had = o.ContainsKey("had") && Convert.ToBoolean(o["had"]);
                    if (had) File.WriteAllBytes(path, data); else if (File.Exists(path)) File.Delete(path);
                }
                File.Delete(PendingIniFile);
            }
            catch { }
        }

        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

        void ShowGame(Instance i)
        {
            try { i.p.Refresh(); ShowWindow(i.p.MainWindowHandle, 9); SetForegroundWindow(i.p.MainWindowHandle); } catch { }
        }

        Instance InstanceOf(Account a)
        {
            foreach (var i in instances) if (i.acc == a) return i;
            return null;
        }

        // ------------------------------------------------------------ Bandeja
        void ToTray()
        {
            if (tray == null)
            {
                tray = new NotifyIcon { Icon = Icon, Text = cfg.name.Length > 60 ? cfg.name.Substring(0, 60) : cfg.name };
                var m = new ContextMenu();
                m.MenuItems.Add("Abrir el launcher", delegate { FromTray(); });
                m.MenuItems.Add("Salir", delegate { Close(); });
                tray.ContextMenu = m;
                tray.DoubleClick += delegate { FromTray(); };
                tray.Visible = true;
                tray.ShowBalloonTip(4000, cfg.name, "El launcher sigue aquí: tus cuentas y los avisos de nivel. Doble clic para abrirlo.", ToolTipIcon.Info);
            }
            tray.Visible = true;
            Hide();
        }

        void FromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); }

        void Notify(string title, string text)
        {
            if (tray == null) { tray = new NotifyIcon { Icon = Icon, Text = cfg.name.Length > 60 ? cfg.name.Substring(0, 60) : cfg.name, Visible = true }; tray.DoubleClick += delegate { FromTray(); }; }
            tray.Visible = true;
            tray.ShowBalloonTip(8000, title, text, ToolTipIcon.Info);
        }

        // ------------------------------------------------------------ Niveles y avisos
        void RefreshLevels()
        {
            var list = new List<Account>(accounts);
            // ".../tester/" + "login"  o  ".../bg-launcher.php?action=" + "login"
            string api = cfg.accountApi.EndsWith("/") || cfg.accountApi.EndsWith("=") ? cfg.accountApi : cfg.accountApi + "/";
            string server = cfg.accountServer ?? "";
            var th = new Thread(delegate ()
            {
                foreach (var a in list)
                {
                    if (accBad.Contains(a.user)) continue;
                    try
                    {
                        string body = Util.Json().Serialize(new Dictionary<string, string> { { "server", server }, { "user", a.user }, { "pass", a.pass } });
                        string res;
                        using (var w = Util.Web()) { w.Headers[HttpRequestHeader.ContentType] = "application/json"; res = w.UploadString(api + "login", body); }
                        var d = Util.Json().DeserializeObject(res) as Dictionary<string, object>;
                        var chars = d != null && d.ContainsKey("characters") ? d["characters"] as object[] : null;
                        var lv = new Dictionary<string, int[]>();
                        string best = ""; int bestScore = -1;
                        if (chars != null)
                            foreach (var o in chars)
                            {
                                var c = o as Dictionary<string, object>; if (c == null) continue;
                                string n = Convert.ToString(c["name"]); int level = Convert.ToInt32(c["level"]), resets = c.ContainsKey("resets") ? Convert.ToInt32(c["resets"]) : 0;
                                lv[n] = new[] { level, resets, 0 };
                                if (resets * 10000 + level > bestScore) { bestScore = resets * 10000 + level; best = n + " · nivel " + level + (resets > 0 ? " · " + resets + " resets" : ""); }
                            }
                        var acc = a;
                        Ui(delegate { UpdateLevels(acc, lv, lv.Count == 0 ? "Sin personajes" : best); });
                    }
                    catch (WebException ex)
                    {
                        var r = ex.Response as HttpWebResponse;
                        var acc = a;
                        if (r != null && (int)r.StatusCode == 401) Ui(delegate { accBad.Add(acc.user); accInfo[acc.user] = "Usuario o contraseña incorrectos"; Invalidate(); });
                        else if (r != null && (int)r.StatusCode == 429) Ui(delegate { accInfo[acc.user] = "Demasiados intentos, espera unos minutos"; Invalidate(); });
                        else Ui(delegate { if (!accInfo.ContainsKey(acc.user) || accInfo[acc.user] == "") accInfo[acc.user] = "Sin datos del servidor"; Invalidate(); });
                    }
                    catch { var acc = a; Ui(delegate { if (!accInfo.ContainsKey(acc.user) || accInfo[acc.user] == "") accInfo[acc.user] = "Sin datos del servidor"; Invalidate(); }); }
                }
            });
            th.IsBackground = true; th.Start();
        }

        void UpdateLevels(Account a, Dictionary<string, int[]> now, string info)
        {
            accInfo[a.user] = info;
            Dictionary<string, int[]> old;
            if (prefs.alerts && accLevels.TryGetValue(a.user, out old))
                foreach (var kv in now)
                {
                    int[] o;
                    if (!old.TryGetValue(kv.Key, out o)) continue;
                    if (kv.Value[1] > o[1]) { Notify("¡Reset!", kv.Key + " ha hecho reset (" + kv.Value[1] + ")."); continue; }
                    foreach (var t in cfg.alertLevels ?? new List<int>())
                        if (o[0] < t && kv.Value[0] >= t) Notify("¡Nivel " + t + "!", kv.Key + " ha llegado a nivel " + kv.Value[0] + ".");
                }
            accLevels[a.user] = now;
            Invalidate();
        }

        // ------------------------------------------------------------ Cuentas
        void AddAccount()
        {
            using (var f = new AccountForm(accent))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                accounts.RemoveAll(delegate (Account x) { return string.Equals(x.user, f.Result.user, StringComparison.OrdinalIgnoreCase); });
                accounts.Add(f.Result);
                accBad.Remove(f.Result.user);
                Store.SaveAccounts(dataDir, accounts);
                nextLevels = DateTime.MinValue;
                Invalidate();
            }
        }

        void RemoveAccount(Account a)
        {
            if (MessageBox.Show(this, "¿Quitar la cuenta " + a.user + " del launcher?\n(No se borra del juego, solo de esta lista.)", cfg.name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            accounts.Remove(a);
            Store.SaveAccounts(dataDir, accounts);
            Invalidate();
        }

        // Panel "Mis cuentas" (lado derecho)
        void PaintAccounts(Graphics g)
        {
            if (cfg.autoLogin == null || !Pro) return;              // "Mis cuentas" es PRO
            int x0 = W - 352, y = 112, w = 320;
            string head = "MIS CUENTAS" + (instances.Count > 0 ? "   ·   " + instances.Count + " en juego" : "");
            using (var b = new SolidBrush(accent)) g.DrawString(head, fH, b, x0, y);
            y += 28;
            bool can = CanLaunch();
            foreach (var a in accounts)
            {
                if (y > H - 250) break;
                var r = new Rectangle(x0, y, w, 50);
                using (var p = Round(r, 10)) using (var b = new SolidBrush(Color.FromArgb(150, 13, 18, 25))) g.FillPath(b, p);
                string title = a.user + (string.IsNullOrEmpty(a.alias) ? "" : "   · " + a.alias);
                using (var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                {
                    g.DrawString(title, fBtn, Brushes.White, new RectangleF(x0 + 12, y + 7, w - 120, 20), fmt);
                    string info; accInfo.TryGetValue(a.user, out info);
                    using (var b = new SolidBrush(Color.FromArgb(160, 230, 233, 239))) g.DrawString(info ?? "", fSmall, b, new RectangleF(x0 + 12, y + 27, w - 120, 18), fmt);
                }
                var inst = InstanceOf(a);
                var acc = a;
                var rx = new Rectangle(r.Right - 30, y + 13, 24, 24);
                Btn(g, rx, "✕", false, delegate { RemoveAccount(acc); }, null);
                var rb = new Rectangle(r.Right - 106, y + 11, 72, 28);
                if (inst != null)
                {
                    var ii = inst;
                    using (var b = new SolidBrush(Color.FromArgb(34, 197, 94))) g.FillEllipse(b, rb.X - 14, rb.Y + 10, 8, 8);
                    Btn(g, rb, "Mostrar", true, delegate { ShowGame(ii); }, null);
                }
                else if (can) PillAccent(g, rb, "JUGAR", delegate { StartGame(acc); });
                else { using (var p = Round(rb, 14)) using (var b = new SolidBrush(Color.FromArgb(120, 70, 80, 95))) g.FillPath(b, p); using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString("JUGAR", fBtn, Brushes.Gray, rb, sf); }
                y += 58;
            }
            if (accounts.Count < 8 && y <= H - 250)
                Btn(g, new Rectangle(x0, y, w, 36), "+  Añadir cuenta", true, AddAccount, null);
        }

        void PillAccent(Graphics g, Rectangle r, string text, Action click)
        {
            bool hot = hover != null && hover.r == r;
            using (var p = Round(r, r.Height / 2)) using (var b = new SolidBrush(hot ? ControlPaint.Light(accent, .25f) : accent)) g.FillPath(b, p);
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var b = new SolidBrush(Color.FromArgb(20, 14, 2)))
                g.DrawString(text, fBtn, b, r, sf);
            zones.Add(new Zone(r, click, null));
        }

        // Aviso de requisito que falta (encima de los botones)
        void PaintRequirements(Graphics g)
        {
            if (missing.Count == 0 && reqBusy == null) return;
            string t = reqBusy != null ? "Instalando " + reqBusy + "…  (acepta el permiso de Windows)" : "Falta " + missing[0].name + "  ·  Pulsa aquí para instalarlo";
            var sz = g.MeasureString(t, fBtn);
            var r = new Rectangle(32, H - 196, (int)sz.Width + 30, 34);
            bool hot = hover != null && hover.r == r;
            using (var p = Round(r, 17)) using (var b = new SolidBrush(Color.FromArgb(hot ? 240 : 210, 120, 80, 10))) g.FillPath(b, p);
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString(t, fBtn, Brushes.White, r, sf);
            if (reqBusy == null) { var req = missing[0]; zones.Add(new Zone(r, delegate { InstallRequirement(req); }, null)); }
        }
    }

    // ---------------------------------------------------------------- Ventana "Añadir cuenta"
    public class AccountForm : Form
    {
        public Account Result;
        readonly TextBox tAlias, tUser, tPass;

        public AccountForm(Color accent)
        {
            Text = "Añadir cuenta";
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(13, 18, 25); ForeColor = Color.FromArgb(230, 233, 239);
            Font = new Font("Segoe UI", 10f);
            ClientSize = new Size(360, 300);
            int y = 16;
            Func<string, bool, TextBox> field = delegate (string label, bool pw)
            {
                Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(20, y), ForeColor = Color.FromArgb(147, 161, 181) });
                var t = new TextBox { Location = new Point(20, y + 22), Width = 320, BackColor = Color.FromArgb(18, 25, 38), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = pw, MaxLength = 20 };
                Controls.Add(t); y += 60; return t;
            };
            tUser = field("Usuario de la cuenta (con el que entras al juego)", false);
            tPass = field("Contraseña", true);
            tAlias = field("Apodo para reconocerla (opcional, ej. Mi DK)", false);
            Controls.Add(new Label { Text = "Se guarda cifrada en este PC. Solo tu usuario de Windows puede leerla.", AutoSize = false, Size = new Size(320, 34), Location = new Point(20, y - 6), ForeColor = Color.Gray, Font = new Font("Segoe UI", 8.5f) });
            var ok = new Button { Text = "Guardar", Location = new Point(220, 254), Size = new Size(120, 34), FlatStyle = FlatStyle.Flat, BackColor = accent, ForeColor = Color.FromArgb(20, 14, 2) };
            ok.FlatAppearance.BorderSize = 0;
            var cancel = new Button { Text = "Cancelar", Location = new Point(110, 254), Size = new Size(100, 34), FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel };
            ok.Click += delegate
            {
                if (tUser.Text.Trim() == "" || tPass.Text == "") { MessageBox.Show(this, "Escribe usuario y contraseña.", Text); return; }
                Result = new Account { alias = tAlias.Text.Trim(), user = tUser.Text.Trim(), pass = tPass.Text };
                DialogResult = DialogResult.OK;
            };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
        }
    }
}
