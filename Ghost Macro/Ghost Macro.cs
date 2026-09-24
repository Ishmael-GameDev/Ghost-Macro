using Modding;
using UnityEngine;
using System.Collections.Generic;
using Satchel.BetterMenus;
using InControl;
using Newtonsoft.Json;
using Modding.Converters;
using System;
using System.IO;
using UnityEngine.SceneManagement;

namespace GhostMacro
{
    public class TrailActions : PlayerActionSet
    {
        public PlayerAction NextColor;
        public PlayerAction ToggleRecord;
        public PlayerAction StopRecord;
        public PlayerAction Clear;
        public PlayerAction Playback;
        public PlayerAction SaveTrail;
        public PlayerAction OpenReplayMenu;
        public PlayerAction SeekBack;
        public PlayerAction SeekForward;

        public TrailActions()
        {
            ToggleRecord = CreatePlayerAction("Toggle Record");
            StopRecord = CreatePlayerAction("Stop Record");
            Clear = CreatePlayerAction("Clear Trail");
            NextColor = CreatePlayerAction("Next Color");
            Playback = CreatePlayerAction("Playback");
            SaveTrail = CreatePlayerAction("Save Trail");
            OpenReplayMenu = CreatePlayerAction("Open Replay Menu");
            SeekBack = CreatePlayerAction("Seek Back");
            SeekForward = CreatePlayerAction("Seek Forward");

            ToggleRecord.AddDefaultBinding(Key.J);
            StopRecord.AddDefaultBinding(Key.K);
            Clear.AddDefaultBinding(Key.L);
            NextColor.AddDefaultBinding(Key.N);
            Playback.AddDefaultBinding(Key.P);
            SaveTrail.AddDefaultBinding(Key.M);
            OpenReplayMenu.AddDefaultBinding(Key.O);
            SeekBack.AddDefaultBinding(Key.Comma);
            SeekForward.AddDefaultBinding(Key.Period);
        }
    }
    public enum TrailRenderMode
    {
        Hitboxes = 0,
        CharacterFrames = 1
    }
    public enum PlaybackDisplayMode
    {
        Trail = 0,
        CurrentOnly = 1
    }

    public class GlobalSettings
    {
        [JsonConverter(typeof(PlayerActionSetConverter))]
        public TrailActions Binds = new TrailActions();

        public bool ToggleRecordWithSameKey = true;
        public bool AutoHide = false;
        public bool TrailVisible = true;
        public bool DelayedSceneActivation = false;

        public int ActiveColors = 3;
        public int StartColorIndex = 0;
        public int CurrentColorIndex = 0;
        public bool TrailSmoothing = true;
        public int TrailThicknessIndex = 1;
        public TrailRenderMode RenderMode = TrailRenderMode.Hitboxes;
        public PlaybackDisplayMode PlaybackDisplay = PlaybackDisplayMode.Trail;
        public bool ColorizeCharacterFrames = true;
        public bool CustomKnightSkins = true;
        public int ParticleEffectsMode = 1;
        public bool AutoSaveBackup = true;
        public int SaveFormat = 0;
        public int RecordRateIndex = 1;
        public int InputOverlayMode = 0;
        public int StatusCorner = 1;

        public bool CameraFollowGhost = false;
        public int CameraSmoothingIndex = 3;
    }

    public class GhostMacro : Mod, IGlobalSettings<GlobalSettings>, ICustomMenuMod
    {
        public static GlobalSettings Settings { get; private set; } = new GlobalSettings();
        public static readonly float[] ThicknessPresets = { 0.015f, 0.03f, 0.05f, 0.08f };

        public const string ModVersion = "2.0.0";
        public override string GetVersion() => ModVersion;

        public void OnLoadGlobal(GlobalSettings s) => Settings = s;
        public GlobalSettings OnSaveGlobal() => Settings;
        private GameObject runner;
        private Menu _menu;
        public override void Initialize()
        {
            runner = new GameObject("HitboxTrailRunner");
            UnityEngine.Object.DontDestroyOnLoad(runner);
            runner.AddComponent<HitboxTrailBehaviour>();
            ModHooks.BeforeSceneLoadHook += OnBeforeSceneLoad;
            SceneTracker.Init();
        }
        private string OnBeforeSceneLoad(string sceneName)
        {
            return sceneName;
        }
        public bool ToggleButtonInsideMenu => false;

        public MenuScreen GetMenuScreen(MenuScreen modListMenu, ModToggleDelegates? toggleDelegates)
        {
            _menu ??= ConfigScreen.Build();
            return _menu.GetMenuScreen(modListMenu);
        }
    }

    public static class SceneTracker
    {
        public static string CurrentScene { get; private set; }
        public static float SceneEnterTime { get; private set; }

        public static void Init()
        {
            CurrentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            SceneEnterTime = Time.time;

            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private static void OnSceneChanged(
        UnityEngine.SceneManagement.Scene from,
        UnityEngine.SceneManagement.Scene to)
        {
            CurrentScene = to.name;
            SceneEnterTime = Time.time;
            var bh = UnityEngine.Object.FindObjectOfType<HitboxTrailBehaviour>();

            if (bh != null)
                bh.RebuildSceneCache();
        }

        public static float GetSceneTime()
        {
            return Time.time - SceneEnterTime;
        }

    }

    internal class HitboxTrailBehaviour : MonoBehaviour
    {
        public static void LoadExternalGhost(string path)
        {
            var bh = UnityEngine.Object.FindObjectOfType<HitboxTrailBehaviour>();

            if (bh != null)
                bh.LoadGhostFile(path);
        }
        public void LoadGhostFile(string path)
        {
            SpriteResolve.InvalidateCache();

            GhostTrailData data;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                data = GhostTrailFormat.Read(path);
            }
            catch (Exception e)
            {
                Modding.Logger.LogError($"[GhostMacro] Не удалось загрузить {Path.GetFileName(path)}: {e.Message}");
                return;
            }

            frames.Clear();
            actionFrames.Clear();

            for (int i = 0; i < data.heroFrames.Count; i++)
            {
                TrailHeroFrame h = data.heroFrames[i];
                frames.Add(new BoxFrame
                {
                    a = new Vector3(h.min.x, h.min.y, 0),
                    b = new Vector3(h.max.x, h.min.y, 0),
                    c = new Vector3(h.max.x, h.max.y, 0),
                    d = new Vector3(h.min.x, h.max.y, 0),
                    color = h.color,
                    timeFromStart = h.time,
                    timeFromScene = h.sceneTime,
                    scene = h.scene,
                    characterFrame = h.characterFrame,
                    inputs = h.inputs
                });
            }

            int maxId = 0;
            for (int i = 0; i < data.actionFrames.Count; i++)
            {
                actionFrames.Add(data.actionFrames[i]);
                if (data.actionFrames[i].entityId > maxId) maxId = data.actionFrames[i].entityId;
            }
            ActionEntityCapture.EnsureIdAbove(maxId);

            visibleFrames = frames.Count;
            unsaved = false;
            ResetRecordingFps();

            Modding.Logger.Log($"[GhostMacro] Загружен {Path.GetFileName(path)}: героя {frames.Count}, " +
                               $"действий {actionFrames.Count} кадров за {sw.ElapsedMilliseconds} мс");

            RebuildSceneCache();
        }

        public static void LoadExternalTrail(SavedTrail trail)
        {
            var bh = UnityEngine.Object.FindObjectOfType<HitboxTrailBehaviour>();

            if (bh != null)
                bh.LoadTrail(trail);
        }

        public void LoadTrail(SavedTrail trail)
        {
            frames.Clear();
            actionFrames.Clear();
            SpriteResolve.InvalidateCache();

            foreach (var f in trail.frames)
            {
                frames.Add(new BoxFrame
                {
                    a = new Vector3(f.ax, f.ay, 0),
                    b = new Vector3(f.bx, f.by, 0),
                    c = new Vector3(f.cx, f.cy, 0),
                    d = new Vector3(f.dx, f.dy, 0),

                    color = new Color(f.r, f.g, f.b),

                    timeFromStart = f.time,
                    timeFromScene = f.sceneTime,

                    scene = f.scene,

                    characterFrame = f.ToCharacterFrame(),
                    inputs = (ushort)f.inputs
                });
            }

            if (trail.actionFrames != null)
            {
                int maxId = 0;
                foreach (var a in trail.actionFrames)
                {
                    actionFrames.Add(a.ToActionEntityFrame());
                    if (a.entityId > maxId) maxId = a.entityId;
                }
                ActionEntityCapture.EnsureIdAbove(maxId);
            }

            visibleFrames = frames.Count;
            unsaved = false; // [ФИКС 15] загруженный трейл уже лежит в файле
            RebuildSceneCache();
        }

        private struct BoxFrame
        {
            public Vector3 a, b, c, d;
            public Color color;

            public float timeFromStart;
            public float timeFromScene;

            public string scene;
            public CharacterFrameSnapshot characterFrame;
            public ushort inputs;
        }
        private string saveDirectory;

        private readonly List<BoxFrame> frames = new();
        private readonly List<BoxFrame> sceneFrames = new();
        private List<BoxFrame> renderFrames = new();
        private readonly List<ActionEntityFrame> actionFrames = new();
        private readonly List<ActionEntityFrame> sceneActionFrames = new();
        private readonly Dictionary<int, float> liveEntityLastSeen = new();
        private const int MaxFrames = 10000;

        private bool recording = false;
        private bool playing = false;
        private bool playingButtonBan = false;
        private bool delayedPlaying = false;
        private bool delayedPlayingActive = false;
        private bool wasRecording = false;

        private float recordStartTime;
        private float playbackStartTime;
        private float delayedPlaybackStartTime;

        private int playbackIndex = 0;
        private int visibleFrames = 0;

        private string currentScene;
        private float sceneStartTime;

        private static readonly Color[] Palette =
        {
            Color.yellow,
            Color.red,
            Color.blue,
            Color.green,
            Color.cyan,
            new Color(1f, 0.5f, 0f)
        };

        void Awake()
        {
            saveDirectory = ReplayFileTools.SaveDirectory;
            currentScene = SceneTracker.CurrentScene;
            sceneStartTime = Time.time;
            ActionEntityCapture.Reset();
            ActionEntityCapture.RebuildPrefabDictionary();
            CameraFollow.Hook();
            AutosaveWatcher.Init(OnAutosaveTrigger);
        }
        private bool unsaved;

        private bool HasAnyTrail => frames.Count > 0 || actionFrames.Count > 0;
        private bool HasUnsavedTrail => unsaved && HasAnyTrail;
        public bool HasTrail => HasAnyTrail;
        public bool TrailUnsaved => HasUnsavedTrail;
        public GhostTrailData ExportCurrentTrail()
        {
            var data = new GhostTrailData();
            for (int i = 0; i < frames.Count; i++) data.heroFrames.Add(ToHeroFrame(frames[i]));
            data.actionFrames.AddRange(actionFrames);
            return data;
        }
        public string SaveCurrentTrail() { return SaveTrail(); }
        public void MarkTrailSaved() { unsaved = false; }

        private void OnAutosaveTrigger(string reason)
        {
            if (!GhostMacro.Settings.AutoSaveBackup) return;
            if (!HasUnsavedTrail) return;

            string path = SaveTrail("recovered_backup");
            if (path != null)
                Modding.Logger.Log($"[GhostMacro][AutoSave] {reason}: несохранённый трейл сохранён в {path}");
            if (reason == "exit to menu")
            {
                recording = false;
                playing = false;
                delayedPlayingActive = false;
            }
        }
        private const float PlaybackHoldToCancel = 0.8f;
        private float playbackHoldTime;
        private bool playbackHoldConsumed;
        private bool playbackHoldArmedAtPress;

        private void UpdatePlaybackHoldCancel(TrailActions binds)
        {
            if (ReplayMenuUI.IsOpen) { TrailStatusHud.HoldProgress = 0f; playbackHoldTime = 0f; return; }

            bool delayedOn = playingButtonBan && (delayedPlaying || delayedPlayingActive);

            if (!binds.Playback.IsPressed || !playbackHoldArmedAtPress || !delayedOn || playbackHoldConsumed)
            {
                if (!binds.Playback.IsPressed) playbackHoldTime = 0f;
                TrailStatusHud.HoldProgress = 0f;
                return;
            }

            playbackHoldTime += Time.unscaledDeltaTime;
            TrailStatusHud.HoldProgress = Mathf.Clamp01(playbackHoldTime / PlaybackHoldToCancel);

            if (playbackHoldTime < PlaybackHoldToCancel) return;

            playbackHoldConsumed = true;
            TrailStatusHud.HoldProgress = 0f;

            delayedPlaying = false;
            delayedPlayingActive = false;
            delayedPaused = false;
            playbackIndex = 0;
            ShowFullSceneTrail();   // как после обычного окончания проигрывания

            Modding.Logger.Log("[GhostMacro] Delayed playback cancelled (Playback held)");
        }
        public static readonly int[] RecordRates = { 60, 120, 240, 0 };        // 0 = каждый кадр
        public static readonly string[] RecordRateLabels = { "60 FPS", "120 FPS", "240 FPS", "Full" };

        private bool samplingActive;
        private float nextSampleTime;

        private static int recFpsFrames;
        private static float recFpsTime;

        public static float RecordingAverageFps => recFpsTime > 0.5f ? recFpsFrames / recFpsTime : 0f;

        private static void ResetRecordingFps() { recFpsFrames = 0; recFpsTime = 0f; }
        private bool RecordSampleDue()
        {
            int idx = Mathf.Clamp(GhostMacro.Settings.RecordRateIndex, 0, RecordRates.Length - 1);
            int rate = RecordRates[idx];
            if (rate <= 0) return true;

            float interval = 1f / rate;
            if (Time.time + 0.0001f < nextSampleTime) return false;

            nextSampleTime += interval;
            if (nextSampleTime < Time.time) nextSampleTime = Time.time + interval;
            return true;
        }
        private const float SeekTapThreshold = 0.1f;
        private const float SeekSpeed = 1f;
        private float seekHoldTime;
        private int seekDir;
        private ushort playbackInputs;

        private bool normalPaused;
        private bool delayedPaused;
        private float pausedPlaybackTime;

        private bool IsPaused =>
            (normalPaused && !playing && !GhostMacro.Settings.DelayedSceneActivation) ||
            (delayedPlayingActive && delayedPaused);

        private bool IsPlaybackRunning => playing || (delayedPlayingActive && !delayedPaused);
        private float FrameTime(BoxFrame f) => delayedPlayingActive ? f.timeFromScene : f.timeFromStart;

        private float CurrentPlaybackTime =>
            IsPaused ? pausedPlaybackTime
            : delayedPlayingActive ? Time.time - delayedPlaybackStartTime
            : Time.time - playbackStartTime;

        private float PlaybackProgress01()
        {
            if (!(IsPlaybackRunning || IsPaused) || sceneFrames.Count == 0) return -1f;
            float first = FrameTime(sceneFrames[0]);
            float last = FrameTime(sceneFrames[sceneFrames.Count - 1]);
            if (last - first < 0.0001f) return 1f;
            return Mathf.Clamp01((CurrentPlaybackTime - first) / (last - first));
        }

        private void ToggleDelayedPause()
        {
            if (!delayedPaused)
            {
                delayedPaused = true;
                pausedPlaybackTime = Time.time - delayedPlaybackStartTime;
            }
            else
            {
                delayedPaused = false;
                delayedPlaybackStartTime = Time.time - pausedPlaybackTime;
            }
        }

        private void UpdateSeek(TrailActions binds)
        {
            if (ReplayMenuUI.IsOpen) { seekDir = 0; return; }

            if (!IsPaused || recording || sceneFrames.Count == 0) { seekDir = 0; return; }

            if (binds.SeekForward.WasPressed || binds.SeekBack.WasPressed)
            {
                seekDir = binds.SeekForward.WasPressed ? 1 : -1;
                seekHoldTime = 0f;
            }
            if (seekDir == 0) return;

            PlayerAction key = seekDir > 0 ? binds.SeekForward : binds.SeekBack;
            if (key.IsPressed)
            {
                float dt = Time.unscaledDeltaTime;
                float before = seekHoldTime;
                seekHoldTime += dt;
                if (seekHoldTime > SeekTapThreshold)
                {
                    float step = seekHoldTime - Mathf.Max(before, SeekTapThreshold);
                    SeekTo(pausedPlaybackTime + seekDir * step * SeekSpeed);
                }
                return;
            }
            if (seekHoldTime < SeekTapThreshold) StepFrame(seekDir);
            seekDir = 0;
        }

        private void StepFrame(int dir)
        {
            int count = sceneFrames.Count;
            float t;
            if (dir > 0)
            {
                if (playbackIndex >= count) return;                  // уже на последнем кадре
                t = FrameTime(sceneFrames[playbackIndex]);
            }
            else
            {
                t = playbackIndex >= 2 ? FrameTime(sceneFrames[playbackIndex - 2]) : FrameTime(sceneFrames[0]);
            }
            SeekTo(t);
        }
        private void SeekTo(float t)
        {
            float first = FrameTime(sceneFrames[0]);
            float last = FrameTime(sceneFrames[sceneFrames.Count - 1]);
            t = Mathf.Clamp(t, first, last);
            pausedPlaybackTime = t;

            bool trailMode = GhostMacro.Settings.PlaybackDisplay == PlaybackDisplayMode.Trail;

            renderFrames.Clear();
            playbackIndex = 0;
            while (playbackIndex < sceneFrames.Count && FrameTime(sceneFrames[playbackIndex]) <= t)
            {
                if (!trailMode) renderFrames.Clear();
                renderFrames.Add(sceneFrames[playbackIndex]);
                playbackIndex++;
            }

            if (playbackIndex > 0)
            {
                BoxFrame cur = sceneFrames[playbackIndex - 1];
                CameraFollow.SetTarget((cur.a + cur.c) * 0.5f);
                playbackInputs = cur.inputs;
            }

            ActionEntityPlayback.ResetPlayback();
            ActionEntityPlayback.Tick(t, trailMode);
        }

        private TrailStatus CurrentStatus()
        {
            if (recording) return TrailStatus.Recording;
            if (IsPaused) return TrailStatus.Paused;
            if (playing || delayedPlayingActive) return TrailStatus.Playing;
            if (delayedPlaying && GhostMacro.Settings.DelayedSceneActivation) return TrailStatus.PlaybackArmed;
            if (HasUnsavedTrail) return TrailStatus.Unsaved;
            return HasAnyTrail ? TrailStatus.Saved : TrailStatus.Empty;
        }

        void OnGUI()
        {
            if (HeroController.instance == null) return;
            if (SceneTracker.CurrentScene == "Menu_Title" || SceneTracker.CurrentScene == "Quit_To_Menu") return;

            TrailStatusHud.PlaybackProgress = PlaybackProgress01();
            TrailStatusHud.Draw(CurrentStatus());

            if ((IsPlaybackRunning || IsPaused) && playbackIndex > 0)
                InputOverlayHud.Draw(playbackInputs);
        }

        void OnDestroy()
        {
            AutosaveWatcher.Shutdown();
            CameraFollow.SetActive(false);
            CameraFollow.Unhook();
            ActionEntityCapture.Reset();
            ActionEntityPlayback.Reset();
            SpriteResolve.InvalidateCache();
        }

        private Color GetColor()
        {
            var s = GhostMacro.Settings;

            int m = Mathf.Clamp(s.ActiveColors, 1, Palette.Length);

            int idx = (s.StartColorIndex + s.CurrentColorIndex) % m;

            return Palette[idx];
        }
        public void RebuildSceneCache()
        {
            renderFrames.Clear();
            ActionEntityPlayback.ClearRenderBuffers();
            liveEntityLastSeen.Clear();
            SpriteResolve.InvalidateCache();
            try { ActionEntityCapture.RebuildPrefabDictionary(); }
            catch (Exception e) { Modding.Logger.LogError("[GhostMacro] RebuildPrefabDictionary: " + e.Message); }
            ActionEntityCapture.ClearTracking();

            sceneFrames.Clear();
            playbackIndex = 0;
            normalPaused = false;
            delayedPaused = false;

            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].scene == SceneTracker.CurrentScene)
                {
                    sceneFrames.Add(frames[i]);
                }
            }

            sceneActionFrames.Clear();
            for (int i = 0; i < actionFrames.Count; i++)
            {
                if (actionFrames[i].scene == SceneTracker.CurrentScene)
                {
                    sceneActionFrames.Add(actionFrames[i]);
                }
            }

            bool useSceneTime = GhostMacro.Settings.DelayedSceneActivation && delayedPlaying;
            if (useSceneTime)
            {
                delayedPlaybackStartTime = Time.time;
                delayedPlayingActive = true;
                playing = false;
                sceneFrames.Sort((a, b) => a.timeFromScene.CompareTo(b.timeFromScene));
            }
            else
            {
                playing = false;
                sceneFrames.Sort((a, b) => a.timeFromStart.CompareTo(b.timeFromStart));
            }

            ActionEntityPlayback.RebuildSceneGroups(sceneActionFrames, useSceneTime);

            if (!playing)
            {
                if (!delayedPlayingActive)
                {
                    renderFrames.AddRange(sceneFrames);
                    ActionEntityPlayback.RevealAll();
                }
            }
        }
        private void ShowFullSceneTrail()
        {
            renderFrames.Clear();
            renderFrames.AddRange(sceneFrames);
            ActionEntityPlayback.RevealAll();
        }

        void Update()
        {
            AutosaveWatcher.Poll();

            var binds = GhostMacro.Settings.Binds;
            bool hk = !ReplayMenuUI.IsOpen;
            var s = GhostMacro.Settings;

            if (s.ToggleRecordWithSameKey)
            {
                if ((hk && binds.ToggleRecord.WasPressed))
                {
                    if (!recording && frames.Count == 0 && actionFrames.Count == 0)
                        recordStartTime = Time.time;

                    recording = !recording;
                }
            }
            else
            {
                if ((hk && binds.ToggleRecord.WasPressed))
                {
                    if (!recording && frames.Count == 0 && actionFrames.Count == 0)
                        recordStartTime = Time.time;

                    recording = true;
                }

                if ((hk && binds.StopRecord.WasPressed))
                    recording = false;
            }
            if (wasRecording && !recording)
            {
                ActionEntityPlayback.RebuildSceneGroups(sceneActionFrames, false);
                ActionEntityPlayback.RevealAll();
            }
            wasRecording = recording;

            if ((hk && binds.Clear.WasPressed))
            {
                frames.RemoveAll(f => f.scene == SceneTracker.CurrentScene);
                actionFrames.RemoveAll(a => a.scene == SceneTracker.CurrentScene);
                ActionEntityCapture.ClearTracking();
                ActionEntityPlayback.Reset();

                if (!HasAnyTrail) { unsaved = false; ResetRecordingFps(); }

                RebuildSceneCache();

                playbackIndex = 0;
                playing = false;
            }

            if ((hk && binds.NextColor.WasPressed))
            {
                s.CurrentColorIndex++;

                int m = Mathf.Clamp(s.ActiveColors, 1, Palette.Length);

                if (s.CurrentColorIndex >= m)
                    s.CurrentColorIndex = 0;
            }

            if ((hk && binds.SaveTrail.WasPressed) && !recording)
            {
                SaveTrail();
            }

            if (binds.OpenReplayMenu.WasPressed && !ReplayMenuUI.IsTyping)
            {
                ReplayMenuUI.Toggle();
            }
            if (GhostMacro.Settings.DelayedSceneActivation)
            {
                playing = false;
                normalPaused = false;
                playingButtonBan = true;
            }
            else
            {
                delayedPlaying = false;
                playingButtonBan = false;
                delayedPlayingActive = false;
            }

            if ((hk && binds.Playback.WasPressed) && !recording && sceneFrames.Count > 0 && !playingButtonBan)
            {
                playing = !playing;

                if (playing && normalPaused)
                {
                    normalPaused = false;
                    playbackStartTime = Time.time - pausedPlaybackTime;
                }
                else if (!playing)
                {
                    normalPaused = true;
                    pausedPlaybackTime = Time.time - playbackStartTime;
                }
                else
                {
                    {
                        renderFrames.Clear();
                        playbackIndex = 0;
                        Modding.Logger.Log(
                            $"[GhostMacro][Action] Live playback start: rebuilding action groups from {sceneActionFrames.Count} scene action-frame(s).");
                        ActionEntityPlayback.RebuildSceneGroups(sceneActionFrames, false);
                    }
                    renderFrames.Clear();
                    playbackIndex = 0;
                    playbackStartTime = Time.time - sceneFrames[0].timeFromStart;
                }
            }
            if ((hk && binds.Playback.WasPressed))
            {
                playbackHoldArmedAtPress = playingButtonBan && (delayedPlaying || delayedPlayingActive);
                playbackHoldTime = 0f;
                playbackHoldConsumed = false;
            }

            if ((hk && binds.Playback.WasPressed) && !recording && playingButtonBan)
            {
                delayedPlaying = true;
            }
            if ((hk && binds.Playback.WasReleased) && delayedPlayingActive && playbackHoldArmedAtPress &&
                !playbackHoldConsumed && playbackHoldTime < PlaybackHoldToCancel)
            {
                ToggleDelayedPause();
            }

            UpdatePlaybackHoldCancel(binds);

            if (recording) normalPaused = false;   // запись поверх паузы - пауза сбрасывается
            UpdateSeek(binds);

            if (playing)
            {
                float playbackTime = Time.time - playbackStartTime;
                bool trailMode = GhostMacro.Settings.PlaybackDisplay == PlaybackDisplayMode.Trail;

                while (playbackIndex < sceneFrames.Count &&
                       sceneFrames[playbackIndex].timeFromStart <= playbackTime)
                {
                    if (!trailMode)
                        renderFrames.Clear();

                    renderFrames.Add(sceneFrames[playbackIndex]);
                    CameraFollow.SetTarget((sceneFrames[playbackIndex].a + sceneFrames[playbackIndex].c) * 0.5f);
                    playbackInputs = sceneFrames[playbackIndex].inputs;
                    playbackIndex++;
                }

                ActionEntityPlayback.Tick(playbackTime, trailMode);

                if (playbackIndex >= sceneFrames.Count)
                {
                    playing = false;
                    normalPaused = false;
                    Modding.Logger.Log("Playback ended");
                    playbackIndex = 0;
                    ShowFullSceneTrail(); // [ФИКС 8]
                }
            }
            if (delayedPlayingActive && !delayedPaused)
            {
                float playbackTime = Time.time - delayedPlaybackStartTime;
                bool trailMode = GhostMacro.Settings.PlaybackDisplay == PlaybackDisplayMode.Trail;

                while (playbackIndex < sceneFrames.Count &&
                       sceneFrames[playbackIndex].timeFromScene <= playbackTime)
                {
                    if (!trailMode)
                        renderFrames.Clear();

                    renderFrames.Add(sceneFrames[playbackIndex]);
                    CameraFollow.SetTarget((sceneFrames[playbackIndex].a + sceneFrames[playbackIndex].c) * 0.5f);
                    playbackInputs = sceneFrames[playbackIndex].inputs;
                    playbackIndex++;
                }

                ActionEntityPlayback.Tick(playbackTime, trailMode);

                if (playbackIndex >= sceneFrames.Count)
                {
                    delayedPlayingActive = false;
                    delayedPaused = false;
                    Modding.Logger.Log("Delayed Playback ended");
                    playbackIndex = 0;
                    ShowFullSceneTrail(); // [ФИКС 8]
                }
            }
            CameraFollow.SetActive(GhostMacro.Settings.CameraFollowGhost && (playing || delayedPlayingActive || IsPaused));

            if (!recording || HeroController.instance == null)
            {
                samplingActive = false;
                return;
            }
            if (!samplingActive)
            {
                samplingActive = true;
                nextSampleTime = 0f;
                InputRecorder.Reset();
            }

            InputRecorder.Accumulate();
            recFpsFrames++;
            recFpsTime += Time.unscaledDeltaTime;

            if (!RecordSampleDue())
                return;

            ushort sampleInputs = InputRecorder.Consume();
            var entityCaptures = ActionEntityCapture.Tick(HeroController.instance.transform);

            foreach (var (entityId, sprites) in entityCaptures)
            {
                var actionFrame = new ActionEntityFrame
                {
                    entityId = entityId,
                    sprites = sprites,

                    timeFromStart = Time.time - recordStartTime,
                    timeFromScene = SceneTracker.GetSceneTime(),
                    scene = SceneTracker.CurrentScene,

                    color = GetColor()
                };

                actionFrames.Add(actionFrame);
                unsaved = true;

                if (actionFrame.scene == SceneTracker.CurrentScene)
                {
                    sceneActionFrames.Add(actionFrame);
                    ActionEntityPlayback.RecordLive(actionFrame, true);
                }
            }

            BoxCollider2D box =
                HeroController.instance.GetComponent<BoxCollider2D>();

            if (box == null)
                return;

            Bounds b = box.bounds;

            BoxFrame frame = new BoxFrame
            {
                a = new Vector3(b.min.x, b.min.y, 0),
                b = new Vector3(b.max.x, b.min.y, 0),
                c = new Vector3(b.max.x, b.max.y, 0),
                d = new Vector3(b.min.x, b.max.y, 0),

                color = GetColor(),

                timeFromStart = Time.time - recordStartTime,
                timeFromScene = SceneTracker.GetSceneTime(),

                scene = SceneTracker.CurrentScene,

                inputs = sampleInputs
            };

            if (CharacterFrameCapture.TryCapture(HeroController.instance, out var charSnapshot))
            {
                frame.characterFrame = charSnapshot;

                CharacterFrameDrawing.RegisterAnimatorSource(
                    HeroController.instance.GetComponent<tk2dSpriteAnimator>());
            }

            frames.Add(frame);

            if (frame.scene == SceneTracker.CurrentScene)
            {
                sceneFrames.Add(frame);
                renderFrames.Add(frame);
            }

            unsaved = true;
            visibleFrames = frames.Count;

            TrimOldestFrames();
        }
        private const int TrimBatch = 500;

        private void TrimOldestFrames()
        {
            if (frames.Count <= MaxFrames + TrimBatch) return;

            int remove = frames.Count - MaxFrames;
            frames.RemoveRange(0, remove);

            float oldestKept = frames[0].timeFromStart;
            actionFrames.RemoveAll(a => a.timeFromStart < oldestKept);
        }

        void OnRenderObject()
        {
            if (!GameCameraUtil.IsMainGameCamera(Camera.current))
                return;

            if (!GhostMacro.Settings.TrailVisible)
                return;

            if (GhostMacro.Settings.AutoHide && recording)
                return;
            ActionEntityPlayback.Draw(SceneTracker.CurrentScene);

            if (renderFrames.Count == 0)
                return;

            if (GhostMacro.Settings.RenderMode == TrailRenderMode.CharacterFrames)
            {
                DrawCharacterFrames();
            }
            else
            {
                DrawHitboxFrames();
            }
        }
        private static float FadeAlpha(int i, int count)
        {
            return count > 1 ? (float)i / count : 1f;
        }

        private void DrawHitboxFrames()
        {
            var presets = GhostMacro.ThicknessPresets;
            float width = presets[Mathf.Clamp(GhostMacro.Settings.TrailThicknessIndex, 0, presets.Length - 1)];
            bool smooth = GhostMacro.Settings.TrailSmoothing;

            TrailDrawing.Begin();

            for (int i = 0; i < renderFrames.Count; i++)
            {
                var f = renderFrames[i];
                if (f.scene != SceneTracker.CurrentScene)
                    continue;

                float t = FadeAlpha(i, renderFrames.Count);
                Color color = new Color(f.color.r, f.color.g, f.color.b, t);
                DrawBox(f, color, width, smooth);
            }
            TrailDrawing.End();
        }
        private void DrawCharacterFrames()
        {
            bool colorize = GhostMacro.Settings.ColorizeCharacterFrames;
            CharacterFrameDrawing.Begin();

            for (int i = 0; i < renderFrames.Count; i++)
            {
                var f = renderFrames[i];
                if (f.scene != SceneTracker.CurrentScene)
                    continue;

                if (!f.characterFrame.IsDrawable)
                    continue;

                float t = FadeAlpha(i, renderFrames.Count);
                Color tint = colorize
                    ? new Color(f.color.r, f.color.g, f.color.b, t)
                    : new Color(1f, 1f, 1f, t);

                CharacterFrameDrawing.DrawFrame(f.characterFrame, tint);
            }
            CharacterFrameDrawing.End();
        }

        private void DrawBox(BoxFrame f, Color color, float width, bool smooth)
        {
            DrawEdge(f.a, f.b, color, width, smooth);
            DrawEdge(f.b, f.c, color, width, smooth);
            DrawEdge(f.c, f.d, color, width, smooth);
            DrawEdge(f.d, f.a, color, width, smooth);
        }

        private static void DrawEdge(Vector3 a, Vector3 b, Color color, float width, bool smooth)
        {
            if (smooth)
            {
                TrailDrawing.DrawSmoothLine(a, b, color, width);
            }
            else
            {
                TrailDrawing.DrawLine(a, b, color, width);
            }
        }
        private string SaveTrail(string suffix = null)
        {
            if (GhostMacro.Settings.SaveFormat != 0) return SaveTrailToJson(suffix);

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string name = string.IsNullOrEmpty(suffix) ? $"trail_{timestamp}" : $"trail_{timestamp}_{suffix}";
            string path = Path.Combine(saveDirectory, name + GhostTrailFormat.Extension);

            var heroes = new List<TrailHeroFrame>(frames.Count);
            for (int i = 0; i < frames.Count; i++) heroes.Add(ToHeroFrame(frames[i]));

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                TrailMetadata meta = TrailMetadata.CreateForCurrentSession();
                meta.FillStats(heroes, actionFrames);
                GhostTrailFormat.Write(path, heroes, actionFrames, meta);
            }
            catch (Exception e)
            {
                Modding.Logger.LogError($"[GhostMacro] Не удалось сохранить трейл: {e.Message}");
                return null;
            }

            unsaved = false;
            long size = new FileInfo(path).Length;
            Modding.Logger.Log($"Trail saved to {path} ({size / 1024f:F1} KB, {sw.ElapsedMilliseconds} ms)");
            return path;
        }

        private static TrailHeroFrame ToHeroFrame(BoxFrame f)
        {
            return new TrailHeroFrame
            {
                min = new Vector2(Mathf.Min(f.a.x, f.c.x), Mathf.Min(f.a.y, f.c.y)),
                max = new Vector2(Mathf.Max(f.a.x, f.c.x), Mathf.Max(f.a.y, f.c.y)),
                color = f.color,
                time = f.timeFromStart,
                sceneTime = f.timeFromScene,
                scene = f.scene,
                characterFrame = f.characterFrame,
                inputs = f.inputs
            };
        }

        private string SaveTrailToJson(string suffix = null)
        {
            SavedTrail trail = new SavedTrail();

            foreach (var f in frames)
            {
                trail.frames.Add(new SavedFrame
                {
                    scene = f.scene,

                    time = f.timeFromStart,
                    sceneTime = f.timeFromScene,

                    ax = f.a.x,
                    ay = f.a.y,

                    bx = f.b.x,
                    by = f.b.y,

                    cx = f.c.x,
                    cy = f.c.y,

                    dx = f.d.x,
                    dy = f.d.y,

                    r = f.color.r,
                    g = f.color.g,
                    b = f.color.b,
                    characterFrame = f.characterFrame.IsDrawable ? SavedSprite.From(f.characterFrame) : null,
                    inputs = f.inputs
                });
            }

            foreach (var a in actionFrames)
            {
                var saved = new SavedActionFrame
                {
                    entityId = a.entityId,

                    time = a.timeFromStart,
                    sceneTime = a.timeFromScene,
                    scene = a.scene,

                    r = a.color.r,
                    g = a.color.g,
                    b = a.color.b,

                    sprites = new List<SavedSprite>()
                };

                if (a.sprites != null)
                {
                    for (int i = 0; i < a.sprites.Count; i++)
                    {
                        if (!a.sprites[i].IsDrawable) continue;
                        saved.sprites.Add(SavedSprite.From(a.sprites[i]));
                    }
                }

                trail.actionFrames.Add(saved);
            }

            string json = JsonConvert.SerializeObject(trail, Formatting.Indented);

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

            string path = Path.Combine(
                saveDirectory,
                string.IsNullOrEmpty(suffix) ? $"trail_{timestamp}.json" : $"trail_{timestamp}_{suffix}.json"
            );

            try
            {
                File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                Modding.Logger.LogError($"[GhostMacro] Не удалось сохранить трейл: {e.Message}");
                return null;
            }

            unsaved = false;
            Modding.Logger.Log($"Trail saved to {path}");
            return path;
        }
    }

}