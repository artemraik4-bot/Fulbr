using CommunityToolkit.Mvvm.ComponentModel;
using Fulbr.App.Services;
using Fulbr.Core.Util;

namespace Fulbr.App.ViewModels;

public sealed class SourceItem : ObservableObject
{
    public SourceItem(SourceDto dto) => Dto = dto;

    public SourceDto Dto { get; }

    public string Id => Dto.Id;
    public string Name => Dto.Name;
    public string Url => Dto.Url;
    public bool Enabled => Dto.Enabled;
    public int ServerCount => Dto.ServerCount;
    public string HostDisplay => Redaction.RedactUrl(Dto.Url);

    public string Status => Dto.LastError is not null
        ? "Error"
        : Dto.LastSuccess is not null ? "OK" : "Not fetched";

    public string LastFetchText => Dto.LastFetch is null
        ? "—"
        : Dto.LastFetch.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string LastErrorText => Dto.LastError ?? "None";

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Url));
        OnPropertyChanged(nameof(HostDisplay));
        OnPropertyChanged(nameof(ServerCount));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(LastFetchText));
        OnPropertyChanged(nameof(LastErrorText));
    }
}

public sealed class LogItem
{
    public LogItem(LogDto dto) => Dto = dto;

    public LogDto Dto { get; }
    public string TimeText => Dto.Timestamp.ToLocalTime().ToString("HH:mm:ss");
    public string Level => Dto.Level;
    public string Message => Dto.Message;
    public string Display => $"{TimeText}  {Level,-5}  {Message}";
}

public sealed class ProbeRow
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string ServerName { get; init; }
    public required string Method { get; init; }
    public required string Result { get; init; }
    public double? LatencyMs { get; init; }

    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss");
    public string LatencyText => LatencyMs is null ? "—" : $"{LatencyMs:0} ms";
}
