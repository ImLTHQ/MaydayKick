using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace MaydayKick
{
    [BepInPlugin(Guid, "Mayday Kick", "1.0.0")]
    public class Plugin : BasePlugin
    {
        public const string Guid = "local.mayday.kick";

        internal static ManualLogSource Logger;
        internal static ConfigEntry<string> RoomName;
        internal static ConfigEntry<bool> BlockNumericSteamName;
        internal static ConfigEntry<bool> BlockNumericInGameName;
        internal static ConfigEntry<bool> LogSteamLevel;
        internal static ConfigEntry<bool> KickZeroSteamLevel;
        internal static ConfigEntry<bool> KickOnSteamLevelLookupFailure;
        internal static ConfigEntry<int> SteamLevelMinimum;
        internal static ConfigEntry<int> SteamLevelQueryConcurrency;
        internal static Plugin Instance;

        public override void Load()
        {
            Logger = base.Log;
            Instance = this;
            MainThreadActionRunner.Initialize();

            RoomName = Config.Bind(
                "Room",
                "Name",
                "",
                "自定义大厅名。留空 = 游戏默认（房主名 + 航空）。支持 {host} 占位符，最长 63 字符。");

            BlockNumericSteamName = Config.Bind(
                "Moderation",
                "BlockNumericSteamName",
                true,
                "拒绝 Steam 昵称仅由数字组成的玩家。");

            BlockNumericInGameName = Config.Bind(
                "Moderation",
                "BlockNumericInGameName",
                true,
                "拒绝游戏内昵称仅由数字组成的玩家。");

            LogSteamLevel = Config.Bind(
                "Moderation",
                "LogSteamLevel",
                true,
                "记录玩家的 Steam 等级查询结果，不会因此踢出玩家。");

            KickZeroSteamLevel = Config.Bind(
                "Moderation",
                "KickZeroSteamLevel",
                false,
                "玩家先进游戏；后台查询后踢出 Steam 等级低于下限或资料不合格的玩家。查询失败是否踢出由 KickOnSteamLevelLookupFailure 控制。");

            KickOnSteamLevelLookupFailure = Config.Bind(
                "Moderation",
                "KickOnSteamLevelLookupFailure",
                false,
                "Steam 等级查询超时、请求失败或发生意外错误时是否踢出玩家；关闭时保留玩家。");

            SteamLevelMinimum = Config.Bind(
                "Moderation",
                "SteamLevelMinimum",
                1,
                "Steam 等级最低要求。低于此等级的玩家会在 KickZeroSteamLevel 开启时被踢出（最小值为 1）。");

            SteamLevelQueryConcurrency = Config.Bind(
                "Moderation",
                "SteamLevelQueryConcurrency",
                5,
                "Steam 等级后台查询的最大并发数（1-32，超出范围会限制到范围内）。");

            Blacklist.Init();
            Harmony.CreateAndPatchAll(typeof(BlacklistUiPatch), Guid);
            Harmony.CreateAndPatchAll(typeof(RowHookPatch), Guid);
            Harmony.CreateAndPatchAll(typeof(KickPatch), Guid);
            Harmony.CreateAndPatchAll(typeof(BanPatch), Guid);
            Harmony.CreateAndPatchAll(typeof(CrewScrollPatch), Guid);
            Harmony.CreateAndPatchAll(typeof(LobbyWatchPatch), Guid);
            Harmony.CreateAndPatchAll(typeof(LobbyTickPatch), Guid);
            Harmony.CreateAndPatchAll(typeof(RoomNameOnLobbyCreatedPatch), Guid);
            Harmony.CreateAndPatchAll(typeof(RoomNameOnSessionPatch), Guid);
            Harmony.CreateAndPatchAll(typeof(RoomNameSyncPatch), Guid);
            Logger.LogInfo("[MaydayKick] loaded v1.0.0 (kick + blacklist + queued Steam checks + player filters + crew scroll + room name)");
        }
    }
}