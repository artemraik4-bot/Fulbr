using System.Text;
using Fulbr.Core.Models;
using Fulbr.Core.Parsing;
using Xunit;

namespace Fulbr.Core.Tests;

public sealed class ShareLinkParserTests
{
    private readonly ShareLinkParser _parser = new();

    [Fact]
    public void ParsesVlessReality()
    {
        const string link =
            "vless://11111111-1111-1111-1111-111111111111@example.com:443" +
            "?security=reality&sni=www.example.com&fp=chrome&pbk=abc&sid=12&type=tcp&flow=xtls-rprx-vision#Test%20Node";

        var result = _parser.Parse(link);
        var server = Assert.Single(result.Servers);

        Assert.Equal(ProxyProtocol.Vless, server.Protocol);
        Assert.Equal("example.com", server.Host);
        Assert.Equal(443, server.Port);
        Assert.Equal("Test Node", server.DisplayName);
        Assert.True(server.TlsEnabled);
        Assert.False(server.InsecureTls);
        Assert.Equal("tcp", server.Transport);
        Assert.Equal("xtls-rprx-vision", server.Flow);
        Assert.Equal(64, server.Fingerprint.Length);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public void FlagsInsecureTls()
    {
        const string link =
            "trojan://secret@example.com:443?security=tls&sni=example.com&allowInsecure=1#Insecure";
        var result = _parser.Parse(link);
        var server = Assert.Single(result.Servers);
        Assert.True(server.InsecureTls);
        Assert.Contains(server.Warnings, w => w.Contains("verification", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParsesVmessBase64Json()
    {
        const string json =
            "{\"v\":\"2\",\"ps\":\"VMess Node\",\"add\":\"vm.example.com\",\"port\":\"443\"," +
            "\"id\":\"11111111-1111-1111-1111-111111111111\",\"aid\":\"0\",\"scy\":\"auto\"," +
            "\"net\":\"ws\",\"type\":\"none\",\"host\":\"vm.example.com\",\"path\":\"/ws\"," +
            "\"tls\":\"tls\",\"sni\":\"vm.example.com\"}";
        var link = "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        var result = _parser.Parse(link);
        var server = Assert.Single(result.Servers);
        Assert.Equal(ProxyProtocol.Vmess, server.Protocol);
        Assert.Equal("vm.example.com", server.Host);
        Assert.Equal(443, server.Port);
        Assert.True(server.TlsEnabled);
    }

    [Fact]
    public void ParsesShadowsocksSip002()
    {
        var user = Convert.ToBase64String(Encoding.UTF8.GetBytes("aes-256-gcm:secret"));
        var link = $"ss://{user}@ss.example.com:8388#SS%20Node";

        var result = _parser.Parse(link);
        var server = Assert.Single(result.Servers);
        Assert.Equal(ProxyProtocol.Shadowsocks, server.Protocol);
        Assert.Equal("ss.example.com", server.Host);
        Assert.Equal(8388, server.Port);
        Assert.Equal("SS Node", server.DisplayName);
    }

    [Fact]
    public void ParsesShadowsocksLegacy()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("aes-128-gcm:secret@ss.example.com:8388"));
        var link = $"ss://{payload}#Legacy";

        var result = _parser.Parse(link);
        var server = Assert.Single(result.Servers);
        Assert.Equal("ss.example.com", server.Host);
    }

    [Fact]
    public void RejectsWeakShadowsocksCipher()
    {
        var user = Convert.ToBase64String(Encoding.UTF8.GetBytes("rc4-md5:secret"));
        var link = $"ss://{user}@ss.example.com:8388#Weak";

        var result = _parser.Parse(link);
        Assert.Empty(result.Servers);
        Assert.Single(result.Rejected);
        Assert.Contains("allow-list", result.Rejected[0].Reason);
    }

    [Fact]
    public void ParsesHysteria2AndTuic()
    {
        var result = _parser.Parse(
            "hysteria2://pass@hy.example.com:443?sni=hy.example.com&insecure=1#HY2\n" +
            "tuic://uuid:pass@tuic.example.com:443?sni=tuic.example.com#TUIC");
        Assert.Equal(2, result.Servers.Count);
        Assert.Contains(result.Servers, s => s.Protocol == ProxyProtocol.Hysteria2 && s.InsecureTls);
        Assert.Contains(result.Servers, s => s.Protocol == ProxyProtocol.Tuic);
    }

    [Fact]
    public void RejectsUnknownScheme()
    {
        var result = _parser.Parse("ftp://example.com/file#x");
        Assert.Empty(result.Servers);
        Assert.Single(result.Rejected);
        Assert.Contains("Unsupported scheme", result.Rejected[0].Reason);
    }

    [Fact]
    public void ParsesWholeBase64SubscriptionAndDeduplicates()
    {
        const string link =
            "vless://11111111-1111-1111-1111-111111111111@example.com:443?security=tls&sni=example.com#Node";
        var content = Convert.ToBase64String(Encoding.UTF8.GetBytes(link + "\n" + link));

        var result = _parser.Parse(content);
        Assert.Single(result.Servers); // duplicate removed
    }

    [Fact]
    public void RedactedPreviewHidesCredentials()
    {
        const string link =
            "vless://11111111-1111-1111-1111-111111111111@example.com:443?security=tls&sni=example.com#Name";
        var result = _parser.Parse(link + "\nnot-a-link");
        Assert.Single(result.Rejected);
        Assert.DoesNotContain("11111111", result.Rejected[0].RedactedPreview);
    }
}
