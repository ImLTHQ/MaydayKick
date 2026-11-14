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
        internal static Plugin Instance;

        public override void Load()
        {
            Logger = base.Log;
            Instance = this;

            RoomName = Config.Bind(
                "Room",
                "Name",
                "",
                "自定义大厅名。留空 = 游戏默认（房主名 + 航空）。支持 {host} 占位符，最长 63 字符。");

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
            Logger.LogInfo("[MaydayKick] loaded v1.0.0 (kick + blacklist + crew scroll + room name)");
        }
    }
}