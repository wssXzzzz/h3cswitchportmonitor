using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using H3CSwitchPortMonitor.Models;
using Microsoft.Extensions.Options;

namespace H3CSwitchPortMonitor.Services;

public sealed class FeishuNotifier
{
    private readonly HttpClient _httpClient;
    private readonly MonitorOptions _options;
    private readonly ILogger<FeishuNotifier> _logger;

    public FeishuNotifier(HttpClient httpClient, IOptions<MonitorOptions> options, ILogger<FeishuNotifier> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public Task NotifyPortChangedAsync(
        SwitchOptions device,
        InterfaceSnapshot? previous,
        InterfaceSnapshot current,
        CancellationToken cancellationToken)
    {
        var oldStatus = previous?.OperStatusText ?? "unknown";
        var text = new StringBuilder()
            .AppendLine("[端口状态变化]")
            .AppendLine($"设备：{device.DisplayName} ({device.Host})")
            .AppendLine($"端口：{current.EffectiveName} (ifIndex {current.Index})")
            .AppendLine($"状态：{oldStatus} -> {current.OperStatusText}")
            .AppendLine($"管理状态：{current.AdminStatusText}")
            .AppendLine($"端口备注：{(string.IsNullOrWhiteSpace(current.Alias) ? "无" : current.Alias)}")
            .AppendLine($"时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}")
            .ToString();

        return SendTextAsync(text, cancellationToken);
    }

    public async Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        var webhookUrl = _options.Feishu.WebhookUrl;
        var payload = BuildPayload(text, _options.Feishu.Secret);
        var json = JsonSerializer.Serialize(payload);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(webhookUrl, content, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Feishu webhook returned HTTP {(int)response.StatusCode}: {responseText}");
        }

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        var code = root.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Number
            ? codeElement.GetInt32()
            : 0;

        if (code != 0)
        {
            var message = root.TryGetProperty("msg", out var msgElement) ? msgElement.GetString() : responseText;
            throw new InvalidOperationException($"Feishu webhook rejected the message: code={code}, msg={message}");
        }

        _logger.LogInformation("Feishu notification sent.");
    }

    private static Dictionary<string, object> BuildPayload(string text, string secret)
    {
        var payload = new Dictionary<string, object>
        {
            ["msg_type"] = "text",
            ["content"] = new Dictionary<string, string>
            {
                ["text"] = text
            }
        };

        if (!string.IsNullOrWhiteSpace(secret))
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            payload["timestamp"] = timestamp;
            payload["sign"] = GenerateSign(timestamp, secret);
        }

        return payload;
    }

    private static string GenerateSign(string timestamp, string secret)
    {
        var stringToSign = $"{timestamp}\n{secret}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(stringToSign));
        return Convert.ToBase64String(hmac.ComputeHash(Array.Empty<byte>()));
    }
}
