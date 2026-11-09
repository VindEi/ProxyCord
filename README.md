# ProxyCord

<p align="center">
  <strong>Split-tunnel Discord traffic through any SOCKS5 or HTTP proxy on Windows.</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?logo=windows&logoColor=white" alt="Platform" />
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white" alt=".NET Version" />
  <img src="https://img.shields.io/badge/Architecture-x64-blue" alt="Architecture" />
  <img src="https://img.shields.io/badge/License-MIT-green" alt="License" />
</p>

---

## What is ProxyCord?

In many regions and restricted network environments, Discord is blocked, throttled, or subjected to severe packet loss. Turning on a system-wide VPN or TUN proxy solves the problem for Discord, but routes **all** your network traffic (Steam games, web browsers, torrents, work apps) through the proxy—introducing latency spikes in online games, wasting bandwidth, and breaking local network access.

**ProxyCord** solves this by creating a dedicated virtual network adapter that captures **only Discord traffic**. All other applications on your PC continue communicating directly through your physical network card with zero latency penalty.

---

## Key Features

- **Single Portable Executable (`ProxyCord.exe`)**: Completely self-contained ~22 MB binary with no installers, external runtime requirements, or loose DLL files.
- **Dedicated Virtual Adapter (`ProxyCord`)**: Configures a dedicated Wintun virtual network interface cleanly named `ProxyCord` in Windows Network Connections.
- **Independent Protocol Toggles**:
  - **TCP (Chat & Gateway)**: Routes text messages, WebSocket gateway connections, embeds, and updates through the proxy.
  - **UDP (Voice & WebRTC)**: Routes Discord voice channels, screen share streams, and Opus audio packets through the proxy.
- **Zero-Disruption Live Reload**: Toggling TCP or UDP updates routing rules on the fly via an internal REST API without restarting the TUN driver or dropping active voice calls.
- **Live Upstream Probe**: Verifies your proxy is reachable using non-blocking TCP handshakes with cancellation timeouts, accurately indicating whether traffic is actively flowing or if the upstream proxy is down.
- **Flexible Proxy Formats**: Supports port numbers (`2080`), host-port pairs (`127.0.0.1:10808`, `192.168.1.50:8080`), and full authenticated URIs (`socks5://user:pass@host:port` or `http://host:port`).
- **Modern Interface**: Designed with Obsidian Dark and Studio Light modes, concentric tactile power button, real-time activity log viewer, and smart system tray minimization.

---

## How It Works Under the Hood

```
+---------------------------------------------------------------------------------+
|                                 WINDOWS SYSTEM                                  |
|                                                                                 |
|   +-----------------------+     +------------------------+   +--------------+   |
|   |      Discord.exe      |     |  Update.exe (Discord)  |   | Steam / Web  |   |
|   +-----------------------+     +------------------------+   +--------------+   |
|               |                             |                        |          |
|               +--------------+--------------+                        |          |
|                              |                                       |          |
|                              v                                       v          |
|         +-----------------------------------------+           DIRECT INTERNET   |
|         |     Wintun Virtual Adapter (ProxyCord)  |           (Physical NIC)    |
|         +-----------------------------------------+                             |
|                              |                                                  |
|                              v                                                  |
|         +-----------------------------------------+                             |
|         |           Embedded Mihomo Core          |                             |
|         |  • Fake-IP DNS Resolver (198.18.0.1/16) |                             |
|         |  • Rule Matcher: Process & Domain       |                             |
|         +-----------------------------------------+                             |
|                     /                         \                                 |
|          TCP Rules /                           \ UDP Rules                      |
|                   v                             v                               |
|        [TCP: Proxy or Direct]         [UDP: Proxy or Direct]                    |
|                   \                             /                               |
|                    +-------------+-------------+                                |
|                                  |                                              |
|                                  v                                              |
|                     Local / Remote Proxy Upstream                               |
|               (SOCKS5 / HTTP e.g., 127.0.0.1:2080)                             |
+---------------------------------------------------------------------------------+
```

### 1. Driver & Binary Self-Extraction
ProxyCord embeds high-performance compiled binaries (`mihomo.exe` and `wintun.dll`) compressed inside its assembly resources. On first launch, it extracts them directly to `%LOCALAPPDATA%\ProxyCord\bin\`. No manual driver installation or setup is needed.

### 2. Process Filtering & DNS Interception
ProxyCord provisions a Wintun adapter in `mixed` stack mode with `auto-route: true`. It employs process-level capture (`include-process: [Discord.exe, Update.exe]`) and redirects DNS requests on `any:53` through an internal fake-IP resolver (`198.18.0.1/16`). This prevents DNS poisoning and ensures Windows never routes general applications into the TUN tunnel.

### 3. Rule Isolation
Generated routing rules ensure surgical precision:
```yaml
rules:
  # TCP: Chat, media, gateway
  - AND,((PROCESS-NAME,Discord.exe),(NETWORK,tcp)),local-proxy
  # Updates: Only Discord update domains
  - AND,((PROCESS-NAME,Update.exe),(DOMAIN-SUFFIX,discord.com)),local-proxy
  - AND,((PROCESS-NAME,Update.exe),(DOMAIN-SUFFIX,discord.gg)),local-proxy
  - AND,((PROCESS-NAME,Update.exe),(DOMAIN-SUFFIX,discordapp.com)),local-proxy
  # UDP: Voice and WebRTC audio
  - AND,((PROCESS-NAME,Discord.exe),(NETWORK,udp)),local-proxy
  # All non-Discord traffic stays untouched
  - PROCESS-NAME,Update.exe,DIRECT
  - MATCH,DIRECT
```

### 4. Live Rule Hot-Reloading
When you switch the **TCP** or **UDP** chips in the UI, ProxyCord rewires the configuration file and submits an atomic HTTP `PUT` request to `http://127.0.0.1:9097/configs?force=true`. The routing engine reloads its rules in-memory within milliseconds without tearing down the network adapter.

### 5. Non-Blocking Upstream Health Probing
A background `DispatcherTimer` periodically tests upstream connectivity using `client.ConnectAsync(host, port, cancellationToken)`. If your proxy client crashes or a port closes, the app automatically transitions to an Amber warning state and warns you in the system tray without leaking unhandled socket handles.

---

## Installation & Requirements

- **Operating System**: Windows 10 or Windows 11 (64-bit).
- **Permissions**: **Must be run as Administrator**. Windows requires administrative privileges to register and bind virtual TUN network drivers.
- **Upstream Proxy**: An active SOCKS5 or HTTP proxy client (such as v2rayN, NekoRay, Throne, Clash, sing-box, Xray, or an SSH tunnel).

### Quick Start
1. Download `ProxyCord.exe` from the [Releases](https://github.com/your-username/ProxyCord/releases) page.
2. In **Settings**, enter your proxy address (default is `127.0.0.1:2080`).
3. Click the **Connect** to start routing.
5. Launch Discord—your chat and voice will route through your proxy seamlessly.

---

## Configuration Reference

ProxyCord accepts any of the following endpoint notations in the **Proxy Endpoint** input:

| Format | Example | Protocol | Target |
| :--- | :--- | :--- | :--- |
| **Port only** | `2080` | SOCKS5 | `127.0.0.1:2080` |
| **Host & Port** | `127.0.0.1:2080` | SOCKS5 | `127.0.0.1:2080` |
| **SOCKS5 URI** | `socks5://127.0.0.1:2080` | SOCKS5 | `127.0.0.1:2080` |
| **Authenticated SOCKS5** | `socks5://admin:pass123@remote.host:1080` | SOCKS5 | Authenticated |
| **HTTP Proxy** | `http://127.0.0.1:8080` | HTTP | `127.0.0.1:8080` |

---

## Building from Source

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Run Unit Tests
```powershell
dotnet test src/ProxyCord.Tests/ProxyCord.Tests.csproj
```

### Compile Standalone Executable
```powershell
dotnet publish src/ProxyCord/ProxyCord.csproj `
  -c Release `
  -r win-x64 `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  --self-contained false `
  -o dist
```
The compiled single-file executable will be generated at `dist/ProxyCord.exe`.

---
## Troubleshooting 

#### Why does Discord voice say "RTC Connecting" or "No Route"?
Discord voice channels use UDP (WebRTC). Make sure:
1. The **UDP** toggle in ProxyCord is enabled.
2. Your upstream proxy protocol and server support UDP (e.g. standard SOCKS5 with UDP enabled). Standard HTTP proxies do **not** support UDP voice.

#### Windows Defender / SmartScreen warning on launch?
Because this is an open-source binary compiled without an expensive commercial EV code-signing certificate, Windows SmartScreen may show an "Unknown Publisher" prompt on first launch. Click **More info** -> **Run anyway**. You can also compile the executable yourself directly using the steps in [Building from Source](#building-from-source).

#### Where are settings and logs stored?
Settings and logs are stored locally in:
`%LOCALAPPDATA%\ProxyCord\` (`settings.json`, `config.yaml`, and runtime binaries).

---

## License

MIT License. See [LICENSE](LICENSE) for details.
