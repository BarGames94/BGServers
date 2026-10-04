// BG Launcher - código común del launcher y del publicador.
// Compilado con el csc de .NET Framework 4 (C# 5): sin $"", sin ?. y sin nameof.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace BGLauncher
{
    // Un archivo del cliente dentro de la lista (manifest.json)
    public class FileEntry
    {
        public string p;   // ruta relativa con "/"
        public long s;     // tamaño en bytes
        public string h;   // MD5 en hexadecimal
        public int u;      // 1 = está subido al hosting (files/<p>) y el launcher puede descargarlo
        public string c;   // solo publicador: huella del original si la copia subida es distinta (Settings.ini limpio)
    }

    public class Manifest
    {
        public int version;
        public string date;
        public List<FileEntry> files = new List<FileEntry>();
        public List<string> delete = new List<string>();    // archivos que el launcher debe borrar
        public List<string> preserve = new List<string>();  // archivos del jugador: solo se crean si faltan (Settings.ini...)

        public Dictionary<string, FileEntry> Map()
        {
            var d = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files) d[f.p] = f;
            return d;
        }
    }

    public static class Util
    {
        public static JavaScriptSerializer Json()
        {
            var j = new JavaScriptSerializer();
            j.MaxJsonLength = int.MaxValue;
            j.RecursionLimit = 64;
            return j;
        }

        public static string Md5File(string path)
        {
            using (var md5 = MD5.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16))
            {
                return Hex(md5.ComputeHash(fs));
            }
        }

        public static string Md5Bytes(byte[] b)
        {
            using (var md5 = MD5.Create()) return Hex(md5.ComputeHash(b));
        }

        static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        // Ruta segura dentro de la carpeta del cliente: nunca deja escribir fuera de ella
        public static string SafeLocal(string root, string rel)
        {
            if (string.IsNullOrEmpty(rel) || rel.Contains(":") || rel.StartsWith("/") || rel.StartsWith("\\"))
                throw new Exception("Ruta no permitida en la lista: " + rel);
            foreach (var seg in rel.Split('/', '\\'))
                if (seg == ".." || seg == ".") throw new Exception("Ruta no permitida en la lista: " + rel);
            string full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            string r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(r, StringComparison.OrdinalIgnoreCase)) throw new Exception("Ruta fuera del cliente: " + rel);
            return full;
        }

        // files/Data/Local/Item.bmd -> cada trozo codificado para la URL
        public static string UrlPath(string rel)
        {
            var parts = rel.Split('/');
            for (int i = 0; i < parts.Length; i++) parts[i] = Uri.EscapeDataString(parts[i]);
            return string.Join("/", parts);
        }

        public static string Rel(string root, string full)
        {
            string r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.Substring(r.Length).Replace(Path.DirectorySeparatorChar, '/');
        }

        public static void InitNet()
        {
            // TLS 1.2 para HTTPS en Windows 7/8 con .NET 4
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls;
            ServicePointManager.DefaultConnectionLimit = 8;
        }

        public static WebClient Web()
        {
            var w = new WebClient();
            w.Encoding = Encoding.UTF8;
            w.Headers[HttpRequestHeader.UserAgent] = "BGLauncher/" + typeof(Util).Assembly.GetName().Version.ToString(3);
            w.Headers[HttpRequestHeader.CacheControl] = "no-cache";
            return w;
        }

        public static string NoCache(string url)
        {
            return url + (url.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
        }

        // Lee un .ini sencillo: [Seccion] Clave = Valor
        public static Dictionary<string, string> ReadIni(string path, string section)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return d;
            string cur = "";
            foreach (var raw in File.ReadAllLines(path))
            {
                string l = raw.Trim();
                if (l.StartsWith("[") && l.EndsWith("]")) { cur = l.Substring(1, l.Length - 2).Trim(); continue; }
                int i = l.IndexOf('=');
                if (i <= 0 || l.StartsWith(";") || l.StartsWith("//")) continue;
                if (!string.Equals(cur, section, StringComparison.OrdinalIgnoreCase)) continue;
                string v = l.Substring(i + 1).Trim();
                int c = v.IndexOf(';');
                if (c >= 0 && !v.StartsWith("\"")) v = v.Substring(0, c).Trim();
                d[l.Substring(0, i).Trim()] = v.Trim('"');
            }
            return d;
        }

        // Cambia una clave de un .ini respetando el resto del archivo (comentarios, orden, formato)
        public static void WriteIni(string path, string section, string key, string value)
        {
            var lines = File.Exists(path) ? new List<string>(File.ReadAllLines(path)) : new List<string>();
            string cur = null; int secEnd = -1; bool done = false;
            for (int i = 0; i < lines.Count; i++)
            {
                string l = lines[i].Trim();
                if (l.StartsWith("[") && l.EndsWith("]"))
                {
                    if (string.Equals(cur, section, StringComparison.OrdinalIgnoreCase)) { secEnd = i; break; }
                    cur = l.Substring(1, l.Length - 2).Trim();
                    continue;
                }
                if (!string.Equals(cur, section, StringComparison.OrdinalIgnoreCase)) continue;
                int eq = l.IndexOf('=');
                if (eq > 0 && string.Equals(l.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = key + " = " + value;
                    done = true; break;
                }
            }
            if (!done)
            {
                if (string.Equals(cur, section, StringComparison.OrdinalIgnoreCase) && secEnd < 0) lines.Add(key + " = " + value);
                else if (secEnd >= 0) { int at = secEnd; while (at > 0 && lines[at - 1].Trim() == "") at--; lines.Insert(at, key + " = " + value); }
                else { lines.Add(""); lines.Add("[" + section + "]"); lines.Add(key + " = " + value); }
            }
            File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(false));
        }

        public static string Size(long b)
        {
            if (b >= 1L << 30) return (b / (double)(1L << 30)).ToString("0.00") + " GB";
            if (b >= 1L << 20) return (b / (double)(1L << 20)).ToString("0.0") + " MB";
            if (b >= 1L << 10) return (b / 1024.0).ToString("0") + " KB";
            return b + " B";
        }
    }

    // Caché de huellas: no recalcular el MD5 de un archivo que no ha cambiado de tamaño ni de fecha
    public class HashCache
    {
        readonly string file;
        readonly Dictionary<string, string[]> map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        public HashCache(string file)
        {
            this.file = file;
            try
            {
                if (File.Exists(file))
                    foreach (var l in File.ReadAllLines(file))
                    {
                        var a = l.Split('|');
                        if (a.Length == 4) map[a[0]] = a;
                    }
            }
            catch { }
        }

        public string Get(string root, string rel, bool force)
        {
            string full = Util.SafeLocal(root, rel);
            var fi = new FileInfo(full);
            if (!fi.Exists) return null;
            string len = fi.Length.ToString(CultureInfo.InvariantCulture), t = fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
            string[] c;
            if (!force && map.TryGetValue(rel, out c) && c[1] == len && c[2] == t) return c[3];
            string h = Util.Md5File(full);
            map[rel] = new[] { rel, len, t, h };
            return h;
        }

        public void Forget(string rel) { map.Remove(rel); }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                var sb = new StringBuilder();
                foreach (var a in map.Values) sb.Append(string.Join("|", a)).Append('\n');
                File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
