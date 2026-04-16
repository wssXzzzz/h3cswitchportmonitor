using System.Text.Json;
using H3CSwitchPortMonitor.Models;
using Microsoft.Extensions.Options;

namespace H3CSwitchPortMonitor.Services;

public sealed class PortStateStore
{
    private readonly MonitorOptions _options;
    private readonly ILogger<PortStateStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public PortStateStore(IOptions<MonitorOptions> options, ILogger<PortStateStore> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Dictionary<string, InterfaceSnapshot>> LoadAsync(CancellationToken cancellationToken)
    {
        var path = GetStateFilePath();

        if (!File.Exists(path))
        {
            return new Dictionary<string, InterfaceSnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var data = await JsonSerializer.DeserializeAsync<Dictionary<string, InterfaceSnapshot>>(stream, _jsonOptions, cancellationToken);
            return data is null
                ? new Dictionary<string, InterfaceSnapshot>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, InterfaceSnapshot>(data, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to load state file {StateFile}. Starting with empty state.", path);
            return new Dictionary<string, InterfaceSnapshot>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task SaveAsync(IReadOnlyDictionary<string, InterfaceSnapshot> states, CancellationToken cancellationToken)
    {
        var path = GetStateFilePath();
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, states, _jsonOptions, cancellationToken);
        }

        File.Copy(tempPath, path, overwrite: true);
        File.Delete(tempPath);
    }

    private string GetStateFilePath()
    {
        if (Path.IsPathRooted(_options.StateFile))
        {
            return _options.StateFile;
        }

        return Path.Combine(AppContext.BaseDirectory, _options.StateFile);
    }
}
