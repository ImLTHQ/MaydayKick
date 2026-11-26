using System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mayday;
using Mayday.UI;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace MaydayKick
{
    [HarmonyPatch(typeof(PauseScreen), "MakeRow")]
    internal static class KickPatch
    {
        private const string KickBtn = "MaydayKickButton";
        private const string BanBtn = "MaydayBanButton";

        private static void Postfix(PauseScreen __instance, PlayerNet p, PauseScreen.CrewRow __result)
        {
            try
            {
                // 名字对客户端也显示，所以放在房主判断之前
                NameTag.Attach(p, __result);
                Attach(__instance, p, __result);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("[MaydayKick] attach failed: " + e);
            }
        }

        // IL2CPP 的 wrapper 每次访问都可能是新的托管对象，
        // 直接 == 比较两个 NetworkConnectionToClient 永远为 false，
        // 所以一律比 connectionId，并用 isLocalPlayer 兜底。
        private static int LocalConnId()
        {
            try
            {
                var local = NetworkServer.localConnection;
                return local == null ? -1 : local.connectionId;
            }
            catch
            {
                return -1;
            }
        }

        private static bool IsHostSelf(PlayerNet p)
        {
            try
            {
                if (p.isLocalPlayer) return true;
            }
            catch
            {
                // 继续用连接号判断
            }

            try
            {
                var conn = p.connectionToClient;
                if (conn == null) return true;
                var local = LocalConnId();
                if (local >= 0 && conn.connectionId == local) return true;
            }
            catch
            {
                return true;
            }

            return false;
        }

        public static void AttachRow(PauseScreen screen, PauseScreen.CrewRow row)
        {
            if (row == null) return;
            try
            {
                NameTag.Attach(row.player, row);
                Attach(screen, row.player, row);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[MaydayKick] row hook failed: " + e.Message);
            }
        }

        private static void Attach(PauseScreen screen, PlayerNet p, PauseScreen.CrewRow row)
        {
            if (row == null || row.go == null) return;
            if (!NetworkServer.active) return;
            if (p == null) return;
            if (IsHostSelf(p)) return;

            if (Diag.Once("kickrow"))
            {
                string connTxt = "n/a", localTxt = "n/a";
                try { connTxt = p.connectionToClient == null ? "null" : p.connectionToClient.connectionId.ToString(); } catch (Exception e) { connTxt = "err:" + e.Message; }
                try { localTxt = LocalConnId().ToString(); } catch { }
                Plugin.Logger.LogInfo("[MaydayKick] kickrow diag: display='" + p.DisplayName + "' isLocal=" + p.isLocalPlayer
                    + " conn=" + connTxt + " local=" + localTxt + " serverActive=" + NetworkServer.active);
            }

            var tr = row.go.transform;
            if (tr.Find(BanBtn) != null) return;

            // 关键：行对象会被 RebuildCrew 复用给别的玩家（老玩家走了、新玩家占了这一行），
            // 如果在创建按钮时把 connId/steamId 存进闭包，之后这一行换了人，按钮还是踢原来那个人。
            // 所以闭包里只存「这一行」本身，点的时候再去读 row.player 是谁。
            var targetRow = row;

            var kickAction = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new System.Action(delegate
            {
                Kick(targetRow, false);
            }));
            var banAction = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new System.Action(delegate
            {
                Kick(targetRow, true);
            }));

            var hlg = row.go.GetComponent<HorizontalLayoutGroup>();
            AddButton(tr, KickBtn, GlyphId.Exit, ButtonStyle.Danger, kickAction, hlg);
            AddButton(tr, BanBtn, GlyphId.Lock, ButtonStyle.Secondary, banAction, hlg);

            Plugin.Logger.LogInfo("[MaydayKick] buttons added for row of " + p.DisplayName + " layout=" + (hlg != null));
        }

        private static void AddButton(Transform parent, string name, GlyphId glyph, ButtonStyle style, Il2CppSystem.Action onClick, HorizontalLayoutGroup hlg)
        {
            var btn = UIF.IconButton(parent, name, glyph, onClick, style, 22f);
            if (btn == null)
            {
                Plugin.Logger.LogWarning("[MaydayKick] IconButton returned null for " + name);
                return;
            }

            if (hlg != null)
            {
                btn.transform.SetAsLastSibling();
            }
            else
            {
                UIF.IgnoreLayout(btn.gameObject);
                var rt = btn.GetComponent<RectTransform>();
                if (rt != null)
                {
                    var offset = name == BanBtn ? 26f : 0f;
                    rt.anchorMin = new Vector2(1f, 0.5f);
                    rt.anchorMax = new Vector2(1f, 0.5f);
                    rt.pivot = new Vector2(1f, 0.5f);
                    rt.anchoredPosition = new Vector2(-(2f + offset), 0f);
                    rt.sizeDelta = new Vector2(22f, 22f);
                }
            }
        }

        private static void Kick(PauseScreen.CrewRow row, bool ban)
        {
            try
            {
                // 点击这一刻才去问「这一行现在是谁」，不要用创建按钮时缓存的身份
                if (row == null)
                {
                    Plugin.Logger.LogWarning("[MaydayKick] kick clicked but row is null");
                    return;
                }

                var p = row.player;
                if (p == null)
                {
                    Plugin.Logger.LogWarning("[MaydayKick] kick clicked but row.player is null");
                    return;
                }

                if (IsHostSelf(p))
                {
                    Plugin.Logger.LogWarning("[MaydayKick] refuse to kick myself");
                    return;
                }

                var conn = p.connectionToClient;
                if (conn == null)
                {
                    Plugin.Logger.LogWarning("[MaydayKick] kick target has no connection: " + p.DisplayName);
                    return;
                }

                var connId = conn.connectionId;
                var steamId = p.SteamId;
                var displayName = p.DisplayName;

                var local = LocalConnId();
                if (local >= 0 && connId == local)
                {
                    Plugin.Logger.LogWarning("[MaydayKick] refuse to kick host's own connection " + connId);
                    return;
                }

                if (ban && Blacklist.Add(steamId, displayName))
                {
                    Plugin.Logger.LogInfo("[MaydayKick] banned " + displayName + " (" + steamId + ")");
                }

                var live = FindConnection(connId);
                if (live == null)
                {
                    Plugin.Logger.LogWarning("[MaydayKick] connection " + connId + " already gone");
                    return;
                }

                Plugin.Logger.LogInfo("[MaydayKick] kicking " + displayName + " (" + steamId + ") conn=" + connId + " ban=" + ban);
                LobbyDiag.Snapshot("before-kick");
                live.Disconnect();
                LobbyDiag.Schedule(steamId, 1.5f, "kick+1.5s");
                LobbyDiag.Schedule(steamId, 5f, "kick+5s");
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("[MaydayKick] kick failed: " + e);
            }
        }

        private static NetworkConnectionToClient FindConnection(int connId)
        {
            try
            {
                foreach (var kv in NetworkServer.connections)
                {
                    var c = kv.Value;
                    if (c != null && c.connectionId == connId) return c;
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[MaydayKick] connection lookup failed: " + e.Message);
            }

            return null;
        }
    }
}