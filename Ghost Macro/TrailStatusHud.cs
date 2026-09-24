using UnityEngine;

namespace GhostMacro
{
    public enum TrailStatus
    {
        Empty,        // трейла нет
        Saved,        // трейл есть, всё сохранено
        Unsaved,      // есть несохранённый трейл (в любой сцене)
        Recording,
        Playing,
        Paused,       // проигрывание на паузе (можно перематывать)
        PlaybackArmed // отложенное проигрывание ждёт входа в комнату
    }
    internal static class TrailStatusHud
    {
        public static readonly string[] CornerLabels = { "Top Left", "Top Right", "Bottom Left", "Bottom Right", "Off" };
        public static float HoldProgress;
        public static float PlaybackProgress = -1f;

        private static Texture2D white;
        private static Texture2D circle;
        private static Texture2D triangle;
        private static Texture2D square;
        private static Texture2D pauseBars;
        private static GUIStyle labelStyle;
        private static int styleForHeight = -1;

        public static void Draw(TrailStatus status)
        {
            int corner = GhostMacro.Settings.StatusCorner;
            if (corner < 0 || corner > 3) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            EnsureTextures();

            string text;
            Color plate, iconColor;
            Texture2D icon;
            switch (status)
            {
                case TrailStatus.Recording:
                    text = "RECORDING"; icon = circle;
                    plate = new Color(0.45f, 0.05f, 0.05f, 0.6f); iconColor = new Color(1f, 0.25f, 0.25f, 1f);
                    break;
                case TrailStatus.Playing:
                    text = PlaybackProgress >= 0f
                        ? "PLAYING " + Mathf.RoundToInt(PlaybackProgress * 100f) + "%"
                        : "PLAYING";
                    icon = triangle;
                    plate = new Color(0.05f, 0.35f, 0.12f, 0.6f); iconColor = new Color(0.35f, 1f, 0.45f, 1f);
                    break;
                case TrailStatus.Paused:
                    text = PlaybackProgress >= 0f
                        ? "PAUSED " + Mathf.RoundToInt(PlaybackProgress * 100f) + "%"
                        : "PAUSED";
                    icon = pauseBars;
                    plate = new Color(0.08f, 0.2f, 0.35f, 0.6f); iconColor = new Color(0.6f, 0.82f, 1f, 1f);
                    break;
                case TrailStatus.PlaybackArmed:
                    text = "PLAYBACK: NEXT ROOM"; icon = triangle;
                    plate = new Color(0.05f, 0.25f, 0.3f, 0.6f); iconColor = new Color(0.4f, 0.9f, 1f, 1f);
                    break;
                case TrailStatus.Unsaved:
                    text = "UNSAVED TRAIL"; icon = square;
                    plate = new Color(0.45f, 0.3f, 0.02f, 0.6f); iconColor = new Color(1f, 0.8f, 0.2f, 1f);
                    break;
                case TrailStatus.Saved:
                    text = "TRAIL SAVED"; icon = square;
                    plate = new Color(0.18f, 0.18f, 0.18f, 0.5f); iconColor = new Color(0.65f, 0.65f, 0.65f, 1f);
                    break;
                default:
                    text = "TRAIL EMPTY"; icon = circle;
                    plate = new Color(0.18f, 0.18f, 0.18f, 0.5f); iconColor = new Color(0.55f, 0.55f, 0.55f, 1f);
                    break;
            }
            float h = Mathf.Max(22f, Screen.height * 0.034f);
            float pad = h * 0.3f;
            float iconSize = h * 0.5f;
            float margin = Screen.height * 0.02f;

            EnsureStyle(Mathf.RoundToInt(h * 0.48f));
            Vector2 textSize = labelStyle.CalcSize(new GUIContent(text));
            float w = pad + iconSize + pad * 0.8f + textSize.x + pad;

            float x = (corner == 0 || corner == 2) ? margin : Screen.width - margin - w;
            float y = (corner == 0 || corner == 1) ? margin : Screen.height - margin - h;

            Color prev = GUI.color;

            GUI.color = plate;
            GUI.DrawTexture(new Rect(x, y, w, h), white);

            GUI.color = iconColor;
            GUI.DrawTexture(new Rect(x + pad, y + (h - iconSize) * 0.5f, iconSize, iconSize), icon);

            GUI.color = new Color(1f, 1f, 1f, 0.95f);
            GUI.Label(new Rect(x + pad + iconSize + pad * 0.8f, y, textSize.x + 2f, h), text, labelStyle);
            float barH = Mathf.Max(2f, h * 0.1f);
            if (HoldProgress > 0f)
            {
                GUI.color = new Color(1f, 0.45f, 0.35f, 0.95f);
                GUI.DrawTexture(new Rect(x, y + h - barH, w * HoldProgress, barH), white);
            }
            else if ((status == TrailStatus.Playing || status == TrailStatus.Paused) && PlaybackProgress >= 0f)
            {
                GUI.color = status == TrailStatus.Paused
                    ? new Color(0.6f, 0.82f, 1f, 0.9f)
                    : new Color(0.35f, 1f, 0.45f, 0.9f);
                GUI.DrawTexture(new Rect(x, y + h - barH, w * PlaybackProgress, barH), white);
            }

            GUI.color = prev;
        }

        private static void EnsureStyle(int fontSize)
        {
            if (labelStyle != null && styleForHeight == fontSize) return;
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Overflow,
                wordWrap = false
            };
            labelStyle.normal.textColor = Color.white;
            styleForHeight = fontSize;
        }

        private static void EnsureTextures()
        {
            if (white == null)
            {
                white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }
            if (circle == null) circle = MakeIcon((u, v) => 0.5f - Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)));
            if (triangle == null) triangle = MakeIcon(TriangleSdf);
            if (square == null) square = MakeIcon((u, v) => 0.42f - Mathf.Max(Mathf.Abs(u - 0.5f), Mathf.Abs(v - 0.5f)));
            if (pauseBars == null) pauseBars = MakeIcon(PauseSdf);
        }
        private static float PauseSdf(float u, float v)
        {
            float vy = 0.36f - Mathf.Abs(v - 0.5f);
            float left = 0.1f - Mathf.Abs(u - 0.31f);
            float right = 0.1f - Mathf.Abs(u - 0.69f);
            return Mathf.Min(vy, Mathf.Max(left, right));
        }
        private static float TriangleSdf(float u, float v)
        {
            float x = u, y = v - 0.5f;
            float left = x - 0.12f;                       // вертикальная сторона
            float top = (0.92f - x) * 0.5f - y;           // верхняя наклонная
            float bottom = (0.92f - x) * 0.5f + y;        // нижняя наклонная
            return Mathf.Min(left, Mathf.Min(top, bottom) * 0.9f);
        }
        private static Texture2D MakeIcon(System.Func<float, float, float> sdf)
        {
            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            float edge = 1.5f / size;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float v = (y + 0.5f) / size;
                    float a = Mathf.Clamp01(sdf(u, v) / edge + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return tex;
        }
    }
}
