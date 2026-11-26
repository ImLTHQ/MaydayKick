using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace MaydayUnlock
{
    /// <summary>
    /// 把 Mayday Airways 的联机人数上限从 4 提升到配置值。
    ///
    /// 游戏用 Mirror 做网络层，三处独立的 4 挡路：
    ///   1. MaydayNetworkManager.ApplyDefaults 里 NetworkManager.maxConnections = 4
    ///   2. SteamLobbyService.Create 里 CreateLobby(type, 4) 的大厅席位数
    ///   3. MaydayAuthenticator.IsFull 里 maxConnections ?? 4 的兜底常数
    ///
    /// 本插件在 Mirror 层统一改写 maxConnections，不改任何游戏文件。
    /// </summary>
    [BepInPlugin(Guid, Name, BuildInfo.Version)]
    public sealed class Plugin : BasePlugin
    {
        public const string Guid = "local.mayday.unlock";
        public const string Name = "Mayday Unlock";

        /// <summary>版本号来自当前项目目录的 VERSION，编译时生成到 BuildInfo。</summary>
        public const string Version = BuildInfo.Version;

        /// <summary>引擎给 Mirror 预留的槽位上限，见 AllocateSlot 的 cmp ebx,10h。</summary>
        public const int SlotLimit = 16;

        internal static ManualLogSource LogSource = null;
        internal static ConfigEntry<int> PlayerLimit = null;

        public override void Load()
        {
            LogSource = base.Log;

            PlayerLimit = Config.Bind(
                "General",
                "PlayerLimit",
                8,
                new ConfigDescription(
                    "联机人数上限（原版固定 4，引擎最多支持 " + SlotLimit + "）",
                    new AcceptableValueRange<int>(1, SlotLimit)));

            LogSource.LogInfo("[MaydayUnlock] 已加载，人数上限 = " + PlayerLimit.Value + "（原版 4，引擎上限 " + SlotLimit + "）");

            // BepInEx 6 (IL2CPP) 不会自动扫描 Harmony 补丁，必须显式挂载。
            //
            // 用 PatchAll 的话，任何一个补丁目标方法不存在（游戏改版、方法名猜错）
            // 都会抛 Undefined target method，整批补丁全部失效 —— 包括改写人数上限
            // 那几个核心补丁，插件看上去"加载了"但什么都不做。
            // 所以逐个挂载，单个失败只记日志。
            var harmony = new HarmonyLib.Harmony(Guid);
            PatchEach(harmony, typeof(Plugin).Assembly);

            Apply();

            PlayerLimit.SettingChanged += delegate (object sender, EventArgs e)
            {
                LogSource.LogInfo("[MaydayUnlock] 人数上限改为 " + PlayerLimit.Value);
                Apply();
            };
        }

        /// <summary>
        /// 逐个挂载补丁类：某一个类挂不上时只记错误，不影响其它类。
        /// Harmony 的 PatchAll 是「全有或全无」，在 IL2CPP + 会改版的游戏上太脆。
        /// </summary>
        private static void PatchEach(HarmonyLib.Harmony harmony, Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), true).Length == 0)
                {
                    continue;
                }

                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    LogSource.LogError("[MaydayUnlock] 补丁 " + type.Name + " 挂载失败: " + e.Message);
                }
            }
        }

        /// <summary>把当前配置值写进 Mirror 的 maxConnections。</summary>
        internal static void Apply()
        {
            try
            {
                var manager = Mirror.NetworkManager.singleton;
                if (manager == null)
                {
                    // 主菜单阶段还没有实例，由 ApplyDefaults 的 postfix 兜底。
                    return;
                }

                int target = PlayerLimit.Value;
                int before = manager.maxConnections;
                if (before != target)
                {
                    manager.maxConnections = target;
                    LogSource.LogInfo("[MaydayUnlock] maxConnections " + before + " -> " + target);
                }
            }
            catch (Exception e)
            {
                LogSource.LogError("[MaydayUnlock] 应用失败: " + e);
            }
        }
    }

    /// <summary>
    /// 在游戏把 maxConnections 重置回 4 之后立刻改回配置值。
    /// ApplyDefaults 在 Awake 里跑，此时 NetworkManager.singleton 还没赋值，
    /// 所以必须直接改实例自身的字段，不能走 singleton。
    /// </summary>
    [HarmonyPatch]
    internal static class ApplyDefaultsPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.Net.MaydayNetworkManager), "ApplyDefaults")]
        private static void Postfix(global::Mayday.Net.MaydayNetworkManager __instance)
        {
            int target = Plugin.PlayerLimit.Value;
            int before = __instance.maxConnections;
            if (before != target)
            {
                __instance.maxConnections = target;
                Plugin.LogSource.LogInfo("[MaydayUnlock] ApplyDefaults 之后 maxConnections " + before + " -> " + target);
            }
        }
    }

    /// <summary>
    /// 创建 Steam 大厅时把席位数改成配置值。
    /// 原版是 CreateLobby(type, 4)，第 5 个人会被 Steam 直接挡在门外。
    /// </summary>
    [HarmonyPatch]
    internal static class CreateLobbyPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(global::Steamworks.SteamMatchmaking), "CreateLobby")]
        private static void Prefix(ref global::Steamworks.ELobbyType eLobbyType, ref int cMaxMembers)
        {
            int target = Plugin.PlayerLimit.Value;
            if (target > cMaxMembers)
            {
                cMaxMembers = target;
            }
        }
    }

    /// <summary>
    /// 兜底：CreateLobby 改写参数在部分 IL2CPP 构建里可能不生效，
    /// 因此在大厅创建回调里再用 Steam API 显式设一次成员上限。
    /// </summary>
    [HarmonyPatch]
    internal static class LobbyCreatedPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.Net.SteamLobbyService), "OnLobbyCreated")]
        private static void Postfix(global::Mayday.Net.SteamLobbyService __instance)
        {
            try
            {
                var lobby = __instance.Lobby;
                if (!lobby.IsValid())
                {
                    return;
                }

                int target = Plugin.PlayerLimit.Value;
                int before = global::Steamworks.SteamMatchmaking.GetLobbyMemberLimit(lobby);
                if (before != target)
                {
                    global::Steamworks.SteamMatchmaking.SetLobbyMemberLimit(lobby, target);
                    Plugin.LogSource.LogInfo("[MaydayUnlock] 大厅席位 " + before + " -> " + target);
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError("[MaydayUnlock] 设置大厅席位失败: " + e);
            }
        }
    }

    /// <summary>
    /// 有新连接到达时再校一次 maxConnections。
    /// IL2CPP 下无法自建 MonoBehaviour 子类做每帧巡检，只能挂在游戏已有方法上。
    /// </summary>
    [HarmonyPatch]
    internal static class ServerConnectPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(global::Mayday.Net.MaydayNetworkManager), "OnServerConnect")]
        private static void Prefix()
        {
            int target = Plugin.PlayerLimit.Value;
            var manager = Mirror.NetworkManager.singleton;
            if (manager != null && manager.maxConnections != target)
            {
                manager.maxConnections = target;
                Plugin.LogSource.LogInfo("[MaydayUnlock] 新连接到达，校正 maxConnections -> " + target);
            }
        }
    }

    /// <summary>
    /// IsFull 用 NetworkManager.maxConnections 判断满员，但它自己有一份 4 兜底。
    /// 在服务端接受连接前，把实例上的值重新校正一次。
    /// </summary>
    [HarmonyPatch]
    internal static class IsFullPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(global::Mayday.Net.MaydayAuthenticator), "IsFull")]
        private static void Prefix()
        {
            int target = Plugin.PlayerLimit.Value;
            var manager = Mirror.NetworkManager.singleton;
            if (manager != null && manager.maxConnections != target)
            {
                manager.maxConnections = target;
            }
        }
    }
}

