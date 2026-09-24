using System.Reflection;
using InControl;
using UnityEngine;

namespace GhostMacro
{
    internal static class InputRecorder
    {
        public const int Left = 0, Right = 1, Up = 2, Down = 3,
                         Jump = 4, Attack = 5, Dash = 6, Cast = 7,
                         QuickCast = 8, SuperDash = 9, DreamNail = 10, QuickMap = 11;

        private static readonly string[] FieldNames =
        {
            "left", "right", "up", "down",
            "jump", "attack", "dash", "cast",
            "quickCast", "superDash", "dreamNail", "quickMap"
        };

        private static FieldInfo[] fields;
        private static ushort pending;

        public static bool Has(ushort mask, int bit) { return (mask & (1 << bit)) != 0; }
        public static void Accumulate() { pending |= ReadNow(); }
        public static ushort Consume()
        {
            ushort v = (ushort)(pending | ReadNow());
            pending = 0;
            return v;
        }

        public static void Reset() { pending = 0; }

        private static ushort ReadNow()
        {
            InputHandler ih = InputHandler.Instance;
            if (ih == null || ih.inputActions == null) return 0;

            object actions = ih.inputActions;
            if (fields == null)
            {
                fields = new FieldInfo[FieldNames.Length];
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                for (int i = 0; i < FieldNames.Length; i++)
                {
                    FieldInfo f = actions.GetType().GetField(FieldNames[i], flags);
                    fields[i] = (f != null && typeof(PlayerAction).IsAssignableFrom(f.FieldType)) ? f : null;
                }
            }

            ushort v = 0;
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo f = fields[i];
                if (f == null) continue;
                PlayerAction a = f.GetValue(actions) as PlayerAction;
                if (a != null && a.IsPressed) v |= (ushort)(1 << i);
            }
            return v;
        }
    }
    internal static class InputOverlayHud
    {
        public static readonly string[] ModeLabels = { "Off", "Bottom Left", "Bottom Center", "Bottom Right" };

        private static Texture2D white;
        private static GUIStyle style;
        private static int styleSize = -1;

        private static readonly (int bit, string label)[] Buttons =
        {
            (InputRecorder.Jump, "JUMP"), (InputRecorder.Attack, "ATK"),
            (InputRecorder.Dash, "DASH"), (InputRecorder.Cast, "CAST"),
            (InputRecorder.QuickCast, "QCAST"), (InputRecorder.SuperDash, "CDASH"),
            (InputRecorder.DreamNail, "DNAIL"), (InputRecorder.QuickMap, "MAP")
        };

        public static void Draw(ushort inputs)
        {
            int mode = GhostMacro.Settings.InputOverlayMode;
            if (mode < 1 || mode > 3) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            if (white == null)
            {
                white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }

            float k = Mathf.Max(20f, Screen.height * 0.036f);   // размер клавиши
            float gap = k * 0.12f;
            float btnW = k * 1.9f;
            float margin = Screen.height * 0.02f;
            float padW = 3 * k + 2 * gap;
            float btnsW = 4 * btnW + 3 * gap;
            float totalW = padW + k * 0.6f + btnsW;
            float totalH = 2 * k + gap;
            float panelPad = gap * 2f;

            float x;
            if (mode == 1) x = margin;
            else if (mode == 3) x = Screen.width - margin - totalW - 2 * panelPad;
            else x = (Screen.width - totalW) * 0.5f - panelPad;

            float y = Screen.height - margin - totalH - 2 * panelPad;
            int corner = GhostMacro.Settings.StatusCorner;
            if ((mode == 1 && corner == 2) || (mode == 3 && corner == 3))
                y -= Mathf.Max(22f, Screen.height * 0.034f) + gap * 2f;

            EnsureStyle(Mathf.RoundToInt(k * 0.42f));
            Color prev = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(x, y, totalW + 2 * panelPad, totalH + 2 * panelPad), white);

            float ox = x + panelPad, oy = y + panelPad;
            Key(new Rect(ox + k + gap, oy, k, k), "↑", InputRecorder.Has(inputs, InputRecorder.Up));
            Key(new Rect(ox, oy + k + gap, k, k), "←", InputRecorder.Has(inputs, InputRecorder.Left));
            Key(new Rect(ox + k + gap, oy + k + gap, k, k), "↓", InputRecorder.Has(inputs, InputRecorder.Down));
            Key(new Rect(ox + 2 * (k + gap), oy + k + gap, k, k), "→", InputRecorder.Has(inputs, InputRecorder.Right));
            float bx = ox + padW + k * 0.6f;
            for (int i = 0; i < Buttons.Length; i++)
            {
                int col = i % 4, row = i / 4;
                Rect r = new Rect(bx + col * (btnW + gap), oy + row * (k + gap), btnW, k);
                Key(r, Buttons[i].label, InputRecorder.Has(inputs, Buttons[i].bit));
            }

            GUI.color = prev;
        }

        private static void Key(Rect r, string label, bool pressed)
        {
            GUI.color = pressed ? new Color(1f, 1f, 1f, 0.92f) : new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(r, white);

            style.normal.textColor = pressed ? new Color(0.08f, 0.08f, 0.08f, 1f) : new Color(1f, 1f, 1f, 0.55f);
            GUI.color = Color.white;
            GUI.Label(r, label, style);
        }

        private static void EnsureStyle(int size)
        {
            if (style != null && styleSize == size) return;
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Overflow,
                wordWrap = false
            };
            styleSize = size;
        }
    }
}
