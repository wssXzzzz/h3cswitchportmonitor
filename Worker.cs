using H3CSwitchPortMonitor.Models;
using H3CSwitchPortMonitor.Services;
using Microsoft.Extensions.Options;

namespace H3CSwitchPortMonitor;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly MonitorOptions _options;
    private readonly ISnmpClient _snmpClient;
    private readonly FeishuNotifier _notifier;
    private readonly PortStateStore _stateStore;
    private readonly WindowsFirewallConfigurator _firewallConfigurator;
    private readonly Dictionary<string, bool> _deviceErrorState = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _portDownCounters = new(StringComparer.OrdinalIgnoreCase);

    public Worker(
        ILogger<Worker> logger,
        IOptions<MonitorOptions> options,
        ISnmpClient snmpClient,
        FeishuNotifier notifier,
        PortStateStore stateStore,
        WindowsFirewallConfigurator firewallConfigurator)
    {
        _logger = logger;
        _options = options.Value;
        _snmpClient = snmpClient;
        _notifier = notifier;
        _stateStore = stateStore;
        _firewallConfigurator = firewallConfigurator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunMonitorAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("H3C switch port monitor stopped.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "H3C switch port monitor stopped unexpectedly.");
            await StartupDiagnostics.RecordFatalAsync("Worker stopped unexpectedly.", ex, CancellationToken.None);
            throw;
        }
    }

    private async Task RunMonitorAsync(CancellationToken stoppingToken)
    {
        ValidateOptions();
        await _firewallConfigurator.EnsureSnmpOutboundRuleAsync(stoppingToken);

        var previousStates = await _stateStore.LoadAsync(stoppingToken);
        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));

        _logger.LogInformation("H3C switch port monitor started. Switches: {SwitchCount}, interval: {Interval}s",
            _options.Switches.Count,
            pollInterval.TotalSeconds);

        using var timer = new PeriodicTimer(pollInterval);

        do
        {
            var currentStates = new Dictionary<string, InterfaceSnapshot>(previousStates, StringComparer.OrdinalIgnoreCase);

            foreach (var device in _options.Switches)
            {
                stoppingToken.ThrowIfCancellationRequested();
                await PollDeviceAsync(device, previousStates, currentStates, stoppingToken);
            }

            previousStates = currentStates;
            await _stateStore.SaveAsync(previousStates, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollDeviceAsync(
        SwitchOptions device,
        IReadOnlyDictionary<string, InterfaceSnapshot> previousStates,
        IDictionary<string, InterfaceSnapshot> currentStates,
        CancellationToken stoppingToken)
    {
        var deviceKey = DeviceKey(device);
        IReadOnlyList<InterfaceSnapshot> interfaces;

        try
        {
            interfaces = await ReadInterfacesWithRetryAsync(device, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to poll switch {SwitchName} ({Host}:{Port})", device.DisplayName, device.Host, device.Port);

            var alreadyFailed = _deviceErrorState.TryGetValue(deviceKey, out var wasFailed) && wasFailed;
            _deviceErrorState[deviceKey] = true;

            if (_options.AlertDeviceErrors && !alreadyFailed)
            {
                await TrySendTextAsync(
                    $"[告警] 交换机 SNMP 读取失败\n设备：{device.DisplayName}\n地址：{device.Host}:{device.Port}\n错误：{ex.Message}",
                    stoppingToken);
            }

            return;
        }

        var hadPreviousError = _deviceErrorState.TryGetValue(deviceKey, out var hadFailed) && hadFailed;

        if (hadPreviousError && _options.AlertDeviceRecovery)
        {
            await TrySendTextAsync($"[恢复] 交换机 SNMP 读取恢复\n设备：{device.DisplayName}\n地址：{device.Host}:{device.Port}", stoppingToken);
        }

        _deviceErrorState[deviceKey] = false;
        RemoveDeviceStates(currentStates, device);

        foreach (var port in interfaces.Where(port => ShouldMonitorPort(device, port)))
        {
            var stateKey = StateKey(device, port.Index);
            currentStates[stateKey] = port;

            if (!previousStates.TryGetValue(stateKey, out var previous))
            {
                if (_options.AlertOnFirstPoll)
                {
                    await TryNotifyPortChangedAsync(device, null, port, stoppingToken);
                }

                continue;
            }

            if (previous.OperStatus != port.OperStatus)
            {
                if (port.OperStatus == 2) // went down
                {
                    var downCount = _portDownCounters.GetValueOrDefault(stateKey, 0) + 1;
                    _portDownCounters[stateKey] = downCount;

                    if (downCount >= _options.DownConfirmCount)
                    {
                        await TryNotifyPortChangedAsync(device, previous, port, stoppingToken);
                    }
                }
                else if (port.OperStatus == 1) // recovered to up
                {
                    _portDownCounters[stateKey] = 0;
                    await TryNotifyPortChangedAsync(device, previous, port, stoppingToken);
                }
                else
                {
                    await TryNotifyPortChangedAsync(device, previous, port, stoppingToken);
                }
            }
        }
    }

    private async Task<IReadOnlyList<InterfaceSnapshot>> ReadInterfacesWithRetryAsync(
        SwitchOptions device,
        CancellationToken stoppingToken)
    {
        var retryCount = Math.Max(0, _options.RetryCount);
        var retryDelay = TimeSpan.FromMilliseconds(Math.Max(0, _options.RetryDelayMs));

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await _snmpClient.ReadInterfacesAsync(device, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < retryCount)
            {
                _logger.LogWarning(ex,
                    "SNMP poll failed for {SwitchName} ({Host}:{Port}). Retry {RetryAttempt}/{RetryCount} after {RetryDelayMs}ms.",
                    device.DisplayName,
                    device.Host,
                    device.Port,
                    attempt + 1,
                    retryCount,
                    retryDelay.TotalMilliseconds);

                if (retryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(retryDelay, stoppingToken);
                }
            }
        }
    }

    private async Task TryNotifyPortChangedAsync(
        SwitchOptions device,
        InterfaceSnapshot? previous,
        InterfaceSnapshot current,
        CancellationToken stoppingToken)
    {
        try
        {
            await _notifier.NotifyPortChangedAsync(device, previous, current, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send Feishu notification for {SwitchName} interface {InterfaceIndex}",
                device.DisplayName,
                current.Index);
        }
    }

    private async Task TrySendTextAsync(string text, CancellationToken stoppingToken)
    {
        try
        {
            await _notifier.SendTextAsync(text, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Feishu notification.");
        }
    }

    private static bool ShouldMonitorPort(SwitchOptions device, InterfaceSnapshot port)
    {
        if (device.IncludeInterfaceIndexes.Count > 0 && !device.IncludeInterfaceIndexes.Contains(port.Index))
        {
            return false;
        }

        if (device.ExcludeInterfaceIndexes.Contains(port.Index))
        {
            return false;
        }

        if (device.IncludeNamePrefixes.Count == 0)
        {
            return true;
        }

        return device.IncludeNamePrefixes.Any(prefix =>
            port.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            port.Description.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private void ValidateOptions()
    {
        if (_options.Switches.Count == 0)
        {
            throw new InvalidOperationException("Monitor:Switches must contain at least one switch.");
        }

        if (string.IsNullOrWhiteSpace(_options.Feishu.WebhookUrl))
        {
            throw new InvalidOperationException("Monitor:Feishu:WebhookUrl is required.");
        }

        foreach (var device in _options.Switches)
        {
            if (string.IsNullOrWhiteSpace(device.Host))
            {
                throw new InvalidOperationException("Every switch must have a Host.");
            }

            if (string.IsNullOrWhiteSpace(device.Community))
            {
                throw new InvalidOperationException($"Switch {device.DisplayName} must have an SNMP community.");
            }
        }
    }

    private static string DeviceKey(SwitchOptions device) => $"{device.Host}:{device.Port}";

    private static string StateKey(SwitchOptions device, int interfaceIndex) => $"{DeviceKey(device)}:{interfaceIndex}";

    private static void RemoveDeviceStates(IDictionary<string, InterfaceSnapshot> states, SwitchOptions device)
    {
        var prefix = DeviceKey(device) + ":";
        foreach (var key in states.Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            states.Remove(key);
        }
    }
}
