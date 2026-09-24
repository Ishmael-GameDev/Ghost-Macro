using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;

namespace GhostMacro
{
    internal static class ActionEntityCapture
    {
        public const float DetectionRadius = 6f;

        public static bool VerboseDiagnostics = false;

        private const float TeleportThreshold = 4f;

        private const int UnseenTicksToDrop = 15;

        private sealed class Entity
        {
            public GameObject root;
            public int entityId;
            public Vector3 lastPos;
            public bool hasLastPos;
            public int unseenTicks;
            public float lastRecordTime = -999f;
            public readonly List<CharacterFrameSnapshot> lastSprites = new List<CharacterFrameSnapshot>();
        }

        private static readonly Dictionary<GameObject, Entity> entities = new Dictionary<GameObject, Entity>();
        private static readonly List<GameObject> candidates = new List<GameObject>();
        private static readonly List<CharacterFrameSnapshot> scratch = new List<CharacterFrameSnapshot>();
        private static readonly HashSet<string> playerObjectNames = new HashSet<string>();
        private static readonly Dictionary<string, float> unknownLoggedAt = new Dictionary<string, float>();
        private static readonly List<(int entityId, List<CharacterFrameSnapshot> sprites)> results =
            new List<(int, List<CharacterFrameSnapshot>)>();

        private static int nextEntityId = 1;
        private static bool dictionaryBuilt;

        private static readonly HashSet<GameObject> loggedOwnershipCheck = new HashSet<GameObject>();

        private static readonly string[] FallbackNames =
        {
            "Slash", "AltSlash", "UpSlash", "DownSlash", "WallSlash",
            "Great Slash", "Dash Slash", "Cyclone Slash",
            "Q Charge", "Q Slam", "Q Trail", "Q Pillar", "Q Mega",
            "Q1 Pillar", "Q Slam Lines", "Q Slam 2", "Q Trail 2",
            "Scr Heads", "Scr Base", "Scr Heads 2", "Scr Base 2",
            "Fireball", "Fireball Blast", "Fireball2 Spiral", "Fireball2 Blast",
            "SD Charge", "SD Charge Wall", "SD Trail", "SD Crystal",
            "NA Charge", "NA Charged",
            "DG Set Charge", "DG Set Impact", "DG Set Release", "DG Set Ready", "DG Warp Charge",
            "Shade", "Shadow Recharge", "Double J Wings", "Wall Puff", "Jump Puff",
            "Acid Armour", "Shell Anim", "Hero Death", "Geo Get", "Heal Anim",
            "Lines Anim", "Wall Hit Effect", "Dung Cloud", "Dung Particle",
            "Q Flash Start", "SD Sharp Flash", "Charge Effect",
            "Hit L", "Hit R", "Hit U", "Nail Art Charged", "Nail Art Charged Flash"
        };

        public static void RebuildPrefabDictionary()
        {
            dictionaryBuilt = false;
            playerObjectNames.Clear();
            loggedOwnershipCheck.Clear(); // [ДИАГНОСТИКА] свежие сравнения на новую сцену/героя
            loggedContainerDrill.Clear(); // [ДИАГНОСТИКА]

            for (int i = 0; i < FallbackNames.Length; i++)
                playerObjectNames.Add(NormalizeName(FallbackNames[i]));

            HeroController hero = HeroController.instance;
            if (hero == null)
            {
                dictionaryBuilt = true;
                return;
            }

            FieldInfo[] heroFields = typeof(HeroController).GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < heroFields.Length; i++)
            {
                FieldInfo f = heroFields[i];
                object value;
                try { value = f.GetValue(hero); } catch { continue; }
                CollectNames(value, 0);
            }

            PlayMakerFSM[] fsms = hero.gameObject.GetComponentsInChildren<PlayMakerFSM>(true);
            for (int i = 0; i < fsms.Length; i++)
            {
                PlayMakerFSM fsm = fsms[i];
                if (fsm == null || fsm.FsmStates == null) continue;

                for (int s = 0; s < fsm.FsmStates.Length; s++)
                {
                    var state = fsm.FsmStates[s];
                    if (state == null || state.Actions == null) continue;

                    for (int a = 0; a < state.Actions.Length; a++)
                    {
                        var action = state.Actions[a];
                        if (action == null) continue;

                        FieldInfo[] actionFields = action.GetType().GetFields(
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        for (int fi = 0; fi < actionFields.Length; fi++)
                        {
                            object value;
                            try { value = actionFields[fi].GetValue(action); } catch { continue; }
                            CollectNames(value, 0);
                        }
                    }
                }
            }

            dictionaryBuilt = true;
            if (VerboseDiagnostics)
                Modding.Logger.Log($"[GhostMacro][Ownership] Собрано имён префабов игрока: {playerObjectNames.Count}. " +
                                   "Объекты с этими именами (и дети героя) считаются «нашими».");
            if (VerboseDiagnostics)
                Modding.Logger.Log("[GhostMacro][Ownership][Dump] " + string.Join(" | ", playerObjectNames));
        }
        private static void CollectNames(object value, int depth)
        {
            try
            {
                GameObject go = ExtractGameObject(value, depth);
                if (go != null) playerObjectNames.Add(NormalizeName(go.name));
            }
            catch
            {
            }
        }
        private static GameObject ExtractGameObject(object value, int depth)
        {
            if (value == null || depth > 4) return null;
            if (value is UnityEngine.Object uo)
            {
                if (uo == null) return null;
                if (uo is GameObject g) return g;
                if (uo is Component c) return c.gameObject;
                return null;
            }

            System.Type type = value.GetType();
            string ns = type.Namespace ?? string.Empty;
            if (!ns.StartsWith("HutongGames")) return null; // в чужие графы не лезем

            PropertyInfo[] props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < props.Length; i++)
            {
                PropertyInfo p = props[i];
                if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
                if (p.PropertyType == typeof(string)) continue;

                object inner;
                try { inner = p.GetValue(value, null); } catch { continue; }
                GameObject res = ExtractGameObject(inner, depth + 1);
                if (res != null) return res;
            }

            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo f = fields[i];
                if (f.FieldType == typeof(string)) continue;

                object inner;
                try { inner = f.GetValue(value); } catch { continue; }
                GameObject res = ExtractGameObject(inner, depth + 1);
                if (res != null) return res;
            }

            return null;
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            int cloneIdx = name.IndexOf("(Clone)", System.StringComparison.Ordinal);
            if (cloneIdx > 0) name = name.Substring(0, cloneIdx);

            return name.Trim();
        }

        private static bool IsKnownPlayerObject(GameObject go)
        {
            if (go == null) return false;
            return playerObjectNames.Contains(NormalizeName(go.name));
        }
        private static GameObject FindOwnershipRoot(GameObject go, Transform hero)
        {
            if (go == null) return null;

            GameObject known = IsKnownPlayerObject(go) ? go : null;
            Transform t = go.transform;

            while (t.parent != null)
            {
                Transform parent = t.parent;
                if (parent == hero) break;                 // сам герой корнем не становится
                if (!IsKnownPlayerObject(parent.gameObject)) break;

                known = parent.gameObject;
                t = parent;
            }
            if (known == null && go.transform.parent == hero)
                known = go;

            return known;
        }
        private static readonly HashSet<string> genericHeroContainers = new HashSet<string>
        {
            "attacks", "spells", "effects", "dream effects", "charm effects"
        };

        private static bool IsGenericContainer(GameObject go)
        {
            if (go == null) return false;
            return genericHeroContainers.Contains(NormalizeName(go.name).ToLowerInvariant());
        }
        private static readonly HashSet<GameObject> loggedContainerDrill = new HashSet<GameObject>();
        private static GameObject GetHeroChildRoot(GameObject go, Transform hero)
        {
            if (go == null || hero == null) return null;

            Transform t = go.transform;
            Transform prev = t;
            while (t.parent != null && t.parent != hero)
            {
                prev = t;
                t = t.parent;
            }

            if (t.parent != hero) return null; // объект вообще не под героем

            if (IsGenericContainer(t.gameObject))
            {
                GameObject specific = prev.gameObject;

                if (VerboseDiagnostics && loggedContainerDrill.Add(specific))
                {
                    Modding.Logger.Log(
                        $"[GhostMacro][Ownership][Drill] '{t.gameObject.name}' - раздаточный контейнер, " +
                        $"root сущности = '{specific.name}' (а не '{t.gameObject.name}')");
                }

                return specific;
            }

            return t.gameObject;
        }
        private static void DiagnoseHeroChildOwnership(tk2dBaseSprite sprite, GameObject naiveRoot, Transform hero)
        {
            if (!VerboseDiagnostics) return;
            if (naiveRoot == null) return;
            if (!loggedOwnershipCheck.Add(naiveRoot)) return;

            GameObject namedRoot = FindOwnershipRoot(sprite.gameObject, hero);
            bool differs = namedRoot != naiveRoot;

            string naivePath = SpriteDiagnostics.GetPath(naiveRoot.transform);
            string namedPath = namedRoot != null ? SpriteDiagnostics.GetPath(namedRoot.transform) : "<null>";

            tk2dBaseSprite[] under = naiveRoot.GetComponentsInChildren<tk2dBaseSprite>(true);
            List<string> names = new List<string>();
            for (int i = 0; i < under.Length; i++)
            {
                if (under[i] == null) continue;
                bool known = IsKnownPlayerObject(under[i].gameObject);
                names.Add(under[i].gameObject.name + (known ? "" : "[!нет в словаре]"));
            }

            Modding.Logger.Log(
                $"[GhostMacro][OwnershipCheck] триггер-спрайт='{sprite.gameObject.name}' " +
                $"наивныйКорень='{naivePath}' именнойКорень='{namedPath}' РАЗЛИЧАЮТСЯ={differs} | " +
                $"под наивным корнем спрайтов={under.Length}: [{string.Join(", ", names)}]");
        }

        public static List<(int entityId, List<CharacterFrameSnapshot> sprites)> Tick(Transform hero)
        {
            results.Clear();
            if (hero == null) return results;

            if (!dictionaryBuilt || hero != lastHero) { RebuildPrefabDictionary(); lastHero = hero; }

            GatherCandidates(hero);

            for (int i = 0; i < candidates.Count; i++)
            {
                GameObject root = candidates[i];
                if (root == null) continue;

                Entity entity;
                entities.TryGetValue(root, out entity);

                SpriteCapture.CaptureVisibleSprites(root, scratch);
                if (scratch.Count == 0)
                {
                    if (entity != null) entity.unseenTicks++;
                    continue;
                }

                if (entity == null)
                {
                    entity = new Entity { root = root, entityId = nextEntityId++ };
                    entities[root] = entity;
                    if (VerboseDiagnostics)
                        Modding.Logger.Log($"[GhostMacro][EntityBorn] entityId={entity.entityId} узел='{root.name}' " +
                                           $"путь='{SpriteDiagnostics.GetPath(root.transform)}'");
                }

                Vector3 pos = root.transform.position;
                if (entity.hasLastPos && Vector3.Distance(entity.lastPos, pos) > TeleportThreshold)
                {
                    int oldId = entity.entityId;
                    entity.entityId = nextEntityId++;
                    entity.lastSprites.Clear();
                    if (VerboseDiagnostics)
                        Modding.Logger.Log($"[GhostMacro][EntityBorn] entityId={entity.entityId} узел='{root.name}' " +
                                           $"путь='{SpriteDiagnostics.GetPath(root.transform)}' (teleport-сплит от entityId={oldId})");
                }

                entity.lastPos = pos;
                entity.hasLastPos = true;
                entity.unseenTicks = 0;

                if (SpriteCapture.SameSpriteSet(entity.lastSprites, scratch)) continue;

                float interval = ParticleInterval(scratch);
                if (interval > 0f && Time.time - entity.lastRecordTime < interval) continue;
                entity.lastRecordTime = Time.time;

                entity.lastSprites.Clear();
                entity.lastSprites.AddRange(scratch);
                results.Add((entity.entityId, new List<CharacterFrameSnapshot>(scratch)));
            }

            PruneUnseen();
            return results;
        }

        private static Transform lastHero;

        private static void GatherCandidates(Transform hero)
        {
            candidates.Clear();
            tk2dBaseSprite[] heroSprites = hero.GetComponentsInChildren<tk2dBaseSprite>(true);
            for (int i = 0; i < heroSprites.Length; i++)
            {
                tk2dBaseSprite sprite = heroSprites[i];
                if (sprite == null) continue;
                if (sprite.gameObject == hero.gameObject) continue;
                if (!SpriteCapture.IsSpriteVisible(sprite)) continue;

                GameObject root = GetHeroChildRoot(sprite.gameObject, hero);
                if (root != null)
                {
                    DiagnoseHeroChildOwnership(sprite, root, hero); // [ДИАГНОСТИКА]
                    AddCandidate(root);
                }
            }
            GatherExtraCandidates(hero);
            tk2dBaseSprite[] sceneSprites = Object.FindObjectsOfType<tk2dBaseSprite>();
            for (int i = 0; i < sceneSprites.Length; i++)
            {
                tk2dBaseSprite sceneSprite = sceneSprites[i];
                if (sceneSprite == null) continue;
                if (sceneSprite.gameObject == hero.gameObject) continue;
                if (!SpriteCapture.IsSpriteVisible(sceneSprite)) continue;

                GameObject go = sceneSprite.gameObject;
                if (go.transform.IsChildOf(hero)) continue;
                GameObject root = FindOwnershipRoot(go, hero);

                if (root == null)
                {
                    LogUnknown(go, hero);
                    continue;
                }

                if (Vector2.Distance(root.transform.position, hero.position) > DetectionRadius) continue;
                AddCandidate(root);
            }
            foreach (KeyValuePair<GameObject, Entity> kv in entities)
                if (kv.Key != null) AddCandidate(kv.Key);

            ScanUncapturedRenderers(hero);
        }
        private static readonly HashSet<string> notCapturedLogged = new HashSet<string>();
        private const int MaxNotCapturedLines = 30;
        private static float lastRendererScan = -999f;

        private static void ScanUncapturedRenderers(Transform hero)
        {
            if (!VerboseDiagnostics) return; // [ФИКС 13] скан всех рендереров сцены - только для отладки
            if (notCapturedLogged.Count >= MaxNotCapturedLines) return;
            if (Time.unscaledTime - lastRendererScan < 0.5f) return;
            lastRendererScan = Time.unscaledTime;

            Renderer[] renderers = Object.FindObjectsOfType<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || !r.enabled || !r.isVisible) continue;
                if (r.gameObject == hero.gameObject) continue;
                if (r.GetComponent<tk2dBaseSprite>() != null) continue; // это мы и так пишем
                if (ExtraEffectCapture.IsAllowed(r.gameObject)) continue; // и это теперь тоже

                GameObject go = r.gameObject;
                if (!go.transform.IsChildOf(hero))
                {
                    if (Vector2.Distance(go.transform.position, hero.position) > DetectionRadius) continue;
                    if (FindOwnershipRoot(go, hero) == null) continue;
                }

                string key = go.name + "|" + r.GetType().Name;
                if (!notCapturedLogged.Add(key)) continue;

                Modding.Logger.Log($"[GhostMacro][NotCaptured] виден эффект '{SpriteDiagnostics.GetPath(go.transform)}' " +
                                   $"({r.GetType().Name}) - этот тип рендерера в трейл не записывается.");

                if (notCapturedLogged.Count >= MaxNotCapturedLines) return;
            }
        }
        private static float ParticleInterval(List<CharacterFrameSnapshot> list)
        {
            float interval = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].sourceKind != ExtraEffectCapture.SourceParticles) continue;
                float v = list[i].particleWeight == 2
                    ? ExtraEffectCapture.HeavyRecordInterval
                    : ExtraEffectCapture.LightRecordInterval;
                if (v > interval) interval = v;
            }
            return interval;
        }
        private const float ExtraSceneScanInterval = 0.1f;
        private static float lastExtraSceneScan = -999f;

        private static void GatherExtraCandidates(Transform hero)
        {
            if (!ExtraEffectCapture.Enabled) return;

            AddExtraCandidates(hero.GetComponentsInChildren<ParticleSystemRenderer>(false), hero, true);
            AddExtraCandidates(hero.GetComponentsInChildren<SpriteRenderer>(false), hero, true);

            if (Time.time - lastExtraSceneScan < ExtraSceneScanInterval) return;
            lastExtraSceneScan = Time.time;

            AddExtraCandidates(Object.FindObjectsOfType<ParticleSystemRenderer>(), hero, false);
            AddExtraCandidates(Object.FindObjectsOfType<SpriteRenderer>(), hero, false);
        }

        private static void AddExtraCandidates(Renderer[] renderers, Transform hero, bool heroChildren)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;

                GameObject go = r.gameObject;
                bool isHeroChild = heroChildren || go.transform.IsChildOf(hero);
                if (!heroChildren && isHeroChild) continue; // уже обработано в проходе по герою

                if (!ExtraEffectCapture.IsAllowed(go)) continue;
                if (!ExtraEffectCapture.IsRendererVisible(r)) continue;
                if (!isHeroChild &&
                    Vector2.Distance(go.transform.position, hero.position) > DetectionRadius) continue;

                AddCandidate(go);
            }
        }

        private static void AddCandidate(GameObject go)
        {
            if (go == null) return;
            if (!candidates.Contains(go)) candidates.Add(go);
        }

        private static void PruneUnseen()
        {
            List<GameObject> toRemove = null;

            foreach (KeyValuePair<GameObject, Entity> kv in entities)
            {
                if (kv.Value.root != null && kv.Value.unseenTicks <= UnseenTicksToDrop) continue;

                if (toRemove == null) toRemove = new List<GameObject>();
                toRemove.Add(kv.Key);
            }

            if (toRemove != null)
                for (int i = 0; i < toRemove.Count; i++) entities.Remove(toRemove[i]);
        }
        private static void LogUnknown(GameObject go, Transform hero)
        {
            if (!VerboseDiagnostics) return; // [ФИКС 10] иначе спамит подсказками UI (Arrow Prompt)
            float dist = Vector2.Distance(go.transform.position, hero.position);
            if (dist > DetectionRadius) return;
            tk2dBaseSprite sprite = go.GetComponent<tk2dBaseSprite>();
            if (sprite == null) sprite = go.GetComponentInChildren<tk2dBaseSprite>(true);
            if (!SpriteCapture.IsSpriteVisible(sprite)) return;

            string key = go.name;
            if (unknownLoggedAt.TryGetValue(key, out float last) && Time.time - last < 5f) return;
            unknownLoggedAt[key] = Time.time;

            tk2dSpriteAnimator animator = go.GetComponent<tk2dSpriteAnimator>();
            string clip = (animator != null && animator.CurrentClip != null) ? animator.CurrentClip.name : "idle";

            Modding.Logger.Log($"[GhostMacro][Discover] Рядом с героем виден спрайт вне словаря: '{go.name}' " +
                               $"(клип '{clip}', dist={dist:F2}). Если это эффект игрока - добавьте имя " +
                               "в ActionEntityCapture.FallbackNames.");
        }
        public static void ClearTracking()
        {
            entities.Clear();
            candidates.Clear();
            results.Clear();
        }
        public static void EnsureIdAbove(int maxUsedId)
        {
            if (nextEntityId <= maxUsedId) nextEntityId = maxUsedId + 1;
        }

        public static void Reset()
        {
            entities.Clear();
            candidates.Clear();
            unknownLoggedAt.Clear();
            loggedOwnershipCheck.Clear(); // [ДИАГНОСТИКА]
            loggedContainerDrill.Clear(); // [ДИАГНОСТИКА]
            results.Clear();
            nextEntityId = 1;
            lastHero = null;
            dictionaryBuilt = false;
        }
    }
}
