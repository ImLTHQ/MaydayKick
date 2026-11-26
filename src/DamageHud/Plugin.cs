using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Mayday.Flight;
using Mayday.Systems;
using Mayday.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DamageHud;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "com.mayday.damagehud";
    public const string PluginName = "DamageHud";
    public const string PluginVersion = "1.0.0";

    private const int HardMaxRows = 16;
    private const float PollInterval = 0.2f;

    internal static ManualLogSource LogSource;
    internal static ConfigEntry<int> ConfigMaxRows;
    internal static ConfigEntry<float> ConfigDamageThreshold;
    internal static ConfigEntry<int> ConfigHullSkinCountThreshold;
    internal static ConfigEntry<float> ConfigIceThreshold;

    private enum Severity
    {
        Minor = 1,    // 轻微（黄色）
        Major = 2,    // 严重（橙色）
        Critical = 3  // 危急（红色）
    }

    private sealed class DamageEntry
    {
        public Severity Level;
        public float Score;
        public string Part;
        public string Detail;
    }

    private static readonly string[] HullPanelLabels =
    {
        "机鼻蒙皮",
        "前左机身",
        "前右机身",
        "中左机身",
        "中右机身",
        "后左机身",
        "后右机身",
        "机尾蒙皮",
        "左机翼",
        "右机翼",
        "垂直尾翼",
        "水平尾翼"
    };

    private static readonly string[] DoorLabels =
    {
        "主舱门",
        "货舱门",
        "左应急门",
        "右应急门",
        "驾驶舱门",
        "内货舱门"
    };

    private static readonly List<DamageEntry> Entries = new();
    private static UIPanel _panel;
    private static readonly List<UIText> _rowTexts = new();
    private static UIText _moreText;
    private static float _nextPollAt;
    private static bool _hasOrigFlightKeysSlot;
    private static Vector2 _origFlightKeysSlot;

    public override void Load()
    {
        LogSource = Log;

        ConfigMaxRows = Config.Bind(
            "UI",
            "MaxRows",
            8,
            new ConfigDescription(
                "左下角最多显示的损坏条目数，超出部分折叠为“+N 项其他损坏”。",
                new AcceptableValueRange<int>(1, HardMaxRows)));

        ConfigDamageThreshold = Config.Bind(
            "Detection",
            "DamageThreshold",
            0.05f,
            new ConfigDescription(
                "连续受损值（如蒙皮损坏、引擎掉血）触发显示的最小阈值（0.05 = 5%）。",
                new AcceptableValueRange<float>(0.01f, 0.5f)));

        ConfigHullSkinCountThreshold = Config.Bind(
            "Detection",
            "HullSkinDamagedCountThreshold",
            1,
            new ConfigDescription(
                "机身蒙皮损坏部位个数显示阈值：仅当受损蒙皮部位数量大于该阈值时，才显示机身蒙皮损坏情况（默认 1，即至少 2 处蒙皮受损才显示；设为 0 则 1 处受损即显示）。",
                new AcceptableValueRange<int>(0, 12)));

        ConfigIceThreshold = Config.Bind(
            "Detection",
            "IceThreshold",
            0.05f,
            new ConfigDescription(
                "机翼/机身结冰触发显示的最小阈值（0.05 = 5%）。",
                new AcceptableValueRange<float>(0.01f, 0.5f)));

        var harmony = new Harmony(PluginGuid);
        harmony.PatchAll(typeof(Hooks));

        LogSource.LogInfo($"{PluginName} v{PluginVersion} 已加载。");
    }

    private static void Add(Severity level, string part, string detail, float score = 1f)
    {
        Entries.Add(new DamageEntry
        {
            Level = level,
            Score = score,
            Part = part,
            Detail = detail
        });
    }

    private static void CollectHullDamage(float threshold)
    {
        var hull = SysRefs.Hull;
        if (hull == null) return;

        // 机身破洞/失压穿孔（按具体部位报告）
        for (int i = 0; i < HullPanelLabels.Length; i++)
        {
            if (hull.HoleOpen(i) || hull.Breached(i))
            {
                Add(Severity.Critical, HullPanelLabels[i], "破洞穿孔（失压）");
            }
        }

        // 机身区段结构受损（脱板/仅剩骨架）
        if (hull.OpenSectionCount > 0)
        {
            Add(Severity.Critical, "机身区段", $"{hull.OpenSectionCount} 处外壳缺失");
        }

        // 12 块机身蒙皮受损百分比（仅在受损部位个数 > 阈值时显示）
        if (hull.damage != null)
        {
            int count = Mathf.Min(hull.damage.Count, HullPanelLabels.Length);
            int damagedSkinCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (hull.damage[i] >= threshold)
                    damagedSkinCount++;
            }

            if (damagedSkinCount > ConfigHullSkinCountThreshold.Value)
            {
                for (int i = 0; i < count; i++)
                {
                    float dmg = hull.damage[i];
                    if (dmg < threshold) continue;

                    int pct = Mathf.Clamp(Mathf.RoundToInt(dmg * 100f), 1, 100);
                    var sev = dmg >= 0.7f ? Severity.Critical
                            : dmg >= 0.35f ? Severity.Major
                            : Severity.Minor;
                    Add(sev, HullPanelLabels[i], $"受损 {pct}%", dmg);
                }
            }
        }

        // 机身结冰
        float iceThr = ConfigIceThreshold.Value;
        if (hull.Ice >= iceThr)
        {
            int pct = Mathf.Clamp(Mathf.RoundToInt(hull.Ice * 100f), 1, 100);
            var sev = hull.Ice >= 0.6f ? Severity.Major : Severity.Minor;
            Add(sev, "机体结冰", $"结冰 {pct}%", hull.Ice);
        }
    }

    private static void CollectEngineDamage(float threshold)
    {
        var engines = SysRefs.Engines;
        if (engines == null) return;

        for (int i = 0; i < engines.Count; i++)
        {
            var eng = engines[i];
            if (eng == null) continue;

            string name = i == 0 ? "左引擎" : i == 1 ? "右引擎" : $"引擎#{i + 1}";

            if (eng.OnFire)
                Add(Severity.Critical, name, "起火！");

            if (eng.State == EngineState.Dead)
                Add(Severity.Critical, name, "已停机损坏");

            if (eng.Health < 1f - threshold)
            {
                float dmg = 1f - eng.Health;
                int dmgPct = Mathf.Clamp(Mathf.RoundToInt(dmg * 100f), 1, 100);
                var sev = eng.Health <= 0.3f ? Severity.Critical
                        : eng.Health <= 0.65f ? Severity.Major
                        : Severity.Minor;
                Add(sev, name, $"受损 {dmgPct}%", dmg);
            }

            if (eng.Cowl == CowlState.Loose)
                Add(Severity.Minor, $"{name}整流罩", "松动");
            else if (eng.Cowl == CowlState.Missing)
                Add(Severity.Major, $"{name}整流罩", "脱落缺失");

            if (eng.BatteryDead || eng.BatState == BatteryState.Dead || eng.BatState == BatteryState.Spent)
                Add(Severity.Major, $"{name}电瓶", "亏电失效");

            CollectEngineFaultFlags(name, eng.Faults);
        }
    }

    private static void CollectEngineFaultFlags(string engineName, ushort faults)
    {
        if (faults == 0) return;

        var reasons = new List<string>();
        Severity maxSev = Severity.Minor;

        void Check(ushort flag, string label, Severity sev)
        {
            if ((faults & flag) != 0)
            {
                reasons.Add(label);
                if (sev > maxSev) maxSev = sev;
            }
        }

        Check(EngineFault.Fire, "内部火情", Severity.Critical);
        Check(EngineFault.Swallow, "鸟击", Severity.Critical);
        Check(EngineFault.Prop, "螺旋桨受损", Severity.Major);
        Check(EngineFault.OilLeak, "漏油", Severity.Major);
        Check(EngineFault.NoPump, "油泵失效", Severity.Major);
        Check(EngineFault.Starving, "供油不足", Severity.Major);
        Check(EngineFault.Overheat, "过热", Severity.Major);
        Check(EngineFault.Intake, "进气堵塞", Severity.Minor);
        Check(EngineFault.Plugs, "火花塞故障", Severity.Minor);
        Check(EngineFault.LowOil, "缺机油", Severity.Minor);
        Check(EngineFault.Smoking, "冒烟", Severity.Minor);

        if (reasons.Count > 0)
            Add(maxSev, $"{engineName}故障", string.Join(" / ", reasons));
    }

    private static void CollectElectricalDamage()
    {
        var elec = SysRefs.Elec;
        if (elec == null) return;

        int trippedCount = 0;
        int blownCount = 0;
        int busCount = ElectricalSystem.BusCount;
        for (int b = 0; b < busCount; b++)
        {
            if (!elec.BreakerClosed(b) || ((elec.trippedMask >> b) & 1) != 0)
                trippedCount++;
            if (elec.FuseBlown(b))
                blownCount++;
        }

        if (trippedCount > 0)
            Add(Severity.Major, "电力断路器", $"{trippedCount} 路跳闸");

        if (blownCount > 0)
            Add(Severity.Major, "保险丝", $"{blownCount} 枚熔断");

        if (elec.Battery < 0.2f)
        {
            int pct = Mathf.Clamp(Mathf.RoundToInt(elec.Battery * 100f), 0, 100);
            Add(elec.Battery <= 0.02f ? Severity.Critical : Severity.Major, "主电池", $"电量仅剩 {pct}%", 1f - elec.Battery);
        }
    }

    private static void CollectFuelDamage()
    {
        var fuel = SysRefs.Fuel;
        if (fuel == null) return;

        if ((fuel.leaks & 1) != 0)
            Add(Severity.Critical, "左油箱", "燃油泄漏");
        if ((fuel.leaks & 2) != 0)
            Add(Severity.Critical, "右油箱", "燃油泄漏");

        int openCaps = CountBits(fuel.caps);
        if (openCaps > 0)
            Add(Severity.Minor, "油箱盖", $"{openCaps} 处未盖紧/缺失");
    }

    private static void CollectDoorDamage()
    {
        var doors = SysRefs.Doors;
        if (doors == null) return;

        if (doors.Depressurized)
            Add(Severity.Critical, "客舱气压", "失压");

        for (int i = 0; i < DoorLabels.Length; i++)
        {
            int bit = 1 << i;
            bool bombed = (doors.bombed & bit) != 0;
            bool jammed = (doors.jammed & bit) != 0;
            if (bombed)
                Add(Severity.Critical, DoorLabels[i], "爆破损毁");
            else if (jammed)
                Add(Severity.Major, DoorLabels[i], "卡阻故障");
        }
    }

    private static void CollectFlightAndIceDamage()
    {
        var flight = PlaneFlight.Instance;
        if (flight == null) return;

        if (flight.GearJammed)
            Add(Severity.Critical, "起落架", "卡阻无法收放");

        byte wd = flight.WheelDamage;
        if (wd != 0)
        {
            if ((wd & PlaneFlight.WheelL) != 0) Add(Severity.Major, "左起落架轮胎", "爆胎/损坏");
            if ((wd & PlaneFlight.WheelR) != 0) Add(Severity.Major, "右起落架轮胎", "爆胎/损坏");
            if ((wd & PlaneFlight.WheelNose) != 0) Add(Severity.Major, "前起落架轮胎", "爆胎/损坏");
        }

        if (flight.Fault == ApFault.Disconnect)
            Add(Severity.Major, "自动驾驶", "断开故障");
        else if (flight.Fault == ApFault.Drift)
            Add(Severity.Major, "自动驾驶", "航向漂移故障");
        else if (flight.Fault == ApFault.Runaway)
            Add(Severity.Critical, "自动驾驶", "失控偏航");

        // 机翼分段结冰（8 段，每段 8 bit）
        float iceThr = ConfigIceThreshold.Value;
        ulong iceSync = flight.iceSync;
        if (iceSync != 0)
        {
            float maxSegIce = 0f;
            int icedSegs = 0;
            for (int s = 0; s < WingIce.SegCount; s++)
            {
                float v = ((iceSync >> (s * 8)) & 0xFF) / 255f;
                if (v >= iceThr)
                {
                    icedSegs++;
                    if (v > maxSegIce) maxSegIce = v;
                }
            }
            if (icedSegs > 0)
            {
                int pct = Mathf.Clamp(Mathf.RoundToInt(maxSegIce * 100f), 1, 100);
                var sev = maxSegIce >= 0.6f ? Severity.Major : Severity.Minor;
                Add(sev, "机翼结冰", $"{icedSegs} 段结冰（最高 {pct}%）", maxSegIce);
            }
        }
    }

    private static int CountBits(int mask)
    {
        int c = 0;
        uint u = (uint)mask;
        while (u != 0)
        {
            c += (int)(u & 1u);
            u >>= 1;
        }
        return c;
    }

    private static void RefreshDamage()
    {
        if (_panel == null || _panel.Root == null) return;

        Entries.Clear();
        float thr = ConfigDamageThreshold.Value;

        CollectHullDamage(thr);
        CollectEngineDamage(thr);
        CollectElectricalDamage();
        CollectFuelDamage();
        CollectDoorDamage();
        CollectFlightAndIceDamage();

        if (Entries.Count == 0)
        {
            _panel.Root.gameObject.SetActive(false);
            SyncFlightKeysPosition();
            return;
        }

        Entries.Sort((a, b) =>
        {
            int cmp = b.Level.CompareTo(a.Level);
            if (cmp != 0) return cmp;
            cmp = b.Score.CompareTo(a.Score);
            if (cmp != 0) return cmp;
            return string.Compare(a.Part, b.Part, StringComparison.Ordinal);
        });

        _panel.Root.gameObject.SetActive(true);

        int maxRows = Mathf.Clamp(ConfigMaxRows.Value, 1, HardMaxRows);
        int shown = Mathf.Min(Entries.Count, maxRows);

        for (int i = 0; i < _rowTexts.Count; i++)
        {
            var row = _rowTexts[i];
            if (row == null) continue;

            if (i < shown)
            {
                var e = Entries[i];
                row.gameObject.SetActive(true);
                row.Set($"• {e.Part}：{e.Detail}");
                row.Color = e.Level switch
                {
                    Severity.Critical => UITheme.Red,
                    Severity.Major => UITheme.Orange,
                    _ => UITheme.Yellow
                };
            }
            else
            {
                row.gameObject.SetActive(false);
            }
        }

        if (_moreText != null)
        {
            int extra = Entries.Count - shown;
            if (extra > 0)
            {
                _moreText.gameObject.SetActive(true);
                _moreText.Set($"+{extra} 项其他损坏");
            }
            else
            {
                _moreText.gameObject.SetActive(false);
            }
        }

        SyncFlightKeysPosition();
    }

    private static void SyncFlightKeysPosition()
    {
        var hint = FlightKeysHint.instance;
        var widget = hint != null ? hint.widget : null;
        var fkPanel = widget != null ? widget.panel : null;
        if (fkPanel == null) return;

        if (!_hasOrigFlightKeysSlot)
        {
            Vector2 slot = FlightKeysHint.FlightKeysWidget.Slot;
            _origFlightKeysSlot = slot.sqrMagnitude > 0.01f ? slot : fkPanel.anchoredPosition;
            _hasOrigFlightKeysSlot = true;
        }

        bool damageVisible = _panel != null && _panel.Root != null && _panel.Root.gameObject.activeSelf;
        if (!damageVisible)
        {
            FlightKeysHint.FlightKeysWidget.Slot = _origFlightKeysSlot;
            if (fkPanel.anchoredPosition != _origFlightKeysSlot)
                fkPanel.anchoredPosition = _origFlightKeysSlot;
            return;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(_panel.Root);
        float damageHeight = Mathf.Max(
            _panel.Root.rect.height,
            _panel.Root.sizeDelta.y,
            LayoutUtility.GetPreferredHeight(_panel.Root));
        float damageTopY = _panel.Root.anchoredPosition.y + damageHeight;

        float fkHeight = Mathf.Max(fkPanel.rect.height, fkPanel.sizeDelta.y);
        float requiredY = damageTopY + 12f + fkHeight * fkPanel.pivot.y;
        float targetY = Mathf.Max(_origFlightKeysSlot.y, requiredY);
        var targetPos = new Vector2(_origFlightKeysSlot.x, targetY);

        FlightKeysHint.FlightKeysWidget.Slot = targetPos;
        if (fkPanel.anchoredPosition != targetPos)
            fkPanel.anchoredPosition = targetPos;
    }

    private static void BuildPanel(RectTransform hudRoot)
    {
        if (hudRoot == null) return;

        if (_panel != null && _panel.Root != null)
            UnityEngine.Object.Destroy(_panel.Root.gameObject);

        _rowTexts.Clear();

        _panel = UIF.Panel(hudRoot, "DamageHudPanel", UITheme.HudPanel, UITheme.Radius, shadow: true, raycast: false);
        UIF.At(_panel.Root, UIF.BottomLeft, new Vector2(0f, 0f), new Vector2(20f, 30f), new Vector2(320f, 100f));

        UIF.VLayout(
            _panel.Root.gameObject,
            spacing: 4f,
            pad: UIF.Pad(14, 10),
            align: TextAnchor.UpperLeft,
            controlW: true,
            controlH: true,
            expandW: true,
            expandH: false);
        UIF.Fit(_panel.Root.gameObject, horizontal: false, vertical: true);

        UIF.Text(
            _panel.Root,
            "Title",
            "飞机损坏警报",
            UITheme.SizeSmall,
            UITheme.Red,
            TextAlignmentOptions.Left);

        UIF.Divider(_panel.Root, UITheme.WithAlpha(UITheme.Red, 0.35f), 1.5f);

        for (int i = 0; i < HardMaxRows; i++)
        {
            var row = UIF.Text(
                _panel.Root,
                $"Row_{i}",
                string.Empty,
                UITheme.SizeTiny,
                UITheme.HudText,
                TextAlignmentOptions.Left);
            row.Ellipsis();
            row.gameObject.SetActive(false);
            _rowTexts.Add(row);
        }

        _moreText = UIF.Text(
            _panel.Root,
            "More",
            string.Empty,
            UITheme.SizeTiny,
            UITheme.InkSoft,
            TextAlignmentOptions.Left);
        _moreText.gameObject.SetActive(false);

        _panel.Root.gameObject.SetActive(false);
        _nextPollAt = 0f;
    }

    private static class Hooks
    {
        [HarmonyPatch(typeof(HudScreen), nameof(HudScreen.Build))]
        [HarmonyPostfix]
        private static void HudScreenBuildPostfix(RectTransform rt)
        {
            BuildPanel(rt);
        }

        [HarmonyPatch(typeof(HudScreen), nameof(HudScreen.Update))]
        [HarmonyPostfix]
        private static void HudScreenUpdatePostfix()
        {
            if (_panel == null || _panel.Root == null) return;

            SyncFlightKeysPosition();

            float now = Time.unscaledTime;
            if (now < _nextPollAt) return;
            _nextPollAt = now + PollInterval;

            RefreshDamage();
        }

        [HarmonyPatch(typeof(FlightKeysHint), nameof(FlightKeysHint.Update))]
        [HarmonyPostfix]
        private static void FlightKeysHintUpdatePostfix()
        {
            SyncFlightKeysPosition();
        }
    }
}
