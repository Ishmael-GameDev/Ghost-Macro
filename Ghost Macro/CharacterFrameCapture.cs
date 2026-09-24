using UnityEngine;

namespace GhostMacro
{
    internal static class CharacterFrameCapture
    {
        private static readonly System.Collections.Generic.HashSet<int> loggedCaptureSuccess = new System.Collections.Generic.HashSet<int>();

        public static bool TryCapture(HeroController hero, out CharacterFrameSnapshot snapshot)
        {
            snapshot = default;
            if (hero == null) return false;

            tk2dSpriteAnimator animator = hero.GetComponent<tk2dSpriteAnimator>();
            if (animator == null)
            {
                LogOnce(hero.gameObject, "нет tk2dSpriteAnimator");
                return false;
            }

            return TryCaptureAnimator(animator, out snapshot);
        }

        public static bool TryCaptureAnimator(tk2dSpriteAnimator animator, out CharacterFrameSnapshot snapshot)
        {
            snapshot = default;
            if (animator == null) return false;

            tk2dBaseSprite sprite = animator.Sprite;
            if (sprite == null) sprite = animator.GetComponent<tk2dBaseSprite>();
            if (sprite == null)
            {
                LogOnce(animator.gameObject, "у аниматора нет спрайта");
                return false;
            }
            if (!SpriteCapture.TryCaptureSprite(sprite, false, out snapshot))
            {
                LogOnce(animator.gameObject, "спрайт не захватывается (нет Collection/def)");
                return false;
            }

            int id = animator.gameObject.GetInstanceID();
            if (loggedCaptureSuccess.Add(id))
            {
                Modding.Logger.Log($"[GhostMacro][Capture] OK '{animator.gameObject.name}': {snapshot.Describe()}");
            }

            return true;
        }

        private static readonly System.Collections.Generic.HashSet<string> loggedSkipReason = new System.Collections.Generic.HashSet<string>();

        private static void LogOnce(GameObject go, string reason)
        {
            int id = go != null ? go.GetInstanceID() : 0;
            string key = id + "|" + reason;
            if (loggedSkipReason.Add(key))
                Modding.Logger.Log($"[GhostMacro][Capture] Пропуск '{(go != null ? go.name : "null")}' (id={id}): {reason}");
        }
    }
}
