namespace Fulbr.Core.Engine;

public sealed record TunOptions(
    string InterfaceName = "Fulbr",
    string Address4 = "10.255.255.1/30",
    string Address6 = "fdfe:dcba:9876:1::1/126",
    int Mtu = 1500,
    bool AutoRoute = true,
    bool StrictRoute = true,
    string Stack = "gvisor",
    int DiagnosticPort = 0,
    string DiagnosticUsername = "",
    string DiagnosticPassword = "",
    string DnsServer = "dns.google",
    int ClashApiPort = 0,
    string ClashApiSecret = "",
    string LogLevel = "warn",
    string LogPath = "");
