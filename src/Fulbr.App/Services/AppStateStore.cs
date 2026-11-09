using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fulbr.Core.Models;

namespace Fulbr.App.Services;

public sealed class AppState
{
    public List<SourceDto> Sources { get; set; } = new();
    public List<ServerDto> Servers { get; set; } = new();
    public SettingsDto Settings { get; set; } = new();
    public List<LogDto> Logs { get; set; } = new();
}

public sealed class SettingsDto
{
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "System";
    public bool MinimizeToTray { get; set; } = true;
    public string RankingPreset { get; set; } = "Balanced";
}

public sealed class SourceDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? UrlProtected { get; set; }
    public bool Enabled { get; set; } = true;
    public string? ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public DateTimeOffset? LastFetch { get; set; }
    public DateTimeOffset? LastSuccess { get; set; }
    public string? LastError { get; set; }
    public int ServerCount { get; set; }
}

public sealed class ServerDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ProxyProtocol Protocol { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string? SourceId { get; set; }
    public string? SourceName { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsUserCreated { get; set; }
    public bool InsecureTls { get; set; }
    public bool TlsEnabled { get; set; }
    public string? Sni { get; set; }
    public string? Transport { get; set; }
    public string? Flow { get; set; }
    public List<string> Warnings { get; set; } = new();
    public string Trust { get; set; } = "Unknown";
    public string? RawProtected { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public double? LastLatencyMs { get; set; }
    public double? MedianLatencyMs { get; set; }
    public double? JitterMs { get; set; }
    public string? LastProbeMethod { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public string? LastError { get; set; }
}

public sealed class LogDto
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Level { get; set; } = "Info";
    public string Message { get; set; } = string.Empty;
}

public sealed class AppStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public AppStateStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Fulbr", "state.json");
    }

    public string StatePath => _path;

    public async Task<AppState> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_path))
                return new AppState();
            var json = await File.ReadAllTextAsync(_path, cancellationToken);
            return JsonSerializer.Deserialize<AppState>(json, JsonOptions) ?? new AppState();
        }
        catch
        {
            return new AppState();
        }
    }

    public async Task SaveAsync(AppState state, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(state, JsonOptions);
        var temp = _path + ".tmp";
        await File.WriteAllTextAsync(temp, json, cancellationToken);
        File.Move(temp, _path, overwrite: true);
    }
}
