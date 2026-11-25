using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MaydayKick
{
    internal enum SteamProfileLevelStatus
    {
        Level,
        Private,
        Unqualified,
        Unavailable
    }

    internal struct SteamProfileLevelResult
    {
        internal SteamProfileLevelStatus Status;
        internal int Level;
        internal string Detail;

        internal SteamProfileLevelResult(SteamProfileLevelStatus status, int level, string detail)
        {
            Status = status;
            Level = level;
            Detail = detail;
        }
    }

    internal static class SteamProfileLevelLookup
    {
        private sealed class CacheEntry
        {
            internal SteamProfileLevelResult Result;
            internal DateTime ExpiresAtUtc;
        }

        private static readonly HttpClient Client = CreateClient();
        private static readonly object Gate = new object();
        private static readonly Dictionary<ulong, CacheEntry> Cache = new Dictionary<ulong, CacheEntry>();
        private static readonly Dictionary<ulong, TaskCompletionSource<SteamProfileLevelResult>> Pending =
            new Dictionary<ulong, TaskCompletionSource<SteamProfileLevelResult>>();
        private static readonly Queue<ulong> Queue = new Queue<ulong>();
        private static int activeRequests;

        internal static Task<SteamProfileLevelResult> GetAsync(ulong steamId)
        {
            TaskCompletionSource<SteamProfileLevelResult> source;
            lock (Gate)
            {
                CacheEntry cached;
                if (Cache.TryGetValue(steamId, out cached))
                {
                    if (cached.ExpiresAtUtc > DateTime.UtcNow)
                        return Task.FromResult(cached.Result);

                    Cache.Remove(steamId);
                }

                TaskCompletionSource<SteamProfileLevelResult> pending;
                if (Pending.TryGetValue(steamId, out pending))
                    return pending.Task;

                source = new TaskCompletionSource<SteamProfileLevelResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                Pending.Add(steamId, source);
                Queue.Enqueue(steamId);
            }

            StartQueuedRequests();
            return source.Task;
        }

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MaydayKick/1.0");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.8");
            return client;
        }

        private static void StartQueuedRequests()
        {
            List<ulong> toStart = new List<ulong>();
            lock (Gate)
            {
                var configuredLimit = Plugin.SteamLevelQueryConcurrency == null
                    ? 5
                    : Plugin.SteamLevelQueryConcurrency.Value;
                var limit = Math.Max(1, Math.Min(32, configuredLimit));
                if (configuredLimit != limit)
                    Plugin.Logger.LogWarning("[MaydayKick] SteamLevelQueryConcurrency 超出范围，实际使用 " + limit);

                while (activeRequests < limit && Queue.Count > 0)
                {
                    toStart.Add(Queue.Dequeue());
                    activeRequests++;
                }
            }

            foreach (var steamId in toStart)
                _ = FetchAndCacheAsync(steamId);
        }

        private static async Task FetchAndCacheAsync(ulong steamId)
        {
            SteamProfileLevelResult result;
            try
            {
                result = await FetchAsync(steamId).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                result = new SteamProfileLevelResult(SteamProfileLevelStatus.Unavailable, 0, e.Message);
            }

            TaskCompletionSource<SteamProfileLevelResult> source;
            lock (Gate)
            {
                Pending.TryGetValue(steamId, out source);
                Pending.Remove(steamId);
                Cache[steamId] = new CacheEntry
                {
                    Result = result,
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(result.Status == SteamProfileLevelStatus.Unavailable ? 1 : 10)
                };
                activeRequests--;
            }

            if (source != null)
                source.TrySetResult(result);

            StartQueuedRequests();
        }

        private static async Task<SteamProfileLevelResult> FetchAsync(ulong steamId)
        {
            if (steamId == 0UL)
                return new SteamProfileLevelResult(SteamProfileLevelStatus.Unavailable, 0, "SteamID64 is zero");

            var url = "https://steamcommunity.com/profiles/" +
                steamId.ToString(CultureInfo.InvariantCulture) + "?l=english";
            using (var response = await Client.GetAsync(url).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    return new SteamProfileLevelResult(
                        SteamProfileLevelStatus.Unavailable,
                        0,
                        "HTTP " + (int)response.StatusCode);
                }

                var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (IsPrivateProfile(html))
                    return new SteamProfileLevelResult(SteamProfileLevelStatus.Private, 0, "profile is private");

                var match = Regex.Match(
                    html,
                    @"class\s*=\s*['""][^'""]*\bfriendPlayerLevelNum\b[^'""]*['""][^>]*>\s*(?:<[^>]+>\s*)*(\d+)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                int level;
                if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out level))
                    return new SteamProfileLevelResult(SteamProfileLevelStatus.Level, level, null);

                return new SteamProfileLevelResult(SteamProfileLevelStatus.Unqualified, 0, "public profile has no friendPlayerLevelNum");
            }
        }

        private static bool IsPrivateProfile(string html)
        {
            return html.IndexOf("profile_private_info", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
