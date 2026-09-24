using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace GhostMacro
{
    public struct TrailHeroFrame
    {
        public Vector2 min, max;          // хитбокс (оси выровнены - 2 угла вместо 4)
        public Color color;
        public float time, sceneTime;
        public string scene;
        public CharacterFrameSnapshot characterFrame;
        public ushort inputs;             // маска кнопок (InputRecorder), с версии 2
    }

    public sealed class GhostTrailData
    {
        public readonly List<TrailHeroFrame> heroFrames = new List<TrailHeroFrame>();
        public readonly List<ActionEntityFrame> actionFrames = new List<ActionEntityFrame>();
    }
    internal static class GhostTrailFormat
    {
        public const string Extension = ".ghost";
        private static readonly byte[] Magic = { (byte)'G', (byte)'H', (byte)'S', (byte)'T' };
        private const byte Version = 3;

        private const float QPos = 4096f;     // координаты
        private const float QLin = 65536f;    // поворот/масштаб матрицы, UV
        private const float QTime = 10000f;   // время
        private const byte FSceneChanged = 1, FColorChanged = 2, FHasSprite = 4, FInputsChanged = 8;
        private const byte SFullMatrix = 1, SSpriteScale = 2, SMesh = 4, SBlob = 8, SColor = 16, SMeshColors = 32;

        public static void Write(string path, List<TrailHeroFrame> heroFrames, List<ActionEntityFrame> actionFrames,
            TrailMetadata meta = null)
        {
            var strings = new StringTable();
            var keys = new KeyTable(strings);
            var body = new ByteWriter(heroFrames.Count * 24 + actionFrames.Count * 32 + 1024);
            body.VarUInt((uint)heroFrames.Count);
            long pt = 0, pst = 0;
            int pscene = -1;
            Color32 pcol = new Color32(0, 0, 0, 0);
            bool hasPcol = false;
            long[] pbox = new long[4];
            var heroCtx = new SpriteCtx();
            ushort pinputs = 0;

            for (int i = 0; i < heroFrames.Count; i++)
            {
                TrailHeroFrame f = heroFrames[i];
                int scene = strings.Id(f.scene);
                Color32 col = f.color;
                bool hasSprite = f.characterFrame.IsDrawable || f.characterFrame.fromFileKey;

                byte flags = 0;
                if (scene != pscene) flags |= FSceneChanged;
                if (!hasPcol || !SameRgb(col, pcol)) flags |= FColorChanged;
                if (hasSprite) flags |= FHasSprite;
                if (f.inputs != pinputs) flags |= FInputsChanged;
                body.U8(flags);

                if ((flags & FSceneChanged) != 0) { body.VarUInt((uint)scene); pscene = scene; }
                if ((flags & FColorChanged) != 0) { body.U8(col.r); body.U8(col.g); body.U8(col.b); pcol = col; hasPcol = true; }
                if ((flags & FInputsChanged) != 0) { body.U8((byte)(f.inputs & 255)); body.U8((byte)(f.inputs >> 8)); pinputs = f.inputs; }

                long t = Q(f.time, QTime), st = Q(f.sceneTime, QTime);
                body.VarLong(t - pt); body.VarLong(st - pst); pt = t; pst = st;

                long b0 = Q(f.min.x, QPos), b1 = Q(f.min.y, QPos), b2 = Q(f.max.x, QPos), b3 = Q(f.max.y, QPos);
                body.VarLong(b0 - pbox[0]); body.VarLong(b1 - pbox[1]);
                body.VarLong(b2 - pbox[2]); body.VarLong(b3 - pbox[3]);
                pbox[0] = b0; pbox[1] = b1; pbox[2] = b2; pbox[3] = b3;

                if (hasSprite) WriteSprite(body, f.characterFrame, keys, heroCtx);
            }
            body.VarUInt((uint)actionFrames.Count);
            pt = 0; pst = 0; pscene = -1; hasPcol = false;
            int pentity = 0;
            var entityCtx = new Dictionary<int, SpriteCtx>();

            for (int i = 0; i < actionFrames.Count; i++)
            {
                ActionEntityFrame a = actionFrames[i];
                int scene = strings.Id(a.scene);
                Color32 col = a.color;

                byte flags = 0;
                if (scene != pscene) flags |= FSceneChanged;
                if (!hasPcol || !SameRgb(col, pcol)) flags |= FColorChanged;
                body.U8(flags);

                if ((flags & FSceneChanged) != 0) { body.VarUInt((uint)scene); pscene = scene; }
                if ((flags & FColorChanged) != 0) { body.U8(col.r); body.U8(col.g); body.U8(col.b); pcol = col; hasPcol = true; }

                body.VarLong(a.entityId - pentity); pentity = a.entityId;
                long t = Q(a.timeFromStart, QTime), st = Q(a.timeFromScene, QTime);
                body.VarLong(t - pt); body.VarLong(st - pst); pt = t; pst = st;

                SpriteCtx ctx;
                if (!entityCtx.TryGetValue(a.entityId, out ctx)) { ctx = new SpriteCtx(); entityCtx[a.entityId] = ctx; }

                int count = 0;
                if (a.sprites != null)
                    for (int s = 0; s < a.sprites.Count; s++) if (Writable(a.sprites[s])) count++;
                body.VarUInt((uint)count);

                if (a.sprites != null)
                    for (int s = 0; s < a.sprites.Count; s++)
                        if (Writable(a.sprites[s])) WriteSprite(body, a.sprites[s], keys, ctx);
            }
            var payload = new ByteWriter(body.Length + 4096);
            strings.Write(payload);
            keys.Write(payload);
            payload.Bytes(body.Buffer, 0, body.Length);

            byte[] raw = payload.ToArray();
            byte compression = 1;
            byte[] packed;
            try { packed = Deflate(raw); }
            catch (Exception e)
            {
                Modding.Logger.Log("[GhostMacro][Format] Deflate недоступен, пишем без него: " + e.Message);
                compression = 0;
                packed = raw;
            }

            byte[] metaBytes = meta != null
                ? Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(meta, Formatting.None))
                : new byte[0];

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                fs.Write(Magic, 0, 4);
                fs.WriteByte(Version);
                fs.WriteByte(compression);
                fs.Write(BitConverter.GetBytes((uint)metaBytes.Length), 0, 4);
                fs.Write(metaBytes, 0, metaBytes.Length);
                fs.Write(BitConverter.GetBytes((uint)raw.Length), 0, 4);
                fs.Write(packed, 0, packed.Length);
            }
        }

        private static bool Writable(CharacterFrameSnapshot s) { return s.IsDrawable || s.fromFileKey; }
        public static void RewriteMetadata(string path, TrailMetadata meta)
        {
            byte[] file = File.ReadAllBytes(path);
            if (file.Length < 10 || file[0] != Magic[0] || file[1] != Magic[1] || file[2] != Magic[2] || file[3] != Magic[3])
                throw new InvalidDataException("не файл .ghost");

            string tmp = path + ".tmp";
            if (file[4] >= 3)
            {
                int oldMetaLen = (int)BitConverter.ToUInt32(file, 6);
                int restStart = 10 + oldMetaLen;
                if (restStart > file.Length) throw new InvalidDataException("файл обрезан");

                byte[] metaBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(meta, Formatting.None));
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                {
                    fs.Write(file, 0, 6);                                   // сигнатура, версия, сжатие
                    fs.Write(BitConverter.GetBytes((uint)metaBytes.Length), 0, 4);
                    fs.Write(metaBytes, 0, metaBytes.Length);
                    fs.Write(file, restStart, file.Length - restStart);     // длина + сжатый трейл как есть
                }
            }
            else
            {
                GhostTrailData data = Read(path, false);
                Write(tmp, data.heroFrames, data.actionFrames, meta);
            }

            File.Delete(path);
            File.Move(tmp, path);
        }
        public static GhostTrailData ReadRaw(string path) { return Read(path, false); }
        public static TrailMetadata ReadMetadata(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    byte[] head = new byte[10];
                    if (fs.Read(head, 0, 10) != 10) return null;
                    if (head[0] != Magic[0] || head[1] != Magic[1] || head[2] != Magic[2] || head[3] != Magic[3]) return null;
                    if (head[4] < 3) return null;

                    int metaLen = (int)BitConverter.ToUInt32(head, 6);
                    if (metaLen <= 0 || metaLen > 4 * 1024 * 1024) return null;

                    byte[] meta = new byte[metaLen];
                    int read = 0;
                    while (read < metaLen)
                    {
                        int n = fs.Read(meta, read, metaLen - read);
                        if (n <= 0) return null;
                        read += n;
                    }
                    return JsonConvert.DeserializeObject<TrailMetadata>(Encoding.UTF8.GetString(meta));
                }
            }
            catch
            {
                return null;
            }
        }

        private sealed class SpriteCtx
        {
            public readonly long[] m = new long[7];
        }

        private static void WriteSprite(ByteWriter w, CharacterFrameSnapshot s, KeyTable keys, SpriteCtx ctx)
        {
            w.VarUInt((uint)keys.Id(s));

            Matrix4x4 m = s.localToWorld;
            bool full = !IsSimple2D(m);
            bool scale = s.spriteScale != Vector3.one && s.spriteScale != Vector3.zero;
            bool mesh = s.HasMesh;
            bool blob = s.particleBlob != null;
            Color32 c = s.spriteColor;
            bool color = c.r != 255 || c.g != 255 || c.b != 255 || c.a != 255;
            bool meshColors = mesh && s.meshColors != null && s.meshColors.Length >= s.meshVerts.Length;

            byte flags = 0;
            if (full) flags |= SFullMatrix;
            if (scale) flags |= SSpriteScale;
            if (mesh) flags |= SMesh;
            if (blob) flags |= SBlob;
            if (color) flags |= SColor;
            if (meshColors) flags |= SMeshColors;
            w.U8(flags);

            if (full)
            {
                for (int i = 0; i < 16; i++) w.F32(m[i]);
            }
            else
            {
                long[] v =
                {
                    Q(m.m00, QLin), Q(m.m01, QLin), Q(m.m10, QLin), Q(m.m11, QLin),
                    Q(m.m03, QPos), Q(m.m13, QPos), Q(m.m23, QPos)
                };
                for (int i = 0; i < 7; i++) { w.VarLong(v[i] - ctx.m[i]); ctx.m[i] = v[i]; }
            }

            if (scale) { w.F32(s.spriteScale.x); w.F32(s.spriteScale.y); w.F32(s.spriteScale.z); }
            if (color) { w.U8(c.r); w.U8(c.g); w.U8(c.b); w.U8(c.a); }

            if (mesh)
            {
                Vector3[] vs = s.meshVerts;
                w.VarUInt((uint)vs.Length);
                long px = 0, py = 0, pz = 0;
                for (int i = 0; i < vs.Length; i++)
                {
                    long x = Q(vs[i].x, QPos), y = Q(vs[i].y, QPos), z = Q(vs[i].z, QPos);
                    w.VarLong(x - px); w.VarLong(y - py); w.VarLong(z - pz);
                    px = x; py = y; pz = z;
                }

                Vector2[] uv = s.meshUvs;
                w.VarUInt((uint)uv.Length);
                long pu = 0, pv = 0;
                for (int i = 0; i < uv.Length; i++)
                {
                    long u = Q(uv[i].x, QLin), vv = Q(uv[i].y, QLin);
                    w.VarLong(u - pu); w.VarLong(vv - pv);
                    pu = u; pv = vv;
                }

                int[] tris = s.meshTris;
                w.VarUInt((uint)tris.Length);
                int ptri = 0;
                for (int i = 0; i < tris.Length; i++) { w.VarLong(tris[i] - ptri); ptri = tris[i]; }

                if (meshColors)
                    for (int i = 0; i < vs.Length; i++)
                    {
                        Color32 mc = s.meshColors[i];
                        w.U8(mc.r); w.U8(mc.g); w.U8(mc.b); w.U8(mc.a);
                    }
            }

            if (blob)
            {
                w.VarUInt((uint)s.particleBlob.Length);
                w.Bytes(s.particleBlob, 0, s.particleBlob.Length);
            }
        }

        private static bool IsSimple2D(Matrix4x4 m)
        {
            const float e = 1e-6f;
            return Mathf.Abs(m.m02) < e && Mathf.Abs(m.m12) < e &&
                   Mathf.Abs(m.m20) < e && Mathf.Abs(m.m21) < e && Mathf.Abs(m.m22 - 1f) < e &&
                   Mathf.Abs(m.m30) < e && Mathf.Abs(m.m31) < e && Mathf.Abs(m.m32) < e &&
                   Mathf.Abs(m.m33 - 1f) < e;
        }

        private static bool SameRgb(Color32 a, Color32 b) { return a.r == b.r && a.g == b.g && a.b == b.b; }

        private static long Q(float v, float scale) { return (long)Math.Round((double)v * scale); }

        public static bool IsGhostFile(string path)
        {
            return string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase);
        }
        public static GhostTrailData Read(string path, bool resolve = true)
        {
            byte[] file = File.ReadAllBytes(path);
            if (file.Length < 10 || file[0] != Magic[0] || file[1] != Magic[1] || file[2] != Magic[2] || file[3] != Magic[3])
                throw new InvalidDataException("не файл .ghost");

            byte version = file[4];
            if (version > Version) throw new InvalidDataException("файл новее, чем поддерживает мод (v" + version + ")");

            byte compression = file[5];
            int offset = 6;
            if (version >= 3)
            {
                int metaLen = (int)BitConverter.ToUInt32(file, 6);
                offset = 10 + metaLen;
                if (offset + 4 > file.Length) throw new InvalidDataException("файл обрезан");
            }
            int rawLength = (int)BitConverter.ToUInt32(file, offset);
            offset += 4;

            byte[] raw;
            if (compression == 1) raw = Inflate(file, offset, file.Length - offset, rawLength);
            else
            {
                raw = new byte[file.Length - offset];
                Buffer.BlockCopy(file, offset, raw, 0, raw.Length);
            }

            var r = new ByteReader(raw);
            string[] strings = StringTable.Read(r);
            KeyRecord[] keys = KeyTable.Read(r, strings);
            var resolved = new CharacterFrameSnapshot[keys.Length];
            var resolvedDone = new bool[keys.Length];

            var data = new GhostTrailData();
            int heroCount = (int)r.VarUInt();
            data.heroFrames.Capacity = heroCount;
            long pt = 0, pst = 0;
            string scene = null;
            Color color = Color.white;
            long[] pbox = new long[4];
            var heroCtx = new SpriteCtx();
            ushort inputs = 0;

            for (int i = 0; i < heroCount; i++)
            {
                byte flags = r.U8();
                if ((flags & FSceneChanged) != 0) scene = strings[r.VarUInt()];
                if ((flags & FColorChanged) != 0) color = new Color32(r.U8(), r.U8(), r.U8(), 255);
                if (version >= 2 && (flags & FInputsChanged) != 0) inputs = (ushort)(r.U8() | (r.U8() << 8));

                pt += r.VarLong(); pst += r.VarLong();
                for (int k = 0; k < 4; k++) pbox[k] += r.VarLong();

                var f = new TrailHeroFrame
                {
                    scene = scene,
                    color = color,
                    time = pt / QTime,
                    sceneTime = pst / QTime,
                    min = new Vector2(pbox[0] / QPos, pbox[1] / QPos),
                    max = new Vector2(pbox[2] / QPos, pbox[3] / QPos),
                    inputs = inputs
                };

                if ((flags & FHasSprite) != 0)
                    f.characterFrame = ReadSprite(r, keys, resolved, resolvedDone, heroCtx, resolve);

                data.heroFrames.Add(f);
            }
            int actionCount = (int)r.VarUInt();
            data.actionFrames.Capacity = actionCount;
            pt = 0; pst = 0; scene = null; color = Color.white;
            int entity = 0;
            var entityCtx = new Dictionary<int, SpriteCtx>();

            for (int i = 0; i < actionCount; i++)
            {
                byte flags = r.U8();
                if ((flags & FSceneChanged) != 0) scene = strings[r.VarUInt()];
                if ((flags & FColorChanged) != 0) color = new Color32(r.U8(), r.U8(), r.U8(), 255);

                entity += (int)r.VarLong();
                pt += r.VarLong(); pst += r.VarLong();

                SpriteCtx ctx;
                if (!entityCtx.TryGetValue(entity, out ctx)) { ctx = new SpriteCtx(); entityCtx[entity] = ctx; }

                int count = (int)r.VarUInt();
                var sprites = new List<CharacterFrameSnapshot>(count);
                for (int s = 0; s < count; s++)
                {
                    CharacterFrameSnapshot snap = ReadSprite(r, keys, resolved, resolvedDone, ctx, resolve);
                    if (Writable(snap)) sprites.Add(snap);   // коллекция не найдена - спрайт пропускаем
                }

                data.actionFrames.Add(new ActionEntityFrame
                {
                    entityId = entity,
                    sprites = sprites,
                    timeFromStart = pt / QTime,
                    timeFromScene = pst / QTime,
                    scene = scene,
                    color = color
                });
            }

            return data;
        }

        private static CharacterFrameSnapshot ReadSprite(ByteReader r, KeyRecord[] keys,
            CharacterFrameSnapshot[] resolved, bool[] resolvedDone, SpriteCtx ctx, bool resolve)
        {
            int keyId = (int)r.VarUInt();
            byte flags = r.U8();

            Matrix4x4 m;
            if ((flags & SFullMatrix) != 0)
            {
                m = new Matrix4x4();
                for (int i = 0; i < 16; i++) m[i] = r.F32();
            }
            else
            {
                for (int i = 0; i < 7; i++) ctx.m[i] += r.VarLong();
                m = Matrix4x4.identity;
                m.m00 = ctx.m[0] / QLin; m.m01 = ctx.m[1] / QLin;
                m.m10 = ctx.m[2] / QLin; m.m11 = ctx.m[3] / QLin;
                m.m03 = ctx.m[4] / QPos; m.m13 = ctx.m[5] / QPos; m.m23 = ctx.m[6] / QPos;
            }

            Vector3 spriteScale = Vector3.one;
            if ((flags & SSpriteScale) != 0) spriteScale = new Vector3(r.F32(), r.F32(), r.F32());

            Color32 color = new Color32(255, 255, 255, 255);
            if ((flags & SColor) != 0) color = new Color32(r.U8(), r.U8(), r.U8(), r.U8());

            Vector3[] verts = null; Vector2[] uvs = null; int[] tris = null; Color32[] vcols = null;
            if ((flags & SMesh) != 0)
            {
                int vn = (int)r.VarUInt();
                verts = new Vector3[vn];
                long px = 0, py = 0, pz = 0;
                for (int i = 0; i < vn; i++)
                {
                    px += r.VarLong(); py += r.VarLong(); pz += r.VarLong();
                    verts[i] = new Vector3(px / QPos, py / QPos, pz / QPos);
                }

                int un = (int)r.VarUInt();
                uvs = new Vector2[un];
                long pu = 0, pv = 0;
                for (int i = 0; i < un; i++)
                {
                    pu += r.VarLong(); pv += r.VarLong();
                    uvs[i] = new Vector2(pu / QLin, pv / QLin);
                }

                int tn = (int)r.VarUInt();
                tris = new int[tn];
                long ptri = 0;
                for (int i = 0; i < tn; i++) { ptri += r.VarLong(); tris[i] = (int)ptri; }

                if ((flags & SMeshColors) != 0)
                {
                    vcols = new Color32[vn];
                    for (int i = 0; i < vn; i++) vcols[i] = new Color32(r.U8(), r.U8(), r.U8(), r.U8());
                }
            }

            byte[] blob = null;
            if ((flags & SBlob) != 0)
            {
                int bn = (int)r.VarUInt();
                blob = r.Bytes(bn);
            }
            if (keyId < 0 || keyId >= keys.Length) return default;
            if (!resolvedDone[keyId])
            {
                resolved[keyId] = resolve ? ResolveKey(keys[keyId], blob != null || verts != null) : RawKey(keys[keyId]);
                resolvedDone[keyId] = true;
            }

            CharacterFrameSnapshot snap = resolved[keyId];
            if (resolve && snap.material == null && snap.spriteDef == null) return default;

            snap.localToWorld = m;
            snap.position = new Vector3(m.m03, m.m13, m.m23);
            snap.lossyScale = new Vector3(new Vector2(m.m00, m.m10).magnitude, new Vector2(m.m01, m.m11).magnitude, 1f);
            snap.rotationZ = Mathf.Atan2(m.m10, m.m00) * Mathf.Rad2Deg;
            snap.spriteScale = spriteScale;
            snap.spriteColor = color;
            snap.meshVerts = verts;
            snap.meshUvs = uvs;
            snap.meshTris = tris;
            snap.meshColors = vcols;
            snap.particleBlob = blob;
            return snap;
        }
        private static CharacterFrameSnapshot RawKey(KeyRecord k)
        {
            CharacterFrameSnapshot s = default;
            s.fromFileKey = true;
            s.sourceKind = k.sourceKind;
            s.particleWeight = k.particleWeight;
            s.spriteId = k.spriteId;
            s.texturePage = k.texturePage;
            s.frameIndex = k.frameIndex;
            s.spriteCollectionName = k.collectionName;
            s.spriteCollectionGuid = k.collectionGuid;
            s.spriteDefName = k.defName;
            s.materialName = k.materialName;
            s.textureName = k.textureName;
            s.clipName = k.clipName;
            return s;
        }
        private static CharacterFrameSnapshot ResolveKey(KeyRecord k, bool hasOwnGeometry)
        {
            if (k.sourceKind == ExtraEffectCapture.SourceTk2d)
            {
                var saved = new SavedSprite
                {
                    spriteId = k.spriteId,
                    spriteCollectionName = k.collectionName,
                    spriteCollectionGuid = k.collectionGuid,
                    spriteDefName = k.defName,
                    materialName = k.materialName,
                    textureName = k.textureName,
                    texturePage = k.texturePage,
                    clipName = k.clipName,
                    frameIndex = k.frameIndex,
                    m00 = 1f, m11 = 1f, m22 = 1f, m33 = 1f,
                    spriteScaleX = 1f, spriteScaleY = 1f, spriteScaleZ = 1f,
                    lossyScaleX = 1f, lossyScaleY = 1f, lossyScaleZ = 1f,
                    csR = 1f, csG = 1f, csB = 1f, csA = 1f
                };
                CharacterFrameSnapshot snap = saved.ToSnapshot();
                snap.sourceKind = ExtraEffectCapture.SourceTk2d;
                return snap;
            }
            CharacterFrameSnapshot e = default;
            e.sourceKind = k.sourceKind;
            e.particleWeight = k.particleWeight;
            e.spriteId = -1;
            e.texturePage = -1;
            e.spriteDefName = k.defName;
            e.clipName = k.clipName;
            e.materialName = k.materialName;

            Material source = SpriteResolve.FindMaterialByName(k.materialName);
            Texture tex = source != null ? source.mainTexture : null;
            if (tex == null) tex = SpriteResolve.FindTextureByName(k.textureName);
            if (source == null) source = SpriteResolve.GetFallbackSpriteMaterial();

            e.texture = tex;
            e.textureName = tex != null ? tex.name : k.textureName;
            e.material = source != null ? SpriteResolve.GetDrawMaterial(source, tex) : null;
            return e;
        }

        private sealed class StringTable
        {
            private readonly Dictionary<string, int> ids = new Dictionary<string, int>();
            private readonly List<string> list = new List<string>();
            public int Id(string s)
            {
                if (s == null) return 0;
                int id;
                if (ids.TryGetValue(s, out id)) return id;
                list.Add(s);
                id = list.Count;
                ids[s] = id;
                return id;
            }

            public void Write(ByteWriter w)
            {
                w.VarUInt((uint)list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    byte[] b = Encoding.UTF8.GetBytes(list[i]);
                    w.VarUInt((uint)b.Length);
                    w.Bytes(b, 0, b.Length);
                }
            }

            public static string[] Read(ByteReader r)
            {
                int n = (int)r.VarUInt();
                var arr = new string[n + 1];     // [0] = null
                for (int i = 1; i <= n; i++)
                {
                    int len = (int)r.VarUInt();
                    arr[i] = Encoding.UTF8.GetString(r.Bytes(len));
                }
                return arr;
            }
        }

        private struct KeyRecord
        {
            public int sourceKind, particleWeight, spriteId, texturePage, frameIndex;
            public string collectionName, collectionGuid, defName, materialName, textureName, clipName;
        }

        private sealed class KeyTable
        {
            private readonly StringTable strings;
            private readonly Dictionary<string, int> ids = new Dictionary<string, int>();
            private readonly List<int[]> rows = new List<int[]>();
            private readonly StringBuilder sb = new StringBuilder(128);

            public KeyTable(StringTable strings) { this.strings = strings; }

            public int Id(CharacterFrameSnapshot s)
            {
                int[] row =
                {
                    s.sourceKind,
                    strings.Id(s.spriteCollectionName),
                    strings.Id(s.spriteCollectionGuid),
                    strings.Id(s.spriteDefName),
                    s.spriteId,
                    s.texturePage,
                    strings.Id(s.materialName),
                    strings.Id(s.textureName),
                    strings.Id(s.clipName),
                    s.frameIndex,
                    s.particleWeight
                };

                sb.Length = 0;
                for (int i = 0; i < row.Length; i++) { sb.Append(row[i]); sb.Append(','); }
                string k = sb.ToString();

                int id;
                if (ids.TryGetValue(k, out id)) return id;
                id = rows.Count;
                rows.Add(row);
                ids[k] = id;
                return id;
            }

            public void Write(ByteWriter w)
            {
                w.VarUInt((uint)rows.Count);
                for (int i = 0; i < rows.Count; i++)
                    for (int j = 0; j < rows[i].Length; j++) w.VarLong(rows[i][j]);
            }

            public static KeyRecord[] Read(ByteReader r, string[] strings)
            {
                int n = (int)r.VarUInt();
                var arr = new KeyRecord[n];
                for (int i = 0; i < n; i++)
                {
                    arr[i] = new KeyRecord
                    {
                        sourceKind = (int)r.VarLong(),
                        collectionName = Str(strings, r.VarLong()),
                        collectionGuid = Str(strings, r.VarLong()),
                        defName = Str(strings, r.VarLong()),
                        spriteId = (int)r.VarLong(),
                        texturePage = (int)r.VarLong(),
                        materialName = Str(strings, r.VarLong()),
                        textureName = Str(strings, r.VarLong()),
                        clipName = Str(strings, r.VarLong()),
                        frameIndex = (int)r.VarLong(),
                        particleWeight = (int)r.VarLong()
                    };
                }
                return arr;
            }

            private static string Str(string[] strings, long id)
            {
                return id > 0 && id < strings.Length ? strings[id] : null;
            }
        }

        private static byte[] Deflate(byte[] raw)
        {
            using (var ms = new MemoryStream(raw.Length / 3 + 64))
            {
                using (var ds = new DeflateStream(ms, CompressionMode.Compress, true))
                    ds.Write(raw, 0, raw.Length);
                return ms.ToArray();
            }
        }

        private static byte[] Inflate(byte[] src, int offset, int count, int rawLength)
        {
            var result = new byte[rawLength];
            using (var ms = new MemoryStream(src, offset, count))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < rawLength)
                {
                    int n = ds.Read(result, read, rawLength - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read != rawLength) throw new InvalidDataException("файл повреждён (распаковано " + read + " из " + rawLength + ")");
            }
            return result;
        }

        private sealed class ByteWriter
        {
            public byte[] Buffer;
            public int Length;

            public ByteWriter(int capacity) { Buffer = new byte[Math.Max(64, capacity)]; }

            private void Ensure(int extra)
            {
                if (Length + extra <= Buffer.Length) return;
                int size = Buffer.Length * 2;
                while (size < Length + extra) size *= 2;
                Array.Resize(ref Buffer, size);
            }

            public void U8(byte v) { Ensure(1); Buffer[Length++] = v; }

            public void VarUInt(uint v)
            {
                Ensure(5);
                while (v >= 0x80) { Buffer[Length++] = (byte)(v | 0x80); v >>= 7; }
                Buffer[Length++] = (byte)v;
            }
            public void VarLong(long v)
            {
                ulong z = (ulong)((v << 1) ^ (v >> 63));
                Ensure(10);
                while (z >= 0x80) { Buffer[Length++] = (byte)(z | 0x80); z >>= 7; }
                Buffer[Length++] = (byte)z;
            }

            public void F32(float v)
            {
                Ensure(4);
                byte[] b = BitConverter.GetBytes(v);
                Buffer[Length++] = b[0]; Buffer[Length++] = b[1]; Buffer[Length++] = b[2]; Buffer[Length++] = b[3];
            }

            public void Bytes(byte[] src, int offset, int count)
            {
                Ensure(count);
                System.Buffer.BlockCopy(src, offset, Buffer, Length, count);
                Length += count;
            }

            public byte[] ToArray()
            {
                var r = new byte[Length];
                System.Buffer.BlockCopy(Buffer, 0, r, 0, Length);
                return r;
            }
        }

        private sealed class ByteReader
        {
            private readonly byte[] b;
            private int p;

            public ByteReader(byte[] data) { b = data; }

            private void Need(int n)
            {
                if (p + n > b.Length) throw new InvalidDataException("файл обрезан или повреждён");
            }

            public byte U8() { Need(1); return b[p++]; }

            public uint VarUInt()
            {
                uint v = 0; int shift = 0;
                while (true)
                {
                    byte x = U8();
                    v |= (uint)(x & 0x7F) << shift;
                    if ((x & 0x80) == 0) return v;
                    shift += 7;
                    if (shift > 28) throw new InvalidDataException("битое число");
                }
            }

            public long VarLong()
            {
                ulong z = 0; int shift = 0;
                while (true)
                {
                    byte x = U8();
                    z |= (ulong)(x & 0x7F) << shift;
                    if ((x & 0x80) == 0) break;
                    shift += 7;
                    if (shift > 63) throw new InvalidDataException("битое число");
                }
                return (long)(z >> 1) ^ -(long)(z & 1);
            }

            public float F32() { Need(4); float v = BitConverter.ToSingle(b, p); p += 4; return v; }

            public byte[] Bytes(int n)
            {
                Need(n);
                var r = new byte[n];
                System.Buffer.BlockCopy(b, p, r, 0, n);
                p += n;
                return r;
            }
        }
    }
}
