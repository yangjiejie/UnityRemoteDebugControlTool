using System.IO;
using System.Text.Json;

namespace RemoteDebugServer.Helpers
{
    /// <summary>
    /// proto 代码生成的持久化设置：proto 目录、输出目录、protoc 路径。
    /// 与 DeviceAliasStore / SentRequestsStore 保持一致的 JSON 持久化风格。
    /// </summary>
    public class ProtoGenSettings
    {
        public string ProtoDirectory { get; set; } = "";
        public string OutputDirectory { get; set; } = "";
        public string ProtocPath { get; set; } = "";
    }

    public static class ProtoGenSettingsStore
    {
        private static readonly string FilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "protogen_settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static ProtoGenSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new ProtoGenSettings();

                var json = File.ReadAllText(FilePath);
                if (string.IsNullOrWhiteSpace(json))
                    return new ProtoGenSettings();

                return JsonSerializer.Deserialize<ProtoGenSettings>(json, JsonOptions)
                       ?? new ProtoGenSettings();
            }
            catch
            {
                return new ProtoGenSettings();
            }
        }

        public static void Save(ProtoGenSettings settings)
        {
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProtoGen] 保存设置失败: {ex.Message}");
            }
        }
    }
}
