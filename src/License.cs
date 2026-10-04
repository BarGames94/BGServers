// BG Launcher - comprobación de licencia.
// El servidor de licencias (bargameservers.com) firma su respuesta con su clave RSA privada;
// aquí solo está la clave pública: una respuesta falsificada o modificada no pasa la verificación.
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace BGLauncher
{
    public class LicenseInfo
    {
        public string key; public string host; public string plan; public string name; public string expires; public string reason;
        public bool valid; public long ts;

        public bool IsPro
        {
            get
            {
                if (!valid || plan != "pro") return false;
                DateTime e;
                if (!string.IsNullOrEmpty(expires) && DateTime.TryParseExact(expires, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out e) && e.AddDays(1) < DateTime.UtcNow) return false;
                return true;
            }
        }
    }

    static class License
    {
        public const string Server = "https://bargameservers.com/wp-json/bgme/v1/licencia";
        public const string PublicKey = "<RSAKeyValue><Modulus>0mq+zC6HGr9KvwHS7GXBjS68AkBf5F0BuzKCMMLLvgLTIyXQokqCrQruOyDS/7vA8WmRKTQQ5y7Zp0Qil0Efe34b0+w9RMDkH/vuNfqPCe0cgAY7wSNhvYAv5X9wjb/BLTtx9rs2X77hsDYRcygp8b2K1eqEhN5bCDAfwF/vrFBthY4RZ2S9ASmyO7p3XcB53hhefpSiDaVONJGzT/KUaV3G/WP0V+VFxcAN8r5/tdP4THSfH34+04uVmWUzSo3RvJheQ4BJL4UXbRBPr+0xFUMKFan/O1KS8Sn5YE8K1ehN+mgMWg8fPHlDbGVoM/LVwjxStfG151fr7hktzCd5CQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
        const int GraceDays = 15;          // sin conexión con el servidor de licencias, vale la última comprobación 15 días

        public static string Host(string url)
        {
            try { string h = new Uri(url).Host.ToLowerInvariant(); return h.StartsWith("www.") ? h.Substring(4) : h; } catch { return ""; }
        }

        public static LicenseInfo Check(string key, string host, string dataDir, string version)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(PublicKey)) return null;
            string cache = Path.Combine(dataDir, "licencia.dat");
            try
            {
                string url = Server + "?key=" + Uri.EscapeDataString(key) + "&host=" + Uri.EscapeDataString(host) + "&v=" + Uri.EscapeDataString(version);
                string raw;
                using (var w = Util.Web()) raw = w.DownloadString(Util.NoCache(url));
                var info = Parse(raw, key, host);
                if (info != null) { Directory.CreateDirectory(dataDir); File.WriteAllText(cache, raw); return info; }
            }
            catch { }
            // Sin conexión: última respuesta firmada, si es reciente
            try
            {
                if (!File.Exists(cache)) return null;
                var info = Parse(File.ReadAllText(cache), key, host);
                if (info == null) return null;
                var when = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(info.ts);
                return (DateTime.UtcNow - when).TotalDays <= GraceDays ? info : null;
            }
            catch { return null; }
        }

        // Verifica la firma y que la respuesta sea para ESTA licencia y ESTE dominio
        static LicenseInfo Parse(string raw, string key, string host)
        {
            var o = Util.Json().DeserializeObject(raw) as System.Collections.Generic.Dictionary<string, object>;
            if (o == null || !o.ContainsKey("data") || !o.ContainsKey("sig")) return null;
            byte[] data = Convert.FromBase64String(Convert.ToString(o["data"]));
            byte[] sig = Convert.FromBase64String(Convert.ToString(o["sig"]));
            using (var rsa = new RSACryptoServiceProvider(new CspParameters(24)))   // 24 = proveedor con SHA-256
            {
                rsa.PersistKeyInCsp = false;
                rsa.FromXmlString(PublicKey);
                if (!rsa.VerifyData(data, "SHA256", sig)) return null;
            }
            var info = Util.Json().Deserialize<LicenseInfo>(Encoding.UTF8.GetString(data));
            if (info == null || !string.Equals(info.key, key, StringComparison.OrdinalIgnoreCase) || !string.Equals(info.host, host, StringComparison.OrdinalIgnoreCase)) return null;
            return info;
        }
    }
}
