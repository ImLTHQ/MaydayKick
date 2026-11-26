using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;

namespace RoomKit
{
    internal sealed class WhitelistEntry
    {
        internal ulong SteamId;
        internal string Name;
        internal string Note;
    }

    /// <summary>
    /// 白名单：每行依次保存 SteamID64、昵称和可选备注，字段以 Tab 分隔。
    /// </summary>
    internal static class Whitelist
    {
        private static readonly HashSet<ulong> Ids = new HashSet<ulong>();
        private static readonly object Gate = new object();
        private static DateTime lastWrite = DateTime.MinValue;
        private static long lastLength = -1;
        private static string path;

        internal static string FilePath
        {
            get
            {
                if (path == null)
                    path = Path.Combine(Paths.ConfigPath, "RoomKit", "whitelist.txt");
                return path;
            }
        }

        internal static int Count
        {
            get { lock (Gate) { return Ids.Count; } }
        }

        internal static void Init()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (!File.Exists(FilePath))
                    File.WriteAllText(FilePath, "", new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[RoomKit] 创建白名单文件失败: " + e.Message);
            }

            Reload(true);
            Plugin.Logger.LogInfo("[RoomKit] 白名单文件: " + FilePath + "，当前 " + Count + " 条");
        }

        internal static bool Contains(ulong steamId)
        {
            if (steamId == 0UL) return false;
            Reload(false);
            lock (Gate) { return Ids.Contains(steamId); }
        }

        internal static bool Add(ulong steamId, string name, string note = "")
        {
            if (steamId == 0UL) return false;
            Reload(false);

            if (Blacklist.Contains(steamId) && !Blacklist.Remove(steamId))
            {
                Plugin.Logger.LogWarning("[RoomKit] 未能从黑名单移除 " + steamId + "，取消加入白名单");
                return false;
            }

            lock (Gate)
            {
                if (Ids.Contains(steamId)) return false;

                try
                {
                    var dir = Path.GetDirectoryName(FilePath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    if (File.Exists(FilePath))
                    {
                        var existing = File.ReadAllText(FilePath, Encoding.UTF8);
                        if (existing.Length > 0 && !existing.EndsWith("\n", StringComparison.Ordinal) &&
                            !existing.EndsWith("\r", StringComparison.Ordinal))
                            File.AppendAllText(FilePath, Environment.NewLine, new UTF8Encoding(false));
                    }
                    var line = steamId + "\t" + CleanField(name) + "\t" + CleanField(note) + Environment.NewLine;
                    File.AppendAllText(FilePath, line, new UTF8Encoding(false));
                    Ids.Add(steamId);
                    UpdateFileStamp();
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogWarning("[RoomKit] 写入白名单失败: " + e.Message);
                    return false;
                }
            }

            Plugin.Logger.LogInfo("[RoomKit] 已加入白名单 " + name + " (" + steamId + ")，共 " + Count + " 条");
            return true;
        }

        internal static List<WhitelistEntry> Snapshot()
        {
            var list = new List<WhitelistEntry>();
            try
            {
                Reload(false);
                if (!File.Exists(FilePath)) return list;

                foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var entry = Parse(raw);
                    if (entry != null) list.Add(entry);
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[RoomKit] 读取白名单列表失败: " + e.Message);
            }
            return list;
        }

        internal static bool Remove(ulong steamId)
        {
            if (steamId == 0UL) return false;
            Reload(false);

            lock (Gate)
            {
                if (!Ids.Contains(steamId)) return false;

                try
                {
                    var lines = new List<string>();
                    if (File.Exists(FilePath))
                    {
                        foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                        {
                            var entry = Parse(raw);
                            if (entry != null && entry.SteamId == steamId) continue;
                            lines.Add(raw);
                        }
                    }

                    File.WriteAllLines(FilePath, lines, new UTF8Encoding(false));
                    Ids.Remove(steamId);
                    UpdateFileStamp();
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogWarning("[RoomKit] 重写白名单失败: " + e.Message);
                    return false;
                }
            }

            Plugin.Logger.LogInfo("[RoomKit] 已移出白名单 " + steamId + "，共 " + Count + " 条");
            return true;
        }

        private static string CleanField(string value)
        {
            return (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private static WhitelistEntry Parse(string raw)
        {
            var line = (raw ?? "").Trim();
            if (line.Length == 0 || line.StartsWith("#")) return null;

            var parts = line.Split(new[] { '\t' }, 3);
            ulong id;
            if (!ulong.TryParse(parts[0].Trim(), out id) || id == 0UL) return null;

            return new WhitelistEntry
            {
                SteamId = id,
                Name = parts.Length > 1 ? parts[1].Trim() : "",
                Note = parts.Length > 2 ? parts[2].Trim() : ""
            };
        }

        private static void Reload(bool force)
        {
            try
            {
                var info = new FileInfo(FilePath);
                if (!info.Exists)
                {
                    lock (Gate)
                    {
                        if (force || lastWrite != DateTime.MinValue || Ids.Count > 0)
                        {
                            Ids.Clear();
                            lastWrite = DateTime.MinValue;
                            lastLength = -1;
                        }
                    }
                    return;
                }

                if (!force && info.LastWriteTimeUtc == lastWrite && info.Length == lastLength) return;

                var ids = new HashSet<ulong>();
                foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var entry = Parse(raw);
                    if (entry != null) ids.Add(entry.SteamId);
                }

                lock (Gate)
                {
                    Ids.Clear();
                    foreach (var id in ids) Ids.Add(id);
                    lastWrite = info.LastWriteTimeUtc;
                    lastLength = info.Length;
                }

                Plugin.Logger.LogInfo("[RoomKit] 白名单已重载，共 " + ids.Count + " 条");
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[RoomKit] 读取白名单失败: " + e.Message);
            }
        }

        private static void UpdateFileStamp()
        {
            var info = new FileInfo(FilePath);
            lastWrite = info.LastWriteTimeUtc;
            lastLength = info.Length;
        }
    }
}
