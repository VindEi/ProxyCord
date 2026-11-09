using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Forms;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ProxyCord
{
    public partial class MainWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly RouterService _router;
        private NotifyIcon? _notifyIcon;
        private DispatcherTimer? _upstreamTimer;
        private bool _lastReachable;

        public MainWindow()
        {
            InitializeComponent();

            _settings = AppSettings.Load();
            _router = new RouterService();

            _router.OnLog += LogReceived;
            _router.OnStatusChanged += StatusChanged;

            // Load saved settings
            ChkRouteTcp.IsChecked = _settings.RouteTcp;
            ChkRouteUdp.IsChecked = _settings.RouteUdp;
            ChkCloseToTray.IsChecked = _settings.CloseToTray;
            TxtProxyEndpoint.Text = _settings.ProxyEndpoint;
            TxtHeaderEndpoint.Text = $"• {_settings.ProxyEndpoint}";

            if (_settings.IsDarkMode)
            {
                RadioThemeDark.IsChecked = true;
            }
            else
            {
                RadioThemeLight.IsChecked = true;
            }

            ApplyTheme(_settings.IsDarkMode);
            InitSystemTray();
        }

        private void InitSystemTray()
        {
            _notifyIcon = new NotifyIcon
            {
                Text = "ProxyCord - Ready",
                Visible = true
            };

            UpdateSystemTray(false);

            var contextMenu = new ContextMenuStrip();
            var openItem = contextMenu.Items.Add("Open");
            openItem.Click += (_, _) => RestoreWindow();

            var exitItem = contextMenu.Items.Add("Exit");
            exitItem.Click += (_, _) => ShutdownApp();

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (_, _) => RestoreWindow();
        }

        private void ShutdownApp()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
            _upstreamTimer?.Stop();
            _router.StopAsync().Wait();
            Application.Current.Shutdown();
        }

        private void UpdateSystemTray(bool isRunning, bool isReachable = true)
        {
            if (_notifyIcon == null) return;
            try
            {
                using var bmp = new Bitmap(32, 32);
                using var g = Graphics.FromImage(bmp);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);
                var baseColor = !isRunning
                    ? System.Drawing.Color.FromArgb(37, 99, 235)  // Electric Azure Blue (Ready)
                    : (isReachable
                        ? System.Drawing.Color.FromArgb(16, 185, 129) // Emerald Green (Connected)
                        : System.Drawing.Color.FromArgb(245, 158, 11)); // Amber (Unreachable)

                using (var brush = new SolidBrush(baseColor))
                {
                    g.FillEllipse(brush, 4, 4, 24, 24);
                }
                using (var inner = new SolidBrush(System.Drawing.Color.White))
                {
                    g.FillEllipse(inner, 11, 11, 10, 10);
                }
                var hIcon = bmp.GetHicon();
                _notifyIcon.Icon = System.Drawing.Icon.FromHandle(hIcon);
            }
            catch { }
        }

        private void RestoreWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_settings.CloseToTray)
            {
                Hide();
            }
            else
            {
                ShutdownApp();
            }
        }

        private void ChkCloseToTray_Click(object sender, RoutedEventArgs e)
        {
            _settings.CloseToTray = ChkCloseToTray.IsChecked ?? true;
            _settings.Save();
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (PageMain == null || PageSettings == null || PageLogs == null || TxtPageTitle == null) return;

            if (NavMain.IsChecked == true)
            {
                PageMain.Visibility = Visibility.Visible;
                PageSettings.Visibility = Visibility.Collapsed;
                PageLogs.Visibility = Visibility.Collapsed;
                TxtPageTitle.Text = "Dashboard";
            }
            else if (NavSettings.IsChecked == true)
            {
                PageMain.Visibility = Visibility.Collapsed;
                PageSettings.Visibility = Visibility.Visible;
                PageLogs.Visibility = Visibility.Collapsed;
                TxtPageTitle.Text = "Settings";
            }
            else if (NavLogs.IsChecked == true)
            {
                PageMain.Visibility = Visibility.Collapsed;
                PageSettings.Visibility = Visibility.Collapsed;
                PageLogs.Visibility = Visibility.Visible;
                TxtPageTitle.Text = "Activity Logs";
            }
        }

        private async void BtnConnectToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_router.IsRunning)
            {
                BtnConnectToggle.IsEnabled = false;
                await _router.StopAsync();
                BtnConnectToggle.IsEnabled = true;
            }
            else
            {
                BtnConnectToggle.IsEnabled = false;
                var endpoint = _settings.ProxyEndpoint;
                var tcp = ChkRouteTcp.IsChecked ?? true;
                var udp = ChkRouteUdp.IsChecked ?? true;

                var success = await _router.StartAsync(endpoint, tcp, udp);
                BtnConnectToggle.IsEnabled = true;

                if (!success)
                {
                    MessageBox.Show("Failed to start router. Please ensure ProxyCord was launched as Administrator.", 
                                    "ProxyCord", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void StatusChanged(bool isRunning)
        {
            Dispatcher.Invoke(() =>
            {
                if (isRunning)
                {
                    // Show initial active status while probe checks upstream
                    TxtStatusLabel.Text = "Routing active";
                    TxtStatusLabel.Foreground = (SolidColorBrush)Resources["TextPrimary"];
                    DotStatus.Fill = (SolidColorBrush)Resources["StatusDotColor"];
                    StartUpstreamProbe();
                }
                else
                {
                    StopUpstreamProbe();
                    _lastReachable = false;
                    TxtStatusLabel.Text = "Disconnected";
                    TxtStatusLabel.Foreground = (SolidColorBrush)Resources["TextMuted"];
                    DotStatus.Fill = (SolidColorBrush)Resources["StatusDotColor"];
                    if (_notifyIcon != null) _notifyIcon.Text = "ProxyCord - Ready";
                    UpdateHeroVisualState(false, false, _settings.IsDarkMode);
                    UpdateSystemTray(false);
                }

                BtnConnectToggle.InvalidateVisual();
            });
        }

        private void StartUpstreamProbe()
        {
            _upstreamTimer?.Stop();
            _upstreamTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            _upstreamTimer.Tick += async (_, _) => await CheckUpstreamStatusAsync();
            _upstreamTimer.Start();

            _ = CheckUpstreamStatusAsync();
        }

        private void StopUpstreamProbe()
        {
            _upstreamTimer?.Stop();
            _upstreamTimer = null;
        }

        private async Task CheckUpstreamStatusAsync()
        {
            if (!_router.IsRunning) return;

            var reachable = await RouterService.CheckUpstreamReachableAsync(_settings.ProxyEndpoint);
            _lastReachable = reachable;

            Dispatcher.Invoke(() =>
            {
                if (!_router.IsRunning) return;

                if (reachable)
                {
                    TxtStatusLabel.Text = "Routing active • Upstream connected";
                    TxtStatusLabel.Foreground = (SolidColorBrush)Resources["VoiceGreenColor"];
                    DotStatus.Fill = (SolidColorBrush)Resources["VoiceGreenColor"];
                    if (_notifyIcon != null) _notifyIcon.Text = $"ProxyCord - Connected via {_settings.ProxyEndpoint}";
                }
                else
                {
                    TxtStatusLabel.Text = "Routing active • Upstream unreachable";
                    TxtStatusLabel.Foreground = (SolidColorBrush)Resources["WarningAmberBrush"];
                    DotStatus.Fill = (SolidColorBrush)Resources["WarningAmberBrush"];
                    if (_notifyIcon != null) _notifyIcon.Text = $"ProxyCord - Upstream unreachable ({_settings.ProxyEndpoint})";
                }

                UpdateHeroVisualState(true, reachable, _settings.IsDarkMode);
                UpdateSystemTray(true, reachable);
                BtnConnectToggle.InvalidateVisual();
            });
        }

        private void UpdateHeroVisualState(bool isRunning, bool isReachable, bool isDark)
        {
            if (isRunning)
            {
                if (isReachable)
                {
                    // Active Connected State (Vibrant Emerald Core + Soft Aura Rings)
                    var coreGrad = new LinearGradientBrush(
                        (Color)ColorConverter.ConvertFromString("#10B981"),
                        (Color)ColorConverter.ConvertFromString("#059669"),
                        new System.Windows.Point(0, 0),
                        new System.Windows.Point(0, 1));
                    Resources["HeroCoreBtnBg"] = coreGrad;
                    Resources["HeroCoreBtnBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
                    Resources["HeroPowerIconColor"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));

                    if (isDark)
                    {
                        Resources["HeroMiddleRingBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0E251B"));
                        Resources["HeroMiddleRingBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#143829"));
                        Resources["HeroOuterRingBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#091912"));
                        Resources["HeroOuterRingBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0E261C"));
                    }
                    else
                    {
                        Resources["HeroMiddleRingBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D1FAE5"));
                        Resources["HeroMiddleRingBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A7F3D0"));
                        Resources["HeroOuterRingBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5"));
                        Resources["HeroOuterRingBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C6F6D5"));
                    }
                }
                else
                {
                    // Upstream Unreachable Warning State (Warm Amber Core + Rings)
                    var coreGrad = new LinearGradientBrush(
                        (Color)ColorConverter.ConvertFromString("#F59E0B"),
                        (Color)ColorConverter.ConvertFromString("#D97706"),
                        new System.Windows.Point(0, 0),
                        new System.Windows.Point(0, 1));
                    Resources["HeroCoreBtnBg"] = coreGrad;
                    Resources["HeroCoreBtnBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24"));
                    Resources["HeroPowerIconColor"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));

                    if (isDark)
                    {
                        Resources["HeroMiddleRingBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#241B0D"));
                        Resources["HeroMiddleRingBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A2A14"));
                        Resources["HeroOuterRingBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#181209"));
                        Resources["HeroOuterRingBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#291E0E"));
                    }
                    else
                    {
                        Resources["HeroMiddleRingBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF3C7"));
                        Resources["HeroMiddleRingBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDE68A"));
                        Resources["HeroOuterRingBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFBEB"));
                        Resources["HeroOuterRingBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF08A"));
                    }
                }
            }
            else
            {
                // Disconnected / Ready State (Cloudflare WARP Inspired: Electric Cobalt Core + Subtle Rings)
                var coreGrad = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString("#2563EB"),
                    (Color)ColorConverter.ConvertFromString("#1D4ED8"),
                    new System.Windows.Point(0, 0),
                    new System.Windows.Point(0, 1));
                Resources["HeroCoreBtnBg"] = coreGrad;
                Resources["HeroCoreBtnBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
                Resources["HeroPowerIconColor"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));

                if (isDark)
                {
                    Resources["HeroMiddleRingBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111827"));
                    Resources["HeroMiddleRingBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A243A"));
                    Resources["HeroOuterRingBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0B0F19"));
                    Resources["HeroOuterRingBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#121929"));
                }
                else
                {
                    Resources["HeroMiddleRingBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0EBFE"));
                    Resources["HeroMiddleRingBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BFD7FE"));
                    Resources["HeroOuterRingBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EEF4FF"));
                    Resources["HeroOuterRingBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DCE8FD"));
                }
            }
        }

        private async void Toggle_Click(object sender, RoutedEventArgs e)
        {
            _settings.RouteTcp = ChkRouteTcp.IsChecked ?? true;
            _settings.RouteUdp = ChkRouteUdp.IsChecked ?? true;
            _settings.Save();

            if (_router.IsRunning)
            {
                await _router.UpdateTogglesLiveAsync(_settings.ProxyEndpoint, _settings.RouteTcp, _settings.RouteUdp);
            }
        }

        private void TxtProxyEndpoint_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveProxyEndpoint();
        }

        private void TxtProxyEndpoint_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SaveProxyEndpoint();
                Keyboard.ClearFocus();
            }
        }

        private void SaveProxyEndpoint()
        {
            var text = TxtProxyEndpoint.Text.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                _settings.ProxyEndpoint = text;
                _settings.Save();
                TxtHeaderEndpoint.Text = $"• {_settings.ProxyEndpoint}";
            }
            else
            {
                TxtProxyEndpoint.Text = _settings.ProxyEndpoint;
            }
        }

        private void ThemeSwitch_Click(object sender, RoutedEventArgs e)
        {
            _settings.IsDarkMode = RadioThemeDark.IsChecked == true;
            _settings.Save();
            ApplyTheme(_settings.IsDarkMode);
        }

        private void ApplyTheme(bool dark)
        {
            if (dark)
            {
                // Deep Obsidian / Titanium Dark Theme (Cloudflare WARP & Linear aesthetic)
                Resources["WindowBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0A0C11"));
                Resources["WindowBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#181D29"));
                Resources["SidebarBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#06070B"));
                Resources["CardSurface"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F131D"));
                Resources["CardHoverBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#263147"));
                Resources["BorderMuted"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#192030"));
                Resources["TextPrimary"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
                Resources["TextMuted"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                Resources["AccentColor"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
                Resources["VoiceGreenColor"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                Resources["VoiceGreenBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
                Resources["StatusDotColor"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
                Resources["NavIconIdle"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
                Resources["NavHoverBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#131826"));
                Resources["NavActiveBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#172238"));
                Resources["PillBgOn"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#131B2D"));
                Resources["PillBorderOn"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
                Resources["PillBgOff"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F131D"));
                Resources["PillBorderOff"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#192030"));
                Resources["PillDotOff"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                Resources["InputBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0B0E16"));
                Resources["LogBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#07090E"));
                Resources["LogText"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                Resources["ScrollbarThumbBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E273A"));
                Resources["SegmentContainerBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0B0E16"));
                Resources["SegmentActiveBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A2234"));
                Resources["SegmentActiveFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
            }
            else
            {
                // Crisp Modern Light Theme (Clean Studio Slate)
                Resources["WindowBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
                Resources["WindowBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                Resources["SidebarBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"));
                Resources["CardSurface"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                Resources["CardHoverBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));
                Resources["BorderMuted"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                Resources["TextPrimary"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
                Resources["TextMuted"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
                Resources["AccentColor"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
                Resources["VoiceGreenColor"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                Resources["VoiceGreenBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
                Resources["StatusDotColor"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                Resources["NavIconIdle"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                Resources["NavHoverBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                Resources["NavActiveBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE"));
                Resources["PillBgOn"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFF6FF"));
                Resources["PillBorderOn"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
                Resources["PillBgOff"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                Resources["PillBorderOff"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                Resources["PillDotOff"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));
                Resources["InputBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                Resources["LogBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"));
                Resources["LogText"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
                Resources["ScrollbarThumbBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));
                Resources["SegmentContainerBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                Resources["SegmentActiveBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                Resources["SegmentActiveFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
            }

            Resources["WarningAmberBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));

            UpdateHeroVisualState(_router.IsRunning, _lastReachable, dark);
            BtnConnectToggle.InvalidateVisual();
        }

        private void LogReceived(string line)
        {
            Dispatcher.Invoke(() =>
            {
                TxtLogs.AppendText(line + Environment.NewLine);
                LogScrollViewer.ScrollToEnd();
            });
        }

        private void BtnCopyLogs_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(TxtLogs.Text))
            {
                Clipboard.SetText(TxtLogs.Text);
                var btn = (System.Windows.Controls.Button)sender;
                var oldContent = btn.Content;
                btn.Content = "Copied";
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                timer.Tick += (_, _) =>
                {
                    btn.Content = oldContent;
                    timer.Stop();
                };
                timer.Start();
            }
        }

        private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
        {
            TxtLogs.Clear();
        }

        protected override void OnClosed(EventArgs e)
        {
            ShutdownApp();
            base.OnClosed(e);
        }
    }
}
