namespace H3CSwitchPortMonitor.Models;

public sealed class MonitorOptions
{
    public int PollIntervalSeconds { get; set; } = 10;
    public bool AlertOnFirstPoll { get; set; }
    public bool AlertDeviceErrors { get; set; } = true;
    public bool AlertDeviceRecovery { get; set; } = true;
    public string StateFile { get; set; } = "state/port-state.json";
    public FeishuOptions Feishu { get; set; } = new();
    public List<SwitchOptions> Switches { get; set; } = [];
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
    public string Community { get; set; } = "public";
    public string Version { get; set; } = "V2";
    public int TimeoutMs { get; set; } = 5000;
    public int MaxRepetitions { get; set; } = 10;
    public List<string> IncludeNamePrefixes { get; set; } = [];
    public List<int> IncludeInterfaceIndexes { get; set; } = [];
    public List<int> ExcludeInterfaceIndexes { get; set; } = [];

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Host : Name;
}
