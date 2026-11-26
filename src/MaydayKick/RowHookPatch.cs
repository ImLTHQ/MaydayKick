using System;
using HarmonyLib;
using Mayday.UI;

namespace MaydayKick
{
    // 行对象会被 RebuildCrew 复用：只有新玩家进来时才调 MakeRow，
    // 已有行只是刷新文字。所以「加名字/加按钮」不能只挂在 MakeRow 上，
    // 每次 RebuildCrew 结束后把所有现存行都过一遍。
    [HarmonyPatch(typeof(PauseScreen), "RebuildCrew")]
    internal static class RowHookPatch
    {
        private static void Postfix(PauseScreen __instance)
        {
            try
            {
                var rows = __instance.rows;
                if (rows == null) return;

                int count = rows.Count;
                if (count == 0) return;

                Plugin.Logger.LogInfo("[MaydayKick] rebuild hook rows=" + count);
                for (int i = 0; i < count; i++)
                {
                    var row = rows[i];
                    if (row == null) continue;
                    KickPatch.AttachRow(__instance, row);

                    // 本地玩家那一行原版只给 12px 高（日志 row12 h=12），头像和名字都被压没了。
                    // 给每行都钉一个 62px 的 LayoutElement，谁矮就补谁。
                    try
                    {
                        var go = row.go;
                        if (go != null)
                        {
                            var grt = go.GetComponent<UnityEngine.RectTransform>();
                            if (grt != null && grt.sizeDelta.y < 60f)
                                grt.sizeDelta = new UnityEngine.Vector2(grt.sizeDelta.x, 62f);

                            var le = go.GetComponent<UnityEngine.UI.LayoutElement>();
                            if (le == null) le = go.AddComponent<UnityEngine.UI.LayoutElement>();
                            if (le != null)
                            {
                                le.minHeight = 62f;
                                le.preferredHeight = 62f;
                            }
                        }
                    }
                    catch (Exception e) { Plugin.Logger.LogWarning("[MaydayKick] row height: " + e.Message); }
                }

                // 行数变了以后让内容容器重新算高度，否则最后一行会被裁在视口外面
                try
                {
                    var content = __instance.crewList;
                    if (content != null)
                    {
                        var rt = content.TryCast<UnityEngine.RectTransform>();
                        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

                        // 内容容器高度如果没把最后一行算进去，滚到底也会被切掉，这里自己补够
                        float need = 0f;
                        for (int i = 0; i < content.childCount; i++)
                        {
                            var c = content.GetChild(i).TryCast<UnityEngine.RectTransform>();
                            if (c != null) need += c.rect.height + 8f;
                        }
                        if (need > 0f && rt.rect.height < need + 8f)
                        {
                            rt.sizeDelta = new UnityEngine.Vector2(rt.sizeDelta.x, need + 8f);
                        }

                        if (Diag.Once("rowdump"))
                        {
                            try
                            {
                                string dump = "";
                                for (int i = 0; i < rows.Count; i++)
                                {
                                    var rw = rows[i];
                                    if (rw == null) { dump += " | row" + i + ":null"; continue; }
                                    var p = rw.player;
                                    var go = rw.go;
                                    var grt = go != null ? go.GetComponent<UnityEngine.RectTransform>() : null;
                                    dump += " | row" + i
                                        + " h=" + (grt != null ? grt.rect.height.ToString("F0") : "?")
                                        + " active=" + (go != null && go.activeSelf)
                                        + " name=" + (p != null ? p.DisplayName : "-")
                                        + " local=" + (p != null && p.isLocalPlayer)
                                        + " conn=" + (p != null && p.connectionToClient != null ? p.connectionToClient.connectionId.ToString() : "-");
                                }
                                Plugin.Logger.LogInfo("[MaydayKick] rowdump:" + dump);

                                string kids = "";
                                for (int i = 0; i < content.childCount; i++)
                                {
                                    var c = content.GetChild(i);
                                    var crt = c.TryCast<UnityEngine.RectTransform>();
                                    kids += " <" + c.name + " a=" + c.gameObject.activeSelf
                                        + " h=" + (crt != null ? crt.rect.height.ToString("F0") : "?")
                                        + " y=" + (crt != null ? crt.anchoredPosition.y.ToString("F0") : "?") + ">";
                                }
                                Plugin.Logger.LogInfo("[MaydayKick] contentkids:" + kids);
                            }
                            catch (Exception e) { Plugin.Logger.LogWarning("[MaydayKick] rowdump failed: " + e.Message); }
                        }

                        if (Diag.Once("scrolltree"))
                        {
                            var vp = content.parent != null ? content.parent.TryCast<UnityEngine.RectTransform>() : null;
                            string chain = "";
                            UnityEngine.Transform cur = content;
                            for (int d = 0; d < 4 && cur != null; d++)
                            {
                                var crt = cur.TryCast<UnityEngine.RectTransform>();
                                chain += " <" + cur.name + " h=" + (crt != null ? crt.rect.height.ToString("F0") : "?") + ">";
                                if (cur == null) break;
                                cur = cur.parent;
                            }
                            Plugin.Logger.LogInfo("[MaydayKick] scrolltree: need=" + need.ToString("F0")
                                + " content=" + rt.rect.height.ToString("F0")
                                + " viewport=" + (vp != null ? vp.rect.height.ToString("F0") : "null")
                                + " anchoredPos=" + rt.anchoredPosition
                                + " kids=" + content.childCount + chain);
                        }
                    }
                }
                catch (Exception e) { Plugin.Logger.LogWarning("[MaydayKick] content relayout: " + e.Message); }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[MaydayKick] rebuild hook failed: " + e.Message);
            }
        }
    }
}