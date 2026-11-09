using System;
using Xunit;
using ProxyCord;

namespace ProxyCord.Tests
{
    public class RouterServiceTests
    {
        [Fact]
        public void GenerateConfig_BothTogglesOn_RoutesBothToLocalProxy()
        {
            var router = new RouterService();
            var yaml = router.GenerateConfig("2080", true, true);

            Assert.Contains("port: 2080", yaml);
            Assert.Contains("device: ProxyCord", yaml);
            Assert.Contains("AND,((PROCESS-NAME,Discord.exe),(NETWORK,tcp)),local-proxy", yaml);
            Assert.Contains("AND,((PROCESS-NAME,Discord.exe),(NETWORK,udp)),local-proxy", yaml);
            Assert.Contains("AND,((PROCESS-NAME,Update.exe),(DOMAIN-SUFFIX,discord.com)),local-proxy", yaml);
            Assert.Contains("PROCESS-NAME,Update.exe,DIRECT", yaml);
            Assert.Contains("include-process:", yaml);
            Assert.Contains("- Discord.exe", yaml);
            Assert.Contains("- Update.exe", yaml);
            Assert.Contains("MATCH,DIRECT", yaml);
        }

        [Fact]
        public void GenerateConfig_TcpOnUdpOff_RoutesTcpToProxyAndUdpToDirect()
        {
            var router = new RouterService();
            var yaml = router.GenerateConfig("2080", true, false);

            Assert.Contains("port: 2080", yaml);
            Assert.Contains("AND,((PROCESS-NAME,Discord.exe),(NETWORK,tcp)),local-proxy", yaml);
            Assert.Contains("AND,((PROCESS-NAME,Discord.exe),(NETWORK,udp)),DIRECT", yaml);
            Assert.Contains("AND,((PROCESS-NAME,Update.exe),(DOMAIN-SUFFIX,discord.com)),local-proxy", yaml);
            Assert.Contains("PROCESS-NAME,Update.exe,DIRECT", yaml);
        }

        [Fact]
        public void GenerateConfig_TcpOffUdpOn_RoutesTcpToDirectAndUdpToProxy()
        {
            var router = new RouterService();
            var yaml = router.GenerateConfig("10808", false, true);

            Assert.Contains("port: 10808", yaml);
            Assert.Contains("AND,((PROCESS-NAME,Discord.exe),(NETWORK,tcp)),DIRECT", yaml);
            Assert.Contains("AND,((PROCESS-NAME,Discord.exe),(NETWORK,udp)),local-proxy", yaml);
            Assert.Contains("PROCESS-NAME,Update.exe,DIRECT", yaml);
        }

        [Fact]
        public void Settings_SaveAndLoad_MaintainsState()
        {
            var settings = new AppSettings
            {
                ProxyEndpoint = "socks5://admin:pass123@192.168.1.100:1080",
                IsDarkMode = false,
                RouteTcp = true,
                RouteUdp = false
            };

            settings.Save();
            var loaded = AppSettings.Load();

            Assert.Equal("socks5://admin:pass123@192.168.1.100:1080", loaded.ProxyEndpoint);
            Assert.False(loaded.IsDarkMode);
            Assert.True(loaded.RouteTcp);
            Assert.False(loaded.RouteUdp);

            // Restore defaults
            loaded.ProxyEndpoint = "127.0.0.1:2080";
            loaded.IsDarkMode = true;
            loaded.RouteTcp = true;
            loaded.RouteUdp = true;
            loaded.Save();
        }

        [Fact]
        public void ParseEndpoint_HandlesPortOnly_HostAndPort_AndFullUri()
        {
            var p1 = RouterService.ParseEndpoint("2080");
            Assert.Equal("127.0.0.1", p1.Server);
            Assert.Equal(2080, p1.Port);
            Assert.Equal("socks5", p1.Type);

            var p2 = RouterService.ParseEndpoint("192.168.1.50:1080");
            Assert.Equal("192.168.1.50", p2.Server);
            Assert.Equal(1080, p2.Port);

            var p3 = RouterService.ParseEndpoint("socks5://alice:secret99@proxy.example.com:8443");
            Assert.Equal("proxy.example.com", p3.Server);
            Assert.Equal(8443, p3.Port);
            Assert.Equal("alice", p3.Username);
            Assert.Equal("secret99", p3.Password);
            Assert.Equal("socks5", p3.Type);
        }

        [Fact]
        public void GenerateConfig_WithRemoteEndpointAndCredentials_EmitsProperYaml()
        {
            var router = new RouterService();
            var yaml = router.GenerateConfig("socks5://myuser:mypass@remote.host.lan:9999", true, true);

            Assert.Contains("server: remote.host.lan", yaml);
            Assert.Contains("port: 9999", yaml);
            Assert.Contains("username: myuser", yaml);
            Assert.Contains("password: mypass", yaml);
        }

        [Fact]
        public async System.Threading.Tasks.Task CheckUpstreamReachable_UnusedPort_ReturnsFalse()
        {
            // Port 59999 is typically unused locally; should fail gracefully and return false
            var reachable = await RouterService.CheckUpstreamReachableAsync("127.0.0.1:59999", timeoutMs: 200);
            Assert.False(reachable);
        }

        [Fact]
        public void Settings_CloseToTray_DefaultsToTrue()
        {
            var settings = new AppSettings();
            Assert.True(settings.CloseToTray);
            Assert.Equal("127.0.0.1:2080", settings.ProxyEndpoint);
        }

        [Fact]
        public void GetAvailablePorts_ReturnsValidPortNumbers()
        {
            var tcpPort = RouterService.GetAvailableTcpPort(9097);
            var udpPort = RouterService.GetAvailableUdpPort(1053);
            Assert.InRange(tcpPort, 1, 65535);
            Assert.InRange(udpPort, 1, 65535);
        }

        [Fact]
        public void AppIcon_ExistsAndIsValid()
        {
            var testDir = AppContext.BaseDirectory;
            var projectDir = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "ProxyCord"));
            var iconPath = Path.Combine(projectDir, "app.ico");
            Assert.True(File.Exists(iconPath), $"app.ico should exist at {iconPath}");
            Assert.True(new FileInfo(iconPath).Length > 10000, "app.ico should be larger than 10KB");
        }
    }
}
