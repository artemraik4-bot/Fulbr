namespace Fulbr.Core.Models;

public enum ProxyProtocol
{
    Unknown = 0,
    Vless,
    Vmess,
    Trojan,
    Shadowsocks,
    Hysteria2,
    Tuic,
    Socks5,
    Http
}

public static class ProxyProtocolExtensions
{
    public static bool UsesTls(this ProxyProtocol protocol) => protocol is
        ProxyProtocol.Vless or ProxyProtocol.Vmess or ProxyProtocol.Trojan or
        ProxyProtocol.Hysteria2 or ProxyProtocol.Tuic;

    public static bool UsesUdpTransport(this ProxyProtocol protocol) => protocol is
        ProxyProtocol.Hysteria2 or ProxyProtocol.Tuic;

    public static string ToDisplayName(this ProxyProtocol protocol) => protocol switch
    {
        ProxyProtocol.Vless => "VLESS",
        ProxyProtocol.Vmess => "VMess",
        ProxyProtocol.Trojan => "Trojan",
        ProxyProtocol.Shadowsocks => "Shadowsocks",
        ProxyProtocol.Hysteria2 => "Hysteria2",
        ProxyProtocol.Tuic => "TUIC",
        ProxyProtocol.Socks5 => "SOCKS5",
        ProxyProtocol.Http => "HTTP",
        _ => "Unknown"
    };
}
