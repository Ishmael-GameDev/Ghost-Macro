using System.Collections.Generic;
using UnityEngine;

namespace GhostMacro
{
    [System.Serializable]
    public class SavedTrail
    {
        public List<SavedFrame> frames = new List<SavedFrame>();
        public List<SavedActionFrame> actionFrames = new List<SavedActionFrame>();
    }

    [System.Serializable]
    public class SavedFrame
    {
        public string scene;
        public float time;
        public float sceneTime;

        public float ax, ay;
        public float bx, by;
        public float cx, cy;
        public float dx, dy;

        public float r, g, b;
        public SavedSprite characterFrame;
        public int inputs;
        public bool hasCharacterFrame;
        public string clipName;
        public string libraryName;
        public int frameIndex;
        public int spriteCollectionId;
        public string spriteCollectionName;
        public int spriteId;
        public float posX, posY, posZ;
        public float scaleX, scaleY, scaleZ;
        public float rotZ;
        public bool hasSpriteColor;
        public float csR, csG, csB, csA;

        public CharacterFrameSnapshot ToCharacterFrame()
        {
            if (characterFrame != null && !string.IsNullOrEmpty(characterFrame.spriteDefName))
                return characterFrame.ToSnapshot();

            if (!hasCharacterFrame) return default;

            return SavedSprite.FromLegacy(
                spriteId,
                spriteCollectionName,
                frameIndex,
                clipName,
                libraryName,
                new Vector3(posX, posY, posZ),
                new Vector3(scaleX, scaleY, scaleZ),
                rotZ,
                hasSpriteColor ? new Color(csR, csG, csB, csA) : Color.white,
                false,
                default
            ).ToSnapshot();
        }
    }

    [System.Serializable]
    public class SavedActionFrame
    {
        public int entityId;
        public float time;
        public float sceneTime;
        public string scene;
        public float r, g, b;
        public List<SavedSprite> sprites = new List<SavedSprite>();
        public string clipName;
        public string libraryName;
        public int frameIndex;
        public int spriteCollectionId;
        public string spriteCollectionName;
        public int spriteId;
        public float posX, posY, posZ;
        public float scaleX, scaleY, scaleZ;
        public float rotZ;
        public bool hasSpriteColor;
        public float csR, csG, csB, csA;

        public ActionEntityFrame ToActionEntityFrame()
        {
            ActionEntityFrame frame = new ActionEntityFrame
            {
                entityId = entityId,
                sprites = new List<CharacterFrameSnapshot>(),
                timeFromStart = time,
                timeFromScene = sceneTime,
                scene = scene,
                color = new Color(r, g, b)
            };

            if (sprites != null && sprites.Count > 0)
            {
                for (int i = 0; i < sprites.Count; i++)
                {
                    CharacterFrameSnapshot snap = sprites[i].ToSnapshot();
                    if (snap.IsDrawable) frame.sprites.Add(snap);
                }
                return frame;
            }
            CharacterFrameSnapshot legacy = SavedSprite.FromLegacy(
                spriteId,
                spriteCollectionName,
                frameIndex,
                clipName,
                libraryName,
                new Vector3(posX, posY, posZ),
                new Vector3(scaleX, scaleY, scaleZ),
                rotZ,
                hasSpriteColor ? new Color(csR, csG, csB, csA) : Color.white,
                false,
                default
            ).ToSnapshot();

            if (legacy.IsDrawable) frame.sprites.Add(legacy);
            return frame;
        }
    }
}
