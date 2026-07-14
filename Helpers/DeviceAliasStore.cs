using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RemoteDebugServer.Helpers
{
    /// <summary>
    /// 设备别名本地持久化存储 — DeviceName（机器名）→ Alias 映射表
    /// 
    /// 设计要点：
    /// - 主键用 DeviceName（机器名），因为它是稳定的跨会话标识，不会因客户端重连而变化。
    /// - 客户端 DeviceId 包含随机数（旧版）或哈希（新版），不保证跨会话完全相同。
    /// - 预留旧 DeviceId 格式兼容（启动时自动迁移）。
    /// </summary>
    public static class DeviceAliasStore
    {
        private static readonly string FilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "device_aliases.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>
        /// 加载全部别名映射。键为稳定的机器名（DeviceName）。
        /// 首次加载时自动尝试迁移旧格式（DeviceId → DeviceName）。
        /// </summary>
        public static Dictionary<string, string> LoadAll()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                       ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// 获取指定设备的别名。
        /// - 优先按 deviceName（机器名，稳定键）查找
        /// - 其次按 deviceId 查找（兼容旧格式）
        /// </summary>
        public static string? Load(string deviceId, string deviceName)
        {
            var all = LoadAll();

            // 主查找：机器名
            if (all.TryGetValue(deviceName, out var alias) && !string.IsNullOrWhiteSpace(alias))
                return alias;

            // 兼容旧格式：DeviceId 作为键
            if (all.TryGetValue(deviceId, out alias) && !string.IsNullOrWhiteSpace(alias))
                return alias;

            return null;
        }

        /// <summary>
        /// 保存别名（key = deviceName 机器名）。传入空字符串则删除。
        /// 同时写入 fileName 字段记录最后编辑的机器名，方便 debug。
        /// </summary>
        public static void Save(string deviceId, string deviceName, string alias)
        {
            try
            {
                var all = LoadAll();
                if (string.IsNullOrWhiteSpace(alias))
                {
                    all.Remove(deviceName);
                }
                else
                {
                    all[deviceName] = alias;
                }
                File.WriteAllText(FilePath, JsonSerializer.Serialize(all, JsonOptions));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceAlias] 保存失败: {ex.Message}");
            }
        }
    }
}
