using Satchel.BetterMenus;
using UnityEngine;

namespace GhostMacro
{
    internal static class ConfigScreen
    {
        private static GlobalSettings Settings => GhostMacro.Settings;

        public static Menu Build()
        {
            return new Menu(
                "Ghost Macro",
                new Element[]
                {
                new TextPanel("Recording"),

                new HorizontalOption(
                    "Record Mode",
                    "Toggle or separate keys",
                    new[] { "Toggle", "Separate" },
                    i => Settings.ToggleRecordWithSameKey = i == 0,
                    () => Settings.ToggleRecordWithSameKey ? 0 : 1
                ),

                new HorizontalOption(
                    "Particle Effects",
                    "Light: dash and wings. All: adds spell particles",
                    new[] { "All", "Light Only", "Off" },
                    i => Settings.ParticleEffectsMode = i,
                    () => Mathf.Clamp(Settings.ParticleEffectsMode, 0, 2)
                ),

                new HorizontalOption(
                    "Record Rate",
                    "Trail frames per second (Full = every game frame)",
                    HitboxTrailBehaviour.RecordRateLabels,
                    i => Settings.RecordRateIndex = i,
                    () => Mathf.Clamp(Settings.RecordRateIndex, 0, HitboxTrailBehaviour.RecordRateLabels.Length - 1)
                ),

                new HorizontalOption(
                    "Save Format",
                    "Ghost: compact binary. JSON: old readable format",
                    new[] { "Ghost", "JSON" },
                    i => Settings.SaveFormat = i,
                    () => Mathf.Clamp(Settings.SaveFormat, 0, 1)
                ),

                new HorizontalOption(
                    "Auto Backup",
                    "Backup on menu exit, Benchwarp and savestates",
                    new[] { "ON", "OFF" },
                    i => Settings.AutoSaveBackup = i == 0,
                    () => Settings.AutoSaveBackup ? 0 : 1
                ),

                new HorizontalOption(
                    "Auto Hide",
                    "Hide trail while recording",
                    new[] { "ON", "OFF" },
                    i => Settings.AutoHide = i == 0,
                    () => Settings.AutoHide ? 0 : 1
                ),
                new TextPanel("Display"),

                new HorizontalOption(
                    "Trail Visibility",
                    "Show or hide trail rendering",
                    new[] { "Visible", "Hidden" },
                    i => Settings.TrailVisible = i == 0,
                    () => Settings.TrailVisible ? 0 : 1
                ),

                new HorizontalOption(
                    "Status Indicator",
                    "Corner for the status plate",
                    TrailStatusHud.CornerLabels,
                    i => Settings.StatusCorner = i,
                    () => Mathf.Clamp(Settings.StatusCorner, 0, TrailStatusHud.CornerLabels.Length - 1)
                ),

                new HorizontalOption(
                    "Trail Render Mode",
                    "Draw hitboxes or real sprite frames",
                    new[] { "Hitboxes", "Character Frames" },
                    i => Settings.RenderMode = i == 0 ? TrailRenderMode.Hitboxes : TrailRenderMode.CharacterFrames,
                    () => Settings.RenderMode == TrailRenderMode.Hitboxes ? 0 : 1
                ),

                new HorizontalOption(
                    "Colorize Character Frames",
                    "Tint sprite frames with the trail color",
                    new[] { "ON", "OFF" },
                    i => Settings.ColorizeCharacterFrames = i == 0,
                    () => Settings.ColorizeCharacterFrames ? 0 : 1
                ),

                new HorizontalOption(
                    "Custom Knight Skins",
                    "Draw ghosts with the current skin",
                    new[] { "ON", "OFF" },
                    i => Settings.CustomKnightSkins = i == 0,
                    () => Settings.CustomKnightSkins ? 0 : 1
                ),
                new TextPanel("Hitbox Lines"),

                new HorizontalOption(
                    "Trail Line Smoothing",
                    "Feather the outline edges",
                    new[] { "ON", "OFF" },
                    i => Settings.TrailSmoothing = i == 0,
                    () => Settings.TrailSmoothing ? 0 : 1
                ),

                new HorizontalOption(
                    "Trail Line Thickness",
                    "Thickness of the hitbox outline",
                    new[] { "Very Thin", "Thin", "Normal", "Thick" },
                    i => Settings.TrailThicknessIndex = i,
                    () => Settings.TrailThicknessIndex
                ),
                new TextPanel("Colors"),

                new HorizontalOption(
                    "Active Colors",
                    "How many colors are used",
                    new[] { "1","2","3","4","5","6" },
                    i => Settings.ActiveColors = i + 1,
                    () => Settings.ActiveColors - 1
                ),

                new HorizontalOption(
                    "Start Color",
                    "First color in cycle",
                    new[] { "Yellow","Red","Blue","Green","Cyan","Orange" },
                    i =>
                    {
                        Settings.StartColorIndex = i;
                        Settings.CurrentColorIndex = 0;
                    },
                    () => Settings.StartColorIndex
                ),
                new TextPanel("Playback"),

                new HorizontalOption(
                    "Playback Display",
                    "Keep played frames, or show only the current",
                    new[] { "Trail", "Current Only" },
                    i => Settings.PlaybackDisplay = i == 0 ? PlaybackDisplayMode.Trail : PlaybackDisplayMode.CurrentOnly,
                    () => Settings.PlaybackDisplay == PlaybackDisplayMode.Trail ? 0 : 1
                ),

                new HorizontalOption(
                    "Scene Playback Mode",
                    "Classic or delayed scene activation",
                    new[] { "Classic", "Delayed" },
                    i => {Settings.DelayedSceneActivation = i == 1;
                    var bh = UnityEngine.Object.FindObjectOfType<HitboxTrailBehaviour>();
                    if (bh != null)
                        bh.RebuildSceneCache();},
                    () => Settings.DelayedSceneActivation ? 1 : 0
                ),

                new HorizontalOption(
                    "Input Overlay",
                    "Show the ghost's button presses",
                    InputOverlayHud.ModeLabels,
                    i => Settings.InputOverlayMode = i,
                    () => Mathf.Clamp(Settings.InputOverlayMode, 0, InputOverlayHud.ModeLabels.Length - 1)
                ),

                new HorizontalOption(
                    "Camera Follow Ghost",
                    "Camera follows the ghost during playback",
                    new[] { "ON", "OFF" },
                    i => Settings.CameraFollowGhost = i == 0,
                    () => Settings.CameraFollowGhost ? 0 : 1
                ),

                new HorizontalOption(
                    "Camera Smoothing",
                    "Camera catch-up softness (Off = locked)",
                    CameraFollow.SmoothingLabels,
                    i => Settings.CameraSmoothingIndex = i,
                    () => Mathf.Clamp(Settings.CameraSmoothingIndex, 0, CameraFollow.SmoothingLabels.Length - 1)
                ),
                new TextPanel("Controls"),

                new KeyBind("Toggle Record", Settings.Binds.ToggleRecord),
                new KeyBind("Stop Record", Settings.Binds.StopRecord),
                new KeyBind("Clear Trail", Settings.Binds.Clear),
                new KeyBind("Next Color", Settings.Binds.NextColor),
                new KeyBind("Playback", Settings.Binds.Playback),
                new KeyBind("Save Trail", Settings.Binds.SaveTrail),
                new KeyBind("Open Replay Menu", Settings.Binds.OpenReplayMenu),
                new KeyBind("Seek Back", Settings.Binds.SeekBack),
                new KeyBind("Seek Forward", Settings.Binds.SeekForward),
                }
            );
        }
    }
}
