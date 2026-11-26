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
        private const string MinimumLevelSuffixPrefix = " | 下限Steam公开Lv";
        private const float LobbyDataLogInterval = 30f;
        private static string lastLobbyDataLog;
        private static ulong lastLobbyDataLobbyId;
        private static float lastLobbyDataLogAt;

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
                var configuredName = Plugin.RoomName.Value;
                var wanted = string.IsNullOrWhiteSpace(configuredName) ? current : configuredName;
                wanted = RemoveGeneratedSuffix(wanted);
                if (!string.IsNullOrWhiteSpace(wanted))
                {
                    wanted = wanted.Replace("{host}", SafePersona());
                    var suffix = MinimumLevelSuffixPrefix + Math.Max(1, Plugin.SteamLevelMinimum.Value);
                    var maxBaseLength = 63 - suffix.Length;
                    if (wanted.Length > maxBaseLength)
                    {
                        if (maxBaseLength > 0 && char.IsHighSurrogate(wanted[maxBaseLength - 1]) &&
                            char.IsLowSurrogate(wanted[maxBaseLength]))
                            maxBaseLength--;

                        wanted = wanted.Substring(0, maxBaseLength);
                    }
                    wanted += suffix;

                    if (wanted != current)
                    {
                        var ok = SteamMatchmaking.SetLobbyData(lobby, key, wanted);
                        Plugin.Logger.LogInfo($"[MaydayKick] room name -> '{wanted}' ok={ok}");
                    }
                }

                LogLobbyDataIfNeeded(lobby, key);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[MaydayKick] room name failed: " + e.Message);
            }
        }

        private static void LogLobbyDataIfNeeded(CSteamID lobby, string nameKey)
        {
            var game = SteamMatchmaking.GetLobbyData(lobby, SteamLobbyService.KeyGame);
            var version = SteamMatchmaking.GetLobbyData(lobby, SteamLobbyService.KeyVersion);
            var host = SteamMatchmaking.GetLobbyData(lobby, SteamLobbyService.KeyHost);
            var name = SteamMatchmaking.GetLobbyData(lobby, nameKey);
            var message = $"[MaydayKick] lobby data: {SteamLobbyService.KeyGame}='{game}' {SteamLobbyService.KeyVersion}='{version}' {SteamLobbyService.KeyHost}='{host}' {nameKey}='{name}'";
            var now = Time.realtimeSinceStartup;

            if (lobby.m_SteamID != lastLobbyDataLobbyId ||
                message != lastLobbyDataLog ||
                now - lastLobbyDataLogAt >= LobbyDataLogInterval)
            {
                Plugin.Logger.LogInfo(message);
                lastLobbyDataLobbyId = lobby.m_SteamID;
                lastLobbyDataLog = message;
                lastLobbyDataLogAt = now;
            }
        }

        public static string RemoveGeneratedSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            var suffixIndex = name.LastIndexOf(MinimumLevelSuffixPrefix, StringComparison.Ordinal);
            if (suffixIndex < 0) return name;

            var level = name.Substring(suffixIndex + MinimumLevelSuffixPrefix.Length);
            if (level.Length == 0) return name;
            for (var i = 0; i < level.Length; i++)
            {
                if (level[i] < '0' || level[i] > '9') return name;
            }

            return name.Substring(0, suffixIndex);
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
