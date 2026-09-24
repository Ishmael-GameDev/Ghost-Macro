using System.Collections.Generic;
using UnityEngine;

namespace GhostMacro
{
    internal static class SpriteDiagnostics
    {
        public const int MaxLines = 40;          // защита от флуда для редких диагностик
        public const int MaxCaptureLines = 120;  // строки захвата (по одной на смену набора спрайтов)
        public static int CaptureLineCount { get { return captureLines; } }

        private static readonly HashSet<string> seenRoots = new HashSet<string>();
        private static readonly Dictionary<GameObject, int> lastSetHashes = new Dictionary<GameObject, int>();
        private static int captureLines;
        private static readonly HashSet<string> seenMaterials = new HashSet<string>();
        private static readonly HashSet<string> seenMeshes = new HashSet<string>();
        private static readonly HashSet<string> seenResolves = new HashSet<string>();
        private static int lines;
        public static int LineCount { get { return lines; } }

        public static void Reset()
        {
            seenRoots.Clear();
            lastSetHashes.Clear();
            captureLines = 0;
            seenMaterials.Clear();
            seenMeshes.Clear();
            seenResolves.Clear();
            lines = 0;
        }

        private static bool CanWrite(string key, HashSet<string> set)
        {
            if (lines >= MaxLines) return false;
            if (!set.Add(key)) return false;
            lines++;
            return true;
        }
        public static void ReportRoot(GameObject root, List<CharacterFrameSnapshot> sprites)
        {
            if (!ActionEntityCapture.VerboseDiagnostics) return; // [ФИКС 8] без спама в модлог
            if (root == null || sprites == null || sprites.Count == 0) return;
            if (captureLines >= MaxCaptureLines) return;

            int hash = sprites.Count;
            for (int i = 0; i < sprites.Count; i++)
            {
                hash = hash * 31 + (sprites[i].spriteDefName != null ? sprites[i].spriteDefName.GetHashCode() : 0);
                hash = hash * 31 + sprites[i].texturePage;
                hash = hash * 31 + (sprites[i].HasMesh ? 7 : 3);
            }

            int previous;
            if (lastSetHashes.TryGetValue(root, out previous) && previous == hash) return;
            lastSetHashes[root] = hash;

            string path = GetPath(root.transform);

            string list = "";
            bool anyMismatch = false;
            for (int i = 0; i < sprites.Count; i++)
            {
                if (i > 0) list += " | ";
                string where = PathOfSprite(root, sprites[i]);
                if (!string.IsNullOrEmpty(where)) list += where + ": ";
                list += sprites[i].spriteCollectionName + "/" + sprites[i].spriteDefName;
                if (sprites[i].texturePage >= 0) list += "#p" + sprites[i].texturePage;
                if (!string.IsNullOrEmpty(sprites[i].textureName)) list += "@" + sprites[i].textureName;
                if (sprites[i].HasMesh) list += "(mesh)";

                Texture correctAtlas = sprites[i].texture;
                Texture materialAtlas = sprites[i].material != null ? sprites[i].material.mainTexture : null;
                bool mismatch = correctAtlas != null && materialAtlas != null &&
                                 !ReferenceEquals(correctAtlas, materialAtlas) &&
                                 correctAtlas.name != materialAtlas.name;
                if (mismatch)
                {
                    anyMismatch = true;
                    list += $" [!!MATERIAL УКАЗЫВАЕТ НА '{materialAtlas.name}' (id={materialAtlas.GetInstanceID()}), " +
                            $"А НАДО '{correctAtlas.name}' (id={correctAtlas.GetInstanceID()})]";
                }
            }

            if (captureLines >= MaxCaptureLines) return;
            captureLines++;

            string tag = anyMismatch ? "[GhostMacro][DiagMISMATCH]" : "[GhostMacro][Diag]";
            Modding.Logger.Log($"{tag} кадр '{path}': [{list}]");
        }
        private static string PathOfSprite(GameObject root, CharacterFrameSnapshot snap)
        {
            tk2dBaseSprite[] sprites = root.GetComponentsInChildren<tk2dBaseSprite>(true);
            for (int i = 0; i < sprites.Length; i++)
            {
                tk2dBaseSprite sp = sprites[i];
                if (sp == null) continue;
                if (!ReferenceEquals(sp.Collection, snap.collection)) continue;
                if (sp.spriteId != snap.spriteId) continue;
                return sp.gameObject.name;
            }
            return null;
        }
        public static void ReportPhantom(tk2dBaseSprite sprite)
        {
            if (sprite == null) return;
            if (lines >= MaxLines) return;

            string path = GetPath(sprite.transform);
            if (!seenRoots.Add(path)) return;
            lines++;

            Modding.Logger.Log(
                $"[GhostMacro][Diag] пропущен невидимый спрайт в '{path}': рендерер включён, " +
                "но камера его не рисует (заготовка/остаток эффекта) - в запись не берём.");
        }
        public static void ReportMaterial(
            tk2dBaseSprite sprite,
            tk2dSpriteDefinition def,
            Material rendererMaterial,
            Material spriteMaterial,
            Texture atlas,
            Material chosen)
        {
            if (def == null || rendererMaterial == null || chosen == null) return;
            if (rendererMaterial == chosen) return;   // менять ничего не пришлось

            if (lines >= MaxLines) return;
            string key = "mat|" + def.name + "|" + rendererMaterial.name;
            if (!CanWrite(key, seenMaterials)) return;

            Modding.Logger.Log(
                $"[GhostMacro][Diag] спрайт '{def.name}': на рендерере '{rendererMaterial.name}' " +
                $"(tex='{(rendererMaterial.mainTexture != null ? rendererMaterial.mainTexture.name : "null")}'), " +
                $"а спрайт живёт в '{(atlas != null ? atlas.name : "?")}' " +
                $"(mat='{(spriteMaterial != null ? spriteMaterial.name : "?")}') - рисуем спрайтовым.");
        }
        public static void ReportMesh(tk2dBaseSprite sprite, tk2dSpriteDefinition def, bool differs, bool hasVertexColors)
        {
            if (!differs || def == null) return;

            if (lines >= MaxLines) return;
            string key = "mesh|" + def.name;
            if (!CanWrite(key, seenMeshes)) return;

            Modding.Logger.Log(
                $"[GhostMacro][Diag] спрайт '{def.name}' (flipped={def.flipped}) имеет меш, " +
                "отличный от определения - кадр записан точной геометрией" +
                (hasVertexColors ? " (с вершинными цветами)" : ""));
        }
        public static void ReportResolve(tk2dSpriteDefinition def, int pageInFile, string textureNameInFile, Texture atlas)
        {
            if (def == null) return;
            if (lines >= MaxLines) return;

            string applied = atlas != null ? atlas.name : "нет";
            string key = "res|" + def.name + "|" + pageInFile + "|" + applied;
            if (!seenResolves.Add(key)) return;
            lines++;

            bool mismatch = !string.IsNullOrEmpty(textureNameInFile) && applied != textureNameInFile;

            Modding.Logger.Log(
                $"[GhostMacro][Diag] резолв '{def.name}': в записи page={pageInFile} tex='{textureNameInFile}', " +
                $"рисуем tex='{applied}' (def.materialId={def.materialId})" +
                (mismatch ? " - СТРАНИЦА АТЛАСА НЕ СОВПАЛА, спрайт будет чужим" : " - совпало"));
        }

        public static string GetPath(Transform t)
        {
            if (t == null) return string.Empty;
            string path = t.name;
            Transform p = t.parent;
            int guard = 0;
            while (p != null && guard++ < 12)
            {
                path = p.name + "/" + path;
                p = p.parent;
            }
            return path;
        }
    }
}
