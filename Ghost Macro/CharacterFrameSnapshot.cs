using System.Collections.Generic;
using UnityEngine;

namespace GhostMacro
{
    public struct CharacterFrameSnapshot
    {
        public tk2dSpriteCollectionData collection;
        public tk2dSpriteDefinition spriteDef;
        public Material material;       // материал для отрисовки (копия, см. SpriteResolve.GetDrawMaterial)
        public Texture texture;         // текстура атласа, к которой относятся UV спрайта
        public int spriteId;
        public string spriteCollectionName;
        public string spriteCollectionGuid;
        public string spriteDefName;
        public string materialName;
        public string textureName;
        public int texturePage;
        public int sourceKind;
        public int particleWeight;
        public byte[] particleBlob;
        public bool fromFileKey;

        public bool HasParticleCloud
        {
            get { return particleBlob != null && ParticleCloud.Count(particleBlob) > 0; }
        }
        public string clipName;
        public int frameIndex;
        public string libraryName;
        public Matrix4x4 localToWorld;
        public Vector3 position;
        public Vector3 lossyScale;
        public float rotationZ;

        public Vector3 spriteScale;
        public Color spriteColor;
        public Vector3[] meshVerts;
        public Vector2[] meshUvs;
        public int[] meshTris;
        public Color32[] meshColors;   // могут содержать тинт спрайта - тогда spriteColor не применяется

        public bool HasMesh
        {
            get
            {
                return meshVerts != null && meshVerts.Length > 0 &&
                       meshTris != null && meshTris.Length >= 3 &&
                       meshUvs != null && meshUvs.Length >= meshVerts.Length;
            }
        }

        public bool IsDrawable
        {
            get
            {
                if (HasMesh) return true;
                if (HasParticleCloud) return true;
                if (spriteDef == null) return false;
                if (spriteDef.positions == null || spriteDef.uvs == null || spriteDef.indices == null) return false;
                return spriteDef.indices.Length >= 3;
            }
        }

        public string Describe()
        {
            return $"col='{spriteCollectionName}' def='{spriteDefName}' spriteId={spriteId} " +
                   $"mat='{materialName}' tex='{textureName}' clip='{clipName}' frame={frameIndex}" +
                   (HasMesh ? " [mesh]" : "");
        }
    }
    internal static class SpriteCapture
    {
        private sealed class MeshCheck
        {
            public Mesh mesh;
            public tk2dSpriteDefinition def;
            public tk2dSpriteCollectionData collection;
            public tk2dSpriteAnimator animator;
            public GameObject owner;
            public int spriteId;      // дешёвый признак "кадр сменился": tk2d меняет его при SetSprite
            public int frame;
            public Vector3 spriteScale; // масштаб спрайта меняется -> tk2d перестраивает меш
            public bool differs;
            public Vector3[] verts;
            public Vector2[] uvs;
            public int[] tris;
            public Color32[] colors;
        }
        private static readonly Dictionary<MeshRenderer, MeshCheck> meshChecks = new Dictionary<MeshRenderer, MeshCheck>();
        private const int MeshCacheLimit = 512;

        public static void ClearCache() { meshChecks.Clear(); }
        public static bool IsSpriteVisible(tk2dBaseSprite sprite)
        {
            if (sprite == null) return false;
            if (!sprite.gameObject.activeInHierarchy) return false;

            MeshRenderer mr = sprite.GetComponent<MeshRenderer>();
            if (mr == null || !mr.enabled) return false;

            if (sprite.color.a <= 0.004f) return false;
            Vector3 size = mr.bounds.size;
            if (size.x <= 0.0001f || size.y <= 0.0001f) return false;
            if (!mr.isVisible)
            {
                SpriteDiagnostics.ReportPhantom(sprite);
                return false;
            }

            return true;
        }

        public static bool TryCaptureSprite(tk2dBaseSprite sprite, bool requireVisible, out CharacterFrameSnapshot snap)
        {
            snap = default;
            if (sprite == null) return false;
            if (sprite.GetComponentInParent<Canvas>() != null) return false;
            if (requireVisible && !IsSpriteVisible(sprite)) return false;

            tk2dSpriteCollectionData col = sprite.Collection;
            if (col == null) return false;

            tk2dSpriteDefinition[] defs = col.spriteDefinitions;
            int spriteId = sprite.spriteId;
            if (defs == null || spriteId < 0 || spriteId >= defs.Length) return false;

            tk2dSpriteDefinition def = defs[spriteId];
            if (def == null) return false;

            MeshRenderer mr = sprite.GetComponent<MeshRenderer>();
            Transform t = sprite.transform;

            snap.collection = col;
            snap.spriteDef = def;
            snap.spriteId = spriteId;
            snap.spriteCollectionName = col.spriteCollectionName;
            snap.spriteCollectionGuid = col.spriteCollectionGUID;
            snap.spriteDefName = def.name;

            tk2dSpriteAnimator animator = sprite.GetComponent<tk2dSpriteAnimator>();
            if (animator != null && animator.CurrentClip != null)
            {
                snap.clipName = animator.CurrentClip.name;
                int frameCount = animator.CurrentClip.frames != null ? animator.CurrentClip.frames.Length : 0;
                snap.frameIndex = frameCount > 0 ? Mathf.Clamp(animator.CurrentFrame, 0, frameCount - 1) : 0;
                snap.libraryName = (animator.Library != null) ? animator.Library.name : null;
            }

            snap.localToWorld = t.localToWorldMatrix;
            snap.position = t.position;
            snap.lossyScale = t.lossyScale;
            snap.rotationZ = t.eulerAngles.z;

            Vector3 spScale = sprite.scale;
            snap.spriteScale = (spScale == Vector3.zero) ? Vector3.one : spScale;
            snap.spriteColor = sprite.color;
            Material rendererMaterial = (mr != null) ? mr.sharedMaterial : null;
            Material spriteMaterial = SpriteResolve.PickMaterial(col, def);
            Texture atlas = SpriteResolve.AtlasTexture(col, def);
            if (GhostMacro.Settings.CustomKnightSkins)
            {
                Texture live = SpriteResolve.LiveTexture(def, rendererMaterial, spriteMaterial);
                if (live != null) atlas = live;
            }

            snap.texture = atlas;
            snap.textureName = atlas != null ? atlas.name : null;
            snap.texturePage = def.materialId;

            Material chosen = rendererMaterial != null ? rendererMaterial : spriteMaterial;
            if (chosen != null && atlas != null && !SpriteResolve.SameTexture(chosen.mainTexture, atlas) &&
                spriteMaterial != null && SpriteResolve.SameTexture(spriteMaterial.mainTexture, atlas))
            {
                chosen = spriteMaterial;
            }

            snap.materialName = chosen != null ? chosen.name : null;
            snap.material = SpriteResolve.GetDrawMaterial(chosen, atlas);

            SpriteDiagnostics.ReportMaterial(sprite, def, rendererMaterial, spriteMaterial, atlas, chosen);
            ApplyMeshCheck(sprite, def, col, animator, mr, ref snap);

            return snap.IsDrawable;
        }

        private static void ApplyMeshCheck(
            tk2dBaseSprite sprite,
            tk2dSpriteDefinition def,
            tk2dSpriteCollectionData col,
            tk2dSpriteAnimator animator,
            MeshRenderer mr,
            ref CharacterFrameSnapshot snap)
        {
            if (mr == null) return;

            MeshFilter mf = mr.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) return;
            bool cacheable = sprite is tk2dSprite;

            if (cacheable && meshChecks.TryGetValue(mr, out MeshCheck cached) && cached != null &&
                cached.mesh == mesh && cached.def == def && cached.collection == col &&
                cached.animator == animator && cached.owner == sprite.gameObject &&
                cached.spriteId == snap.spriteId &&
                cached.frame == (animator != null ? animator.CurrentFrame : -1) &&
                cached.spriteScale == snap.spriteScale)
            {
                if (cached.differs)
                {
                    snap.meshVerts = cached.verts;
                    snap.meshUvs = cached.uvs;
                    snap.meshTris = cached.tris;
                    snap.meshColors = cached.colors;
                }
                return;
            }

            if (meshChecks.Count > MeshCacheLimit) meshChecks.Clear();

            bool differs = MeshDiffersFromDef(mesh, def, snap.spriteScale, sprite.color);
            MeshCheck entry = new MeshCheck
            {
                mesh = mesh,
                def = def,
                collection = col,
                animator = animator,
                owner = sprite.gameObject,
                spriteId = snap.spriteId,
                frame = animator != null ? animator.CurrentFrame : -1,
                spriteScale = snap.spriteScale,
                differs = differs
            };

            if (differs)
            {
                entry.verts = mesh.vertices;
                entry.uvs = mesh.uv;
                entry.tris = mesh.triangles;
                Color32[] colors = mesh.colors32;
                entry.colors = IsFlatColor(colors) ? null : colors;

                snap.meshVerts = entry.verts;
                snap.meshUvs = entry.uvs;
                snap.meshTris = entry.tris;
                snap.meshColors = entry.colors;
            }

            meshChecks[mr] = entry;

            SpriteDiagnostics.ReportMesh(sprite, def, differs, entry.colors != null);
        }

        private static bool IsFlatColor(Color32[] colors)
        {
            if (colors == null || colors.Length == 0) return true;
            Color32 first = colors[0];
            for (int i = 1; i < colors.Length; i++)
            {
                if (colors[i].r != first.r || colors[i].g != first.g ||
                    colors[i].b != first.b || colors[i].a != first.a) return false;
            }
            return true;
        }
        private static bool MeshDiffersFromDef(Mesh mesh, tk2dSpriteDefinition def, Vector3 scale, Color spriteColor)
        {
            if (def == null) return true;
            if (def.positions == null || def.uvs == null) return true;

            int meshCount = mesh.vertexCount;
            if (meshCount != def.positions.Length) return true;
            if (meshCount == 0 || meshCount > 512) return true;

            Vector3[] verts = mesh.vertices;
            Vector2[] uvs = mesh.uv;

            const float eps = 0.0005f;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 expected = Vector3.Scale(def.positions[i], scale);
                if (Mathf.Abs(verts[i].x - expected.x) > eps) return true;
                if (Mathf.Abs(verts[i].y - expected.y) > eps) return true;
            }

            for (int i = 0; i < uvs.Length && i < def.uvs.Length; i++)
            {
                if (Mathf.Abs(uvs[i].x - def.uvs[i].x) > eps) return true;
                if (Mathf.Abs(uvs[i].y - def.uvs[i].y) > eps) return true;
            }

            return false;
        }
        public static void CaptureVisibleSprites(GameObject root, List<CharacterFrameSnapshot> outList)
        {
            outList.Clear();
            if (root == null) return;

            tk2dBaseSprite[] sprites = root.GetComponentsInChildren<tk2dBaseSprite>(true);
            for (int i = 0; i < sprites.Length; i++)
            {
                if (!IsSpriteVisible(sprites[i])) continue;
                if (TryCaptureSprite(sprites[i], false, out CharacterFrameSnapshot snap))
                    outList.Add(snap);
            }
            ExtraEffectCapture.CaptureExtras(root, outList);

            SpriteDiagnostics.ReportRoot(root, outList);
        }

        public static bool SameSprite(CharacterFrameSnapshot a, CharacterFrameSnapshot b)
        {
            if (a.sourceKind != b.sourceKind) return false;
            if (!ReferenceEquals(a.particleBlob, b.particleBlob)) return false;
            if (a.spriteId != b.spriteId) return false;
            if (!ReferenceEquals(a.spriteDef, b.spriteDef)) return false;
            if (!ReferenceEquals(a.collection, b.collection)) return false;
            if (a.material != b.material) return false;
            if (a.localToWorld != b.localToWorld) return false;
            if (a.spriteColor != b.spriteColor) return false;
            if (!SameMesh(a, b)) return false;
            return true;
        }
        private static bool SameMesh(CharacterFrameSnapshot a, CharacterFrameSnapshot b)
        {
            if (a.meshVerts == null && b.meshVerts == null) return true;
            if (a.meshVerts == null || b.meshVerts == null) return false;
            if (ReferenceEquals(a.meshVerts, b.meshVerts)) return true;
            if (a.meshVerts.Length != b.meshVerts.Length) return false;
            if (a.meshVerts.Length == 0) return true;

            if (a.meshVerts[0] != b.meshVerts[0]) return false;
            if (a.meshUvs != null && b.meshUvs != null && a.meshUvs.Length > 0 && b.meshUvs.Length > 0 &&
                a.meshUvs[0] != b.meshUvs[0]) return false;

            return true;
        }

        public static bool SameSpriteSet(List<CharacterFrameSnapshot> a, List<CharacterFrameSnapshot> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!SameSprite(a[i], b[i])) return false;
            return true;
        }
    }
    internal static class SpriteResolve
    {
        private static readonly List<tk2dSpriteCollectionData> allCollections = new List<tk2dSpriteCollectionData>();
        private static readonly Dictionary<string, tk2dSpriteCollectionData> byGuid = new Dictionary<string, tk2dSpriteCollectionData>();
        private static readonly Dictionary<string, tk2dSpriteCollectionData> byName = new Dictionary<string, tk2dSpriteCollectionData>();
        private static bool cacheBuilt;
        private static readonly Dictionary<string, Material> drawMaterials = new Dictionary<string, Material>();
        private const int DrawMaterialLimit = 128;

        public static void InvalidateCache()
        {
            cacheBuilt = false;
            allCollections.Clear();
            byGuid.Clear();
            byName.Clear();
            SpriteCapture.ClearCache();
            drawMaterials.Clear();
            materialsByName.Clear();
            CharacterFrameDrawing.ClearMaterialCache(); // [ФИКС 6] кэш безопасных материалов по текстурам
            SpriteDiagnostics.Reset();
        }

        private static void EnsureCache()
        {
            if (cacheBuilt) return;
            cacheBuilt = true;

            allCollections.Clear();
            byGuid.Clear();
            byName.Clear();
            tk2dSpriteCollectionData[] found = Resources.FindObjectsOfTypeAll<tk2dSpriteCollectionData>();
            if (found == null) return;

            for (int i = 0; i < found.Length; i++)
            {
                tk2dSpriteCollectionData col = found[i];
                if (col == null) continue;
                allCollections.Add(col);

                string guid = col.spriteCollectionGUID;
                if (!string.IsNullOrEmpty(guid) && !byGuid.ContainsKey(guid)) byGuid[guid] = col;

                string name = col.spriteCollectionName;
                if (!string.IsNullOrEmpty(name) && !byName.ContainsKey(name)) byName[name] = col;
            }

            Modding.Logger.Log($"[GhostMacro][Resolve] Найдено коллекций спрайтов: {allCollections.Count} " +
                               $"(с GUID: {byGuid.Count}, с именем: {byName.Count})");
        }

        public static tk2dSpriteCollectionData FindCollection(string collectionName, string collectionGuid)
        {
            EnsureCache();

            if (!string.IsNullOrEmpty(collectionGuid) &&
                byGuid.TryGetValue(collectionGuid, out tk2dSpriteCollectionData byG) && byG != null)
                return byG;

            if (!string.IsNullOrEmpty(collectionName) && byName.TryGetValue(collectionName, out tk2dSpriteCollectionData byN) && byN != null)
                return byN;

            return null;
        }

        public static tk2dSpriteCollectionData FindCollectionContaining(string collectionName, string collectionGuid, string defName, int spriteId)
        {
            EnsureCache();

            for (int i = 0; i < allCollections.Count; i++)
            {
                tk2dSpriteCollectionData col = allCollections[i];
                if (col == null) continue;

                if (!string.IsNullOrEmpty(collectionGuid) && col.spriteCollectionGUID != collectionGuid) continue;
                if (!string.IsNullOrEmpty(collectionName) && col.spriteCollectionName != collectionName) continue;

                if (FindDefinition(col, defName, spriteId) != null) return col;
            }

            return FindCollection(collectionName, collectionGuid);
        }

        public static tk2dSpriteDefinition FindDefinition(tk2dSpriteCollectionData col, string defName, int spriteId)
        {
            if (col == null) return null;

            tk2dSpriteDefinition[] defs = col.spriteDefinitions;
            if (defs == null) return null;

            if (!string.IsNullOrEmpty(defName))
            {
                for (int i = 0; i < defs.Length; i++)
                    if (defs[i] != null && defs[i].name == defName) return defs[i];
            }

            if (spriteId >= 0 && spriteId < defs.Length && defs[spriteId] != null) return defs[spriteId];

            return null;
        }
        public static Material PickMaterial(tk2dSpriteCollectionData col, tk2dSpriteDefinition def)
        {
            if (def == null) return null;

            if (def.materialInst != null) return def.materialInst;

            int materialId = def.materialId;
            if (col != null && materialId >= 0)
            {
                Material[] insts = col.materialInsts;
                if (insts != null && materialId < insts.Length && insts[materialId] != null) return insts[materialId];

                Material[] mats = col.materials;
                if (mats != null && materialId < mats.Length && materialsSafe(mats, materialId)) return mats[materialId];

                if (materialId == 0 && col.material != null) return col.material;
            }

            return def.material;
        }

        private static bool materialsSafe(Material[] mats, int index)
        {
            return mats != null && index >= 0 && index < mats.Length && mats[index] != null;
        }
        public static Texture AtlasTexture(tk2dSpriteCollectionData col, tk2dSpriteDefinition def)
        {
            if (col == null || def == null) return null;
            return AtlasTexture(col, def, def.materialId);
        }
        public static Texture AtlasTexture(tk2dSpriteCollectionData col, tk2dSpriteDefinition def, int page)
        {
            if (col == null) return null;

            int materialId = page;

            if (materialId >= 0)
            {
                Texture[] insts = col.textureInsts;
                if (insts != null && materialId < insts.Length && insts[materialId] != null) return insts[materialId];

                Texture[] texs = col.textures;
                if (texs != null && materialId < texs.Length && texs[materialId] != null) return texs[materialId];
            }

            if (def != null)
            {
                if (def.material != null && def.material.mainTexture != null) return def.material.mainTexture;
                if (def.materialInst != null && def.materialInst.mainTexture != null) return def.materialInst.mainTexture;
            }
            if (col.material != null && col.material.mainTexture != null) return col.material.mainTexture;

            return null;
        }
        public static Texture FindTextureInCollection(tk2dSpriteCollectionData col, string name)
        {
            if (col == null || string.IsNullOrEmpty(name)) return null;

            Texture[] insts = col.textureInsts;
            if (insts != null)
            {
                for (int i = 0; i < insts.Length; i++)
                    if (insts[i] != null && insts[i].name == name) return insts[i];
            }

            Texture[] texs = col.textures;
            if (texs != null)
            {
                for (int i = 0; i < texs.Length; i++)
                    if (texs[i] != null && texs[i].name == name) return texs[i];
            }

            return null;
        }

        private static readonly Dictionary<string, Texture> texturesByName = new Dictionary<string, Texture>();
        private static readonly Dictionary<string, Material> materialsByName = new Dictionary<string, Material>();

        public static Material FindMaterialByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (materialsByName.TryGetValue(name, out Material cached) && cached != null) return cached;

            Material[] all = Resources.FindObjectsOfTypeAll<Material>();
            for (int i = 0; i < all.Length; i++)
            {
                Material m = all[i];
                if (m == null || m.name != name) continue;
                if (m.name.Contains("(GhostMacro)")) continue;
                materialsByName[name] = m;
                return m;
            }

            materialsByName[name] = null;
            return null;
        }

        private static Material fallbackSpriteMaterial;

        public static Material GetFallbackSpriteMaterial()
        {
            if (fallbackSpriteMaterial != null) return fallbackSpriteMaterial;
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) return null;
            fallbackSpriteMaterial = new Material(sh) { hideFlags = HideFlags.HideAndDontSave, name = "GhostMacro Fallback" };
            return fallbackSpriteMaterial;
        }
        public static Texture FindTextureByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (texturesByName.TryGetValue(name, out Texture cached) && cached != null) return cached;

            Texture2D[] all = Resources.FindObjectsOfTypeAll<Texture2D>();
            for (int i = 0; i < all.Length; i++)
            {
                Texture2D tex = all[i];
                if (tex == null) continue;
                if (tex.name == name)
                {
                    texturesByName[name] = tex;
                    return tex;
                }
            }

            texturesByName[name] = null;
            return null;
        }
        public static bool SameTexture(Texture a, Texture b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            string na = a.name;
            string nb = b.name;
            return !string.IsNullOrEmpty(na) && na == nb;
        }
        public static Texture LiveTexture(tk2dSpriteDefinition def, Material rendererMaterial, Material spriteMaterial)
        {
            if (def != null)
            {
                if (def.material != null && def.material.mainTexture != null) return def.material.mainTexture;
                if (def.materialInst != null && def.materialInst.mainTexture != null) return def.materialInst.mainTexture;
            }
            if (spriteMaterial != null && spriteMaterial.mainTexture != null) return spriteMaterial.mainTexture;
            if (rendererMaterial != null && rendererMaterial.mainTexture != null) return rendererMaterial.mainTexture;
            return null;
        }
        public static Texture CurrentTextureFor(CharacterFrameSnapshot snap)
        {
            if (snap.spriteDef == null) return null;
            if (GhostMacro.Settings.CustomKnightSkins)
            {
                Texture live = LiveTexture(snap.spriteDef, null, PickMaterial(snap.collection, snap.spriteDef));
                if (live != null) return live;
            }
            return AtlasTexture(snap.collection, snap.spriteDef, snap.texturePage);
        }

        public static Material GetDrawMaterial(Material source, Texture atlas)
        {
            if (source == null) return null;

            int srcId = source.GetInstanceID();
            int texId = atlas != null ? atlas.GetInstanceID() : 0;
            string key = srcId + "|" + texId;

            if (drawMaterials.TryGetValue(key, out Material cached) && cached != null) return cached;

            if (drawMaterials.Count > DrawMaterialLimit) drawMaterials.Clear();

            Material copy;
            try
            {
                copy = new Material(source);
            }
            catch
            {
                return source; // на всякий случай: если копию создать нельзя, рисуем исходным
            }

            copy.hideFlags = HideFlags.HideAndDontSave;
            copy.name = source.name + " (GhostMacro)";

            if (atlas != null)
            {
                if (copy.HasProperty("_MainTex")) copy.mainTexture = atlas;
                else if (copy.HasProperty("_Texture")) copy.SetTexture("_Texture", atlas);
            }
            if (copy.HasProperty("_FadeAmount")) copy.SetFloat("_FadeAmount", 0f);

            drawMaterials[key] = copy;
            return copy;
        }
    }
}
