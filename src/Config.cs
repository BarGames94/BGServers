// Configuración del launcher que el admin publica en su hosting (config.json).
// Todo lo que se ve y hace el launcher sale de aquí: así el mismo .exe sirve para cualquier servidor.
using System.Collections.Generic;

namespace BGLauncher
{
    public class Link { public string text; public string url; }
    // Red social: whatsapp, discord, tiktok, facebook, youtube, instagram, telegram.
    // x, y = posición en la ventana (960x580). -1 = en la fila de arriba a la derecha.
    public class Social { public string type; public string url; public int x = -1; public int y = -1; }
    public class News { public string title; public string date; public string url; }
    public class Lang { public string code; public string name; }
    public class IniOption { public string label; public string section; public string key; public string on = "1"; public string off = "0"; }

    // ---------------------------------------------------------------- Configuración (config.json)
    public class Requirement
    {
        public string name;                 // "Visual C++ 2015-2022 (x86)"
        public string regKey;               // clave en HKLM (vista de 32 y 64 bits)
        public string regValue = "Installed";
        public string file;                 // o un .dll que debe existir (en el cliente o en Windows)
        public string url;                  // instalador oficial de Microsoft (https)
        public string args = "/install /passive /norestart";
    }

    public class AutoLoginCfg
    {
        public string file = "Settings.ini";
        public string section = "AutoLogin";
        public string user = "User";
        public string pass = "Password";
        public string save = "SaveData";
        public bool quote = true;           // User= "cuenta"
    }

    public class LauncherConfig
    {
        // Diseño
        public string name = "Mu Online";
        public string subtitle = "";
        public string accent = "#f5b73b";     // botón JUGAR y detalles
        public string accent2 = "#2f7bff";    // enlaces y barra de progreso
        public string background = "";        // imagen de fondo (archivo en el hosting, junto a config.json)
        public string logo = "";              // logo (png) opcional
        public List<Link> links = new List<Link>();
        public List<Social> socials = new List<Social>();   // redes sociales con su logo
        public List<News> news = new List<News>();
        public string newsUrl = "";           // opcional: entradas de WordPress (…/wp-json/wp/v2/posts?per_page=5)

        // Juego
        public string exe = "Main.exe";
        public string args = "";
        public bool closeOnPlay = true;
        public string statusHost = "";        // IP del ConnectServer para saber si está online
        public int statusPort = 44405;
        public string playersUrl = "";        // opcional: devuelve {"online": N} (WebEngine: …/api/bg-launcher.php?action=status)

        // Opciones del cliente
        public string regPath = @"Software\Webzen\Mu\Config";
        public List<string> resolutions = new List<string>();
        public List<Lang> languages = new List<Lang>();
        public string iniFile = "Settings.ini";
        public List<IniOption> iniOptions = new List<IniOption>();

        // Fase 2
        public List<Requirement> requirements = new List<Requirement>();   // Visual C++ y otros instaladores de Microsoft
        public bool dep = true;               // añadir el juego a las excepciones de DEP al jugar
        public bool tray = true;              // al jugar, el launcher se queda en la bandeja (cuentas, instancias y avisos)
        public int maxInstances = 3;          // juegos abiertos a la vez
        public AutoLoginCfg autoLogin;        // null = sin "Mis cuentas"
        public string accountApi = "";        // opcional: …/wp-json/bgme/v1/tester/ para niveles y avisos
        public string accountServer = "";     // clave del servidor en esa API (louis)
        public List<int> alertLevels = new List<int> { 100, 200, 300, 400 };
        public bool defenderButton = true;    // botón opcional en Opciones

        // Licencia PRO (BGL-XXXX-XXXX-XXXX-XXXX). Sin licencia: plan gratis con "Powered by BGServers"
        public string license = "";

        // Autoactualización del propio launcher
        public string launcherVersion = "";
        public string launcherMd5 = "";
    }
}
