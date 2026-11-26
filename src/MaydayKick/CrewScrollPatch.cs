using System;
using HarmonyLib;
using Mayday.UI;
using UnityEngine;
using UnityEngine.UI;

namespace MaydayKick
{
    // 机组列表超过大约 8 行就会被卡片裁掉，后面的人连名字都看不到，因为原版
    // crewList 是个固定高度的容器，没有滚动。
    //
    // 做法：在 Build() 之后建一个 ScrollRect，把**原来那个 crewList 容器整块塞进
    // viewport 当 content**，PauseScreen.crewList 仍然指向它。这样 RebuildCrew()
    // 照旧往里塞行，行都是可见的，只是现在多了一层滚动裁剪。
    // （v0.3.0 曾经另建一个 content 然后把旧容器停用，结果 RebuildCrew 复用的还是
    //   旧容器里的行 → 整个机组列表被隐藏，一个人都看不到。）
    [HarmonyPatch(typeof(PauseScreen), "Build")]
    internal static class CrewScrollPatch
    {
        private static int diagLeft;

        // 打开暂停菜单后头几帧把滚动框/内容框的真实尺寸打出来，方便远程判断布局对不对
        private static string TmpOf(UIText u)
        {
            if (u == null) return "null";
            try
            {
                var tmp = u.GetComponentInChildren<TMPro.TMP_Text>();
                return tmp == null ? "(no tmp)" : (tmp.text ?? "(null)");
            }
            catch (System.Exception e) { return "(err " + e.Message + ")"; }
        }

        public static void Diag(PauseScreen screen)
        {
            if (diagLeft <= 0 || screen == null) return;
            diagLeft--;
            try
            {
                var scrollT = screen.transform.Find("Body/MaydayCrewScroll");
                var rt = scrollT == null ? null : scrollT.TryCast<RectTransform>();
                var content = screen.crewList;
                var crt = content == null ? null : content.TryCast<RectTransform>();
                // 兜底自愈：要是又被压扁了，就直接给个高度，至少能看见
                if (rt != null && rt.rect.height < 100f)
                {
                    rt.sizeDelta = new Vector2(rt.sizeDelta.x, 420f);
                    Plugin.Logger.LogWarning("[MaydayKick] scroll too flat, forced height 420");
                }

                var rowInfo = "";
                if (screen.rows != null && screen.rows.Count > 0)
                {
                    var r = screen.rows[0];
                    if (r != null)
                    {
                        rowInfo = " row0.name='" + TmpOf(r.name)
                                + "' status='" + TmpOf(r.status)
                                + "' nameActive=" + (r.name != null && r.name.gameObject.activeSelf)
                                + " rowH=" + (r.go == null ? -1f : r.go.GetComponent<RectTransform>().rect.height);
                    }
                }

                Plugin.Logger.LogInfo(
                    "[MaydayKick] scroll diag: scroll=" + (rt == null ? "null" : rt.rect.size.ToString())
                    + " content=" + (crt == null ? "null" : crt.rect.size.ToString())
                    + " rows=" + (screen.rows == null ? -1 : screen.rows.Count)
                    + " children=" + (content == null ? -1 : content.childCount) + rowInfo);
            }
            catch (System.Exception e)
            {
                Plugin.Logger.LogWarning("[MaydayKick] scroll diag failed: " + e.Message);
            }
        }
        private static void Postfix(PauseScreen __instance)
        {
            try
            {
                PausePeek.Column = __instance.crewList != null ? __instance.crewList.parent : null;
                Wrap(__instance);
                RoomNameUi.Build(__instance, PausePeek.Column);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("[MaydayKick] crew scroll wrap failed: " + e);
            }
        }

        private static void Wrap(PauseScreen screen)
        {
            var old = screen.crewList;
            if (old == null)
            {
                Plugin.Logger.LogWarning("[MaydayKick] crewList is null, skip scroll");
                return;
            }

            var parent = old.parent;
            if (parent == null) return;

            var oldRt = old.TryCast<RectTransform>();
            if (oldRt == null) return;

            if (parent.Find("MaydayCrewScroll") != null)
            {
                Plugin.Logger.LogInfo("[MaydayKick] crew scroll already present");
                return;
            }

            int index = old.GetSiblingIndex();
            var aMin = oldRt.anchorMin;
            var aMax = oldRt.anchorMax;
            var pivot = oldRt.pivot;
            var pos = oldRt.anchoredPosition;
            var size = oldRt.sizeDelta;

            Plugin.Logger.LogInfo($"[MaydayKick] crewList rect: anchor={aMin}/{aMax} pivot={pivot} pos={pos} size={size} parent={parent.name} children={old.childCount}");

            RectTransform throwaway;
            var scroll = UIF.Scroll(parent, "MaydayCrewScroll", out throwaway, 8f, UIF.Pad(8));
            if (scroll == null)
            {
                Plugin.Logger.LogWarning("[MaydayKick] UIF.Scroll returned null");
                return;
            }

            var srt = scroll.GetComponent<RectTransform>();
            if (srt == null)
            {
                Plugin.Logger.LogWarning("[MaydayKick] scroll has no RectTransform");
                return;
            }

            // 上一版让布局组无视它，结果那行提示文字挤上来跟列表重叠。这次留在布局流里，
            // 但必须给 LayoutElement 首选高度，否则 Body 会把它压成 0 高度（500x0 什么都看不见）。
            UIF.LE(srt.gameObject, 0f, 420f, 1f, 1f, 0f, 260f);
            srt.SetSiblingIndex(index);
            srt.anchorMin = aMin;
            srt.anchorMax = aMax;
            srt.pivot = pivot;
            srt.anchoredPosition = pos;
            srt.sizeDelta = new Vector2(size.x, 420f);

            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            if (scroll.verticalScrollbar != null)
            {
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            }

            // 把原来的容器挪进 viewport，让它自己当 content
            var viewport = scroll.viewport;
            Transform viewportT = viewport != null ? viewport : srt;
            if (viewportT != old && viewportT != old.parent)
            {
                old.SetParent(viewportT, false);
            }

            oldRt.anchorMin = new Vector2(0f, 1f);
            oldRt.anchorMax = new Vector2(1f, 1f);
            oldRt.pivot = new Vector2(0.5f, 1f);
            oldRt.anchoredPosition = Vector2.zero;
            oldRt.sizeDelta = new Vector2(0f, size.y);

            // UIF.Scroll 顺手建的那个空 content 没用了，藏起来
            if (throwaway != null && throwaway != oldRt)
            {
                throwaway.gameObject.SetActive(false);
            }

            scroll.content = oldRt;

            if (old.GetComponent<VerticalLayoutGroup>() == null)
            {
                UIF.VLayout(old.gameObject, 8f, UIF.Pad(0), TextAnchor.UpperLeft, true, true, true, false);
                Plugin.Logger.LogInfo("[MaydayKick] added VerticalLayoutGroup to crewList");
            }

            if (old.GetComponent<ContentSizeFitter>() == null)
            {
                UIF.Fit(old.gameObject, false, true);
                Plugin.Logger.LogInfo("[MaydayKick] added ContentSizeFitter to crewList");
            }

            try
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(oldRt);
                Canvas.ForceUpdateCanvases();
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[MaydayKick] layout rebuild: " + e.Message);
            }

            diagLeft = 4;
            DumpTree(srt, 0);
            Plugin.Logger.LogInfo("[MaydayKick] crew list is now scrollable");
        }

        private static void DumpTree(Transform t, int depth)
        {
            if (depth > 3 || t == null) return;
            var rt = t.TryCast<RectTransform>();
            var extra = rt == null ? "" : $" size={rt.sizeDelta} anchor={rt.anchorMin}/{rt.anchorMax}";
            Plugin.Logger.LogInfo(new string(' ', depth * 2) + "[MaydayKick] tree " + t.name + extra + " children=" + t.childCount);
            for (int i = 0; i < t.childCount; i++)
            {
                DumpTree(t.GetChild(i), depth + 1);
            }
        }
    }
}