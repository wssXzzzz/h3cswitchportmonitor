using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using H3CSwitchPortMonitor.Models;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Microsoft.Extensions.Options;

namespace H3CSwitchPortMonitor.Services;

public sealed class SharpSnmpClient : ISnmpClient
{
    private const string IfDescrOid = "1.3.6.1.2.1.2.2.1.2";
    private const string IfAdminStatusOid = "1.3.6.1.2.1.2.2.1.7";
    private const string IfOperStatusOid = "1.3.6.1.2.1.2.2.1.8";
    private const string IfNameOid = "1.3.6.1.2.1.31.1.1.1.1";
    private const string IfAliasOid = "1.3.6.1.2.1.31.1.1.1.18";
    private readonly MonitorOptions _options;
    private readonly ILogger<SharpSnmpClient> _logger;

    public SharpSnmpClient(IOptions<MonitorOptions> options, ILogger<SharpSnmpClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<InterfaceSnapshot>> ReadInterfacesAsync(SwitchOptions device, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var version = ParseVersion(device.Version);
            var address = Dns.GetHostAddresses(device.Host)
                .OrderBy(ip => ip.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .First();
            var endpoint = new IPEndPoint(address, device.Port);
            var community = new OctetString(device.Community);
            var timeout = Math.Max(1000, device.TimeoutMs);
            var maxRepetitions = Math.Max(1, device.MaxRepetitions);
            var textEncoding = ResolveTextEncoding(device);

            var descriptions = WalkText(version, endpoint, community, IfDescrOid, timeout, maxRepetitions, textEncoding);
            var names = WalkText(version, endpoint, community, IfNameOid, timeout, maxRepetitions, textEncoding);
            var aliases = WalkText(version, endpoint, community, IfAliasOid, timeout, maxRepetitions, textEncoding);
            var adminStatuses = WalkInt(version, endpoint, community, IfAdminStatusOid, timeout, maxRepetitions);
            var operStatuses = WalkInt(version, endpoint, community, IfOperStatusOid, timeout, maxRepetitions);

            return operStatuses
                .OrderBy(item => item.Key)
                .Select(item =>
                {
                    var index = item.Key;
                    return new InterfaceSnapshot(
                        index,
                        names.GetValueOrDefault(index, ""),
                        descriptions.GetValueOrDefault(index, ""),
                        aliases.GetValueOrDefault(index, ""),
                        adminStatuses.GetValueOrDefault(index, 0),
                        item.Value);
                })
                .ToList();
        }, cancellationToken);
    }

    private Encoding ResolveTextEncoding(SwitchOptions device)
    {
        var encodingName = string.IsNullOrWhiteSpace(device.TextEncoding)
            ? _options.SnmpTextEncoding
            : device.TextEncoding;

        if (string.IsNullOrWhiteSpace(encodingName))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(encodingName.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            _logger.LogWarning(
                ex,
                "Unsupported SNMP text encoding {EncodingName} for {SwitchName}. Falling back to UTF-8.",
                encodingName,
                device.DisplayName);
            return Encoding.UTF8;
        }
    }

    private static Dictionary<int, string> WalkText(
        VersionCode version,
        IPEndPoint endpoint,
        OctetString community,
        string baseOid,
        int timeout,
        int maxRepetitions,
        Encoding textEncoding)
    {
        return Walk(version, endpoint, community, baseOid, timeout, maxRepetitions)
            .Select(variable => new { Index = TryGetIndex(baseOid, variable.Id), Value = ToDisplayString(variable.Data, textEncoding) })
            .Where(item => item.Index.HasValue)
            .ToDictionary(item => item.Index!.Value, item => item.Value);
    }

    private static Dictionary<int, int> WalkInt(
        VersionCode version,
        IPEndPoint endpoint,
        OctetString community,
        string baseOid,
        int timeout,
        int maxRepetitions)
    {
        return Walk(version, endpoint, community, baseOid, timeout, maxRepetitions)
            .Select(variable => new { Index = TryGetIndex(baseOid, variable.Id), Value = ToInt(variable.Data) })
            .Where(item => item.Index.HasValue && item.Value.HasValue)
            .ToDictionary(item => item.Index!.Value, item => item.Value!.Value);
    }

    private static List<Variable> Walk(
        VersionCode version,
        IPEndPoint endpoint,
        OctetString community,
        string baseOid,
        int timeout,
        int maxRepetitions)
    {
        var result = new List<Variable>();
        var oid = new ObjectIdentifier(baseOid);

        if (version == VersionCode.V1)
        {
            Messenger.Walk(version, endpoint, community, oid, result, timeout, WalkMode.WithinSubtree);
            return result;
        }

        Messenger.BulkWalk(version, endpoint, community, null, oid, result, timeout, maxRepetitions, WalkMode.WithinSubtree, null, null);
        return result;
    }

    private static VersionCode ParseVersion(string version)
    {
        return version.Trim().ToUpperInvariant() switch
        {
            "V1" or "1" => VersionCode.V1,
            "V2" or "V2C" or "2" => VersionCode.V2,
            _ => throw new InvalidOperationException($"Unsupported SNMP version: {version}. Only V1 and V2C are supported.")
        };
    }

    private static int? TryGetIndex(string baseOid, ObjectIdentifier oid)
    {
        var text = oid.ToString().TrimStart('.');
        var prefix = baseOid.TrimStart('.') + ".";

        if (!text.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var indexText = text[prefix.Length..];
        return int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            ? index
            : null;
    }

    private static string ToDisplayString(ISnmpData data, Encoding textEncoding)
    {
        return data switch
        {
            OctetString octets => octets.ToString(textEncoding).TrimEnd('\0'),
            _ => data.ToString()
        };
    }

    private static int? ToInt(ISnmpData data)
    {
        if (data is Integer32 integer)
        {
            return integer.ToInt32();
        }

        return int.TryParse(data.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
