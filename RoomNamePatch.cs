using System;
using HarmonyLib;
using Mayday.Net;
using Steamworks;
using UnityEngine;

namespace MaydayKick
{
    // 大厅名由房主写进 Steam 大厅数据里（键名是 SteamLobbyService.KeyName），
    // 浏览器读的是同一条数据。所以在创建成功之后直接覆盖它就行。
    internal static class RoomName
    {
        public static void TryApply(SteamLobbyService steam)
        {
            if (steam == null) return;

            try
            {
                if (!steam.InLobby) return;
                var lobby = steam.Lobby;
                if (lobby.m_SteamID == 0) return;
                if (!steam.IsOwner) return;

                var key = SteamLobbyService.KeyName;
                var current = SteamMatchmaking.GetLobbyData(lobby, key);
                Plugin.Logger.LogInfo($"[MaydayKick] lobby data: {SteamLobbyService.KeyGame}='{SteamMatchmaking.GetLobbyData(lobby, SteamLobbyService.KeyGame)}' {SteamLobbyService.KeyVersion}='{SteamMatchmaking.GetLobbyData(lobby, SteamLobbyService.KeyVersion)}' {SteamLobbyService.KeyHost}='{SteamMatchmaking.GetLobbyData(lobby, SteamLobbyService.KeyHost)}' {key}='{current}'");

                var wanted = Plugin.RoomName.Value;
                if (string.IsNullOrWhiteSpace(wanted)) return;

                wanted = wanted.Replace("{host}", SafePersona());
                if (wanted.Length > 63) wanted = wanted.Substring(0, 63);
                if (wanted == current) return;

                var ok = SteamMatchmaking.SetLobbyData(lobby, key, wanted);
                Plugin.Logger.LogInfo($"[MaydayKick] room name -> '{wanted}' ok={ok}");
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[MaydayKick] room name failed: " + e.Message);
            }
        }

        private static string SafePersona()
        {
            try
            {
                return SteamFriends.GetPersonaName();
            }
            catch
            {
                return "";
            }
        }
    }

    [HarmonyPatch(typeof(SteamLobbyService), "OnLobbyCreated")]
    internal static class RoomNameOnLobbyCreatedPatch
    {
        private static void Postfix(SteamLobbyService __instance, LobbyCreated_t result, bool ioFailure)
        {
            try
            {
                if (ioFailure) return;
                RoomName.TryApply(__instance);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("[MaydayKick] room name patch: " + e);
            }
        }
    }

    // NetSession 在拿到大厅之后还会走一遍自己的回调，这里再兜一次，
    // 防止 SteamLobbyService.Lobby 在 OnLobbyCreated 时还没赋值。
    [HarmonyPatch(typeof(NetSession), "OnLobbyCreated")]
    internal static class RoomNameOnSessionPatch
    {
        private static void Postfix(NetSession __instance)
        {
            try
            {
                RoomName.TryApply(__instance.steam);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[MaydayKick] room name fallback: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(NetSession), "Update")]
    internal static class RoomNameSyncPatch
    {
        private static float next;

        private static void Postfix(NetSession __instance)
        {
            try
            {
                var now = Time.realtimeSinceStartup;
                if (now < next) return;
                next = now + 2f;

                if (string.IsNullOrWhiteSpace(Plugin.RoomName.Value)) return;
                if (Plugin.Instance != null) Plugin.Instance.Config.Reload();
                RoomName.TryApply(__instance.steam);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogDebug("[MaydayKick] room name sync: " + e.Message);
            }
        }
    }
}
