using System;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Mayday;
using Mayday.Airport;
using Mayday.Flight;
using Mayday.Net;
using Mayday.Service;
using Mayday.UI;

namespace SaveManager;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "com.mayday.savemanager";
    public const string PluginName = "SaveManager";
    public const string PluginVersion = "1.0.0";

    internal static ManualLogSource Logger;

    public override void Load()
    {
        Logger = Log;
        SaveStorage.EnsureDirectory();

        var harmony = new Harmony(PluginGuid);
        harmony.PatchAll(typeof(PauseScreenPatches));
        harmony.PatchAll(typeof(LoadFlowPatches));

        var patched = harmony.GetPatchedMethods()
            .Select(m => $"{m.DeclaringType?.Name}.{m.Name}")
            .Distinct()
            .OrderBy(n => n);

        int existingCount = 0;
        try
        {
            existingCount = SaveStorage.ListAll().Count;
        }
        catch (Exception ex)
        {
            Log.LogWarning($"[初始化] 扫描已有存档异常: {ex.Message}");
        }

        Log.LogInfo(
            $"{PluginName} {PluginVersion} 已加载 (存档目录={SaveStorage.SavesDirectory}, 已有存档={existingCount}, Harmony补丁=[{string.Join(", ", patched)}])。");
    }
}

internal static class PauseScreenPatches
{
    [HarmonyPatch(typeof(PauseScreen), nameof(PauseScreen.Build))]
    [HarmonyPostfix]
    private static void PauseScreen_Build_Postfix(PauseScreen __instance)
    {
        SaveMenuUI.AttachToPauseScreen(__instance);
    }

    [HarmonyPatch(typeof(PauseScreen), nameof(PauseScreen.OnOpen))]
    [HarmonyPostfix]
    private static void PauseScreen_OnOpen_Postfix(PauseScreen __instance)
    {
        SaveMenuUI.RefreshPauseButtonState();
    }

    [HarmonyPatch(typeof(PauseScreen), nameof(PauseScreen.OnClose))]
    [HarmonyPostfix]
    private static void PauseScreen_OnClose_Postfix()
    {
        SaveMenuUI.CloseModal();
    }
}

internal static class LoadFlowPatches
{
    [HarmonyPatch(typeof(GameDirector), nameof(GameDirector.ServerTick))]
    [HarmonyPrefix]
    private static bool GameDirector_ServerTick_Prefix()
    {
        // 异步切机场/世界重建与恢复期间冻结 ServerTick，避免误触到达/离港/破产/生成新选项或全灭重判
        return !SaveLoadController.IsLoading;
    }

    [HarmonyPatch(typeof(GameDirector), nameof(GameDirector.Update))]
    [HarmonyPostfix]
    private static void GameDirector_Update_Postfix(GameDirector __instance)
    {
        SaveLoadController.Tick(__instance);
    }

    [HarmonyPatch(typeof(ServiceDirector), nameof(ServiceDirector.OnPhaseChanged))]
    [HarmonyPrefix]
    private static bool ServiceDirector_OnPhaseChanged_Prefix(GamePhase from, GamePhase to)
    {
        if (SaveLoadController.IsLoading)
        {
            Plugin.Logger?.LogInfo($"[载入保护] 拦截 ServiceDirector.OnPhaseChanged ({from} -> {to})，防止覆盖恢复后物品。");
            return false;
        }
        return true;
    }

    [HarmonyPatch(typeof(PlaneFlight), nameof(PlaneFlight.OnPhaseChanged))]
    [HarmonyPrefix]
    private static bool PlaneFlight_OnPhaseChanged_Prefix(GamePhase oldPhase, GamePhase newPhase)
    {
        if (SaveLoadController.IsLoading)
        {
            Plugin.Logger?.LogInfo($"[载入保护] 拦截 PlaneFlight.OnPhaseChanged ({oldPhase} -> {newPhase})，防止触发重置停机。");
            return false;
        }
        return true;
    }

    [HarmonyPatch(typeof(MaydayNetworkManager), nameof(MaydayNetworkManager.OnServerPhaseChanged))]
    [HarmonyPrefix]
    private static bool MaydayNetworkManager_OnServerPhaseChanged_Prefix(GamePhase from, GamePhase to)
    {
        if (SaveLoadController.IsLoading)
        {
            Plugin.Logger?.LogInfo($"[载入保护] 拦截 MaydayNetworkManager.OnServerPhaseChanged ({from} -> {to})，防止补发初始修理锤。");
            return false;
        }
        return true;
    }
}
