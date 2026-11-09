using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Fulbr.Service;

public sealed record ServiceRequest(string Command, string? Link, string? InterfaceName);

public sealed record ServiceResponse(
    bool Ok,
    string State,
    string Detail,
    string? Server,
    string? Interface,
    int? DiagnosticPort,
    int? EnginePid);

/// <summary>
/// Strict, line-delimited JSON command server. There is no arbitrary command execution,
/// no arbitrary file path, and no raw engine config pass-through.
/// </summary>
public sealed class NamedPipeServer
{
    public const string PipeName = "Fulbr.Service";
    private const int MaxLineLength = 32 * 1024;

    private readonly TunnelEngine _engine;
    private readonly ILogger<NamedPipeServer> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public NamedPipeServer(TunnelEngine engine, ILogger<NamedPipeServer> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(cancellationToken);
                await HandleAsync(pipe, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Named-pipe connection failed");
            }
            finally
            {
                if (pipe is not null)
                {
                    try { pipe.Disconnect(); } catch { }
                    await pipe.DisposeAsync();
                }
            }
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(admins, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(users,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            4096,
            4096,
            security);
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        string caller = "local-user (ACL-validated)";
        try
        {
            pipe.RunAsClient(() =>
            {
                caller = WindowsIdentity.GetCurrent().Name;
            });
        }
        catch
        {
            // Some environments do not allow impersonation on an async pipe.
            // The pipe ACL still limits access to local Users/Administrators.
        }

        if (caller.Contains("ANONYMOUS LOGON", StringComparison.OrdinalIgnoreCase))
        {
            await WriteAsync(pipe, new ServiceResponse(false, "Error", "Caller is not authenticated.",
                null, null, null, null), cancellationToken);
            return;
        }

        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
        {
            AutoFlush = true
        };

        var line = await reader.ReadLineAsync(cancellationToken);
        if (line is null || line.Length == 0 || line.Length > MaxLineLength)
        {
            await WriteAsync(writer, new ServiceResponse(false, "Error", "Invalid or oversized request.",
                null, null, null, null), cancellationToken);
            return;
        }

        ServiceRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<ServiceRequest>(line, JsonOptions);
        }
        catch
        {
            request = null;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Command))
        {
            await WriteAsync(writer, new ServiceResponse(false, "Error", "Malformed request.",
                null, null, null, null), cancellationToken);
            return;
        }

        _logger.LogInformation("IPC command={Command} caller={Caller}", request.Command, caller);

        try
        {
            switch (request.Command.Trim().ToLowerInvariant())
            {
                case "ping":
                    await WriteAsync(writer, new ServiceResponse(true, "OK", "pong", null, null, null, null),
                        cancellationToken);
                    return;
                case "status":
                    await WriteStatusAsync(writer, _engine.GetStatus(), cancellationToken);
                    return;
                case "start":
                    if (string.IsNullOrWhiteSpace(request.Link))
                    {
                        await WriteAsync(writer, new ServiceResponse(false, "Error",
                            "A share link is required.", null, null, null, null), cancellationToken);
                        return;
                    }
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(45));
                        var status = await _engine.StartAsync(request.Link, request.InterfaceName, timeout.Token);
                        await WriteStatusAsync(writer, status, cancellationToken);
                    }
                    return;
                case "stop":
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(20));
                        var status = await _engine.StopAsync(timeout.Token);
                        await WriteStatusAsync(writer, status, cancellationToken);
                    }
                    return;
                case "recover":
                    var recovered = await _engine.RecoverAsync(cancellationToken);
                    await WriteStatusAsync(writer, recovered, cancellationToken);
                    return;
                default:
                    await WriteAsync(writer, new ServiceResponse(false, "Error", "Unknown command.",
                        null, null, null, null), cancellationToken);
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IPC command failed: {Command}", request.Command);
            await WriteAsync(writer, new ServiceResponse(false, "Error",
                $"Command failed: {ex.GetType().Name}.", null, null, null, null), cancellationToken);
        }
    }

    private static Task WriteStatusAsync(StreamWriter writer, TunnelStatus status,
        CancellationToken cancellationToken)
        => WriteAsync(writer, new ServiceResponse(
            status.State is "Connected" or "Disconnected",
            status.State,
            status.Detail,
            status.Server,
            status.Interface,
            status.DiagnosticPort,
            status.EnginePid), cancellationToken);

    private static async Task WriteAsync(StreamWriter writer, ServiceResponse response,
        CancellationToken cancellationToken)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions).AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private static async Task WriteAsync(NamedPipeServerStream pipe, ServiceResponse response,
        CancellationToken cancellationToken)
    {
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
        {
            AutoFlush = true
        };
        await WriteAsync(writer, response, cancellationToken);
    }
}
