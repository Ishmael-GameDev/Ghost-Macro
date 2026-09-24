using System.Collections.Generic;
using UnityEngine;

namespace GhostMacro
{
    [System.Serializable]
    public class SavedSprite
    {
        public int spriteId;
        public string spriteCollectionName;
        public string spriteCollectionGuid;
        public string spriteDefName;
        public string materialName;
        public string textureName;
        public int texturePage = -1;
        public int sourceKind;
        public int particleWeight;
        public string pblob;

        public string clipName;
        public string libraryName;
        public int frameIndex;

        public float posX, posY, posZ;
        public float rotZ;
        public float lossyScaleX, lossyScaleY, lossyScaleZ;
        public float spriteScaleX, spriteScaleY, spriteScaleZ;

        public float m00, m01, m02, m03;
        public float m10, m11, m12, m13;
        public float m20, m21, m22, m23;
        public float m30, m31, m32, m33;

        public float csR, csG, csB, csA;
        public bool hasMesh;
        public float[] verts;   // x,y,z,...
        public float[] uvs;     // u,v,...
        public int[] tris;
        public float[] vcols;   // r,g,b,a,...

        public static SavedSprite From(CharacterFrameSnapshot s)
        {
            SavedSprite saved = new SavedSprite
            {
                spriteId = s.spriteId,
                spriteCollectionName = s.spriteCollectionName,
                spriteCollectionGuid = s.spriteCollectionGuid,
                spriteDefName = s.spriteDefName,
                materialName = s.materialName,
                textureName = s.textureName,
                texturePage = s.texturePage,
                sourceKind = s.sourceKind,
                particleWeight = s.particleWeight,

                clipName = s.clipName,
                libraryName = s.libraryName,
                frameIndex = s.frameIndex,

                posX = s.position.x,
                posY = s.position.y,
                posZ = s.position.z,
                rotZ = s.rotationZ,
                lossyScaleX = s.lossyScale.x,
                lossyScaleY = s.lossyScale.y,
                lossyScaleZ = s.lossyScale.z,
                spriteScaleX = s.spriteScale.x,
                spriteScaleY = s.spriteScale.y,
                spriteScaleZ = s.spriteScale.z,

                m00 = s.localToWorld.m00, m01 = s.localToWorld.m01, m02 = s.localToWorld.m02, m03 = s.localToWorld.m03,
                m10 = s.localToWorld.m10, m11 = s.localToWorld.m11, m12 = s.localToWorld.m12, m13 = s.localToWorld.m13,
                m20 = s.localToWorld.m20, m21 = s.localToWorld.m21, m22 = s.localToWorld.m22, m23 = s.localToWorld.m23,
                m30 = s.localToWorld.m30, m31 = s.localToWorld.m31, m32 = s.localToWorld.m32, m33 = s.localToWorld.m33,

                csR = s.spriteColor.r,
                csG = s.spriteColor.g,
                csB = s.spriteColor.b,
                csA = s.spriteColor.a,
            };

            if (s.particleBlob != null)
                saved.pblob = System.Convert.ToBase64String(s.particleBlob);

            if (s.HasMesh)
            {
                saved.hasMesh = true;
                saved.verts = Flatten(s.meshVerts);
                saved.uvs = Flatten(s.meshUvs);
                saved.tris = s.meshTris;
                saved.vcols = Flatten(s.meshColors);
            }

            return saved;
        }

        public CharacterFrameSnapshot ToSnapshot()
        {
            if (sourceKind != ExtraEffectCapture.SourceTk2d) return ToExtraSnapshot();

            CharacterFrameSnapshot snap = default;

            Vector3 lossyScale = new Vector3(lossyScaleX, lossyScaleY, lossyScaleZ);
            if (lossyScale == Vector3.zero) lossyScale = Vector3.one;

            Vector3 spriteScale = new Vector3(spriteScaleX, spriteScaleY, spriteScaleZ);
            if (spriteScale == Vector3.zero) spriteScale = Vector3.one;

            Matrix4x4 m = new Matrix4x4();
            m.m00 = m00; m.m01 = m01; m.m02 = m02; m.m03 = m03;
            m.m10 = m10; m.m11 = m11; m.m12 = m12; m.m13 = m13;
            m.m20 = m20; m.m21 = m21; m.m22 = m22; m.m23 = m23;
            m.m30 = m30; m.m31 = m31; m.m32 = m32; m.m33 = m33;

            if (m == default(Matrix4x4))
                m = Matrix4x4.TRS(new Vector3(posX, posY, posZ), Quaternion.Euler(0f, 0f, rotZ), lossyScale);

            snap.localToWorld = m;
            snap.position = new Vector3(posX, posY, posZ);
            snap.lossyScale = lossyScale;
            snap.rotationZ = rotZ;
            snap.spriteScale = spriteScale;
            snap.spriteColor = new Color(csR, csG, csB, csA);

            snap.clipName = clipName;
            snap.libraryName = libraryName;
            snap.frameIndex = frameIndex;
            snap.materialName = materialName;
            snap.textureName = textureName;
            if (hasMesh && verts != null && uvs != null && tris != null)
            {
                snap.meshVerts = UnflattenVectors(verts);
                snap.meshUvs = UnflattenUvs(uvs);
                snap.meshTris = tris;
                snap.meshColors = UnflattenColors(vcols);
            }

            tk2dSpriteCollectionData col = SpriteResolve.FindCollectionContaining(
                spriteCollectionName, spriteCollectionGuid, spriteDefName, spriteId);
            if (col == null && !snap.HasMesh)
            {
                Modding.Logger.Log($"[GhostMacro][Resolve] Коллекция не найдена: '{spriteCollectionName}' " +
                                   $"(guid='{spriteCollectionGuid}'), def='{spriteDefName}' - кадр пропущен.");
                return default;
            }

            if (col != null)
            {
                tk2dSpriteDefinition def = SpriteResolve.FindDefinition(col, spriteDefName, spriteId);
                snap.collection = col;
                snap.spriteDef = def;
                snap.spriteId = spriteId;
                snap.spriteCollectionName = col.spriteCollectionName;
                snap.spriteCollectionGuid = col.spriteCollectionGUID;
                snap.spriteDefName = def != null ? def.name : spriteDefName;
                Texture atlas = SpriteResolve.AtlasTexture(col, def, texturePage);
                if (atlas == null) atlas = SpriteResolve.FindTextureInCollection(col, textureName);
                if (atlas == null) atlas = SpriteResolve.FindTextureByName(textureName);
                if (GhostMacro.Settings.CustomKnightSkins)
                {
                    Texture live = SpriteResolve.LiveTexture(def, null, SpriteResolve.PickMaterial(col, def));
                    if (live != null) atlas = live;
                }

                snap.texture = atlas;
                snap.textureName = atlas != null ? atlas.name : textureName;
                snap.texturePage = def != null ? def.materialId : texturePage;
                snap.material = SpriteResolve.GetDrawMaterial(SpriteResolve.PickMaterial(col, def), atlas);

                SpriteDiagnostics.ReportResolve(def, texturePage, textureName, atlas);
            }

            if (!snap.IsDrawable)
                return default;

            return snap;
        }
        private CharacterFrameSnapshot ToExtraSnapshot()
        {
            byte[] blob = null;
            if (!string.IsNullOrEmpty(pblob))
            {
                try { blob = System.Convert.FromBase64String(pblob); } catch { blob = null; }
            }

            bool meshOk = hasMesh && verts != null && uvs != null && tris != null;
            if (!meshOk && blob == null) return default;

            CharacterFrameSnapshot snap = default;
            snap.particleBlob = blob;
            snap.sourceKind = sourceKind;
            snap.particleWeight = particleWeight;
            snap.spriteId = -1;
            snap.texturePage = -1;
            snap.spriteDefName = spriteDefName;
            snap.clipName = clipName;
            snap.materialName = materialName;

            Matrix4x4 m = new Matrix4x4();
            m.m00 = m00; m.m01 = m01; m.m02 = m02; m.m03 = m03;
            m.m10 = m10; m.m11 = m11; m.m12 = m12; m.m13 = m13;
            m.m20 = m20; m.m21 = m21; m.m22 = m22; m.m23 = m23;
            m.m30 = m30; m.m31 = m31; m.m32 = m32; m.m33 = m33;
            if (m == default(Matrix4x4)) m = Matrix4x4.identity;

            snap.localToWorld = m;
            snap.position = new Vector3(posX, posY, posZ);
            snap.lossyScale = Vector3.one;
            snap.spriteScale = Vector3.one;
            snap.spriteColor = new Color(csR, csG, csB, csA);

            if (meshOk)
            {
                snap.meshVerts = UnflattenVectors(verts);
                snap.meshUvs = UnflattenUvs(uvs);
                snap.meshTris = tris;
                snap.meshColors = UnflattenColors(vcols);
            }

            Material source = SpriteResolve.FindMaterialByName(materialName);
            Texture tex = source != null ? source.mainTexture : null;
            if (tex == null) tex = SpriteResolve.FindTextureByName(textureName);
            if (source == null) source = SpriteResolve.GetFallbackSpriteMaterial();
            if (source == null) return default;

            snap.texture = tex;
            snap.textureName = tex != null ? tex.name : textureName;
            snap.material = SpriteResolve.GetDrawMaterial(source, tex);

            return snap.IsDrawable ? snap : default;
        }

        private static float[] Flatten(Vector3[] source)
        {
            if (source == null) return null;
            float[] result = new float[source.Length * 3];
            for (int i = 0; i < source.Length; i++)
            {
                result[i * 3] = source[i].x;
                result[i * 3 + 1] = source[i].y;
                result[i * 3 + 2] = source[i].z;
            }
            return result;
        }

        private static float[] Flatten(Vector2[] source)
        {
            if (source == null) return null;
            float[] result = new float[source.Length * 2];
            for (int i = 0; i < source.Length; i++)
            {
                result[i * 2] = source[i].x;
                result[i * 2 + 1] = source[i].y;
            }
            return result;
        }

        private static float[] Flatten(Color32[] source)
        {
            if (source == null) return null;
            float[] result = new float[source.Length * 4];
            for (int i = 0; i < source.Length; i++)
            {
                result[i * 4] = source[i].r / 255f;
                result[i * 4 + 1] = source[i].g / 255f;
                result[i * 4 + 2] = source[i].b / 255f;
                result[i * 4 + 3] = source[i].a / 255f;
            }
            return result;
        }

        private static Vector3[] UnflattenVectors(float[] source)
        {
            int count = source.Length / 3;
            Vector3[] result = new Vector3[count];
            for (int i = 0; i < count; i++)
                result[i] = new Vector3(source[i * 3], source[i * 3 + 1], source[i * 3 + 2]);
            return result;
        }

        private static Vector2[] UnflattenUvs(float[] source)
        {
            int count = source.Length / 2;
            Vector2[] result = new Vector2[count];
            for (int i = 0; i < count; i++)
                result[i] = new Vector2(source[i * 2], source[i * 2 + 1]);
            return result;
        }

        private static Color32[] UnflattenColors(float[] source)
        {
            if (source == null) return null;
            int count = source.Length / 4;
            Color32[] result = new Color32[count];
            for (int i = 0; i < count; i++)
                result[i] = new Color(source[i * 4], source[i * 4 + 1], source[i * 4 + 2], source[i * 4 + 3]);
            return result;
        }
        public static SavedSprite FromLegacy(
            int spriteId, string collectionName, int frameIndex, string clipName, string libraryName,
            Vector3 position, Vector3 scale, float rotZ, Color spriteColor, bool hasMatrix, Matrix4x4 matrix)
        {
            tk2dSpriteCollectionData col = SpriteResolve.FindCollectionContaining(collectionName, null, null, spriteId);
            string defName = null;
            if (col != null)
            {
                tk2dSpriteDefinition def = SpriteResolve.FindDefinition(col, null, spriteId);
                if (def != null) defName = def.name;
            }

            CharacterFrameSnapshot snap = default;
            snap.collection = col;
            snap.spriteId = spriteId;
            snap.spriteCollectionName = collectionName;
            snap.spriteDefName = defName;
            snap.clipName = clipName;
            snap.libraryName = libraryName;
            snap.frameIndex = frameIndex;
            snap.position = position;
            snap.lossyScale = scale == Vector3.zero ? Vector3.one : scale;
            snap.rotationZ = rotZ;
            snap.spriteScale = Vector3.one;
            snap.spriteColor = spriteColor;
            snap.localToWorld = hasMatrix
                ? matrix
                : Matrix4x4.TRS(position, Quaternion.Euler(0f, 0f, rotZ), scale == Vector3.zero ? Vector3.one : scale);

            return From(snap);
        }
    }
    [System.Serializable]
    public class SavedSpriteList
    {
        public List<SavedSprite> sprites = new List<SavedSprite>();
    }
}
