using System;
using Mayday;
using Mayday.UI;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MaydayKick
{
    // 机组列表每行原本只有「值班中 / 驾驶中」这种状态字，没有人名。
    // 这里在状态字右边补一个名字，优先取 Steam 昵称，取不到就用游戏自己的 displayName。
    internal static class NameTag
    {
        private const string TagName = "MaydayNameTag";

        private const string Sep = "  ";

        public static void Attach(PlayerNet p, PauseScreen.CrewRow row)
        {
            if (row == null || row.go == null || row.status == null) return;

            try
            {
                var tmp = row.status.GetComponentInChildren<TMP_Text>();
                if (tmp == null) return;

                // 本地玩家那一行原版自己就会显示头像和「名字（你）」，再拼一遍就重复了
                if (p != null && p.isLocalPlayer)
                {
                    var dup = row.go.transform.Find(TagName);
                    if (dup != null) UnityEngine.Object.Destroy(dup.gameObject);
                    return;
                }

                var text = ResolveName(p);
                if (string.IsNullOrEmpty(text)) text = "玩家";

                // 干脆共用状态文本这一个框：状态字后面空两格直接接名字。
                // 不新建控件就不用跟布局组、锚点、坐标系打架。
                string cur = tmp.text ?? string.Empty;
                string suffix = Sep + text;
                if (cur.EndsWith(suffix, StringComparison.Ordinal))
                    cur = cur.Substring(0, cur.Length - suffix.Length);
                tmp.text = cur + suffix;

                // 顺手清掉旧版留在这个行里的独立名字对象
                var old = row.go.transform.Find(TagName);
                if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[MaydayKick] name tag failed: " + e.Message);
            }
        }

        private static TMP_Text FindStatusText(UIText status)
        {
            try
            {
                if (status == null) return null;
                var t = status.GetComponentInChildren<TMP_Text>();
                return t;
            }
            catch { return null; }
        }

        private static string ResolveName(PlayerNet p)
        {
            if (p == null) return null;

            try
            {
                var id = p.SteamId;
                if (id != 0UL)
                {
                    var persona = SteamFriends.GetFriendPersonaName(new CSteamID(id));
                    if (!string.IsNullOrEmpty(persona))
                    {
                        // 没拿到 Steam 资料时它会返回 "[unknown]" 之类的占位
                        if (persona.Length > 0 && persona[0] != '[') return persona;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[MaydayKick] persona lookup failed: " + e.Message);
            }

            try
            {
                var dn = p.DisplayName;
                if (!string.IsNullOrEmpty(dn)) return dn;
            }
            catch { }

            return null;
        }
    }
}