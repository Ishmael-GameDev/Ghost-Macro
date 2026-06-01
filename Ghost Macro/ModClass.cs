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

        public TrailActions()
        {
            ToggleRecord = CreatePlayerAction("Toggle Record");
            StopRecord = CreatePlayerAction("Stop Record");
            Clear = CreatePlayerAction("Clear Trail");
            NextColor = CreatePlayerAction("Next Color");
            Playback = CreatePlayerAction("Playback");
            SaveTrail = CreatePlayerAction("Save Trail");
            OpenReplayMenu = CreatePlayerAction("Open Replay Menu");

            ToggleRecord.AddDefaultBinding(Key.J);
            StopRecord.AddDefaultBinding(Key.K);
            Clear.AddDefaultBinding(Key.L);
            NextColor.AddDefaultBinding(Key.N);
            Playback.AddDefaultBinding(Key.P);
            SaveTrail.AddDefaultBinding(Key.M);
            OpenReplayMenu.AddDefaultBinding(Key.O);
        }
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
    }

    public class GhostMacro : Mod, IGlobalSettings<GlobalSettings>, ICustomMenuMod
    {
        public static GlobalSettings Settings { get; private set; } = new GlobalSettings();

        public override string GetVersion() => "3.2.0";

        public void OnLoadGlobal(GlobalSettings s) => Settings = s;
        public GlobalSettings OnSaveGlobal() => Settings;

        //public static string currentScene;
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
            _menu ??= new Menu(
                "Ghost Macro",
                new Element[]
                {
                    new HorizontalOption(
                        "Record Mode",
                        "Toggle or separate keys",
                        new[] { "Toggle", "Separate" },
                        i => Settings.ToggleRecordWithSameKey = i == 0,
                        () => Settings.ToggleRecordWithSameKey ? 0 : 1
                    ),

                    new HorizontalOption(
                        "Trail Visibility",
                        "Show or hide trail rendering",
                        new[] { "Visible", "Hidden" },
                        i => Settings.TrailVisible = i == 0,
                        () => Settings.TrailVisible ? 0 : 1
                    ),

                    new HorizontalOption(
                        "Auto Hide",
                        "Hide trail while recording",
                        new[] { "ON", "OFF" },
                        i => Settings.AutoHide = i == 0,
                        () => Settings.AutoHide ? 0 : 1
                    ),

                    new HorizontalOption(
                        "Scene Playback Mode",
                        "Classic or delayed scene activation",
                        new[] { "Classic", "Delayed" },
                        i => {Settings.DelayedSceneActivation = i == 1;
                        var bh = UnityEngine.Object.FindObjectOfType<HitboxTrailBehaviour>();
                        if (bh != null)
                            bh.RebuildSceneCache();},
                        () => Settings.DelayedSceneActivation ? 1 : 0
                    ),

                    new HorizontalOption(
                        "Active Colors",
                        "How many colors are used",
                        new[] { "1","2","3","4","5","6" },
                        i => Settings.ActiveColors = i + 1,
                        () => Settings.ActiveColors - 1
                    ),

                    new HorizontalOption(
                        "Start Color",
                        "First color in cycle",
                        new[] { "Yellow","Red","Blue","Green","Cyan","Orange" },
                        i =>
                        {
                            Settings.StartColorIndex = i;
                            Settings.CurrentColorIndex = 0;
                        },
                        () => Settings.StartColorIndex
                    ),

                    new TextPanel("Controls"),

                    new KeyBind("Toggle Record", Settings.Binds.ToggleRecord),
                    new KeyBind("Stop Record", Settings.Binds.StopRecord),
                    new KeyBind("Clear Trail", Settings.Binds.Clear),
                    new KeyBind("Next Color", Settings.Binds.NextColor),
                    new KeyBind("Playback", Settings.Binds.Playback),
                    new KeyBind("Save Trail", Settings.Binds.SaveTrail),
                }
            );

            return _menu.GetMenuScreen(modListMenu);
        }
    }

    [System.Serializable]
    public class SavedTrail
    {
        public List<SavedFrame> frames = new();
    }

    [System.Serializable]
    public class SavedFrame
    {
        public string scene;

        public float time;
        public float sceneTime;

        public float ax, ay;
        public float bx, by;
        public float cx, cy;
        public float dx, dy;

        public float r, g, b;
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
        public static void LoadExternalTrail(SavedTrail trail)
        {
            var bh = UnityEngine.Object.FindObjectOfType<HitboxTrailBehaviour>();

            if (bh != null)
                bh.LoadTrail(trail);
        }
        public void LoadTrail(SavedTrail trail)
        {
            frames.Clear();

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

                    scene = f.scene
                });
            }

            visibleFrames = frames.Count;
        }
        private struct BoxFrame
        {
            public Vector3 a, b, c, d;
            public Color color;

            public float timeFromStart;
            public float timeFromScene;

            public string scene;
        }
        private string saveDirectory;

        private readonly List<BoxFrame> frames = new();
        private readonly List<BoxFrame> sceneFrames = new();
        private List<BoxFrame> renderFrames = new();

        private const int MaxFrames = 10000;

        private Material lineMaterial;

        private bool recording = false;
        private bool playing = false;
        private bool playingButtonBan = false;
        private bool delayedPlaying = false;
        private bool delayedPlayingActive = false;

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
            lineMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));

            lineMaterial.hideFlags = HideFlags.HideAndDontSave;

            lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            lineMaterial.SetInt("_ZWrite", 0);
            saveDirectory = Path.Combine(
                Application.persistentDataPath,
                "GhostMacro"
            );

            Directory.CreateDirectory(saveDirectory);
            currentScene = SceneTracker.CurrentScene;
            sceneStartTime = Time.time;
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
            sceneFrames.Clear();
            playbackIndex = 0;
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].scene == SceneTracker.CurrentScene)
                {
                    sceneFrames.Add(frames[i]);
                }
            }

            if (GhostMacro.Settings.DelayedSceneActivation && delayedPlaying)
            {
                delayedPlaybackStartTime = Time.time;
                delayedPlayingActive = true;
                playing = false;
                sceneFrames.Sort((a, b) =>
                a.timeFromScene.CompareTo(b.timeFromScene));
            }
            else
            {
                playing = false;
                sceneFrames.Sort((a, b) =>
                a.timeFromStart.CompareTo(b.timeFromStart));
            }
            // если НЕ проигрываем - рендерим сцену
            if (!playing)
            {
                renderFrames.Clear();
                if (!delayedPlayingActive)
                    renderFrames.AddRange(sceneFrames);
            }
        }
        void Update()
        {
            var binds = GhostMacro.Settings.Binds;
            var s = GhostMacro.Settings;

            // RECORD

            if (s.ToggleRecordWithSameKey)
            {
                if (binds.ToggleRecord.WasPressed)
                {
                    if (!recording && frames.Count == 0)
                        recordStartTime = Time.time;

                    recording = !recording;
                }
            }
            else
            {
                if (binds.ToggleRecord.WasPressed)
                {
                    if (!recording && frames.Count == 0)
                        recordStartTime = Time.time;

                    recording = true;
                }

                if (binds.StopRecord.WasPressed)
                    recording = false;
            }

            // CLEAR

            if (binds.Clear.WasPressed)
            {
                frames.RemoveAll(f => f.scene == SceneTracker.CurrentScene);

                RebuildSceneCache();

                playbackIndex = 0;
                playing = false;
            }

            // COLOR SWITCH

            if (binds.NextColor.WasPressed)
            {
                s.CurrentColorIndex++;

                int m = Mathf.Clamp(s.ActiveColors, 1, Palette.Length);

                if (s.CurrentColorIndex >= m)
                    s.CurrentColorIndex = 0;
            }

            if (binds.SaveTrail.WasPressed && !recording)
            {
                SaveTrailToJson();
            }

            if (binds.OpenReplayMenu.WasPressed)
            {
                ReplayMenuUI.Toggle();
            }
            if(GhostMacro.Settings.DelayedSceneActivation)
            {
                playing = false;
                playingButtonBan = true;
            }
            else
            {
                delayedPlaying = false;
                playingButtonBan = false;
                delayedPlayingActive = false;
            }
            // PLAYBACK

            if (binds.Playback.WasPressed && !recording && sceneFrames.Count > 0 && !playingButtonBan)
            {
                playing = !playing;

                if (playing)
                {
                    if (playbackIndex >= frames.Count || playbackIndex == 0)
                    {
                        renderFrames.Clear();
                        playbackIndex = 0;
                    }
                    Modding.Logger.Log(playbackIndex);
                    playbackStartTime = Time.time - sceneFrames[playbackIndex].timeFromStart;
                }
            }
            if (binds.Playback.WasPressed && !recording && playingButtonBan)
            {
                delayedPlaying = true;
            }

            if (playing)
            {
                float playbackTime = Time.time - playbackStartTime;

                while (playbackIndex < sceneFrames.Count &&
                       sceneFrames[playbackIndex].timeFromStart <= playbackTime)
                {
                    renderFrames.Add(sceneFrames[playbackIndex]);
                    playbackIndex++;
                }

                //visibleFrames = playbackIndex;

                if (playbackIndex >= sceneFrames.Count)
                {
                    playing = false;
                    Modding.Logger.Log("Playback ended");
                    playbackIndex = 0;
                }
            }
            if (delayedPlayingActive)
            {
                float playbackTime = Time.time - delayedPlaybackStartTime;

                while (playbackIndex < sceneFrames.Count &&
                       sceneFrames[playbackIndex].timeFromScene <= playbackTime)
                {
                    renderFrames.Add(sceneFrames[playbackIndex]);
                    playbackIndex++;
                }

                //visibleFrames = playbackIndex;

                if (playbackIndex >= sceneFrames.Count)
                {
                    delayedPlayingActive = false;
                    Modding.Logger.Log("Delayed Playback ended");
                    playbackIndex = 0;
                }
            }

            // RECORD FRAME

            if (!recording || HeroController.instance == null)
                return;

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

                scene = SceneTracker.CurrentScene
            };
            frames.Add(frame);

            if (frame.scene == SceneTracker.CurrentScene)
            {
                sceneFrames.Add(frame);
                renderFrames.Add(frame);
            }
            
            visibleFrames = frames.Count;

            if (frames.Count > MaxFrames)
                frames.RemoveAt(0);
        }

        void OnRenderObject()
        {
            if (renderFrames.Count == 0)
                return;

            if (!GhostMacro.Settings.TrailVisible)
                return;

            if (GhostMacro.Settings.AutoHide && recording)
            {
                return;
            }
                
            lineMaterial.SetPass(0);

            GL.PushMatrix();

            GL.Begin(GL.LINES);

            for (int i = 0; i < renderFrames.Count; i++)
            {
                float t = (float)i / renderFrames.Count;

                var f = renderFrames[i];

                GL.Color(new Color(f.color.r, f.color.g, f.color.b, t));

                DrawBox(f);
            }

            GL.End();

            GL.PopMatrix();
        }

        private void DrawBox(BoxFrame f)
        {
            Line(f.a, f.b);
            Line(f.b, f.c);
            Line(f.c, f.d);
            Line(f.d, f.a);
        }

        private void Line(Vector3 a, Vector3 b)
        {
            GL.Vertex(a);
            GL.Vertex(b);
        }
        private void SaveTrailToJson()
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
                    b = f.color.b
                });
            }

            string json = JsonConvert.SerializeObject(trail, Formatting.Indented);

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

            string path = Path.Combine(
                saveDirectory,
                $"trail_{timestamp}.json"
            );

            File.WriteAllText(path, json);

            Modding.Logger.Log($"Trail saved to {path}");
        }
    }
}