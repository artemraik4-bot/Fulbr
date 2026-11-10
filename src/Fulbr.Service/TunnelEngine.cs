using Fulbr.Core.Engine;
using Fulbr.Core.Models;
using Fulbr.Core.Parsing;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Fulbr.Service;

public sealed class TunnelSession : IAsyncDisposable
{
    public required Process Process { get; init; }
    public required string ConfigPath { get; init; }
    public required string ServerName { get; init; }
    public required string InterfaceName { get; init; }
    public required int DiagnosticPort { get; init; }
    public required string DiagnosticUsername { get; init; }
    public required string DiagnosticPassword { get; init; }
    public required int ClashApiPort { get; init; }
    public required Task<string> StandardError { get; init; }

    public string DiagnosticEndpoint => $"http://127.0.0.1:{DiagnosticPort}";

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await Process.WaitForExitAsync(timeout.Token);
            }
        }
        catch
        {
            // Best-effort.
        }
        finally
        {
            Process.Dispose();
            try { File.Delete(ConfigPath); } catch { }
        }
    }
}

public sealed record TunnelStatus(
    string State,
    string Detail,
    string? Server,
    string? Interface,
    int? DiagnosticPort,
    int? EnginePid);

/// <summary>
/// Owns the privileged TUN engine process. It uses the same pinned sing-box binary
/// and strict config builder as the UI, but only the service can start TUN mode.
/// </summary>
public sealed class TunnelEngine
{
    private const string ExpectedSha256 =
        "8f30682ee198855440d2a7fe070d5d3653793b2d01bb8c85eea88f93bbe7c8b9";

    private readonly ILogger<TunnelEngine> _logger;
    private readonly ShareLinkParser _parser = new();
    private readonly SingBoxConfigBuilder _builder = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TunnelSession? _session;
    private string? _lastError;
    private bool _hashVerified;

    public TunnelEngine(ILogger<TunnelEngine> logger) => _logger = logger;

    public static string EnginePath => Path.Combine(AppContext.BaseDirectory, "engine-service", "sing-box.exe");
    public static string WintunPath => Path.Combine(AppContext.BaseDirectory, "engine-service", "wintun.dll");

    public TunnelStatus GetStatus()
    {
        var session = _session;
        if (session is null)
            return new TunnelStatus("Disconnected", _lastError ?? "No active tunnel.", null, null, null, null);

        return new TunnelStatus(
            session.Process.HasExited ? "Error" : "Connected",
            session.Process.HasExited ? "The engine exited unexpectedly." : "TUN mode is running.",
            session.ServerName,
            session.InterfaceName,
            session.DiagnosticPort,
            session.Process.Id);
    }

    public async Task<TunnelStatus> StartAsync(string rawLink, string? interfaceName,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_session is not null)
                return GetStatus();

            if (!File.Exists(EnginePath))
                return Fail("The sing-box engine is not installed beside the service.");
            if (!File.Exists(WintunPath))
                return Fail("The official Wintun DLL is missing beside the engine.");

            VerifyEngine();

            var parsed = _parser.Parse(rawLink);
            var server = parsed.Servers.FirstOrDefault();
            if (server is null)
            {
                var reason = parsed.Rejected.FirstOrDefault()?.Reason ?? "Malformed link.";
                return Fail($"Configuration rejected: {reason}");
            }

            var name = string.IsNullOrWhiteSpace(interfaceName) ? "Fulbr" : interfaceName.Trim();
            if (name.Length > 32 || name.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
                return Fail("Invalid TUN interface name.");

            var diagnosticsPort = GetFreePort();
            var clashPort = GetFreePort();
            var username = "fulbr-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
            var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(18)).ToLowerInvariant();
            var apiSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(18)).ToLowerInvariant();

            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Fulbr", "logs", "engine.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

            var options = new TunOptions(
                InterfaceName: name,
                DiagnosticPort: diagnosticsPort,
                DiagnosticUsername: username,
                DiagnosticPassword: password,
                ClashApiPort: clashPort,
                ClashApiSecret: apiSecret,
                LogLevel: "debug",
                LogPath: logPath);

            if (!_builder.TryBuildTun(server, options, out var json, out var buildError))
                return Fail(buildError ?? "Could not build the TUN configuration.");

            var runtimeDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Fulbr", "runtime");
            Directory.CreateDirectory(runtimeDir);
            var configPath = Path.Combine(runtimeDir, $"tunnel-{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(configPath, json, cancellationToken);

            try
            {
                var check = await RunCheckAsync(configPath, cancellationToken);
                if (!check.Ok)
                {
                    try { File.Delete(configPath); } catch { }
                    return Fail($"Engine configuration check failed: {check.Error}");
                }

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = EnginePath,
                        Arguments = $"run -c \"{configPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = Path.GetDirectoryName(EnginePath) ?? AppContext.BaseDirectory
                    },
                    EnableRaisingEvents = true
                };

                if (!process.Start())
                {
                    try { File.Delete(configPath); } catch { }
                    return Fail("The engine process could not be started.");
                }

                var stderr = process.StandardError.ReadToEndAsync();
                _ = process.StandardOutput.ReadToEndAsync();

                var ready = await WaitForPortAsync(process, diagnosticsPort, cancellationToken);
                var tunReady = ready && await WaitForInterfaceAsync(name, cancellationToken);
                if (!ready || !tunReady)
                {
                    var exited = process.HasExited;
                    var detail = exited ? await SafeReadAsync(stderr) : "TUN adapter did not appear in time.";
                    try { process.Kill(entireProcessTree: true); } catch { }
                    process.Dispose();
                    try { File.Delete(configPath); } catch { }
                    return Fail(string.IsNullOrWhiteSpace(detail)
                        ? "The engine did not become ready."
                        : detail);
                }

                _session = new TunnelSession
                {
                    Process = process,
                    ConfigPath = configPath,
                    ServerName = server.DisplayName,
                    InterfaceName = name,
                    DiagnosticPort = diagnosticsPort,
                    DiagnosticUsername = username,
                    DiagnosticPassword = password,
                    ClashApiPort = clashPort,
                    StandardError = stderr
                };
                _lastError = null;
                _logger.LogInformation("TUN started: server={Server}, interface={Interface}, pid={Pid}",
                    server.DisplayName, name, process.Id);

                process.Exited += (_, _) =>
                {
                    _logger.LogWarning("TUN engine exited: pid={Pid}, code={Code}",
                        process.Id, SafeExitCode(process));
                };

                return GetStatus();
            }
            catch
            {
                try { File.Delete(configPath); } catch { }
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TUN start failed");
            return Fail($"TUN start failed: {ex.GetType().Name}.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TunnelStatus> StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_session is null)
                return new TunnelStatus("Disconnected", "No active tunnel.", null, null, null, null);

            var session = _session;
            _session = null;
            await session.DisposeAsync();
            _lastError = null;
            _logger.LogInformation("TUN stopped: server={Server}", session.ServerName);
            return new TunnelStatus("Disconnected", "Tunnel stopped.", null, null, null, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TunnelStatus> RecoverAsync(CancellationToken cancellationToken)
    {
        var status = await StopAsync(cancellationToken);
        CleanupStale();
        return new TunnelStatus("Disconnected", "Recovery cleanup completed.", null, null, null, null);
    }

    public void CleanupStale()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("sing-box"))
            {
                try
                {
                    var file = process.MainModule?.FileName;
                    if (file is not null &&
                        string.Equals(Path.GetFullPath(file), Path.GetFullPath(EnginePath),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(3000);
                    }
                }
                catch { }
            }

            var runtimeDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Fulbr", "runtime");
            if (!Directory.Exists(runtimeDir))
                return;
            foreach (var file in Directory.EnumerateFiles(runtimeDir, "tunnel-*.json"))
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stale session cleanup failed");
        }
    }

    private void VerifyEngine()
    {
        if (_hashVerified) return;
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(EnginePath))).ToLowerInvariant();
        if (!hash.Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("sing-box SHA-256 does not match the pinned value.");
        _hashVerified = true;
    }

    private static async Task<(bool Ok, string Error)> RunCheckAsync(string configPath,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = EnginePath,
                Arguments = $"check -c \"{configPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(EnginePath) ?? AppContext.BaseDirectory
            }
        };
        if (!process.Start())
            return (false, "Could not start the engine checker.");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (false, "Engine configuration check timed out.");
        }

        if (process.ExitCode == 0)
            return (true, string.Empty);
        var text = Trim(await SafeReadAsync(stderr));
        if (string.IsNullOrWhiteSpace(text))
            text = Trim(await SafeReadAsync(stdout));
        return (false, string.IsNullOrWhiteSpace(text) ? $"exit code {process.ExitCode}" : text);
    }

    private static async Task<bool> WaitForPortAsync(Process process, int port,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited) return false;
            try
            {
                using var client = new TcpClient();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMilliseconds(300));
                await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
                return true;
            }
            catch
            {
                await Task.Delay(100, cancellationToken);
            }
        }
        return false;
    }

    private static async Task<bool> WaitForInterfaceAsync(string name, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (NetworkInterface.GetAllNetworkInterfaces().Any(n =>
                    n.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    n.Description.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return true;
            await Task.Delay(100, cancellationToken);
        }
        return false;
    }

    private TunnelStatus Fail(string detail)
    {
        _lastError = detail;
        _logger.LogWarning("Tunnel error: {Detail}", detail);
        return new TunnelStatus("Error", detail, null, null, null, null);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<string> SafeReadAsync(Task<string> task)
    {
        try { return await task; }
        catch { return string.Empty; }
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; } catch { return -1; }
    }

    private static string Trim(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        text = text.Trim();
        return text.Length > 1200 ? text[..1200] : text;
    }
}
