namespace H3CSwitchPortMonitor.Models;

public sealed record InterfaceSnapshot(
    int Index,
    string Name,
    string Description,
    string Alias,
    int AdminStatus,
    int OperStatus)
{
    public string EffectiveName => string.IsNullOrWhiteSpace(Name) ? Description : Name;

    public string AdminStatusText => AdminStatus switch
    {
        1 => "up",
        2 => "down",
        3 => "testing",
        _ => $"unknown({AdminStatus})"
    };

    public string OperStatusText => OperStatus switch
    {
        1 => "up",
        2 => "down",
        3 => "testing",
        4 => "unknown",
        5 => "dormant",
        6 => "notPresent",
        7 => "lowerLayerDown",
        _ => $"unknown({OperStatus})"
    };
}
