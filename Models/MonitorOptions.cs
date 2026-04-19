namespace H3CSwitchPortMonitor.Models;

public sealed class MonitorOptions
{
    public int PollIntervalSeconds { get; set; } = 10;
    public bool AlertOnFirstPoll { get; set; }
    public bool AlertDeviceErrors { get; set; } = true;
    public bool AlertDeviceRecovery { get; set; } = true;
    public int DownConfirmCount { get; set; } = 3;
    public int RetryCount { get; set; } = 2;
    public int RetryDelayMs { get; set; } = 1000;
    public string SnmpTextEncoding { get; set; } = "GB18030";
    public string StateFile { get; set; } = "state/port-state.json";
    public FirewallOptions Firewall { get; set; } = new();
    public FeishuOptions Feishu { get; set; } = new();
    public List<SwitchOptions> Switches { get; set; } = [];
}

public sealed class FirewallOptions
{
    public bool EnsureSnmpOutboundRule { get; set; } = true;
    public string RuleName { get; set; } = "H3CSwitchPortMonitor SNMP Outbound";
}

public sealed class FeishuOptions
{
    public string WebhookUrl { get; set; } = "";
    public string Secret { get; set; } = "";
}

public sealed class SwitchOptions
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 161;
    public string Community { get; set; } = "";
    public string Version { get; set; } = "V2";
    public int TimeoutMs { get; set; } = 20000;
    public int MaxRepetitions { get; set; } = 10;
    public string TextEncoding { get; set; } = "";
    public List<string> IncludeNamePrefixes { get; set; } = [];
    public List<int> IncludeInterfaceIndexes { get; set; } = [];
    public List<int> ExcludeInterfaceIndexes { get; set; } = [];

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Host : Name;
}
