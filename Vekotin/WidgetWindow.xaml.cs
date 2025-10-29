using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

using Microsoft.Web.WebView2.Core;

using Vekotin.Services;
using Vekotin.Bridges;

namespace Vekotin
{
    public partial class WidgetWindow : Window
    {
        private readonly ConfigurationManager _configManager;
        private readonly WidgetManifest _manifest;
        private CoreWebView2Environment? _webViewEnvironment;
        private List<IDisposable> _bridgeReferences = new();

        private bool _isDevToolsOpen = false;
        private bool _isClosing = false;

        // Snap settings
        private int _snapDistance = 20;    // Pixels from edge to trigger snap
        private int _snapMargin = 0;       // Final distance from edge when snapped

        public string WidgetPath { get; }

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const uint WM_CLOSE = 0x0010;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_TRANSPARENT = 0x20;

        public WidgetWindow(string widgetPath, WidgetManifest manifest, ConfigurationManager configManager)
        {
            if (string.IsNullOrWhiteSpace(widgetPath))
                throw new ArgumentException("Widget path cannot be null or empty", nameof(widgetPath));
            if (!Directory.Exists(widgetPath))
                throw new DirectoryNotFoundException($"Widget path not found: {widgetPath}");

            WidgetPath = widgetPath;
            this._manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            this._configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));

            InitializeComponent();
            InitializeWidget();
        }

        private void InitializeWidget()
        {
            Title = _manifest.Name ?? "Widget";
            Width = _manifest.Width;
            Height = _manifest.Height;

            var widgetName = Path.GetFileName(WidgetPath);
            var widgetConfig = _configManager.GetWidgetConfig(widgetName);

            if (widgetConfig != null && widgetConfig.SavePosition == true)
            {
                Left = widgetConfig.WindowX;
                Top = widgetConfig.WindowY;
            }
            else
            {
                Left = 100;
                Top = 100;
            }

            InitializeWebView();

            // Set up drag handling if draggable
            UpdateDragHandlers();

            // Keep on screen if enabled
            if (widgetConfig?.KeepOnScreen == true)
            {
                ConstrainToScreen();
            }

            // Listen for configuration changes
            _configManager.ConfigChanged += OnConfigurationChanged;
        }

        /// <summary>
        /// Handles configuration changes from the control panel.
        /// </summary>
        private void OnConfigurationChanged(object? sender, ConfigChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var widgetName = Path.GetFileName(WidgetPath);
                var widgetConfig = _configManager.GetWidgetConfig(widgetName);

                WebView.CoreWebView2.Settings.IsNonClientRegionSupportEnabled = widgetConfig?.Draggable ?? true;
                WebView.Reload();

                UpdateDragHandlers();
                ApplyWindowStyles();
            });
        }

        /// <summary>
        /// Enables or disables drag handlers based on the Draggable setting.
        /// </summary>
        private void UpdateDragHandlers()
        {
            var widgetName = Path.GetFileName(WidgetPath);
            var widgetConfig = _configManager.GetWidgetConfig(widgetName);

            // Remove existing handlers first to avoid duplicates
            LocationChanged -= OnWidgetLocationChanged;

            // Add handlers
            if (widgetConfig?.SnapToEdges == true || widgetConfig?.KeepOnScreen == true)
            {
                LocationChanged += OnWidgetLocationChanged;
            }
        }

        private void OnWidgetLocationChanged(object? sender, EventArgs e)
        {
            var widgetName = Path.GetFileName(WidgetPath);
            var widgetConfig = _configManager.GetWidgetConfig(widgetName);

            // Apply snap to edges after dragging
            if (widgetConfig?.SnapToEdges == true)
            {
                SnapToNearestEdge();
            }

            // Keep on screen after dragging
            if (widgetConfig?.KeepOnScreen == true)
            {
                ConstrainToScreen();
            }
        }

        /// <summary>
        /// Gets the working area of the screen containing this window using Win32 API.
        /// </summary>
        private Rect GetCurrentScreenWorkingArea()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

            if (monitor != IntPtr.Zero)
            {
                MONITORINFO monitorInfo = new MONITORINFO();
                monitorInfo.cbSize = (uint)Marshal.SizeOf(monitorInfo);

                if (GetMonitorInfo(monitor, ref monitorInfo))
                {
                    var workArea = monitorInfo.rcWork;
                    return new Rect(
                        workArea.Left,
                        workArea.Top,
                        workArea.Right - workArea.Left,
                        workArea.Bottom - workArea.Top);
                }
            }

            // Fallback to primary screen
            return SystemParameters.WorkArea;
        }

        /// <summary>
        /// Snaps the window to the nearest screen edge if within snap distance.
        /// </summary>
        private void SnapToNearestEdge()
        {
            var workingArea = GetCurrentScreenWorkingArea();

            double left = Left;
            double top = Top;
            double right = Left + Width;
            double bottom = Top + Height;

            // Check distance from each edge
            double distanceLeft = Math.Abs(left - workingArea.Left);
            double distanceTop = Math.Abs(top - workingArea.Top);
            double distanceRight = Math.Abs(right - workingArea.Right);
            double distanceBottom = Math.Abs(bottom - workingArea.Bottom);

            // Find minimum distance
            double minDistance = Math.Min(Math.Min(distanceLeft, distanceTop),
                                         Math.Min(distanceRight, distanceBottom));

            // Snap if within threshold
            if (minDistance <= _snapDistance)
            {
                if (minDistance == distanceLeft)
                {
                    Left = workingArea.Left + _snapMargin;
                }
                else if (minDistance == distanceTop)
                {
                    Top = workingArea.Top + _snapMargin;
                }
                else if (minDistance == distanceRight)
                {
                    Left = workingArea.Right - Width - _snapMargin;
                }
                else if (minDistance == distanceBottom)
                {
                    Top = workingArea.Bottom - Height - _snapMargin;
                }
            }
        }

        /// <summary>
        /// Constrains the window to stay within the screen bounds.
        /// </summary>
        private void ConstrainToScreen()
        {
            var workingArea = GetCurrentScreenWorkingArea();

            double left = Left;
            double top = Top;

            // Constrain horizontal position
            if (left < workingArea.Left)
            {
                left = workingArea.Left;
            }
            else if (left + Width > workingArea.Right)
            {
                left = workingArea.Right - Width;
            }

            // Constrain vertical position
            if (top < workingArea.Top)
            {
                top = workingArea.Top;
            }
            else if (top + Height > workingArea.Bottom)
            {
                top = workingArea.Bottom - Height;
            }

            Left = left;
            Top = top;
        }

        private async void InitializeWebView()
        {
            try
            {
                string userDataFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Constants.AppName);

                var widgetName = Path.GetFileName(WidgetPath);
                var widgetConfig = _configManager.GetWidgetConfig(widgetName);

                Directory.CreateDirectory(userDataFolderPath);

                _webViewEnvironment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolderPath,
                    browserExecutableFolder: null,
                    options: new CoreWebView2EnvironmentOptions{}
                );

                await WebView.EnsureCoreWebView2Async(_webViewEnvironment);

                WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                WebView.CoreWebView2.Settings.IsNonClientRegionSupportEnabled = widgetConfig?.Draggable ?? true;

                // Initialize and add bridges
                if (_manifest.Bridges != null)
                {
                    foreach (var bridge in _manifest.Bridges)
                    {
                        switch (bridge.ToLower())
                        {
                            case "cpu":
                                CpuBridge cpuBridge = new CpuBridge();
                                WebView.CoreWebView2.AddHostObjectToScript($"{bridge.ToLower()}", cpuBridge);
                                _bridgeReferences.Add(cpuBridge);
                                break;
                            case "ram":
                                RamBridge ramBridge = new RamBridge();
                                WebView.CoreWebView2.AddHostObjectToScript($"{bridge.ToLower()}", ramBridge);
                                _bridgeReferences.Add(ramBridge);
                                break;
                            case "disk":
                                DiskBridge diskBridge = new DiskBridge();
                                WebView.CoreWebView2.AddHostObjectToScript($"{bridge.ToLower()}", diskBridge);
                                _bridgeReferences.Add(diskBridge);
                                break;
                        }
                    }
                }

                // Navigate to widget
                string htmlPath = Path.Combine(WidgetPath, "index.html");
                if (File.Exists(htmlPath))
                {
                    WebView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
                }
                else
                {
                    ShowErrorPage("Widget Not Found", "index.html is missing");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error initializing WebView2: {ex.Message}\n\nMake sure WebView2 Runtime is installed.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Close();
            }
        }

        private void ShowErrorPage(string title, string message)
        {
            WebView.NavigateToString(
                $"<html><body style='display:flex;justify-content:center;align-items:center;" +
                $"height:100vh;margin:0;font-family:sans-serif;background:#1e1e1e;color:#fff;'>" +
                $"<div style='text-align:center;'><h1>{title}</h1><p>{message}</p></div>" +
                "</body></html>");
        }

        public void OpenDevTools()
        {
            if (WebView?.CoreWebView2 != null && !_isDevToolsOpen)
            {
                WebView.CoreWebView2.OpenDevToolsWindow();
                _isDevToolsOpen = true;
            }
        }

        private void CloseDevTools()
        {
            var devToolsProcess = GetDevToolsProcess();
            if (devToolsProcess == null) return;

            try
            {
                IntPtr handle = devToolsProcess.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    SetForegroundWindow(handle);
                    PostMessage(handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
                _isDevToolsOpen = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error closing DevTools: {ex.Message}");
            }
        }

        private Process? GetDevToolsProcess()
        {
            string? webViewUrl = WebView?.Source?.ToString();
            if (string.IsNullOrEmpty(webViewUrl)) return null;

            string urlPart = new Uri(webViewUrl).Host;

            return Process.GetProcesses()
                .FirstOrDefault(p => !string.IsNullOrEmpty(p.MainWindowTitle) &&
                                     p.MainWindowTitle.Contains("DevTools") &&
                                     p.MainWindowTitle.Contains(urlPart));
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyWindowStyles();
        }

        private void ApplyWindowStyles()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

            // Always set as a tool window to hide from Alt+Tab
            exStyle |= WS_EX_TOOLWINDOW;

            var widgetName = Path.GetFileName(WidgetPath);
            var widgetConfig = _configManager.GetWidgetConfig(widgetName);

            if (widgetConfig?.ClickThrough == true)
            {
                exStyle |= WS_EX_TRANSPARENT;
            }
            else
            {
                exStyle &= ~WS_EX_TRANSPARENT;
            }

            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
        }

        private async Task CleanupWebView()
        {
            if (WebView?.CoreWebView2 != null)
            {
                // Remove bridges
                if (_manifest.Bridges != null)
                {
                    foreach (var bridge in _manifest.Bridges)
                    {
                        WebView.CoreWebView2.RemoveHostObjectFromScript($"{bridge.ToLower()}");
                    }
                }

                // Clean browsing data
                try
                {
                    await WebView.CoreWebView2.Profile.ClearBrowsingDataAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error clearing browsing data: {ex.Message}");
                }
            }

            foreach (var bridge in _bridgeReferences)
            {
                bridge?.Dispose();
            }
            _bridgeReferences.Clear();

            _webViewEnvironment = null;

            WebView?.Dispose();
            if (WidgetBorder != null)
            {
                WidgetBorder.Child = null;
            }
        }

        protected override async void OnClosing(CancelEventArgs e)
        {
            if (_isClosing)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            _isClosing = true;

            try
            {
                // Unsubscribe from config changes
                _configManager.ConfigChanged -= OnConfigurationChanged;

                // Update config
                var widgetName = Path.GetFileName(WidgetPath);
                _configManager.UpdateWidgetConfig(widgetName, widgetConfig =>
                {
                    widgetConfig.Active = false;

                    if (widgetConfig.SavePosition == true)
                    {
                        widgetConfig.WindowX = (int)Left;
                        widgetConfig.WindowY = (int)Top;
                    }
                });

                _configManager.Save();

                // Close DevTools if open
                if (_isDevToolsOpen)
                {
                    CloseDevTools();
                }

                // Cleanup WebView
                await CleanupWebView();

                // Actually close the window
                Dispatcher.BeginInvoke(new Action(Close));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during window close: {ex.Message}");
                // Force close even if there's an error
                Dispatcher.BeginInvoke(new Action(Close));
            }
        }
    }
}
