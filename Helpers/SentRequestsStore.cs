using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RemoteDebugServer.Models;

namespace RemoteDebugServer.Helpers
{
    /// <summary>
    /// 最近发包请求本地持久化存储。
    /// 退出游戏 / 关闭工具后，下次启动仍能看到最近一次运行期间发送过的协议命令。
    /// </summary>
    public static class SentRequestsStore
    {
        private const int MaxCount = 100;

        private static readonly string FilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "sent_requests.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>
        /// 加载最近发包请求（按时间倒序，最新的在前）。文件不存在或损坏时返回空集合。
        /// </summary>
        public static List<SentRequestEntry> LoadAll()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<SentRequestEntry>();

                var json = File.ReadAllText(FilePath);
                if (string.IsNullOrWhiteSpace(json))
                    return new List<SentRequestEntry>();

                var list = JsonSerializer.Deserialize<List<SentRequestEntry>>(json, JsonOptions);
                return list ?? new List<SentRequestEntry>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SentRequests] 加载失败: {ex.Message}");
                return new List<SentRequestEntry>();
            }
        }

        /// <summary>
        /// 保存最近发包请求（调用方需传入倒序排列、已截断的集合）。
        /// </summary>
        public static void Save(IEnumerable<SentRequestEntry> entries)
        {
            try
            {
                var list = entries.Take(MaxCount).ToList();
                File.WriteAllText(FilePath, JsonSerializer.Serialize(list, JsonOptions));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SentRequests] 保存失败: {ex.Message}");
            }
        }
    }
}
