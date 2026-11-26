using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx;

namespace SaveManager;

public sealed class SaveData
{
    public int Version { get; set; } = 5;
    public string Id { get; set; } = string.Empty;
    public string SaveName { get; set; } = string.Empty;
    public string SavedAtLocal { get; set; } = string.Empty;
    public long SavedAtUnix { get; set; }

    // GameState 核心运营状态
    public int GamePhaseValue { get; set; }
    public int DirectorStageValue { get; set; }
    public int RunSeed { get; set; }
    public int Leg { get; set; }
    public int Money { get; set; }
    public int Debt { get; set; }
    public float Rating { get; set; }
    public float Difficulty { get; set; }
    public int CurrentAirport { get; set; }
    public int DestinationAirport { get; set; }
    public string CurrentAirportName { get; set; } = string.Empty;
    public string DestinationName { get; set; } = string.Empty;
    public float GroundTimeLeft { get; set; }
    public float LegTime { get; set; }
    public float TimeOfDay { get; set; }
    public float Weather { get; set; }
    public int TotalPassengers { get; set; }
    public int TotalEarned { get; set; }
    public List<string> Upgrades { get; set; } = new();

    // GameDirector 整备与航段统计
    public DirectorSnapshot Director { get; set; } = new();

    // 天气与时钟
    public WeatherSnapshot WeatherState { get; set; } = new();

    // 飞机完整状态（位置、速度、油门、手刹、控制面、引擎、电气、燃油、机体、门、服务站）
    public PlaneSnapshot Plane { get; set; } = new();

    // 兼容旧版存档读取（v1）
    public FuelSnapshot Fuel { get; set; }
    public byte WheelDamage { get; set; }
    public PlanePoseSnapshot PlanePose { get; set; }

    // 地面设备与车辆（加油车、上下梯、货舱传送带、行李车、行李拖车、餐车）
    public GroundEquipmentSnapshot GroundEquipment { get; set; } = new();

    // 玩家背包与位置
    public List<PlayerInventorySnapshot> Players { get; set; } = new();

    // 飞机插槽物品（工具架、冰箱、咖啡机、餐车等）
    public List<SocketItemSnapshot> SocketItems { get; set; } = new();

    // 玩家丢在地上或放在飞机上的自由物品（不含商品）
    public List<WorldItemSnapshot> WorldItems { get; set; } = new();

    // 地面/机舱污渍
    public List<MessSnapshot> Messes { get; set; } = new();
}

public sealed class DirectorSnapshot
{
    public int ChosenOption { get; set; } = -1;
    public int LeasePaid { get; set; }
    public int LandingsSinceLease { get; set; }
    public int NextLease { get; set; }
    public int FuelNeeded { get; set; }
    public string LegFrom { get; set; } = string.Empty;
    public string LegTo { get; set; } = string.Empty;
    public float BestStars { get; set; }
    public int ButterCount { get; set; }
    public int StowawayTotal { get; set; }
    public int LandingsTotal { get; set; }
    public int RunDeaths { get; set; }
    public int RunDefibs { get; set; }
    public int RunOverboard { get; set; }
    public int RunBounty { get; set; }
    public int RunCompensation { get; set; }
    public int RunFines { get; set; }

    // v3 新增：确保已访问机场、航线选项、阶段计时与租约/自动调度计时完整保留
    public int ReturnAirport { get; set; }
    public int OptionsLeg { get; set; }
    public int OptionsFrom { get; set; }
    public bool OptionsVirtual { get; set; }
    public bool Millionaire { get; set; }
    public float StageTime { get; set; }
    public float SlotSecondsLeft { get; set; }
    public float AutoDispatchSecondsLeft { get; set; }
    public List<int> VisitedAirports { get; set; } = new();
    public List<DestOptionSnapshot> Options { get; set; } = new();
}

public sealed class DestOptionSnapshot
{
    public int Airport { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public float Km { get; set; }
    public int Pax { get; set; }
    public float Pay { get; set; }
    public byte Contract { get; set; }
    public byte Trait { get; set; }
    public byte Weather { get; set; }
    public int Fuel { get; set; }
    public bool Visited { get; set; }
    public bool VirtualSite { get; set; }
    public byte WFlags { get; set; }
    public ushort FogVis { get; set; }
    public byte Birds { get; set; }
    public byte Hazard { get; set; }
}

public sealed class WeatherSnapshot
{
    public bool HasData { get; set; }
    // WorldSync
    public int OriginA { get; set; }
    public int OriginB { get; set; }
    public int WeatherLeg { get; set; }
    public byte WeatherKind { get; set; }
    public float Severity { get; set; }
    public int WeatherFrom { get; set; }
    public int WeatherTo { get; set; }
    public byte WeatherFlags { get; set; }
    public byte Birds { get; set; }
    public ushort FogVis { get; set; }
    public ushort DepFogVis { get; set; }
    public double FogRoll { get; set; }
    public double FogLift { get; set; }
    // WeatherSample
    public float SampleTurbulence { get; set; }
    public byte SampleRain { get; set; }
    public byte SampleHail { get; set; }
    public byte SampleIcing { get; set; }
    public byte SampleStorm { get; set; }
    public byte SampleCloud { get; set; }
    // WorldClock
    public int ClockSeed { get; set; }
    public double ClockAnchorHours { get; set; }
    public float ClockRate { get; set; }
    // WeatherFront
    public int FrontId { get; set; }
    public byte FrontKind { get; set; }
    public float FrontIntensity { get; set; }
    public float FrontRemainingSeconds { get; set; }
    public float FrontRampIn { get; set; }
    public float FrontRampOut { get; set; }
    public float FrontHeading { get; set; }
    public float FrontWind { get; set; }
    public short FrontAirport { get; set; }
    public ushort FrontFogVis { get; set; }
    // WeatherSystem 瞬时风向风速与云量
    public float WindDirX { get; set; }
    public float WindDirY { get; set; }
    public float WindDirZ { get; set; }
    public float WindSpeed { get; set; }
    public float CloudTarget { get; set; }
}

public sealed class PlaneSnapshot
{
    public PlanePoseSnapshot Pose { get; set; } = new();
    public FlightSimSnapshot Sim { get; set; } = new();

    // 驾驶舱控制与开关
    public float[] Throttles { get; set; } = Array.Empty<float>();
    public bool ParkingBrake { get; set; }
    public bool ReverseGear { get; set; }
    public byte FlapSetting { get; set; }
    public bool GearDownCmd { get; set; } = true;
    public bool SeatbeltSign { get; set; }
    public byte WheelDamage { get; set; }
    public ulong IceSync { get; set; }
    public bool GearJammed { get; set; }
    public byte ApModes { get; set; }
    public float ApAltTarget { get; set; }
    public float ApHdgTarget { get; set; }
    public float ApSpdTarget { get; set; }
    public byte ApFault { get; set; }
    public float CrosswindX { get; set; }
    public float CrosswindY { get; set; }

    // 点火与双发引擎
    public byte IgnitionPos { get; set; }
    public List<EngineSnapshot> Engines { get; set; } = new();

    // 电气系统
    public ElectricalSnapshot Electrical { get; set; } = new();

    // 燃油系统
    public FuelSnapshot Fuel { get; set; } = new();

    // 门与机舱设施
    public DoorSnapshot Doors { get; set; } = new();
    public LavSnapshot Lav { get; set; } = new();
    public GalleySnapshot Galley { get; set; } = new();
    public List<int> TrashFills { get; set; } = new();

    // 机体损伤与涂装
    public HullSnapshot Hull { get; set; } = new();

    // 客舱座椅配置
    public byte SeatRowMask { get; set; }
    public bool PremiumSeats { get; set; }
    public List<float> SeatInstallProgress { get; set; } = new();
}

public sealed class FlightSimSnapshot
{
    public bool HasSim { get; set; }
    public float Speed { get; set; }
    public float Gamma { get; set; }
    public float RelTrack { get; set; }
    public float RelHeading { get; set; }
    public float Pitch { get; set; }
    public float Roll { get; set; }
    public float PitchRate { get; set; }
    public float RollRate { get; set; }
    public float YawRate { get; set; }
    public bool OnGround { get; set; } = true;
    public float GearPos { get; set; } = 1f;
    public float FlapPos { get; set; }
    public float SteerAngle { get; set; }
    public bool Backward { get; set; }
    public bool Braking { get; set; }
    public float VelX { get; set; }
    public float VelY { get; set; }
    public float VelZ { get; set; }
}

public sealed class EngineSnapshot
{
    public int Index { get; set; }
    public byte State { get; set; }
    public float Rpm { get; set; }
    public float Spool { get; set; }
    public float StartT { get; set; }
    public float Thrust { get; set; }
    public float Temp { get; set; }
    public float Oil { get; set; }
    public float Health { get; set; }
    public float Fire { get; set; }
    public ushort Faults { get; set; }
    public byte Cowl { get; set; }
    public float CowlRipLeft { get; set; }
    public bool BottleUsed { get; set; }
    public float Repair { get; set; }
    public byte BatState { get; set; }
    public float BattCondition { get; set; }
    public float JumpLeft { get; set; }
}

public sealed class ElectricalSnapshot
{
    public bool HasData { get; set; }
    public bool Master { get; set; }
    public bool GenL { get; set; }
    public bool GenR { get; set; }
    public bool CabinLightsSw { get; set; }
    public bool DeiceSw { get; set; }
    public bool SeatbeltSw { get; set; }
    public float Battery { get; set; }
    public byte Breakers { get; set; }
    public byte FusesBlown { get; set; }
    public byte TrippedMask { get; set; }
    public bool PanelDoorOpen { get; set; }
    public byte PanelDoorPops { get; set; }
}

public sealed class DoorSnapshot
{
    public bool HasData { get; set; }
    public byte OpenMask { get; set; }
    public byte JammedMask { get; set; }
    public byte BombedMask { get; set; }
    public float CabinTemp { get; set; }
    public bool VentOk { get; set; }
}

public sealed class LavSnapshot
{
    public bool HasData { get; set; }
    public bool DoorOpen { get; set; }
    public bool Locked { get; set; }
    public float Dirt { get; set; }
    public byte Clog { get; set; }
    public float Plunge { get; set; }
    public byte FlushCount { get; set; }
}

public sealed class GalleySnapshot
{
    public bool HasData { get; set; }
    public bool CurtainClosed { get; set; }
    public bool FridgeOpen { get; set; }
    public byte CoffeePhase { get; set; }
    public bool CoffeeJammed { get; set; }
    public byte CoffeeWhacks { get; set; }
    public byte KettlePhase { get; set; }
    public bool OvenOn { get; set; }
    public bool OvenDoorOpen { get; set; }
    public bool OvenFire { get; set; }
    public bool MicroOn { get; set; }
    public bool MicroDoorOpen { get; set; }
    public bool MicroFire { get; set; }
    public bool PrepBun { get; set; }
    public bool PrepPatty { get; set; }
    public bool PrepCheese { get; set; }
    public float PrepPattyCook { get; set; }
    public byte SinkPresses { get; set; }
}

public sealed class HullSnapshot
{
    public bool HasData { get; set; }
    public List<float> Damage { get; set; } = new();
    public List<float> Dirt { get; set; } = new();
    public List<uint> MainColors { get; set; } = new();
    public List<uint> AccentColors { get; set; } = new();
    public ushort Breaches { get; set; }
    public float Ice { get; set; }
    public ulong JetSync { get; set; }
    public ulong SectSync { get; set; }
}

public sealed class GroundEquipmentSnapshot
{
    public FuelTruckSnapshot FuelTruck { get; set; }
    public List<AirStairsSnapshot> AirStairs { get; set; } = new();
    public List<CargoLoaderSnapshot> CargoLoaders { get; set; } = new();
    public BaggageTugSnapshot BaggageTug { get; set; }
    public List<BaggageCartSnapshot> BaggageCarts { get; set; } = new();
    public DrinkTrolleySnapshot DrinkTrolley { get; set; }
}

public sealed class FuelTruckSnapshot
{
    public float RelPosX { get; set; }
    public float RelPosY { get; set; }
    public float RelPosZ { get; set; }
    public float RelYaw { get; set; }
    public bool Handbrake { get; set; }
    public bool Manual { get; set; }
    public bool Deployed { get; set; }
    public byte HoseState { get; set; }
    public sbyte Slot { get; set; }
    public bool Flowing { get; set; }
    public float SessionLitres { get; set; }
    public int SessionCost { get; set; }
}

public sealed class AirStairsSnapshot
{
    public float SitePosX { get; set; }
    public float SitePosY { get; set; }
    public float SitePosZ { get; set; }
    public float SiteYaw { get; set; }
    public bool Docked { get; set; }
    public bool Tipped { get; set; }
}

public sealed class CargoLoaderSnapshot
{
    public bool Deployed { get; set; }
    public float DockSitePosX { get; set; }
    public float DockSitePosY { get; set; }
    public float DockSitePosZ { get; set; }
    public float DockSiteYaw { get; set; }
    public bool BeltOn { get; set; }
    public bool BeltRunning { get; set; }
    public float BeltDist { get; set; }
}

public sealed class BaggageTugSnapshot
{
    public float RelPosX { get; set; }
    public float RelPosY { get; set; }
    public float RelPosZ { get; set; }
    public float RelYaw { get; set; }
    public bool Handbrake { get; set; }
}

public sealed class BaggageCartSnapshot
{
    public bool IsArrival { get; set; }
    public float SitePosX { get; set; }
    public float SitePosY { get; set; }
    public float SitePosZ { get; set; }
    public float SiteYaw { get; set; }
    public bool AtAnchor { get; set; }
}

public sealed class DrinkTrolleySnapshot
{
    public float PoseX { get; set; }
    public float PoseY { get; set; }
    public float PoseZ { get; set; }
    public bool Braked { get; set; }
    public float Side { get; set; }
}

public sealed class SocketItemSnapshot
{
    public string SocketKey { get; set; } = string.Empty;
    public ItemSnapshot Item { get; set; } = new();
}

public sealed class MessSnapshot
{
    public byte Kind { get; set; }
    public float Size { get; set; }
    public bool RelativeToPlane { get; set; }
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
}

public sealed class PlanePoseSnapshot
{
    public bool HasPose { get; set; }
    public bool RelativeToAirport { get; set; }
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public float RotX { get; set; }
    public float RotY { get; set; }
    public float RotZ { get; set; }
    public float RotW { get; set; } = 1f;
}

public sealed class FuelSnapshot
{
    public float FuelL { get; set; }
    public float FuelR { get; set; }
    public float Reserve { get; set; }
    public float Capacity { get; set; }
    public bool PumpL { get; set; }
    public bool PumpR { get; set; }
    public bool Crossfeed { get; set; }
    public byte Leaks { get; set; }
    public byte Caps { get; set; }
}

public sealed class PlayerInventorySnapshot
{
    public ulong SteamId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool HasPose { get; set; }
    public bool RelativeToPlane { get; set; }
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public float Yaw { get; set; }
    public List<ItemSnapshot> Items { get; set; } = new();
}

public sealed class ItemSnapshot
{
    public string ItemId { get; set; } = string.Empty;
    public byte State { get; set; }
    public float Amount { get; set; }
    public int Count { get; set; }
    public byte TintR { get; set; }
    public byte TintG { get; set; }
    public byte TintB { get; set; }
    public byte TintA { get; set; }
    public uint Tag { get; set; }
    public byte EatPct { get; set; }
    public byte CigarettePuffs { get; set; }
    public byte LuggageLoot { get; set; }
}

public sealed class WorldItemSnapshot
{
    public ItemSnapshot Item { get; set; } = new();
    public bool RelativeToPlane { get; set; }
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public float RotX { get; set; }
    public float RotY { get; set; }
    public float RotZ { get; set; }
    public float RotW { get; set; } = 1f;
}

public static class SaveStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string SavesDirectory =>
        Path.Combine(Paths.ConfigPath, "SaveManager", "saves");

    public static void EnsureDirectory()
    {
        if (!Directory.Exists(SavesDirectory))
        {
            Directory.CreateDirectory(SavesDirectory);
            Plugin.Logger?.LogInfo($"[存储] 已创建存档目录: {SavesDirectory}");
        }
    }

    public static List<SaveData> ListAll()
    {
        EnsureDirectory();
        var list = new List<SaveData>();
        string[] files = Directory.GetFiles(SavesDirectory, "*.json");
        Plugin.Logger?.LogInfo($"[存储] 扫描存档目录: {SavesDirectory}, 发现 {files.Length} 个 .json 文件。");

        foreach (string file in files)
        {
            try
            {
                string json = File.ReadAllText(file);
                var data = JsonSerializer.Deserialize<SaveData>(json, JsonOptions);
                if (data != null)
                {
                    if (string.IsNullOrWhiteSpace(data.Id))
                    {
                        data.Id = Path.GetFileNameWithoutExtension(file);
                    }
                    list.Add(data);
                    Plugin.Logger?.LogInfo(
                        $"[存储] 读取存档 '{data.SaveName}' (Id={data.Id}, Ver={data.Version}, Leg={data.Leg}, Money=${data.Money}, Airport={data.CurrentAirport}:{data.CurrentAirportName}, Upgrades={data.Upgrades?.Count ?? 0}, FreeItems={data.WorldItems?.Count ?? 0}, SocketItems={data.SocketItems?.Count ?? 0})。");
                }
                else
                {
                    Plugin.Logger?.LogWarning($"[存储] 反序列化返回 null: {Path.GetFileName(file)}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning($"[存储] 读取存档失败 ({Path.GetFileName(file)}): {ex.Message}");
            }
        }

        return list.OrderByDescending(s => s.SavedAtUnix).ToList();
    }

    public static void Save(SaveData data)
    {
        EnsureDirectory();
        if (string.IsNullOrWhiteSpace(data.Id))
        {
            data.Id = $"save_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        }

        string path = GetFilePath(data.Id);
        string json = JsonSerializer.Serialize(data, JsonOptions);
        File.WriteAllText(path, json);
        Plugin.Logger?.LogInfo($"[存储] 已写入存档文件: {path} (Bytes={json.Length}, Id={data.Id}, Ver={data.Version})。");
    }

    public static bool Delete(string saveId)
    {
        if (string.IsNullOrWhiteSpace(saveId))
        {
            Plugin.Logger?.LogWarning("[存储] Delete 调用时 saveId 为空。");
            return false;
        }
        string path = GetFilePath(saveId);
        if (!File.Exists(path))
        {
            Plugin.Logger?.LogWarning($"[存储] 删除失败，文件不存在: {path}");
            return false;
        }
        File.Delete(path);
        Plugin.Logger?.LogInfo($"[存储] 已删除存档文件: {path}");
        return true;
    }

    private static string GetFilePath(string saveId)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            saveId = saveId.Replace(c, '_');
        }
        return Path.Combine(SavesDirectory, $"{saveId}.json");
    }
}
