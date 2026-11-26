using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Mayday.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MaydayKick
{
    internal static class WhitelistUi
    {
        private const string RootName = "MaydayWhitelistRoot";
        private const string OpenButtonName = "MaydayWhitelistOpen";
        private const string BlacklistButtonName = "MaydayBlOpen";

        private static GameObject overlay;
        private static RectTransform content;
        private static ScrollRect scrollRef;
        private static UIText countText;
        private static readonly List<GameObject> rows = new List<GameObject>();

        private static Color PanelColor { get { return new Color(0.968f, 0.945f, 0.894f, 1f); } }
        private static Color TextColor { get { return new Color(0.13f, 0.19f, 0.31f, 1f); } }
        private static Color SubColor { get { return new Color(0.35f, 0.4f, 0.5f, 1f); } }

        internal static void Attach(MainMenuScreen screen)
        {
            if (screen == null) return;

            try
            {
                var host = screen.GetComponent<RectTransform>();
                if (host == null || host.Find(RootName) != null) return;

                var rt = UIF.Rect(RootName, host);
                if (rt == null) return;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                UIF.IgnoreLayout(rt.gameObject);
                overlay = rt.gameObject;
                BuildOverlay(rt);
                overlay.SetActive(false);

                var blacklistButton = FindButton(host, BlacklistButtonName);
                var buttonParent = blacklistButton != null
                    ? blacklistButton.transform.parent
                    : null;
                if (buttonParent == null)
                {
                    Plugin.Logger.LogWarning("[MaydayKick] blacklist menu button not found; cannot place whitelist button");
                    return;
                }

                if (buttonParent.Find(OpenButtonName) != null) return;

                var blacklistRect = blacklistButton.GetComponent<RectTransform>();
                float width = blacklistRect != null && blacklistRect.rect.width > 20f
                    ? blacklistRect.rect.width
                    : 220f;
                float height = blacklistRect != null && blacklistRect.rect.height > 20f
                    ? blacklistRect.rect.height
                    : 64f;

                var openAction = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(
                    new System.Action(Open));
                var open = UIF.Button(buttonParent, OpenButtonName, "白名单",
                    openAction, ButtonStyle.Secondary, width, height,
                    new Il2CppSystem.Nullable<GlyphId>(), 26f, 14f);
                if (open == null)
                {
                    Plugin.Logger.LogWarning("[MaydayKick] whitelist open button creation failed");
                    return;
                }

                open.transform.SetSiblingIndex(blacklistButton.transform.GetSiblingIndex() + 1);

                var openRt = open.GetComponent<RectTransform>();
                var parentRect = buttonParent.TryCast<RectTransform>();
                bool hasLayout = parentRect != null && parentRect.GetComponent<LayoutGroup>() != null;
                if (openRt != null && hasLayout)
                {
                    openRt.anchorMin = blacklistRect.anchorMin;
                    openRt.anchorMax = blacklistRect.anchorMax;
                    openRt.pivot = blacklistRect.pivot;
                    openRt.anchoredPosition = blacklistRect.anchoredPosition;
                    openRt.sizeDelta = new Vector2(width, height);

                    var le = openRt.gameObject.GetComponent<LayoutElement>();
                    if (le == null) le = openRt.gameObject.AddComponent<LayoutElement>();
                    le.preferredWidth = width;
                    le.preferredHeight = height;
                    le.minHeight = height;
                    le.flexibleWidth = 0f;
                    le.flexibleHeight = 0f;
                }
                else if (openRt != null && blacklistRect != null)
                {
                    openRt.anchorMin = blacklistRect.anchorMin;
                    openRt.anchorMax = blacklistRect.anchorMax;
                    openRt.pivot = blacklistRect.pivot;
                    openRt.anchoredPosition = blacklistRect.anchoredPosition -
                        new Vector2(0f, height + 12f);
                    openRt.sizeDelta = blacklistRect.sizeDelta;
                }

                Plugin.Logger.LogInfo("[MaydayKick] main menu whitelist manager attached");
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("[MaydayKick] whitelist ui build failed: " + e);
            }
        }

        private static UIButton FindButton(RectTransform host, string buttonName)
        {
            var buttons = host.GetComponentsInChildren<UIButton>(true);
            if (buttons == null) return null;

            foreach (var button in buttons)
            {
                if (button != null && button.name == buttonName)
                    return button;
            }
            return null;
        }

        private static RectTransform MakePanel(Transform parent, string name, Color color, bool raycast)
        {
            var rt = UIF.Rect(name, parent);
            if (rt == null) return null;

            var image = rt.gameObject.AddComponent<Image>();
            if (image != null)
            {
                image.color = color;
                image.raycastTarget = raycast;
            }
            return rt;
        }

        private static void BuildOverlay(RectTransform host)
        {
            var backdrop = host.GetComponent<Image>();
            if (backdrop == null) backdrop = host.gameObject.AddComponent<Image>();
            backdrop.color = new Color(0.05f, 0.07f, 0.1f, 0.62f);
            backdrop.raycastTarget = true;

            var panel = MakePanel(host, "Panel", PanelColor, true);
            if (panel == null) return;
            UIF.IgnoreLayout(panel.gameObject);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(900f, 620f);

            var title = UIF.Text(panel, "Title", "白名单管理", 34f, TextColor,
                TextAlignmentOptions.Left, true, false);
            PlaceHeaderText(title, new Vector2(32f, -28f), new Vector2(520f, 46f));

            countText = UIF.Text(panel, "Count", "", 20f, SubColor,
                TextAlignmentOptions.Left, true, false);
            PlaceHeaderText(countText, new Vector2(32f, -78f), new Vector2(700f, 28f));

            var closeAction = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(
                new System.Action(Close));
            var close = UIF.IconButton(panel, "Close", GlyphId.Cross, closeAction,
                ButtonStyle.Ghost, 44f);
            if (close != null)
            {
                var closeRt = close.GetComponent<RectTransform>();
                if (closeRt != null)
                {
                    closeRt.anchorMin = new Vector2(1f, 1f);
                    closeRt.anchorMax = new Vector2(1f, 1f);
                    closeRt.pivot = new Vector2(1f, 1f);
                    closeRt.anchoredPosition = new Vector2(-24f, -24f);
                    UIF.IgnoreLayout(closeRt.gameObject);
                }
            }

            RectTransform listContent;
            var scroll = UIF.Scroll(panel, "WhitelistScroll", out listContent, 8f, UIF.Pad(10));
            if (scroll == null) return;

            var scrollRt = scroll.GetComponent<RectTransform>();
            if (scrollRt != null)
            {
                scrollRt.anchorMin = Vector2.zero;
                scrollRt.anchorMax = Vector2.one;
                scrollRt.offsetMin = new Vector2(20f, 24f);
                scrollRt.offsetMax = new Vector2(-20f, -112f);
                UIF.IgnoreLayout(scrollRt.gameObject);
            }
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 32f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            scrollRef = scroll;
            content = listContent;
            if (content == null) return;

            var layout = content.GetComponent<VerticalLayoutGroup>();
            if (layout != null) UnityEngine.Object.Destroy(layout);
            var fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter != null) UnityEngine.Object.Destroy(fitter);

            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
        }

        private static void PlaceHeaderText(UIText text, Vector2 position, Vector2 size)
        {
            if (text == null) return;
            var rt = text.GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            UIF.IgnoreLayout(rt.gameObject);
        }

        private static void Open()
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
                Plugin.Logger.LogWarning("[MaydayKick] whitelist open failed: " + e.Message);
            }
        }

        private static void Close()
        {
            if (overlay != null) overlay.SetActive(false);
        }

        internal static void RefreshIfOpen()
        {
            if (overlay != null && overlay.activeSelf) Refresh();
        }

        private static void ClearRows()
        {
            foreach (var row in rows)
            {
                if (row != null) UnityEngine.Object.Destroy(row);
            }
            rows.Clear();
        }

        private static void Refresh()
        {
            try
            {
                if (content == null) return;
                ClearRows();

                var entries = Whitelist.Snapshot();
                if (countText != null)
                {
                    countText.Set(entries.Count == 0
                        ? "当前没有白名单成员。文件：BepInEx\\config\\MaydayKick\\whitelist.txt"
                        : "共 " + entries.Count + " 条；白名单成员不会被自动过滤或踢出");
                }

                const float rowHeight = 76f;
                const float rowStep = 84f;
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var row = UIF.Rect("WhitelistRow_" + i, content);
                    if (row == null) continue;

                    row.anchorMin = new Vector2(0f, 1f);
                    row.anchorMax = new Vector2(1f, 1f);
                    row.pivot = new Vector2(0.5f, 1f);
                    row.sizeDelta = new Vector2(0f, rowHeight);
                    row.anchoredPosition = new Vector2(0f, -(i * rowStep));
                    UIF.IgnoreLayout(row.gameObject);
                    rows.Add(row.gameObject);

                    var background = MakePanel(row, "Bg", new Color(1f, 1f, 1f, 0.92f), false);
                    if (background != null)
                    {
                        background.anchorMin = Vector2.zero;
                        background.anchorMax = Vector2.one;
                        background.offsetMin = Vector2.zero;
                        background.offsetMax = Vector2.zero;
                        UIF.IgnoreLayout(background.gameObject);
                    }

                    string name = string.IsNullOrEmpty(entry.Name) ? "(未知玩家)" : entry.Name;
                    if (!string.IsNullOrEmpty(entry.Note)) name += "  -  " + entry.Note;
                    var nameText = UIF.Text(row, "Name", name, 23f, TextColor,
                        TextAlignmentOptions.MidlineLeft, true, false);
                    if (nameText != null)
                    {
                        var nameRt = nameText.GetComponent<RectTransform>();
                        nameRt.anchorMin = Vector2.zero;
                        nameRt.anchorMax = Vector2.one;
                        nameRt.offsetMin = new Vector2(24f, 22f);
                        nameRt.offsetMax = new Vector2(-150f, -18f);
                        UIF.IgnoreLayout(nameText.gameObject);
                    }

                    var idText = UIF.Text(row, "SteamId", entry.SteamId.ToString(), 18f, SubColor,
                        TextAlignmentOptions.MidlineLeft, true, false);
                    if (idText != null)
                    {
                        var idRt = idText.GetComponent<RectTransform>();
                        idRt.anchorMin = new Vector2(0f, 0f);
                        idRt.anchorMax = new Vector2(0f, 0f);
                        idRt.pivot = new Vector2(0f, 0f);
                        idRt.anchoredPosition = new Vector2(24f, 8f);
                        idRt.sizeDelta = new Vector2(360f, 22f);
                        UIF.IgnoreLayout(idText.gameObject);
                    }

                    ulong targetId = entry.SteamId;
                    var removeAction = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(
                        new System.Action(delegate
                        {
                            if (Whitelist.Remove(targetId))
                                Plugin.Logger.LogInfo("[MaydayKick] whitelist ui removed " + targetId);
                            Refresh();
                        }));
                    var remove = UIF.Button(row, "Remove", "移除", removeAction,
                        ButtonStyle.Danger, 108f, 44f,
                        new Il2CppSystem.Nullable<GlyphId>(), 22f, 10f);
                    if (remove != null)
                    {
                        var removeRt = remove.GetComponent<RectTransform>();
                        if (removeRt != null)
                        {
                            removeRt.anchorMin = new Vector2(1f, 0.5f);
                            removeRt.anchorMax = new Vector2(1f, 0.5f);
                            removeRt.pivot = new Vector2(1f, 0.5f);
                            removeRt.anchoredPosition = new Vector2(-14f, 0f);
                            removeRt.sizeDelta = new Vector2(108f, 44f);
                        }
                    }
                }

                content.sizeDelta = new Vector2(content.sizeDelta.x,
                    Mathf.Max(entries.Count * rowStep + 12f, 10f));
                Canvas.ForceUpdateCanvases();
                if (scrollRef != null)
                {
                    scrollRef.StopMovement();
                    scrollRef.verticalNormalizedPosition = 1f;
                    LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[MaydayKick] whitelist refresh failed: " + e.Message);
            }
        }
    }
}
