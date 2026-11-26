using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime;
using Mayday;
using Mayday.Airport;
using Mayday.Flight;
using Mayday.Passengers;
using Mayday.Service;
using Mayday.Systems;
using Mayday.UI;
using Mayday.World;
using Mirror;
using UnityEngine;

namespace SaveManager;

internal static class SaveLoadController
{
    private enum LoadPhase
    {
        Idle,
        WaitingWorld,
        Settling
    }

    private static LoadPhase _phase = LoadPhase.Idle;
    private static SaveData _pendingData;
    private static GameDirector _ownerDirector;
    private static int _waitFrames;
    private static int _totalFrames;
    private static int _settleFrames;
    private static float _elapsed;

    public static bool IsLoading
    {
        get
        {
            if (_phase == LoadPhase.Idle) return false;
            if (!SaveService.IsHostOrSolo || GameState.Instance == null || GameDirector.Instance == null)
            {
                Plugin.Logger?.LogWarning("[载入控制器] 检测到对局已退出，自动复位 Loading 状态。");
                ResetState();
                return false;
            }
            return true;
        }
    }

    public static void StartAsyncLoad(SaveData data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        _ownerDirector = GameDirector.Instance;
        _pendingData = data;
        _waitFrames = 0;
        _totalFrames = 0;
        _settleFrames = 0;
        _elapsed = 0f;
        _phase = LoadPhase.WaitingWorld;

        try
        {
            SaveService.PrepareWorldForLoad(data);
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogError($"[载入控制器] PrepareWorldForLoad 异常，已复位状态: {ex}");
            ResetState();
            throw;
        }
    }

    public static void Tick(GameDirector gd)
    {
        if (_phase == LoadPhase.Idle || _pendingData == null) return;
        if (_ownerDirector != null && gd != _ownerDirector) return;
        if (!SaveService.IsHostOrSolo || GameState.Instance == null)
        {
            Plugin.Logger?.LogWarning("[载入控制器] 会话状态失效，中止载入并复位。");
            ResetState();
            return;
        }

        _elapsed += Time.unscaledDeltaTime;

        if (_phase == LoadPhase.WaitingWorld)
        {
            _totalFrames++;
            var wm = WorldManager.Instance;
            int curIdx = wm != null && wm.Current != null ? wm.Current.index : -999;
            int destIdx = wm != null && wm.Destination != null ? wm.Destination.index : -1;
            bool hasPending = wm != null && wm.HasPendingWork;
            bool airportMatched = wm == null || (wm.Current != null && wm.Current.index == _pendingData.CurrentAirport);
            bool worldReady = wm == null || (!hasPending && airportMatched);

            if (_totalFrames == 1 || _totalFrames % 15 == 0)
            {
                Plugin.Logger?.LogInfo(
                    $"[载入-阶段1] 等待机场就绪: Frame={_totalFrames}, Elapsed={_elapsed:F2}s, WorldReady={worldReady}, Pending={hasPending}, CurIdx={curIdx} (目标={_pendingData.CurrentAirport}), DestIdx={destIdx} (目标={_pendingData.DestinationAirport})。");
            }

            if (worldReady && _totalFrames >= 2)
            {
                _waitFrames++;
            }

            if (_waitFrames >= 6 || _elapsed > 4.5f)
            {
                if (!worldReady)
                {
                    Plugin.Logger?.LogWarning(
                        $"[载入-阶段1] 等待世界就绪超时 ({_elapsed:F2}s)，强制继续恢复 (CurIdx={curIdx}, Pending={hasPending})。");
                }
                else
                {
                    Plugin.Logger?.LogInfo(
                        $"[载入-阶段1] 目标机场 #{curIdx} 与碰撞体已稳定 ({_elapsed:F2}s, WaitFrames={_waitFrames})，进入阶段2快照恢复。");
                }

                try
                {
                    Plugin.Logger?.LogInfo("[载入-阶段2] 即将调用 ApplyLoadedSnapshot...");
                    SaveService.ApplyLoadedSnapshot(_pendingData);
                    _phase = LoadPhase.Settling;
                    _settleFrames = 0;
                    Plugin.Logger?.LogInfo($"[载入-阶段2] ApplyLoadedSnapshot 已返回，进入 Settling 阶段 (unscaledDt={Time.unscaledDeltaTime:F4}s)。");
                }
                catch (Exception ex)
                {
                    Plugin.Logger?.LogError($"[载入-阶段2] 恢复存档数据失败: {ex}");
                    UIRoot.Instance?.Toast($"载入失败：{ex.Message}", NotifyKind.Danger);
                    ResetState();
                }
            }
            return;
        }

        if (_phase == LoadPhase.Settling)
        {
            _settleFrames++;
            Plugin.Logger?.LogInfo($"[载入-阶段3] Settling 帧 {_settleFrames}/3 开始 (elapsed={_elapsed:F2}s, unscaledDt={Time.unscaledDeltaTime:F4}s)。");
            if (_settleFrames == 3)
            {
                try
                {
                    Plugin.Logger?.LogInfo("[载入-阶段3] 即将调用 ReapplyPostSettleState...");
                    SaveService.ReapplyPostSettleState(_pendingData);
                    Plugin.Logger?.LogInfo("[载入-阶段3] ReapplyPostSettleState 已返回，即将调用 LogPostLoadAudit...");
                    SaveService.LogPostLoadAudit(_pendingData);
                    Plugin.Logger?.LogInfo("[载入-阶段3] LogPostLoadAudit 已返回，即将显示 Toast...");
                    UIRoot.Instance?.Toast($"已载入存档：{_pendingData.SaveName}", NotifyKind.Good);
                    Plugin.Logger?.LogInfo($"[载入完成] 存档 '{_pendingData.SaveName}' 载入流程全部结束 (总耗时={_elapsed:F2}s)。");
                }
                catch (Exception ex)
                {
                    Plugin.Logger?.LogWarning($"[载入-阶段3] 二阶段位置巩固异常: {ex}");
                }
                Plugin.Logger?.LogInfo("[载入-阶段3] 即将调用 ResetState...");
                ResetState();
                Plugin.Logger?.LogInfo("[载入-阶段3] ResetState 已完成。");
            }
        }
    }

    private static void ResetState()
    {
        _phase = LoadPhase.Idle;
        _pendingData = null;
        _ownerDirector = null;
        _waitFrames = 0;
        _totalFrames = 0;
        _settleFrames = 0;
        _elapsed = 0f;
    }
}

internal static class SaveService
{
    private const float PlaneAttachMaxDist = 38f;
    private static IntPtr s_syncListDestOptionAddMethod;

    /// <summary>
    /// Il2CppInterop 将含引用字段的 DestOption 结构体生成为继承 Il2CppSystem.ValueType 的托管类，
    /// Mirror.SyncList&lt;T&gt;.Add 检查 typeof(T).IsValueType 为 false，会错误传入 boxed 指针而非 unbox 指针。
    /// 直接通过 il2cpp_runtime_invoke 传入 il2cpp_object_unbox 指针以匹配原生值类型参数约定。
    /// </summary>
    private static unsafe void AddDestOptionSafe(Mirror.SyncList<DestOption> list, DestOption item)
    {
        if (list == null || item == null) return;

        if (s_syncListDestOptionAddMethod == IntPtr.Zero)
        {
            FieldInfo fi = typeof(Mirror.SyncList<DestOption>).GetField(
                "NativeMethodInfoPtr_Add_Public_Virtual_Final_New_Void_T_0",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (fi != null)
            {
                s_syncListDestOptionAddMethod = (IntPtr)fi.GetValue(null);
            }
        }

        if (s_syncListDestOptionAddMethod == IntPtr.Zero)
        {
            throw new InvalidOperationException("未能找到 Mirror.SyncList<DestOption>.Add 的 NativeMethodInfoPtr。");
        }

        IntPtr listPtr = IL2CPP.Il2CppObjectBaseToPtrNotNull(list);
        IntPtr boxedPtr = IL2CPP.Il2CppObjectBaseToPtrNotNull(item);
        IntPtr unboxedPtr = IL2CPP.il2cpp_object_unbox(boxedPtr);

        void** args = stackalloc void*[1];
        args[0] = (void*)unboxedPtr;
        IntPtr exc = IntPtr.Zero;
        IL2CPP.il2cpp_runtime_invoke(s_syncListDestOptionAddMethod, listPtr, args, ref exc);
        GC.KeepAlive(item);
        GC.KeepAlive(list);
        Il2CppException.RaiseExceptionIfNecessary(exc);
    }

    public static bool IsHostOrSolo =>
        NetworkServer.active || (!NetworkClient.active && GameState.Instance != null);

    /// <summary>
    /// 仅在整备期间允许存档：GamePhase 为 PreShift/Ground 且 GameDirector.stage 为 PreShift/Turnaround。
    /// </summary>
    public static bool CanSaveNow(out string reason)
    {
        if (!IsHostOrSolo)
        {
            reason = "仅房主可以保存存档";
            return false;
        }

        if (SaveLoadController.IsLoading)
        {
            reason = "正在载入存档中，请稍候";
            return false;
        }

        var gs = GameState.Instance;
        if (gs == null)
        {
            reason = "当前不在对局中";
            return false;
        }

        if (gs.Phase == GamePhase.Flight)
        {
            reason = "飞行中不可保存，仅整备期间可保存";
            return false;
        }

        if (gs.Phase == GamePhase.GameOver)
        {
            reason = "坠机结算中不可保存，仅整备期间可保存";
            return false;
        }

        var gd = GameDirector.Instance;
        if (gd != null)
        {
            switch (gd.stage)
            {
                case DirectorStage.PreShift:
                case DirectorStage.Turnaround:
                    break;
                case DirectorStage.Arrived:
                case DirectorStage.Deplaning:
                    reason = "乘客下机中，仅整备期间可保存";
                    return false;
                case DirectorStage.Boarding:
                    reason = "登机期间不可保存，仅整备期间可保存";
                    return false;
                case DirectorStage.Flight:
                    reason = "飞行中不可保存，仅整备期间可保存";
                    return false;
                case DirectorStage.GameOver:
                    reason = "坠机结算中不可保存，仅整备期间可保存";
                    return false;
                default:
                    reason = "当前非整备阶段，不可保存";
                    return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    public static bool CanLoadNow(out string reason)
    {
        if (!IsHostOrSolo)
        {
            reason = "仅房主可以载入存档";
            return false;
        }

        if (SaveLoadController.IsLoading)
        {
            reason = "正在载入存档中，请稍候";
            return false;
        }

        if (GameState.Instance == null)
        {
            reason = "当前不在对局中";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static SaveData CaptureAndWrite(string customName, string overwriteId = null)
    {
        if (!CanSaveNow(out string reason))
        {
            throw new InvalidOperationException(reason);
        }

        var gs = GameState.Instance;
        var gd = GameDirector.Instance;
        var now = DateTimeOffset.Now;
        string airport = string.IsNullOrWhiteSpace(gs.CurrentAirportName)
            ? $"机场#{gs.CurrentAirport}"
            : gs.CurrentAirportName;

        string saveName = string.IsNullOrWhiteSpace(customName)
            ? $"航班 {gs.Leg} - {airport} ({now:MM-dd HH:mm})"
            : customName.Trim();

        var data = new SaveData
        {
            Version = 5,
            Id = string.IsNullOrWhiteSpace(overwriteId)
                ? $"save_{now.ToUnixTimeMilliseconds()}"
                : overwriteId,
            SaveName = saveName,
            SavedAtLocal = now.ToString("yyyy-MM-dd HH:mm:ss"),
            SavedAtUnix = now.ToUnixTimeSeconds(),

            GamePhaseValue = (int)gs.Phase,
            DirectorStageValue = gd != null ? (int)gd.stage : (int)DirectorStage.Turnaround,
            RunSeed = gs.RunSeed,
            Leg = gs.Leg,
            Money = gs.Money,
            Debt = gs.Debt,
            Rating = gs.Rating,
            Difficulty = gs.Difficulty,
            CurrentAirport = gs.CurrentAirport,
            DestinationAirport = gs.DestinationAirport,
            CurrentAirportName = gs.CurrentAirportName ?? string.Empty,
            DestinationName = gs.DestinationName ?? string.Empty,
            GroundTimeLeft = gs.GroundTimeLeft,
            LegTime = gs.LegTime,
            TimeOfDay = gs.TimeOfDay,
            Weather = gs.Weather,
            TotalPassengers = gs.TotalPassengers,
            TotalEarned = gs.TotalEarned
        };

        if (gs.Upgrades != null)
        {
            for (int i = 0; i < gs.Upgrades.Count; i++)
            {
                data.Upgrades.Add(gs.Upgrades[i]);
            }
        }

        CaptureDirector(data, gd);
        CaptureWeather(data);

        var plane = UnityEngine.Object.FindObjectOfType<PlaneFlight>();
        CapturePlane(data, plane);
        CaptureGroundEquipment(data, plane);
        CapturePlayerInventoriesAndPoses(data, plane);
        CapturePlaneSockets(data);
        CaptureFreeWorldItems(data, plane);
        CaptureMesses(data, plane);

        SaveStorage.Save(data);
        Plugin.Logger?.LogInfo(
            $"[存档完成] '{data.SaveName}' (Id={data.Id}, Ver={data.Version}, Phase={data.GamePhaseValue}, Stage={data.DirectorStageValue}, Seed={data.RunSeed}, Leg={data.Leg}, Money=${data.Money}, Debt=${data.Debt}, Airport={data.CurrentAirport}:{data.CurrentAirportName}, Dest={data.DestinationAirport}:{data.DestinationName}, Upgrades={data.Upgrades.Count}[{string.Join(",", data.Upgrades)}], Visited={data.Director?.VisitedAirports?.Count ?? 0}, Options={data.Director?.Options?.Count ?? 0}, ChosenOpt={data.Director?.ChosenOption ?? -1}, 玩家={data.Players.Count}, 插槽={data.SocketItems.Count}, 自由物品={data.WorldItems.Count}, 污渍={data.Messes.Count})。");
        return data;
    }

    public static void BeginLoad(SaveData data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (!CanLoadNow(out string reason))
        {
            Plugin.Logger?.LogWarning($"[载入拒绝] 无法开始载入 '{data.SaveName}': {reason}");
            throw new InvalidOperationException(reason);
        }

        Plugin.Logger?.LogInfo(
            $"[载入开始] 请求载入 '{data.SaveName}' (Id={data.Id}, Ver={data.Version}, TargetPhase={data.GamePhaseValue}, TargetStage={data.DirectorStageValue}, Seed={data.RunSeed}, Leg={data.Leg}, Money=${data.Money}, Debt=${data.Debt}, Airport={data.CurrentAirport}:{data.CurrentAirportName}, Dest={data.DestinationAirport}:{data.DestinationName}, Upgrades={data.Upgrades?.Count ?? 0}, Visited={data.Director?.VisitedAirports?.Count ?? 0}, Options={data.Director?.Options?.Count ?? 0}, ChosenOpt={data.Director?.ChosenOption ?? -1})。");
        LogStateSnapshot("[载入前实时状态]");
        SaveLoadController.StartAsyncLoad(data);
    }

    internal static void PrepareWorldForLoad(SaveData data)
    {
        Plugin.Logger?.LogInfo("[载入-阶段0] 开始准备世界与会话状态（不调用 ServerRestartRun 以保留运行进度）...");
        CloseSummaryScreens();

        var sd = ServiceDirector.Instance;
        if (sd != null)
        {
            sd.StopAllCoroutines();
            sd.SyncRun();
            Plugin.Logger?.LogInfo("[载入-阶段0] 已停止 ServiceDirector 协程并执行 SyncRun()。");
        }

        var gs = GameState.Instance;
        if (gs != null)
        {
            if (!string.IsNullOrEmpty(gs.AlertKey))
            {
                string prevAlert = gs.AlertKey;
                gs.ServerClearAlert(prevAlert);
                Plugin.Logger?.LogInfo($"[载入-阶段0] 已清除残留警报: '{prevAlert}'。");
            }
            gs.legLedger?.Clear();
        }

        ApplyGameStateCore(data, forceSyncUpgrades: true);

        var pd = PassengerDirector.Instance;
        if (pd != null)
        {
            pd.ServerResetForNewRun();
            RestorePassengerSeats(data);
            Plugin.Logger?.LogInfo(
                $"[载入-阶段0] 已重置 PassengerDirector 并恢复座椅配置 (RowMask=0x{pd.rowMask:X2}, Rows={pd.RowCount}, Seats={pd.SeatCapacity}, Premium={pd.premiumSeats})。");
        }

        var gd = GameDirector.Instance;
        if (gd != null)
        {
            gd.inReport = false;
            gd.pendingGameOver = false;
            gd.pendingGameOverAt = 0;
            gd.pendingCause = string.Empty;
            gd.pendingInsurance = false;
            gd.pendingInsuranceAt = 0;
            gd.pendingInsuranceCause = string.Empty;
            gd.pendingInsuranceAirport = -1;
            gd.restartPromptAt = 0f;
            if (gd.ghostFlight && gs != null)
            {
                gd.EndGhostFlight(gs);
            }
            gd.ghostFlight = false;
            gd.ghostFlightEnd = 0;
            gd.ghostStopTimer = 0f;
            gd.ClearCarry();
            gd.oldBags?.Clear();
            gd.boardingBags?.Clear();
            gd.groundedTimer = 0f;
            gd.airborneTimer = 0f;
            gd.stoppedTimer = 0f;
            gd.goAroundTimer = 0f;
            gd.deplaneClock = 0f;
            gd.ignoreCrashUntil = NetworkTime.time + 10.0;
            gd.NetworkautoDispatchAt = 0;
            gd.NetworkslotEnd = 0;
            gd.NetworkstrandedAt = 0;

            DirectorStage targetStage = GetTargetDirectorStage(data);
            gd.SetStage(targetStage);
            Plugin.Logger?.LogInfo($"[载入-阶段0] 已清理 GameDirector 残留故障/结算状态，并设定 Stage={targetStage}。");
        }

        var plane = UnityEngine.Object.FindObjectOfType<PlaneFlight>();
        if (plane != null)
        {
            plane.ClearCrash();
            Plugin.Logger?.LogInfo("[载入-阶段0] 已清除 PlaneFlight 坠机标记 (ClearCrash)。");
        }

        ReviveDeadPlayersTemporarily(plane);
        ClearInventoriesSocketsAndFreeItems();

        var wn = WorldNet.Instance;
        if (wn != null)
        {
            var curSync = wn.Sync;
            curSync.seed = data.RunSeed;
            curSync.current = data.CurrentAirport;
            curSync.destination = data.DestinationAirport;
            if (data.WeatherState != null && data.WeatherState.HasData)
            {
                curSync.originA = data.WeatherState.OriginA;
                curSync.originB = data.WeatherState.OriginB;
            }
            curSync.version++;
            wn.ServerSetSync(curSync);
            Plugin.Logger?.LogInfo(
                $"[载入-阶段0] 已预同步 WorldNet.Sync (Seed={curSync.seed}, Current={curSync.current}, Dest={curSync.destination}, Origin=({curSync.originA},{curSync.originB}), Ver={curSync.version})。");
        }

        if (WorldManager.Instance != null)
        {
            WorldManager.Instance.Reconcile(data.RunSeed, data.CurrentAirport, data.DestinationAirport, true);
            Plugin.Logger?.LogInfo(
                $"[载入-阶段0] 已触发 WorldManager.Reconcile(seed={data.RunSeed}, cur={data.CurrentAirport}, dest={data.DestinationAirport}, currentNow=true)。");
        }
    }

    internal static void ApplyLoadedSnapshot(SaveData data)
    {
        Plugin.Logger?.LogInfo("[载入-阶段2] 世界就绪，开始恢复完整存档快照...");

        // 二次覆盖 GameState 核心数据
        ApplyGameStateCore(data, forceSyncUpgrades: false);

        // 先将地勤与机场锚点切换到目标机场，再恢复 Director 与地面设备位置
        var crew = GroundCrew.Instance;
        if (crew != null)
        {
            crew.lastPhase = GetTargetGamePhase(data);
            crew.airborneThisFlight = false;
            crew.sinceLanding = false;
            crew.ServerReanchor(data.CurrentAirport, ReanchorKind.Snap);
            crew.ServerAssumeParked();
            Plugin.Logger?.LogInfo(
                $"[载入-阶段2] GroundCrew 已重锚定至机场#{data.CurrentAirport} (ServiceAirport={crew.ServiceAirport}, CurrentIndex={GroundCrew.CurrentIndex}, PlaneParked={crew.PlaneParked})。");
        }

        RestoreDirector(data);
        RestoreWeather(data);

        // 恢复飞机物理、控制面、引擎、电气、燃油、门、机舱系统
        var plane = UnityEngine.Object.FindObjectOfType<PlaneFlight>();
        RestorePlane(data, plane);

        // 恢复地面设备（加油车、上下梯、传送带、行李车、餐车）
        RestoreGroundEquipment(data);

        // 确保彻底清空残余道具，再按存档还原服务插槽、玩家背包、自由物品和污渍
        ClearInventoriesSocketsAndFreeItems();
        RestorePlaneSockets(data);
        RestorePlayerInventoriesAndPoses(data, plane);
        RestoreFreeWorldItems(data, plane);
        RestoreMesses(data, plane);

        var sd = ServiceDirector.Instance;
        if (sd != null)
        {
            sd.kitSpawned = true;
            sd.SyncRun();
        }

        RestorePassengerSeats(data);

        Plugin.Logger?.LogInfo(
            $"[载入-阶段2] 已应用存档 '{data.SaveName}' (Leg={data.Leg}, Money=${data.Money}, 机场={data.CurrentAirport}:{data.CurrentAirportName}, 自由物品={data.WorldItems?.Count ?? 0}, 插槽物品={data.SocketItems?.Count ?? 0})。");
    }

    internal static void ReapplyPostSettleState(SaveData data)
    {
        var plane = UnityEngine.Object.FindObjectOfType<PlaneFlight>();
        if (plane != null)
        {
            RestorePlanePoseAndSim(data, plane);
        }
        RestorePlayerPosesOnly(data, plane);
        Plugin.Logger?.LogInfo("[载入-阶段3] 已完成二次位置巩固 (飞机姿态 + 玩家传送/复活校验)。");
    }

    internal static void LogPostLoadAudit(SaveData data)
    {
        LogStateSnapshot($"[载入-最终校验 '{data?.SaveName}']");
    }

    private static GamePhase GetTargetGamePhase(SaveData data) =>
        (data != null && data.Version >= 2 && data.GamePhaseValue == (int)GamePhase.PreShift)
            ? GamePhase.PreShift
            : GamePhase.Ground;

    private static DirectorStage GetTargetDirectorStage(SaveData data) =>
        (data != null && data.Version >= 2 && data.DirectorStageValue == (int)DirectorStage.PreShift)
            ? DirectorStage.PreShift
            : DirectorStage.Turnaround;

    private static void LogStateSnapshot(string tag)
    {
        var gs = GameState.Instance;
        var gd = GameDirector.Instance;
        var wm = WorldManager.Instance;
        var crew = GroundCrew.Instance;
        var plane = UnityEngine.Object.FindObjectOfType<PlaneFlight>();

        int upgCount = gs?.Upgrades?.Count ?? 0;
        string upgs = string.Empty;
        if (gs?.Upgrades != null && upgCount > 0)
        {
            var list = new List<string>(upgCount);
            for (int i = 0; i < upgCount; i++) list.Add(gs.Upgrades[i]);
            upgs = string.Join(",", list);
        }

        int deadCount = 0;
        int playerCount = PlayerNet.All != null ? PlayerNet.All.Count : 0;
        if (PlayerNet.All != null)
        {
            for (int i = 0; i < PlayerNet.All.Count; i++)
            {
                if (PlayerNet.All[i] != null && PlayerNet.All[i].IsDead) deadCount++;
            }
        }

        int curWorldAirport = wm != null && wm.Current != null ? wm.Current.index : -999;
        int destWorldAirport = wm != null && wm.Destination != null ? wm.Destination.index : -1;
        string planePosStr = plane != null ? plane.transform.position.ToString("F2") : "null";
        float groundLeft = gs != null ? gs.GroundTimeLeft : 0f;
        float legTime = gs != null ? gs.LegTime : 0f;

        var snapPd = PassengerDirector.Instance;
        string snapRowMask = snapPd != null ? $"0x{snapPd.rowMask:X2}" : "null";
        int snapRows = snapPd != null ? snapPd.RowCount : -1;
        int snapSeats = snapPd != null ? snapPd.SeatCapacity : -1;
        var snapHull = UnityEngine.Object.FindObjectOfType<HullSystem>();
        string snapHullMain0 = snapHull?.mainColor != null && snapHull.mainColor.Count > 0
            ? $"#{PackColor(snapHull.mainColor[0]):X8}"
            : "none";
        string snapHullAccent0 = snapHull?.accentColor != null && snapHull.accentColor.Count > 0
            ? $"#{PackColor(snapHull.accentColor[0]):X8}"
            : "none";

        Plugin.Logger?.LogInfo(
            $"{tag} GS[Phase={gs?.Phase}, Seed={gs?.RunSeed}, Leg={gs?.Leg}, Money=${gs?.Money}, Debt=${gs?.Debt}, Cur={gs?.CurrentAirport}:'{gs?.CurrentAirportName}', Dest={gs?.DestinationAirport}:'{gs?.DestinationName}', Upgrades={upgCount}[{upgs}], GroundLeft={groundLeft:F1}, LegTime={legTime:F1}] | GD[Stage={gd?.stage}, Opts={gd?.options?.Count ?? 0}, Chosen={gd?.chosenOption}, Visited={gd?.visited?.Count ?? 0}, inReport={gd?.inReport}, pendingGO={gd?.pendingGameOver}, ghost={gd?.ghostFlight}] | World[Cur={curWorldAirport}, Dest={destWorldAirport}, Pending={wm?.HasPendingWork}, CrewIdx={GroundCrew.CurrentIndex}, CrewSvc={crew?.ServiceAirport}] | Plane[Pos={planePosStr}, Crashed={plane?.crashed}, RowMask={snapRowMask}, Rows={snapRows}, Seats={snapSeats}, HullPanel0={snapHullMain0}/{snapHullAccent0}] | Players[Count={playerCount}, Dead={deadCount}]");
    }

    private static void ReviveDeadPlayersTemporarily(PlaneFlight plane)
    {
        if (PlayerNet.All == null) return;
        AirportSite curAirport = WorldManager.Instance != null ? WorldManager.Instance.Current : null;
        Vector3 fallbackPos = curAirport != null
            ? curAirport.StandWorldPos + new Vector3(0f, 1.0f, 0f)
            : (plane != null ? plane.transform.position + new Vector3(0f, 2.0f, 0f) : Vector3.up * 2f);
        float fallbackYaw = curAirport != null ? curAirport.transform.eulerAngles.y : 0f;

        for (int i = 0; i < PlayerNet.All.Count; i++)
        {
            var p = PlayerNet.All[i];
            if (p == null) continue;
            if (p.IsSeated) p.ServerReleaseSeat();
            if (p.IsHauled) p.ServerHaulRelease();
            p.ServerClearStun();
            if (p.IsDead || p.IsSpectator)
            {
                Vector3 revPos = p.transform != null ? p.transform.position : fallbackPos;
                if (revPos.sqrMagnitude < 0.01f) revPos = fallbackPos;
                p.ServerRevive(revPos, fallbackYaw, null);
                p.ServerResetDeaths();
                Plugin.Logger?.LogInfo($"[载入-阶段0] 已预先复活玩家 '{p.DisplayName}' -> pos={revPos.ToString("F2")}。");
            }
        }
    }

    private static void ApplyGameStateCore(SaveData data, bool forceSyncUpgrades = true)
    {
        var gs = GameState.Instance;
        if (gs == null) return;

        gs.NetworkRunSeed = data.RunSeed;
        gs.NetworkLeg = data.Leg;
        gs.NetworkMoney = data.Money;
        gs.NetworkDebt = data.Debt;
        gs.NetworkRating = data.Rating;
        gs.NetworkDifficulty = data.Difficulty;
        gs.NetworkCurrentAirport = data.CurrentAirport;
        gs.NetworkDestinationAirport = data.DestinationAirport;
        gs.NetworkCurrentAirportName = data.CurrentAirportName ?? string.Empty;
        gs.NetworkDestinationName = data.DestinationName ?? string.Empty;
        gs.NetworkGroundTimeLeft = data.GroundTimeLeft;
        gs.NetworkLegTime = data.LegTime;
        gs.NetworkTimeOfDay = data.TimeOfDay;
        gs.NetworkWeather = data.Weather;
        gs.NetworkTotalPassengers = data.TotalPassengers;
        gs.NetworkTotalEarned = data.TotalEarned;

        GamePhase prevPhase = gs.Phase;
        GamePhase targetPhase = GetTargetGamePhase(data);
        if (prevPhase != targetPhase)
        {
            gs.ServerSetPhase(targetPhase);
            Plugin.Logger?.LogInfo($"[GameState] ServerSetPhase: {prevPhase} -> {targetPhase}");
        }
        else
        {
            gs.NetworkPhase = targetPhase;
        }

        if (gs.Upgrades != null)
        {
            var desired = data.Upgrades ?? new List<string>();
            bool same = !forceSyncUpgrades && gs.Upgrades.Count == desired.Count;
            if (same)
            {
                for (int i = 0; i < desired.Count; i++)
                {
                    if (!string.Equals(gs.Upgrades[i], desired[i], StringComparison.Ordinal))
                    {
                        same = false;
                        break;
                    }
                }
            }

            if (!same)
            {
                gs.Upgrades.Clear();
                foreach (string upg in desired)
                {
                    if (!string.IsNullOrEmpty(upg))
                    {
                        gs.Upgrades.Add(upg);
                    }
                }
                Plugin.Logger?.LogInfo($"[GameState] 已同步 Upgrades 共 {gs.Upgrades.Count} 项: [{string.Join(", ", desired)}]。");
            }
        }
    }

    #region Capture & Restore: GameDirector & Weather

    private static void CaptureDirector(SaveData data, GameDirector gd)
    {
        if (gd == null) return;

        var visitedList = new List<int>();
        if (gd.visited != null)
        {
            var en = gd.visited.GetEnumerator();
            while (en.MoveNext())
            {
                visitedList.Add(en.Current);
            }
        }

        var optionsList = new List<DestOptionSnapshot>();
        if (gd.options != null)
        {
            for (int i = 0; i < gd.options.Count; i++)
            {
                var opt = gd.options[i];
                if (opt == null) continue;
                optionsList.Add(new DestOptionSnapshot
                {
                    Airport = opt.airport,
                    Name = opt.name ?? string.Empty,
                    Code = opt.code ?? string.Empty,
                    Km = opt.km,
                    Pax = opt.pax,
                    Pay = opt.pay,
                    Contract = opt.contract,
                    Trait = opt.trait,
                    Weather = opt.weather,
                    Fuel = opt.fuel,
                    Visited = opt.visited,
                    VirtualSite = opt.virtualSite,
                    WFlags = opt.wflags,
                    FogVis = opt.fogVis,
                    Birds = opt.birds,
                    Hazard = opt.hazard
                });
            }
        }

        data.Director = new DirectorSnapshot
        {
            ChosenOption = gd.chosenOption,
            LeasePaid = gd.leasePaid,
            LandingsSinceLease = gd.landingsSinceLease,
            NextLease = gd.nextLease,
            FuelNeeded = gd.fuelNeeded,
            LegFrom = gd.legFrom ?? string.Empty,
            LegTo = gd.legTo ?? string.Empty,
            BestStars = gd.bestStars,
            ButterCount = gd.butterCount,
            StowawayTotal = gd.stowawayTotal,
            LandingsTotal = gd.landingsTotal,
            RunDeaths = gd.runDeaths,
            RunDefibs = gd.runDefibs,
            RunOverboard = gd.runOverboard,
            RunBounty = gd.runBounty,
            RunCompensation = gd.runCompensation,
            RunFines = gd.runFines,
            ReturnAirport = gd.returnAirport,
            OptionsLeg = gd.optionsLeg,
            OptionsFrom = gd.optionsFrom,
            OptionsVirtual = gd.optionsVirtual,
            Millionaire = gd.millionaire,
            StageTime = gd.stageTime,
            SlotSecondsLeft = gd.slotEnd > NetworkTime.time ? (float)(gd.slotEnd - NetworkTime.time) : 0f,
            AutoDispatchSecondsLeft = gd.autoDispatchAt > NetworkTime.time ? (float)(gd.autoDispatchAt - NetworkTime.time) : 0f,
            VisitedAirports = visitedList,
            Options = optionsList
        };
    }

    private static void RestoreDirector(SaveData data)
    {
        var gd = GameDirector.Instance;
        if (gd == null) return;

        var ds = data.Director;
        if (ds != null)
        {
            gd.NetworkleasePaid = ds.LeasePaid;
            gd.NetworklandingsSinceLease = ds.LandingsSinceLease;
            gd.NetworknextLease = ds.NextLease;
            gd.NetworkfuelNeeded = ds.FuelNeeded;
            gd.NetworklegFrom = ds.LegFrom ?? string.Empty;
            gd.NetworklegTo = ds.LegTo ?? string.Empty;
            gd.bestStars = ds.BestStars;
            gd.butterCount = ds.ButterCount;
            gd.stowawayTotal = ds.StowawayTotal;
            gd.landingsTotal = ds.LandingsTotal;
            gd.runDeaths = ds.RunDeaths;
            gd.runDefibs = ds.RunDefibs;
            gd.runOverboard = ds.RunOverboard;
            gd.runBounty = ds.RunBounty;
            gd.runCompensation = ds.RunCompensation;
            gd.runFines = ds.RunFines;
            gd.returnAirport = ds.ReturnAirport;
            gd.optionsLeg = ds.OptionsLeg;
            gd.optionsFrom = ds.OptionsFrom;
            gd.optionsVirtual = ds.OptionsVirtual;
            gd.millionaire = ds.Millionaire;
        }

        // 先恢复已访问机场集合（必须在任何 EnsureOptions/GenerateOptions 之前）
        if (gd.visited != null)
        {
            gd.visited.Clear();
            if (ds?.VisitedAirports != null && ds.VisitedAirports.Count > 0)
            {
                foreach (int a in ds.VisitedAirports)
                {
                    gd.visited.Add(a);
                }
            }
            else
            {
                gd.visited.Add(0);
                gd.visited.Add(data.CurrentAirport);
            }
        }

        // 清理所有残留失败/结算/定时状态
        gd.inReport = false;
        gd.pendingGameOver = false;
        gd.pendingGameOverAt = 0;
        gd.pendingCause = string.Empty;
        gd.pendingInsurance = false;
        gd.pendingInsuranceAt = 0;
        gd.pendingInsuranceCause = string.Empty;
        gd.pendingInsuranceAirport = -1;
        gd.restartPromptAt = 0f;
        if (gd.ghostFlight && GameState.Instance != null)
        {
            gd.EndGhostFlight(GameState.Instance);
        }
        gd.ghostFlight = false;
        gd.ghostFlightEnd = 0;
        gd.ghostStopTimer = 0f;
        gd.ClearCarry();
        gd.oldBags?.Clear();
        gd.boardingBags?.Clear();
        gd.groundedTimer = 0f;
        gd.airborneTimer = 0f;
        gd.stoppedTimer = 0f;
        gd.goAroundTimer = 0f;
        gd.deplaneClock = 0f;
        gd.ignoreCrashUntil = NetworkTime.time + 6.0;
        gd.lastLegTime = Mathf.RoundToInt(data.LegTime);
        gd.lastGroundLeft = Mathf.RoundToInt(data.GroundTimeLeft);
        gd.NetworkpaxBoarded = 0;
        gd.NetworkpaxTotal = 0;
        gd.NetworkbagsLoaded = 0;
        gd.NetworkbagsTotal = 0;
        gd.NetworkbagsInHold = 0;
        gd.NetworkpaxAboard = 0;
        gd.NetworkpaxLeaving = 0;
        gd.NetworkstrandedAt = 0;
        gd.NetworkslotEnd = (ds != null && ds.SlotSecondsLeft > 0f) ? NetworkTime.time + ds.SlotSecondsLeft : 0;
        gd.NetworkautoDispatchAt = (ds != null && ds.AutoDispatchSecondsLeft > 0f) ? NetworkTime.time + ds.AutoDispatchSecondsLeft : 0;

        DirectorStage targetStage = GetTargetDirectorStage(data);
        gd.SetStage(targetStage);
        if (ds != null)
        {
            gd.stageTime = ds.StageTime;
        }

        // 优先还原存档中的精确航线选项；若旧版存档无此列表，则在 visited 恢复后生成
        if (ds?.Options != null && ds.Options.Count > 0 && gd.options != null)
        {
            gd.options.Clear();
            foreach (var os in ds.Options)
            {
                if (os == null) continue;
                AddDestOptionSafe(gd.options, new DestOption
                {
                    airport = os.Airport,
                    name = os.Name ?? string.Empty,
                    code = os.Code ?? string.Empty,
                    km = os.Km,
                    pax = os.Pax,
                    pay = os.Pay,
                    contract = os.Contract,
                    trait = os.Trait,
                    weather = os.Weather,
                    fuel = os.Fuel,
                    visited = os.Visited,
                    virtualSite = os.VirtualSite,
                    wflags = os.WFlags,
                    fogVis = os.FogVis,
                    birds = os.Birds,
                    hazard = os.Hazard
                });
            }
            gd.NetworkchosenOption = (ds.ChosenOption >= -1 && ds.ChosenOption < gd.options.Count) ? ds.ChosenOption : -1;
            gd.NetworkoptionsVersion = gd.optionsVersion + 1;
            gd.clientOptionsDirty = true;
            string firstInfo = "none";
            if (gd.options.Count > 0)
            {
                var first = gd.options[0];
                firstInfo = $"{first.airport}:{first.code}({first.name})";
            }
            Plugin.Logger?.LogInfo(
                $"[GameDirector] 已恢复精确航线选项 {gd.options.Count} 条, FirstOption={firstInfo}, ChosenOption={gd.chosenOption}, Visited={gd.visited?.Count ?? 0}, Stage={gd.stage}。");
        }
        else
        {
            gd.EnsureOptions();
            if (ds != null && ds.ChosenOption >= 0 && gd.options != null && ds.ChosenOption < gd.options.Count)
            {
                gd.NetworkchosenOption = ds.ChosenOption;
            }
            else
            {
                gd.NetworkchosenOption = -1;
            }
            Plugin.Logger?.LogInfo(
                $"[GameDirector] 旧版存档重生成航线选项 {gd.options?.Count ?? 0} 条, ChosenOption={gd.chosenOption}, Visited={gd.visited?.Count ?? 0}, Stage={gd.stage}。");
        }

        gd.UpdateLease();
        if (GameState.Instance != null)
        {
            gd.UpdateStatusLine(GameState.Instance);
        }
    }

    private static void CaptureWeather(SaveData data)
    {
        var wn = WorldNet.Instance;
        var ws = WeatherSystem.Instance;
        if (wn == null && ws == null) return;

        var snap = new WeatherSnapshot { HasData = true };

        if (wn != null)
        {
            var s = wn.Sync;
            snap.OriginA = s.originA;
            snap.OriginB = s.originB;
            snap.WeatherLeg = s.weatherLeg;
            snap.WeatherKind = s.weather;
            snap.Severity = s.severity;
            snap.WeatherFrom = s.weatherFrom;
            snap.WeatherTo = s.weatherTo;
            snap.WeatherFlags = s.weatherFlags;
            snap.Birds = s.birds;
            snap.FogVis = s.fogVis;
            snap.DepFogVis = s.depFogVis;
            snap.FogRoll = s.fogRoll;
            snap.FogLift = s.fogLift;

            var smp = wn.Sample;
            snap.SampleTurbulence = smp.turbulence;
            snap.SampleRain = smp.rain;
            snap.SampleHail = smp.hail;
            snap.SampleIcing = smp.icing;
            snap.SampleStorm = smp.storm;
            snap.SampleCloud = smp.cloud;

            var clk = wn.Clock;
            snap.ClockSeed = clk.seed;
            snap.ClockAnchorHours = clk.anchorHours;
            snap.ClockRate = clk.rate;

            var fr = wn.Front;
            snap.FrontId = fr.id;
            snap.FrontKind = fr.kind;
            snap.FrontIntensity = fr.intensity;
            snap.FrontRemainingSeconds = fr.end > NetworkTime.time ? (float)(fr.end - NetworkTime.time) : 0f;
            snap.FrontRampIn = fr.rampIn;
            snap.FrontRampOut = fr.rampOut;
            snap.FrontHeading = fr.heading;
            snap.FrontWind = fr.wind;
            snap.FrontAirport = fr.airport;
            snap.FrontFogVis = fr.fogVis;
        }

        if (ws != null)
        {
            snap.WindDirX = ws.windTargetDir.x;
            snap.WindDirY = ws.windTargetDir.y;
            snap.WindDirZ = ws.windTargetDir.z;
            snap.WindSpeed = ws.windTargetSpeed;
            snap.CloudTarget = ws.cloudTarget;
        }

        data.WeatherState = snap;
    }

    private static void RestoreWeather(SaveData data)
    {
        var snap = data.WeatherState;
        if (snap == null || !snap.HasData) return;

        var wn = WorldNet.Instance;
        var ws = WeatherSystem.Instance;
        var wm = WorldManager.Instance;

        var lw = new LegWeather
        {
            leg = snap.WeatherLeg,
            from = snap.WeatherFrom,
            to = snap.WeatherTo,
            kind = (WeatherKind)snap.WeatherKind,
            severity = snap.Severity,
            flags = (WeatherFlags)snap.WeatherFlags,
            birds = (BirdSpecies)snap.Birds,
            fogVis = snap.FogVis,
            depFogVis = snap.DepFogVis,
            fogRoll = snap.FogRoll,
            fogLift = snap.FogLift
        };

        if (wm != null)
        {
            wm.SetLegWeather(ref lw);
        }
        if (ws != null)
        {
            ws.SetLegWeather(ref lw);
            ws.windTargetDir = new Vector3(snap.WindDirX, snap.WindDirY, snap.WindDirZ);
            ws.windTargetSpeed = snap.WindSpeed;
            ws.cloudTarget = snap.CloudTarget;
        }

        if (wn != null)
        {
            var curSync = wn.Sync;
            curSync.seed = data.RunSeed;
            curSync.current = data.CurrentAirport;
            curSync.destination = data.DestinationAirport;
            curSync.originA = snap.OriginA;
            curSync.originB = snap.OriginB;
            curSync.weatherLeg = snap.WeatherLeg;
            curSync.weather = snap.WeatherKind;
            curSync.severity = snap.Severity;
            curSync.weatherFrom = snap.WeatherFrom;
            curSync.weatherTo = snap.WeatherTo;
            curSync.weatherFlags = snap.WeatherFlags;
            curSync.birds = snap.Birds;
            curSync.fogVis = snap.FogVis;
            curSync.depFogVis = snap.DepFogVis;
            curSync.fogRoll = snap.FogRoll;
            curSync.fogLift = snap.FogLift;
            curSync.version++;
            wn.ServerSetSync(curSync);

            var smp = new WeatherSample
            {
                turbulence = snap.SampleTurbulence,
                rain = snap.SampleRain,
                hail = snap.SampleHail,
                icing = snap.SampleIcing,
                storm = snap.SampleStorm,
                cloud = snap.SampleCloud
            };
            wn.ServerSetSample(smp);

            var clk = wn.Clock;
            clk.seed = snap.ClockSeed;
            clk.anchorTime = NetworkTime.time;
            clk.anchorHours = snap.ClockAnchorHours;
            clk.rate = snap.ClockRate > 0f ? snap.ClockRate : clk.rate;
            clk.version++;
            wn.ServerSetClock(clk);

            if (snap.FrontIntensity > 0f && snap.FrontRemainingSeconds > 0f)
            {
                var fr = new WeatherFront
                {
                    id = snap.FrontId,
                    kind = snap.FrontKind,
                    intensity = snap.FrontIntensity,
                    start = NetworkTime.time,
                    end = NetworkTime.time + snap.FrontRemainingSeconds,
                    rampIn = snap.FrontRampIn,
                    rampOut = snap.FrontRampOut,
                    heading = snap.FrontHeading,
                    wind = snap.FrontWind,
                    airport = snap.FrontAirport,
                    fogVis = snap.FrontFogVis
                };
                wn.ServerSetFront(fr);
            }
            else
            {
                wn.ServerSetFront(default);
            }
        }
    }

    #endregion

    #region Capture & Restore: Plane (Pose, Sim, Controls, Engines, Electrical, Fuel, Doors, Hull)

    private static void CapturePlane(SaveData data, PlaneFlight plane)
    {
        var ps = new PlaneSnapshot();
        AirportSite curAirport = WorldManager.Instance != null ? WorldManager.Instance.Current : null;
        SiteFrame airportFrame = curAirport != null ? AirportSites.FrameOf(curAirport) : SiteFrame.Identity;
        float airportWorldYaw = curAirport != null ? airportFrame.rot.eulerAngles.y : 0f;

        if (plane != null)
        {
            Vector3 worldPos = plane.sim != null
                ? plane.sim.Pos
                : (WorldRoot.Instance != null ? WorldRoot.Instance.PlanePos : plane.transform.position);
            Quaternion worldRot = plane.sim != null
                ? plane.sim.Rotation
                : (WorldRoot.Instance != null ? WorldRoot.Instance.PlaneRot : plane.transform.rotation);

            Vector3 relPos = curAirport != null ? airportFrame.FromWorld(worldPos) : worldPos;
            Quaternion relRot = curAirport != null ? airportFrame.FromWorld(worldRot) : worldRot;

            ps.Pose = new PlanePoseSnapshot
            {
                HasPose = true,
                RelativeToAirport = curAirport != null,
                PosX = relPos.x,
                PosY = relPos.y,
                PosZ = relPos.z,
                RotX = relRot.x,
                RotY = relRot.y,
                RotZ = relRot.z,
                RotW = relRot.w
            };

            if (plane.sim != null)
            {
                var sim = plane.sim;
                Vector3 vel = sim.Velocity;
                ps.Sim = new FlightSimSnapshot
                {
                    HasSim = true,
                    Speed = sim.Speed,
                    Gamma = sim.Gamma,
                    RelTrack = sim.Track - airportWorldYaw,
                    RelHeading = sim.Heading - airportWorldYaw,
                    Pitch = sim.Pitch,
                    Roll = sim.Roll,
                    PitchRate = sim.PitchRate,
                    RollRate = sim.RollRate,
                    YawRate = sim.YawRate,
                    OnGround = sim.OnGround,
                    GearPos = sim.GearPos,
                    FlapPos = sim.FlapPos,
                    SteerAngle = sim.SteerAngle,
                    Backward = sim.Backward,
                    Braking = sim.Braking,
                    VelX = vel.x,
                    VelY = vel.y,
                    VelZ = vel.z
                };
            }

            if (plane.throttle != null)
            {
                ps.Throttles = new float[plane.throttle.Length];
                for (int i = 0; i < plane.throttle.Length; i++)
                {
                    ps.Throttles[i] = plane.throttle[i];
                }
            }

            ps.ParkingBrake = plane.parkingBrake;
            ps.ReverseGear = plane.reverseGear;
            ps.FlapSetting = plane.flapSetting;
            ps.GearDownCmd = plane.gearDownCmd;
            ps.SeatbeltSign = plane.seatbeltSign;
            ps.WheelDamage = plane.WheelDamage;
            ps.IceSync = plane.NetworkiceSync;
            ps.GearJammed = plane.gearJammed;
            ps.ApModes = plane.apModes;
            ps.ApAltTarget = plane.apAltTarget;
            ps.ApHdgTarget = plane.apHdgTarget;
            ps.ApSpdTarget = plane.apSpdTarget;
            ps.ApFault = plane.apFault;
            ps.CrosswindX = plane.crosswind.x;
            ps.CrosswindY = plane.crosswind.y;
        }

        // 点火开关
        var ignition = UnityEngine.Object.FindObjectOfType<IgnitionSystem>();
        if (ignition != null)
        {
            ps.IgnitionPos = (byte)ignition.Position;
        }

        // 左右引擎
        var engines = UnityEngine.Object.FindObjectsOfType<EngineUnit>();
        if (engines != null)
        {
            for (int i = 0; i < engines.Count; i++)
            {
                var eu = engines[i];
                if (eu == null) continue;
                ps.Engines.Add(new EngineSnapshot
                {
                    Index = eu.index,
                    State = (byte)eu.state,
                    Rpm = eu.rpm,
                    Spool = eu.spool,
                    StartT = eu.startT,
                    Thrust = eu.thrust,
                    Temp = eu.temp,
                    Oil = eu.oil,
                    Health = eu.health,
                    Fire = eu.fire,
                    Faults = eu.faults,
                    Cowl = (byte)eu.cowl,
                    CowlRipLeft = eu.cowlRipLeft,
                    BottleUsed = eu.bottleUsed,
                    Repair = eu.repair,
                    BatState = (byte)eu.bat,
                    BattCondition = eu.batt,
                    JumpLeft = eu.jumpLeft
                });
            }
        }

        // 电气系统
        var elec = UnityEngine.Object.FindObjectOfType<ElectricalSystem>();
        if (elec != null)
        {
            ps.Electrical = new ElectricalSnapshot
            {
                HasData = true,
                Master = elec.master,
                GenL = elec.genL,
                GenR = elec.genR,
                CabinLightsSw = elec.cabinLightsSw,
                DeiceSw = elec.deiceSw,
                SeatbeltSw = elec.seatbeltSw,
                Battery = elec.battery,
                Breakers = elec.breakers,
                FusesBlown = elec.fusesBlown,
                TrippedMask = elec.trippedMask,
                PanelDoorOpen = elec.panelDoorOpen,
                PanelDoorPops = elec.panelDoorPops
            };
        }

        // 燃油系统
        var fuel = UnityEngine.Object.FindObjectOfType<FuelSystem>();
        if (fuel != null)
        {
            ps.Fuel = new FuelSnapshot
            {
                FuelL = fuel.fuelL,
                FuelR = fuel.fuelR,
                Reserve = fuel.reserveTank,
                Capacity = fuel.capacity,
                PumpL = fuel.pumpL,
                PumpR = fuel.pumpR,
                Crossfeed = fuel.crossfeed,
                Leaks = fuel.leaks,
                Caps = fuel.caps
            };
        }

        // 门开关与客舱温度
        var doors = UnityEngine.Object.FindObjectOfType<DoorSystem>();
        if (doors != null)
        {
            ps.Doors = new DoorSnapshot
            {
                HasData = true,
                OpenMask = doors.open,
                JammedMask = doors.jammed,
                BombedMask = doors.bombed,
                CabinTemp = doors.cabinTemp,
                VentOk = doors.ventOk
            };
        }

        // 厕所门/锁/状态
        var lav = UnityEngine.Object.FindObjectOfType<LavSystem>();
        if (lav != null)
        {
            ps.Lav = new LavSnapshot
            {
                HasData = true,
                DoorOpen = lav.DoorOpen,
                Locked = lav.Locked,
                Dirt = lav.Dirt,
                Clog = lav.Clog,
                Plunge = lav.Plunge,
                FlushCount = lav.FlushCount
            };
        }

        // 厨房门帘/冰箱门/烤箱门/微波炉门/设备状态
        var galley = UnityEngine.Object.FindObjectOfType<GalleySystem>();
        if (galley != null)
        {
            ps.Galley = new GalleySnapshot
            {
                HasData = true,
                CurtainClosed = galley.CurtainClosed,
                FridgeOpen = galley.FridgeOpen,
                CoffeePhase = galley.Coffee.phase,
                CoffeeJammed = galley.Coffee.jammed,
                CoffeeWhacks = galley.Coffee.whacks,
                KettlePhase = galley.Kettle.phase,
                OvenOn = galley.Oven.on,
                OvenDoorOpen = galley.Oven.doorOpen,
                OvenFire = galley.Oven.fire,
                MicroOn = galley.Micro.on,
                MicroDoorOpen = galley.Micro.doorOpen,
                MicroFire = galley.Micro.fire,
                PrepBun = galley.Prep.bun,
                PrepPatty = galley.Prep.patty,
                PrepCheese = galley.Prep.cheese,
                PrepPattyCook = galley.Prep.pattyCook,
                SinkPresses = galley.SinkPresses
            };
        }

        // 垃圾桶
        var bins = UnityEngine.Object.FindObjectsOfType<TrashBin>();
        if (bins != null)
        {
            for (int i = 0; i < bins.Count; i++)
            {
                if (bins[i] != null) ps.TrashFills.Add(bins[i].Fill);
            }
        }

        // 客舱座椅配置
        var pd = PassengerDirector.Instance;
        if (pd != null)
        {
            byte mask = pd.rowMask;
            if (mask == 0)
            {
                for (int s = 0; s < 8; s++)
                    if (pd.IsRowInstalled(s)) mask |= (byte)(1 << s);
            }
            ps.SeatRowMask = mask;
            ps.PremiumSeats = pd.premiumSeats;
            if (pd.progress != null)
            {
                for (int i = 0; i < pd.progress.Length; i++)
                    ps.SeatInstallProgress.Add(pd.progress[i]);
            }
        }

        // 机体损伤与涂装
        var hull = UnityEngine.Object.FindObjectOfType<HullSystem>();
        if (hull != null)
        {
            if (NetworkServer.active)
            {
                hull.Push();
                hull.PushJets();
                hull.PushSections();
            }

            var hs = new HullSnapshot
            {
                HasData = true,
                Breaches = hull.breaches,
                Ice = hull.lDamage != null ? hull.lIce : hull.ice,
                JetSync = hull.jetSync,
                SectSync = hull.sectSync
            };

            if (hull.lDamage != null && hull.lDamage.Length > 0)
                for (int i = 0; i < hull.lDamage.Length; i++) hs.Damage.Add(hull.lDamage[i]);
            else if (hull.damage != null)
                for (int i = 0; i < hull.damage.Count; i++) hs.Damage.Add(hull.damage[i]);

            if (hull.lDirt != null && hull.lDirt.Length > 0)
                for (int i = 0; i < hull.lDirt.Length; i++) hs.Dirt.Add(hull.lDirt[i]);
            else if (hull.dirt != null)
                for (int i = 0; i < hull.dirt.Count; i++) hs.Dirt.Add(hull.dirt[i]);

            if (hull.lMain != null && hull.lMain.Length > 0)
                for (int i = 0; i < hull.lMain.Length; i++) hs.MainColors.Add(PackColor(ToColor32(hull.lMain[i])));
            else if (hull.mainColor != null)
                for (int i = 0; i < hull.mainColor.Count; i++) hs.MainColors.Add(PackColor(hull.mainColor[i]));

            if (hull.lAccent != null && hull.lAccent.Length > 0)
                for (int i = 0; i < hull.lAccent.Length; i++) hs.AccentColors.Add(PackColor(ToColor32(hull.lAccent[i])));
            else if (hull.accentColor != null)
                for (int i = 0; i < hull.accentColor.Count; i++) hs.AccentColors.Add(PackColor(hull.accentColor[i]));

            ps.Hull = hs;
        }

        data.Plane = ps;
    }

    private static void RestorePlane(SaveData data, PlaneFlight plane)
    {
        var ps = data.Plane;

        // 兼容 v1 存档
        if (ps == null || ps.Pose == null || !ps.Pose.HasPose)
        {
            if (data.PlanePose != null && data.PlanePose.HasPose)
            {
                ps ??= new PlaneSnapshot();
                ps.Pose = data.PlanePose;
                ps.WheelDamage = data.WheelDamage;
                if (data.Fuel != null) ps.Fuel = data.Fuel;
            }
        }

        if (plane != null && ps != null)
        {
            plane.ClearCrash();
            ResetAircraftSystemsBeforeRestore();
            RestorePlanePoseAndSim(data, plane);

            if (ps.Throttles != null && plane.throttle != null)
            {
                int n = Math.Min(ps.Throttles.Length, plane.throttle.Length);
                for (int i = 0; i < n; i++)
                {
                    plane.throttle[i] = ps.Throttles[i];
                }
            }

            plane.NetworkparkingBrake = ps.ParkingBrake;
            plane.NetworkreverseGear = ps.ReverseGear;
            plane.NetworkflapSetting = ps.FlapSetting;
            plane.NetworkgearDownCmd = ps.GearDownCmd;
            plane.NetworkseatbeltSign = ps.SeatbeltSign;
            plane.NetworkwheelDamage = ps.WheelDamage;
            plane.NetworkiceSync = ps.IceSync;
            plane.NetworkgearJammed = ps.GearJammed;
            plane.NetworkapModes = ps.ApModes;
            plane.NetworkapAltTarget = ps.ApAltTarget;
            plane.NetworkapHdgTarget = ps.ApHdgTarget;
            plane.NetworkapSpdTarget = ps.ApSpdTarget;
            plane.NetworkapFault = ps.ApFault;
            plane.Networkcrosswind = new Vector2(ps.CrosswindX, ps.CrosswindY);
        }

        if (ps == null) return;

        // 电气系统（先于引擎，避免电源状态影响）
        var elec = UnityEngine.Object.FindObjectOfType<ElectricalSystem>();
        if (elec != null && ps.Electrical != null && ps.Electrical.HasData)
        {
            elec.Networkmaster = ps.Electrical.Master;
            elec.NetworkgenL = ps.Electrical.GenL;
            elec.NetworkgenR = ps.Electrical.GenR;
            elec.NetworkcabinLightsSw = ps.Electrical.CabinLightsSw;
            elec.NetworkdeiceSw = ps.Electrical.DeiceSw;
            elec.NetworkseatbeltSw = ps.Electrical.SeatbeltSw;
            elec.Networkbattery = ps.Electrical.Battery;
            elec.Networkbreakers = ps.Electrical.Breakers;
            elec.NetworkfusesBlown = ps.Electrical.FusesBlown;
            elec.NetworktrippedMask = ps.Electrical.TrippedMask;
            elec.NetworkpanelDoorOpen = ps.Electrical.PanelDoorOpen;
            elec.NetworkpanelDoorPops = ps.Electrical.PanelDoorPops;
        }

        // 点火与引擎
        var ignition = UnityEngine.Object.FindObjectOfType<IgnitionSystem>();
        if (ignition != null)
        {
            ignition.Networkpos = (IgnitionPos)ps.IgnitionPos;
        }

        var engines = UnityEngine.Object.FindObjectsOfType<EngineUnit>();
        if (engines != null && ps.Engines != null && ps.Engines.Count > 0)
        {
            for (int i = 0; i < engines.Count; i++)
            {
                var eu = engines[i];
                if (eu == null) continue;
                var es = ps.Engines.Find(e => e.Index == eu.index);
                if (es == null && i < ps.Engines.Count) es = ps.Engines[i];
                if (es == null) continue;

                eu.Networkstate = (EngineState)es.State;
                eu.state = (EngineState)es.State;
                eu.Networkrpm = es.Rpm;
                eu.rpm = es.Rpm;
                eu.spool = es.Spool;
                eu.startT = es.StartT;
                eu.Networkthrust = es.Thrust;
                eu.Networktemp = es.Temp;
                eu.Networkoil = es.Oil;
                eu.Networkhealth = es.Health;
                eu.Networkfire = es.Fire;
                eu.Networkfaults = es.Faults;
                eu.Networkcowl = (CowlState)es.Cowl;
                eu.NetworkcowlRipLeft = es.CowlRipLeft;
                eu.NetworkbottleUsed = es.BottleUsed;
                eu.Networkrepair = es.Repair;
                eu.Networkbat = (BatteryState)es.BatState;
                eu.Networkbatt = es.BattCondition;
                eu.NetworkjumpLeft = es.JumpLeft;
                eu.PushSync();
            }
        }

        // 燃油系统
        var fuel = UnityEngine.Object.FindObjectOfType<FuelSystem>();
        if (fuel != null && ps.Fuel != null)
        {
            fuel.fuelL = ps.Fuel.FuelL;
            fuel.fuelR = ps.Fuel.FuelR;
            fuel.reserveTank = ps.Fuel.Reserve;
            if (ps.Fuel.Capacity > 0f) fuel.Networkcapacity = ps.Fuel.Capacity;
            fuel.NetworkfuelL = ps.Fuel.FuelL;
            fuel.NetworkfuelR = ps.Fuel.FuelR;
            fuel.NetworkreserveSync = ps.Fuel.Reserve;
            fuel.ServerSetPump(0, ps.Fuel.PumpL);
            fuel.ServerSetPump(1, ps.Fuel.PumpR);
            fuel.ServerSetCrossfeed(ps.Fuel.Crossfeed);
            fuel.Networkleaks = ps.Fuel.Leaks;
            fuel.Networkcaps = ps.Fuel.Caps;
        }

        // 门开关
        var doors = UnityEngine.Object.FindObjectOfType<DoorSystem>();
        if (doors != null && ps.Doors != null && ps.Doors.HasData)
        {
            doors.Networkopen = ps.Doors.OpenMask;
            doors.Networkjammed = ps.Doors.JammedMask;
            doors.Networkbombed = ps.Doors.BombedMask;
            doors.NetworkcabinTemp = ps.Doors.CabinTemp;
            doors.NetworkventOk = ps.Doors.VentOk;
        }

        // 厕所
        var lav = UnityEngine.Object.FindObjectOfType<LavSystem>();
        if (lav != null && ps.Lav != null && ps.Lav.HasData)
        {
            lav.NetworkDoorOpen = ps.Lav.DoorOpen;
            lav.NetworkLocked = ps.Lav.Locked;
            lav.NetworkDirt = ps.Lav.Dirt;
            lav.NetworkClog = ps.Lav.Clog;
            lav.NetworkPlunge = ps.Lav.Plunge;
            lav.NetworkFlushCount = ps.Lav.FlushCount;
        }

        // 厨房
        var galley = UnityEngine.Object.FindObjectOfType<GalleySystem>();
        if (galley != null && ps.Galley != null && ps.Galley.HasData)
        {
            galley.NetworkCurtainClosed = ps.Galley.CurtainClosed;
            galley.NetworkFridgeOpen = ps.Galley.FridgeOpen;
            galley.NetworkCoffee = new CoffeeState
            {
                phase = ps.Galley.CoffeePhase,
                jammed = ps.Galley.CoffeeJammed,
                whacks = ps.Galley.CoffeeWhacks
            };
            galley.NetworkKettle = new PourState
            {
                phase = ps.Galley.KettlePhase
            };
            galley.NetworkOven = new CookerState
            {
                on = ps.Galley.OvenOn,
                doorOpen = ps.Galley.OvenDoorOpen,
                fire = ps.Galley.OvenFire
            };
            galley.NetworkMicro = new CookerState
            {
                on = ps.Galley.MicroOn,
                doorOpen = ps.Galley.MicroDoorOpen,
                fire = ps.Galley.MicroFire
            };
            galley.NetworkPrep = new PrepState
            {
                bun = ps.Galley.PrepBun,
                patty = ps.Galley.PrepPatty,
                cheese = ps.Galley.PrepCheese,
                pattyCook = ps.Galley.PrepPattyCook
            };
            galley.NetworkSinkPresses = ps.Galley.SinkPresses;
        }

        // 垃圾桶
        var bins = UnityEngine.Object.FindObjectsOfType<TrashBin>();
        if (bins != null && ps.TrashFills != null)
        {
            int n = Math.Min(bins.Count, ps.TrashFills.Count);
            for (int i = 0; i < n; i++)
            {
                if (bins[i] != null) bins[i].NetworkFill = ps.TrashFills[i];
            }
        }

        // 机体损伤与涂装
        var hull = UnityEngine.Object.FindObjectOfType<HullSystem>();
        if (hull != null && ps.Hull != null && ps.Hull.HasData)
        {
            hull.Networkbreaches = ps.Hull.Breaches;
            hull.lIce = ps.Hull.Ice;
            hull.Networkice = ps.Hull.Ice;
            hull.pushIn = 0f;
            hull.shownInit = false;

            if (ps.Hull.Damage != null)
            {
                if (hull.lDamage != null)
                {
                    int n = Math.Min(hull.lDamage.Length, ps.Hull.Damage.Count);
                    for (int i = 0; i < n; i++) hull.lDamage[i] = ps.Hull.Damage[i];
                }
                if (hull.coverDamage != null)
                {
                    int n = Math.Min(hull.coverDamage.Length, ps.Hull.Damage.Count);
                    for (int i = 0; i < n; i++) hull.coverDamage[i] = ps.Hull.Damage[i];
                }
                if (hull.damage != null)
                {
                    int n = Math.Min(hull.damage.Count, ps.Hull.Damage.Count);
                    for (int i = 0; i < n; i++) hull.damage[i] = ps.Hull.Damage[i];
                }
            }

            if (ps.Hull.Dirt != null)
            {
                if (hull.lDirt != null)
                {
                    int n = Math.Min(hull.lDirt.Length, ps.Hull.Dirt.Count);
                    for (int i = 0; i < n; i++) hull.lDirt[i] = ps.Hull.Dirt[i];
                }
                if (hull.dirt != null)
                {
                    int n = Math.Min(hull.dirt.Count, ps.Hull.Dirt.Count);
                    for (int i = 0; i < n; i++) hull.dirt[i] = ps.Hull.Dirt[i];
                }
            }

            if (ps.Hull.MainColors != null)
            {
                for (int i = 0; i < ps.Hull.MainColors.Count; i++)
                {
                    Color32 c32 = UnpackColor(ps.Hull.MainColors[i]);
                    Color c = ToColor(c32);
                    if (hull.lMain != null && i < hull.lMain.Length) hull.lMain[i] = c;
                    if (hull.shownMain != null && i < hull.shownMain.Length) hull.shownMain[i] = c;
                    if (hull.goalMain != null && i < hull.goalMain.Length) hull.goalMain[i] = c;
                    if (hull.paintCover != null && i < hull.paintCover.Length) hull.paintCover[i] = 0f;
                    if (hull.mainColor != null && i < hull.mainColor.Count) hull.mainColor[i] = c32;
                }
            }

            if (ps.Hull.AccentColors != null)
            {
                for (int i = 0; i < ps.Hull.AccentColors.Count; i++)
                {
                    Color32 c32 = UnpackColor(ps.Hull.AccentColors[i]);
                    Color c = ToColor(c32);
                    if (hull.lAccent != null && i < hull.lAccent.Length) hull.lAccent[i] = c;
                    if (hull.shownAccent != null && i < hull.shownAccent.Length) hull.shownAccent[i] = c;
                    if (hull.goalAccent != null && i < hull.goalAccent.Length) hull.goalAccent[i] = c;
                    if (hull.accentColor != null && i < hull.accentColor.Count) hull.accentColor[i] = c32;
                }
            }

            if (hull.jetHp != null)
            {
                int n = Math.Min(7, hull.jetHp.Length);
                for (int i = 0; i < n; i++)
                {
                    byte raw = (byte)((ps.Hull.JetSync >> (i * 8)) & 0xFFUL);
                    hull.jetHp[i] = raw / 255f;
                }
            }

            for (int i = 0; i < 7; i++)
            {
                byte packed = (byte)((ps.Hull.SectSync >> (i * 8)) & 0xFFUL);
                if (hull.sStage != null && i < hull.sStage.Length)
                    hull.sStage[i] = (byte)(packed & 0x03);
                if (hull.sProg != null && i < hull.sProg.Length)
                    hull.sProg[i] = ((packed >> 2) & 0x3F) / 63f;
            }

            hull.Push();
            hull.PushJets();
            hull.PushSections();
            hull.MarkAllDirty();
            hull.ApplyDirtyPanels();
        }

        float logFuelL = ps.Fuel != null ? ps.Fuel.FuelL : 0f;
        float logFuelR = ps.Fuel != null ? ps.Fuel.FuelR : 0f;
        float logIce = ps.Hull != null ? ps.Hull.Ice : 0f;
        string hullMain0 = hull?.mainColor != null && hull.mainColor.Count > 0
            ? $"#{PackColor(hull.mainColor[0]):X8}"
            : "none";
        string hullAccent0 = hull?.accentColor != null && hull.accentColor.Count > 0
            ? $"#{PackColor(hull.accentColor[0]):X8}"
            : "none";
        var logPd = PassengerDirector.Instance;
        string logRowMask = logPd != null ? $"0x{logPd.rowMask:X2}" : "null";
        int logRows = logPd != null ? logPd.RowCount : -1;
        int logSeats = logPd != null ? logPd.SeatCapacity : -1;
        Plugin.Logger?.LogInfo(
            $"[飞机恢复] HasSim={ps.Sim?.HasSim}, FuelL={logFuelL:F1}, FuelR={logFuelR:F1}, Master={ps.Electrical?.Master}, WheelDmg={ps.WheelDamage}, Breaches={ps.Hull?.Breaches}, Ice={logIce:F2}, HullPanel0={hullMain0}/{hullAccent0}, RowMask={logRowMask}, Rows={logRows}, Seats={logSeats}。");
    }

    private static void ResetAircraftSystemsBeforeRestore()
    {
        var hull = UnityEngine.Object.FindObjectOfType<HullSystem>();
        if (hull != null)
        {
            hull.ServerResetHoles();
            hull.ServerResetSections();
        }

        var fuel = UnityEngine.Object.FindObjectOfType<FuelSystem>();
        fuel?.ServerResetForRun();

        var engines = UnityEngine.Object.FindObjectsOfType<EngineUnit>();
        if (engines != null)
        {
            for (int i = 0; i < engines.Count; i++)
            {
                engines[i]?.ServerResetForRun();
            }
        }

        var elec = UnityEngine.Object.FindObjectOfType<ElectricalSystem>();
        elec?.ServerResetForRun();

        GalleySystem.Instance?.ServerResetForNewRun();
        LavSystem.Instance?.ServerResetForNewRun();
        DrinkTrolley.Instance?.ServerResetPark();

        var bins = UnityEngine.Object.FindObjectsOfType<TrashBin>();
        if (bins != null)
        {
            for (int i = 0; i < bins.Count; i++)
            {
                bins[i]?.ServerReset();
            }
        }
    }

    private static void RestorePlanePoseAndSim(SaveData data, PlaneFlight plane)
    {
        if (plane == null) return;
        var pp = data.Plane?.Pose ?? data.PlanePose;
        if (pp == null || !pp.HasPose) return;

        AirportSite curAirport = WorldManager.Instance != null ? WorldManager.Instance.Current : null;
        SiteFrame airportFrame = curAirport != null ? AirportSites.FrameOf(curAirport) : SiteFrame.Identity;
        float airportWorldYaw = curAirport != null ? airportFrame.rot.eulerAngles.y : 0f;

        Vector3 savedPos = new Vector3(pp.PosX, pp.PosY, pp.PosZ);
        Quaternion savedRot = new Quaternion(pp.RotX, pp.RotY, pp.RotZ, pp.RotW);
        if (savedRot.x == 0f && savedRot.y == 0f && savedRot.z == 0f && savedRot.w == 0f)
            savedRot = Quaternion.identity;

        Vector3 worldPos = pp.RelativeToAirport && curAirport != null
            ? airportFrame.ToWorld(savedPos)
            : savedPos;
        Quaternion worldRot = pp.RelativeToAirport && curAirport != null
            ? airportFrame.ToWorld(savedRot)
            : savedRot;
        Vector3 worldVel = Vector3.zero;

        plane.needsInitialPark = false;

        var simSnap = data.Plane?.Sim;
        bool isLegacyV3Sim = data.Version <= 3 && pp.RelativeToAirport && curAirport != null && simSnap != null && simSnap.HasSim;
        if (isLegacyV3Sim)
        {
            float thetaRad = worldRot.eulerAngles.y * Mathf.Deg2Rad;
            float denom = 2f * (1f + Mathf.Cos(thetaRad));
            if (Mathf.Abs(denom) > 1e-4f)
            {
                Vector3 invRotW = Quaternion.Inverse(worldRot) * worldPos;
                worldPos = new Vector3(
                    (worldPos.x + invRotW.x) / denom,
                    worldPos.y * 0.5f,
                    (worldPos.z + invRotW.z) / denom);
            }
            else
            {
                PlaneFlight.StandWorldPose(curAirport, out worldPos, out _);
            }
        }

        if (plane.sim != null && simSnap != null && simSnap.HasSim)
        {
            var sim = plane.sim;
            sim.Pos = worldPos;
            sim.Speed = simSnap.Speed;
            sim.Gamma = simSnap.Gamma;
            if (isLegacyV3Sim)
            {
                float trueHeading = worldRot.eulerAngles.y;
                sim.Heading = trueHeading;
                sim.Track = simSnap.Speed < 0.05f ? trueHeading : (trueHeading + (simSnap.RelTrack - simSnap.RelHeading));
            }
            else
            {
                sim.Track = airportWorldYaw + simSnap.RelTrack;
                sim.Heading = airportWorldYaw + simSnap.RelHeading;
            }
            sim.Pitch = simSnap.Pitch;
            sim.Roll = simSnap.Roll;
            sim.PitchRate = simSnap.PitchRate;
            sim.RollRate = simSnap.RollRate;
            sim.YawRate = simSnap.YawRate;
            sim.OnGround = simSnap.OnGround;
            sim.GearPos = simSnap.GearPos;
            sim.FlapPos = simSnap.FlapPos;
            sim.SteerAngle = simSnap.SteerAngle;
            sim.Backward = simSnap.Backward;
            sim.Braking = simSnap.Braking;
            worldRot = sim.Rotation;
            worldVel = sim.GroundVelocity;
            if (float.IsNaN(worldVel.x) || float.IsNaN(worldVel.y) || float.IsNaN(worldVel.z))
                worldVel = Vector3.zero;
        }

        // 游戏采用 WorldRoot 浮动原点：飞机在场景坐标中始终保持 (0,0,0) 与 identity，
        // 世界位姿变化通过 WorldRoot.SetPlanePose 反向平移/旋转整个世界（含机场与地形）实现。
        if (WorldRoot.Instance != null)
            WorldRoot.Instance.SetPlanePose(worldPos, worldRot, worldVel);

        var rb = plane.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = Vector3.zero;
            rb.rotation = Quaternion.identity;
        }

        plane.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        plane.CapturePhys(true);

        Vector3 rotEuler = worldRot.eulerAngles;
        Plugin.Logger?.LogInfo(
            $"[载入-飞机位姿] 已同步 WorldRoot 与 PlaneFlight: worldPos=({worldPos.x:F2},{worldPos.y:F2},{worldPos.z:F2}), worldRotEuler=({rotEuler.x:F1},{rotEuler.y:F1},{rotEuler.z:F1}), worldVel=({worldVel.x:F2},{worldVel.y:F2},{worldVel.z:F2}), airportWorldYaw={airportWorldYaw:F1}, hasWorldRoot={WorldRoot.Instance != null}");
    }

    private static byte ResolveTargetSeatRowMask(SaveData data)
    {
        var ps = data?.Plane;
        if (ps != null && ps.SeatRowMask != 0)
            return ps.SeatRowMask;

        int maxPax = 0;
        if (data?.Director?.Options != null)
        {
            foreach (var opt in data.Director.Options)
            {
                if (opt != null && opt.Pax > maxPax)
                    maxPax = opt.Pax;
            }
        }

        if (maxPax > 12)
        {
            int rows = Math.Clamp((maxPax + 3) / 4, 3, 8);
            return (byte)((1 << rows) - 1);
        }

        return 0x07;
    }

    private static void RestorePassengerSeats(SaveData data)
    {
        var pd = PassengerDirector.Instance;
        if (pd == null || !NetworkServer.active || data == null) return;

        byte targetMask = ResolveTargetSeatRowMask(data);
        bool targetPremium = (data.Version >= 5 && data.Plane != null && data.Plane.SeatRowMask != 0)
            ? data.Plane.PremiumSeats
            : (pd.premiumSeats || (data.Upgrades != null && data.Upgrades.Contains("seat_premium")));

        if (pd.premiumSeats != targetPremium)
            pd.ServerSetPremium(targetPremium, false);

        for (int slot = 0; slot < 8; slot++)
        {
            if ((targetMask & (1 << slot)) != 0 && !pd.IsRowInstalled(slot))
                pd.ServerInstallRow(slot, true);
        }

        pd.EnsureInstallLists();
        if (data.Plane?.SeatInstallProgress != null && pd.progress != null)
        {
            int n = Math.Min(pd.progress.Length, data.Plane.SeatInstallProgress.Count);
            for (int i = 0; i < n; i++)
            {
                float p = data.Plane.SeatInstallProgress[i];
                pd.progress[i] = p;
                pd.SetProgress(i, p, 0);
            }
        }

        pd.RefreshMounts();
    }

    private static uint PackColor(Color32 c) =>
        ((uint)c.r << 24) | ((uint)c.g << 16) | ((uint)c.b << 8) | c.a;

    private static Color32 UnpackColor(uint v) =>
        new((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);

    private static Color32 ToColor32(Color c) => new(
        (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255),
        (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255),
        (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255),
        (byte)Mathf.Clamp(Mathf.RoundToInt(c.a * 255f), 0, 255));

    private static Color ToColor(Color32 c) =>
        new(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);

    #endregion

    #region Capture & Restore: Ground Equipment (FuelTruck, AirStairs, CargoLoaders, BaggageTug, BaggageCart, DrinkTrolley)

    private static void CaptureGroundEquipment(SaveData data, PlaneFlight plane)
    {
        var ge = new GroundEquipmentSnapshot();
        AirportSite curAirport = WorldManager.Instance != null ? WorldManager.Instance.Current : null;
        Transform airportT = curAirport != null ? curAirport.transform : null;
        float airportYaw = airportT != null ? airportT.eulerAngles.y : 0f;

        // 加油车
        var fuelTruck = FuelTruck.Instance ?? UnityEngine.Object.FindObjectOfType<FuelTruck>();
        var fuelDrive = FuelTruckDrive.Instance ?? UnityEngine.Object.FindObjectOfType<FuelTruckDrive>();
        if (fuelTruck != null || fuelDrive != null)
        {
            Transform ftT = fuelDrive != null ? fuelDrive.transform : fuelTruck.transform;
            Vector3 relPos = airportT != null ? airportT.InverseTransformPoint(ftT.position) : ftT.position;
            float relYaw = ftT.eulerAngles.y - airportYaw;

            ge.FuelTruck = new FuelTruckSnapshot
            {
                RelPosX = relPos.x,
                RelPosY = relPos.y,
                RelPosZ = relPos.z,
                RelYaw = relYaw,
                Handbrake = fuelDrive != null && fuelDrive.simHandbrake,
                Manual = fuelDrive != null && fuelDrive.manual,
                Deployed = fuelTruck != null && fuelTruck.deployed,
                HoseState = fuelTruck != null ? (byte)fuelTruck.NetworkhoseState : (byte)0,
                Slot = fuelTruck != null ? fuelTruck.Networkslot : (sbyte)-1,
                Flowing = fuelTruck != null && fuelTruck.Networkflowing,
                SessionLitres = fuelTruck != null ? fuelTruck.NetworksessionLitres : 0f,
                SessionCost = fuelTruck != null ? fuelTruck.NetworksessionCost : 0
            };
        }

        // 上下梯（登机梯）
        var stairsArr = UnityEngine.Object.FindObjectsOfType<AirStairs>();
        if (stairsArr != null)
        {
            for (int i = 0; i < stairsArr.Count; i++)
            {
                var st = stairsArr[i];
                if (st == null) continue;
                ge.AirStairs.Add(new AirStairsSnapshot
                {
                    SitePosX = st.sitePos.x,
                    SitePosY = st.sitePos.y,
                    SitePosZ = st.sitePos.z,
                    SiteYaw = st.siteYaw,
                    Docked = st.docked,
                    Tipped = st.tipped
                });
            }
        }

        // 货舱传送带车
        var loaders = UnityEngine.Object.FindObjectsOfType<CargoLoaders>();
        if (loaders != null)
        {
            for (int i = 0; i < loaders.Count; i++)
            {
                var cl = loaders[i];
                if (cl == null) continue;
                ge.CargoLoaders.Add(new CargoLoaderSnapshot
                {
                    Deployed = cl.deployed,
                    DockSitePosX = cl.dockSitePos.x,
                    DockSitePosY = cl.dockSitePos.y,
                    DockSitePosZ = cl.dockSitePos.z,
                    DockSiteYaw = cl.dockSiteYaw,
                    BeltOn = cl.beltOn,
                    BeltRunning = cl.beltRunning,
                    BeltDist = cl.beltDist
                });
            }
        }

        // 行李拖车
        var tug = BaggageTug.Instance ?? UnityEngine.Object.FindObjectOfType<BaggageTug>();
        if (tug != null)
        {
            Vector3 relPos = airportT != null ? airportT.InverseTransformPoint(tug.transform.position) : tug.transform.position;
            float relYaw = tug.transform.eulerAngles.y - airportYaw;
            ge.BaggageTug = new BaggageTugSnapshot
            {
                RelPosX = relPos.x,
                RelPosY = relPos.y,
                RelPosZ = relPos.z,
                RelYaw = relYaw,
                Handbrake = tug.simHandbrake
            };
        }

        // 行李车
        var carts = UnityEngine.Object.FindObjectsOfType<BaggageCart>();
        if (carts != null)
        {
            for (int i = 0; i < carts.Count; i++)
            {
                var bc = carts[i];
                if (bc == null) continue;
                ge.BaggageCarts.Add(new BaggageCartSnapshot
                {
                    IsArrival = bc.arrival,
                    SitePosX = bc.sitePos.x,
                    SitePosY = bc.sitePos.y,
                    SitePosZ = bc.sitePos.z,
                    SiteYaw = bc.siteYaw,
                    AtAnchor = bc.NetworkatAnchor
                });
            }
        }

        // 机舱饮料推车
        var trolley = DrinkTrolley.Instance ?? UnityEngine.Object.FindObjectOfType<DrinkTrolley>();
        if (trolley != null)
        {
            ge.DrinkTrolley = new DrinkTrolleySnapshot
            {
                PoseX = trolley.pose.x,
                PoseY = trolley.pose.y,
                PoseZ = trolley.pose.z,
                Braked = trolley.braked,
                Side = trolley.side
            };
        }

        data.GroundEquipment = ge;
    }

    private static void RestoreGroundEquipment(SaveData data)
    {
        var ge = data.GroundEquipment;
        if (ge == null) return;

        AirportSite curAirport = WorldManager.Instance != null ? WorldManager.Instance.Current : null;
        Transform airportT = curAirport != null ? curAirport.transform : null;
        float airportYaw = airportT != null ? airportT.eulerAngles.y : 0f;

        // 加油车
        if (ge.FuelTruck != null)
        {
            var fuelTruck = FuelTruck.Instance ?? UnityEngine.Object.FindObjectOfType<FuelTruck>();
            var fuelDrive = FuelTruckDrive.Instance ?? UnityEngine.Object.FindObjectOfType<FuelTruckDrive>();
            if (fuelDrive != null)
            {
                Vector3 scenePos = new Vector3(ge.FuelTruck.RelPosX, ge.FuelTruck.RelPosY, ge.FuelTruck.RelPosZ);
                if (airportT != null) scenePos = airportT.TransformPoint(scenePos);
                float sceneYaw = airportYaw + ge.FuelTruck.RelYaw;

                fuelDrive.ServerClearDriver();
                fuelDrive.Networkmanual = ge.FuelTruck.Manual;
                fuelDrive.simHandbrake = ge.FuelTruck.Handbrake;
                fuelDrive.DevTeleport(scenePos, sceneYaw);
            }
            if (fuelTruck != null)
            {
                fuelTruck.Networkdeployed = ge.FuelTruck.Deployed;
                if (ge.FuelTruck.Deployed)
                {
                    fuelTruck.ServerSnapDeployed();
                }
                else
                {
                    fuelTruck.ServerStow(false);
                }

                // 若曾连接油管且非手持，恢复连接槽位
                var hose = (HoseState)ge.FuelTruck.HoseState;
                if (hose == HoseState.Connected && ge.FuelTruck.Slot >= 0)
                {
                    fuelTruck.NetworkhoseState = HoseState.Connected;
                    fuelTruck.Networkslot = ge.FuelTruck.Slot;
                    fuelTruck.Networkflowing = ge.FuelTruck.Flowing;
                }
                else
                {
                    fuelTruck.NetworkhoseState = HoseState.Stowed;
                    fuelTruck.Networkslot = -1;
                    fuelTruck.Networkflowing = false;
                }
                fuelTruck.NetworksessionLitres = ge.FuelTruck.SessionLitres;
                fuelTruck.NetworksessionCost = ge.FuelTruck.SessionCost;
            }
        }

        // 上下梯（登机梯）
        var stairsArr = UnityEngine.Object.FindObjectsOfType<AirStairs>();
        if (stairsArr != null && ge.AirStairs != null && ge.AirStairs.Count > 0)
        {
            int n = Math.Min(stairsArr.Count, ge.AirStairs.Count);
            for (int i = 0; i < n; i++)
            {
                var st = stairsArr[i];
                var ss = ge.AirStairs[i];
                if (st == null || ss == null) continue;

                st.ServerRelease();
                st.NetworksitePos = new Vector3(ss.SitePosX, ss.SitePosY, ss.SitePosZ);
                st.NetworksiteYaw = ss.SiteYaw;
                st.sitePos = st.NetworksitePos;
                st.siteYaw = ss.SiteYaw;
                st.Networktipped = ss.Tipped;
                st.tipped = ss.Tipped;

                if (ss.Docked)
                {
                    st.ServerDock();
                }
                else
                {
                    st.ServerUndock();
                }
            }
        }

        // 货舱传送带车
        var loaders = UnityEngine.Object.FindObjectsOfType<CargoLoaders>();
        if (loaders != null && ge.CargoLoaders != null && ge.CargoLoaders.Count > 0)
        {
            int n = Math.Min(loaders.Count, ge.CargoLoaders.Count);
            for (int i = 0; i < n; i++)
            {
                var cl = loaders[i];
                var ls = ge.CargoLoaders[i];
                if (cl == null || ls == null) continue;

                cl.Networkdeployed = ls.Deployed;
                cl.NetworkdockSitePos = new Vector3(ls.DockSitePosX, ls.DockSitePosY, ls.DockSitePosZ);
                cl.NetworkdockSiteYaw = ls.DockSiteYaw;
                cl.NetworkbeltOn = ls.BeltOn;
                cl.NetworkbeltRunning = ls.BeltRunning;
                cl.NetworkbeltDist = ls.BeltDist;
            }
        }

        // 行李拖车
        var tug = BaggageTug.Instance ?? UnityEngine.Object.FindObjectOfType<BaggageTug>();
        if (tug != null && ge.BaggageTug != null)
        {
            Vector3 scenePos = new Vector3(ge.BaggageTug.RelPosX, ge.BaggageTug.RelPosY, ge.BaggageTug.RelPosZ);
            if (airportT != null) scenePos = airportT.TransformPoint(scenePos);
            float sceneYaw = airportYaw + ge.BaggageTug.RelYaw;

            tug.ServerClearDriver();
            tug.simHandbrake = ge.BaggageTug.Handbrake;
            tug.DevTeleport(scenePos, sceneYaw);
        }

        // 行李车
        var carts = UnityEngine.Object.FindObjectsOfType<BaggageCart>();
        if (carts != null && ge.BaggageCarts != null && ge.BaggageCarts.Count > 0)
        {
            foreach (var cs in ge.BaggageCarts)
            {
                BaggageCart target = null;
                for (int i = 0; i < carts.Count; i++)
                {
                    if (carts[i] != null && carts[i].arrival == cs.IsArrival)
                    {
                        target = carts[i];
                        break;
                    }
                }
                if (target == null) continue;

                target.NetworksitePos = new Vector3(cs.SitePosX, cs.SitePosY, cs.SitePosZ);
                target.NetworksiteYaw = cs.SiteYaw;
                target.NetworkatAnchor = cs.AtAnchor;
            }
        }

        // 饮料推车
        var trolley = DrinkTrolley.Instance ?? UnityEngine.Object.FindObjectOfType<DrinkTrolley>();
        if (trolley != null && ge.DrinkTrolley != null)
        {
            trolley.ServerRelease();
            trolley.Networkpose = new Vector3(ge.DrinkTrolley.PoseX, ge.DrinkTrolley.PoseY, ge.DrinkTrolley.PoseZ);
            trolley.Networkbraked = ge.DrinkTrolley.Braked;
            trolley.Networkside = ge.DrinkTrolley.Side;
        }
    }

    #endregion

    #region Capture & Restore: Players, Plane Sockets, Free World Items, Messes

    private static Dictionary<string, ItemSocket> BuildPlaneSocketMap()
    {
        var map = new Dictionary<string, ItemSocket>(StringComparer.Ordinal);

        var toolRacks = UnityEngine.Object.FindObjectsOfType<ToolRackStation>();
        if (toolRacks != null)
        {
            for (int r = 0; r < toolRacks.Count; r++)
            {
                var tr = toolRacks[r];
                if (tr == null) continue;
                if (tr.cradle != null) map[$"ToolRack:{r}:cradle"] = tr.cradle;
                if (tr.hooks != null)
                    for (int h = 0; h < tr.hooks.Length; h++)
                        if (tr.hooks[h] != null) map[$"ToolRack:{r}:hook:{h}"] = tr.hooks[h];
            }
        }

        var coffees = UnityEngine.Object.FindObjectsOfType<CoffeeMachine>();
        if (coffees != null)
        {
            for (int i = 0; i < coffees.Count; i++)
            {
                var c = coffees[i];
                if (c == null) continue;
                if (c.cupSocket != null) map[$"Coffee:{i}:cup"] = c.cupSocket;
                if (c.podSocket != null) map[$"Coffee:{i}:pod"] = c.podSocket;
            }
        }

        var cookers = UnityEngine.Object.FindObjectsOfType<CookerStation>();
        if (cookers != null)
        {
            for (int i = 0; i < cookers.Count; i++)
            {
                if (cookers[i]?.rack != null) map[$"Cooker:{i}:rack"] = cookers[i].rack;
            }
        }

        var fridges = UnityEngine.Object.FindObjectsOfType<FridgeStation>();
        if (fridges != null)
        {
            for (int i = 0; i < fridges.Count; i++)
            {
                var f = fridges[i];
                if (f == null) continue;
                if (f.sockets != null)
                    for (int k = 0; k < f.sockets.Length; k++)
                        if (f.sockets[k] != null) map[$"Fridge:{i}:s:{k}"] = f.sockets[k];
                if (f.extraSockets != null)
                    for (int k = 0; k < f.extraSockets.Length; k++)
                        if (f.extraSockets[k] != null) map[$"Fridge:{i}:ex:{k}"] = f.extraSockets[k];
            }
        }

        var dispensers = UnityEngine.Object.FindObjectsOfType<HotWaterDispenser>();
        if (dispensers != null)
        {
            for (int i = 0; i < dispensers.Count; i++)
            {
                if (dispensers[i]?.cupSocket != null) map[$"HotWater:{i}:cup"] = dispensers[i].cupSocket;
            }
        }

        var tpHolders = UnityEngine.Object.FindObjectsOfType<TPHolderStation>();
        if (tpHolders != null)
        {
            for (int i = 0; i < tpHolders.Count; i++)
            {
                if (tpHolders[i]?.socket != null) map[$"TPHolder:{i}:s"] = tpHolders[i].socket;
            }
        }

        var trolleys = UnityEngine.Object.FindObjectsOfType<DrinkTrolley>();
        if (trolleys != null)
        {
            for (int i = 0; i < trolleys.Count; i++)
            {
                var dt = trolleys[i];
                if (dt?.slots == null) continue;
                for (int k = 0; k < dt.slots.Length; k++)
                    if (dt.slots[k] != null) map[$"DrinkTrolley:{i}:s:{k}"] = dt.slots[k];
            }
        }

        var sd = ServiceDirector.Instance;
        if (sd?.storage != null)
        {
            for (int i = 0; i < sd.storage.Length; i++)
            {
                if (sd.storage[i] != null) map[$"ServiceStorage:{i}"] = sd.storage[i];
            }
        }

        return map;
    }

    private static void CapturePlaneSockets(SaveData data)
    {
        var map = BuildPlaneSocketMap();
        foreach (var kv in map)
        {
            var cur = kv.Value.Current;
            if (cur != null && !string.IsNullOrEmpty(cur.ItemId))
            {
                data.SocketItems.Add(new SocketItemSnapshot
                {
                    SocketKey = kv.Key,
                    Item = SnapshotItem(cur)
                });
            }
        }
    }

    private static void RestorePlaneSockets(SaveData data)
    {
        if (data.SocketItems == null || data.SocketItems.Count == 0) return;
        var map = BuildPlaneSocketMap();
        int restored = 0;
        int missing = 0;

        foreach (var entry in data.SocketItems)
        {
            if (entry?.Item == null || string.IsNullOrEmpty(entry.Item.ItemId)) continue;
            if (!map.TryGetValue(entry.SocketKey, out var socket) || socket == null)
            {
                missing++;
                continue;
            }

            Vector3 pos = socket.Anchor != null ? socket.Anchor.position : socket.transform.position;
            Quaternion rot = socket.Anchor != null ? socket.Anchor.rotation : socket.transform.rotation;

            var captured = entry.Item;
            var spawned = Item.ServerSpawn(captured.ItemId, pos, rot, (Action<Item>)(it => ApplyItemSnapshot(it, captured)), false);
            if (spawned != null)
            {
                socket.ServerInsert(spawned);
                restored++;
            }
        }

        Plugin.Logger?.LogInfo($"[插槽物品] 还原 {restored}/{data.SocketItems.Count} 个服务插槽物品 (未找到插槽={missing})。");
    }

    private static void CapturePlayerInventoriesAndPoses(SaveData data, PlaneFlight plane)
    {
        if (PlayerNet.All == null) return;

        Transform planeT = plane != null ? plane.transform : null;
        AirportSite curAirport = WorldManager.Instance != null ? WorldManager.Instance.Current : null;
        Transform airportT = curAirport != null ? curAirport.transform : null;

        for (int pIdx = 0; pIdx < PlayerNet.All.Count; pIdx++)
        {
            var p = PlayerNet.All[pIdx];
            if (p == null) continue;

            var snap = new PlayerInventorySnapshot
            {
                SteamId = p.SteamId,
                DisplayName = p.DisplayName ?? $"Player{pIdx + 1}"
            };

            Vector3 worldPos = p.transform.position;
            float worldYaw = p.transform.eulerAngles.y;

            if (planeT != null && Vector3.Distance(worldPos, planeT.position) <= PlaneAttachMaxDist)
            {
                snap.HasPose = true;
                snap.RelativeToPlane = true;
                Vector3 lp = planeT.InverseTransformPoint(worldPos);
                snap.PosX = lp.x;
                snap.PosY = lp.y;
                snap.PosZ = lp.z;
                snap.Yaw = worldYaw - planeT.eulerAngles.y;
            }
            else if (airportT != null)
            {
                snap.HasPose = true;
                snap.RelativeToPlane = false;
                Vector3 lp = airportT.InverseTransformPoint(worldPos);
                snap.PosX = lp.x;
                snap.PosY = lp.y;
                snap.PosZ = lp.z;
                snap.Yaw = worldYaw - airportT.eulerAngles.y;
            }
            else
            {
                snap.HasPose = true;
                snap.RelativeToPlane = false;
                snap.PosX = worldPos.x;
                snap.PosY = worldPos.y;
                snap.PosZ = worldPos.z;
                snap.Yaw = worldYaw;
            }

            var inv = p.Inventory;
            if (inv != null)
            {
                int count = inv.slots != null ? inv.slots.Count : 0;
                for (int i = 0; i < count; i++)
                {
                    var item = inv.Get(i);
                    if (item != null && !string.IsNullOrEmpty(item.ItemId))
                    {
                        snap.Items.Add(SnapshotItem(item));
                    }
                }
            }

            data.Players.Add(snap);
        }
    }

    private static void CaptureFreeWorldItems(SaveData data, PlaneFlight plane)
    {
        var allItems = UnityEngine.Object.FindObjectsOfType<Item>();
        if (allItems == null) return;

        Transform planeT = plane != null ? plane.transform : null;
        AirportSite curAirport = WorldManager.Instance != null ? WorldManager.Instance.Current : null;
        Transform airportT = curAirport != null ? curAirport.transform : null;

        for (int i = 0; i < allItems.Count; i++)
        {
            var item = allItems[i];
            if (item == null || string.IsNullOrEmpty(item.ItemId)) continue;

            // 只保存玩家丢在地上或放在飞机上的自由物品（排除手持/背包和插槽中的物品）
            if (item.InSocket || item.IsHeld || !item.IsFree)
            {
                continue;
            }

            Vector3 scenePos = item.transform.position;
            Quaternion sceneRot = item.transform.rotation;

            bool onPlane = false;
            Vector3 relPos = scenePos;
            Quaternion relRot = sceneRot;

            if (planeT != null && Vector3.Distance(scenePos, planeT.position) <= PlaneAttachMaxDist)
            {
                onPlane = true;
                relPos = planeT.InverseTransformPoint(scenePos);
                relRot = Quaternion.Inverse(planeT.rotation) * sceneRot;
            }
            else if (airportT != null)
            {
                relPos = airportT.InverseTransformPoint(scenePos);
                relRot = Quaternion.Inverse(airportT.rotation) * sceneRot;
            }

            data.WorldItems.Add(new WorldItemSnapshot
            {
                Item = SnapshotItem(item),
                RelativeToPlane = onPlane,
                PosX = relPos.x,
                PosY = relPos.y,
                PosZ = relPos.z,
                RotX = relRot.x,
                RotY = relRot.y,
                RotZ = relRot.z,
                RotW = relRot.w
            });
        }
    }

    private static void CaptureMesses(SaveData data, PlaneFlight plane)
    {
        var messes = UnityEngine.Object.FindObjectsOfType<Mess>();
        if (messes == null) return;

        Transform planeT = plane != null ? plane.transform : null;
        AirportSite curAirport = WorldManager.Instance != null ? WorldManager.Instance.Current : null;
        Transform airportT = curAirport != null ? curAirport.transform : null;

        for (int i = 0; i < messes.Count; i++)
        {
            var m = messes[i];
            if (m == null) continue;

            Vector3 pos = m.transform.position;
            bool onPlane = false;
            Vector3 rel = pos;

            if (planeT != null && Vector3.Distance(pos, planeT.position) <= PlaneAttachMaxDist)
            {
                onPlane = true;
                rel = planeT.InverseTransformPoint(pos);
            }
            else if (airportT != null)
            {
                rel = airportT.InverseTransformPoint(pos);
            }

            data.Messes.Add(new MessSnapshot
            {
                Kind = (byte)m.Kind,
                Size = m.Size,
                RelativeToPlane = onPlane,
                PosX = rel.x,
                PosY = rel.y,
                PosZ = rel.z
            });
        }
    }

    private static void RestoreMesses(SaveData data, PlaneFlight plane)
    {
        var existing = UnityEngine.Object.FindObjectsOfType<Mess>();
        if (existing != null)
        {
            for (int i = 0; i < existing.Count; i++)
            {
                if (existing[i] != null) NetworkServer.Destroy(existing[i].gameObject);
            }
        }

        if (data.Messes == null || data.Messes.Count == 0) return;

        Transform planeT = plane != null ? plane.transform : null;
        AirportSite curAirport = WorldManager.Instance != null ? WorldManager.Instance.Current : null;
        Transform airportT = curAirport != null ? curAirport.transform : null;

        foreach (var ms in data.Messes)
        {
            Vector3 pos = new Vector3(ms.PosX, ms.PosY, ms.PosZ);
            if (ms.RelativeToPlane && planeT != null) pos = planeT.TransformPoint(pos);
            else if (!ms.RelativeToPlane && airportT != null) pos = airportT.TransformPoint(pos);

            Mess.ServerSpawnOrMerge((MessKind)ms.Kind, pos, ms.Size);
        }
    }

    private static ItemSnapshot SnapshotItem(Item item)
    {
        var c = item.Tint;
        var snap = new ItemSnapshot
        {
            ItemId = item.ItemId,
            State = item.State,
            Amount = item.Amount,
            Count = item.Count,
            TintR = c.r,
            TintG = c.g,
            TintB = c.b,
            TintA = c.a,
            Tag = item.Tag
        };

        var edible = item.GetComponent<EdibleItem>();
        if (edible != null) snap.EatPct = edible.eatPct;

        var cig = item.GetComponent<CigaretteItem>();
        if (cig != null) snap.CigarettePuffs = cig.puffs;

        var lug = item.GetComponent<LuggageItem>();
        if (lug != null) snap.LuggageLoot = lug.loot;

        return snap;
    }

    private static void ApplyItemSnapshot(Item item, ItemSnapshot snap)
    {
        if (item == null || snap == null) return;
        item.NetworkState = snap.State;
        item.NetworkAmount = snap.Amount;
        item.NetworkCount = snap.Count;
        item.NetworkTint = new Color32(snap.TintR, snap.TintG, snap.TintB, snap.TintA);
        item.NetworkTag = snap.Tag;

        var edible = item.GetComponent<EdibleItem>();
        if (edible != null) edible.NetworkeatPct = snap.EatPct;

        var cig = item.GetComponent<CigaretteItem>();
        if (cig != null) cig.Networkpuffs = snap.CigarettePuffs;

        var lug = item.GetComponent<LuggageItem>();
        if (lug != null) lug.Networkloot = snap.LuggageLoot;
    }

    private static void ClearInventoriesSocketsAndFreeItems()
    {
        int clearedInvItems = 0;
        int clearedSockets = 0;
        int clearedFreeItems = 0;

        if (PlayerNet.All != null)
        {
            for (int pIdx = 0; pIdx < PlayerNet.All.Count; pIdx++)
            {
                var p = PlayerNet.All[pIdx];
                if (p == null || p.Inventory == null) continue;
                var inv = p.Inventory;
                int count = inv.slots != null ? inv.slots.Count : 0;
                for (int i = 0; i < count; i++)
                {
                    var item = inv.Get(i);
                    if (item != null)
                    {
                        inv.ServerForget(item.netId);
                        NetworkServer.Destroy(item.gameObject);
                        clearedInvItems++;
                    }
                }
            }
        }

        // 清空飞机上服务插槽里的初始道具
        var map = BuildPlaneSocketMap();
        foreach (var kv in map)
        {
            var cur = kv.Value.Current;
            if (cur != null)
            {
                kv.Value.ServerConsume();
                clearedSockets++;
            }
        }

        // 销毁自由道具（确保不遗漏 starting kit 散落件）
        var allItems = UnityEngine.Object.FindObjectsOfType<Item>();
        if (allItems != null)
        {
            for (int i = 0; i < allItems.Count; i++)
            {
                var item = allItems[i];
                if (item != null && !item.InSocket && !item.IsHeld && item.IsFree)
                {
                    NetworkServer.Destroy(item.gameObject);
                    clearedFreeItems++;
                }
            }
        }

        Plugin.Logger?.LogInfo(
            $"[物品清理] 已清理 背包物品={clearedInvItems}, 服务插槽={clearedSockets}, 地面/机舱自由物品={clearedFreeItems}。");
    }

    private static void RestorePlayerInventoriesAndPoses(SaveData data, PlaneFlight plane)
    {
        if (PlayerNet.All == null) return;

        Transform planeT = plane != null ? plane.transform : null;
        AirportSite curAirport = WorldManager.Instance != null ? WorldManager.Instance.Current : null;
        Transform airportT = curAirport != null ? curAirport.transform : null;

        var usedIndices = new HashSet<int>();
        for (int pIdx = 0; pIdx < PlayerNet.All.Count; pIdx++)
        {
            var p = PlayerNet.All[pIdx];
            if (p == null) continue;

            int matchIdx = data.Players != null
                ? FindMatchingPlayerSnapshot(data.Players, p, usedIndices)
                : -1;

            if (matchIdx < 0)
            {
                if (p.IsDead || p.IsSpectator)
                {
                    Vector3 revPos = curAirport != null
                        ? curAirport.StandWorldPos + new Vector3(0f, 1.0f, 0f)
                        : (planeT != null ? planeT.position + new Vector3(0f, 1.5f, 0f) : Vector3.up * 2f);
                    p.ServerRevive(revPos, 0f, null);
                    p.ServerResetDeaths();
                    Plugin.Logger?.LogInfo($"[玩家恢复] 未匹配快照的玩家 '{p.DisplayName}' 已复活至备用点 {revPos.ToString("F2")}。");
                }
                continue;
            }

            usedIndices.Add(matchIdx);
            var snap = data.Players[matchIdx];

            TeleportPlayerFromSnapshot(p, snap, planeT, airportT, reviveIfDead: true);

            int given = 0;
            if (p.Inventory != null && snap.Items != null)
            {
                foreach (var itemSnap in snap.Items)
                {
                    if (string.IsNullOrEmpty(itemSnap.ItemId)) continue;
                    var captured = itemSnap;
                    p.Inventory.ServerGive(captured.ItemId, (Action<Item>)(it => ApplyItemSnapshot(it, captured)));
                    given++;
                }
            }

            Plugin.Logger?.LogInfo(
                $"[玩家恢复] '{p.DisplayName}' (SteamId={p.SteamId}) 已还原: RelToPlane={snap.RelativeToPlane}, 给与物品={given}, IsDeadAfter={p.IsDead}, Pos={p.transform.position.ToString("F2")}。");
        }
    }

    private static void RestorePlayerPosesOnly(SaveData data, PlaneFlight plane)
    {
        if (PlayerNet.All == null || data.Players == null || data.Players.Count == 0) return;

        Transform planeT = plane != null ? plane.transform : null;
        AirportSite curAirport = WorldManager.Instance != null ? WorldManager.Instance.Current : null;
        Transform airportT = curAirport != null ? curAirport.transform : null;

        var usedIndices = new HashSet<int>();
        for (int pIdx = 0; pIdx < PlayerNet.All.Count; pIdx++)
        {
            var p = PlayerNet.All[pIdx];
            if (p == null) continue;

            int matchIdx = FindMatchingPlayerSnapshot(data.Players, p, usedIndices);
            if (matchIdx < 0) continue;

            usedIndices.Add(matchIdx);
            TeleportPlayerFromSnapshot(p, data.Players[matchIdx], planeT, airportT, reviveIfDead: true);
        }
    }

    private static void TeleportPlayerFromSnapshot(PlayerNet p, PlayerInventorySnapshot snap, Transform planeT, Transform airportT, bool reviveIfDead = false)
    {
        if (p == null || snap == null || !snap.HasPose) return;

        Vector3 localPos = new Vector3(snap.PosX, snap.PosY, snap.PosZ);
        Vector3 targetPos = localPos;
        float targetYaw = snap.Yaw;

        if (snap.RelativeToPlane && planeT != null)
        {
            targetPos = planeT.TransformPoint(localPos);
            targetYaw = planeT.eulerAngles.y + snap.Yaw;
        }
        else if (!snap.RelativeToPlane && airportT != null)
        {
            targetPos = airportT.TransformPoint(localPos);
            targetYaw = airportT.eulerAngles.y + snap.Yaw;
        }

        if (p.IsSeated) p.ServerReleaseSeat();
        if (p.IsHauled) p.ServerHaulRelease();
        p.ServerClearStun();

        if (reviveIfDead && (p.IsDead || p.IsSpectator))
        {
            p.ServerRevive(targetPos, targetYaw, null);
            p.ServerResetDeaths();
        }

        p.ServerTeleport(targetPos, targetYaw);
    }

    private static int FindMatchingPlayerSnapshot(List<PlayerInventorySnapshot> snapshots, PlayerNet p, HashSet<int> used)
    {
        if (p.SteamId != 0)
        {
            for (int i = 0; i < snapshots.Count; i++)
            {
                if (!used.Contains(i) && snapshots[i].SteamId == p.SteamId) return i;
            }
        }

        if (!string.IsNullOrEmpty(p.DisplayName))
        {
            for (int i = 0; i < snapshots.Count; i++)
            {
                if (!used.Contains(i) && string.Equals(snapshots[i].DisplayName, p.DisplayName, StringComparison.Ordinal))
                    return i;
            }
        }

        for (int i = 0; i < snapshots.Count; i++)
        {
            if (!used.Contains(i)) return i;
        }

        return -1;
    }

    private static void RestoreFreeWorldItems(SaveData data, PlaneFlight plane)
    {
        if (data.WorldItems == null || data.WorldItems.Count == 0) return;

        Transform planeT = plane != null ? plane.transform : null;
        AirportSite curAirport = WorldManager.Instance != null ? WorldManager.Instance.Current : null;
        Transform airportT = curAirport != null ? curAirport.transform : null;
        int spawnedCount = 0;

        foreach (var ws in data.WorldItems)
        {
            if (ws?.Item == null || string.IsNullOrEmpty(ws.Item.ItemId)) continue;

            Vector3 relPos = new Vector3(ws.PosX, ws.PosY, ws.PosZ);
            Quaternion relRot = new Quaternion(ws.RotX, ws.RotY, ws.RotZ, ws.RotW);

            Vector3 scenePos = relPos;
            Quaternion sceneRot = relRot;

            if (ws.RelativeToPlane && planeT != null)
            {
                scenePos = planeT.TransformPoint(relPos);
                sceneRot = planeT.rotation * relRot;
            }
            else if (!ws.RelativeToPlane && airportT != null)
            {
                scenePos = airportT.TransformPoint(relPos);
                sceneRot = airportT.rotation * relRot;
            }

            // 轻微防穿地偏移，确保不会卡进地面碰撞体下
            scenePos.y += 0.04f;

            var captured = ws.Item;
            var spawned = Item.ServerSpawn(
                captured.ItemId,
                scenePos,
                sceneRot,
                (Action<Item>)(it => ApplyItemSnapshot(it, captured)),
                false
            );

            if (spawned != null)
            {
                spawnedCount++;
                spawned.SetWorld(true);
                spawned.ServerTeleport(scenePos, sceneRot);
                var rb = spawned.Body;
                if (rb != null)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.position = scenePos;
                    rb.rotation = sceneRot;
                }
            }
        }

        Plugin.Logger?.LogInfo($"[自由物品] 已还原 {spawnedCount}/{data.WorldItems.Count} 个地面/机舱自由物品。");
    }

    private static void CloseSummaryScreens()
    {
        var ui = UIRoot.Instance;
        int closedCount = 0;

        static void TryClose(UIScreen screen, ref int count, string label)
        {
            if (screen == null) return;
            bool wasActive = screen.IsOpen || screen.gameObject.activeInHierarchy;
            if (!wasActive) return;
            UIRoot.CloseIfOpen(screen);
            if (screen.gameObject.activeInHierarchy)
            {
                screen.CloseInstant();
            }
            count++;
            Plugin.Logger?.LogInfo($"[UI清理] 已关闭界面: {label}");
        }

        if (ui != null)
        {
            TryClose(ui.summary, ref closedCount, "RunSummaryScreen");
            TryClose(ui.report, ref closedCount, "FlightReportScreen");
            TryClose(ui.dialog, ref closedCount, "DialogScreen");
            TryClose(ui.list, ref closedCount, "ListScreen");
            TryClose(ui.dispatchCards, ref closedCount, "DispatchScreen");
        }

        var runSummary = UnityEngine.Object.FindObjectOfType<RunSummaryScreen>();
        if (runSummary != null && runSummary.gameObject.activeInHierarchy)
        {
            runSummary.CloseInstant();
            closedCount++;
        }

        var flightReport = UnityEngine.Object.FindObjectOfType<FlightReportScreen>();
        if (flightReport != null && flightReport.gameObject.activeInHierarchy)
        {
            flightReport.CloseInstant();
            closedCount++;
        }

        Plugin.Logger?.LogInfo($"[UI清理] 共关闭活动界面 {closedCount} 个。");
    }

    #endregion
}
