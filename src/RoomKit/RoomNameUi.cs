using System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mayday.Net;
using Mayday.UI;
using Mirror;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoomKit
{
    // 在暂停菜单左边那列按钮下面插一行「房间名」输入框，只有房主看得到。
    // 回车（或失去焦点）就写进 Steam 大厅数据。
    internal static class RoomNameUi
    {
        private static TMP_InputField field;

        public static void Build(PauseScreen screen, Transform column)
        {
            if (!NetworkServer.active) return;
            if (field != null) return;

            try
            {
                var invite = screen.inviteBtn;
                if (invite == null)
                {
                    Plugin.Logger.LogWarning("[RoomKit] inviteBtn is null, skip room name ui");
                    return;
                }

                var column2 = invite.transform.parent;
                if (column2 != null) column = column2;
                if (column == null)
                {
                    Plugin.Logger.LogWarning("[RoomKit] no column for room name ui");
                    return;
                }

                var ibrt = invite.GetComponent<RectTransform>();
                float rowHeight = 56f;
                float width = 420f;
                if (ibrt != null)
                {
                    if (ibrt.sizeDelta.y > 20f) rowHeight = Mathf.Clamp(ibrt.sizeDelta.y * 0.75f, 40f, 72f);
                    if (ibrt.sizeDelta.x > 20f) width = ibrt.sizeDelta.x;
                    Plugin.Logger.LogInfo($"[RoomKit] inviteBtn rect size={ibrt.sizeDelta} anchor={ibrt.anchorMin}/{ibrt.anchorMax}");
                }

                var row = UIF.Row(column, "MaydayRoomRow", rowHeight, 8f, UIF.Pad(0));
                if (row == null)
                {
                    Plugin.Logger.LogWarning("[RoomKit] room row null");
                    return;
                }
                row.SetSiblingIndex(invite.transform.GetSiblingIndex() + 1);

                UIF.Text(row, "RoomNameLabel", "房间名", 20f, new Color(0.14f, 0.2f, 0.32f, 1f), TextAlignmentOptions.Left, true, false);

                var onEdit = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<string>>(
                    new System.Action<string>(delegate(string value) { Apply(value); }));

                field = UIF.Input(row, "MaydayRoomInput", CurrentName(), "", 63, onEdit, Mathf.Max(160f, width - 120f), rowHeight - 10f);
                if (field == null)
                {
                    Plugin.Logger.LogWarning("[RoomKit] input field null");
                    return;
                }

                Plugin.Logger.LogInfo("[RoomKit] room name input added");
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("[RoomKit] room name ui failed: " + e);
            }
        }

        private static SteamLobbyService Steam
        {
            get
            {
                var s = NetSession.Instance;
                return s == null ? null : s.steam;
            }
        }

        private static string CurrentName()
        {
            try
            {
                var steam = Steam;
                if (steam == null || !steam.InLobby) return "";
                return RoomName.RemoveGeneratedSuffix(
                    SteamMatchmaking.GetLobbyData(steam.Lobby, SteamLobbyService.KeyName));
            }
            catch
            {
                return "";
            }
        }

        private static void Apply(string value)
        {
            try
            {
                var steam = Steam;
                if (steam == null || !steam.InLobby || !steam.IsOwner) return;

                value = value == null ? "" : value.Trim();
                value = RoomName.RemoveGeneratedSuffix(value);
                if (value.Length == 0) return;

                Plugin.RoomName.Value = value;
                RoomName.TryApply(steam);
                if (Plugin.Instance != null) Plugin.Instance.Config.Save();
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("[RoomKit] room name apply failed: " + e);
            }
        }
    }
}