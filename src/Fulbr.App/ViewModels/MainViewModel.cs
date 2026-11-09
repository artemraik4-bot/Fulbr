using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fulbr.App.Services;
using Fulbr.Core.Diagnostics;
using Fulbr.Core.Models;
using Fulbr.Core.Parsing;
using Fulbr.Core.Ranking;
using Fulbr.Core.Subscriptions;
using Fulbr.Core.Util;

namespace Fulbr.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const string RecommendedSourceName = "Public VLESS (reference)";
    private const string RecommendedSourceUrl =
        "https://raw.githubusercontent.com/igareck/vpn-configs-for-russia/main/BLACK_VLESS_RUS_mobile.txt";

    private readonly AppStateStore _store = new();
    private readonly DpapiProtector _protector = new();
    private readonly ShareLinkParser _parser = new();
    private readonly SubscriptionFetcher _fetcher = new();
    private readonly EndpointProbe _probe = new();
    private readonly ServerRanker _ranker = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly SingBoxEngine _engine = new();
    private EngineSession? _session;
    private bool _initialized;

    [ObservableProperty] private string pageKey = "Dashboard";
    [ObservableProperty] private ServerItem? selectedServer;
    [ObservableProperty] private SourceItem? selectedSource;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string protocolFilter = "All";
    [ObservableProperty] private string statusFilter = "All";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string busyText = string.Empty;
    [ObservableProperty] private string theme = "Dark";
    [ObservableProperty] private string language = "System";
    [ObservableProperty] private bool minimizeToTray = true;
    [ObservableProperty] private string newSourceName = string.Empty;
    [ObservableProperty] private string newSourceUrl = string.Empty;
    [ObservableProperty] private string newManualLink = string.Empty;
    [ObservableProperty] private string publicIp = "—";
    [ObservableProperty] private string importSummary = string.Empty;
    [ObservableProperty] private string connectionState = "Disconnected";
    [ObservableProperty] private string connectionDetail = string.Empty;
    [ObservableProperty] private string proxyEndpoint = string.Empty;
    [ObservableProperty] private string proxyUsername = string.Empty;
    [ObservableProperty] private string proxyPasswordMasked = string.Empty;
    [ObservableProperty] private bool engineAvailable;

    public MainViewModel()
    {
        Loc = new LocalizationService();
        Servers.CollectionChanged += (_, _) => RaiseSummary();
        Sources.CollectionChanged += (_, _) => RaiseSummary();
        Logs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(LogCount));
        RecentProbes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ProbeCount));
    }

    public LocalizationService Loc { get; }
    public ObservableCollection<ServerItem> Servers { get; } = new();
    public ObservableCollection<SourceItem> Sources { get; } = new();
    public ObservableCollection<LogItem> Logs { get; } = new();
    public ObservableCollection<ProbeRow> RecentProbes { get; } = new();
    public ICollectionView? ServersView { get; private set; }

    public string PageTitle => Loc[PageKey + "Title"];
    public string PageSubtitle => Loc[PageKey + "Subtitle"];
    public bool IsNotBusy => !IsBusy;
    public int ReachableCount => Servers.Count(s => s.Status == "Reachable");
    public int LogCount => Logs.Count;
    public int ProbeCount => RecentProbes.Count;
    public string BestScoreText
    {
        get
        {
            if (Servers.Count == 0) return "—";
            return $"{Servers.Max(s => s.Score):0}";
        }
    }

    public string LastCheckText
    {
        get
        {
            var last = Servers.Where(s => s.Record.LastCheckedAt is not null)
                .Select(s => s.Record.LastCheckedAt!.Value)
                .DefaultIfEmpty()
                .Max();
            return last == default ? Loc["NotChecked"] : last.ToLocalTime().ToString("HH:mm:ss");
        }
    }

    public string Version => "0.2.0 (proxy prototype)";
    public string VersionShort => "v0.2.0";
    public string EngineStatus => EngineAvailable ? "sing-box 1.14.4 (proxy mode)" : Loc["NotInstalled"];
    public bool IsConnected => _session is not null;
    public bool IsConnectEnabled => SelectedServer is not null && !IsBusy && EngineAvailable;
    public string ConnectButtonText => IsConnected ? Loc["Disconnect"] : Loc["Connect"];
    public string DnsProtectionStatus => IsConnected ? Loc["ProxyOnlyNotProtected"] : Loc["NotTested"];

    public string ConnectionStateText => ConnectionState switch
    {
        "Connected" => Loc["ProxyConnected"],
        "Connecting" => Loc["ProxyConnecting"],
        "Error" => Loc["ProxyError"],
        _ => Loc["Disconnected"]
    };

    public string SidebarStatusText => ConnectionState switch
    {
        "Connected" => Loc["ConnectedShort"],
        "Connecting" => Loc["ConnectingShort"],
        "Error" => Loc["ErrorShort"],
        _ => Loc["Disconnected"]
    };

    public string SelectedServerName => SelectedServer?.DisplayName ?? Loc["NoServerSelected"];
    public string SelectedServerProtocol => SelectedServer?.ProtocolName ?? "—";
    public string SelectedServerEndpoint => SelectedServer?.Endpoint ?? "—";
    public string SelectedServerTrust => SelectedServer?.Trust ?? "—";
    public string SelectedServerLatency => SelectedServer?.LatencyText ?? "—";
    public string SelectedServerMethod => SelectedServer?.MethodText ?? "—";
    public string SelectedServerStatus => SelectedServer?.Status ?? "—";
    public string SelectedServerWarnings => SelectedServer?.WarningsText ?? Loc["None"];

    public async Task InitializeAsync()
    {
        var state = await _store.LoadAsync();
        Theme = state.Settings.Theme;
        Language = state.Settings.Language;
        MinimizeToTray = state.Settings.MinimizeToTray;
        Loc.Language = Language;
        ThemeManager.Apply(Theme);

        foreach (var source in state.Sources)
        {
            if (!string.IsNullOrWhiteSpace(source.UrlProtected))
            {
                var fullUrl = _protector.Unprotect(source.UrlProtected);
                if (!string.IsNullOrWhiteSpace(fullUrl))
                    source.Url = fullUrl;
            }
            Sources.Add(new SourceItem(source));
        }

        foreach (var dto in state.Servers)
        {
            var raw = _protector.Unprotect(dto.RawProtected);
            if (raw is null)
                AddLog("Warn", $"Could not decrypt the stored credential for '{dto.DisplayName}'.");

            var record = new ServerRecord
            {
                Id = dto.Id,
                Protocol = dto.Protocol,
                Host = dto.Host,
                Port = dto.Port,
                DisplayName = dto.DisplayName,
                Fingerprint = dto.Fingerprint,
                SourceId = dto.SourceId,
                SourceName = dto.SourceName,
                IsFavorite = dto.IsFavorite,
                IsUserCreated = dto.IsUserCreated,
                InsecureTls = dto.InsecureTls,
                TlsEnabled = dto.TlsEnabled,
                Sni = dto.Sni,
                Transport = dto.Transport,
                Flow = dto.Flow,
                Warnings = dto.Warnings,
                Trust = dto.Trust,
                SuccessCount = dto.SuccessCount,
                FailureCount = dto.FailureCount,
                LastLatencyMs = dto.LastLatencyMs,
                MedianLatencyMs = dto.MedianLatencyMs,
                JitterMs = dto.JitterMs,
                LastProbeMethod = dto.LastProbeMethod,
                LastCheckedAt = dto.LastCheckedAt,
                LastError = dto.LastError
            };
            Servers.Add(new ServerItem(record, raw));
        }

        foreach (var log in state.Logs.TakeLast(200))
            Logs.Add(new LogItem(log));

        ServersView = CollectionViewSource.GetDefaultView(Servers);
        ServersView.Filter = FilterServer;
        EngineAvailable = _engine.IsAvailable;
        SingBoxEngine.CleanupStaleSessions();
        ConnectionState = "Disconnected";
        _initialized = true;

        AddLog("Info", $"Fulbr {Version} started. Engine: {EngineStatus}.");
        AddLog("Info", EngineAvailable
            ? "Proxy mode is available. TUN, kill switch, and split tunneling are not implemented."
            : "sing-box engine not found; proxy mode is unavailable.");
        if (Sources.Count == 0)
            AddLog("Info", "No subscription sources configured. Add one on the Subscriptions page.");
        RaiseSummary();

        // Migrate any older plaintext source URL to DPAPI-protected storage.
        await SaveStateAsync();
    }

    partial void OnPageKeyChanged(string value)
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
        OnPropertyChanged(nameof(IsConnectEnabled));
    }

    partial void OnConnectionStateChanged(string value)
    {
        OnPropertyChanged(nameof(ConnectionStateText));
        OnPropertyChanged(nameof(SidebarStatusText));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(DnsProtectionStatus));
    }

    partial void OnEngineAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(EngineStatus));
        OnPropertyChanged(nameof(IsConnectEnabled));
    }

    partial void OnSearchTextChanged(string value) => ServersView?.Refresh();
    partial void OnProtocolFilterChanged(string value) => ServersView?.Refresh();
    partial void OnStatusFilterChanged(string value) => ServersView?.Refresh();

    partial void OnThemeChanged(string value)
    {
        if (!_initialized) return;
        ThemeManager.Apply(value);
        _ = SaveStateAsync();
    }

    partial void OnLanguageChanged(string value)
    {
        Loc.Language = value;
        OnPropertyChanged(nameof(Loc));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
        OnPropertyChanged(nameof(ConnectionStateText));
        OnPropertyChanged(nameof(SidebarStatusText));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(DnsProtectionStatus));
        if (_initialized) _ = SaveStateAsync();
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        if (_initialized) _ = SaveStateAsync();
    }

    partial void OnSelectedServerChanged(ServerItem? value)
    {
        OnPropertyChanged(nameof(SelectedServerName));
        OnPropertyChanged(nameof(SelectedServerProtocol));
        OnPropertyChanged(nameof(SelectedServerEndpoint));
        OnPropertyChanged(nameof(SelectedServerTrust));
        OnPropertyChanged(nameof(SelectedServerLatency));
        OnPropertyChanged(nameof(SelectedServerMethod));
        OnPropertyChanged(nameof(SelectedServerStatus));
        OnPropertyChanged(nameof(SelectedServerWarnings));
        OnPropertyChanged(nameof(IsConnectEnabled));
    }

    [RelayCommand]
    private void Navigate(string? page)
    {
        PageKey = string.IsNullOrWhiteSpace(page) ? "Dashboard" : page;
    }

    [RelayCommand]
    private async Task ToggleConnectionAsync()
    {
        if (IsConnected)
            await DisconnectProxyAsync();
        else
            await ConnectProxyAsync();
    }

    private async Task ConnectProxyAsync()
    {
        if (IsBusy)
            return;

        if (SelectedServer is null)
        {
            AddLog("Warn", Loc["NoSelection"]);
            return;
        }

        await ConnectProxyCoreAsync(SelectedServer);
    }

    [RelayCommand]
    private async Task ConnectBestAsync()
    {
        if (IsBusy)
            return;

        if (IsConnected)
            await DisconnectProxyAsync();

        if (Servers.Count == 0)
        {
            AddLog("Warn", Loc["NoServers"]);
            return;
        }

        var candidates = _ranker
            .Rank(Servers.Select(s => s.Record), RankingPreset.Balanced)
            .Select(r => Servers.FirstOrDefault(s => s.Id == r.Server.Id))
            .Where(s => s is not null)
            .Cast<ServerItem>()
            .Take(5)
            .ToList();

        foreach (var candidate in candidates)
        {
            if (IsBusy)
                return;
            SelectedServer = candidate;
            AddLog("Info", $"Trying proxy candidate: {candidate.DisplayName}.");
            if (await ConnectProxyCoreAsync(candidate))
                return;
            AddLog("Warn", $"Candidate {candidate.DisplayName} failed; trying the next one.");
        }

        ConnectionState = "Error";
        ConnectionDetail = "No candidate server passed the proxy test.";
        AddLog("Error", ConnectionDetail);
    }

    private async Task<bool> ConnectProxyCoreAsync(ServerItem server)
    {
        if (IsBusy)
            return false;

        if (!EngineAvailable)
        {
            ConnectionState = "Error";
            ConnectionDetail = Loc["EngineNotInstalled"];
            AddLog("Error", ConnectionDetail);
            return false;
        }

        IsBusy = true;
        BusyText = Loc["ProxyConnecting"];
        ConnectionState = "Connecting";
        ConnectionDetail = Loc["ProxyConnecting"];

        EngineSession? session = null;
        try
        {
            session = await _engine.StartAsync(server.Candidate);
            var test = await ProxyTester.TestAsync(session);
            if (!test.Ok)
            {
                await session.DisposeAsync();
                session = null;
                ConnectionState = "Error";
                ConnectionDetail = test.Detail;
                AddLog("Error", $"Proxy test failed for {server.DisplayName}: {test.Detail}");
                return false;
            }

            _session = session;
            ProxyEndpoint = session.ProxyUri;
            ProxyUsername = session.Username;
            ProxyPasswordMasked = "••••••••";
            ConnectionState = "Connected";
            ConnectionDetail = Loc["ProxyConnectedDetail"];
            AddLog("Info",
                $"Proxy mode connected via {server.DisplayName} at {session.ProxyUri}. " +
                "No TUN, no kill switch.");

            session.Process.Exited += (_, _) => Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                if (ReferenceEquals(_session, session) && !IsBusy)
                {
                    _session = null;
                    ConnectionState = "Error";
                    ConnectionDetail = "The engine exited unexpectedly.";
                    ProxyEndpoint = string.Empty;
                    AddLog("Error", "sing-box exited unexpectedly; proxy mode is down.");
                }
            });
            return true;
        }
        catch (Exception ex)
        {
            if (session is not null)
                await session.DisposeAsync();
            ConnectionState = "Error";
            ConnectionDetail = Redaction.RedactText(ex.Message);
            AddLog("Error", $"Proxy connection failed: {ex.GetType().Name}: {ConnectionDetail}");
            return false;
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
            await SaveStateAsync();
        }
    }

    private async Task DisconnectProxyAsync()
    {
        if (_session is null)
        {
            ConnectionState = "Disconnected";
            ConnectionDetail = string.Empty;
            ProxyEndpoint = string.Empty;
            return;
        }

        if (IsBusy)
            return;

        IsBusy = true;
        BusyText = Loc["Disconnect"];
        try
        {
            var session = _session;
            _session = null;
            await session.DisposeAsync();
            ConnectionState = "Disconnected";
            ConnectionDetail = string.Empty;
            ProxyEndpoint = string.Empty;
            ProxyUsername = string.Empty;
            ProxyPasswordMasked = string.Empty;
            AddLog("Info", "Proxy mode disconnected.");
        }
        catch (Exception ex)
        {
            AddLog("Warn", $"Proxy disconnect: {ex.GetType().Name}.");
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
            await SaveStateAsync();
        }
    }

    [RelayCommand]
    private void CopyProxyUrl()
    {
        if (_session is null)
            return;
        var url = $"http://{_session.Username}:{_session.Password}@127.0.0.1:{_session.Port}";
        Clipboard.SetText(url);
        AddLog("Info", "Proxy credentials copied to the clipboard.");
    }

    public async Task ShutdownAsync()
    {
        if (_session is null)
            return;
        var session = _session;
        _session = null;
        await session.DisposeAsync();
    }

    [RelayCommand]
    private async Task AddSourceAsync()
    {
        var name = NewSourceName.Trim();
        var url = NewSourceUrl.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            AddLog("Error", "Subscription URL must be a valid HTTPS URL.");
            return;
        }

        var source = new SourceItem(new SourceDto
        {
            Name = string.IsNullOrWhiteSpace(name) ? uri.Host : name,
            Url = uri.ToString()
        });
        Sources.Insert(0, source);
        NewSourceName = string.Empty;
        NewSourceUrl = string.Empty;
        AddLog("Info", $"Added source {Redaction.RedactUrl(source.Url)}.");
        await SaveStateAsync();
        await RefreshSourceCoreAsync(source);
    }

    [RelayCommand]
    private async Task AddRecommendedSourceAsync()
    {
        if (Sources.Any(s => string.Equals(s.Url, RecommendedSourceUrl, StringComparison.OrdinalIgnoreCase)))
        {
            AddLog("Info", "The recommended source is already configured.");
            return;
        }

        var source = new SourceItem(new SourceDto
        {
            Name = RecommendedSourceName,
            Url = RecommendedSourceUrl
        });
        Sources.Insert(0, source);
        AddLog("Warn", Loc["RecommendedSourceWarning"]);
        await SaveStateAsync();
        await RefreshSourceCoreAsync(source);
    }

    [RelayCommand]
    private async Task RefreshSourceAsync()
    {
        if (SelectedSource is null)
        {
            AddLog("Warn", Loc["NoSelection"]);
            return;
        }
        await RefreshSourceCoreAsync(SelectedSource);
    }

    [RelayCommand]
    private async Task RefreshAllSourcesAsync()
    {
        if (Sources.Count == 0)
        {
            AddLog("Warn", Loc["NoSources"]);
            return;
        }
        foreach (var source in Sources.ToList())
            await RefreshSourceCoreAsync(source);
    }

    [RelayCommand]
    private async Task RemoveSourceAsync()
    {
        if (SelectedSource is null)
        {
            AddLog("Warn", Loc["NoSelection"]);
            return;
        }
        if (MessageBox.Show(Loc["ConfirmRemove"], "Fulbr", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var removed = SelectedSource;
        Sources.Remove(removed);
        AddLog("Info", $"Removed source {removed.HostDisplay}.");
        await SaveStateAsync();
    }

    private async Task RefreshSourceCoreAsync(SourceItem source)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        BusyText = $"{Loc["Refresh"]}: {source.Name}";
        try
        {
            var result = await _fetcher.FetchAsync(
                new Uri(source.Url), source.Dto.ETag, source.Dto.LastModified);
            source.Dto.LastFetch = DateTimeOffset.Now;

            if (!result.Success)
            {
                source.Dto.LastError = result.Error;
                source.Refresh();
                AddLog("Error", $"Refresh failed for {source.HostDisplay}: {result.Error}");
                await SaveStateAsync();
                return;
            }

            if (result.NotModified)
            {
                source.Dto.LastError = null;
                source.Dto.LastSuccess = DateTimeOffset.Now;
                source.Refresh();
                AddLog("Info", $"Source unchanged (HTTP 304): {source.HostDisplay}.");
                await SaveStateAsync();
                return;
            }

            var parsed = _parser.Parse(result.Content);
            var (added, updated) = MergeServers(parsed.Servers, source);
            source.Dto.ETag = result.ETag;
            source.Dto.LastModified = result.LastModified;
            source.Dto.LastSuccess = DateTimeOffset.Now;
            source.Dto.LastError = null;
            source.Dto.ServerCount = Servers.Count(s => s.Record.SourceId == source.Id);
            source.Refresh();

            ImportSummary = $"{parsed.Servers.Count} accepted, {parsed.Rejected.Count} rejected, " +
                            $"{added} new, {updated} updated";
            AddLog("Info",
                $"Imported {source.HostDisplay}: {added} new, {updated} updated, " +
                $"{parsed.Rejected.Count} rejected.");

            foreach (var rejected in parsed.Rejected.Take(5))
                AddLog("Warn", $"Rejected: {rejected.Reason} ({rejected.RedactedPreview})");

            ServersView?.Refresh();
            RaiseSummary();
            await SaveStateAsync();
        }
        catch (Exception ex)
        {
            source.Dto.LastError = ex.GetType().Name;
            source.Refresh();
            AddLog("Error", $"Refresh error for {source.HostDisplay}: {ex.GetType().Name}.");
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    private (int Added, int Updated) MergeServers(
        IReadOnlyList<ServerCandidate> candidates, SourceItem source)
    {
        var existing = Servers.ToDictionary(s => s.Record.Fingerprint, StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var updated = 0;

        foreach (var candidate in candidates)
        {
            if (existing.TryGetValue(candidate.Fingerprint, out var item))
            {
                item.Record.SourceName ??= source.Name;
                item.Refresh();
                updated++;
                continue;
            }

            var record = new ServerRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Protocol = candidate.Protocol,
                Host = candidate.Host,
                Port = candidate.Port,
                DisplayName = candidate.DisplayName,
                Fingerprint = candidate.Fingerprint,
                SourceId = source.Id,
                SourceName = source.Name,
                IsUserCreated = false,
                InsecureTls = candidate.InsecureTls,
                TlsEnabled = candidate.TlsEnabled,
                Sni = candidate.Sni,
                Transport = candidate.Transport,
                Flow = candidate.Flow,
                Warnings = candidate.Warnings,
                Trust = candidate.InsecureTls ? "Insecure" : "Unknown"
            };
            var newItem = new ServerItem(record, candidate.RawLink);
            Servers.Add(newItem);
            existing[candidate.Fingerprint] = newItem;
            added++;
        }

        return (added, updated);
    }

    [RelayCommand]
    private async Task AddManualLinkAsync()
    {
        var link = NewManualLink.Trim();
        if (string.IsNullOrWhiteSpace(link))
        {
            AddLog("Warn", "Paste a share link first.");
            return;
        }

        var parsed = _parser.Parse(link);
        var candidate = parsed.Servers.FirstOrDefault();
        if (candidate is null)
        {
            var reason = parsed.Rejected.FirstOrDefault()?.Reason ?? "Unsupported or malformed link.";
            AddLog("Error", $"Manual link rejected: {reason}");
            return;
        }

        if (Servers.Any(s => string.Equals(s.Record.Fingerprint, candidate.Fingerprint,
                StringComparison.OrdinalIgnoreCase)))
        {
            AddLog("Info", "That server is already in the list.");
            return;
        }

        var record = new ServerRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Protocol = candidate.Protocol,
            Host = candidate.Host,
            Port = candidate.Port,
            DisplayName = candidate.DisplayName,
            Fingerprint = candidate.Fingerprint,
            SourceId = null,
            SourceName = "Manual",
            IsUserCreated = true,
            InsecureTls = candidate.InsecureTls,
            TlsEnabled = candidate.TlsEnabled,
            Sni = candidate.Sni,
            Transport = candidate.Transport,
            Flow = candidate.Flow,
            Warnings = candidate.Warnings,
            Trust = candidate.InsecureTls ? "Insecure" : "Unknown"
        };
        Servers.Add(new ServerItem(record, candidate.RawLink));
        NewManualLink = string.Empty;
        ServersView?.Refresh();
        AddLog("Info", $"Added manual server {record.DisplayName}.");
        await SaveStateAsync();
    }

    [RelayCommand]
    private async Task PingAllAsync()
    {
        if (Servers.Count == 0)
        {
            AddLog("Warn", Loc["NoServers"]);
            return;
        }
        if (IsBusy)
            return;

        IsBusy = true;
        BusyText = $"{Loc["Busy"]} {Servers.Count}";
        var targets = Servers.ToList();
        var semaphore = new SemaphoreSlim(4);
        var successes = 0;
        var failures = 0;

        try
        {
            var tasks = targets.Select(async item =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var result = await _probe.ProbeAsync(item.Candidate, TimeSpan.FromSeconds(6));
                    item.ApplyPing(result);
                    RecentProbes.Insert(0, new ProbeRow
                    {
                        Timestamp = result.CheckedAt,
                        ServerName = item.DisplayName,
                        Method = result.Method,
                        Result = result.Supported
                            ? (result.Success ? "OK" : result.Error ?? "Failed")
                            : "Not supported without engine",
                        LatencyMs = result.LatencyMs
                    });
                    if (result.Success) Interlocked.Increment(ref successes);
                    else Interlocked.Increment(ref failures);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            while (RecentProbes.Count > 100)
                RecentProbes.RemoveAt(RecentProbes.Count - 1);

            AddLog("Info", $"Reachability checks: {successes} reachable, {failures} failed/unavailable.");
            RaiseSummary();
            await SaveStateAsync();
        }
        finally
        {
            semaphore.Dispose();
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task PingSelectedAsync()
    {
        if (SelectedServer is null)
        {
            AddLog("Warn", Loc["NoSelection"]);
            return;
        }
        await PingOneAsync(SelectedServer);
    }

    [RelayCommand]
    private async Task PingServerAsync(ServerItem? item)
    {
        if (item is null) return;
        await PingOneAsync(item);
    }

    private async Task PingOneAsync(ServerItem item)
    {
        if (IsBusy) return;
        IsBusy = true;
        BusyText = $"{Loc["Ping"]}: {item.DisplayName}";
        try
        {
            var result = await _probe.ProbeAsync(item.Candidate, TimeSpan.FromSeconds(8));
            item.ApplyPing(result);
            RecentProbes.Insert(0, new ProbeRow
            {
                Timestamp = result.CheckedAt,
                ServerName = item.DisplayName,
                Method = result.Method,
                Result = result.Supported
                    ? (result.Success ? "OK" : result.Error ?? "Failed")
                    : "Not supported without engine",
                LatencyMs = result.LatencyMs
            });
            AddLog("Info", result.Supported
                ? $"{item.DisplayName}: {result.Method} {(result.Success ? "OK" : "failed")} " +
                  $"({result.LatencyMs:0} ms)."
                : $"{item.DisplayName}: {result.Error}");
            RaiseSummary();
            await SaveStateAsync();
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    [RelayCommand]
    private void BestServer()
    {
        if (Servers.Count == 0)
        {
            AddLog("Warn", Loc["NoServers"]);
            return;
        }
        var best = _ranker.Rank(Servers.Select(s => s.Record), RankingPreset.Balanced).FirstOrDefault();
        if (best is null)
            return;
        var item = Servers.FirstOrDefault(s => s.Id == best.Server.Id);
        SelectedServer = item;
        PageKey = "Servers";
        AddLog("Info", $"Best server by current data: {best.Server.DisplayName} (score {best.Score:0}).");
    }

    [RelayCommand]
    private void ToggleFavorite(ServerItem? item)
    {
        if (item is null) return;
        item.IsFavorite = !item.IsFavorite;
        _ = SaveStateAsync();
    }

    [RelayCommand]
    private async Task CheckPublicIpAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        BusyText = Loc["CheckPublicIp"];
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Fulbr/0.1");
            var json = await http.GetStringAsync("https://api.ipify.org?format=json");
            using var doc = JsonDocument.Parse(json);
            var ip = doc.RootElement.TryGetProperty("ip", out var value) ? value.GetString() : null;
            PublicIp = string.IsNullOrWhiteSpace(ip) ? "Unknown" : ip;
            AddLog("Info", "Direct public IP check succeeded (via api.ipify.org).");
        }
        catch (Exception ex)
        {
            PublicIp = "Check failed";
            AddLog("Error", $"Public IP check failed: {ex.GetType().Name}.");
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task ClearLogsAsync()
    {
        Logs.Clear();
        await SaveStateAsync();
    }

    [RelayCommand]
    private async Task ClearAllDataAsync()
    {
        if (MessageBox.Show(Loc["ClearDataConfirm"], "Fulbr", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await DisconnectProxyAsync();
        Servers.Clear();
        Sources.Clear();
        SelectedServer = null;
        SelectedSource = null;
        ServersView?.Refresh();
        AddLog("Warn", Loc["ClearDataDone"]);
        RaiseSummary();
        await SaveStateAsync();
    }

    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Fulbr", "logs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"fulbr-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        var lines = Logs.Select(l => $"{l.Dto.Timestamp:O}  {l.Level,-5}  {Redaction.RedactText(l.Message)}");
        await File.WriteAllLinesAsync(path, lines);
        AddLog("Info", $"Exported {Logs.Count} log entries to {path}.");
    }

    [RelayCommand]
    private void OpenDocs()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "docs"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs"))
        };
        var path = candidates.FirstOrDefault(Directory.Exists);
        OpenPath(path ?? AppContext.BaseDirectory);
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fulbr");
        Directory.CreateDirectory(path);
        OpenPath(path);
    }

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch
        {
            // Opening a folder is best-effort.
        }
    }

    private bool FilterServer(object obj)
    {
        if (obj is not ServerItem server)
            return false;

        var query = SearchText.Trim();
        if (query.Length > 0 &&
            !server.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) &&
            !server.Host.Contains(query, StringComparison.OrdinalIgnoreCase) &&
            !server.ProtocolName.Contains(query, StringComparison.OrdinalIgnoreCase) &&
            !server.SourceDisplay.Contains(query, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!ProtocolFilter.Equals("All", StringComparison.OrdinalIgnoreCase) &&
            !server.ProtocolName.Equals(ProtocolFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!StatusFilter.Equals("All", StringComparison.OrdinalIgnoreCase) &&
            !server.Status.Equals(StatusFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private void RaiseSummary()
    {
        OnPropertyChanged(nameof(ReachableCount));
        OnPropertyChanged(nameof(BestScoreText));
        OnPropertyChanged(nameof(LastCheckText));
        OnPropertyChanged(nameof(LogCount));
        OnPropertyChanged(nameof(ProbeCount));
        OnPropertyChanged(nameof(SelectedServerName));
        OnPropertyChanged(nameof(SelectedServerProtocol));
        OnPropertyChanged(nameof(SelectedServerEndpoint));
        OnPropertyChanged(nameof(SelectedServerTrust));
        OnPropertyChanged(nameof(SelectedServerLatency));
        OnPropertyChanged(nameof(SelectedServerMethod));
        OnPropertyChanged(nameof(SelectedServerStatus));
        OnPropertyChanged(nameof(SelectedServerWarnings));
    }

    private void AddLog(string level, string message)
    {
        Logs.Insert(0, new LogItem(new LogDto
        {
            Timestamp = DateTimeOffset.Now,
            Level = level,
            Message = Redaction.RedactText(message)
        }));
        while (Logs.Count > 300)
            Logs.RemoveAt(Logs.Count - 1);
    }

    private async Task SaveStateAsync()
    {
        if (!_initialized) return;
        await _saveLock.WaitAsync();
        try
        {
            var state = new AppState
            {
                Settings = new SettingsDto
                {
                    Theme = Theme,
                    Language = Language,
                    MinimizeToTray = MinimizeToTray
                },
                Sources = Sources.Select(s => new SourceDto
                {
                    Id = s.Dto.Id,
                    Name = s.Dto.Name,
                    // Never persist the full URL in plaintext: it can contain tokens.
                    Url = Redaction.RedactUrl(s.Url),
                    UrlProtected = string.IsNullOrWhiteSpace(s.Url) ? null : _protector.Protect(s.Url),
                    Enabled = s.Dto.Enabled,
                    ETag = s.Dto.ETag,
                    LastModified = s.Dto.LastModified,
                    LastFetch = s.Dto.LastFetch,
                    LastSuccess = s.Dto.LastSuccess,
                    LastError = s.Dto.LastError,
                    ServerCount = s.Dto.ServerCount
                }).ToList(),
                Servers = Servers.Select(s => new ServerDto
                {
                    Id = s.Record.Id,
                    Protocol = s.Record.Protocol,
                    Host = s.Record.Host,
                    Port = s.Record.Port,
                    DisplayName = s.Record.DisplayName,
                    Fingerprint = s.Record.Fingerprint,
                    SourceId = s.Record.SourceId,
                    SourceName = s.Record.SourceName,
                    IsFavorite = s.Record.IsFavorite,
                    IsUserCreated = s.Record.IsUserCreated,
                    InsecureTls = s.Record.InsecureTls,
                    TlsEnabled = s.Record.TlsEnabled,
                    Sni = s.Record.Sni,
                    Transport = s.Record.Transport,
                    Flow = s.Record.Flow,
                    Warnings = s.Record.Warnings.ToList(),
                    Trust = s.Record.Trust,
                    RawProtected = string.IsNullOrEmpty(s.RawLink) ? null : _protector.Protect(s.RawLink),
                    SuccessCount = s.Record.SuccessCount,
                    FailureCount = s.Record.FailureCount,
                    LastLatencyMs = s.Record.LastLatencyMs,
                    MedianLatencyMs = s.Record.MedianLatencyMs,
                    JitterMs = s.Record.JitterMs,
                    LastProbeMethod = s.Record.LastProbeMethod,
                    LastCheckedAt = s.Record.LastCheckedAt,
                    LastError = s.Record.LastError
                }).ToList(),
                Logs = Logs.Take(200).Select(l => l.Dto).ToList()
            };
            await _store.SaveAsync(state);
        }
        catch
        {
            // Saving must not crash the UI. A failed save is surfaced through the log next time.
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
