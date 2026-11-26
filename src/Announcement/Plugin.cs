using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace MaydayAnnouncement
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BasePlugin
    {
        public const string PluginGuid = "com.mayday.announcement";
        public const string PluginName = "Mayday Announcement";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource LogSource;
        internal static ConfigEntry<string> AnnouncementText;

        public override void Load()
        {
            LogSource = Log;
            AnnouncementText = Config.Bind(
                "Announcement",
                "Text",
                "",
                "进入房间时显示的文字公告，留空则不显示。");
            var harmony = new Harmony(PluginGuid);

            foreach (var type in typeof(Plugin).Assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0)
                    continue;

                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    LogSource.LogError("[MaydayAnnouncement] 补丁 " + type.Name + " 挂载失败: " + e);
                }
            }

            LogSource.LogInfo("[MaydayAnnouncement] 已加载；公告文本为空时不会显示");
        }
    }

    [HarmonyPatch]
    internal static class AnnouncementHooks
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.UI.UIRoot), "OnSceneLoaded")]
        private static void SceneLoadedPostfix(UnityEngine.SceneManagement.Scene scene)
        {
            string sceneName;
            try
            {
                sceneName = scene.name;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayAnnouncement] 无法读取场景名: " + e.Message);
                return;
            }

            AnnouncementCard.OnSceneLoaded(sceneName);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.UI.UIRoot), "OnSessionEnded")]
        private static void SessionEndedPostfix()
        {
            AnnouncementCard.OnSessionEnded();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.UI.UIRoot), "Update")]
        private static void UpdatePostfix()
        {
            AnnouncementCard.Tick();
        }
    }
}
