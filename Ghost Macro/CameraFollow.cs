using System.Reflection;
using UnityEngine;

namespace GhostMacro
{
    internal static class GameCameraUtil
    {
        public static bool IsMainGameCamera(Camera cam)
        {
            if (cam == null) return false;

            GameCameras gc = GameCameras.instance;
            if (gc != null && gc.mainCamera != null)
                return cam == gc.mainCamera;

            return cam == Camera.main;
        }
    }
    internal static class CameraFollow
    {
        public static readonly float[] SmoothingPresets = { 0f, 0.05f, 0.1f, 0.2f, 0.35f, 0.5f, 0.8f };
        public static readonly string[] SmoothingLabels = { "Off", "0.05s", "0.1s", "0.2s", "0.35s", "0.5s", "0.8s" };

        private static bool hooked;
        private static bool active;
        private static bool hasTarget;
        private static bool hasCurrent;
        private static Vector2 target;
        private static Vector2 current;
        private static int lastFrame = -1;

        public static void Hook()
        {
            if (hooked) return;
            Camera.onPreCull += OnPreCull;
            hooked = true;
        }

        public static void Unhook()
        {
            if (!hooked) return;
            Camera.onPreCull -= OnPreCull;
            hooked = false;
        }

        public static void SetActive(bool value)
        {
            if (value && !active) hasCurrent = false;    // старт - начинаем с текущей позиции камеры
            if (!value) hasTarget = false;
            active = value;
        }

        public static void SetTarget(Vector2 position)
        {
            target = position;
            hasTarget = true;
        }

        private static void OnPreCull(Camera cam)
        {
            if (!active || !hasTarget) return;
            if (!GameCameraUtil.IsMainGameCamera(cam)) return;
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;

            Transform rig = GetRig(cam);
            if (rig == null) return;

            Vector3 p = rig.position;
            if (!hasCurrent)
            {
                current = new Vector2(p.x, p.y);
                hasCurrent = true;
            }

            var presets = SmoothingPresets;
            float tau = presets[Mathf.Clamp(GhostMacro.Settings.CameraSmoothingIndex, 0, presets.Length - 1)];

            if (tau <= 0.0001f)
                current = target;
            else
                current = Vector2.Lerp(current, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime / tau));

            Vector2 c = ClampToScene(current);
            rig.position = new Vector3(c.x, c.y, p.z);
        }

        private static Transform GetRig(Camera cam)
        {
            GameCameras gc = GameCameras.instance;
            if (gc != null && gc.cameraController != null) return gc.cameraController.transform;
            return cam.transform.parent != null ? cam.transform.parent : cam.transform;
        }
        private static FieldInfo xLimitField, yLimitField;
        private static bool limitsSearched;

        private static Vector2 ClampToScene(Vector2 v)
        {
            GameCameras gc = GameCameras.instance;
            if (gc == null || gc.cameraController == null) return v;

            if (!limitsSearched)
            {
                limitsSearched = true;
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                xLimitField = typeof(CameraController).GetField("xLimit", flags);
                yLimitField = typeof(CameraController).GetField("yLimit", flags);
            }

            try
            {
                if (xLimitField != null)
                {
                    float xLimit = (float)xLimitField.GetValue(gc.cameraController);
                    if (xLimit > 14.6f) v.x = Mathf.Clamp(v.x, 14.6f, xLimit);
                }
                if (yLimitField != null)
                {
                    float yLimit = (float)yLimitField.GetValue(gc.cameraController);
                    if (yLimit > 8.3f) v.y = Mathf.Clamp(v.y, 8.3f, yLimit);
                }
            }
            catch { }

            return v;
        }
    }
}
