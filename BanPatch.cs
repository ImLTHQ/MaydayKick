using System;
using HarmonyLib;
using Mayday.Net;
using Mirror;

namespace MaydayKick
{
    /// <summary>
    /// 在认证阶段拦黑名单：客户端连上来、还没进场景时就把人拒掉，
    /// 走游戏自己的 Reject 通道，客户端看到的是正常的“被移出会话”提示，不是黑屏。
    /// </summary>
    [HarmonyPatch(typeof(MaydayAuthenticator), "OnAuthRequest")]
    internal static class BanPatch
    {
        // net.err.kicked  = "You were removed from the session."（不需要参数，最稳）
        // 其他可用键：net.err.lobby_denied + net.err.detail.banned / net.err.detail.access_denied
        private const string ReasonKey = "net.err.kicked";

        private static bool Prefix(MaydayAuthenticator __instance, NetworkConnectionToClient conn, NetAuthRequestMessage msg)
        {
            try
            {
                if (conn == null || msg == null) return true;

                var steamId = msg.steamId;
                if (!Blacklist.Contains(steamId)) return true;

                Plugin.Logger.LogInfo("[MaydayKick] 拒绝黑名单玩家 " + msg.name + " (" + steamId + ")");
                __instance.Reject(conn, ReasonKey, null, null);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("[MaydayKick] 黑名单拦截出错: " + e);
                return true;
            }
        }
    }
}
