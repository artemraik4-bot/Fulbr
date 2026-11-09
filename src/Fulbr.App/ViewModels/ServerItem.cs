using CommunityToolkit.Mvvm.ComponentModel;
using Fulbr.Core.Diagnostics;
using Fulbr.Core.Models;
using Fulbr.Core.Ranking;
using Fulbr.Core.Util;

namespace Fulbr.App.ViewModels;

public sealed class ServerItem : ObservableObject
{
    private readonly List<double> _samples = new();
    private readonly ServerRanker _ranker = new();

    public ServerItem(ServerRecord record, string? rawLink)
    {
        Record = record;
        RawLink = rawLink;
        Candidate = new ServerCandidate(
            record.Protocol, record.Host, record.Port, record.DisplayName,
            rawLink ?? string.Empty, record.Fingerprint, null,
            record.Sni, record.Transport, record.Flow, record.TlsEnabled,
            record.InsecureTls, record.Warnings);
    }

    public ServerRecord Record { get; }
    public ServerCandidate Candidate { get; }
    public string? RawLink { get; }

    public string Id => Record.Id;
    public string DisplayName => DisplayText.CleanName(Record.DisplayName, Record.Endpoint);
    public string ProtocolName => Record.ProtocolName;
    public string Endpoint => Record.Endpoint;
    public string Host => Record.Host;
    public string SourceDisplay => Record.SourceName ?? "Manual";
    public string Trust => Record.Trust;
    public bool InsecureTls => Record.InsecureTls;
    public bool TlsEnabled => Record.TlsEnabled;
    public string? Sni => Record.Sni;
    public string? Transport => Record.Transport;
    public string? Flow => Record.Flow;
    public IReadOnlyList<string> Warnings => Record.Warnings;
    public ProxyProtocol Protocol => Record.Protocol;

    public bool IsFavorite
    {
        get => Record.IsFavorite;
        set
        {
            if (Record.IsFavorite == value) return;
            Record.IsFavorite = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FavoriteGlyph));
        }
    }

    public string FavoriteGlyph => IsFavorite ? "★" : "☆";

    public string Status => Record.LastCheckedAt is null
        ? "Not tested"
        : Record.LastError is not null ? "Unavailable" : "Reachable";

    public string LatencyText => Record.LastCheckedAt is null
        ? "—"
        : Record.LastError is not null
            ? "failed"
            : Record.LastLatencyMs is null ? "—" : $"{Record.LastLatencyMs:0} ms";

    public string MethodText => Record.LastProbeMethod ?? "—";

    public string ReliabilityText
    {
        get
        {
            var total = Record.SuccessCount + Record.FailureCount;
            return total == 0 ? "—" : $"{100.0 * Record.SuccessCount / total:0}%";
        }
    }

    public string WarningsText => Warnings.Count == 0 ? "None" : string.Join("\n", Warnings);

    public double Score => _ranker.RankOne(Record, RankingPreset.Balanced).Score;

    public string ScoreText => Record.LastCheckedAt is null ? "—" : $"{Score:0}";

    public string LastErrorText => Record.LastError ?? "None";

    public string LastCheckedText => Record.LastCheckedAt is null
        ? "—"
        : Record.LastCheckedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public void ApplyPing(PingResult result)
    {
        Record.LastCheckedAt = result.CheckedAt;
        Record.LastProbeMethod = result.Method;

        if (!result.Supported)
        {
            Record.LastError = result.Error;
        }
        else if (result.Success)
        {
            Record.SuccessCount++;
            Record.LastError = null;
            Record.LastLatencyMs = result.LatencyMs;
            if (result.LatencyMs is double ms)
            {
                _samples.Add(ms);
                if (_samples.Count > 20)
                    _samples.RemoveAt(0);
                RecomputeStatistics();
            }
        }
        else
        {
            Record.FailureCount++;
            Record.LastError = result.Error;
            Record.LastLatencyMs = result.LatencyMs;
        }

        RaiseAll();
    }

    public void Refresh()
    {
        RaiseAll();
    }

    private void RecomputeStatistics()
    {
        if (_samples.Count == 0)
            return;
        var sorted = _samples.OrderBy(x => x).ToArray();
        Record.MedianLatencyMs = sorted[sorted.Length / 2];
        var median = Record.MedianLatencyMs.Value;
        var deviations = sorted.Select(x => Math.Abs(x - median)).OrderBy(x => x).ToArray();
        var mad = deviations[deviations.Length / 2];
        Record.JitterMs = mad * 1.4826;
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(LatencyText));
        OnPropertyChanged(nameof(MethodText));
        OnPropertyChanged(nameof(ReliabilityText));
        OnPropertyChanged(nameof(Score));
        OnPropertyChanged(nameof(ScoreText));
        OnPropertyChanged(nameof(LastErrorText));
        OnPropertyChanged(nameof(LastCheckedText));
        OnPropertyChanged(nameof(Trust));
        OnPropertyChanged(nameof(WarningsText));
    }
}
