using UnityEngine;
using System.Collections.Generic;

namespace GhostMacro
{
    internal static class CharacterFrameDrawing
    {
        public static bool AlwaysUseSafeShader = false;

        private static Material currentMaterial;
        private static bool matrixPushed;
        private static bool isDrawing;

        public static int SkippedFrames { get; private set; }

        public static void Begin()
        {
            isDrawing = true;
            currentMaterial = null;
            matrixPushed = false;
        }

        public static void End()
        {
            if (currentMaterial != null) GL.End();
            if (matrixPushed) GL.PopMatrix();
            isDrawing = false;
            currentMaterial = null;
            matrixPushed = false;
        }

        public static bool CanDraw(CharacterFrameSnapshot snap)
        {
            if (!snap.IsDrawable) return false;
            if (snap.material != null) return true;
            return SpriteResolve.PickMaterial(snap.collection, snap.spriteDef) != null;
        }

        public static void DrawFrame(CharacterFrameSnapshot snap, Color tint)
        {
            if (!isDrawing) return;

            if (!snap.IsDrawable)
            {
                ReportedSkip("нет валидной геометрии (ни меша, ни определения спрайта)");
                return;
            }

            Material mat = snap.material;
            if (mat == null)
            {
                Material fallback = SpriteResolve.PickMaterial(snap.collection, snap.spriteDef);
                if (fallback == null)
                {
                    ReportedSkip("не найден материал (" + snap.Describe() + ")");
                    return;
                }
                mat = SpriteResolve.GetDrawMaterial(fallback, snap.texture);
            }
            if (mat.mainTexture == null || snap.texture == null)
            {
                Texture current = SpriteResolve.CurrentTextureFor(snap);
                if (current != null)
                {
                    mat = SpriteResolve.GetDrawMaterial(mat, current);
                    snap.texture = current;
                }
            }
            if (snap.sourceKind == ExtraEffectCapture.SourceParticles && (snap.HasParticleCloud || snap.HasMesh))
            {
                DrawParticleMesh(snap, mat, tint);
                return;
            }

            if (mat != currentMaterial)
            {
                if (currentMaterial != null) GL.End();

                BindMaterial(mat, snap);

                if (!matrixPushed) { GL.PushMatrix(); matrixPushed = true; }
                GL.Begin(GL.TRIANGLES);
                currentMaterial = mat;
            }

            if (snap.HasMesh) DrawMesh(snap, tint);
            else DrawSpriteDef(snap, tint);
        }

        private sealed class BakedMesh
        {
            public Mesh mesh;
            public Color tint;
            public Color32[] baseColors;
        }
        private static readonly Dictionary<object, BakedMesh> particleMeshes = new Dictionary<object, BakedMesh>();
        private const int ParticleMeshCacheLimit = 3000;

        private static void DrawParticleMesh(CharacterFrameSnapshot snap, Material mat, Color tint)
        {
            if (currentMaterial != null) { GL.End(); currentMaterial = null; }
            if (!matrixPushed) { GL.PushMatrix(); matrixPushed = true; }

            Mesh mesh = GetParticleMesh(snap, tint);
            if (mesh == null) return;

            BindMaterial(mat, snap);
            Graphics.DrawMeshNow(mesh, snap.localToWorld);
        }

        private static Mesh GetParticleMesh(CharacterFrameSnapshot snap, Color tint)
        {
            object key = snap.particleBlob != null ? (object)snap.particleBlob : snap.meshVerts;
            if (key == null) return null;

            BakedMesh baked;
            if (particleMeshes.TryGetValue(key, out baked) && baked != null && baked.mesh != null)
            {
                if (SameTint(baked.tint, tint)) return baked.mesh;
                baked.mesh.colors32 = TintColors(baked.baseColors, tint);
                baked.tint = tint;
                return baked.mesh;
            }

            if (particleMeshes.Count >= ParticleMeshCacheLimit) ClearParticleMeshes();

            Vector3[] verts; Vector2[] uvs; Color32[] colors; int[] tris;
            if (snap.particleBlob != null)
            {
                if (!ParticleCloud.Decode(snap.particleBlob, out verts, out uvs, out colors, out tris)) return null;
            }
            else
            {
                verts = snap.meshVerts; uvs = snap.meshUvs; tris = snap.meshTris;
                colors = snap.meshColors;
                if (colors == null || colors.Length < verts.Length)
                {
                    colors = new Color32[verts.Length];
                    Color32 c = snap.spriteColor;
                    for (int i = 0; i < colors.Length; i++) colors[i] = c;
                }
            }

            Mesh mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "GhostMacro Particles" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors32 = TintColors(colors, tint);
            mesh.triangles = tris;
            mesh.RecalculateBounds();

            particleMeshes[key] = new BakedMesh { mesh = mesh, tint = tint, baseColors = colors };
            return mesh;
        }

        private static Color32[] TintColors(Color32[] src, Color tint)
        {
            Color32[] outColors = new Color32[src.Length];
            for (int i = 0; i < src.Length; i++)
                outColors[i] = (Color)src[i] * tint;
            return outColors;
        }

        private static bool SameTint(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f &&
                   Mathf.Abs(a.b - b.b) < 0.01f && Mathf.Abs(a.a - b.a) < 0.01f;
        }

        private static void ClearParticleMeshes()
        {
            foreach (BakedMesh b in particleMeshes.Values)
                if (b != null && b.mesh != null) Object.Destroy(b.mesh);
            particleMeshes.Clear();
        }

        private static readonly Dictionary<Material, bool> safeDecision = new Dictionary<Material, bool>();
        private static readonly Dictionary<Material, bool> additiveDecision = new Dictionary<Material, bool>();
        private static readonly Dictionary<Texture, Material> safeMaterials = new Dictionary<Texture, Material>();
        private static readonly Dictionary<Texture, Material> safeAdditiveMaterials = new Dictionary<Texture, Material>();
        private static Shader safeShader;
        private static bool safeShaderSearched;
        private static Shader safeAdditiveShader;
        private static bool safeAdditiveSearched;
        private static bool IsAdditiveLike(Material m)
        {
            if (m == null) return false;

            if (m.HasProperty("_DstBlend"))
            {
                try
                {
                    int dst = (int)m.GetFloat("_DstBlend");
                    if (dst == (int)UnityEngine.Rendering.BlendMode.One) return true;
                }
                catch { }
            }

            string n = m.shader != null ? m.shader.name.ToLowerInvariant() : string.Empty;
            return n.Contains("screen") || n.Contains("additive") || n.Contains("addvertex") ||
                   n.Contains("/add") || n.Contains("dodge") || n.Contains("lighten");
        }

        private static void BindMaterial(Material mat, CharacterFrameSnapshot snap)
        {
            Texture tex = snap.texture != null ? snap.texture : mat.mainTexture;

            bool useSafe = AlwaysUseSafeShader;
            if (!useSafe)
            {
                bool known;
                if (safeDecision.TryGetValue(mat, out known) && known)
                    useSafe = true;
            }

            if (!useSafe)
            {
                int passCount = mat.shader != null ? mat.passCount : 0;
                bool ok = mat.SetPass(0);
                bool bad = !ok || passCount != 1;

                ReportMaterialOnce(mat, snap, tex, ok, passCount, bad);

                if (!bad)
                {
                    safeDecision[mat] = false;
                    return;
                }

                safeDecision[mat] = true;
                useSafe = true;
            }

            bool additive;
            if (!additiveDecision.TryGetValue(mat, out additive))
            {
                additive = IsAdditiveLike(mat);
                additiveDecision[mat] = additive;
            }

            Material safe = additive ? GetSafeAdditiveMaterial(tex) : null;
            if (safe == null) safe = GetSafeMaterial(tex);
            if (safe == null || !safe.SetPass(0))
            {
                mat.SetPass(0);
            }
        }

        private static Material GetSafeMaterial(Texture tex)
        {
            if (!safeShaderSearched)
            {
                safeShaderSearched = true;
                safeShader = Shader.Find("Sprites/Default");
                if (safeShader == null) safeShader = Shader.Find("tk2d/BlendVertexColor");
                if (safeShader == null) safeShader = Shader.Find("Unlit/Transparent");

                Modding.Logger.Log("[GhostMacro][SafeShader] безопасный шейдер: " +
                                   (safeShader != null ? safeShader.name : "НЕ НАЙДЕН"));
            }

            if (safeShader == null || tex == null) return null;

            Material m;
            if (safeMaterials.TryGetValue(tex, out m) && m != null) return m;

            m = new Material(safeShader)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "GhostMacro Safe (" + tex.name + ")",
                mainTexture = tex
            };
            safeMaterials[tex] = m;
            return m;
        }

        private static Material GetSafeAdditiveMaterial(Texture tex)
        {
            if (!safeAdditiveSearched)
            {
                safeAdditiveSearched = true;

                string[] candidates =
                {
                    "tk2d/AddVertexColor",
                    "Mobile/Particles/Additive",
                    "Legacy Shaders/Particles/Additive",
                    "Particles/Additive"
                };
                for (int i = 0; i < candidates.Length && safeAdditiveShader == null; i++)
                    safeAdditiveShader = Shader.Find(candidates[i]);
                if (safeAdditiveShader == null)
                {
                    Shader[] all = Resources.FindObjectsOfTypeAll<Shader>();
                    for (int i = 0; i < all.Length && safeAdditiveShader == null; i++)
                    {
                        Shader s = all[i];
                        if (s == null || !s.isSupported) continue;
                        string n = s.name.ToLowerInvariant();
                        if (n.Contains("particles/additive") || n.Contains("addvertexcolor"))
                            safeAdditiveShader = s;
                    }
                }

                Modding.Logger.Log("[GhostMacro][SafeShader] аддитивный шейдер: " +
                                   (safeAdditiveShader != null ? safeAdditiveShader.name
                                                               : "НЕ НАЙДЕН (эффекты рисуются обычным)"));
            }

            if (safeAdditiveShader == null || tex == null) return null;

            Material m;
            if (safeAdditiveMaterials.TryGetValue(tex, out m) && m != null) return m;

            m = new Material(safeAdditiveShader)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "GhostMacro SafeAdd (" + tex.name + ")",
                mainTexture = tex
            };
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));

            safeAdditiveMaterials[tex] = m;
            return m;
        }
        public static void ClearMaterialCache()
        {
            safeDecision.Clear();
            additiveDecision.Clear();
            safeMaterials.Clear();
            safeAdditiveMaterials.Clear();
            ClearParticleMeshes();
        }

        private static readonly HashSet<Material> reportedMaterials = new HashSet<Material>();

        private static void ReportMaterialOnce(Material mat, CharacterFrameSnapshot snap, Texture tex,
            bool setPassOk, int passCount, bool usingSafe)
        {
            if (!reportedMaterials.Add(mat)) return;

            string shaderName = mat.shader != null ? mat.shader.name : "null";
            string texInfo = tex != null
                ? $"'{tex.name}' id={tex.GetInstanceID()} {tex.width}x{tex.height}"
                : "null";
            Texture matTex = mat.mainTexture;
            string matTexInfo = matTex != null
                ? $"'{matTex.name}' id={matTex.GetInstanceID()} {matTex.width}x{matTex.height}"
                : "null";

            Modding.Logger.Log(
                $"[GhostMacro][DrawMat] col='{snap.spriteCollectionName}' def='{snap.spriteDefName}' " +
                $"mat='{mat.name}' shader='{shaderName}' passCount={passCount} SetPass={setPassOk} " +
                $"tex(снапшот)={texInfo} tex(материал)={matTexInfo} -> " +
                (usingSafe ? (IsAdditiveLike(mat) ? "РИСУЕМ БЕЗОПАСНЫМ АДДИТИВНЫМ" : "РИСУЕМ БЕЗОПАСНЫМ ШЕЙДЕРОМ")
                           : "рисуем исходным"));
        }

        private static void DrawMesh(CharacterFrameSnapshot snap, Color tint)
        {
            Vector3[] verts = snap.meshVerts;
            Vector2[] uvs = snap.meshUvs;
            int[] tris = snap.meshTris;
            Color32[] colors = snap.meshColors;
            Matrix4x4 m = snap.localToWorld;

            Color baseColor = snap.spriteColor;

            if (colors != null && colors.Length >= verts.Length)
            {
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    Emit(m, verts, uvs, colors, tris[i], tint, true);
                    Emit(m, verts, uvs, colors, tris[i + 1], tint, true);
                    Emit(m, verts, uvs, colors, tris[i + 2], tint, true);
                }
                return;
            }

            GL.Color(tint * baseColor);
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Emit(m, verts, uvs, colors, tris[i], tint, false);
                Emit(m, verts, uvs, colors, tris[i + 1], tint, false);
                Emit(m, verts, uvs, colors, tris[i + 2], tint, false);
            }
        }

        private static void DrawSpriteDef(CharacterFrameSnapshot snap, Color tint)
        {
            tk2dSpriteDefinition def = snap.spriteDef;
            Vector3 spriteScale = (snap.spriteScale == Vector3.zero) ? Vector3.one : snap.spriteScale;
            int[] indices = def.indices;
            Vector3[] positions = def.positions;
            Vector2[] uvs = def.uvs;
            Matrix4x4 m = snap.localToWorld;

            GL.Color(tint * snap.spriteColor);

            for (int idx = 0; idx + 2 < indices.Length; idx += 3)
            {
                EmitScaled(m, positions, uvs, indices[idx], spriteScale);
                EmitScaled(m, positions, uvs, indices[idx + 1], spriteScale);
                EmitScaled(m, positions, uvs, indices[idx + 2], spriteScale);
            }
        }

        private static void Emit(Matrix4x4 m, Vector3[] verts, Vector2[] uvs, Color32[] colors,
            int index, Color tint, bool useVertexColor)
        {
            if (index < 0 || index >= verts.Length) return;

            if (useVertexColor && colors != null && index < colors.Length)
                GL.Color(tint * (Color)colors[index]);

            Vector3 world = m.MultiplyPoint3x4(verts[index]);
            if (uvs != null && index < uvs.Length) GL.TexCoord2(uvs[index].x, uvs[index].y);
            GL.Vertex(world);
        }

        private static void EmitScaled(Matrix4x4 m, Vector3[] positions, Vector2[] uvs, int i, Vector3 spriteScale)
        {
            Vector3 local = Vector3.Scale(positions[i], spriteScale);
            Vector3 world = m.MultiplyPoint3x4(local);
            if (uvs != null && i < uvs.Length) GL.TexCoord2(uvs[i].x, uvs[i].y);
            GL.Vertex(world);
        }

        private static readonly HashSet<string> reportedSkips = new HashSet<string>();

        private static void ReportedSkip(string reason)
        {
            SkippedFrames++;
            if (reportedSkips.Add(reason))
                Modding.Logger.Log($"[GhostMacro][Draw] Кадр пропущен: {reason}");
        }
        public static void RegisterCollection(tk2dSpriteCollectionData collection) { }
        public static void RegisterAnimatorSource(tk2dSpriteAnimator animator) { }
    }
}
