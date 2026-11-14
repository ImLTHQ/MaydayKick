using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mayday.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MaydayKick
{
    [HarmonyPatch(typeof(MainMenuScreen), "Build")]
    internal static class BlacklistUiPatch
    {
        private static void Postfix(MainMenuScreen __instance)
        {
            BlacklistUi.Attach(__instance);
        }
    }

    /// <summary>
    /// 主界面左下角一个「黑名单」按钮，点开是黑名单管理面板，可以逐条移除。
    /// 全部用游戏自己的 UIF 控件搭，风格跟原生一致。
    /// </summary>
    internal static class BlacklistUi
    {
        private const string RootName = "MaydayBlacklistRoot";
        private const string RowPrefix = "BlRow_";

        private static GameObject root;
        private static GameObject overlay;
        private static RectTransform content;
        private static ScrollRect scrollRef;
        private static UIText countText;
        private static readonly List<GameObject> rows = new List<GameObject>();

        private static Color PanelColor { get { return new Color(0.968f, 0.945f, 0.894f, 1f); } }
        private static Color TextColor { get { return new Color(0.13f, 0.19f, 0.31f, 1f); } }
        private static Color SubColor { get { return new Color(0.35f, 0.4f, 0.5f, 1f); } }

        public static void Attach(MainMenuScreen screen)
        {
            try
            {
                if (screen == null) return;
                Plugin.Logger.LogInfo("[MaydayKick] blui step1 screen ok");
                var host = screen.GetComponent<RectTransform>();
                if (host == null) { Plugin.Logger.LogWarning("[MaydayKick] blui host null"); return; }
                Plugin.Logger.LogInfo("[MaydayKick] blui step2 host=" + host.name + " size=" + host.rect.width + "x" + host.rect.height);
                if (host.Find(RootName) != null) return;

                var rt = UIF.Rect(RootName, host);
                if (rt == null) { Plugin.Logger.LogWarning("[MaydayKick] blui rect null"); return; }
                Plugin.Logger.LogInfo("[MaydayKick] blui step3 root ok");
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                UIF.IgnoreLayout(rt.gameObject);
                root = rt.gameObject;
                Plugin.Logger.LogInfo("[MaydayKick] blui step4 ignorelayout ok");

                // 先把面板搭起来（这是主功能），再单独建入口按钮，互不干扰
                try
                {
                    BuildOverlay(rt);
                    if (overlay != null) overlay.SetActive(false);
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogWarning("[MaydayKick] blui overlay failed: " + e);
                }
                Plugin.Logger.LogInfo("[MaydayKick] blui step7 overlay done overlay=" + (overlay != null));

                // 打开按钮：左下角
                try
                {
                    var openAction = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new System.Action(Open));
                    Plugin.Logger.LogInfo("[MaydayKick] blui step5 delegate ok");
                    var open = UIF.Button(rt, "MaydayBlOpen", "黑名单", openAction, ButtonStyle.Secondary, 220f, 64f, new Il2CppSystem.Nullable<GlyphId>(), 26f, 14f);
                    Plugin.Logger.LogInfo("[MaydayKick] blui step6 button " + (open == null ? "null" : "ok"));
                    if (open != null) PlaceOpenButton(rt, open.GetComponent<RectTransform>());
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogWarning("[MaydayKick] blui button failed: " + e);
                }

                                Plugin.Logger.LogInfo("[MaydayKick] blui step9 attached");
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[MaydayKick] blacklist ui failed: " + e.Message);
            }
        }

        /// <summary>
        /// 入口按钮摆位：在 Build 阶段世界坐标全是 0（画布还没排版），所以不量屏幕，
        /// 直接挂到「退出」那个按钮的同一个父节点（那是个纵向布局组 Buttons）后面，
        /// 尺寸按它的 rect 取，位置交给游戏自己的布局组。
        /// </summary>
        private static void PlaceOpenButton(RectTransform root, RectTransform orc)
        {
            if (root == null || orc == null) return;

            RectTransform refBtn = FindMenuButton(root, "退出");
            if (refBtn == null) refBtn = FindMenuButton(root, "开始游戏");
            if (refBtn == null) refBtn = FindMenuButton(root, "退出游戏");
            if (refBtn == null) refBtn = FindMenuButton(root, "Quit");
            if (refBtn == null) refBtn = FindMenuButton(root, "Play");

            if (refBtn == null)
            {
                Plugin.Logger.LogWarning("[MaydayKick] blui diag no reference button, fallback bottom-left");
                orc.anchorMin = new Vector2(0f, 0f);
                orc.anchorMax = new Vector2(0f, 0f);
                orc.pivot = new Vector2(0f, 0f);
                orc.anchoredPosition = new Vector2(153f, 280f);
                orc.sizeDelta = new Vector2(400f, 96f);
                return;
            }

            var parent = refBtn.parent;
            if (parent == null) return;
            var parentRt = parent.TryCast<RectTransform>();

            Plugin.Logger.LogInfo("[MaydayKick] blui diag refBtn '" + refBtn.name + "' rect=" + refBtn.rect.width + "x" + refBtn.rect.height
                + " anchor=" + refBtn.anchorMin + "/" + refBtn.anchorMax + " pivot=" + refBtn.pivot
                + " anchoredPos=" + refBtn.anchoredPosition + " parent=" + parent.name
                + " hasVLG=" + (parentRt != null && parentRt.GetComponent<VerticalLayoutGroup>() != null)
                + " hasHLG=" + (parentRt != null && parentRt.GetComponent<HorizontalLayoutGroup>() != null));

            orc.SetParent(parent, false);
            orc.localScale = Vector3.one;
            orc.SetSiblingIndex(refBtn.GetSiblingIndex() + 1);

            float w = refBtn.rect.width * 0.63f;
            float h = refBtn.rect.height;
            if (w < 80f) w = 220f;
            if (h < 30f) h = 64f;

            bool hasGroup = parentRt != null && (parentRt.GetComponent<LayoutGroup>() != null);

            if (hasGroup)
            {
                // 交给布局组：只声明尺寸，位置它自己排（排在「退出」后面）
                orc.anchorMin = refBtn.anchorMin;
                orc.anchorMax = refBtn.anchorMax;
                orc.pivot = refBtn.pivot;
                orc.anchoredPosition = refBtn.anchoredPosition;
                orc.sizeDelta = new Vector2(w, h);
                try
                {
                    var le = orc.gameObject.GetComponent<LayoutElement>();
                    if (le == null) le = orc.gameObject.AddComponent<LayoutElement>();
                    le.preferredWidth = w;
                    le.preferredHeight = h;
                    le.minHeight = h;
                    le.flexibleWidth = 0f;
                    le.flexibleHeight = 0f;
                }
                catch (Exception e) { Plugin.Logger.LogWarning("[MaydayKick] blui le failed: " + e.Message); }

                // 游戏把 Discord 按钮绝对定位在「退出」正下方那一行，正好跟我们的按钮抢同一行。
                // 在布局组里插一个空白占位，把我们的按钮顶到 Discord 下面一行去。
                float spacerH = 60f;
                var discord = FindByText(root, "Discord");
                if (discord != null)
                {
                    var dr = discord.rect;
                    Plugin.Logger.LogInfo("[MaydayKick] blui diag discord rect=" + dr.width + "x" + dr.height + " pos=" + discord.anchoredPosition + " name=" + discord.name);
                    if (dr.height > 10f) spacerH = dr.height + 16f;
                }

                var spacer = UIF.Rect("MaydayBlSpacer", parentRt);
                if (spacer != null)
                {
                    spacer.SetSiblingIndex(orc.GetSiblingIndex());
                    spacer.sizeDelta = new Vector2(0f, spacerH);
                    try
                    {
                        var sle = spacer.gameObject.GetComponent<LayoutElement>();
                        if (sle == null) sle = spacer.gameObject.AddComponent<LayoutElement>();
                        sle.preferredHeight = spacerH;
                        sle.minHeight = spacerH;
                        sle.preferredWidth = 0f;
                        sle.minWidth = 0f;
                        sle.flexibleWidth = 0f;
                        sle.flexibleHeight = 0f;
                        var simg = spacer.GetComponent<Image>();
                        if (simg != null) simg.enabled = false;
                    }
                    catch (Exception e) { Plugin.Logger.LogWarning("[MaydayKick] blui spacer le failed: " + e.Message); }
                }
            }
            else
            {
                // 没有布局组：手工摆到「退出」下面、左边缘对齐（父节点局部坐标，无需换算像素）
                orc.anchorMin = refBtn.anchorMin;
                orc.anchorMax = refBtn.anchorMax;
                orc.pivot = new Vector2(0f, 0.5f);
                float leftEdge = refBtn.anchoredPosition.x - refBtn.pivot.x * refBtn.rect.width;
                float gap = refBtn.rect.height * 0.35f;
                float refBottom = refBtn.anchoredPosition.y - refBtn.pivot.y * refBtn.rect.height;
                orc.anchoredPosition = new Vector2(leftEdge, refBottom - gap - h * 0.5f);
                orc.sizeDelta = new Vector2(w, h);
            }

            if (parentRt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(parentRt);
            Canvas.ForceUpdateCanvases();

            var oc = new Vector3[4];
            orc.GetWorldCorners(oc);
            Plugin.Logger.LogInfo("[MaydayKick] blui diag placed: rect=" + orc.rect.width + "x" + orc.rect.height
                + " sizeDelta=" + orc.sizeDelta + " anchoredPos=" + orc.anchoredPosition
                + " worldBl=(" + oc[0].x + "," + oc[0].y + ") worldTr=(" + oc[2].x + "," + oc[2].y + ")"
                + " hasGroup=" + hasGroup);
        }

        private static RectTransform FindByText(RectTransform root, string hint)
        {
            try
            {
                var host = (root.parent != null ? root.parent : root);
                var texts = host.GetComponentsInChildren<TMP_Text>(true);
                if (texts == null) return null;
                foreach (var t in texts)
                {
                    if (t == null || string.IsNullOrEmpty(t.text)) continue;
                    if (!t.text.Contains(hint)) continue;
                    var trt = t.transform.parent != null ? t.transform.parent.TryCast<RectTransform>() : null;
                    if (trt != null && trt != root && !trt.IsChildOf(root)) return trt;
                }
            }
            catch { }
            return null;
        }

        private static void ScreenRect(RectTransform rt, Camera cam, out Vector2 bottomLeft, out Vector2 topRight)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            bottomLeft = RectTransformUtility.WorldToScreenPoint(cam, c[0]);
            topRight = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
        }

        private static RectTransform FindMenuButton(RectTransform root, string textHint)
        {
            try
            {
                var hosts = (root.parent != null ? root.parent : root).GetComponentsInChildren<UIButton>(true);
                if (hosts == null) return null;
                foreach (var b in hosts)
                {
                    if (b == null) continue;
                    var r = b.GetComponent<RectTransform>();
                    if (r == null || root != null && r.IsChildOf(root)) continue;
                    var t = b.GetComponentInChildren<TMP_Text>(true);
                    if (t == null || string.IsNullOrEmpty(t.text)) continue;
                    if (t.text.Contains(textHint)) return r;
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[MaydayKick] blui find button failed: " + e.Message);
            }
            return null;
        }

        /// <summary>UIPanel 在 interop 里没有继承 Component，拿不到 RectTransform；自己用 Rect + Image 搭同样效果。</summary>
        private static RectTransform MakePanel(Transform parent, string name, Color color, bool raycast)
        {
            var rt = UIF.Rect(name, parent);
            if (rt == null) return null;
            var img = rt.gameObject.AddComponent<Image>();
            if (img != null)
            {
                img.color = color;
                img.raycastTarget = raycast;
            }
            return rt;
        }

        private static void BuildOverlay(RectTransform host)
        {
            Plugin.Logger.LogInfo("[MaydayKick] blui ov1 start");
            var brt = MakePanel(host, "MaydayBlBackdrop", new Color(0.05f, 0.07f, 0.1f, 0.62f), true);
            Plugin.Logger.LogInfo("[MaydayKick] blui ov2 backdrop " + (brt == null ? "null" : "ok"));
            if (brt == null) return;
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
            UIF.IgnoreLayout(brt.gameObject);
            overlay = brt.gameObject;

            var prt = MakePanel(brt, "MaydayBlPanel", PanelColor, true);
            if (prt == null) return;
            UIF.At(prt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(940f, 660f));

            UIF.Text(prt, "Title", "黑名单管理", 34f, TextColor, TextAlignmentOptions.Left, true, false);
            var trt = prt.Find("Title") != null ? prt.Find("Title").TryCast<RectTransform>() : null;
            if (trt != null)
            {
                trt.anchorMin = new Vector2(0f, 1f);
                trt.anchorMax = new Vector2(0f, 1f);
                trt.pivot = new Vector2(0f, 1f);
                trt.anchoredPosition = new Vector2(32f, -28f);
                trt.sizeDelta = new Vector2(520f, 46f);
                UIF.IgnoreLayout(trt.gameObject);
            }

            countText = UIF.Text(prt, "Count", "", 20f, SubColor, TextAlignmentOptions.Left, true, false);
            var crt = countText != null ? countText.GetComponent<RectTransform>() : null;
            if (crt != null)
            {
                crt.anchorMin = new Vector2(0f, 1f);
                crt.anchorMax = new Vector2(0f, 1f);
                crt.pivot = new Vector2(0f, 1f);
                crt.anchoredPosition = new Vector2(32f, -78f);
                crt.sizeDelta = new Vector2(560f, 26f);
                UIF.IgnoreLayout(crt.gameObject);
            }

            var closeAction = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new System.Action(Close));
            var close = UIF.IconButton(prt, "Close", GlyphId.Cross, closeAction, ButtonStyle.Ghost, 44f);
            if (close != null)
            {
                var xrc = close.GetComponent<RectTransform>();
                if (xrc != null)
                {
                    xrc.anchorMin = new Vector2(1f, 1f);
                    xrc.anchorMax = new Vector2(1f, 1f);
                    xrc.pivot = new Vector2(1f, 1f);
                    xrc.anchoredPosition = new Vector2(-24f, -24f);
                    UIF.IgnoreLayout(xrc.gameObject);
                }
            }

            Plugin.Logger.LogInfo("[MaydayKick] blui ov3 title/count/close ok");
            RectTransform listContent;
            var scroll = UIF.Scroll(prt, "BlScroll", out listContent, 8f, UIF.Pad(10));
            Plugin.Logger.LogInfo("[MaydayKick] blui ov4 scroll " + (scroll == null ? "null" : "ok"));
            if (scroll != null)
            {
                var srt = scroll.GetComponent<RectTransform>();
                if (srt != null)
                {
                    srt.anchorMin = Vector2.zero;
                    srt.anchorMax = Vector2.one;
                    srt.offsetMin = new Vector2(20f, 24f);
                    srt.offsetMax = new Vector2(-20f, -112f);
                    UIF.IgnoreLayout(srt.gameObject);
                }
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.scrollSensitivity = 32f;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
                scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
                scrollRef = scroll;
                content = listContent;
                if (content != null)
                {
                    // UIF.Scroll 给 content 挂的布局组/Fitter 会把高度按「子项首选高度」重算，
                    // 而行都挂了 IgnoreLayout（高度算成 0）→ content 高度被压成 0 →
                    // ScrollRect 的 scrollRange = 0 → 滚动条变成摆设、滚轮也滚不动。
                    // 这里直接拆掉它们，改用我们自己算的绝对高度。
                    var vlg = content.GetComponent<VerticalLayoutGroup>();
                    if (vlg != null) { UnityEngine.Object.Destroy(vlg); Plugin.Logger.LogInfo("[MaydayKick] blui removed VerticalLayoutGroup on content"); }
                    var fitter = content.GetComponent<ContentSizeFitter>();
                    if (fitter != null) { UnityEngine.Object.Destroy(fitter); Plugin.Logger.LogInfo("[MaydayKick] blui removed ContentSizeFitter on content"); }

                    content.anchorMin = new Vector2(0f, 1f);
                    content.anchorMax = new Vector2(1f, 1f);
                    content.pivot = new Vector2(0.5f, 1f);
                    content.anchoredPosition = Vector2.zero;
                    content.sizeDelta = new Vector2(0f, 0f);
                }

                var vp = scroll.viewport;
                Plugin.Logger.LogInfo("[MaydayKick] blui scroll diag viewport=" + (vp == null ? "null" : vp.name + " " + vp.rect.width + "x" + vp.rect.height)
                    + " content=" + (content == null ? "null" : content.rect.width + "x" + content.rect.height)
                    + " vbar=" + (scroll.verticalScrollbar == null ? "null" : scroll.verticalScrollbar.name));
            }
        }

        public static void Open()
        {
            try
            {
                if (overlay == null) return;
                overlay.transform.SetAsLastSibling();
                overlay.SetActive(true);
                Refresh();
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[MaydayKick] blacklist open failed: " + e.Message);
            }
        }

        public static void Close()
        {
            try
            {
                if (overlay != null) overlay.SetActive(false);
            }
            catch { }
        }

        private static void ClearRows()
        {
            foreach (var r in rows)
            {
                if (r != null) UnityEngine.Object.Destroy(r);
            }
            rows.Clear();
        }

        public static void Refresh()
        {
            try
            {
                if (content == null) return;
                ClearRows();

                var list = Blacklist.Snapshot();
                if (countText != null)
                {
                    countText.Set(list.Count == 0
                        ? "当前没有拉黑任何人。被拉黑的人会以 SteamID 的形式存在 BepInEx\\config\\MaydayKick\\blacklist.txt"
                        : "共 " + list.Count + " 条，点「移除」即可解封（立即生效，不用重启）");
                }

                const float rowH = 76f;
                const float step = 84f;

                for (int i = 0; i < list.Count; i++)
                {
                    var entry = list[i];
                    var rowRt = UIF.Rect(RowPrefix + i, content);
                    if (rowRt == null) continue;
                    rowRt.anchorMin = new Vector2(0f, 1f);
                    rowRt.anchorMax = new Vector2(1f, 1f);
                    rowRt.pivot = new Vector2(0.5f, 1f);
                    rowRt.sizeDelta = new Vector2(0f, rowH);
                    rowRt.anchoredPosition = new Vector2(0f, -(i * step));
                    UIF.IgnoreLayout(rowRt.gameObject);
                    rows.Add(rowRt.gameObject);

                    var brc = MakePanel(rowRt, "Bg", new Color(1f, 1f, 1f, 0.92f), false);
                    if (brc != null)
                    {
                        brc.anchorMin = Vector2.zero;
                        brc.anchorMax = Vector2.one;
                        brc.offsetMin = Vector2.zero;
                        brc.offsetMax = Vector2.zero;
                        UIF.IgnoreLayout(brc.gameObject);
                    }

                    string label = string.IsNullOrEmpty(entry.Value) ? "(未知玩家)" : entry.Value;
                    var nm = UIF.Text(rowRt, "Name", label, 24f, TextColor, TextAlignmentOptions.MidlineLeft, true, false);
                    if (nm != null)
                    {
                        var nrc = nm.GetComponent<RectTransform>();
                        nrc.anchorMin = new Vector2(0f, 0f);
                        nrc.anchorMax = new Vector2(1f, 1f);
                        nrc.offsetMin = new Vector2(24f, 22f);
                        nrc.offsetMax = new Vector2(-180f, -18f);
                        UIF.IgnoreLayout(nm.gameObject);
                    }

                    var idText = UIF.Text(rowRt, "SteamId", entry.Key.ToString(), 18f, SubColor, TextAlignmentOptions.MidlineLeft, true, false);
                    if (idText != null)
                    {
                        var irc = idText.GetComponent<RectTransform>();
                        irc.anchorMin = new Vector2(0f, 0f);
                        irc.anchorMax = new Vector2(0f, 0f);
                        irc.pivot = new Vector2(0f, 0f);
                        irc.anchoredPosition = new Vector2(24f, 8f);
                        irc.sizeDelta = new Vector2(360f, 22f);
                        UIF.IgnoreLayout(idText.gameObject);
                    }

                    ulong targetId = entry.Key;
                    string targetName = label;
                    var removeAction = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new System.Action(delegate
                    {
                        try
                        {
                            Blacklist.Remove(targetId);
                            Plugin.Logger.LogInfo("[MaydayKick] blacklist ui removed " + targetName + " (" + targetId + ")");
                            Refresh();
                        }
                        catch (Exception e)
                        {
                            Plugin.Logger.LogWarning("[MaydayKick] blacklist remove failed: " + e.Message);
                        }
                    }));

                    var removeBtn = UIF.Button(rowRt, "Remove", "移除", removeAction, ButtonStyle.Danger,
                        108f, 44f, new Il2CppSystem.Nullable<GlyphId>(), 22f, 10f);
                    if (removeBtn != null)
                    {
                        var rrc = removeBtn.GetComponent<RectTransform>();
                        if (rrc != null)
                        {
                            rrc.anchorMin = new Vector2(1f, 0.5f);
                            rrc.anchorMax = new Vector2(1f, 0.5f);
                            rrc.pivot = new Vector2(1f, 0.5f);
                            rrc.anchoredPosition = new Vector2(-14f, 0f);
                            rrc.sizeDelta = new Vector2(108f, 44f);
                        }
                    }
                }

                if (content != null)
                {
                    float need = list.Count * step + 12f;
                    content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(need, 10f));
                }

                Canvas.ForceUpdateCanvases();
                if (scrollRef != null)
                {
                    scrollRef.StopMovement();
                    scrollRef.verticalNormalizedPosition = 1f;
                    var vp2 = scrollRef.viewport;
                    Plugin.Logger.LogInfo("[MaydayKick] blui scroll diag after refresh rows=" + list.Count
                        + " contentH=" + (content == null ? -1f : content.rect.height)
                        + " viewportH=" + (vp2 == null ? -1f : vp2.rect.height)
                        + " contentAnchors=" + content.anchorMin + "/" + content.anchorMax
                        + " contentPos=" + content.anchoredPosition);
                }
                if (content != null) LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[MaydayKick] blacklist refresh failed: " + e.Message);
            }
        }
    }
}