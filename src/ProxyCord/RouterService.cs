using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ProxyCord
{
    public record ProxyEndpointInfo(string Server, int Port, string? Username = null, string? Password = null, string Type = "socks5");

    public class RouterService
    {
        private Process? _process;
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };

        public bool IsRunning => _process != null && !_process.HasExited;

        public int ControllerPort { get; private set; } = 9097;
        public int DnsPort { get; private set; } = 1053;

        public event Action<string>? OnLog;
        public event Action<bool>? OnStatusChanged;

        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProxyCord");
        private static readonly string BinDir = Path.Combine(AppDataDir, "bin");
        private static readonly string RouterExePath = Path.Combine(BinDir, "mihomo.exe");
        private static readonly string WintunDllPath = Path.Combine(BinDir, "wintun.dll");
        private static readonly string ConfigPath = Path.Combine(AppDataDir, "config.yaml");

        public void ExtractBinariesIfMissing()
        {
            Directory.CreateDirectory(BinDir);
            ExtractResource("ProxyCord.Resources.router.gz", RouterExePath);
            ExtractResource("ProxyCord.Resources.wintun.gz", WintunDllPath);
        }

        private static void ExtractResource(string resourceName, string outputPath)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return;

            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 1000)
            {
                return;
            }

            try
            {
                using var gz = new GZipStream(stream, CompressionMode.Decompress);
                using var fs = File.Create(outputPath);
                gz.CopyTo(fs);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to extract {resourceName}: {ex.Message}");
            }
        }

        public static ProxyEndpointInfo ParseEndpoint(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return new ProxyEndpointInfo("127.0.0.1", 2080);
            }

            input = input.Trim();

            // 1. Pure port number: "2080"
            if (int.TryParse(input, out var portOnly) && portOnly > 0 && portOnly <= 65535)
            {
                return new ProxyEndpointInfo("127.0.0.1", portOnly);
            }

            // 2. URI: "socks5://..." or "http://..."
            if (input.Contains("://"))
            {
                try
                {
                    var uri = new Uri(input);
                    var server = uri.Host;
                    var port = uri.Port > 0 ? uri.Port : 1080;
                    string? user = null;
                    string? pass = null;

                    if (!string.IsNullOrEmpty(uri.UserInfo))
                    {
                        var parts = uri.UserInfo.Split(':', 2);
                        user = Uri.UnescapeDataString(parts[0]);
                        if (parts.Length > 1) pass = Uri.UnescapeDataString(parts[1]);
                    }

                    var type = uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? "http" : "socks5";
                    return new ProxyEndpointInfo(server, port, user, pass, type);
                }
                catch { }
            }

            // 3. host:port: "127.0.0.1:2080" or "192.168.1.100:1080"
            var colonIdx = input.LastIndexOf(':');
            if (colonIdx > 0 && colonIdx < input.Length - 1)
            {
                var host = input.Substring(0, colonIdx).Trim();
                var portStr = input.Substring(colonIdx + 1).Trim();
                if (int.TryParse(portStr, out var p) && p > 0 && p <= 65535)
                {
                    return new ProxyEndpointInfo(host, p);
                }
            }

            return new ProxyEndpointInfo(input, 2080);
        }

        public static async Task<bool> CheckUpstreamReachableAsync(string endpoint, int timeoutMs = 1200)
        {
            try
            {
                var info = ParseEndpoint(endpoint);
                using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
                using var client = new TcpClient();
                await client.ConnectAsync(info.Server, info.Port, cts.Token);
                return client.Connected;
            }
            catch { }
            return false;
        }

        public string GenerateConfig(string endpoint, bool routeTcp, bool routeUdp)
        {
            var info = ParseEndpoint(endpoint);
            var yaml = new StringBuilder();
            yaml.AppendLine("mode: rule");
            yaml.AppendLine("log-level: info");
            yaml.AppendLine("ipv6: false");
            yaml.AppendLine($"external-controller: 127.0.0.1:{ControllerPort}");
            yaml.AppendLine();
            yaml.AppendLine("dns:");
            yaml.AppendLine("  enable: true");
            yaml.AppendLine($"  listen: 127.0.0.1:{DnsPort}");
            yaml.AppendLine("  nameserver:");
            yaml.AppendLine("    - 8.8.8.8");
            yaml.AppendLine("    - 1.1.1.1");
            yaml.AppendLine();
            yaml.AppendLine("tun:");
            yaml.AppendLine("  enable: true");
            yaml.AppendLine("  device: ProxyCord");
            yaml.AppendLine("  stack: mixed");
            yaml.AppendLine("  auto-route: true");
            yaml.AppendLine("  auto-detect-interface: true");
            yaml.AppendLine("  include-process:");
            yaml.AppendLine("    - Discord.exe");
            yaml.AppendLine("    - Update.exe");
            yaml.AppendLine("  dns-hijack:");
            yaml.AppendLine("    - any:53");
            yaml.AppendLine();
            yaml.AppendLine("proxies:");
            yaml.AppendLine("  - name: local-proxy");
            yaml.AppendLine($"    type: {info.Type}");
            yaml.AppendLine($"    server: {info.Server}");
            yaml.AppendLine($"    port: {info.Port}");
            yaml.AppendLine("    udp: true");
            if (!string.IsNullOrEmpty(info.Username))
            {
                yaml.AppendLine($"    username: {info.Username}");
            }
            if (!string.IsNullOrEmpty(info.Password))
            {
                yaml.AppendLine($"    password: {info.Password}");
            }
            yaml.AppendLine();
            yaml.AppendLine("rules:");

            // Discord TCP traffic
            if (routeTcp)
            {
                yaml.AppendLine("  - AND,((PROCESS-NAME,Discord.exe),(NETWORK,tcp)),local-proxy");
                // Only route Update.exe when it contacts Discord domains - never other apps' Update.exe
                yaml.AppendLine("  - AND,((PROCESS-NAME,Update.exe),(DOMAIN-SUFFIX,discord.com)),local-proxy");
                yaml.AppendLine("  - AND,((PROCESS-NAME,Update.exe),(DOMAIN-SUFFIX,discord.gg)),local-proxy");
                yaml.AppendLine("  - AND,((PROCESS-NAME,Update.exe),(DOMAIN-SUFFIX,discordapp.com)),local-proxy");
            }
            else
            {
                yaml.AppendLine("  - AND,((PROCESS-NAME,Discord.exe),(NETWORK,tcp)),DIRECT");
            }

            // Discord UDP traffic (Voice, WebRTC)
            if (routeUdp)
            {
                yaml.AppendLine("  - AND,((PROCESS-NAME,Discord.exe),(NETWORK,udp)),local-proxy");
            }
            else
            {
                yaml.AppendLine("  - AND,((PROCESS-NAME,Discord.exe),(NETWORK,udp)),DIRECT");
            }

            // Any other Update.exe on the PC goes directly
            yaml.AppendLine("  - PROCESS-NAME,Update.exe,DIRECT");
            yaml.AppendLine("  - MATCH,DIRECT");

            return yaml.ToString();
        }

        public async Task<bool> StartAsync(string endpoint, bool routeTcp, bool routeUdp)
        {
            if (IsRunning) return true;

            try
            {
                KillStaleProcess();
                ExtractBinariesIfMissing();

                ControllerPort = GetAvailableTcpPort(9097);
                DnsPort = GetAvailableUdpPort(1053);

                var configContent = GenerateConfig(endpoint, routeTcp, routeUdp);
                var info = ParseEndpoint(endpoint);
                Directory.CreateDirectory(AppDataDir);
                await File.WriteAllTextAsync(ConfigPath, configContent);

                Log($"Routing engine initialized -> {info.Type}://{info.Server}:{info.Port}");
                Log($"TCP Chat/Updates: {(routeTcp ? "ON (PROXY)" : "OFF (DIRECT)")}");
                Log($"UDP Voice/WebRTC: {(routeUdp ? "ON (PROXY)" : "OFF (DIRECT)")}");

                var psi = new ProcessStartInfo
                {
                    FileName = RouterExePath,
                    Arguments = $"-f \"{ConfigPath}\" -d \"{AppDataDir}\"",
                    WorkingDirectory = BinDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

                _process.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        CleanAndLog(e.Data);
                    }
                };

                _process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        CleanAndLog(e.Data);
                    }
                };

                _process.Exited += (_, _) =>
                {
                    Log("Routing engine stopped.");
                    _process = null;
                    OnStatusChanged?.Invoke(false);
                };

                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                OnStatusChanged?.Invoke(true);
                return true;
            }
            catch (Exception ex)
            {
                Log($"Failed to start: {ex.Message}");
                _process = null;
                OnStatusChanged?.Invoke(false);
                return false;
            }
        }

        public async Task StopAsync()
        {
            if (!IsRunning) return;

            try
            {
                Log("Stopping routing engine...");
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill(true);
                    await _process.WaitForExitAsync();
                }
            }
            catch (Exception ex)
            {
                Log($"Error while stopping: {ex.Message}");
            }
            finally
            {
                _process = null;
                OnStatusChanged?.Invoke(false);
            }
        }

        public async Task UpdateTogglesLiveAsync(string endpoint, bool routeTcp, bool routeUdp)
        {
            if (!IsRunning) return;

            try
            {
                var newConfig = GenerateConfig(endpoint, routeTcp, routeUdp);
                await File.WriteAllTextAsync(ConfigPath, newConfig);

                Log($"Updating rules live: TCP={(routeTcp ? "PROXY" : "DIRECT")}, UDP={(routeUdp ? "PROXY" : "DIRECT")}...");

                var json = $"{{\"path\":\"{ConfigPath.Replace("\\", "\\\\")}\"}}";
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _http.PutAsync($"http://127.0.0.1:{ControllerPort}/configs?force=true", content);

                if (response.IsSuccessStatusCode)
                {
                    Log("Rules updated live successfully!");
                }
                else
                {
                    Log($"Live update response: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Log($"Live update note: {ex.Message}");
            }
        }

        private void CleanAndLog(string raw)
        {
            var msg = raw;
            var idx = raw.IndexOf("msg=\"");
            if (idx >= 0)
            {
                var end = raw.IndexOf("\"", idx + 5);
                if (end > idx + 5)
                {
                    msg = raw.Substring(idx + 5, end - (idx + 5));
                }
            }
            Log(msg);
        }

        private void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            OnLog?.Invoke(line);
        }

        private static void KillStaleProcess()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("mihomo"))
                {
                    try
                    {
                        if (string.Equals(p.MainModule?.FileName, RouterExePath, StringComparison.OrdinalIgnoreCase))
                        {
                            p.Kill();
                            p.WaitForExit(1000);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static int GetAvailableTcpPort(int preferred)
        {
            try
            {
                using var listener = new TcpListener(System.Net.IPAddress.Loopback, preferred);
                listener.Start();
                return preferred;
            }
            catch
            {
                try
                {
                    using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
                    listener.Start();
                    return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
                }
                catch { return preferred; }
            }
        }

        public static int GetAvailableUdpPort(int preferred)
        {
            try
            {
                using var client = new UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, preferred));
                return preferred;
            }
            catch
            {
                try
                {
                    using var client = new UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
                    return ((System.Net.IPEndPoint)client.Client.LocalEndPoint!).Port;
                }
                catch { return preferred; }
            }
        }
    }
}
