using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace GhostMacro
{
    public class ReplayMenuUI : MonoBehaviour
    {
        private static ReplayMenuUI inst;
        private static bool opened;

        public static bool IsOpen => opened;
        public static bool IsTyping => opened && inst != null &&
                                       (typingNow || inst.renamingPath != null || inst.noteEditPath != null);

        public static void Toggle()
        {
            if (inst == null)
            {
                var go = new GameObject("GhostMacro ReplayManager");
                DontDestroyOnLoad(go);
                inst = go.AddComponent<ReplayMenuUI>();
            }

            opened = !opened;
            if (opened) inst.OnOpen();
            else inst.OnClose();
        }
        private static string Dir => ReplayFileTools.SaveDirectory;
        private static string BackupDir => Path.Combine(Dir, "Backups");
        private enum Mode { List, Rooms, Help, Workshop }

        private sealed class Entry
        {
            public bool current;
            public string path;
            public bool ghost;
            public long size;
            public DateTime modified;
            public TrailMetadata meta;
            public string Name => current ? "Unsaved recording" : Path.GetFileNameWithoutExtension(path);
        }

        private sealed class RoomView
        {
            public TrailVisit visit;
            public List<int> actions = new List<int>();
            public float workLo, workHi = 1f;     // положение ползунка
            public float appLo, appHi = 1f;       // применённая обрезка (Trim)
            public Texture2D thumb;
            public float thumbLo = -1f, thumbHi = -1f;
            public bool thumbActions;

            public bool IsFull => appLo <= 0f && appHi >= 1f;
            public bool IsRemoved => appHi - appLo <= 0.0005f;
            public bool HasPending => Mathf.Abs(workLo - appLo) > 0.0001f || Mathf.Abs(workHi - appHi) > 0.0001f;
        }

        private sealed class RoomSession
        {
            public bool current;      // редактируется текущий след из памяти
            public string path;
            public GhostTrailData data;
            public TrailMetadata meta;
            public List<RoomView> rooms = new List<RoomView>();
            public int[] actionOwner;

            public bool Dirty
            {
                get
                {
                    for (int i = 0; i < rooms.Count; i++) if (!rooms[i].IsFull) return true;
                    return false;
                }
            }
        }

        private Mode mode = Mode.List;
        private readonly List<Entry> entries = new List<Entry>();
        private string selectedPath;
        private Vector2 listScroll, detailScroll, roomScroll;

        private string renamingPath;
        private string renameText;
        private bool focusRename;
        private string currentSaveName;
        private string roomsSaveName;
        private string workshopName;
        private static bool typingNow;

        private string noteEditPath;     // файл, чья заметка сейчас редактируется
        private string noteText;

        private string status;
        private float statusUntil;

        private RoomSession session;
        private bool showActions = true;
        private sealed class WorkshopSource
        {
            public bool current;
            public string path;
            public string name;
            public GhostTrailData data;
            public List<RoomView> rooms = new List<RoomView>();
        }

        private sealed class WorkshopItem
        {
            public WorkshopSource source;
            public RoomView room;
        }

        private readonly List<WorkshopSource> wsSources = new List<WorkshopSource>();
        private readonly List<WorkshopItem> project = new List<WorkshopItem>();
        private WorkshopSource wsSelected;
        private Vector2 wsFilesScroll, wsRoomsScroll, wsProjectScroll;

        private Vector2 helpScroll;
        private bool helpRussian = IsGameRussian();

        private static bool IsGameRussian()
        {
            try { return Language.Language.CurrentLanguage().ToString() == "RU"; }
            catch { return false; }
        }

        private bool inputBlocked;
        private bool prevHeroInput;
        private Action deferred;
        private void Defer(Action a) { deferred += a; }
        private void OnOpen()
        {
            mode = Mode.List;
            renamingPath = null;
            currentSaveName = DefaultName("trail");
            Refresh();
            var ih = InputHandler.Instance;
            if (ih != null && ih.inputActions != null)
            {
                prevHeroInput = ih.inputActions.Enabled;
                ih.inputActions.Enabled = false;
                inputBlocked = true;
            }
        }

        private void OnClose()
        {
            renamingPath = null;
            CloseSession();
            CloseWorkshop();

            if (inputBlocked)
            {
                var ih = InputHandler.Instance;
                if (ih != null && ih.inputActions != null) ih.inputActions.Enabled = prevHeroInput;
                inputBlocked = false;
            }
        }

        private void Update()
        {
            if (!opened) return;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private static HitboxTrailBehaviour Behaviour => UnityEngine.Object.FindObjectOfType<HitboxTrailBehaviour>();

        private void Refresh()
        {
            entries.Clear();
            currentMeta = null;
            if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);

            var files = new List<string>();
            files.AddRange(Directory.GetFiles(Dir, "*" + GhostTrailFormat.Extension));
            files.AddRange(Directory.GetFiles(Dir, "*.json"));

            foreach (string f in files)
            {
                var info = new FileInfo(f);
                bool ghost = GhostTrailFormat.IsGhostFile(f);
                entries.Add(new Entry
                {
                    path = f,
                    ghost = ghost,
                    size = info.Length,
                    modified = info.LastWriteTime,
                    meta = ghost ? GhostTrailFormat.ReadMetadata(f) : null
                });
            }

            entries.Sort((a, b) => b.modified.CompareTo(a.modified));
            HitboxTrailBehaviour bh = Behaviour;
            if (bh != null && bh.TrailUnsaved)
                entries.Insert(0, new Entry { current = true, modified = DateTime.Now });
            if (Selected == null) selectedPath = entries.Count > 0 ? entries[0].path : null;
        }

        private Entry Selected
        {
            get
            {
                for (int i = 0; i < entries.Count; i++) if (entries[i].path == selectedPath) return entries[i];
                return null;
            }
        }

        private void SetStatus(string text, float seconds = 5f)
        {
            status = text;
            statusUntil = Time.unscaledTime + seconds;
        }
        private const float VH = 1080f;   // виртуальная высота интерфейса (масштабируется под экран)

        private void OnGUI()
        {
            if (!opened) return;

            Cursor.visible = true;
            GUI.depth = -1000;
            EnsureStyles();

            float scale = Screen.height / VH;
            float vw = Screen.width / scale;
            Matrix4x4 prevMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            if (Event.current.type == EventType.Layout && deferred != null)
            {
                Action d = deferred;
                deferred = null;
                d();
            }
            if (!opened) { GUI.matrix = prevMatrix; return; }

            HandleKeys();
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0, 0, vw, VH), white);
            GUI.color = Color.white;

            float pw = Mathf.Min(1600f, vw - 80f), ph = 940f;
            Rect panel = new Rect((vw - pw) * 0.5f, (VH - ph) * 0.5f, pw, ph);
            GUI.Box(panel, GUIContent.none, panelStyle);

            GUILayout.BeginArea(new Rect(panel.x + 18, panel.y + 14, panel.width - 36, panel.height - 28));
            DrawHeader();
            GUILayout.Space(8);

            float contentH = panel.height - 28 - 56 - 40;
            float contentW = panel.width - 36;
            if (mode == Mode.List) DrawList(contentW, contentH);
            else if (mode == Mode.Help) DrawHelp(contentH);
            else if (mode == Mode.Workshop) DrawWorkshop(contentW, contentH);
            else DrawRooms(contentH);

            GUILayout.Space(6);
            GUILayout.Label(Time.unscaledTime < statusUntil ? status : " ", statusStyle, GUILayout.Height(28));
            GUILayout.EndArea();

            if (Event.current.type == EventType.Repaint)
                typingNow = GUI.GetNameOfFocusedControl().StartsWith("ghost_");

            GUI.matrix = prevMatrix;
        }
        private string NameField(string controlName, string value, float width)
        {
            GUI.SetNextControlName(controlName);
            return GUILayout.TextField(value ?? "", textFieldStyle, GUILayout.Width(width));
        }

        private void HandleKeys()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;

            if (e.keyCode == KeyCode.Escape)
            {
                if (renamingPath != null) Defer(() => renamingPath = null);
                else if (mode == Mode.Help || mode == Mode.Workshop) Defer(() => mode = Mode.List);
                else if (noteEditPath != null) Defer(() => noteEditPath = null);
                else if (mode == Mode.Rooms) Defer(() => { CloseSession(); mode = Mode.List; });
                else Defer(Toggle);
                e.Use();
            }
            else if (renamingPath != null && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                string typed = renameText, path = renamingPath;
                Defer(() => { renamingPath = null; DoRename(path, typed); });
                e.Use();
            }
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal(GUILayout.Height(48));
            if (mode == Mode.List)
            {
                GUILayout.Label("REPLAYS", titleStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Workshop", buttonStyle, GUILayout.Width(130)))
                    Defer(() => { mode = Mode.Workshop; wsRoomsScroll = wsProjectScroll = Vector2.zero; });
                if (GUILayout.Button("Help", buttonStyle, GUILayout.Width(90)))
                    Defer(() => { mode = Mode.Help; helpScroll = Vector2.zero; });
                if (GUILayout.Button("Open Folder", buttonStyle, GUILayout.Width(150))) ReplayFileTools.ShowInExplorer(Dir);
                if (GUILayout.Button("Refresh", buttonStyle, GUILayout.Width(110))) Defer(() => { Refresh(); SetStatus("List refreshed", 2f); });
            }
            else if (mode == Mode.Workshop)
            {
                bool backWs = GUILayout.Button("<  Back", buttonStyle, GUILayout.Width(110));
                GUILayout.Space(12);
                GUILayout.Label("WORKSHOP", titleStyle);
                GUILayout.FlexibleSpace();

                if (string.IsNullOrEmpty(workshopName)) workshopName = DefaultName("workshop");
                workshopName = NameField("ghost_wsname", workshopName, 300f);

                GUI.enabled = project.Count > 0;
                if (GUILayout.Button("Build & Save", buttonStyle, GUILayout.Width(170)))
                {
                    string typed = workshopName;
                    Defer(() => BuildWorkshopFile(typed));
                }
                if (GUILayout.Button("Clear", buttonStyle, GUILayout.Width(100))) Defer(() => project.Clear());
                GUI.enabled = true;

                if (backWs) Defer(() => mode = Mode.List);
            }
            else if (mode == Mode.Help)
            {
                bool backFromHelp = GUILayout.Button("<  Back", buttonStyle, GUILayout.Width(110));
                GUILayout.Space(12);
                GUILayout.Label("HELP", titleStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(helpRussian ? "Язык: Русский" : "Language: English", buttonStyle, GUILayout.Width(220)))
                    Defer(() => { helpRussian = !helpRussian; helpScroll = Vector2.zero; });
                if (backFromHelp) Defer(() => mode = Mode.List);
            }
            else
            {
                bool back = GUILayout.Button("<  Back", buttonStyle, GUILayout.Width(110));
                GUILayout.Space(12);
                GUILayout.Label("ROOMS  -  " + (session == null ? "" : session.current ? "Current recording"
                                                : Path.GetFileName(session.path)), titleStyle);
                GUILayout.FlexibleSpace();

                if (GUILayout.Button(showActions ? "Thumbnails: Trail + Actions" : "Thumbnails: Trail Only",
                        buttonStyle, GUILayout.Width(280)))
                    showActions = !showActions;

                bool isCurrent = session != null && session.current;
                if (isCurrent)
                {
                    if (string.IsNullOrEmpty(roomsSaveName)) roomsSaveName = DefaultName("trail");
                    roomsSaveName = NameField("ghost_roomsname", roomsSaveName, 300f);
                }

                GUI.enabled = session != null && (isCurrent || session.Dirty);
                if (GUILayout.Button(isCurrent ? "Save as new file" : "Save (backup original)",
                        buttonStyle, GUILayout.Width(250)))
                {
                    if (isCurrent)
                    {
                        string typed = roomsSaveName;
                        Defer(() => SaveCurrentSession(typed));
                    }
                    else Defer(SaveSession);
                }
                GUI.enabled = true;

                if (back) Defer(() => { CloseSession(); mode = Mode.List; });
            }

            if (GUILayout.Button("Close", buttonStyle, GUILayout.Width(100))) Defer(Toggle);
            GUILayout.EndHorizontal();
        }
        private void DrawList(float width, float height)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(height));

            float listW = Mathf.Clamp(width * 0.56f, 560f, 900f);
            GUILayout.BeginVertical(GUILayout.Width(listW));
            listScroll = GUILayout.BeginScrollView(listScroll, false, true, GUILayout.Height(height));

            if (entries.Count == 0)
                GUILayout.Label("No replays yet. Record a trail and press Save Trail (M).", normalStyle);

            Entry load = null;
            for (int i = 0; i < entries.Count; i++)
                if (DrawRow(entries[i])) load = entries[i];

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.Space(14);

            GUILayout.BeginVertical(cardStyle, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            detailScroll = GUILayout.BeginScrollView(detailScroll, false, true);
            DrawDetails(Selected);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            if (load != null) Defer(() => LoadEntry(load));
        }
        private bool DrawRow(Entry e)
        {
            bool loadClicked = false;
            bool sel = e.current ? selectedPath == null : e.path == selectedPath;
            GUILayout.BeginVertical(sel ? rowSelStyle : rowStyle);

            if (renamingPath != null && renamingPath == e.path)
            {
                GUILayout.BeginHorizontal();
                renameText = NameField("ghost_rename", renameText, 420f);
                if (focusRename && Event.current.type == EventType.Repaint)
                {
                    GUI.FocusControl("ghost_rename");
                    focusRename = false;
                }
                if (GUILayout.Button("OK", buttonStyle, GUILayout.Width(70)))
                {
                    string typed = renameText, path = e.path;
                    Defer(() => { renamingPath = null; DoRename(path, typed); });
                }
                if (GUILayout.Button("Cancel", buttonStyle, GUILayout.Width(90))) Defer(() => renamingPath = null);
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label(e.Name, rowTitleStyle);
            }

            GUILayout.Label(Subtitle(e), smallStyle);

            GUILayout.BeginHorizontal();

            if (e.current)
            {
                if (string.IsNullOrEmpty(currentSaveName)) currentSaveName = DefaultName("trail");
                currentSaveName = NameField("ghost_savename", currentSaveName, 330f);

                if (GUILayout.Button("Save", buttonStyle, GUILayout.Width(90)))
                {
                    string typed = currentSaveName;
                    Defer(() => SaveCurrentTrailAs(typed));
                }
                if (GUILayout.Button("Rooms", buttonStyle, GUILayout.Width(100))) Defer(() => OpenRooms(e));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                RowClick(e);
                GUILayout.Space(6);
                return false;
            }

            if (GUILayout.Button("Load", buttonStyle, GUILayout.Width(90))) loadClicked = true;

            if (e.ghost)
            {
                if (GUILayout.Button("Rooms", buttonStyle, GUILayout.Width(100))) Defer(() => OpenRooms(e));
            }
            else
            {
                if (GUILayout.Button("Convert", buttonStyle, GUILayout.Width(100))) Defer(() => ConvertJson(e));
            }

            if (GUILayout.Button("Copy", buttonStyle, GUILayout.Width(90)))
            {
                bool file = ReplayFileTools.CopyFileToClipboard(e.path);
                SetStatus(file ? "File copied to clipboard - paste it anywhere with Ctrl+V"
                               : "File path copied to clipboard");
            }

            if (GUILayout.Button("Rename", buttonStyle, GUILayout.Width(100)))
            {
                Entry target = e;
                Defer(() =>
                {
                    renamingPath = target.path;
                    renameText = target.Name;
                    focusRename = true;
                    selectedPath = target.path;
                });
            }

            if (GUILayout.Button("Show", buttonStyle, GUILayout.Width(90))) ReplayFileTools.ShowInExplorer(e.path);

            DeleteButton(e);

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            RowClick(e);
            GUILayout.Space(6);
            return loadClicked;
        }
        private void RowClick(Entry e)
        {
            Rect r = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                r.Contains(Event.current.mousePosition) && selectedPath != e.path)
            {
                Defer(() => { selectedPath = e.path; detailScroll = Vector2.zero; });
            }
        }
        private void SaveNote(Entry e)
        {
            try
            {
                TrailMetadata meta = e.meta != null ? e.meta.CloneEnvironment() : BuildMissingMeta(e);
                if (e.meta != null) meta.rooms = e.meta.rooms;   // статистика не меняется - трейл тот же
                CopyStats(e.meta, meta);
                meta.note = string.IsNullOrEmpty(noteText) ? null : noteText.Trim();

                GhostTrailFormat.RewriteMetadata(e.path, meta);
                noteEditPath = null;
                Refresh();
                SetStatus("Note saved", 3f);
            }
            catch (Exception ex)
            {
                SetStatus("Failed to save note: " + ex.Message);
            }
        }
        private static TrailMetadata BuildMissingMeta(Entry e)
        {
            GhostTrailData data = GhostTrailFormat.ReadRaw(e.path);
            var meta = new TrailMetadata { createdUtc = File.GetLastWriteTimeUtc(e.path).ToString("o") };
            meta.FillStats(data.heroFrames, data.actionFrames);
            return meta;
        }

        private void AddMetadata(Entry e)
        {
            try
            {
                GhostTrailFormat.RewriteMetadata(e.path, BuildMissingMeta(e));
                Refresh();
                SetStatus("Metadata added (player info is unknown for old recordings)", 5f);
            }
            catch (Exception ex)
            {
                SetStatus("Failed: " + ex.Message);
            }
        }
        private void RecalculateStats(Entry e)
        {
            try
            {
                GhostTrailData data = GhostTrailFormat.ReadRaw(e.path);
                TrailMetadata meta = e.meta != null
                    ? e.meta.CloneEnvironment()
                    : new TrailMetadata { createdUtc = File.GetLastWriteTimeUtc(e.path).ToString("o") };

                meta.FillStats(data.heroFrames, data.actionFrames);
                if (TrailMetadata.IsWrongGameVersion(meta.gameVersion)) meta.gameVersion = TrailMetadata.GameVersion;

                GhostTrailFormat.RewriteMetadata(e.path, meta);
                Refresh();
                SetStatus("Stats recalculated from the trail", 4f);
            }
            catch (Exception ex)
            {
                SetStatus("Recalculate failed: " + ex.Message);
            }
        }

        private static void CopyStats(TrailMetadata from, TrailMetadata to)
        {
            if (from == null) return;
            to.duration = from.duration;
            to.heroFrames = from.heroFrames;
            to.actionFrames = from.actionFrames;
            to.actionEntities = from.actionEntities;
            to.startScene = from.startScene;
            to.endScene = from.endScene;
            to.sceneTransitions = from.sceneTransitions;
            to.uniqueScenes = from.uniqueScenes;
            to.distance = from.distance;
            to.inputs = from.inputs;
        }

        private TrailMetadata currentMeta;

        private TrailMetadata CurrentMeta()
        {
            if (currentMeta != null) return currentMeta;
            HitboxTrailBehaviour bh = Behaviour;
            if (bh == null || !bh.HasTrail) return null;

            GhostTrailData data = bh.ExportCurrentTrail();
            currentMeta = TrailMetadata.CreateForCurrentSession();
            currentMeta.FillStats(data.heroFrames, data.actionFrames);
            return currentMeta;
        }
        private const float DeleteHoldTime = 1f;
        private string deletingPath;
        private float deleteHold;
        private bool deleteLocked;
        private float deleteUnlockAt;

        private void DeleteButton(Entry e)
        {
            if (deleteLocked)
            {
                bool released = Event.current.rawType == EventType.MouseUp || !Input.GetMouseButton(0);
                if (released && Time.unscaledTime >= deleteUnlockAt) deleteLocked = false;
            }

            if (deleteLocked)
            {
                bool prevEnabled = GUI.enabled;
                GUI.enabled = false;
                GUILayout.Button("Delete", buttonStyle, GUILayout.Width(120));
                GUI.enabled = prevEnabled;
                return;
            }

            bool active = deletingPath == e.path;
            string label = active ? $"Hold... {Mathf.Clamp01(deleteHold / DeleteHoldTime) * 100f:0}%" : "Delete";

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = active ? new Color(1f, 0.35f, 0.3f) : new Color(0.85f, 0.45f, 0.42f);
            bool held = GUILayout.RepeatButton(label, buttonStyle, GUILayout.Width(120));
            GUI.backgroundColor = prevBg;

            if (held)
            {
                if (!active) { deletingPath = e.path; deleteHold = 0f; }
                if (Event.current.type == EventType.Repaint) deleteHold += Time.unscaledDeltaTime;

                if (deleteHold >= DeleteHoldTime)
                {
                    Entry target = e;
                    deletingPath = null;
                    deleteHold = 0f;
                    deleteLocked = true;                                  // до отпускания мыши
                    deleteUnlockAt = Time.unscaledTime + 0.25f;
                    Defer(() => DeleteEntry(target));
                }
            }
            else if (active && Event.current.type == EventType.Repaint)
            {
                deletingPath = null;   // отпустили раньше времени
                deleteHold = 0f;
            }
        }
        private void DeleteEntry(Entry e)
        {
            try
            {
                Directory.CreateDirectory(BackupDir);
                string backup = Path.Combine(BackupDir,
                    Path.GetFileNameWithoutExtension(e.path) + "_deleted_" +
                    DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + Path.GetExtension(e.path));
                File.Copy(e.path, backup, true);
                File.Delete(e.path);

                if (selectedPath == e.path) selectedPath = null;
                Refresh();
                SetStatus("Deleted. A copy is kept in Backups/" + Path.GetFileName(backup), 7f);
            }
            catch (Exception ex)
            {
                SetStatus("Delete failed: " + ex.Message, 8f);
            }
        }

        private string Subtitle(Entry e)
        {
            if (e.current)
            {
                TrailMetadata cm = CurrentMeta();
                return cm == null ? "in memory, not saved yet"
                    : $"in memory, not saved yet   {ReplayFileTools.FormatDuration(cm.duration)}   " +
                      $"{cm.rooms.Count}" + (cm.rooms.Count == 1 ? " room" : " rooms");
            }

            string s = e.modified.ToString("yyyy-MM-dd HH:mm") + "   " + ReplayFileTools.FormatSize(e.size);
            if (e.meta != null)
                s += "   " + ReplayFileTools.FormatDuration(e.meta.duration) + "   " + e.meta.rooms.Count +
                     (e.meta.rooms.Count == 1 ? " room" : " rooms");
            if (!e.ghost) s += "   [json]";
            return s;
        }

        private void DrawDetails(Entry e)
        {
            if (e == null)
            {
                GUILayout.Label("Select a replay", normalStyle);
                return;
            }

            GUILayout.Label(e.Name, rowTitleStyle);
            GUILayout.Space(6);

            if (e.current)
            {
                TrailMetadata cm = CurrentMeta();
                Section("Unsaved recording");
                GUILayout.Label("This trail is only in memory. Save it, or open Rooms to trim it first.",
                                wrapStyle);
                if (cm != null) DrawStats(cm, e);
                return;
            }

            Section("File");
            Field("Name", Path.GetFileName(e.path));
            Field("Size", ReplayFileTools.FormatSize(e.size));
            Field("Modified", e.modified.ToString("yyyy-MM-dd HH:mm:ss"));

            if (!e.ghost)
            {
                Field("Format", "JSON (legacy)");
                GUILayout.Space(8);
                GUILayout.Label("Convert it to .ghost to see metadata and edit rooms.", smallStyle);
                return;
            }

            TrailMetadata m = e.meta;
            if (m == null)
            {
                Field("Format", ".ghost (older version, no metadata)");
                GUILayout.Space(8);
                if (GUILayout.Button("Add metadata", buttonStyle, GUILayout.Width(180))) Defer(() => AddMetadata(e));
                GUILayout.Label("Recording stats will be calculated from the trail; player info is unknown.", smallStyle);
                return;
            }
            Field("Format", ".ghost v" + m.formatVersion);
            Section("Note");
            if (noteEditPath == e.path)
            {
                GUI.SetNextControlName("ghost_note");
                noteText = GUILayout.TextArea(noteText ?? "", textAreaStyle, GUILayout.MinHeight(80));
                if (GUI.GetNameOfFocusedControl() != "ghost_note") GUI.FocusControl("ghost_note");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Save note", buttonStyle, GUILayout.Width(130))) Defer(() => SaveNote(e));
                if (GUILayout.Button("Cancel", buttonStyle, GUILayout.Width(100))) Defer(() => noteEditPath = null);
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label(string.IsNullOrEmpty(m.note) ? "(no note)" : m.note,
                                string.IsNullOrEmpty(m.note) ? smallStyle : wrapStyle);
                if (GUILayout.Button("Edit note", buttonStyle, GUILayout.Width(130)))
                    Defer(() => { noteEditPath = e.path; noteText = m.note ?? ""; });
            }

            DrawStats(m, e);
        }
        private void DrawStats(TrailMetadata m, Entry e)
        {
            Section("Recording");
            if (e != null && !e.current)
            {
                if (GUILayout.Button("Recalculate stats", buttonStyle, GUILayout.Width(200))) Defer(() => RecalculateStats(e));
            }
            if (m.inputs == null || m.inputs.Count == 0)
                GUILayout.Label("Saved by an older version - press Recalculate stats to fill inputs, distance and room details. " +
                                "Player info appears in recordings saved after the update.", wrapSmallStyle);
            Field("Recorded", LocalTime(m.createdUtc));
            if (!string.IsNullOrEmpty(m.editedUtc)) Field("Edited", LocalTime(m.editedUtc));
            if (!string.IsNullOrEmpty(m.convertedFrom)) Field("Converted from", m.convertedFrom);
            Field("Duration", ReplayFileTools.FormatDuration(m.duration));
            Field("Frames", $"{m.heroFrames} hero, {m.actionFrames} actions ({m.actionEntities} effects)");
            Field("Record rate", m.recordRate);
            if (m.avgGameFps > 0f) Field("Game FPS (avg)", m.avgGameFps.ToString("0"));
            Field("Resolution", m.resolution);
            Field("Route", string.IsNullOrEmpty(m.startScene) ? null
                : m.startScene == m.endScene ? m.startScene : m.startScene + "  ->  " + m.endScene);
            Field("Rooms", $"{m.rooms.Count} visits, {m.uniqueScenes} unique, {m.sceneTransitions} transitions");
            Field("Distance", m.distance.ToString("0.0") + " units");
            if (m.inputs != null && m.inputs.Count > 0)
            {
                Section("Inputs (presses)");
                foreach (var kv in m.inputs)
                    if (kv.Value > 0) Field(kv.Key, kv.Value.ToString());
            }
            Section("Player");
            if (m.saveSlot >= 0) Field("Save slot", (m.saveSlot).ToString());
            Field("Game mode", m.gameMode);
            if (m.completion >= 0f) Field("Completion", m.completion.ToString("0.#") + "%");
            if (m.playTimeHours >= 0f) Field("Play time", PlayTime(m.playTimeHours));
            if (m.masks > 0) Field("Masks", m.masks.ToString());
            Field("Soul vessels", m.soulVessels.ToString());
            Field("Nail", TrailMetadata.NailName(m.nailLevel));
            if (m.charmNotches > 0) Field("Notches", $"{m.notchesUsed} / {m.charmNotches}");

            var spells = new List<string>();
            AddIf(spells, TrailMetadata.SpellName("fireball", m.fireballLevel));
            AddIf(spells, TrailMetadata.SpellName("quake", m.quakeLevel));
            AddIf(spells, TrailMetadata.SpellName("scream", m.screamLevel));
            Field("Spells", spells.Count > 0 ? string.Join(", ", spells.ToArray()) : "none");

            if (m.abilities != null && m.abilities.Count > 0)
                Field("Abilities", string.Join(", ", m.abilities.ToArray()));
            Section("Charms");
            var names = new List<string>();
            if (m.charmNames != null && m.charmNames.Count > 0) names.AddRange(m.charmNames);
            else if (m.charms != null) foreach (int id in m.charms) names.Add(CharmNames.Get(id));
            GUILayout.Label(names.Count > 0 ? string.Join(", ", names.ToArray()) : "none",
                            names.Count > 0 ? wrapStyle : smallStyle);
            Section("Rooms (" + m.rooms.Count + ")");
            for (int i = 0; i < m.rooms.Count; i++)
            {
                TrailRoomMeta r = m.rooms[i];
                GUILayout.Label($"{i + 1}. {r.scene}   {ReplayFileTools.FormatDuration(r.duration)}   " +
                                $"{r.distance:0} u", normalStyle);
                GUILayout.Label($"     {r.heroFrames} hero / {r.actionFrames} action frames   " +
                                $"jumps {r.jumps}, dashes {r.dashes}, attacks {r.attacks}, casts {r.casts}", smallStyle);
            }

            Section("Versions");
            Field("Game", TrailMetadata.IsWrongGameVersion(m.gameVersion) ? "unknown (old file)" : m.gameVersion);
            if (!string.IsNullOrEmpty(m.apiVersion)) Field("Modding API", m.apiVersion);
            Field("Mod", m.modVersion);
        }

        private void Section(string title)
        {
            GUILayout.Space(10);
            GUILayout.Label(title, sectionStyle);
        }

        private static void AddIf(List<string> list, string s) { if (!string.IsNullOrEmpty(s)) list.Add(s); }

        private static string PlayTime(float hours)
        {
            int h = (int)hours;
            int m = (int)((hours - h) * 60f);
            return $"{h}h {m:00}m";
        }

        private void Field(string name, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, fieldNameStyle, GUILayout.Width(170));
            GUILayout.Label(string.IsNullOrEmpty(value) ? "-" : value, wrapStyle);
            GUILayout.EndHorizontal();
        }

        private static string LocalTime(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "-";
            try
            {
                return DateTime.Parse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind)
                    .ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch { return iso; }
        }
        private void LoadEntry(Entry e)
        {
            if (!File.Exists(e.path)) { SetStatus("File not found"); Refresh(); return; }

            if (e.ghost) HitboxTrailBehaviour.LoadExternalGhost(e.path);
            else
            {
                try
                {
                    var data = JsonConvert.DeserializeObject<SavedTrail>(File.ReadAllText(e.path));
                    HitboxTrailBehaviour.LoadExternalTrail(data);
                }
                catch (Exception ex)
                {
                    SetStatus("Failed to load: " + ex.Message);
                    return;
                }
            }

            Toggle();   // закрыть менеджер
        }

        private void DoRename(string path, string newName)
        {
            if (!File.Exists(path)) { SetStatus("File not found"); Refresh(); return; }

            string newPath = ReplayFileTools.Rename(path, newName, out string error);
            if (newPath == null)
            {
                SetStatus("Rename failed: " + error);
                return;
            }

            selectedPath = newPath;
            Refresh();
            SetStatus(newPath == path ? "Name unchanged" : "Renamed to " + Path.GetFileName(newPath), 4f);
        }
        private void ConvertJson(Entry e)
        {
            try
            {
                SpriteResolve.InvalidateCache();
                var trail = JsonConvert.DeserializeObject<SavedTrail>(File.ReadAllText(e.path));

                var heroes = new List<TrailHeroFrame>(trail.frames.Count);
                foreach (SavedFrame f in trail.frames)
                {
                    heroes.Add(new TrailHeroFrame
                    {
                        min = new Vector2(Mathf.Min(f.ax, f.cx), Mathf.Min(f.ay, f.cy)),
                        max = new Vector2(Mathf.Max(f.ax, f.cx), Mathf.Max(f.ay, f.cy)),
                        color = new Color(f.r, f.g, f.b),
                        time = f.time,
                        sceneTime = f.sceneTime,
                        scene = f.scene,
                        characterFrame = f.ToCharacterFrame(),
                        inputs = (ushort)f.inputs
                    });
                }

                var actions = new List<ActionEntityFrame>();
                if (trail.actionFrames != null)
                    foreach (SavedActionFrame a in trail.actionFrames) actions.Add(a.ToActionEntityFrame());

                TrailMetadata meta = TrailMetadata.CreateForCurrentSession();
                meta.createdUtc = File.GetLastWriteTimeUtc(e.path).ToString("o");
                meta.convertedFrom = Path.GetFileName(e.path);
                meta.recordRate = null;   // неизвестна для старых записей
                meta.charms.Clear();
                meta.FillStats(heroes, actions);

                string target = Path.ChangeExtension(e.path, GhostTrailFormat.Extension);
                if (File.Exists(target))
                    target = Path.Combine(Dir, e.Name + "_converted" + GhostTrailFormat.Extension);

                GhostTrailFormat.Write(target, heroes, actions, meta);

                long newSize = new FileInfo(target).Length;
                selectedPath = target;
                Refresh();
                SetStatus($"Converted: {ReplayFileTools.FormatSize(e.size)} -> {ReplayFileTools.FormatSize(newSize)}", 6f);
            }
            catch (Exception ex)
            {
                SetStatus("Convert failed: " + ex.Message);
            }
        }
        private void DrawWorkshop(float width, float height)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(height));

            float leftW = Mathf.Clamp(width * 0.58f, 600f, 940f);
            GUILayout.BeginVertical(GUILayout.Width(leftW));
            GUILayout.Label("Source replay", sectionStyle);
            wsFilesScroll = GUILayout.BeginScrollView(wsFilesScroll, false, true, GUILayout.Height(110));
            GUILayout.BeginHorizontal();
            foreach (Entry e in entries)
            {
                string label = e.current ? "Current recording" : e.Name;
                bool active = wsSelected != null && (e.current ? wsSelected.current : wsSelected.path == e.path);
                GUI.enabled = !active;
                if (GUILayout.Button(label, buttonStyle, GUILayout.MaxWidth(340)))
                {
                    Entry picked = e;
                    Defer(() => SelectWorkshopSource(picked));
                }
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.Space(6);
            if (wsSelected == null) GUILayout.Label("Pick a replay above to see its rooms.", normalStyle);
            else
            {
                wsRoomsScroll = GUILayout.BeginScrollView(wsRoomsScroll, false, true,
                    GUILayout.Height(Mathf.Max(200f, height - 150f)));

                for (int i = 0; i < wsSelected.rooms.Count; i++)
                {
                    RoomView rv = wsSelected.rooms[i];
                    GUILayout.BeginHorizontal(cardStyle);

                    Rect tr = GUILayoutUtility.GetRect(ThumbW, ThumbH, GUILayout.Width(ThumbW), GUILayout.Height(ThumbH));
                    if (Event.current.type == EventType.Repaint)
                    {
                        EnsureThumb(rv, wsSelected.data);
                        if (rv.thumb != null) GUI.DrawTexture(tr, rv.thumb);
                    }

                    GUILayout.Space(14);
                    GUILayout.BeginVertical();
                    GUILayout.Label($"{i + 1}.  {rv.visit.scene}", rowTitleStyle);
                    GUILayout.Label($"{ReplayFileTools.FormatDuration(rv.visit.tEnd - rv.visit.tStart)}   " +
                                    $"{rv.visit.last - rv.visit.first} hero frames   {rv.actions.Count} action frames",
                                    smallStyle);

                    bool used = SceneInProject(rv.visit.scene);
                    GUILayout.Space(4);
                    GUI.enabled = !used;
                    if (GUILayout.Button(used ? "Scene already in project" : "Add to project",
                            buttonStyle, GUILayout.Width(240)))
                    {
                        RoomView picked = rv;
                        WorkshopSource src = wsSelected;
                        Defer(() => project.Add(new WorkshopItem { source = src, room = picked }));
                    }
                    GUI.enabled = true;

                    GUILayout.FlexibleSpace();
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                    GUILayout.Space(8);
                }

                GUILayout.EndScrollView();
            }

            GUILayout.EndVertical();
            GUILayout.Space(14);
            GUILayout.BeginVertical(cardStyle, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            float total = 0f;
            foreach (WorkshopItem it in project) total += it.room.visit.tEnd - it.room.visit.tStart;
            GUILayout.Label($"Project - {project.Count} rooms, {ReplayFileTools.FormatDuration(total)}", sectionStyle);

            wsProjectScroll = GUILayout.BeginScrollView(wsProjectScroll, false, true);
            if (project.Count == 0)
                GUILayout.Label("Add rooms from the left. Each scene can be used once.", wrapStyle);

            for (int i = 0; i < project.Count; i++)
            {
                WorkshopItem it = project[i];
                GUILayout.BeginVertical(rowStyle);
                GUILayout.Label($"{i + 1}. {it.room.visit.scene}", rowTitleStyle);
                GUILayout.Label($"{it.source.name}   " +
                                ReplayFileTools.FormatDuration(it.room.visit.tEnd - it.room.visit.tStart), smallStyle);

                GUILayout.BeginHorizontal();
                int index = i;
                GUI.enabled = i > 0;
                if (GUILayout.Button("Up", buttonStyle, GUILayout.Width(70)))
                    Defer(() => Swap(index, index - 1));
                GUI.enabled = i < project.Count - 1;
                if (GUILayout.Button("Down", buttonStyle, GUILayout.Width(80)))
                    Defer(() => Swap(index, index + 1));
                GUI.enabled = true;
                if (GUILayout.Button("Remove", buttonStyle, GUILayout.Width(110)))
                    Defer(() => { if (index < project.Count) project.RemoveAt(index); });
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                GUILayout.Space(6);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void Swap(int a, int b)
        {
            if (a < 0 || b < 0 || a >= project.Count || b >= project.Count) return;
            WorkshopItem t = project[a];
            project[a] = project[b];
            project[b] = t;
        }

        private bool SceneInProject(string scene)
        {
            foreach (WorkshopItem it in project) if (it.room.visit.scene == scene) return true;
            return false;
        }

        private void SelectWorkshopSource(Entry e)
        {
            string key = e.current ? null : e.path;
            foreach (WorkshopSource src in wsSources)
                if (src.current == e.current && src.path == key) { wsSelected = src; wsRoomsScroll = Vector2.zero; return; }

            GhostTrailData data;
            try
            {
                if (e.current)
                {
                    HitboxTrailBehaviour bh = Behaviour;
                    if (bh == null || !bh.HasTrail) { SetStatus("No current trail"); return; }
                    data = bh.ExportCurrentTrail();
                }
                else data = GhostTrailFormat.Read(e.path, false);
            }
            catch (Exception ex)
            {
                SetStatus("Failed to open: " + ex.Message);
                return;
            }

            var source = new WorkshopSource { current = e.current, path = key, name = e.Name, data = data };
            List<TrailVisit> visits = TrailVisit.Split(data.heroFrames);
            int[] owner = TrailVisit.AssignActions(visits, data.actionFrames);
            foreach (TrailVisit v in visits) source.rooms.Add(new RoomView { visit = v });
            for (int a = 0; a < owner.Length; a++)
                if (owner[a] >= 0) source.rooms[owner[a]].actions.Add(a);

            wsSources.Add(source);
            wsSelected = source;
            wsRoomsScroll = Vector2.zero;
        }

        private void CloseWorkshop()
        {
            foreach (WorkshopSource src in wsSources)
                foreach (RoomView r in src.rooms)
                    if (r.thumb != null) Destroy(r.thumb);
            wsSources.Clear();
            wsSelected = null;
            project.Clear();
        }
        private void BuildWorkshopFile(string name)
        {
            if (project.Count == 0) return;

            const float Gap = 0.5f;
            var heroes = new List<TrailHeroFrame>();
            var acts = new List<ActionEntityFrame>();
            var names = new List<string>();

            float offset = 0f;
            int entityBase = 0;

            foreach (WorkshopItem it in project)
            {
                TrailVisit v = it.room.visit;
                GhostTrailData data = it.source.data;
                float dur = v.tEnd - v.tStart;
                int maxEntity = 0;

                for (int i = v.first; i < v.last; i++)
                {
                    TrailHeroFrame f = data.heroFrames[i];
                    f.time = offset + (f.time - v.tStart);
                    heroes.Add(f);
                }

                foreach (int a in it.room.actions)
                {
                    ActionEntityFrame af = data.actionFrames[a];
                    af.timeFromStart = offset + (af.timeFromStart - v.tStart);
                    if (af.entityId > maxEntity) maxEntity = af.entityId;
                    af.entityId += entityBase;          // номера сущностей из разных файлов не должны совпадать
                    acts.Add(af);
                }

                names.Add($"{it.source.name} / {v.scene}");
                offset += dur + Gap;
                entityBase += maxEntity + 1;
            }

            acts.Sort((x, y) => x.timeFromStart.CompareTo(y.timeFromStart));

            try
            {
                TrailMetadata meta = TrailMetadata.CreateForCurrentSession();
                meta.charms.Clear();
                meta.charmNames.Clear();
                meta.abilities.Clear();
                meta.saveSlot = -1;
                meta.gameMode = null;
                meta.completion = -1f;
                meta.playTimeHours = -1f;
                meta.masks = meta.soulVessels = meta.charmNotches = meta.notchesUsed = 0;
                meta.fireballLevel = meta.quakeLevel = meta.screamLevel = 0;
                meta.nailLevel = -1;
                meta.recordRate = null;
                meta.avgGameFps = 0f;
                meta.note = "Built in workshop from: " + string.Join(", ", names.ToArray());
                meta.FillStats(heroes, acts);

                string path = PathForName(name, "workshop");
                GhostTrailFormat.Write(path, heroes, acts, meta);

                selectedPath = path;
                CloseWorkshop();
                Refresh();
                mode = Mode.List;
                SetStatus("Built " + Path.GetFileName(path) + " from " + names.Count + " rooms", 7f);
            }
            catch (Exception ex)
            {
                SetStatus("Build failed: " + ex.Message, 8f);
            }
        }
        private void DrawHelp(float height)
        {
            helpScroll = GUILayout.BeginScrollView(helpScroll, false, true, GUILayout.Height(height));

            string text = helpRussian ? ReadmeText.Russian : ReadmeText.English;
            if (string.IsNullOrEmpty(text))
            {
                GUILayout.Label("ReadMe.md not found next to the mod.", normalStyle);
                GUILayout.EndScrollView();
                return;
            }

            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd();
                string t = line.Trim();

                if (t.Length == 0) { GUILayout.Space(8); continue; }
                if (t.StartsWith("<!--")) continue;
                if (t.StartsWith("---") && t.Replace("-", "").Length == 0) { GUILayout.Space(10); continue; }

                if (t.StartsWith("#"))
                {
                    int level = 0;
                    while (level < t.Length && t[level] == '#') level++;
                    GUILayout.Space(level <= 2 ? 14 : 10);
                    GUILayout.Label(Clean(t.Substring(level).Trim()), level <= 2 ? titleStyle : sectionStyle);
                    continue;
                }
                if (t.StartsWith("|"))
                {
                    string inner = t.Trim('|');
                    if (inner.Replace("-", "").Replace("|", "").Replace(" ", "").Replace(":", "").Length == 0) continue;

                    string[] cells = inner.Split('|');
                    GUILayout.BeginHorizontal();
                    for (int i = 0; i < cells.Length; i++)
                        GUILayout.Label(Clean(cells[i].Trim()), i == 0 ? fieldNameStyle : wrapStyle,
                            i == 0 ? GUILayout.Width(150) : GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();
                    continue;
                }

                if (t.StartsWith("- ") || t.StartsWith("* "))
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(14);
                    GUILayout.Label("-", fieldNameStyle, GUILayout.Width(16));
                    GUILayout.Label(Clean(t.Substring(2)), wrapStyle);
                    GUILayout.EndHorizontal();
                    continue;
                }
                bool indented = line.StartsWith("  ");
                GUILayout.BeginHorizontal();
                if (indented) GUILayout.Space(30);
                GUILayout.Label(Clean(t), wrapStyle);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
        }
        private static string Clean(string s)
        {
            return s.Replace("**", "").Replace("`", "");
        }
        private void OpenRooms(Entry e)
        {
            GhostTrailData data;
            try
            {
                if (e.current)
                {
                    HitboxTrailBehaviour bh = Behaviour;
                    if (bh == null || !bh.HasTrail) { SetStatus("No current trail"); return; }
                    data = bh.ExportCurrentTrail();
                }
                else
                {
                    data = GhostTrailFormat.Read(e.path, false);   // сырой режим: без потерь при пересохранении
                }
            }
            catch (Exception ex)
            {
                SetStatus("Failed to open: " + ex.Message);
                return;
            }

            CloseSession();
            session = new RoomSession { current = e.current, path = e.path, data = data, meta = e.current ? CurrentMeta() : e.meta };

            List<TrailVisit> visits = TrailVisit.Split(data.heroFrames);
            session.actionOwner = TrailVisit.AssignActions(visits, data.actionFrames);
            foreach (TrailVisit v in visits) session.rooms.Add(new RoomView { visit = v });
            for (int a = 0; a < session.actionOwner.Length; a++)
                if (session.actionOwner[a] >= 0) session.rooms[session.actionOwner[a]].actions.Add(a);

            selectedPath = e.current ? null : e.path;
            if (e.current) roomsSaveName = string.IsNullOrEmpty(currentSaveName) ? DefaultName("trail") : currentSaveName;
            roomScroll = Vector2.zero;
            mode = Mode.Rooms;
        }

        private void CloseSession()
        {
            if (session == null) return;
            foreach (RoomView r in session.rooms)
                if (r.thumb != null) Destroy(r.thumb);
            session = null;
        }

        private void DrawRooms(float height)
        {
            if (session == null) { mode = Mode.List; return; }

            roomScroll = GUILayout.BeginScrollView(roomScroll, false, true, GUILayout.Height(height));

            if (session.rooms.Count == 0) GUILayout.Label("This replay has no hero frames.", normalStyle);

            for (int i = 0; i < session.rooms.Count; i++)
            {
                DrawRoomCard(session.rooms[i], i);
                GUILayout.Space(8);
            }

            GUILayout.EndScrollView();
        }

        private void DrawRoomCard(RoomView rv, int index)
        {
            TrailVisit v = rv.visit;
            float dur = Mathf.Max(0f, v.tEnd - v.tStart);

            GUILayout.BeginHorizontal(cardStyle);

            Rect thumbRect = GUILayoutUtility.GetRect(ThumbW, ThumbH, GUILayout.Width(ThumbW), GUILayout.Height(ThumbH));
            if (Event.current.type == EventType.Repaint)
            {
                EnsureThumb(rv, session.data);
                if (rv.thumb != null) GUI.DrawTexture(thumbRect, rv.thumb);
            }

            GUILayout.Space(16);
            GUILayout.BeginVertical();

            GUILayout.Label($"{index + 1}.  {v.scene}", rowTitleStyle);
            GUILayout.Label($"{ReplayFileTools.FormatDuration(dur)}   {v.last - v.first} hero frames   " +
                            $"{rv.actions.Count} action frames", smallStyle);

            GUILayout.Space(4);
            GUILayout.Label($"Keep:  {ReplayFileTools.FormatDuration(rv.workLo * dur)}  -  " +
                            $"{ReplayFileTools.FormatDuration(rv.workHi * dur)}   " +
                            $"({Mathf.RoundToInt((rv.workHi - rv.workLo) * 100f)}%)", normalStyle);

            RangeSlider(ref rv.workLo, ref rv.workHi);

            GUILayout.BeginHorizontal();
            GUI.enabled = rv.HasPending;
            if (GUILayout.Button("Trim", buttonStyle, GUILayout.Width(110)))
            {
                rv.appLo = rv.workLo;
                rv.appHi = rv.workHi;
            }
            GUI.enabled = rv.HasPending || !rv.IsFull;
            if (GUILayout.Button("Cancel", buttonStyle, GUILayout.Width(110)))
            {
                rv.workLo = rv.appLo = 0f;
                rv.workHi = rv.appHi = 1f;
            }
            GUI.enabled = true;

            GUILayout.Space(16);
            string state = rv.IsRemoved ? "Room will be removed"
                         : rv.IsFull ? "Full room"
                         : $"Trimmed to {ReplayFileTools.FormatDuration(rv.appLo * dur)} - {ReplayFileTools.FormatDuration(rv.appHi * dur)}";
            if (rv.HasPending) state += "   (slider changed - press Trim)";
            GUILayout.Label(state, rv.IsFull && !rv.HasPending ? smallStyle : accentStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }
        private static int rangeHandle;

        private void RangeSlider(ref float lo, ref float hi)
        {
            Rect r = GUILayoutUtility.GetRect(200f, 34f, GUILayout.ExpandWidth(true), GUILayout.Height(34f));
            int id = GUIUtility.GetControlID(FocusType.Passive, r);
            Event e = Event.current;

            Rect track = new Rect(r.x + 10f, r.y + r.height * 0.5f - 4f, r.width - 20f, 8f);

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && r.Contains(e.mousePosition))
                    {
                        float v = ValueAt(track, e.mousePosition.x);
                        rangeHandle = NearestHandle(v, lo, hi);
                        SetHandle(ref lo, ref hi, rangeHandle, v);
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        SetHandle(ref lo, ref hi, rangeHandle, ValueAt(track, e.mousePosition.x));
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;

                case EventType.ScrollWheel:
                    if (r.Contains(e.mousePosition))
                    {
                        float v = ValueAt(track, e.mousePosition.x);
                        int h = NearestHandle(v, lo, hi);
                        float step = (e.shift ? 0.05f : 0.01f) * (e.delta.y > 0 ? -1f : 1f);
                        SetHandle(ref lo, ref hi, h, (h == 0 ? lo : hi) + step);
                        e.Use();   // колесо над ползунком не прокручивает список
                    }
                    break;

                case EventType.Repaint:
                    GUI.color = new Color(1f, 1f, 1f, 0.12f);
                    GUI.DrawTexture(track, white);
                    GUI.color = new Color(0.35f, 0.75f, 1f, 0.9f);
                    GUI.DrawTexture(new Rect(track.x + lo * track.width, track.y, (hi - lo) * track.width, track.height), white);

                    DrawHandle(track, lo, new Color(0.45f, 1f, 0.5f, 1f));
                    DrawHandle(track, hi, new Color(1f, 0.45f, 0.4f, 1f));
                    GUI.color = Color.white;
                    break;
            }
        }

        private static float ValueAt(Rect track, float x) { return Mathf.Clamp01((x - track.x) / track.width); }

        private static int NearestHandle(float v, float lo, float hi)
        {
            if (Mathf.Abs(hi - lo) < 0.0001f) return v < lo ? 0 : 1;
            return Mathf.Abs(v - lo) <= Mathf.Abs(v - hi) ? 0 : 1;
        }

        private static void SetHandle(ref float lo, ref float hi, int handle, float v)
        {
            v = Mathf.Clamp01(v);
            if (handle == 0) lo = Mathf.Min(v, hi);
            else hi = Mathf.Max(v, lo);
        }

        private void DrawHandle(Rect track, float v, Color c)
        {
            float x = track.x + v * track.width;
            GUI.color = c;
            GUI.DrawTexture(new Rect(x - 5f, track.y - 9f, 10f, track.height + 18f), white);
        }
        private const int ThumbW = 320, ThumbH = 170;
        private static Color32[] thumbPixels;

        private void EnsureThumb(RoomView rv, GhostTrailData data)
        {
            if (rv.thumb != null && rv.thumbLo == rv.workLo && rv.thumbHi == rv.workHi && rv.thumbActions == showActions)
                return;

            if (rv.thumb == null)
                rv.thumb = new Texture2D(ThumbW, ThumbH, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };

            RenderThumb(rv, data);
            rv.thumbLo = rv.workLo;
            rv.thumbHi = rv.workHi;
            rv.thumbActions = showActions;
        }
        private void RenderThumb(RoomView rv, GhostTrailData data)
        {
            if (thumbPixels == null) thumbPixels = new Color32[ThumbW * ThumbH];
            Color32 bg = new Color32(20, 22, 28, 255);
            for (int i = 0; i < thumbPixels.Length; i++) thumbPixels[i] = bg;

            List<TrailHeroFrame> heroes = data.heroFrames;
            List<ActionEntityFrame> acts = data.actionFrames;
            TrailVisit v = rv.visit;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = v.first; i < v.last; i++)
                Grow(ref minX, ref minY, ref maxX, ref maxY, (heroes[i].min + heroes[i].max) * 0.5f);
            if (showActions)
                foreach (int a in rv.actions)
                    if (TryActionPos(acts[a], out Vector2 p)) Grow(ref minX, ref minY, ref maxX, ref maxY, p);

            if (minX > maxX) { rv.thumb.SetPixels32(thumbPixels); rv.thumb.Apply(); return; }
            if (maxX - minX < 1f) { minX -= 0.5f; maxX += 0.5f; }
            if (maxY - minY < 1f) { minY -= 0.5f; maxY += 0.5f; }

            const float pad = 10f;
            float sc = Mathf.Min((ThumbW - 2 * pad) / (maxX - minX), (ThumbH - 2 * pad) / (maxY - minY));
            float ox = (ThumbW - (maxX - minX) * sc) * 0.5f;
            float oy = (ThumbH - (maxY - minY) * sc) * 0.5f;
            Func<Vector2, Vector2Int> map = w => new Vector2Int(
                Mathf.RoundToInt(ox + (w.x - minX) * sc), Mathf.RoundToInt(oy + (w.y - minY) * sc));

            float dur = Mathf.Max(0.0001f, v.tEnd - v.tStart);
            float tLo = v.tStart + rv.workLo * dur, tHi = v.tStart + rv.workHi * dur;
            bool none = rv.workHi - rv.workLo <= 0.0005f;
            Func<float, bool> inRange = t => !none && t >= tLo - 0.0001f && t <= tHi + 0.0001f;
            if (showActions)
            {
                foreach (int a in rv.actions)
                {
                    if (!TryActionPos(acts[a], out Vector2 p)) continue;
                    Color32 c = inRange(acts[a].timeFromStart) ? new Color32(255, 170, 60, 255) : new Color32(90, 70, 50, 255);
                    Dot(map(p), 1, c);
                }
            }
            int count = v.last - v.first;
            int step = Mathf.Max(1, count / 1500);
            Vector2Int prev = map((heroes[v.first].min + heroes[v.first].max) * 0.5f);
            int firstKept = -1, lastKept = -1;
            for (int i = v.first; i < v.last; i += step)
            {
                Vector2Int cur = map((heroes[i].min + heroes[i].max) * 0.5f);
                bool keep = inRange(heroes[i].time);
                if (keep) { if (firstKept < 0) firstKept = i; lastKept = i; }

                Color32 c = keep ? Bright(heroes[i].color) : new Color32(70, 72, 80, 255);
                Line(prev, cur, c);
                Line(new Vector2Int(prev.x, prev.y + 1), new Vector2Int(cur.x, cur.y + 1), c);
                prev = cur;
            }

            if (firstKept >= 0) Dot(map((heroes[firstKept].min + heroes[firstKept].max) * 0.5f), 3, new Color32(110, 255, 120, 255));
            if (lastKept >= 0) Dot(map((heroes[lastKept].min + heroes[lastKept].max) * 0.5f), 3, new Color32(255, 110, 100, 255));

            rv.thumb.SetPixels32(thumbPixels);
            rv.thumb.Apply();
        }

        private static bool TryActionPos(ActionEntityFrame a, out Vector2 p)
        {
            p = default;
            if (a.sprites == null || a.sprites.Count == 0) return false;
            CharacterFrameSnapshot s = a.sprites[0];
            if (s.particleBlob != null && s.particleBlob.Length >= 8)
            {
                p = new Vector2(BitConverter.ToSingle(s.particleBlob, 0), BitConverter.ToSingle(s.particleBlob, 4));
                return true;
            }
            p = new Vector2(s.localToWorld.m03, s.localToWorld.m13);
            return true;
        }

        private static void Grow(ref float minX, ref float minY, ref float maxX, ref float maxY, Vector2 p)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }

        private static Color32 Bright(Color c)
        {
            return new Color32((byte)(Mathf.Lerp(c.r, 1f, 0.25f) * 255), (byte)(Mathf.Lerp(c.g, 1f, 0.25f) * 255),
                               (byte)(Mathf.Lerp(c.b, 1f, 0.25f) * 255), 255);
        }

        private static void Plot(int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= ThumbW || y >= ThumbH) return;
            thumbPixels[y * ThumbW + x] = c;
        }

        private static void Dot(Vector2Int p, int r, Color32 c)
        {
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++) Plot(p.x + dx, p.y + dy, c);
        }

        private static void Line(Vector2Int a, Vector2Int b, Color32 c)
        {
            int x0 = a.x, y0 = a.y, x1 = b.x, y1 = b.y;
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy, guard = 0;
            while (guard++ < 2000)
            {
                Plot(x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }
        private void SaveSession()
        {
            if (session == null || session.current || !session.Dirty) return;

            string path = session.path;
            try
            {
                Directory.CreateDirectory(BackupDir);
                string backup = Path.Combine(BackupDir,
                    Path.GetFileNameWithoutExtension(path) + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") +
                    Path.GetExtension(path));
                File.Copy(path, backup, true);
                List<TrailHeroFrame> heroes = session.data.heroFrames;
                List<ActionEntityFrame> acts = session.data.actionFrames;
                var newHeroes = new List<TrailHeroFrame>(heroes.Count);
                var newActs = new List<ActionEntityFrame>(acts.Count);

                foreach (RoomView rv in session.rooms)
                    for (int i = rv.visit.first; i < rv.visit.last; i++)
                        if (Keep(rv, heroes[i].time)) newHeroes.Add(heroes[i]);

                for (int a = 0; a < acts.Count; a++)
                {
                    int owner = session.actionOwner[a];
                    if (owner < 0 || Keep(session.rooms[owner], acts[a].timeFromStart)) newActs.Add(acts[a]);
                }
                TrailMetadata meta = session.meta != null ? session.meta.CloneEnvironment() : TrailMetadata.CreateForCurrentSession();
                meta.editedUtc = DateTime.UtcNow.ToString("o");
                meta.FillStats(newHeroes, newActs);
                string tmp = path + ".tmp";
                GhostTrailFormat.Write(tmp, newHeroes, newActs, meta);
                File.Delete(path);
                File.Move(tmp, path);

                Refresh();
                Entry e = null;
                foreach (Entry x in entries) if (x.path == path) e = x;
                if (e != null) OpenRooms(e);

                SetStatus("Saved. Original backed up to Backups/" + Path.GetFileName(backup), 7f);
            }
            catch (Exception ex)
            {
                SetStatus("Save failed: " + ex.Message, 8f);
            }
        }
        private void SaveCurrentSession(string name)
        {
            if (session == null) return;
            BuildTrimmed(out List<TrailHeroFrame> heroes, out List<ActionEntityFrame> acts);

            TrailMetadata meta = TrailMetadata.CreateForCurrentSession();
            meta.FillStats(heroes, acts);

            string path = PathForName(name, "trail");
            GhostTrailFormat.Write(path, heroes, acts, meta);
            HitboxTrailBehaviour bh = Behaviour;
            if (bh != null && !session.Dirty) bh.MarkTrailSaved();

            selectedPath = path;
            Refresh();
            CloseSession();
            mode = Mode.List;
            SetStatus("Saved as " + Path.GetFileName(path), 6f);
        }
        private void BuildTrimmed(out List<TrailHeroFrame> heroes, out List<ActionEntityFrame> acts)
        {
            List<TrailHeroFrame> src = session.data.heroFrames;
            List<ActionEntityFrame> srcActs = session.data.actionFrames;
            heroes = new List<TrailHeroFrame>(src.Count);
            acts = new List<ActionEntityFrame>(srcActs.Count);

            foreach (RoomView rv in session.rooms)
                for (int i = rv.visit.first; i < rv.visit.last; i++)
                    if (Keep(rv, src[i].time)) heroes.Add(src[i]);

            for (int a = 0; a < srcActs.Count; a++)
            {
                int owner = session.actionOwner[a];
                if (owner < 0 || Keep(session.rooms[owner], srcActs[a].timeFromStart)) acts.Add(srcActs[a]);
            }
        }

        private static string DefaultName(string prefix)
        {
            return prefix + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        }
        private static string PathForName(string name, string fallbackPrefix)
        {
            string clean = ReplayFileTools.SanitizeName(name);
            if (string.IsNullOrEmpty(clean)) clean = DefaultName(fallbackPrefix);

            string path = Path.Combine(Dir, clean + GhostTrailFormat.Extension);
            int n = 2;
            while (File.Exists(path))
                path = Path.Combine(Dir, clean + "_" + n++ + GhostTrailFormat.Extension);
            return path;
        }

        private void SaveCurrentTrailAs(string name)
        {
            HitboxTrailBehaviour bh = Behaviour;
            if (bh == null || !bh.HasTrail) { SetStatus("No current trail"); return; }

            try
            {
                GhostTrailData data = bh.ExportCurrentTrail();
                TrailMetadata meta = TrailMetadata.CreateForCurrentSession();
                meta.FillStats(data.heroFrames, data.actionFrames);

                string path = PathForName(name, "trail");
                GhostTrailFormat.Write(path, data.heroFrames, data.actionFrames, meta);
                bh.MarkTrailSaved();

                selectedPath = path;
                Refresh();
                SetStatus("Saved as " + Path.GetFileName(path), 6f);
            }
            catch (Exception ex)
            {
                SetStatus("Save failed: " + ex.Message, 8f);
            }
        }
        private static bool Keep(RoomView rv, float t)
        {
            if (rv.IsRemoved) return false;
            float dur = rv.visit.tEnd - rv.visit.tStart;
            float lo = rv.appLo <= 0f ? float.MinValue : rv.visit.tStart + rv.appLo * dur;
            float hi = rv.appHi >= 1f ? float.MaxValue : rv.visit.tStart + rv.appHi * dur;
            return t >= lo - 0.0001f && t <= hi + 0.0001f;
        }
        private Texture2D white;
        private GUIStyle panelStyle, rowStyle, rowSelStyle, cardStyle;
        private GUIStyle titleStyle, rowTitleStyle, normalStyle, smallStyle, wrapStyle, sectionStyle,
                         fieldNameStyle, accentStyle, statusStyle, buttonStyle, textFieldStyle, textAreaStyle, wrapSmallStyle;

        private void EnsureStyles()
        {
            if (panelStyle != null) return;

            white = Tex(Color.white);

            panelStyle = BoxStyle(new Color(0.08f, 0.09f, 0.11f, 0.97f), 0);
            rowStyle = BoxStyle(new Color(0.14f, 0.15f, 0.18f, 1f), 10);
            rowSelStyle = BoxStyle(new Color(0.17f, 0.26f, 0.40f, 1f), 10);
            cardStyle = BoxStyle(new Color(0.12f, 0.13f, 0.16f, 1f), 12);

            titleStyle = Label(28, FontStyle.Bold, Color.white);
            rowTitleStyle = Label(21, FontStyle.Bold, Color.white);
            normalStyle = Label(18, FontStyle.Normal, new Color(0.9f, 0.9f, 0.92f));
            smallStyle = Label(16, FontStyle.Normal, new Color(0.68f, 0.7f, 0.75f));
            wrapStyle = Label(17, FontStyle.Normal, new Color(0.9f, 0.9f, 0.92f));
            wrapStyle.wordWrap = true;
            wrapSmallStyle = Label(15, FontStyle.Italic, new Color(1f, 0.8f, 0.45f));
            wrapSmallStyle.wordWrap = true;
            sectionStyle = Label(19, FontStyle.Bold, new Color(0.6f, 0.8f, 1f));
            fieldNameStyle = Label(17, FontStyle.Normal, new Color(0.6f, 0.62f, 0.68f));
            accentStyle = Label(16, FontStyle.Bold, new Color(1f, 0.8f, 0.35f));
            statusStyle = Label(17, FontStyle.Italic, new Color(0.6f, 1f, 0.7f));

            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, fixedHeight = 34 };
            textFieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = 20, fixedHeight = 34 };
            textFieldStyle.alignment = TextAnchor.MiddleLeft;
            textAreaStyle = new GUIStyle(GUI.skin.textArea) { fontSize = 18, wordWrap = true };
        }

        private GUIStyle BoxStyle(Color c, int padding)
        {
            var s = new GUIStyle(GUI.skin.box);
            s.normal.background = Tex(c);
            s.padding = new RectOffset(padding, padding, padding, padding);
            s.margin = new RectOffset(0, 0, 0, 0);
            return s;
        }

        private static GUIStyle Label(int size, FontStyle style, Color color)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = style, wordWrap = false };
            s.normal.textColor = color;
            return s;
        }

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}