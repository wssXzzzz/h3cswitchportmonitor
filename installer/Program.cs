using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace H3CSwitchPortMonitorInstaller;

internal static class Program
{
    private const string ServiceName = "H3CSwitchPortMonitor";
    private const string DisplayName = "H3C Switch Port Monitor";
    private const string DefaultInstallDir = @"C:\H3CSwitchPortMonitor";

    [SupportedOSPlatform("windows")]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("该安装器只能在 Windows 上运行。");
            return 1;
        }

        if (!IsAdministrator())
        {
            return RelaunchAsAdministrator(args);
        }

        try
        {
            var installDir = GetOption(args, "--install-dir") ?? DefaultInstallDir;

            if (HasFlag(args, "--uninstall"))
            {
                Uninstall(installDir, removeFiles: HasFlag(args, "--remove-files"));
                PauseIfInteractive(args);
                return 0;
            }

            Install(installDir, noStart: HasFlag(args, "--no-start"), args);
            PauseIfInteractive(args);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("安装失败：");
            Console.Error.WriteLine(ex.Message);
            PauseIfInteractive(args);
            return 1;
        }
    }

    private static void Install(string installDir, bool noStart, string[] args)
    {
        Console.WriteLine("H3C 交换机端口监控服务安装器");
        Console.WriteLine();

        installDir = Prompt("安装目录", installDir, allowEmpty: false);
        var exePath = Path.Combine(installDir, "H3CSwitchPortMonitor.exe");
        var configPath = Path.Combine(installDir, "appsettings.json");
        var serviceExists = ServiceExists();

        if (serviceExists)
        {
            Console.WriteLine("检测到服务已存在，先停止服务以便更新文件。");
            RunScIgnore("stop", ServiceName);
            Thread.Sleep(TimeSpan.FromSeconds(2));
        }

        Directory.CreateDirectory(installDir);
        ExtractPayload(installDir);

        if (!File.Exists(configPath) || PromptYesNo("是否重新生成配置", !File.Exists(configPath)))
        {
            WriteConfiguration(configPath);
        }
        else
        {
            Console.WriteLine($"保留现有配置：{configPath}");
        }

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException("服务程序释放失败，未找到 H3CSwitchPortMonitor.exe。", exePath);
        }

        if (serviceExists)
        {
            RunSc("config", ServiceName, "binPath=", Quote(exePath), "start=", "auto", "DisplayName=", DisplayName);
            RunSc("description", ServiceName, "Monitor H3C switch port status by SNMP and send Feishu robot notifications.");
        }
        else
        {
            RunSc("create", ServiceName, "binPath=", Quote(exePath), "start=", "auto", "DisplayName=", DisplayName);
            RunSc("description", ServiceName, "Monitor H3C switch port status by SNMP and send Feishu robot notifications.");
        }

        if (!noStart)
        {
            RunScIgnore("start", ServiceName);
        }

        Console.WriteLine();
        Console.WriteLine("安装完成。");
        Console.WriteLine($"服务名：{ServiceName}");
        Console.WriteLine($"安装目录：{installDir}");
        Console.WriteLine($"配置文件：{configPath}");
        Console.WriteLine("卸载命令：H3CSwitchPortMonitorInstaller.exe --uninstall");
        Console.WriteLine("卸载并删除文件：H3CSwitchPortMonitorInstaller.exe --uninstall --remove-files");
    }

    private static void Uninstall(string installDir, bool removeFiles)
    {
        Console.WriteLine("正在卸载 H3C 交换机端口监控服务...");

        if (ServiceExists())
        {
            RunScIgnore("stop", ServiceName);
            Thread.Sleep(TimeSpan.FromSeconds(2));
            RunSc("delete", ServiceName);
        }
        else
        {
            Console.WriteLine("服务不存在，跳过服务删除。");
        }

        if (removeFiles && Directory.Exists(installDir))
        {
            Directory.Delete(installDir, recursive: true);
            Console.WriteLine($"已删除安装目录：{installDir}");
        }

        Console.WriteLine("卸载完成。");
    }

    private static void WriteConfiguration(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("请输入监控配置。后续也可以直接编辑 appsettings.json 增加更多交换机。");

        var webhookUrl = Prompt("飞书机器人 WebhookUrl", "", allowEmpty: false);
        var secret = Prompt("飞书机器人 Secret，未启用签名可留空", "", allowEmpty: true);
        var switchName = Prompt("交换机名称", "核心交换机-1", allowEmpty: false);
        var switchHost = Prompt("交换机 IP 或域名", "192.168.1.1", allowEmpty: false);
        var community = Prompt("SNMP Community", "public", allowEmpty: false);
        var interval = PromptInt("轮询间隔秒数", 10, min: 1);

        var config = new
        {
            Logging = new
            {
                LogLevel = new Dictionary<string, string>
                {
                    ["Default"] = "Information",
                    ["Microsoft.Hosting.Lifetime"] = "Information"
                },
                EventLog = new
                {
                    LogLevel = new Dictionary<string, string>
                    {
                        ["Default"] = "Information"
                    }
                }
            },
            Monitor = new
            {
                PollIntervalSeconds = interval,
                AlertOnFirstPoll = false,
                AlertDeviceErrors = true,
                AlertDeviceRecovery = true,
                StateFile = "state/port-state.json",
                Firewall = new
                {
                    EnsureSnmpOutboundRule = true,
                    RuleName = "H3CSwitchPortMonitor SNMP Outbound"
                },
                Feishu = new
                {
                    WebhookUrl = webhookUrl,
                    Secret = secret
                },
                Switches = new object[]
                {
                    new
                    {
                        Name = switchName,
                        Host = switchHost,
                        Port = 161,
                        Community = community,
                        Version = "V2C",
                        TimeoutMs = 5000,
                        MaxRepetitions = 10,
                        IncludeNamePrefixes = new[]
                        {
                            "GigabitEthernet",
                            "Ten-GigabitEthernet",
                            "FortyGigE",
                            "HundredGigE",
                            "Bridge-Aggregation"
                        },
                        IncludeInterfaceIndexes = Array.Empty<int>(),
                        ExcludeInterfaceIndexes = Array.Empty<int>()
                    }
                }
            }
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        File.WriteAllText(configPath, JsonSerializer.Serialize(config, options));
        Console.WriteLine($"已写入配置：{configPath}");
    }

    private static void ExtractPayload(string installDir)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("service.zip");
        if (stream is null)
        {
            throw new InvalidOperationException("安装器缺少内置服务程序包 service.zip，请重新构建安装器。");
        }

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var installRoot = Path.GetFullPath(installDir);
        var installRootWithSeparator = installRoot.EndsWith(Path.DirectorySeparatorChar)
            ? installRoot
            : installRoot + Path.DirectorySeparatorChar;

        foreach (var entry in archive.Entries)
        {
            var targetPath = Path.GetFullPath(Path.Combine(installRoot, entry.FullName));
            if (!targetPath.StartsWith(installRootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(targetPath, installRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"安装包内存在非法路径：{entry.FullName}");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            if (Path.GetFileName(targetPath).Equals("appsettings.json", StringComparison.OrdinalIgnoreCase) && File.Exists(targetPath))
            {
                continue;
            }

            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    private static bool ServiceExists()
    {
        var result = RunProcess("sc.exe", ["query", ServiceName], ignoreExitCode: true);
        return result.ExitCode == 0;
    }

    private static void RunSc(params string[] arguments)
    {
        RunSc(arguments, ignoreExitCode: false);
    }

    private static void RunScIgnore(params string[] arguments)
    {
        RunSc(arguments, ignoreExitCode: true);
    }

    private static void RunSc(string[] arguments, bool ignoreExitCode)
    {
        var result = RunProcess("sc.exe", arguments, ignoreExitCode);
        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            Console.WriteLine(result.Output.Trim());
        }
    }

    private static ProcessResult RunProcess(string fileName, IReadOnlyList<string> arguments, bool ignoreExitCode)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"无法启动进程：{fileName}");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var combined = string.Join(Environment.NewLine, new[] { output, error }.Where(item => !string.IsNullOrWhiteSpace(item)));

        if (process.ExitCode != 0 && !ignoreExitCode)
        {
            throw new InvalidOperationException($"{fileName} {string.Join(' ', arguments)} 执行失败，退出码 {process.ExitCode}：{combined}");
        }

        return new ProcessResult(process.ExitCode, combined);
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int RelaunchAsAdministrator(string[] args)
    {
        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            Console.Error.WriteLine("无法确定安装器路径，请右键选择“以管理员身份运行”。");
            return 1;
        }

        var startInfo = new ProcessStartInfo(exePath)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = string.Join(' ', args.Select(Quote))
        };

        Process.Start(startInfo);
        return 0;
    }

    private static string Prompt(string label, string defaultValue, bool allowEmpty)
    {
        while (true)
        {
            Console.Write(defaultValue.Length == 0 ? $"{label}: " : $"{label} [{defaultValue}]: ");
            var input = Console.ReadLine();
            var value = string.IsNullOrWhiteSpace(input) ? defaultValue : input.Trim();

            if (allowEmpty || !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            Console.WriteLine("该项不能为空。");
        }
    }

    private static int PromptInt(string label, int defaultValue, int min)
    {
        while (true)
        {
            var input = Prompt(label, defaultValue.ToString(), allowEmpty: false);
            if (int.TryParse(input, out var value) && value >= min)
            {
                return value;
            }

            Console.WriteLine($"请输入大于等于 {min} 的整数。");
        }
    }

    private static bool PromptYesNo(string label, bool defaultValue)
    {
        var suffix = defaultValue ? "Y/n" : "y/N";
        Console.Write($"{label} [{suffix}]: ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            return defaultValue;
        }

        return input.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasFlag(IEnumerable<string> args, string flag)
    {
        return args.Any(arg => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetOption(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string Quote(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }

    private static void PauseIfInteractive(string[] args)
    {
        if (HasFlag(args, "--quiet"))
        {
            return;
        }

        Console.WriteLine();
        Console.Write("按回车退出...");
        Console.ReadLine();
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
