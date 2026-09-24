using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GhostMacro
{
    internal static class AutosaveWatcher
    {
        private static Action<string> onTrigger;
        private static bool sceneHooked;
        private static bool benchwarpHooked;
        private static bool pollersSearched;
        private static float nextHookAttempt;
        private static int hookAttempts;

        private sealed class FlagPoller
        {
            public string source;
            public Func<bool> read;
            public bool last;
        }

        private static readonly List<FlagPoller> pollers = new List<FlagPoller>();

        public static void Init(Action<string> trigger)
        {
            onTrigger = trigger;

            if (!sceneHooked)
            {
                UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnSceneChanged;
                sceneHooked = true;
            }

            TryHookMods();
        }

        public static void Shutdown()
        {
            if (sceneHooked)
            {
                UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnSceneChanged;
                sceneHooked = false;
            }
            onTrigger = null;
        }
        public static void Poll()
        {
            if ((!benchwarpHooked || !pollersSearched) && hookAttempts < 10 && Time.unscaledTime >= nextHookAttempt)
            {
                nextHookAttempt = Time.unscaledTime + 2f;
                hookAttempts++;
                TryHookMods();
            }

            for (int i = 0; i < pollers.Count; i++)
            {
                FlagPoller p = pollers[i];
                bool value;
                try { value = p.read(); } catch { continue; }

                if (value && !p.last) Fire(p.source);   // передний фронт: загрузка началась
                p.last = value;
            }
        }

        private static void Fire(string reason)
        {
            try { onTrigger?.Invoke(reason); }
            catch (Exception e) { Modding.Logger.LogError("[GhostMacro][AutoSave] " + e.Message); }
        }

        private static void OnSceneChanged(UnityEngine.SceneManagement.Scene from, UnityEngine.SceneManagement.Scene to)
        {
            if (to.name == "Quit_To_Menu" || to.name == "Menu_Title")
                Fire("exit to menu");
        }

        private static void TryHookMods()
        {
            Assembly[] assemblies;
            try { assemblies = AppDomain.CurrentDomain.GetAssemblies(); }
            catch { return; }

            bool foundAnyPollerSource = false;

            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly asm = assemblies[i];
                string name;
                try { name = asm.GetName().Name; } catch { continue; }

                if (!benchwarpHooked && name == "Benchwarp")
                    HookBenchwarp(asm);

                if (!pollersSearched)
                {
                    if (name == "DebugMod")
                    {
                        AddStaticBoolPoller(asm, "DebugMod.SaveState", "loadingSavestate", "DebugMod savestate");
                        foundAnyPollerSource = true;
                    }
                    else if (name.IndexOf("QuickSaveState", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        AddLoadingFlagPollers(asm, "QuickSaveStates savestate");
                        foundAnyPollerSource = true;
                    }
                }
            }

            if (foundAnyPollerSource) pollersSearched = true;
        }

        private static void HookBenchwarp(Assembly asm)
        {
            try
            {
                Type events = asm.GetType("Benchwarp.Events");
                if (events == null) return;

                EventInfo bench = events.GetEvent("OnBenchwarp", BindingFlags.Public | BindingFlags.Static);
                if (bench != null)
                {
                    Action h = () => Fire("Benchwarp bench warp");
                    bench.AddEventHandler(null, h);
                }

                EventInfo door = events.GetEvent("OnDoorwarp", BindingFlags.Public | BindingFlags.Static);
                if (door != null)
                {
                    Action<string, string> h = (scene, gate) => Fire("Benchwarp door warp");
                    door.AddEventHandler(null, h);
                }

                benchwarpHooked = true;
                Modding.Logger.Log("[GhostMacro][AutoSave] подключено: Benchwarp");
            }
            catch (Exception e)
            {
                Modding.Logger.LogError("[GhostMacro][AutoSave] Benchwarp: " + e.Message);
                benchwarpHooked = true; // не пытаемся бесконечно
            }
        }

        private static void AddStaticBoolPoller(Assembly asm, string typeName, string member, string source)
        {
            try
            {
                Type t = asm.GetType(typeName);
                if (t == null) return;
                Func<bool> read = MakeStaticBoolReader(t, member);
                if (read == null) return;

                pollers.Add(new FlagPoller { source = source, read = read });
                Modding.Logger.Log($"[GhostMacro][AutoSave] подключено: {source} ({typeName}.{member})");
            }
            catch (Exception e)
            {
                Modding.Logger.LogError($"[GhostMacro][AutoSave] {source}: {e.Message}");
            }
        }
        private static void AddLoadingFlagPollers(Assembly asm, string source)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            catch { return; }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            for (int i = 0; i < types.Length; i++)
            {
                Type t = types[i];
                if (t == null) continue;

                foreach (PropertyInfo p in t.GetProperties(flags))
                {
                    if (p.PropertyType != typeof(bool) || !p.CanRead) continue;
                    if (p.Name.IndexOf("load", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    PropertyInfo prop = p;
                    pollers.Add(new FlagPoller { source = source, read = () => (bool)prop.GetValue(null, null) });
                    Modding.Logger.Log($"[GhostMacro][AutoSave] подключено: {source} ({t.FullName}.{p.Name})");
                }

                foreach (FieldInfo f in t.GetFields(flags))
                {
                    if (f.FieldType != typeof(bool)) continue;
                    if (f.Name.IndexOf("load", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (f.Name.Contains("<")) continue; // backing-поля авто-свойств уже учтены выше
                    FieldInfo field = f;
                    pollers.Add(new FlagPoller { source = source, read = () => (bool)field.GetValue(null) });
                    Modding.Logger.Log($"[GhostMacro][AutoSave] подключено: {source} ({t.FullName}.{f.Name})");
                }
            }
        }

        private static Func<bool> MakeStaticBoolReader(Type t, string member)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            PropertyInfo p = t.GetProperty(member, flags);
            if (p != null && p.PropertyType == typeof(bool)) return () => (bool)p.GetValue(null, null);

            FieldInfo f = t.GetField(member, flags);
            if (f != null && f.FieldType == typeof(bool)) return () => (bool)f.GetValue(null);

            return null;
        }
    }
}
