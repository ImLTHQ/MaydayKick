using System;
using System.Collections.Generic;
using Mayday;
using Mayday.Airport;
using Mayday.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaveManager;

internal static class SaveMenuUI
{
    private static UIButton _pauseSaveBtn;
    private static PauseScreen _pauseScreen;

    private static GameObject _modalRoot;
    private static TMP_InputField _nameInput;
    private static UIButton _createBtn;
    private static UIText _statusText;
    private static RectTransform _listContent;

    private static Il2CppSystem.Nullable<GlyphId> NoGlyph() => new Il2CppSystem.Nullable<GlyphId>();
    private static Il2CppSystem.Nullable<GlyphId> WithGlyph(GlyphId g) => new Il2CppSystem.Nullable<GlyphId>(g);

    public static void AttachToPauseScreen(PauseScreen screen)
    {
        if (screen == null || screen.inviteBtn == null) return;
        _pauseScreen = screen;

        Transform parent = screen.inviteBtn.transform.parent;
        if (parent == null) return;

        Transform existing = parent.Find("SaveManagerBtn");
        if (existing != null)
        {
            _pauseSaveBtn = existing.GetComponent<UIButton>();
            RefreshPauseButtonState();
            return;
        }

        RectTransform inviteRt = screen.inviteBtn.GetComponent<RectTransform>();
        float width = inviteRt != null && inviteRt.sizeDelta.x > 10f ? inviteRt.sizeDelta.x : 260f;
        float height = inviteRt != null && inviteRt.sizeDelta.y > 10f ? inviteRt.sizeDelta.y : UITheme.ButtonHeight;

        var inviteTmp = screen.inviteBtn.GetComponentInChildren<TMP_Text>();
        float fontSize = inviteTmp != null && inviteTmp.fontSize > 6f ? inviteTmp.fontSize : UITheme.SizeBody;

        _pauseSaveBtn = UIF.Button(
            parent,
            "SaveManagerBtn",
            "存档 / 载入",
            (Action)OnPauseSaveClicked,
            ButtonStyle.Secondary,
            width,
            height,
            WithGlyph(GlyphId.Box),
            fontSize,
            UITheme.Radius
        );

        if (_pauseSaveBtn != null)
        {
            int inviteIdx = screen.inviteBtn.transform.GetSiblingIndex();
            _pauseSaveBtn.transform.SetSiblingIndex(inviteIdx + 1);
            _pauseSaveBtn.ApplyNow(true);
        }

        RefreshPauseButtonState();
    }

    public static void RefreshPauseButtonState()
    {
        if (_pauseSaveBtn == null) return;

        bool canUse = SaveService.IsHostOrSolo;
        _pauseSaveBtn.SetInteractable(canUse);

        var label = _pauseSaveBtn.GetComponentInChildren<TMP_Text>();
        if (label != null)
        {
            label.text = canUse ? "存档 / 载入" : "存档 / 载入 (仅房主)";
        }

        _pauseSaveBtn.ApplyNow(true);
    }

    public static void CloseModal()
    {
        if (_modalRoot != null)
        {
            _modalRoot.SetActive(false);
        }
    }

    private static void OnPauseSaveClicked()
    {
        bool isHost = SaveService.IsHostOrSolo;
        var gs = GameState.Instance;
        var gd = GameDirector.Instance;
        Plugin.Logger?.LogInfo(
            $"[UI] 点击暂停菜单 '存档/载入' (IsHostOrSolo={isHost}, IsLoading={SaveLoadController.IsLoading}, Phase={gs?.Phase.ToString() ?? "null"}, Stage={gd?.stage.ToString() ?? "null"}, Leg={gs?.Leg ?? -1}, Money={gs?.Money ?? -1}, Airport={gs?.CurrentAirport ?? -1}, Upgrades={gs?.Upgrades?.Count ?? 0})。");

        if (!isHost)
        {
            UIRoot.Instance?.Toast("仅房主可使用存档/载入功能", NotifyKind.Warning);
            return;
        }

        EnsureModalBuilt();
        RefreshModal();
        _modalRoot.SetActive(true);
        _modalRoot.transform.SetAsLastSibling();
    }

    private static void EnsureModalBuilt()
    {
        if (_modalRoot != null) return;

        Transform host = _pauseScreen != null ? _pauseScreen.transform : UIRoot.Instance?.transform;
        if (host == null) return;

        var rootGo = new GameObject("SaveManagerModal");
        var rootRt = rootGo.AddComponent<RectTransform>();
        rootRt.SetParent(host, false);
        UIF.Stretch(rootRt, 0f, 0f, 0f, 0f);

        UIF.Dimmer(rootRt, 0.55f);

        // 使用原版纸张配色 UITheme.Paper 与圆角 UITheme.RadiusLarge
        var panel = UIF.Panel(
            rootRt,
            "SavePanel",
            UITheme.Paper,
            UITheme.RadiusLarge,
            true,
            true
        );
        var panelRt = panel.Root;
        Vector2 modalSize = ResolvePauseCardSize();
        UIF.At(panelRt, new Vector2(0.5f, 0.5f), Vector2.zero, modalSize);

        // 注意：controlH=true, expandH=false，由子元素 LayoutElement 决定各自高度，ScrollRect 使用 flexH=1 占满剩余空间
        UIF.VLayout(panelRt.gameObject, 12f, UIF.Pad(28, 28, 24, 24), TextAnchor.UpperCenter, true, true, true, false);

        // 标题栏
        var header = UIF.Row(panelRt, "Header", 40f, 10f, UIF.Pad(0));
        UIF.LE(header.gameObject, 0f, 40f, 1f, 0f, 0f, 40f);

        var title = UIF.Text(header, "Title", "存档 / 载入", UITheme.SizeHeader, UITheme.Ink, TextAlignmentOptions.Left, true, false);
        UIF.LE(title.gameObject, 0f, 40f, 1f, 0f, 0f, 40f);

        var closeBtn = UIF.Button(
            header,
            "CloseBtn",
            "关闭",
            (Action)CloseModal,
            ButtonStyle.Ghost,
            88f,
            36f,
            WithGlyph(GlyphId.Cross),
            UITheme.SizeSmall,
            UITheme.RadiusSmall
        );
        closeBtn?.ApplyNow(true);

        var div1 = UIF.Divider(panelRt, UITheme.InkFaint, 1.5f);
        UIF.LE(div1.gameObject, 0f, 1.5f, 1f, 0f, 0f, 1.5f);

        // 状态提示
        _statusText = UIF.Text(panelRt, "StatusText", "", UITheme.SizeSmall, UITheme.InkSoft, TextAlignmentOptions.Left, false, true);
        UIF.LE(_statusText.gameObject, 0f, 22f, 1f, 0f, 0f, 22f);

        // 新建存档行
        var createRow = UIF.Row(panelRt, "CreateRow", 42f, 10f, UIF.Pad(0));
        UIF.LE(createRow.gameObject, 0f, 42f, 1f, 0f, 0f, 42f);

        _nameInput = UIF.Input(
            createRow,
            "SaveNameInput",
            string.Empty,
            "输入存档名称（留空则自动按航段与时间命名）",
            40,
            (Action<string>)(_ => { }),
            480f,
            40f
        );
        if (_nameInput != null)
        {
            UIF.LE(_nameInput.gameObject, 0f, 40f, 1f, 0f, 200f, 40f);
        }

        _createBtn = UIF.Button(
            createRow,
            "CreateSaveBtn",
            "新建存档",
            (Action)OnCreateSaveClicked,
            ButtonStyle.Primary,
            148f,
            40f,
            WithGlyph(GlyphId.Check),
            UITheme.SizeBody,
            UITheme.RadiusSmall
        );
        _createBtn?.ApplyNow(true);

        var div2 = UIF.Divider(panelRt, UITheme.InkFaint, 1.5f);
        UIF.LE(div2.gameObject, 0f, 1.5f, 1f, 0f, 0f, 1.5f);

        // 存档滚动列表（flexH=1 占满剩余空间）
        RectTransform scrollContent = null;
        var scroll = UIF.Scroll(panelRt, "SaveListScroll", out scrollContent, 8f, UIF.Pad(2));
        if (scroll != null)
        {
            UIF.LE(scroll.gameObject, 0f, 280f, 1f, 1f, 0f, 160f);
        }
        _listContent = scrollContent;

        _modalRoot = rootGo;
        _modalRoot.SetActive(false);
    }

    private static Vector2 ResolvePauseCardSize()
    {
        if (_pauseScreen != null && _pauseScreen.inviteBtn != null)
        {
            Transform cur = _pauseScreen.inviteBtn.transform;
            while (cur != null && cur.parent != null)
            {
                var rt = cur.GetComponent<RectTransform>();
                var img = cur.GetComponent<Image>();
                if (rt != null && img != null)
                {
                    Vector2 s = rt.rect.size;
                    if (s.x >= 500f && s.x <= 950f && s.y >= 360f && s.y <= 620f)
                    {
                        return s;
                    }
                }
                if (cur.parent == _pauseScreen.transform) break;
                cur = cur.parent;
            }
        }

        // 与原版暂停菜单白色主卡片一致的默认尺寸
        return new Vector2(760f, 500f);
    }

    private static void RefreshModal()
    {
        if (_modalRoot == null) return;

        bool canSave = SaveService.CanSaveNow(out string saveReason);
        bool canLoad = SaveService.CanLoadNow(out string loadReason);
        Plugin.Logger?.LogInfo($"[UI] 刷新存档面板: CanSave={canSave} ({saveReason}), CanLoad={canLoad} ({loadReason})。");
        if (_createBtn != null)
        {
            _createBtn.SetInteractable(canSave);
            _createBtn.ApplyNow(true);
        }

        if (_statusText != null)
        {
            var tmp = _statusText.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                if (canSave)
                {
                    var gs = GameState.Instance;
                    string airport = string.IsNullOrWhiteSpace(gs.CurrentAirportName)
                        ? $"机场#{gs.CurrentAirport}"
                        : gs.CurrentAirportName;
                    tmp.text = $"当前：航班 {gs.Leg} · {airport} · ${gs.Money}（整备期间，可新建或覆盖存档）";
                    tmp.color = UITheme.TealDk;
                }
                else
                {
                    tmp.text = $"{saveReason}（仍可选择下方存档直接载入回档）";
                    tmp.color = UITheme.OrangeDk;
                }
            }
        }

        RebuildSaveRows(canSave);
    }

    private static void RebuildSaveRows(bool canSave)
    {
        if (_listContent == null) return;

        for (int i = _listContent.childCount - 1; i >= 0; i--)
        {
            UnityEngine.Object.Destroy(_listContent.GetChild(i).gameObject);
        }

        List<SaveData> saves = SaveStorage.ListAll();
        if (saves.Count == 0)
        {
            var empty = UIF.Text(
                _listContent,
                "EmptyHint",
                "暂无存档。可在上方输入名称后点击「新建存档」。",
                UITheme.SizeBody,
                UITheme.InkSoft,
                TextAlignmentOptions.Center,
                false,
                true
            );
            UIF.LE(empty.gameObject, 0f, 80f, 1f, 0f, 0f, 60f);
            return;
        }

        foreach (var save in saves)
        {
            var captured = save;
            var rowPanel = UIF.Panel(
                _listContent,
                $"Row_{captured.Id}",
                UITheme.PaperDk,
                UITheme.Radius,
                false,
                false
            );
            var rowRt = rowPanel.Root;
            UIF.LE(rowRt.gameObject, 0f, 66f, 1f, 0f, 0f, 66f);
            UIF.HLayout(rowRt.gameObject, 8f, UIF.Pad(14, 14, 8, 8), TextAnchor.MiddleLeft, true, true, false, false);

            var infoCol = UIF.Rect("InfoCol", rowRt);
            UIF.LE(infoCol.gameObject, 0f, 50f, 1f, 0f, 180f, 50f);
            UIF.VLayout(infoCol.gameObject, 2f, UIF.Pad(0), TextAnchor.MiddleLeft, true, true, true, false);

            string airport = string.IsNullOrWhiteSpace(captured.CurrentAirportName)
                ? $"机场#{captured.CurrentAirport}"
                : captured.CurrentAirportName;

            var nameTxt = UIF.Text(infoCol, "Name", captured.SaveName, UITheme.SizeBody, UITheme.Ink, TextAlignmentOptions.Left, false, false);
            UIF.LE(nameTxt.gameObject, 0f, 24f, 1f, 0f, 0f, 24f);

            var metaTxt = UIF.Text(
                infoCol,
                "Meta",
                $"航班 {captured.Leg} · {airport} · ${captured.Money} · 物品 {captured.WorldItems?.Count ?? 0} · {captured.SavedAtLocal}",
                UITheme.SizeTiny,
                UITheme.InkSoft,
                TextAlignmentOptions.Left,
                false,
                false
            );
            UIF.LE(metaTxt.gameObject, 0f, 20f, 1f, 0f, 0f, 20f);

            var loadBtn = UIF.Button(
                rowRt,
                "LoadBtn",
                "载入",
                (Action)(() => OnLoadSaveClicked(captured)),
                ButtonStyle.Primary,
                72f,
                36f,
                NoGlyph(),
                UITheme.SizeSmall,
                UITheme.RadiusSmall
            );
            loadBtn?.ApplyNow(true);

            var overwriteBtn = UIF.Button(
                rowRt,
                "OverwriteBtn",
                "覆盖",
                (Action)(() => OnOverwriteSaveClicked(captured)),
                ButtonStyle.Secondary,
                72f,
                36f,
                NoGlyph(),
                UITheme.SizeSmall,
                UITheme.RadiusSmall
            );
            if (overwriteBtn != null)
            {
                overwriteBtn.SetInteractable(canSave);
                overwriteBtn.ApplyNow(true);
            }

            var delBtn = UIF.Button(
                rowRt,
                "DeleteBtn",
                "删除",
                (Action)(() => OnDeleteSaveClicked(captured)),
                ButtonStyle.Danger,
                72f,
                36f,
                NoGlyph(),
                UITheme.SizeSmall,
                UITheme.RadiusSmall
            );
            delBtn?.ApplyNow(true);
        }
    }

    private static void OnCreateSaveClicked()
    {
        try
        {
            string inputName = _nameInput != null ? _nameInput.text : string.Empty;
            Plugin.Logger?.LogInfo($"[UI] 点击 '新建存档' (输入名称='{inputName}')。");
            var saved = SaveService.CaptureAndWrite(inputName);
            if (_nameInput != null)
            {
                _nameInput.text = string.Empty;
            }
            UIRoot.Instance?.Toast($"已保存：{saved.SaveName}", NotifyKind.Good);
            RefreshModal();
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogError($"[UI] 新建存档失败: {ex}");
            UIRoot.Instance?.Toast($"保存失败：{ex.Message}", NotifyKind.Danger);
        }
    }

    private static void OnOverwriteSaveClicked(SaveData target)
    {
        try
        {
            string inputName = _nameInput != null && !string.IsNullOrWhiteSpace(_nameInput.text)
                ? _nameInput.text
                : target.SaveName;
            Plugin.Logger?.LogInfo($"[UI] 点击 '覆盖' (TargetId={target?.Id}, OldName='{target?.SaveName}', NewName='{inputName}')。");
            var saved = SaveService.CaptureAndWrite(inputName, target.Id);
            UIRoot.Instance?.Toast($"已覆盖保存：{saved.SaveName}", NotifyKind.Good);
            RefreshModal();
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogError($"[UI] 覆盖存档失败: {ex}");
            UIRoot.Instance?.Toast($"覆盖失败：{ex.Message}", NotifyKind.Danger);
        }
    }

    private static void OnLoadSaveClicked(SaveData target)
    {
        try
        {
            Plugin.Logger?.LogInfo(
                $"[UI] 点击 '载入' (Id={target?.Id}, Name='{target?.SaveName}', Ver={target?.Version}, Leg={target?.Leg}, Money={target?.Money}, Airport={target?.CurrentAirport}:{target?.CurrentAirportName}, Dest={target?.DestinationAirport}:{target?.DestinationName}, Upgrades={target?.Upgrades?.Count ?? 0})。");
            CloseModal();
            _pauseScreen?.CloseInstant();
            SaveService.BeginLoad(target);
            UIRoot.Instance?.Toast($"正在载入存档：{target.SaveName}...", NotifyKind.Info);
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogError($"[UI] 载入存档失败: {ex}");
            UIRoot.Instance?.Toast($"载入失败：{ex.Message}", NotifyKind.Danger);
        }
    }

    private static void OnDeleteSaveClicked(SaveData target)
    {
        Plugin.Logger?.LogInfo($"[UI] 点击 '删除' (Id={target?.Id}, Name='{target?.SaveName}')。");
        if (SaveStorage.Delete(target.Id))
        {
            UIRoot.Instance?.Toast($"已删除：{target.SaveName}", NotifyKind.Info);
            RefreshModal();
        }
    }
}
