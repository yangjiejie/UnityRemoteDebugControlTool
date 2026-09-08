using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace RemoteDebugServer.Helpers
{
    /// <summary>
    /// 调用 protoc 将 .proto 文件生成 C# 协议代码。
    /// C# 代码生成器内置于 protoc（--csharp_out），无需额外插件。
    /// </summary>
    public class ProtoGenResult
    {
        public bool Success { get; set; }
        public string Output { get; set; } = "";
        public string Error { get; set; } = "";
        public int GeneratedCount { get; set; }
    }

    public static class ProtoCodeGenerator
    {
        /// <summary>
        /// 解析 protoc 可执行文件路径，查找顺序：
        /// 1. 显式指定的路径（若存在）
        /// 2. PATH 环境变量中的 protoc
        /// 3. 捆绑目录：&lt;AppBase&gt;/Tools/protoc/protoc.exe 及其向上两级的工程根 Tools 目录
        /// </summary>
        public static string? ResolveProtoc(string? explicitPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
                return explicitPath;

            var fromPath = FindInPath("protoc.exe") ?? FindInPath("protoc");
            if (fromPath != null) return fromPath;

            var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            var candidates = new[]
            {
                Path.Combine(baseDir, "Tools", "protoc", "protoc.exe"),
                Path.Combine(baseDir, "..", "Tools", "protoc", "protoc.exe"),
                Path.Combine(baseDir, "..", "..", "Tools", "protoc", "protoc.exe"),
            };
            foreach (var c in candidates)
            {
                var full = Path.GetFullPath(c);
                if (File.Exists(full)) return full;
            }

            return null;
        }

        private static string? FindInPath(string name)
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var p = Path.Combine(dir, name);
                    if (File.Exists(p)) return p;
                }
                catch
                {
                    // 忽略非法路径段
                }
            }
            return null;
        }

        /// <summary>
        /// 执行 protoc 生成 C# 代码。
        /// </summary>
        /// <param name="protocPath">protoc 可执行文件完整路径</param>
        /// <param name="protoDir">包含 .proto 文件的目录（同时作为 --proto_path）</param>
        /// <param name="outputDir">生成的 .cs 文件输出目录（--csharp_out）</param>
        public static ProtoGenResult Generate(string protocPath, string protoDir, string outputDir)
        {
            var result = new ProtoGenResult();

            try
            {
                if (string.IsNullOrWhiteSpace(protocPath) || !File.Exists(protocPath))
                {
                    result.Error = "未找到 protoc 可执行文件。请安装 protobuf 编译器并将 protoc 加入 PATH，或点击『选择 protoc』手动指定。";
                    return result;
                }

                if (string.IsNullOrWhiteSpace(protoDir) || !Directory.Exists(protoDir))
                {
                    result.Error = $"proto 目录不存在或为空: {protoDir}";
                    return result;
                }

                var protos = Directory.GetFiles(protoDir, "*.proto", SearchOption.TopDirectoryOnly);
                if (protos.Length == 0)
                {
                    result.Error = $"目录中未找到任何 .proto 文件: {protoDir}";
                    return result;
                }

                Directory.CreateDirectory(outputDir);

                var args = new StringBuilder();
                args.Append($"--proto_path=\"{protoDir}\" ");
                args.Append($"--csharp_out=\"{outputDir}\" ");
                foreach (var f in protos)
                {
                    args.Append($"\"{f}\" ");
                }

                var psi = new ProcessStartInfo
                {
                    FileName = protocPath,
                    Arguments = args.ToString().TrimEnd(),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using var proc = Process.Start(psi)!;
                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                if (proc.ExitCode == 0)
                {
                    result.Success = true;
                    result.GeneratedCount = protos.Length;
                    result.Output = $"已生成 {protos.Length} 个 C# 文件到 {outputDir}";
                    if (!string.IsNullOrWhiteSpace(stdout)) result.Output += "\n" + stdout.Trim();
                }
                else
                {
                    result.Success = false;
                    result.Error = string.IsNullOrWhiteSpace(stderr)
                        ? $"protoc 退出码 {proc.ExitCode}"
                        : stderr.Trim();
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = $"生成异常: {ex.Message}";
            }

            return result;
        }
    }
}
