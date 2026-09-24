using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace GhostMacro
{
    public class TrailMetadata
    {
        public int formatVersion = 3;
        public string note;                  // заметка пользователя (редактируется в менеджере)
        public string createdUtc;            // ISO 8601
        public string editedUtc;             // если файл обрезали / меняли заметку
        public string convertedFrom;         // если сконвертирован из .json
        public string modVersion;
        public string gameVersion;
        public string apiVersion;            // версия Modding API (вида 1.5.78.11833-74)
        public string recordRate;
        public float avgGameFps;             // средний FPS игры во время записи
        public string resolution;
        public float duration;               // от первого до последнего кадра героя, с
        public int heroFrames;
        public int actionFrames;
        public int actionEntities;           // сколько отдельных эффектов (касты, удары...)
        public string startScene;
        public string endScene;
        public int sceneTransitions;
        public int uniqueScenes;
        public float distance;               // путь героя, юниты
        public Dictionary<string, int> inputs = new Dictionary<string, int>();   // число нажатий
        public int saveSlot = -1;
        public string gameMode;              // Normal / Steel Soul / Godseeker
        public float completion = -1f;       // %
        public float playTimeHours = -1f;
        public int masks;
        public int soulVessels;
        public int nailLevel = -1;
        public int charmNotches;
        public int notchesUsed;
        public int fireballLevel, quakeLevel, screamLevel;
        public List<string> abilities = new List<string>();
        public List<int> charms = new List<int>();
        public List<string> charmNames = new List<string>();   // английские, с учётом вариантов

        public List<TrailRoomMeta> rooms = new List<TrailRoomMeta>();
        private static string gameVersionCache;

        public static string GameVersion
        {
            get
            {
                if (gameVersionCache != null) return gameVersionCache;

                string v = null;
                try
                {
                    Type constants = typeof(PlayerData).Assembly.GetType("Constants");
                    FieldInfo f = constants != null
                        ? constants.GetField("GAME_VERSION", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                        : null;
                    if (f != null) v = (f.IsLiteral ? f.GetRawConstantValue() : f.GetValue(null)) as string;
                }
                catch { }

                if (string.IsNullOrEmpty(v))
                {
                    try
                    {
                        Type hooks = typeof(Modding.ModHooks);
                        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                        object api = hooks.GetProperty("ModVersion", flags)?.GetValue(null, null)
                                     ?? hooks.GetField("ModVersion", flags)?.GetValue(null);
                        string s = api as string;
                        if (!string.IsNullOrEmpty(s)) v = s.Split('-')[0].Trim();
                    }
                    catch { }
                }

                if (string.IsNullOrEmpty(v)) v = Application.version;
                gameVersionCache = v;
                return v;
            }
        }

        public static string ApiVersion
        {
            get
            {
                try
                {
                    Type hooks = typeof(Modding.ModHooks);
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                    object api = hooks.GetProperty("ModVersion", flags)?.GetValue(null, null)
                                 ?? hooks.GetField("ModVersion", flags)?.GetValue(null);
                    return api as string;
                }
                catch { return null; }
            }
        }
        public static bool IsWrongGameVersion(string stored)
        {
            return string.IsNullOrEmpty(stored) || (stored == Application.version && stored != GameVersion);
        }
        public static TrailMetadata CreateForCurrentSession()
        {
            var m = new TrailMetadata
            {
                createdUtc = DateTime.UtcNow.ToString("o"),
                modVersion = GhostMacro.ModVersion,
                gameVersion = GameVersion,
                apiVersion = ApiVersion,
                resolution = Screen.width + "x" + Screen.height,
                avgGameFps = HitboxTrailBehaviour.RecordingAverageFps
            };

            int idx = Mathf.Clamp(GhostMacro.Settings.RecordRateIndex, 0, HitboxTrailBehaviour.RecordRateLabels.Length - 1);
            m.recordRate = HitboxTrailBehaviour.RecordRateLabels[idx];

            m.FillPlayer();
            return m;
        }

        private void FillPlayer()
        {
            PlayerData pd = PlayerData.instance;
            if (pd == null) return;

            try { if (GameManager.instance != null) saveSlot = GameManager.instance.profileID; } catch { }

            masks = PdInt(pd, "maxHealth");
            soulVessels = PdInt(pd, "MPReserveMax") / 33;
            nailLevel = PdInt(pd, "nailSmithUpgrades");
            charmNotches = PdInt(pd, "charmSlots");
            notchesUsed = PdInt(pd, "charmSlotsFilled");
            fireballLevel = PdInt(pd, "fireballLevel");
            quakeLevel = PdInt(pd, "quakeLevel");
            screamLevel = PdInt(pd, "screamLevel");
            completion = PdFloat(pd, "completionPercentage");
            playTimeHours = PdFloat(pd, "playTime") / 3600f;

            gameMode = PdBool(pd, "bossRushMode") ? "Godseeker"
                     : PdInt(pd, "permadeathMode") > 0 ? "Steel Soul"
                     : "Normal";

            abilities.Clear();
            foreach (var a in AbilityTable)
                if (PdBool(pd, a.Key)) abilities.Add(a.Value);

            charms.Clear();
            charmNames.Clear();
            try
            {
                if (pd.equippedCharms != null)
                    foreach (int id in pd.equippedCharms)
                    {
                        charms.Add(id);
                        charmNames.Add(CharmNames.Get(id, pd));
                    }
            }
            catch { }
        }
        private static readonly KeyValuePair<string, string>[] AbilityTable =
        {
            new KeyValuePair<string, string>("hasDash", "Mothwing Cloak"),
            new KeyValuePair<string, string>("hasShadowDash", "Shade Cloak"),
            new KeyValuePair<string, string>("hasWalljump", "Mantis Claw"),
            new KeyValuePair<string, string>("hasDoubleJump", "Monarch Wings"),
            new KeyValuePair<string, string>("hasSuperDash", "Crystal Heart"),
            new KeyValuePair<string, string>("hasAcidArmour", "Isma's Tear"),
            new KeyValuePair<string, string>("hasDreamNail", "Dream Nail"),
            new KeyValuePair<string, string>("dreamNailUpgraded", "Awoken Dream Nail"),
            new KeyValuePair<string, string>("hasDreamGate", "Dreamgate"),
            new KeyValuePair<string, string>("hasLantern", "Lumafly Lantern"),
            new KeyValuePair<string, string>("hasKingsBrand", "King's Brand"),
            new KeyValuePair<string, string>("hasCyclone", "Cyclone Slash"),
            new KeyValuePair<string, string>("hasDashSlash", "Great Slash"),
            new KeyValuePair<string, string>("hasUpwardSlash", "Dash Slash")
        };

        internal static int PdInt(PlayerData pd, string name) { try { return pd.GetInt(name); } catch { return 0; } }
        internal static bool PdBool(PlayerData pd, string name) { try { return pd.GetBool(name); } catch { return false; } }
        internal static float PdFloat(PlayerData pd, string name) { try { return pd.GetFloat(name); } catch { return 0f; } }
        public TrailMetadata CloneEnvironment()
        {
            TrailMetadata c = JsonConvert.DeserializeObject<TrailMetadata>(JsonConvert.SerializeObject(this));
            c.rooms = new List<TrailRoomMeta>();
            return c;
        }
        public void FillStats(List<TrailHeroFrame> heroes, List<ActionEntityFrame> actions)
        {
            heroFrames = heroes.Count;
            actionFrames = actions.Count;
            duration = heroes.Count > 1 ? heroes[heroes.Count - 1].time - heroes[0].time : 0f;
            startScene = heroes.Count > 0 ? heroes[0].scene : null;
            endScene = heroes.Count > 0 ? heroes[heroes.Count - 1].scene : null;

            var entityIds = new HashSet<int>();
            foreach (ActionEntityFrame a in actions) entityIds.Add(a.entityId);
            actionEntities = entityIds.Count;

            List<TrailVisit> visits = TrailVisit.Split(heroes);
            sceneTransitions = Mathf.Max(0, visits.Count - 1);
            var unique = new HashSet<string>();
            foreach (TrailVisit v in visits) unique.Add(v.scene);
            uniqueScenes = unique.Count;

            int[] owner = TrailVisit.AssignActions(visits, actions);
            var actionCounts = new int[visits.Count];
            for (int i = 0; i < owner.Length; i++) if (owner[i] >= 0) actionCounts[owner[i]]++;

            inputs = new Dictionary<string, int>();
            foreach (var b in CountedInputs) inputs[b.Value] = 0;

            distance = 0f;
            rooms.Clear();
            for (int vi = 0; vi < visits.Count; vi++)
            {
                TrailVisit v = visits[vi];
                var room = new TrailRoomMeta
                {
                    scene = v.scene,
                    heroFrames = v.last - v.first,
                    actionFrames = actionCounts[vi],
                    duration = v.tEnd - v.tStart
                };

                ushort prevInputs = 0;
                for (int i = v.first; i < v.last; i++)
                {
                    if (i > v.first)
                        room.distance += Vector2.Distance(Center(heroes[i]), Center(heroes[i - 1]));
                    ushort cur = heroes[i].inputs;
                    ushort pressed = (ushort)(cur & ~prevInputs);
                    prevInputs = cur;
                    if (pressed == 0) continue;

                    foreach (var b in CountedInputs)
                    {
                        if (!InputRecorder.Has(pressed, b.Key)) continue;
                        inputs[b.Value]++;
                        if (b.Key == InputRecorder.Jump) room.jumps++;
                        else if (b.Key == InputRecorder.Dash) room.dashes++;
                        else if (b.Key == InputRecorder.Attack) room.attacks++;
                        else if (b.Key == InputRecorder.Cast || b.Key == InputRecorder.QuickCast) room.casts++;
                    }
                }

                distance += room.distance;
                rooms.Add(room);
            }
        }

        private static Vector2 Center(TrailHeroFrame f) { return (f.min + f.max) * 0.5f; }

        private static readonly KeyValuePair<int, string>[] CountedInputs =
        {
            new KeyValuePair<int, string>(InputRecorder.Jump, "Jump"),
            new KeyValuePair<int, string>(InputRecorder.Attack, "Attack"),
            new KeyValuePair<int, string>(InputRecorder.Dash, "Dash"),
            new KeyValuePair<int, string>(InputRecorder.Cast, "Cast / Focus"),
            new KeyValuePair<int, string>(InputRecorder.QuickCast, "Quick Cast"),
            new KeyValuePair<int, string>(InputRecorder.SuperDash, "Super Dash"),
            new KeyValuePair<int, string>(InputRecorder.DreamNail, "Dream Nail")
        };
        public static string NailName(int level)
        {
            switch (level)
            {
                case 0: return "Old Nail";
                case 1: return "Sharpened Nail";
                case 2: return "Channelled Nail";
                case 3: return "Coiled Nail";
                case 4: return "Pure Nail";
                default: return null;
            }
        }

        public static string SpellName(string kind, int level)
        {
            if (level <= 0) return null;
            switch (kind)
            {
                case "fireball": return level >= 2 ? "Shade Soul" : "Vengeful Spirit";
                case "quake": return level >= 2 ? "Descending Dark" : "Desolate Dive";
                case "scream": return level >= 2 ? "Abyss Shriek" : "Howling Wraiths";
                default: return null;
            }
        }
    }

    public class TrailRoomMeta
    {
        public string scene;
        public int heroFrames;
        public int actionFrames;
        public float duration;
        public float distance;
        public int jumps, dashes, attacks, casts;
    }
    internal static class CharmNames
    {
        private static readonly string[] Names =
        {
            null,
            "Gathering Swarm", "Wayward Compass", "Grubsong", "Stalwart Shell", "Baldur Shell",
            "Fury of the Fallen", "Quick Focus", "Lifeblood Heart", "Lifeblood Core", "Defender's Crest",
            "Flukenest", "Thorns of Agony", "Mark of Pride", "Steady Body", "Heavy Blow",
            "Sharp Shadow", "Spore Shroom", "Longnail", "Shaman Stone", "Soul Catcher",
            "Soul Eater", "Glowing Womb", "Fragile Heart", "Fragile Greed", "Fragile Strength",
            "Nailmaster's Glory", "Joni's Blessing", "Shape of Unn", "Hiveblood", "Dream Wielder",
            "Dashmaster", "Quick Slash", "Spell Twister", "Deep Focus", "Grubberfly's Elegy",
            "Kingsoul", "Sprintmaster", "Dreamshield", "Weaversong", "Grimmchild"
        };
        public static string Get(int id)
        {
            return id > 0 && id < Names.Length ? Names[id] : "Charm #" + id;
        }
        public static string Get(int id, PlayerData pd)
        {
            if (pd != null)
            {
                switch (id)
                {
                    case 23: if (TrailMetadata.PdBool(pd, "fragileHealth_unbreakable")) return "Unbreakable Heart"; break;
                    case 24: if (TrailMetadata.PdBool(pd, "fragileGreed_unbreakable")) return "Unbreakable Greed"; break;
                    case 25: if (TrailMetadata.PdBool(pd, "fragileStrength_unbreakable")) return "Unbreakable Strength"; break;
                    case 36: if (TrailMetadata.PdBool(pd, "gotShadeCharm")) return "Void Heart"; break;
                    case 40: if (TrailMetadata.PdInt(pd, "grimmChildLevel") >= 5) return "Carefree Melody"; break;
                }
            }
            return Get(id);
        }
    }
    public class TrailVisit
    {
        public string scene;
        public int first, last;          // индексы кадров героя [first, last)
        public float tStart, tEnd;       // время (от начала записи) первого и последнего кадра

        public static List<TrailVisit> Split(List<TrailHeroFrame> heroes)
        {
            var list = new List<TrailVisit>();
            TrailVisit cur = null;
            for (int i = 0; i < heroes.Count; i++)
            {
                if (cur == null || heroes[i].scene != cur.scene)
                {
                    cur = new TrailVisit { scene = heroes[i].scene, first = i, tStart = heroes[i].time };
                    list.Add(cur);
                }
                cur.last = i + 1;
                cur.tEnd = heroes[i].time;
            }
            return list;
        }
        public static int[] AssignActions(List<TrailVisit> visits, List<ActionEntityFrame> actions)
        {
            var owner = new int[actions.Count];
            for (int a = 0; a < actions.Count; a++)
            {
                owner[a] = -1;
                float best = float.MaxValue;
                float t = actions[a].timeFromStart;
                for (int v = 0; v < visits.Count; v++)
                {
                    if (visits[v].scene != actions[a].scene) continue;
                    float d = t < visits[v].tStart ? visits[v].tStart - t
                            : t > visits[v].tEnd ? t - visits[v].tEnd : 0f;
                    if (d < best) { best = d; owner[a] = v; }
                }
            }
            return owner;
        }
    }
    internal static class ReadmeText
    {
        private static string cached;

        public static string English => Part("<!--LANG:EN-->", "<!--LANG:RU-->");
        public static string Russian => Part("<!--LANG:RU-->", null);

        private static string Part(string from, string to)
        {
            string all = Load();
            if (string.IsNullOrEmpty(all)) return null;

            int a = all.IndexOf(from, StringComparison.Ordinal);
            if (a < 0) return all;              // маркеров нет - показываем как есть
            a += from.Length;

            int b = to != null ? all.IndexOf(to, a, StringComparison.Ordinal) : -1;
            return (b < 0 ? all.Substring(a) : all.Substring(a, b - a)).Trim();
        }

        private static string Load()
        {
            if (cached != null) return cached;
            cached = string.Empty;

            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                foreach (string name in asm.GetManifestResourceNames())
                {
                    if (!name.EndsWith("ReadMe.md", StringComparison.OrdinalIgnoreCase)) continue;
                    using (Stream s = asm.GetManifestResourceStream(name))
                    using (var r = new StreamReader(s))
                        cached = r.ReadToEnd();
                    break;
                }

                if (cached.Length == 0)
                {
                    string near = Path.Combine(Path.GetDirectoryName(asm.Location) ?? "", "ReadMe.md");
                    if (File.Exists(near)) cached = File.ReadAllText(near);
                }
            }
            catch (Exception e)
            {
                Modding.Logger.LogError("[GhostMacro] ReadMe: " + e.Message);
            }

            return cached;
        }
    }
    internal static class ReplayFileTools
    {
        public static string SaveDirectory
        {
            get
            {
                string dir = NormalizePath(Path.Combine(Application.persistentDataPath, "GhostMacro"));
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            char sep = Path.DirectorySeparatorChar;
            path = path.Replace('/', sep).Replace('\\', sep);
            try { path = Path.GetFullPath(path); } catch { }
            return path;
        }

        private static bool IsWindows =>
            Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;

        private static bool IsMac =>
            Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor;
        public static void ShowInExplorer(string fileOrDirectory)
        {
            string target = NormalizePath(fileOrDirectory);
            bool isFile = File.Exists(target);
            string dir = isFile ? Path.GetDirectoryName(target) : target;

            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }

            string how = null;
            try
            {
                if (IsWindows)
                {
                    if (isFile && WinSelectFile(target)) how = "SHOpenFolderAndSelectItems";
                    else if (WinOpenFolder(dir)) how = "ShellExecuteW";
                }
                else if (IsMac)
                {
                    Process.Start("open", isFile ? "-R \"" + target + "\"" : "\"" + dir + "\"");
                    how = "open";
                }
                else
                {
                    Process.Start("xdg-open", "\"" + dir + "\"");
                    how = "xdg-open";
                }
            }
            catch (Exception e)
            {
                Modding.Logger.LogError("[GhostMacro] Проводник: " + e.Message);
            }

            if (how == null)
            {
                try
                {
                    Application.OpenURL(new Uri(dir).AbsoluteUri);
                    how = "Application.OpenURL";
                }
                catch (Exception e)
                {
                    Modding.Logger.LogError("[GhostMacro] Проводник (OpenURL): " + e.Message);
                }
            }

            Modding.Logger.Log($"[GhostMacro] Открыть в проводнике: '{(isFile ? target : dir)}' -> {how ?? "НЕ УДАЛОСЬ"}");
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ILCreateFromPathW(string pszPath);

        [DllImport("shell32.dll")]
        private static extern void ILFree(IntPtr pidl);

        [DllImport("shell32.dll")]
        private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[] apidl, uint dwFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ShellExecuteW(IntPtr hwnd, string lpOperation, string lpFile,
            string lpParameters, string lpDirectory, int nShowCmd);

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

        private const int SW_SHOWNORMAL = 1;
        private const uint COINIT_APARTMENTTHREADED = 0x2;
        private static bool WinSelectFile(string file)
        {
            try
            {
                CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);   // COM уже может быть инициализирован - это нормально
                IntPtr pidl = ILCreateFromPathW(file);
                if (pidl == IntPtr.Zero) return false;
                try { return SHOpenFolderAndSelectItems(pidl, 0, null, 0) == 0; }
                finally { ILFree(pidl); }
            }
            catch (Exception e)
            {
                Modding.Logger.LogError("[GhostMacro] SHOpenFolderAndSelectItems: " + e.Message);
                return false;
            }
        }
        private static bool WinOpenFolder(string dir)
        {
            try
            {
                long r = ShellExecuteW(IntPtr.Zero, "open", dir, null, null, SW_SHOWNORMAL).ToInt64();
                if (r > 32) return true;
                Modding.Logger.LogError("[GhostMacro] ShellExecuteW вернул " + r);
            }
            catch (Exception e)
            {
                Modding.Logger.LogError("[GhostMacro] ShellExecuteW: " + e.Message);
            }
            return false;
        }
        public static bool CopyFileToClipboard(string path)
        {
            path = NormalizePath(path);

            if (IsWindows)
            {
                try
                {
                    if (CopyFileDropWindows(path)) return true;
                }
                catch (Exception e)
                {
                    Modding.Logger.LogError("[GhostMacro] Буфер обмена: " + e.Message);
                }
            }

            GUIUtility.systemCopyBuffer = path;
            return false;
        }

        private const uint CF_HDROP = 15;
        private const uint GMEM_MOVEABLE = 0x0002;
        private const uint GMEM_ZEROINIT = 0x0040;

        [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr hMem);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(IntPtr hMem);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalFree(IntPtr hMem);

        private static bool CopyFileDropWindows(string path)
        {
            byte[] files = Encoding.Unicode.GetBytes(path + "\0\0");
            byte[] data = new byte[20 + files.Length];
            BitConverter.GetBytes(20).CopyTo(data, 0);   // pFiles - смещение списка
            BitConverter.GetBytes(1).CopyTo(data, 16);   // fWide = TRUE (UTF-16)
            files.CopyTo(data, 20);

            IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (UIntPtr)data.Length);
            if (hMem == IntPtr.Zero) return false;

            IntPtr ptr = GlobalLock(hMem);
            if (ptr == IntPtr.Zero) { GlobalFree(hMem); return false; }
            Marshal.Copy(data, 0, ptr, data.Length);
            GlobalUnlock(hMem);

            if (!OpenClipboard(IntPtr.Zero)) { GlobalFree(hMem); return false; }
            try
            {
                EmptyClipboard();
                if (SetClipboardData(CF_HDROP, hMem) == IntPtr.Zero)
                {
                    GlobalFree(hMem);   // при успехе память принадлежит буферу обмена
                    return false;
                }
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }
        public static string SanitizeName(string name)
        {
            if (name == null) return string.Empty;
            char[] bad = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name.Trim()) sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
            return sb.ToString().Trim().TrimEnd('.');
        }
        public static string Rename(string path, string newName, out string error)
        {
            error = null;
            string clean = SanitizeName(newName);
            if (string.IsNullOrEmpty(clean)) { error = "Empty name"; return null; }

            string target = Path.Combine(Path.GetDirectoryName(path), clean + Path.GetExtension(path));
            if (string.Equals(target, path, StringComparison.OrdinalIgnoreCase)) return path;
            if (File.Exists(target)) { error = "A file with this name already exists"; return null; }

            try { File.Move(path, target); }
            catch (Exception e) { error = e.Message; return null; }
            return target;
        }

        public static string FormatDuration(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int m = (int)(seconds / 60f);
            float s = seconds - m * 60;
            return m > 0 ? $"{m}:{s:00.00}" : $"{s:0.00}s";
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024f).ToString("0.0") + " KB";
            return (bytes / 1024f / 1024f).ToString("0.00") + " MB";
        }

        public static string CharmName(int id) { return CharmNames.Get(id); }
    }
}
