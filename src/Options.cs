// BG Launcher - ventana de opciones del juego.
// Resolución, ventana, idioma y sonido van al registro (igual que los .reg del cliente);
// el resto son claves de Settings.ini definidas por el admin en config.json.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BGLauncher
{
    public class OptionsForm : Form
    {
        readonly LauncherConfig cfg;
        readonly string root;
        ComboBox cRes, cLang;
        CheckBox chWin, chSound;
        TrackBar tVol;
        readonly List<KeyValuePair<IniOption, CheckBox>> iniChecks = new List<KeyValuePair<IniOption, CheckBox>>();
        static readonly Color Bg = Color.FromArgb(13, 18, 25), Fg = Color.FromArgb(230, 233, 239), Muted = Color.FromArgb(147, 161, 181), Field = Color.FromArgb(18, 25, 38);

        readonly Prefs prefs;
        CheckBox chAlerts;

        public OptionsForm(LauncherConfig cfg, string root, Color accent, Prefs prefs)
        {
            this.cfg = cfg; this.root = root; this.prefs = prefs;
            Text = "Opciones del juego";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Bg; ForeColor = Fg;
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Dpi;

            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(22, 16, 22, 8) };
            Controls.Add(flow);

            RegistryKey k = null;
            try { k = Registry.CurrentUser.CreateSubKey(cfg.regPath); } catch { }

            Head(flow, "Pantalla");
            if (cfg.resolutions != null && cfg.resolutions.Count > 0)
            {
                cRes = Combo(flow, "Resolución");
                foreach (var r in cfg.resolutions) cRes.Items.Add(r);
                int cur = RegInt(k, "Resolution", 1);
                cRes.SelectedIndex = cur >= 0 && cur < cRes.Items.Count ? cur : 0;
            }
            chWin = Check(flow, "Jugar en ventana", RegInt(k, "WindowMode", 1) == 1);

            if (cfg.languages != null && cfg.languages.Count > 0)
            {
                Head(flow, "Idioma");
                cLang = Combo(flow, "Idioma del juego");
                string cl = k != null ? Convert.ToString(k.GetValue("LangSelection", "")) : "";
                for (int i = 0; i < cfg.languages.Count; i++)
                {
                    cLang.Items.Add(cfg.languages[i].name);
                    if (string.Equals(cfg.languages[i].code, cl, StringComparison.OrdinalIgnoreCase)) cLang.SelectedIndex = i;
                }
                if (cLang.SelectedIndex < 0) cLang.SelectedIndex = 0;
            }

            Head(flow, "Sonido");
            chSound = Check(flow, "Música y sonido activados", RegInt(k, "MusicOnOFF", 1) == 1 || RegInt(k, "SoundOnOFF", 1) == 1);
            var lv = new Label { Text = "Volumen", AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 6, 0, 0) };
            flow.Controls.Add(lv);
            tVol = new TrackBar { Minimum = 0, Maximum = 10, TickFrequency = 1, Width = 320, BackColor = Bg };
            tVol.Value = Math.Max(0, Math.Min(10, RegInt(k, "VolumeLevel", 5)));
            flow.Controls.Add(tVol);
            if (k != null) k.Close();

            string ini = IniPath();
            if (cfg.iniOptions != null && cfg.iniOptions.Count > 0 && ini != null)
            {
                Head(flow, "Gráficos y juego");
                foreach (var o in cfg.iniOptions)
                {
                    var d = Util.ReadIni(ini, o.section);
                    string v; d.TryGetValue(o.key, out v);
                    iniChecks.Add(new KeyValuePair<IniOption, CheckBox>(o, Check(flow, o.label, v == o.on)));
                }
            }

            if (!string.IsNullOrEmpty(cfg.accountApi) && cfg.autoLogin != null)
            {
                Head(flow, "Avisos");
                string levels = string.Join(", ", (cfg.alertLevels ?? new List<int>()).ConvertAll(delegate (int i) { return i.ToString(); }).ToArray());
                chAlerts = Check(flow, "Avisarme al llegar a nivel " + levels + " y al hacer reset", prefs.alerts);
            }

            if (cfg.defenderButton)
            {
                Head(flow, "Antivirus");
                var note = new Label { Text = "Si Windows Defender borra o bloquea archivos del juego, puedes excluir esta carpeta. Hazlo solo si confías en este servidor.", AutoSize = false, Size = new Size(360, 40), ForeColor = Muted, Font = new Font("Segoe UI", 8.5f) };
                flow.Controls.Add(note);
                var bd = new Button { Text = "Excluir la carpeta del juego en Windows Defender", Width = 360, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Field, ForeColor = Fg };
                bd.FlatAppearance.BorderColor = Color.FromArgb(45, 59, 82);
                bd.Click += delegate { ExcludeDefender(); };
                flow.Controls.Add(bd);
            }

            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 58, Padding = new Padding(16, 10, 16, 10), BackColor = Color.FromArgb(10, 14, 20) };
            var ok = new Button { Text = "Guardar", Width = 120, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = accent, ForeColor = Color.FromArgb(20, 14, 2), Font = new Font("Segoe UI Semibold", 10f) };
            ok.FlatAppearance.BorderSize = 0;
            ok.Click += delegate { Save(); DialogResult = DialogResult.OK; };
            var cancel = new Button { Text = "Cancelar", Width = 110, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = Field, ForeColor = Fg, DialogResult = DialogResult.Cancel };
            cancel.FlatAppearance.BorderColor = Color.FromArgb(45, 59, 82);
            bar.Controls.Add(ok); bar.Controls.Add(cancel);
            Controls.Add(bar);
            AcceptButton = ok; CancelButton = cancel;
            ClientSize = new Size(420, Math.Min(640, 150 + flow.Controls.Count * 34));
        }

        string IniPath()
        {
            try { return string.IsNullOrEmpty(cfg.iniFile) ? null : Util.SafeLocal(root, cfg.iniFile); } catch { return null; }
        }

        static int RegInt(RegistryKey k, string name, int def)
        {
            try { return k == null ? def : Convert.ToInt32(k.GetValue(name, def)); } catch { return def; }
        }

        void Head(Control p, string t)
        {
            p.Controls.Add(new Label { Text = t.ToUpperInvariant(), AutoSize = true, ForeColor = Color.FromArgb(245, 183, 59), Font = new Font("Segoe UI Semibold", 9f), Margin = new Padding(0, 14, 0, 4) });
        }

        ComboBox Combo(Control p, string label)
        {
            p.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 4, 0, 2) });
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, FlatStyle = FlatStyle.Flat, BackColor = Field, ForeColor = Fg };
            p.Controls.Add(c);
            return c;
        }

        CheckBox Check(Control p, string label, bool on)
        {
            var c = new CheckBox { Text = label, Checked = on, AutoSize = true, ForeColor = Fg, Margin = new Padding(0, 4, 0, 2) };
            p.Controls.Add(c);
            return c;
        }

        // Opcional y siempre a petición del jugador: Windows pedirá permiso de administrador
        void ExcludeDefender()
        {
            string msg = "Vas a pedir a Windows Defender que NO analice esta carpeta:\n\n" + root +
                "\n\nEl resto del PC sigue protegido. Hazlo solo si el antivirus da problemas con el juego y confías en este servidor.\n\n¿Seguir?";
            if (MessageBox.Show(this, msg, "Windows Defender", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                string path = root.Replace("'", "''");
                var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe",
                    "-NoProfile -WindowStyle Hidden -Command \"Add-MpPreference -ExclusionPath '" + path + "'\"") { Verb = "runas", UseShellExecute = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden });
                p.WaitForExit();
                MessageBox.Show(this, p.ExitCode == 0 ? "Listo: la carpeta del juego está excluida." : "Windows no aceptó el cambio (¿Defender está desactivado o lo gestiona otro antivirus?).", "Windows Defender");
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Windows Defender"); }
        }

        void Save()
        {
            if (chAlerts != null) prefs.alerts = chAlerts.Checked;
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(cfg.regPath))
                {
                    if (cRes != null) k.SetValue("Resolution", cRes.SelectedIndex, RegistryValueKind.DWord);
                    k.SetValue("WindowMode", chWin.Checked ? 1 : 0, RegistryValueKind.DWord);
                    if (cLang != null) k.SetValue("LangSelection", cfg.languages[cLang.SelectedIndex].code, RegistryValueKind.String);
                    k.SetValue("MusicOnOFF", chSound.Checked ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("SoundOnOFF", chSound.Checked ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("VolumeLevel", tVol.Value, RegistryValueKind.DWord);
                }
                string ini = IniPath();
                if (ini != null && File.Exists(ini))
                    foreach (var kv in iniChecks)
                        Util.WriteIni(ini, kv.Key.section, kv.Key.key, kv.Value.Checked ? kv.Key.on : kv.Key.off);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudieron guardar las opciones:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
