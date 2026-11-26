using System;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mayday;
using Mayday.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MoveInventory;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "com.mayday.moveinventory";
    public const string PluginName = "MoveInventory";
    public const string PluginVersion = "1.0.0";
    public const int MaxSupportedSlots = 8;

    internal static ManualLogSource Logger;
    internal static ConfigEntry<int> HostSlotCountConfig;

    // 同步后的本地玩家实际物品栏格数（由主机通过 Mirror SyncList 下发，默认 4）
    internal static int CurrentLocalSlotCount = 4;

    public override void Load()
    {
        Logger = Log;

        HostSlotCountConfig = Config.Bind(
            "Host",
            "SlotCount",
            4,
            new ConfigDescription(
                "主机端设定的全员物品栏数量（限制为 1~8）。仅房主配置生效，所有客户端自动同步。",
                new AcceptableValueRange<int>(1, MaxSupportedSlots)
            )
        );

        int hostSlots = Math.Clamp(HostSlotCountConfig.Value, 1, MaxSupportedSlots);
        PatchCmdSelectBounds();

        var harmony = new Harmony(PluginGuid);
        harmony.PatchAll(typeof(InventoryPatches));

        Log.LogInfo($"MoveInventory {PluginVersion} 已加载 (主机设定 SlotCount={hostSlots}，范围 1~8)。");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, UIntPtr dwSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private static unsafe void PatchCmdSelectBounds()
    {
        IntPtr basePtr = GetModuleHandleW("GameAssembly.dll");
        if (basePtr == IntPtr.Zero)
        {
            Logger?.LogError("未找到 GameAssembly.dll，无法修补 CmdSelect 上限。");
            return;
        }

        // 1. PlayerInventory::InvokeUserCode_CmdSelect__Int32 + 0xEA: cmp edi, 3 -> cmp edi, 7
        PatchByteIfMatch((byte*)basePtr + 0x00768FFAu, new byte[] { 0x83, 0xFF, 0x03, 0x77, 0x48 }, 2, 0x07, "InvokeUserCode_CmdSelect");
        // 2. PlayerInventory::UserCode_CmdSelect__Int32 + 0x00: cmp edx, 3 -> cmp edx, 7
        PatchByteIfMatch((byte*)basePtr + 0x0076AED0u, new byte[] { 0x83, 0xFA, 0x03, 0x77, 0x31 }, 2, 0x07, "UserCode_CmdSelect");
    }

    private static unsafe void PatchByteIfMatch(byte* addr, byte[] expected, int offset, byte newVal, string label)
    {
        if (addr[offset] == newVal)
        {
            Logger?.LogInfo($"[OK] {label} 上限已为 {newVal + 1} 格。");
            return;
        }
        for (int i = 0; i < expected.Length; i++)
        {
            if (addr[i] != expected[i])
            {
                Logger?.LogWarning($"[WARN] {label} 特征不匹配，跳过内存补丁。");
                return;
            }
        }
        IntPtr target = (IntPtr)(addr + offset);
        if (VirtualProtect(target, (UIntPtr)1, 0x40, out uint oldProt))
        {
            addr[offset] = newVal;
            VirtualProtect(target, (UIntPtr)1, oldProt, out _);
            FlushInstructionCache(GetCurrentProcess(), target, (UIntPtr)1);
            Logger?.LogInfo($"[OK] {label} 上限已放宽至 {newVal + 1} 格 (3 -> 7)。");
        }
        else
        {
            Logger?.LogError($"[ERR] {label} VirtualProtect 失败: {Marshal.GetLastWin32Error()}");
        }
    }
}

internal static class InventoryPatches
{
    private static bool _inHandleHotbar;
    private static int _pendingScrollSteps;
    private static bool _hasScrollOverride;

    private static float _slotStep = 72f;
    private static float _origCenterX = 0f;
    private static int _lastUiSlotCount = -1;

    // 1. 服务端初始化玩家背包时，按主机配置调整 slots 数量（1~8）。
    //    由于 slots 是 Mirror SyncList，长度变化会自动同步至所有客户端。
    [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.OnStartServer))]
    [HarmonyPostfix]
    private static void PlayerInventory_OnStartServer_Postfix(PlayerInventory __instance)
    {
        if (__instance == null || __instance.slots == null) return;

        int raw = Plugin.HostSlotCountConfig?.Value ?? 4;
        int target = Math.Clamp(raw, 1, Plugin.MaxSupportedSlots);

        var slots = __instance.slots;
        while (slots.Count < target)
        {
            slots.Add(0u);
        }
        while (slots.Count > target)
        {
            slots.RemoveAt(slots.Count - 1);
        }

        if (__instance.Active >= target)
        {
            __instance.Networkactive = 0;
        }

        if (__instance.isLocalPlayer)
        {
            Plugin.CurrentLocalSlotCount = target;
        }
    }

    // 2. 服务端执行切格命令时，放行 0..(slots.Count - 1)，并拦截超出主机格数的非法索引。
    [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.UserCode_CmdSelect__Int32))]
    [HarmonyPrefix]
    private static bool PlayerInventory_UserCode_CmdSelect_Prefix(PlayerInventory __instance, int slot)
    {
        if (__instance == null || __instance.slots == null) return false;

        int count = Math.Clamp(__instance.slots.Count, 1, Plugin.MaxSupportedSlots);
        if (slot < 0 || slot >= count)
        {
            return false;
        }

        if (slot <= 3)
        {
            // 0..3 直接走原版逻辑（完全等价）
            return true;
        }

        if (!__instance.HandsFull)
        {
            __instance.Networkactive = slot;
        }
        return false;
    }

    // 3. 拦截 PlayerInteractor.HandleHotbar，记录当前帧是否由鼠标滚轮触发选格，
    //    并在 CmdSelect 调用时将原版硬编码的 (active - scroll + 4) & 3 修正为按实际格数 N 取模。
    [HarmonyPatch(typeof(PlayerInteractor), nameof(PlayerInteractor.HandleHotbar))]
    [HarmonyPrefix]
    private static void PlayerInteractor_HandleHotbar_Prefix()
    {
        _inHandleHotbar = true;
        _hasScrollOverride = false;
        _pendingScrollSteps = 0;

        int hotbarDown = GameInput.HotbarDown;
        int scroll = GameInput.ScrollSteps;
        if (hotbarDown < 0 && scroll != 0)
        {
            _pendingScrollSteps = scroll;
            _hasScrollOverride = true;
        }
    }

    [HarmonyPatch(typeof(PlayerInteractor), nameof(PlayerInteractor.HandleHotbar))]
    [HarmonyPostfix]
    private static void PlayerInteractor_HandleHotbar_Postfix()
    {
        _inHandleHotbar = false;
        _hasScrollOverride = false;
        _pendingScrollSteps = 0;
    }

    [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.CmdSelect))]
    [HarmonyPrefix]
    private static bool PlayerInventory_CmdSelect_Prefix(PlayerInventory __instance, ref int slot)
    {
        if (__instance == null || __instance.slots == null) return true;

        int count = Math.Clamp(__instance.slots.Count, 1, Plugin.MaxSupportedSlots);
        if (_inHandleHotbar && _hasScrollOverride && _pendingScrollSteps != 0)
        {
            _hasScrollOverride = false;
            int current = Math.Clamp(__instance.Active, 0, count - 1);
            int next = ((current - _pendingScrollSteps) % count + count) % count;
            slot = next;
        }

        return slot >= 0 && slot < count;
    }

    // 4. 扩展数字键 1~8 快捷选格，并屏蔽超出当前主机设定格数的数字键。
    [HarmonyPatch(typeof(GameInput), nameof(GameInput.HotbarDown), MethodType.Getter)]
    [HarmonyPostfix]
    private static void GameInput_HotbarDown_Postfix(ref int __result)
    {
        int count = Math.Clamp(Plugin.CurrentLocalSlotCount, 1, Plugin.MaxSupportedSlots);

        if (__result >= 0)
        {
            if (__result >= count)
            {
                __result = -1;
            }
            return;
        }

        if (count <= 4 || GameInput.Blocked)
        {
            return;
        }

        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (count >= 5 && kb[Key.Digit5].wasPressedThisFrame) { __result = 4; return; }
        if (count >= 6 && kb[Key.Digit6].wasPressedThisFrame) { __result = 5; return; }
        if (count >= 7 && kb[Key.Digit7].wasPressedThisFrame) { __result = 6; return; }
        if (count >= 8 && kb[Key.Digit8].wasPressedThisFrame) { __result = 7; return; }
    }

    // 5. 构建 HotbarWidget 时预先分配 8 个槽位，并在 Tick 时按客户端同步到的实际格数动态显隐。
    [HarmonyPatch(typeof(HotbarWidget), nameof(HotbarWidget.Build))]
    [HarmonyPrefix]
    private static void HotbarWidget_Build_Prefix(HotbarWidget __instance)
    {
        if (__instance == null) return;

        if (__instance.slots == null || __instance.slots.Length < Plugin.MaxSupportedSlots)
        {
            __instance.slots = new Il2CppReferenceArray<HotbarWidget.Slot>(Plugin.MaxSupportedSlots);
        }
        _lastUiSlotCount = -1;
    }

    [HarmonyPatch(typeof(HotbarWidget), nameof(HotbarWidget.Build))]
    [HarmonyPostfix]
    private static void HotbarWidget_Build_Postfix(HotbarWidget __instance)
    {
        if (__instance == null || __instance.slots == null || __instance.slots.Length < 4) return;
        HotbarWidget.Slot s0 = __instance.slots[0];
        HotbarWidget.Slot s1 = __instance.slots[1];
        HotbarWidget.Slot s3 = __instance.slots[3];
        if (s0?.rt != null && s1?.rt != null && s3?.rt != null)
        {
            float step = s1.rt.anchoredPosition.x - s0.rt.anchoredPosition.x;
            if (Mathf.Abs(step) > 1f)
            {
                _slotStep = step;
            }
            // 原版 0..3 格的中点即为屏幕/父容器精确水平中心
            _origCenterX = (s0.rt.anchoredPosition.x + s3.rt.anchoredPosition.x) * 0.5f;
        }
    }

    [HarmonyPatch(typeof(HotbarWidget), nameof(HotbarWidget.Tick))]
    [HarmonyPostfix]
    private static void HotbarWidget_Tick_Postfix(HotbarWidget __instance, HudContext c)
    {
        if (__instance == null || __instance.slots == null) return;
        if (c == null || c.Inventory == null || c.Inventory.slots == null) return;

        int count = Math.Clamp(c.Inventory.slots.Count, 1, Plugin.MaxSupportedSlots);
        Plugin.CurrentLocalSlotCount = count;

        if (count == _lastUiSlotCount) return;
        _lastUiSlotCount = count;

        int len = __instance.slots.Length;
        for (int i = 0; i < len; i++)
        {
            HotbarWidget.Slot slot = __instance.slots[i];
            if (slot != null && slot.rt != null)
            {
                bool visible = i < count;
                slot.rt.gameObject.SetActive(visible);
                if (visible)
                {
                    Vector2 pos = slot.rt.anchoredPosition;
                    pos.x = _origCenterX + (i - (count - 1) * 0.5f) * _slotStep;
                    slot.rt.anchoredPosition = pos;
                }
            }
        }
    }
}
