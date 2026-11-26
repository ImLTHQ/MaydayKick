using System;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Mayday.Net;
using Mirror;
using Steamworks;

namespace RoomKit
{
    /// <summary>
    /// 在认证阶段拦截数字昵称和黑名单；Steam 等级查询在认证通过后异步执行。
    /// </summary>
    [HarmonyPatch(typeof(MaydayAuthenticator), "OnAuthRequest")]
    internal static class BanPatch
    {
        private const string ReasonKey = "net.err.kicked";

        private sealed class LevelQueryState
        {
            internal ulong SteamId;
            internal string Name;
            internal NetworkConnectionToClient Connection;
        }

        private sealed class LevelResultState
        {
            internal LevelQueryState Query;
            internal SteamProfileLevelResult Result;
            internal string Error;
        }

        private static bool Prefix(MaydayAuthenticator __instance, NetworkConnectionToClient conn, NetAuthRequestMessage msg)
        {
            try
            {
                if (conn == null || msg == null) return true;

                var steamId = msg.steamId;
                if (Whitelist.Contains(steamId))
                {
                    Plugin.Logger.LogInfo("[RoomKit] [白名单] - " + msg.name + " - SteamID64 (" + steamId + ")");
                    return true;
                }

                if (Plugin.BlockNumericInGameName.Value && IsNumericName(msg.name))
                {
                    Plugin.Logger.LogInfo("[RoomKit] [阻止] - 纯数字游戏昵称 " + msg.name + " - SteamID64 (" + steamId + ")");
                    Reject(__instance, conn);
                    return false;
                }

                if (Plugin.BlockNumericSteamName.Value && IsNumericSteamName(steamId))
                {
                    Plugin.Logger.LogInfo("[RoomKit] [阻止] - 纯数字 Steam 昵称 SteamID64 (" + steamId + ")");
                    Reject(__instance, conn);
                    return false;
                }

                if (Blacklist.Contains(steamId))
                {
                    Plugin.Logger.LogInfo("[RoomKit] [阻止] [黑名单] - " + msg.name + " - SteamID64 (" + steamId + ")");
                    Reject(__instance, conn);
                    return false;
                }

                if (Plugin.LogSteamLevel.Value || Plugin.KickZeroSteamLevel.Value)
                {
                    var state = new LevelQueryState
                    {
                        SteamId = steamId,
                        Name = msg.name,
                        Connection = conn
                    };

                    QueueSteamLevelCheck(state);
                }

                return true;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("[RoomKit] 认证阶段拦截出错: " + e);
                return true;
            }
        }

        private static void QueueSteamLevelCheck(LevelQueryState state)
        {
            SteamProfileLevelLookup.GetAsync(state.SteamId).ContinueWith(
                new Action<Task<SteamProfileLevelResult>, object>(OnLevelQueryComplete),
                state,
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }

        private static void OnLevelQueryComplete(Task<SteamProfileLevelResult> task, object state)
        {
            var query = (LevelQueryState)state;
            var result = new LevelResultState
            {
                Query = query,
                Result = task.IsCompletedSuccessfully
                    ? task.Result
                    : new SteamProfileLevelResult(SteamProfileLevelStatus.Unavailable, 0, "查询任务失败"),
                Error = task.IsFaulted && task.Exception != null
                    ? task.Exception.GetBaseException().Message
                    : task.IsCanceled ? "查询任务取消" : null
            };

            if (!MainThreadActionRunner.Enqueue(() => CompleteLevelQuery(result)))
            {
                Plugin.Logger.LogError("[RoomKit] 主线程任务队列未初始化，无法处理 Steam 等级结果：" +
                    query.SteamId);
            }
        }

        private static void CompleteLevelQuery(LevelResultState state)
        {
            var query = state.Query;
            if (Whitelist.Contains(query.SteamId))
            {
                Plugin.Logger.LogInfo("[RoomKit] [白名单] 保留 " + query.Name +
                    " - SteamID64 (" + query.SteamId + ")");
                return;
            }

            var minimumLevel = Math.Max(1, Plugin.SteamLevelMinimum.Value);
            if (!string.IsNullOrEmpty(state.Error))
            {
                state.Result = new SteamProfileLevelResult(
                    SteamProfileLevelStatus.Unavailable,
                    0,
                    state.Error);
            }

            var belowMinimum = state.Result.Status == SteamProfileLevelStatus.Level &&
                state.Result.Level < minimumLevel;
            var lookupFailed = state.Result.Status == SteamProfileLevelStatus.Unavailable;
            var isUnqualified = state.Result.Status == SteamProfileLevelStatus.Private ||
                state.Result.Status == SteamProfileLevelStatus.Unqualified ||
                belowMinimum;

            if (Plugin.LogSteamLevel.Value)
                LogSteamLevel(query, state.Result);

            var shouldKick = isUnqualified ||
                lookupFailed && Plugin.KickOnSteamLevelLookupFailure.Value;
            if (!Plugin.KickZeroSteamLevel.Value || !shouldKick)
            {
                if (Plugin.KickZeroSteamLevel.Value && lookupFailed &&
                    !Plugin.KickOnSteamLevelLookupFailure.Value)
                    Plugin.Logger.LogInfo("[RoomKit] [放行] Steam 等级查询失败，按配置不踢出 " +
                        query.Name + " - SteamID64 (" + query.SteamId + ")");

                return;
            }

            var reason = state.Result.Status == SteamProfileLevelStatus.Private
                ? "Steam 资料为私密"
                : state.Result.Status == SteamProfileLevelStatus.Unqualified
                    ? "公开资料没有等级标记"
                    : state.Result.Status == SteamProfileLevelStatus.Unavailable
                        ? "Steam 等级查询失败或超时"
                        : "Steam 等级 " + state.Result.Level + " 低于下限 " + minimumLevel;
            Plugin.Logger.LogInfo("[RoomKit] [踢出] - " + reason + " " + query.Name +
                " - SteamID64 (" + query.SteamId + ")");

            try
            {
                if (query.Connection != null)
                    query.Connection.Disconnect();
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("[RoomKit] Steam 等级不合格玩家断开失败 (" + query.SteamId + "): " + e);
            }
        }

        private static void LogSteamLevel(LevelQueryState query, SteamProfileLevelResult result)
        {
            if (result.Status == SteamProfileLevelStatus.Level)
            {
                Plugin.Logger.LogInfo("[RoomKit] [查询] " + query.Name + " - SteamID64 (" +
                    query.SteamId + ") - Steam 等级: " + result.Level);
            }
            else if (result.Status == SteamProfileLevelStatus.Private)
            {
                Plugin.Logger.LogInfo("[RoomKit] [查询] " + query.Name + " - SteamID64 (" +
                    query.SteamId + ") - Steam 资料为私密");
            }
            else if (result.Status == SteamProfileLevelStatus.Unqualified)
            {
                Plugin.Logger.LogInfo("[RoomKit] [查询] " + query.Name + " - SteamID64 (" +
                    query.SteamId + ") - 公开资料没有等级标记");
            }
            else
            {
                Plugin.Logger.LogWarning("[RoomKit] [查询] 不可用 " + query.Name +
                    " - SteamID64 (" + query.SteamId + ") - " + result.Detail);
            }
        }

        private static void Reject(MaydayAuthenticator authenticator, NetworkConnectionToClient conn)
        {
            authenticator.Reject(conn, ReasonKey, null, null);
        }

        private static bool IsNumericSteamName(ulong steamId)
        {
            if (steamId == 0UL) return false;

            try
            {
                var steamName = SteamFriends.GetFriendPersonaName(new CSteamID(steamId));
                return IsNumericName(steamName);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[RoomKit] 读取 Steam 昵称失败 (" + steamId + "): " + e.Message);
                return false;
            }
        }

        private static bool IsNumericName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            var trimmedName = name.Trim();
            for (var i = 0; i < trimmedName.Length; i++)
            {
                if (!char.IsDigit(trimmedName[i])) return false;
            }

            return true;
        }
    }
}
