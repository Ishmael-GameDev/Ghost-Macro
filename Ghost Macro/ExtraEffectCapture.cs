using System.Collections.Generic;
using UnityEngine;

namespace GhostMacro
{
    internal static class ExtraEffectCapture
    {
        public const int SourceTk2d = 0;
        public const int SourceSpriteRenderer = 1;
        public const int SourceParticles = 2;
        public static readonly HashSet<string> LightEffectNames = new HashSet<string>
        {
            "Double J Feather",                       // перья крыльев
            "Dash Ash", "Shadow Dash Blobs", "Shadow Ring", // деш / теневой деш
            "Roar Dust Lil"                           // мелкая пыль крика
        };

        public static readonly HashSet<string> HeavyEffectNames = new HashSet<string>
        {
            "Q Orbs", "Q Orbs 2",                     // пике
            "Scr Orbs", "Scr Orbs 2", "Roar Dust",    // крик
            "Fireball Top", "Fireball2 Top",          // искры огнешара (дочерний "particles")
            "SD Crystal Burst GL", "SD Crystal Burst GR", "SD Crystal Burst W" // суперпад
        };
        public const float LightRecordInterval = 0.05f;
        public const float HeavyRecordInterval = 0.1f;
        private const int LightMaxParticles = 64;
        private const int HeavyMaxParticles = 40;
        public const int LightTrailFrames = 10;
        public const int HeavyTrailFrames = 3;

        private const int ParticleBufferSize = 512;
        private static ParticleSystem.Particle[] particleBuffer = new ParticleSystem.Particle[ParticleBufferSize];
        public static bool Enabled => GhostMacro.Settings.ParticleEffectsMode != 2;
        private static bool HeavyEnabled => GhostMacro.Settings.ParticleEffectsMode == 0;
        public static int WeightOf(GameObject go)
        {
            Transform t = go != null ? go.transform : null;
            int guard = 0;
            while (t != null && guard++ < 8)
            {
                string n = NormalizeName(t.name);
                if (LightEffectNames.Contains(n)) return 1;
                if (HeavyEffectNames.Contains(n)) return 2;
                t = t.parent;
            }
            return 0;
        }

        public static bool IsAllowed(GameObject go)
        {
            if (!Enabled) return false;
            int w = WeightOf(go);
            return w == 1 || (w == 2 && HeavyEnabled);
        }
        public static bool IsHeavyFrame(CharacterFrameSnapshot snap)
        {
            return snap.sourceKind == SourceParticles && snap.particleWeight == 2;
        }

        public static int TrailCapFor(CharacterFrameSnapshot snap)
        {
            if (snap.sourceKind != SourceParticles) return 0; // 0 = без ограничения
            return snap.particleWeight == 2 ? HeavyTrailFrames : LightTrailFrames;
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int idx = name.IndexOf("(Clone)", System.StringComparison.Ordinal);
            if (idx > 0) name = name.Substring(0, idx);
            return name.Trim();
        }

        public static bool IsRendererVisible(Renderer r)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || !r.isVisible) return false;

            if (r is ParticleSystemRenderer)
            {
                ParticleSystem ps = r.GetComponent<ParticleSystem>();
                return ps != null && ps.particleCount > 0;
            }

            if (r is SpriteRenderer sr)
                return sr.sprite != null && sr.color.a > 0.004f;

            return false;
        }
        public static void CaptureExtras(GameObject root, List<CharacterFrameSnapshot> outList)
        {
            if (root == null || !Enabled) return;
            if (!IsAllowed(root)) return;

            SpriteRenderer sr = root.GetComponent<SpriteRenderer>();
            if (sr != null && IsRendererVisible(sr) && TryCaptureSpriteRenderer(sr, out CharacterFrameSnapshot s1))
                outList.Add(s1);

            ParticleSystemRenderer psr = root.GetComponent<ParticleSystemRenderer>();
            if (psr != null && IsRendererVisible(psr) &&
                TryCaptureParticles(psr, WeightOf(root), out CharacterFrameSnapshot s2))
                outList.Add(s2);
        }

        private static bool TryCaptureSpriteRenderer(SpriteRenderer sr, out CharacterFrameSnapshot snap)
        {
            snap = default;
            Sprite sprite = sr.sprite;
            if (sprite == null) return false;

            Vector2[] v2 = sprite.vertices;
            Vector2[] uv = sprite.uv;
            ushort[] tri16 = sprite.triangles;
            if (v2 == null || uv == null || tri16 == null || v2.Length == 0 || tri16.Length < 3) return false;

            float fx = sr.flipX ? -1f : 1f;
            float fy = sr.flipY ? -1f : 1f;

            Vector3[] verts = new Vector3[v2.Length];
            for (int i = 0; i < v2.Length; i++) verts[i] = new Vector3(v2[i].x * fx, v2[i].y * fy, 0f);

            int[] tris = new int[tri16.Length];
            for (int i = 0; i < tri16.Length; i++) tris[i] = tri16[i];

            Texture tex = sprite.texture;
            Material source = sr.sharedMaterial;
            if (source == null) return false;

            Transform t = sr.transform;
            FillCommon(ref snap, SourceSpriteRenderer, sr.gameObject.name, source, tex);
            snap.spriteDefName = sprite.name;
            snap.localToWorld = t.localToWorldMatrix;
            snap.position = t.position;
            snap.lossyScale = t.lossyScale;
            snap.rotationZ = t.eulerAngles.z;
            snap.spriteScale = Vector3.one;
            snap.spriteColor = sr.color;
            snap.meshVerts = verts;
            snap.meshUvs = (Vector2[])uv.Clone();
            snap.meshTris = tris;
            snap.meshColors = null;
            return snap.IsDrawable;
        }
        private const float HeavyMaxLifeFraction = 0.8f;
        private const float HeavyMinAlpha = 0.1f;
        private const float HeavyMaxSize = 1.25f;
        private const float LightMinAlpha = 0.03f;
        private const float LightMaxSize = 2.5f;

        private static readonly List<int> survivors = new List<int>(ParticleBufferSize);

        private static bool TryCaptureParticles(ParticleSystemRenderer psr, int weight, out CharacterFrameSnapshot snap)
        {
            snap = default;
            if (psr.renderMode == ParticleSystemRenderMode.Mesh) return false;

            ParticleSystem ps = psr.GetComponent<ParticleSystem>();
            if (ps == null) return false;

            Material source = psr.sharedMaterial;
            if (source == null) return false;
            Texture tex = source.mainTexture;

            int alive = ps.GetParticles(particleBuffer);
            if (alive <= 0) return false;

            bool heavy = weight == 2;
            float minAlpha = heavy ? HeavyMinAlpha : LightMinAlpha;
            float maxSize = heavy ? HeavyMaxSize : LightMaxSize;
            survivors.Clear();
            for (int i = 0; i < alive; i++)
            {
                ParticleSystem.Particle p = particleBuffer[i];
                if (heavy && p.startLifetime > 0f &&
                    1f - p.remainingLifetime / p.startLifetime > HeavyMaxLifeFraction) continue;
                if (p.GetCurrentColor(ps).a / 255f < minAlpha) continue;
                survivors.Add(i);
            }
            if (survivors.Count == 0) return false;
            int budget = heavy ? HeavyMaxParticles : LightMaxParticles;
            int count = Mathf.Min(survivors.Count, budget);
            float step = (float)survivors.Count / count;

            var main = ps.main;
            Transform pt = ps.transform;

            Matrix4x4 simToWorld = Matrix4x4.identity;
            if (main.simulationSpace == ParticleSystemSimulationSpace.Local)
                simToWorld = pt.localToWorldMatrix;
            else if (main.simulationSpace == ParticleSystemSimulationSpace.Custom && main.customSimulationSpace != null)
                simToWorld = main.customSimulationSpace.localToWorldMatrix;

            float sizeScale = 1f;
            if (main.scalingMode == ParticleSystemScalingMode.Hierarchy) sizeScale = Mathf.Abs(pt.lossyScale.x);
            else if (main.scalingMode == ParticleSystemScalingMode.Local) sizeScale = Mathf.Abs(pt.localScale.x);

            var tsa = ps.textureSheetAnimation;
            bool sheet = tsa.enabled && tsa.mode == ParticleSystemAnimationMode.Grid &&
                         tsa.numTilesX > 0 && tsa.numTilesY > 0;
            int tilesX = sheet ? Mathf.Clamp(tsa.numTilesX, 1, 255) : 0;
            int tilesY = sheet ? Mathf.Clamp(tsa.numTilesY, 1, 255) : 0;
            Vector3 origin = pt.position;
            ParticleCloud.BeginEncode(origin, tilesX, tilesY, count);
            for (int i = 0; i < count; i++)
            {
                ParticleSystem.Particle p = particleBuffer[survivors[Mathf.Min(survivors.Count - 1, (int)(i * step))]];

                Vector3 center = simToWorld.MultiplyPoint3x4(p.position);
                Vector3 size3 = p.GetCurrentSize3D(ps) * sizeScale;
                float big = Mathf.Max(size3.x, size3.y);
                if (big > maxSize) size3 *= maxSize / big;   // сохраняем пропорции

                int frame = sheet ? SheetFrameIndex(tsa, p, tilesX, tilesY) : 0;
                ParticleCloud.Add(center, size3.x, size3.y, p.rotation, frame, p.GetCurrentColor(ps));
            }
            byte[] blob = ParticleCloud.EndEncode();
            if (blob == null) return false;

            FillCommon(ref snap, SourceParticles, ps.gameObject.name, source, tex);
            snap.particleWeight = weight;
            snap.spriteDefName = ps.gameObject.name;
            snap.localToWorld = Matrix4x4.identity;   // облако хранит мировые координаты
            snap.position = origin;
            snap.lossyScale = Vector3.one;
            snap.spriteScale = Vector3.one;
            snap.spriteColor = Color.white;
            snap.particleBlob = blob;
            return snap.IsDrawable;
        }

        private static int SheetFrameIndex(ParticleSystem.TextureSheetAnimationModule tsa, ParticleSystem.Particle p,
            int tilesX, int tilesY)
        {
            bool singleRow = tsa.animation == ParticleSystemAnimationType.SingleRow;
            int total = singleRow ? tilesX : tilesX * tilesY;
            if (total <= 0) return 0;

            float life = p.startLifetime > 0f ? 1f - p.remainingLifetime / p.startLifetime : 0f;
            float cycles = Mathf.Max(1f, tsa.cycleCount);
            float t = (life * cycles) % 1f;

            float f;
            try { f = tsa.frameOverTime.Evaluate(t); } catch { f = t; }
            if (f <= 1.0001f) f *= total;          // кривая в нормализованном виде
            float start = 0f;
            try { start = tsa.startFrame.Evaluate(0f); } catch { }
            if (start > 0f && start <= 1.0001f) start *= total;

            int frame = Mathf.Clamp(Mathf.FloorToInt(f + start), 0, total - 1);
            if (!singleRow) return frame;

            int row = 0;
            try { row = Mathf.Clamp(tsa.rowIndex, 0, tilesY - 1); } catch { }
            return row * tilesX + frame;
        }

        private static void FillCommon(ref CharacterFrameSnapshot snap, int kind, string objectName, Material source, Texture tex)
        {
            snap.sourceKind = kind;
            snap.spriteId = -1;
            snap.spriteCollectionName = null;
            snap.spriteCollectionGuid = null;
            snap.clipName = objectName;
            snap.materialName = source.name;
            snap.texture = tex;
            snap.textureName = tex != null ? tex.name : null;
            snap.texturePage = -1;
            snap.material = SpriteResolve.GetDrawMaterial(source, tex);
        }
    }
    internal static class ParticleCloud
    {
        public const int HeaderSize = 16;
        public const int ParticleSize = 13;
        private const float PosStep = 1f / 128f;
        private const float SizeStep = 1f / 32f;

        private static byte[] buf;
        private static int pos;
        private static int written;
        private static Vector3 origin;

        public static void BeginEncode(Vector3 o, int tilesX, int tilesY, int capacity)
        {
            origin = o;
            buf = new byte[HeaderSize + capacity * ParticleSize];
            pos = 0;
            WriteFloat(o.x); WriteFloat(o.y); WriteFloat(o.z);
            pos += 2; // count - допишем в EndEncode
            buf[pos++] = (byte)tilesX;
            buf[pos++] = (byte)tilesY;
            written = 0;
        }

        public static void Add(Vector3 center, float sx, float sy, float rotationDeg, int frame, Color32 c)
        {
            int qx = Mathf.RoundToInt((center.x - origin.x) / PosStep);
            int qy = Mathf.RoundToInt((center.y - origin.y) / PosStep);
            if (qx < short.MinValue || qx > short.MaxValue || qy < short.MinValue || qy > short.MaxValue) return;
            if (pos + ParticleSize > buf.Length) return;

            WriteShort((short)qx);
            WriteShort((short)qy);
            buf[pos++] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(sx) / SizeStep), 1, 255);
            buf[pos++] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(sy) / SizeStep), 1, 255);
            float r = rotationDeg % 360f; if (r < 0f) r += 360f;
            buf[pos++] = (byte)(Mathf.RoundToInt(r / 360f * 256f) & 255);
            ushort f = (ushort)Mathf.Clamp(frame, 0, ushort.MaxValue);
            buf[pos++] = (byte)(f & 255); buf[pos++] = (byte)(f >> 8);
            buf[pos++] = c.r; buf[pos++] = c.g; buf[pos++] = c.b; buf[pos++] = c.a;
            written++;
        }

        public static byte[] EndEncode()
        {
            if (written == 0) { buf = null; return null; }
            buf[12] = (byte)(written & 255);
            buf[13] = (byte)(written >> 8);

            byte[] result = buf;
            int used = HeaderSize + written * ParticleSize;
            if (used != result.Length) System.Array.Resize(ref result, used);
            buf = null;
            return result;
        }

        public static int Count(byte[] blob)
        {
            if (blob == null || blob.Length < HeaderSize) return 0;
            int n = blob[12] | (blob[13] << 8);
            return Mathf.Min(n, (blob.Length - HeaderSize) / ParticleSize);
        }
        public static bool Decode(byte[] blob, out Vector3[] verts, out Vector2[] uvs, out Color32[] colors, out int[] tris)
        {
            verts = null; uvs = null; colors = null; tris = null;
            int n = Count(blob);
            if (n <= 0) return false;

            float ox = System.BitConverter.ToSingle(blob, 0);
            float oy = System.BitConverter.ToSingle(blob, 4);
            float oz = System.BitConverter.ToSingle(blob, 8);
            int tilesX = blob[14];
            int tilesY = blob[15];

            verts = new Vector3[n * 4];
            uvs = new Vector2[n * 4];
            colors = new Color32[n * 4];
            tris = new int[n * 6];

            int p = HeaderSize;
            for (int i = 0; i < n; i++)
            {
                float cx = ox + System.BitConverter.ToInt16(blob, p) * PosStep;
                float cy = oy + System.BitConverter.ToInt16(blob, p + 2) * PosStep;
                float hx = blob[p + 4] * SizeStep * 0.5f;
                float hy = blob[p + 5] * SizeStep * 0.5f;
                float rad = -(blob[p + 6] / 256f * 360f) * Mathf.Deg2Rad;
                int frame = blob[p + 7] | (blob[p + 8] << 8);
                Color32 c = new Color32(blob[p + 9], blob[p + 10], blob[p + 11], blob[p + 12]);
                p += ParticleSize;

                float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
                int v = i * 4;
                verts[v] = new Vector3(cx + (-hx * cs + hy * sn), cy + (-hx * sn - hy * cs), oz);
                verts[v + 1] = new Vector3(cx + (hx * cs + hy * sn), cy + (hx * sn - hy * cs), oz);
                verts[v + 2] = new Vector3(cx + (hx * cs - hy * sn), cy + (hx * sn + hy * cs), oz);
                verts[v + 3] = new Vector3(cx + (-hx * cs - hy * sn), cy + (-hx * sn + hy * cs), oz);

                float u0 = 0f, v0 = 0f, u1 = 1f, v1 = 1f;
                if (tilesX > 0 && tilesY > 0)
                {
                    int col = frame % tilesX;
                    int row = Mathf.Min(frame / tilesX, tilesY - 1);
                    float w = 1f / tilesX, h = 1f / tilesY;
                    u0 = col * w; u1 = u0 + w;
                    v1 = 1f - row * h; v0 = v1 - h;   // кадры листа сверху вниз, UV снизу вверх
                }
                uvs[v] = new Vector2(u0, v0);
                uvs[v + 1] = new Vector2(u1, v0);
                uvs[v + 2] = new Vector2(u1, v1);
                uvs[v + 3] = new Vector2(u0, v1);

                colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = c;

                int t = i * 6;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            return true;
        }

        private static void WriteFloat(float f)
        {
            byte[] b = System.BitConverter.GetBytes(f);
            buf[pos++] = b[0]; buf[pos++] = b[1]; buf[pos++] = b[2]; buf[pos++] = b[3];
        }

        private static void WriteShort(short v)
        {
            buf[pos++] = (byte)(v & 255);
            buf[pos++] = (byte)((v >> 8) & 255);
        }
    }
}
