using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json;

namespace GhostMacro
{
    public class ReplayMenuUI : MonoBehaviour
    {
        private static GameObject root;
        private static Transform panel;
        private static bool opened;

        private static readonly List<GameObject> spawned = new();

        private static string PathDir =>
            Path.Combine(Application.persistentDataPath, "GhostMacro");

        public static void Toggle()
        {
            if (root == null)
                CreateUI();

            opened = !opened;
            root.SetActive(opened);

            if (opened)
                RefreshFiles();
        }

        private static void CreateUI()
        {
            root = new GameObject("ReplayMenuUI");
            DontDestroyOnLoad(root);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;

            root.AddComponent<CanvasScaler>()
                .uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            root.AddComponent<GraphicRaycaster>();

            // DIM
            var dim = new GameObject("Dim");
            dim.transform.SetParent(root.transform, false);

            var dimImg = dim.AddComponent<Image>();
            dimImg.color = new Color(0, 0, 0, 0.45f);

            var dimRt = dim.GetComponent<RectTransform>();
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;

            // PANEL
            var p = new GameObject("Panel");
            p.transform.SetParent(dim.transform, false);

            var prt = p.AddComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(300, 360);

            var img = p.AddComponent<Image>();
            img.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);

            panel = p.transform;

            CreateText(panel, "TITLE", "REPLAYS", new Vector2(0, 150), 16);
        }

        private static void RefreshFiles()
        {
            foreach (var go in spawned)
                if (go) Destroy(go);

            spawned.Clear();

            if (!Directory.Exists(PathDir))
                return;

            var files = Directory.GetFiles(PathDir, "*.json");

            float y = 110f;

            foreach (var file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);

                var btn = CreateButton(panel, name, new Vector2(0, y));
                spawned.Add(btn);

                btn.GetComponent<Button>().onClick.AddListener(() =>
                {
                    LoadFile(file);
                });

                y -= 32f;
            }
        }

        private static void LoadFile(string file)
        {
            if (!File.Exists(file))
                return;

            var json = File.ReadAllText(file);
            var data = JsonConvert.DeserializeObject<SavedTrail>(json);

            HitboxTrailBehaviour.LoadExternalTrail(data);

            Toggle();
        }

        // UI

        private static GameObject CreateButton(Transform parent, string text, Vector2 pos)
        {
            var obj = new GameObject(text);
            obj.transform.SetParent(parent, false);

            var rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(260, 24);

            var img = obj.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.06f);

            var btn = obj.AddComponent<Button>();
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;

            CreateText(obj.transform, "t", text, Vector2.zero, 10);

            return obj;
        }

        private static Text CreateText(Transform parent, string name, string text, Vector2 pos, int size)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            var rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(260, 20);

            var t = obj.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = size;
            t.color = new Color(1f, 1f, 1f, 0.85f);
            t.alignment = TextAnchor.MiddleCenter;

            return t;
        }
    }
}