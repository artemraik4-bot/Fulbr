using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Fulbr.Core.Engine;
using Fulbr.Core.Models;

namespace Fulbr.App.Services;

public sealed class EngineSession : IAsyncDisposable
{
    public required Process Process { get; init; }
    public required int Port { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required string ConfigPath { get; init; }
    public required Task<string> StandardError { get; init; }

    public string ProxyUri => $"http://127.0.0.1:{Port}";

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                await Process.WaitForExitAsync(timeout.Token);
            }
        }
        catch
        {
            // Best-effort shutdown.
        }
        finally
        {
            Process.Dispose();
            try
            {
                if (File.Exists(ConfigPath))
                    File.Delete(ConfigPath);
            }
            catch
            {
                // The runtime directory is cleaned on next start.
            }
        }
    }
}

/// <summary>
/// Starts the pinned sing-box binary as an isolated child process in proxy mode.
/// No TUN, no routes, no service, no system proxy changes.
/// </summary>
public sealed class SingBoxEngine
{
    public const string ExpectedSha256 =
        "8f30682ee198855440d2a7fe070d5d3653793b2d01bb8c85eea88f93bbe7c8b9";

    private readonly SingBoxConfigBuilder _builder = new();
    private bool _hashVerified;
    private string? _verifiedPath;

    public static string EnginePath => FindEnginePath();

    public bool IsAvailable => File.Exists(EnginePath);

    public async Task<EngineSession> StartAsync(ServerCandidate server, CancellationToken cancellationToken = default)
    {
        var engine = EnginePath;
        if (!File.Exists(engine))
            throw new FileNotFoundException("sing-box engine was not found next to Fulbr.");

        VerifyEngine(engine);

        var port = GetFreeLoopbackPort();
        var username = "fulbr-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(18)).ToLowerInvariant();

        if (!_builder.TryBuild(server, port, username, password, out var configJson, out var error))
            throw new InvalidOperationException(error ?? "Could not build the engine configuration.");

        var runtimeDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Fulbr", "runtime");
        Directory.CreateDirectory(runtimeDir);
        var configPath = Path.Combine(runtimeDir, $"session-{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllTextAsync(configPath, configJson, cancellationToken);

            var check = await RunCheckAsync(engine, configPath, cancellationToken);
            if (!check.Ok)
                throw new InvalidOperationException($"Engine configuration check failed: {check.Error}");

            var psi = new ProcessStartInfo
            {
                FileName = engine,
                Arguments = $"run -c \"{configPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(engine) ?? AppContext.BaseDirectory
            };

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (!process.Start())
                throw new InvalidOperationException("The engine process could not be started.");

            var stderrTask = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEndAsync();

            var ready = await WaitForPortAsync(process, port, cancellationToken);
            if (!ready)
            {
                var stderr = process.HasExited
                    ? await SafeReadAsync(stderrTask)
                    : "Engine did not open the local proxy port in time.";
                try { process.Kill(entireProcessTree: true); } catch { }
                process.Dispose();
                throw new InvalidOperationException(Trim(stderr));
            }

            return new EngineSession
            {
                Process = process,
                Port = port,
                Username = username,
                Password = password,
                ConfigPath = configPath,
                StandardError = stderrTask
            };
        }
        catch
        {
            try { File.Delete(configPath); } catch { }
            throw;
        }
    }

    private static async Task<(bool Ok, string Error)> RunCheckAsync(
        string engine, string configPath, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = engine,
                Arguments = $"check -c \"{configPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(engine) ?? AppContext.BaseDirectory
            }
        };

        if (!process.Start())
            return (false, "Could not start the engine checker.");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
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

    private static async Task<bool> WaitForPortAsync(Process process, int port, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
                return false;

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

    private void VerifyEngine(string path)
    {
        if (_hashVerified && string.Equals(_verifiedPath, path, StringComparison.OrdinalIgnoreCase))
            return;

        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        if (!hash.Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The installed sing-box binary does not match the pinned SHA-256. Refusing to run it.");

        _hashVerified = true;
        _verifiedPath = path;
    }

    public static void CleanupStaleSessions()
    {
        try
        {
            // Kill orphaned engines launched from our pinned binary only.
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
                catch
                {
                    // Another user's process or access denied; leave it alone.
                }
            }

            var runtimeDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Fulbr", "runtime");
            if (!Directory.Exists(runtimeDir))
                return;

            // No session is active yet at startup, so every session file is stale.
            foreach (var file in Directory.EnumerateFiles(runtimeDir, "session-*.json"))
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch
        {
            // Cleanup is best-effort.
        }
    }

    private static int GetFreeLoopbackPort()
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

    private static string Trim(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        text = text.Trim();
        return text.Length > 1200 ? text[..1200] : text;
    }

    private static string FindEnginePath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "engine", "sing-box.exe"),
            Path.Combine(AppContext.BaseDirectory, "sing-box.exe"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "vendor", "sing-box", "sing-box.exe"))
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
