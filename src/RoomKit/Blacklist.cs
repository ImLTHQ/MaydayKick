using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;

namespace RoomKit
{
    /// <summary>
    /// 黑名单：存 steamId，一行一个，后面可以跟一个制表符和备注名。
    /// 文件改动会被热加载（按修改时间判断），所以解封只要改文件，不用重启游戏。
    /// </summary>
    internal static class Blacklist
    {
        private static readonly HashSet<ulong> Ids = new HashSet<ulong>();
        private static readonly object Gate = new object();
        private static DateTime lastWrite = DateTime.MinValue;
        private static string path;

        internal static string FilePath
        {
            get
            {
                if (path == null)
                {
                    path = Path.Combine(Paths.ConfigPath, "RoomKit", "blacklist.txt");
                }
                return path;
            }
        }

        internal static int Count
        {
            get { lock (Gate) { return Ids.Count; } }
        }

        internal static void Init()
        {
            Reload(true);
            Plugin.Logger.LogInfo("[RoomKit] 黑名单文件: " + FilePath + "，当前 " + Count + " 条");
        }

        internal static bool Contains(ulong steamId)
        {
            if (steamId == 0UL) return false;
            Reload(false);
            lock (Gate) { return Ids.Contains(steamId); }
        }

        internal static bool Add(ulong steamId, string name)
        {
            if (steamId == 0UL) return false;

            lock (Gate)
            {
                if (!Ids.Add(steamId)) return false;
                Append(steamId, name);
            }

            Plugin.Logger.LogInfo("[RoomKit] 已拉黑 " + name + " (" + steamId + ")，共 " + Count + " 条");
            return true;
        }

        // 给界面用：按文件顺序返回 (steamId, 备注) 列表
        internal static List<KeyValuePair<ulong, string>> Snapshot()
        {
            var list = new List<KeyValuePair<ulong, string>>();
            try
            {
                Reload(false);
                if (!File.Exists(FilePath)) return list;

                foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    var parts = line.Split(new[] { '\t', ' ' }, 2);
                    ulong id;
                    if (!ulong.TryParse(parts[0], out id) || id == 0UL) continue;

                    var nm = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                    list.Add(new KeyValuePair<ulong, string>(id, nm));
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[RoomKit] 读取黑名单列表失败: " + e.Message);
            }
            return list;
        }

        internal static bool Remove(ulong steamId)
        {
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
                            var line = raw.Trim();
                            if (line.Length == 0 || line.StartsWith("#")) { lines.Add(raw); continue; }
                            var head = line.Split('\t', ' ')[0];
                            ulong id;
                            if (ulong.TryParse(head, out id) && id == steamId) continue;
                            lines.Add(raw);
                        }
                    }

                    File.WriteAllLines(FilePath, lines, new UTF8Encoding(false));
                    Ids.Remove(steamId);
                    lastWrite = DateTime.MinValue;
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogWarning("[RoomKit] 重写黑名单失败: " + e.Message);
                    return false;
                }
            }

            Plugin.Logger.LogInfo("[RoomKit] 已解封 " + steamId + "，共 " + Count + " 条");
            return true;
        }

        private static void Append(ulong steamId, string name)
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(FilePath, steamId + "\t" + (name ?? "") + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[RoomKit] 写入黑名单失败: " + e.Message);
            }
        }

        private static void Reload(bool force)
        {
            try
            {
                var info = new FileInfo(FilePath);
                if (!info.Exists)
                {
                    if (!force) return;
                    lock (Gate) { Ids.Clear(); }
                    lastWrite = DateTime.MinValue;
                    return;
                }

                if (!force && info.LastWriteTimeUtc == lastWrite) return;

                var ids = new HashSet<ulong>();
                foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var head = line.Split('\t', ' ')[0];
                    ulong id;
                    if (ulong.TryParse(head, out id) && id != 0UL) ids.Add(id);
                }

                lock (Gate)
                {
                    Ids.Clear();
                    foreach (var id in ids) Ids.Add(id);
                }

                lastWrite = info.LastWriteTimeUtc;
                Plugin.Logger.LogInfo("[RoomKit] 黑名单已重载，共 " + ids.Count + " 条");
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[RoomKit] 读取黑名单失败: " + e.Message);
            }
        }
    }
}