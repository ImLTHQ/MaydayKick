using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Mayday.Net;
using Mayday.UI;
using Steamworks;
using UnityEngine;

namespace MaydayKick
{
    // Steam 大厅没有"房主移除成员"的 API：ISteamMatchmaking 里只有 LeaveLobby（自己退）、
    // SetLobbyJoinable、SetLobbyMemberLimit 等，没有 kick。
    // 所以踢人之后对方还在不在大厅里，完全取决于对方客户端收到断线后自己怎么处理
    // （NetSession.OnClientDisconnected 很可能就会调 SteamLobbyService.Leave）。
    // 这里只做观测：踢完在大厅成员列表里采样几次，把结果打进日志。
    internal static class LobbyDiag
    {
        private struct Check
        {
            public float at;
            public ulong steamId;
            public string tag;
        }

        private static readonly List<Check> pending = new List<Check>();

        public static void Snapshot(string tag, ulong watchSteamId = 0)
        {
            try
            {
                var session = NetSession.Instance;
                if (session == null)
                {
                    Plugin.Logger.LogInfo("[MaydayKick] lobby(" + tag + "): no NetSession");
                    return;
                }

                var steam = session.steam;
                if (steam == null || !steam.InLobby)
                {
                    Plugin.Logger.LogInfo("[MaydayKick] lobby(" + tag + "): not in lobby");
                    return;
                }

                var lobby = steam.Lobby;
                int n = SteamMatchmaking.GetNumLobbyMembers(lobby);
                var sb = new StringBuilder();
                bool found = false;
                for (int i = 0; i < n; i++)
                {
                    var m = SteamMatchmaking.GetLobbyMemberByIndex(lobby, i);
                    sb.Append(m.m_SteamID).Append(' ');
                    if (watchSteamId != 0 && m.m_SteamID == watchSteamId) found = true;
                }

                var tail = watchSteamId == 0 ? "" : (found ? " target=IN-LOBBY" : " target=GONE");
                Plugin.Logger.LogInfo($"[MaydayKick] lobby({tag}): owner={steam.IsOwner} members={n}{tail} [{sb.ToString().Trim()}]");
            }
            catch (System.Exception e)
            {
                Plugin.Logger.LogWarning("[MaydayKick] lobby snapshot failed: " + e.Message);
            }
        }

        public static void Schedule(ulong steamId, float delay, string tag)
        {
            pending.Add(new Check { at = Time.realtimeSinceStartup + delay, steamId = steamId, tag = tag });
        }

        // 由 PauseScreen.Update 的补丁驱动；暂停界面关着的时候不会跑，够用了。
        public static void Tick()
        {
            if (pending.Count == 0) return;

            var now = Time.realtimeSinceStartup;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].at > now) continue;
                var c = pending[i];
                pending.RemoveAt(i);
                Snapshot(c.tag, c.steamId);
            }
        }
    }

    [HarmonyPatch(typeof(PauseScreen), "Update")]
    internal static class LobbyTickPatch
    {
        private static void Postfix(PauseScreen __instance)
        {
            try
            {
                LobbyDiag.Tick();
                CrewScrollPatch.Diag(__instance);
            }
            catch (System.Exception e)
            {
                Plugin.Logger.LogWarning("[MaydayKick] lobby tick failed: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(SteamLobbyService), "OnChatUpdate")]
    internal static class LobbyWatchPatch
    {
        private static void Postfix(LobbyChatUpdate_t e)
        {
            try
            {
                var change = (EChatMemberStateChange)e.m_rgfChatMemberStateChange;
                Plugin.Logger.LogInfo($"[MaydayKick] lobby chat update: user={e.m_ulSteamIDUserChanged} by={e.m_ulSteamIDMakingChange} change={change}");
            }
            catch (System.Exception ex)
            {
                Plugin.Logger.LogWarning("[MaydayKick] chat update log failed: " + ex.Message);
            }
        }
    }
}