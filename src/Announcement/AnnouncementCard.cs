using System;
using Mayday.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MaydayAnnouncement
{
    internal static class AnnouncementCard
    {
        private const float DisplaySeconds = 3f;
        private const float FadeOutSeconds = 0.2f;
        private const float CardWidth = 780f;
        private const float CardHeight = 320f;
        private const float Margin = 56f;
        private const float Inner = CardWidth - Margin * 2f;

        private static readonly Vector2 TopLeft = UIF.TopLeft;
        private static readonly Vector2 Center = UIF.Center;

        private static GameObject _root;
        private static CanvasGroup _group;
        private static RectTransform _card;
        private static UIText _tag;
        private static UIText _title;
        private static UIText _message;
        private static float _showAt = -1f;
        private static float _hideAt = -1f;
        private static float _hiddenAt = -1f;
        private static bool _sessionShown;

        internal static void OnSceneLoaded(string sceneName)
        {
            if (!IsGameScene(sceneName))
            {
                _sessionShown = false;
                _showAt = -1f;
                _hideAt = -1f;
                _hiddenAt = -1f;
                Hide();
                return;
            }

            if (_sessionShown)
                return;

            _sessionShown = true;
            if (string.IsNullOrWhiteSpace(Plugin.AnnouncementText.Value))
                return;

            _showAt = Time.unscaledTime + 0.5f;
        }

        internal static void OnSessionEnded()
        {
            _sessionShown = false;
            _showAt = -1f;
            _hideAt = -1f;
            _hiddenAt = -1f;
            Hide();
        }

        internal static void Tick()
        {
            try
            {
                TickCore();
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError("[MaydayAnnouncement] 公告卡更新失败: " + e);
                _showAt = -1f;
                _hideAt = -1f;
                _hiddenAt = -1f;
                Hide();
            }
        }

        private static void TickCore()
        {
            float now = Time.unscaledTime;

            if (_showAt >= 0f && now >= _showAt)
            {
                if (Show())
                {
                    _showAt = -1f;
                }
                else
                {
                    _showAt = now + 0.5f;
                }
            }

            if (_hideAt >= 0f && now >= _hideAt)
            {
                _hideAt = -1f;
                _hiddenAt = now + FadeOutSeconds + 0.05f;
                if (_group != null)
                    UITween.Fade(_group, 0f, FadeOutSeconds, Ease.OutQuad);
                else
                    Hide();
            }

            if (_hiddenAt >= 0f && now >= _hiddenAt)
            {
                _hiddenAt = -1f;
                Hide();
            }
        }

        private static bool Show()
        {
            if (string.IsNullOrWhiteSpace(Plugin.AnnouncementText.Value))
                return true;

            if (!EnsureBuilt())
                return false;

            Refresh();
            _root.SetActive(true);
            _card.SetAsLastSibling();
            _hiddenAt = -1f;

            UITween.Kill(_card);
            if (_group != null)
            {
                _group.alpha = 0f;
                UITween.Fade(_group, 1f, UITheme.AnimOpen, Ease.OutQuad);
            }
            UITween.ScaleFrom(_card, new Vector3(0.93f, 0.93f, 1f), Vector3.one, 0.28f, Ease.OutBack);

            _hideAt = Time.unscaledTime + DisplaySeconds;
            Plugin.LogSource.LogInfo("[MaydayAnnouncement] 房间公告已显示");
            return true;
        }

        private static bool EnsureBuilt()
        {
            if (_root != null)
                return true;

            var ui = global::Mayday.UI.UIRoot.Instance;
            if (ui == null)
                return false;

            Transform parent = null;
            try
            {
                parent = ui.PopupLayer;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayAnnouncement] 无法获取弹层: " + e.Message);
            }

            if (parent == null)
                parent = ui.transform;

            try
            {
                var panel = UIF.Panel(parent, "MaydayAnnouncementCard", UITheme.Paper, UITheme.RadiusLarge, true, false);
                if (panel == null || panel.Root == null)
                    return false;

                _card = panel.Root;
                _root = _card.gameObject;
                UIF.At(_card, Center, new Vector2(0f, 60f), new Vector2(CardWidth, CardHeight));

                _group = TryAddComponent<CanvasGroup>(_root);
                if (_group != null)
                {
                    _group.alpha = 0f;
                    _group.interactable = false;
                    _group.blocksRaycasts = false;
                }

                BuildContent();
                ApplyFont();
                Hide();
                return true;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError("[MaydayAnnouncement] 创建公告卡失败: " + e);
                Reset();
                return false;
            }
        }

        private static void BuildContent()
        {
            var stripe = UIF.Round(_card, "Stripe", UITheme.Orange, 3f);
            UIF.At(stripe.rectTransform, TopLeft, new Vector2(Margin, -16f), new Vector2(Inner, 6f));

            _tag = UIF.Text(_card, "Tag", "", UITheme.SizeTiny, UITheme.TealDk, TextAlignmentOptions.Left, false, false);
            UIF.At(_tag.Rect, TopLeft, new Vector2(Margin, -40f), new Vector2(Inner - 60f, 26f));

            _title = UIF.Text(_card, "Title", "房间公告", UITheme.SizeHeader, UITheme.Ink, TextAlignmentOptions.Left, false, false);
            UIF.At(_title.Rect, TopLeft, new Vector2(Margin, -76f), new Vector2(Inner, 56f));

            var divider = UIF.Divider(_card, UITheme.InkFaint, 2f);
            if (divider != null)
                UIF.At(divider.rectTransform, TopLeft, new Vector2(Margin, -142f), new Vector2(Inner, 2f));

            _message = UIF.Text(_card, "Message", "", UITheme.SizeBody, UITheme.InkSoft,
                TextAlignmentOptions.TopLeft, false, true);
            UIF.At(_message.Rect, TopLeft, new Vector2(Margin, -164f), new Vector2(Inner, CardHeight - 190f));
        }

        private static void Refresh()
        {
            _tag.Text = "MAYDAY ANNOUNCEMENT   ·   v" + Plugin.PluginVersion;
            string text = Plugin.AnnouncementText.Value;
            _message.Text = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
        }

        private static bool IsGameScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
                return false;

            try
            {
                string path = global::Mayday.Net.MaydayNetworkManager.GameScenePath;
                if (!string.IsNullOrEmpty(path))
                {
                    int slash = path.LastIndexOfAny(new[] { '/', '\\' });
                    string leaf = slash >= 0 ? path.Substring(slash + 1) : path;
                    if (leaf.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                        leaf = leaf.Substring(0, leaf.Length - 6);
                    return sceneName == leaf;
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayAnnouncement] 无法读取游戏场景名，使用默认场景名判断: " + e.Message);
            }

            return sceneName == "Game";
        }

        private static T TryAddComponent<T>(GameObject go) where T : Component
        {
            try
            {
                var existing = go.GetComponent<T>();
                if (existing != null)
                    return existing;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayAnnouncement] 读取 " + typeof(T).Name + " 组件失败: " + e.Message);
            }

            try
            {
                var added = go.AddComponent(Il2CppInterop.Runtime.Il2CppType.Of<T>());
                return added != null ? added.TryCast<T>() : null;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayAnnouncement] 添加 " + typeof(T).Name + " 组件失败: " + e.Message);
                return null;
            }
        }

        private static void ApplyFont()
        {
            try
            {
                if (_message.Tmp.font != null && _message.Tmp.font.HasCharacter('公', true, false))
                {
                    return;
                }

                var fonts = LocFonts.cjk;
                if (fonts != null)
                {
                    for (int i = 0; i < fonts.Count; i++)
                    {
                        var font = fonts[i];
                        if (font == null || !font.HasCharacter('公', true, false))
                            continue;

                        _tag.Tmp.font = font;
                        _title.Tmp.font = font;
                        _message.Tmp.font = font;
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayAnnouncement] 中文字体初始化失败: " + e.Message);
            }

            Plugin.LogSource.LogWarning("[MaydayAnnouncement] 未找到中文字体，公告中的中文可能无法显示");
        }

        private static void Hide()
        {
            if (_root != null)
                _root.SetActive(false);
        }

        private static void Reset()
        {
            _root = null;
            _group = null;
            _card = null;
            _tag = null;
            _title = null;
            _message = null;
        }
    }
}
