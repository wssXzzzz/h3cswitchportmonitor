using H3CSwitchPortMonitor.Models;

namespace H3CSwitchPortMonitor.Services;

public interface ISnmpClient
{
    Task<IReadOnlyList<InterfaceSnapshot>> ReadInterfacesAsync(SwitchOptions device, CancellationToken cancellationToken);
}
