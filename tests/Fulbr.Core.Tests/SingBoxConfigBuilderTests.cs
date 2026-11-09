using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Fulbr.Core.Engine;
using Fulbr.Core.Models;
using Fulbr.Core.Parsing;
using Xunit;

namespace Fulbr.Core.Tests;

public sealed class SingBoxConfigBuilderTests
{
    private readonly ShareLinkParser _parser = new();
    private readonly SingBoxConfigBuilder _builder = new();

    private ServerCandidate ParseOne(string link)
    {
        var result = _parser.Parse(link);
        return Assert.Single(result.Servers);
    }

    [Fact]
    public void BuildsVlessRealityConfig()
    {
        var server = ParseOne(
            "vless://11111111-1111-1111-1111-111111111111@example.com:443" +
            "?security=reality&sni=www.example.com&fp=chrome&pbk=abc&sid=12&type=tcp" +
            "&flow=xtls-rprx-vision#Reality");

        var ok = _builder.TryBuild(server, 10808, "user", "pass", out var json, out var error);
        Assert.True(ok, error);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(10808, root.GetProperty("inbounds")[0].GetProperty("listen_port").GetInt32());
        Assert.Equal("user", root.GetProperty("inbounds")[0].GetProperty("users")[0].GetProperty("username").GetString());

        var proxy = root.GetProperty("outbounds")[0];
        Assert.Equal("vless", proxy.GetProperty("type").GetString());
        Assert.Equal("11111111-1111-1111-1111-111111111111", proxy.GetProperty("uuid").GetString());
        Assert.Equal("xtls-rprx-vision", proxy.GetProperty("flow").GetString());
        Assert.True(proxy.GetProperty("tls").GetProperty("reality").GetProperty("enabled").GetBoolean());
        Assert.Equal("abc", proxy.GetProperty("tls").GetProperty("reality").GetProperty("public_key").GetString());
        Assert.Equal("www.example.com", proxy.GetProperty("tls").GetProperty("server_name").GetString());
        Assert.False(proxy.GetProperty("tls").GetProperty("insecure").GetBoolean());
    }

    [Fact]
    public void BuildsTrojanConfigWithTls()
    {
        var server = ParseOne("trojan://secret@example.com:443?security=tls&sni=example.com#T");
        var ok = _builder.TryBuild(server, 10809, "u", "p", out var json, out var error);
        Assert.True(ok, error);

        using var doc = JsonDocument.Parse(json);
        var proxy = doc.RootElement.GetProperty("outbounds")[0];
        Assert.Equal("trojan", proxy.GetProperty("type").GetString());
        Assert.Equal("secret", proxy.GetProperty("password").GetString());
        Assert.True(proxy.GetProperty("tls").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void BuildsShadowsocksConfig()
    {
        var user = Convert.ToBase64String(Encoding.UTF8.GetBytes("aes-256-gcm:secret"));
        var server = ParseOne($"ss://{user}@ss.example.com:8388#SS");
        var ok = _builder.TryBuild(server, 10810, "u", "p", out var json, out var error);
        Assert.True(ok, error);

        using var doc = JsonDocument.Parse(json);
        var proxy = doc.RootElement.GetProperty("outbounds")[0];
        Assert.Equal("shadowsocks", proxy.GetProperty("type").GetString());
        Assert.Equal("aes-256-gcm", proxy.GetProperty("method").GetString());
        Assert.Equal("secret", proxy.GetProperty("password").GetString());
    }

    [Fact]
    public void BuildsVmessWebSocketConfig()
    {
        const string vmessJson =
            "{\"v\":\"2\",\"ps\":\"V\",\"add\":\"vm.example.com\",\"port\":\"443\"," +
            "\"id\":\"11111111-1111-1111-1111-111111111111\",\"aid\":\"0\",\"scy\":\"auto\"," +
            "\"net\":\"ws\",\"type\":\"none\",\"host\":\"vm.example.com\",\"path\":\"/ws\"," +
            "\"tls\":\"tls\",\"sni\":\"vm.example.com\"}";
        var server = ParseOne("vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(vmessJson)));

        var ok = _builder.TryBuild(server, 10811, "u", "p", out var json, out var error);
        Assert.True(ok, error);

        using var doc = JsonDocument.Parse(json);
        var proxy = doc.RootElement.GetProperty("outbounds")[0];
        Assert.Equal("vmess", proxy.GetProperty("type").GetString());
        Assert.Equal("ws", proxy.GetProperty("transport").GetProperty("type").GetString());
        Assert.True(proxy.GetProperty("tls").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void RejectsInsecureTls()
    {
        var server = ParseOne("trojan://secret@example.com:443?security=tls&allowInsecure=1#T");
        var ok = _builder.TryBuild(server, 10808, "u", "p", out _, out var error);
        Assert.False(ok);
        Assert.Contains("TLS verification", error);
    }

    [Fact]
    public void RejectsUdpProtocolInProxyMode()
    {
        var server = ParseOne("hysteria2://pass@example.com:443?sni=example.com#H");
        var ok = _builder.TryBuild(server, 10808, "u", "p", out _, out var error);
        Assert.False(ok);
        Assert.Contains("not supported", error);
    }

    [Fact]
    public void RejectsVlessWithoutTls()
    {
        var server = ParseOne(
            "vless://11111111-1111-1111-1111-111111111111@example.com:80?security=none#Plain");
        var ok = _builder.TryBuild(server, 10808, "u", "p", out _, out var error);
        Assert.False(ok);
        Assert.Contains("TLS", error);
    }

    [Fact]
    public void BuildsTunConfig()
    {
        var server = ParseOne(
            "vless://11111111-1111-1111-1111-111111111111@example.com:443" +
            "?security=reality&sni=www.example.com&fp=chrome&pbk=abc&sid=12&type=tcp#Tun");

        var options = new TunOptions(
            InterfaceName: "FulbrTest",
            DiagnosticPort: 20808,
            DiagnosticUsername: "diag",
            DiagnosticPassword: "secret",
            ClashApiPort: 29090,
            ClashApiSecret: "api-secret");

        var ok = _builder.TryBuildTun(server, options, out var json, out var error);
        Assert.True(ok, error);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tun = root.GetProperty("inbounds")[0];
        Assert.Equal("tun", tun.GetProperty("type").GetString());
        Assert.Equal("FulbrTest", tun.GetProperty("interface_name").GetString());
        Assert.True(tun.GetProperty("auto_route").GetBoolean());
        Assert.True(tun.GetProperty("strict_route").GetBoolean());

        var diag = root.GetProperty("inbounds")[1];
        Assert.Equal("mixed", diag.GetProperty("type").GetString());
        Assert.Equal(20808, diag.GetProperty("listen_port").GetInt32());
        Assert.Equal("diag", diag.GetProperty("users")[0].GetProperty("username").GetString());

        Assert.Equal("proxy", root.GetProperty("route").GetProperty("final").GetString());
        Assert.Equal("tun-dns", root.GetProperty("dns").GetProperty("final").GetString());
        Assert.Equal("https", root.GetProperty("dns").GetProperty("servers")[1].GetProperty("type").GetString());
        Assert.Equal("dns.google", root.GetProperty("dns").GetProperty("servers")[1].GetProperty("server").GetString());
        Assert.Equal("127.0.0.1:29090",
            root.GetProperty("experimental").GetProperty("clash_api").GetProperty("external_controller").GetString());
    }

    [Fact]
    public void RejectsTunWithInvalidDiagnosticPort()
    {
        var server = ParseOne(
            "vless://11111111-1111-1111-1111-111111111111@example.com:443?security=tls&sni=example.com#Tun");
        var ok = _builder.TryBuildTun(server, new TunOptions(DiagnosticPort: 0), out _, out var error);
        Assert.False(ok);
        Assert.Contains("port", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TunConfigPassesPinnedEngineCheck()
    {
        var engine = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "vendor", "sing-box", "sing-box.exe"));
        if (!File.Exists(engine))
            return; // CI without the pinned engine; local verification still runs.

        var server = ParseOne(
            "vless://11111111-1111-1111-1111-111111111111@example.com:443" +
            "?security=reality&sni=www.example.com&fp=chrome" +
            "&pbk=jNXHt1yRo0vDuchQlIP6Z0ZvjT3KtzVI-T4E7RoLJS0&sid=12&type=tcp#TunCheck");

        var options = new TunOptions(
            InterfaceName: "FulbrCheck",
            DiagnosticPort: 20808,
            DiagnosticUsername: "diag",
            DiagnosticPassword: "secret",
            ClashApiPort: 29090,
            ClashApiSecret: "api-secret");

        Assert.True(_builder.TryBuildTun(server, options, out var json, out var error), error);

        var path = Path.Combine(Path.GetTempPath(), $"fulbr-tun-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = engine,
                    Arguments = $"check -c \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.Start();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(15_000);
            Assert.True(process.ExitCode == 0, stderr);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
