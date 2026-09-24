using UnityEngine;
using System.Collections.Generic;

namespace GhostMacro
{
    public struct ActionEntityFrame
    {
        public int entityId;
        public List<CharacterFrameSnapshot> sprites;
        public float timeFromStart;
        public float timeFromScene;
        public string scene;
        public Color color;
    }

    internal static class ActionEntityPlayback
    {
        private static readonly Dictionary<int, List<ActionEntityFrame>> sceneGroups = new Dictionary<int, List<ActionEntityFrame>>();
        private static readonly Dictionary<int, int> cursors = new Dictionary<int, int>();
        private static readonly Dictionary<int, List<ActionEntityFrame>> renderBuffers = new Dictionary<int, List<ActionEntityFrame>>();
        private static bool useSceneTime;

        public static void RecordLive(ActionEntityFrame frame, bool trailMode)
        {
            if (!renderBuffers.TryGetValue(frame.entityId, out List<ActionEntityFrame> buffer))
            {
                buffer = new List<ActionEntityFrame>();
                renderBuffers[frame.entityId] = buffer;
            }
            if (!trailMode) buffer.Clear();
            buffer.Add(frame);
            TrimParticleTrail(buffer);
        }
        private static void TrimParticleTrail(List<ActionEntityFrame> buffer)
        {
            if (buffer.Count == 0) return;

            ActionEntityFrame last = buffer[buffer.Count - 1];
            if (last.sprites == null || last.sprites.Count == 0) return;

            int cap = ExtraEffectCapture.TrailCapFor(last.sprites[0]);
            if (cap > 0 && buffer.Count > cap)
                buffer.RemoveRange(0, buffer.Count - cap);
        }

        public static void ClearRenderBuffers()
        {
            foreach (List<ActionEntityFrame> buffer in renderBuffers.Values) buffer.Clear();
        }

        public static void RebuildSceneGroups(List<ActionEntityFrame> sceneFrames, bool useSceneTimeForSort)
        {
            sceneGroups.Clear();
            cursors.Clear();
            renderBuffers.Clear();
            useSceneTime = useSceneTimeForSort;

            for (int i = 0; i < sceneFrames.Count; i++)
            {
                ActionEntityFrame f = sceneFrames[i];
                if (!sceneGroups.TryGetValue(f.entityId, out List<ActionEntityFrame> list))
                {
                    list = new List<ActionEntityFrame>();
                    sceneGroups[f.entityId] = list;
                }
                list.Add(f);
            }

            foreach (KeyValuePair<int, List<ActionEntityFrame>> kv in sceneGroups)
            {
                if (useSceneTime) kv.Value.Sort((a, b) => a.timeFromScene.CompareTo(b.timeFromScene));
                else kv.Value.Sort((a, b) => a.timeFromStart.CompareTo(b.timeFromStart));

                cursors[kv.Key] = 0;
                renderBuffers[kv.Key] = new List<ActionEntityFrame>();
            }
            if (ActionEntityCapture.VerboseDiagnostics) DumpSceneGroups();
        }

        private static void DumpSceneGroups()
        {
            foreach (KeyValuePair<int, List<ActionEntityFrame>> kv in sceneGroups)
            {
                List<ActionEntityFrame> list = kv.Value;
                if (list.Count == 0) continue;

                HashSet<string> distinctNames = new HashSet<string>();
                for (int i = 0; i < list.Count; i++)
                {
                    List<CharacterFrameSnapshot> sprites = list[i].sprites;
                    if (sprites == null) continue;
                    for (int s = 0; s < sprites.Count; s++)
                        if (sprites[s].spriteDefName != null) distinctNames.Add(sprites[s].spriteDefName);
                }

                float tMin = useSceneTime ? list[0].timeFromScene : list[0].timeFromStart;
                float tMax = useSceneTime ? list[list.Count - 1].timeFromScene : list[list.Count - 1].timeFromStart;

                Modding.Logger.Log(
                    $"[GhostMacro][PlaybackGroup] entityId={kv.Key} кадров={list.Count} " +
                    $"t=[{tMin:F2}..{tMax:F2}] def-имён={distinctNames.Count}: [{string.Join(", ", distinctNames)}]");
            }
        }

        public static void RevealAll()
        {
            foreach (KeyValuePair<int, List<ActionEntityFrame>> kv in sceneGroups)
            {
                if (!renderBuffers.TryGetValue(kv.Key, out List<ActionEntityFrame> buffer))
                {
                    buffer = new List<ActionEntityFrame>();
                    renderBuffers[kv.Key] = buffer;
                }
                buffer.Clear();
                buffer.AddRange(kv.Value);
                TrimParticleTrail(buffer);
                cursors[kv.Key] = kv.Value.Count;
            }
        }

        public static void Tick(float playbackTime, bool trailMode)
        {
            foreach (KeyValuePair<int, List<ActionEntityFrame>> kv in sceneGroups)
            {
                List<ActionEntityFrame> list = kv.Value;
                List<ActionEntityFrame> buffer = renderBuffers[kv.Key];
                int idx = cursors[kv.Key];

                while (idx < list.Count &&
                       (useSceneTime ? list[idx].timeFromScene : list[idx].timeFromStart) <= playbackTime)
                {
                    if (!trailMode) buffer.Clear();
                    buffer.Add(list[idx]);
                    idx++;
                }
                cursors[kv.Key] = idx;
                TrimParticleTrail(buffer);

                if (!trailMode && idx >= list.Count && idx > 0 && buffer.Count > 0)
                {
                    float lastFrameTime = useSceneTime
                        ? list[list.Count - 1].timeFromScene
                        : list[list.Count - 1].timeFromStart;
                    if (playbackTime > lastFrameTime)
                        buffer.Clear();
                }
            }
        }
        public static void FinishPlayback()
        {
            foreach (int key in new List<int>(cursors.Keys)) cursors[key] = 0;
        }

        public static void ResetPlayback()
        {
            foreach (int key in new List<int>(cursors.Keys)) cursors[key] = 0;
            foreach (List<ActionEntityFrame> buffer in renderBuffers.Values) buffer.Clear();
        }
        private static float lastDrawLogTime = -999f;
        public static void Draw(string currentScene)
        {
            bool any = false;
            foreach (List<ActionEntityFrame> buffer in renderBuffers.Values)
                if (buffer.Count > 0) { any = true; break; }
            if (!any) return;

            bool colorize = GhostMacro.Settings.ColorizeCharacterFrames;
            CharacterFrameDrawing.Begin();
            List<string> activeSummary = new List<string>();
            int activeEntities = 0;
            int totalSprites = 0;

            foreach (KeyValuePair<int, List<ActionEntityFrame>> kv in renderBuffers)
            {
                List<ActionEntityFrame> buffer = kv.Value;
                if (buffer.Count == 0) continue;
                activeEntities++;

                for (int i = 0; i < buffer.Count; i++)
                {
                    float alpha = buffer.Count > 1 ? (float)(i + 1) / buffer.Count : 1f;
                    ActionEntityFrame f = buffer[i];
                    if (currentScene != null && f.scene != currentScene) continue;
                    Color tint = colorize
                        ? new Color(f.color.r, f.color.g, f.color.b, alpha)
                        : new Color(1f, 1f, 1f, alpha);

                    if (f.sprites == null) continue;
                    totalSprites += f.sprites.Count;
                    for (int s = 0; s < f.sprites.Count; s++)
                        CharacterFrameDrawing.DrawFrame(f.sprites[s], tint);
                }

                ActionEntityFrame last = buffer[buffer.Count - 1];
                if (last.sprites != null && last.sprites.Count > 0)
                {
                    List<string> names = new List<string>();
                    for (int s = 0; s < last.sprites.Count; s++)
                        names.Add(last.sprites[s].spriteDefName);
                    activeSummary.Add($"id={kv.Key}:[{string.Join(",", names)}]");
                }
            }

            if (ActionEntityCapture.VerboseDiagnostics && Time.unscaledTime - lastDrawLogTime > 1f)
            {
                lastDrawLogTime = Time.unscaledTime;
                Modding.Logger.Log(
                    $"[GhostMacro][DrawStat] активных сущностей={activeEntities} " +
                    $"суммарно спрайтов за кадр={totalSprites}");
                Modding.Logger.Log(
                    $"[GhostMacro][DrawNow] " + string.Join(" | ", activeSummary));
            }

            CharacterFrameDrawing.End();
        }

        public static void Reset()
        {
            sceneGroups.Clear();
            cursors.Clear();
            renderBuffers.Clear();
        }
    }
}
