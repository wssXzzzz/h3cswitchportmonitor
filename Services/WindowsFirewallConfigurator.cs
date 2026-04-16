using System.Diagnostics;
using H3CSwitchPortMonitor.Models;
using Microsoft.Extensions.Options;

namespace H3CSwitchPortMonitor.Services;

public sealed class WindowsFirewallConfigurator
{
    private readonly MonitorOptions _options;
    private readonly ILogger<WindowsFirewallConfigurator> _logger;

    public WindowsFirewallConfigurator(IOptions<MonitorOptions> options, ILogger<WindowsFirewallConfigurator> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task EnsureSnmpOutboundRuleAsync(CancellationToken cancellationToken)
    {
        if (!_options.Firewall.EnsureSnmpOutboundRule)
        {
            _logger.LogInformation("SNMP outbound firewall rule auto-configuration is disabled.");
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            _logger.LogInformation("Skipping Windows Firewall configuration because the current OS is not Windows.");
            return;
        }

        var ruleName = string.IsNullOrWhiteSpace(_options.Firewall.RuleName)
            ? "H3CSwitchPortMonitor SNMP Outbound"
            : _options.Firewall.RuleName.Trim();

        var ports = _options.Switches
            .Select(device => device.Port <= 0 ? 161 : device.Port)
            .Distinct()
            .OrderBy(port => port)
            .ToArray();

        if (ports.Length == 0)
        {
            ports = [161];
        }

        try
        {
            var existing = await RunNetshAsync(
                ["advfirewall", "firewall", "show", "rule", $"name={ruleName}"],
                ignoreExitCode: true,
                cancellationToken);

            if (existing.ExitCode == 0)
            {
                _logger.LogInformation("Windows Firewall rule already exists. Refreshing rule: {RuleName}", ruleName);
                await RunNetshAsync(
                    ["advfirewall", "firewall", "delete", "rule", $"name={ruleName}"],
                    ignoreExitCode: true,
                    cancellationToken);
            }

            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                _logger.LogWarning("Cannot determine current process path. Skipping Windows Firewall rule creation.");
                return;
            }

            var portList = string.Join(",", ports);
            var created = await RunNetshAsync(
                [
                    "advfirewall",
                    "firewall",
                    "add",
                    "rule",
                    $"name={ruleName}",
                    "dir=out",
                    "action=allow",
                    "protocol=UDP",
                    $"remoteport={portList}",
                    $"program={processPath}",
                    "enable=yes",
                    "profile=any"
                ],
                ignoreExitCode: true,
                cancellationToken);

            if (created.ExitCode == 0)
            {
                _logger.LogInformation(
                    "Windows Firewall outbound UDP rule created. Rule: {RuleName}, ports: {Ports}",
                    ruleName,
                    portList);
                return;
            }

            _logger.LogWarning(
                "Failed to create Windows Firewall outbound UDP rule. Rule: {RuleName}, ports: {Ports}, exit code: {ExitCode}, output: {Output}",
                ruleName,
                portList,
                created.ExitCode,
                created.Output);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-configure Windows Firewall outbound UDP rule.");
        }
    }

    private static async Task<ProcessResult> RunNetshAsync(
        IReadOnlyList<string> arguments,
        bool ignoreExitCode,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("netsh.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start netsh.exe.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;
        var combined = string.Join(Environment.NewLine, new[] { output, error }.Where(item => !string.IsNullOrWhiteSpace(item)));

        if (process.ExitCode != 0 && !ignoreExitCode)
        {
            throw new InvalidOperationException($"netsh.exe failed with exit code {process.ExitCode}: {combined}");
        }

        return new ProcessResult(process.ExitCode, combined);
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
