using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;

using Microsoft.Web.WebView2.Core;

using Vekotin.Services;
using Vekotin.Bridges;

namespace Vekotin
{
    public partial class WidgetWindow : Window
    {
        private readonly ConfigurationManager configManager;
        private readonly WidgetManifest manifest;
        private CoreWebView2Environment? webViewEnvironment;
        private CpuBridge? cpuBridge;

        private bool isDevToolsOpen = false;
        private bool isClosing = false;

        // Snap settings
        private const int SnapDistance = 20; // Pixels from edge to trigger snap
        private const int SnapMargin = 0; // Final distance from edge when snapped

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
            this.manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            this.configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));

            InitializeComponent();
            InitializeWidget();
        }

        private void InitializeWidget()
        {
            Title = manifest.Name ?? "Widget";
            Width = manifest.Width;
            Height = manifest.Height;

            var widgetName = Path.GetFileName(WidgetPath);
            var widgetConfig = configManager.GetWidgetConfig(widgetName);

            this.Left = widgetConfig?.WindowX ?? 100;
            this.Top = widgetConfig?.WindowY ?? 100;

            // Set up drag handling if draggable
            UpdateDragHandlers();

            // Keep on screen if enabled
            if (widgetConfig?.KeepOnScreen == true)
            {
                ConstrainToScreen();
            }

            // Listen for configuration changes
            configManager.ConfigChanged += OnConfigurationChanged;

            InitializeWebView();
        }

        /// <summary>
        /// Handles configuration changes from the control panel.
        /// </summary>
        private void OnConfigurationChanged(object? sender, ConfigChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
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
            var widgetConfig = configManager.GetWidgetConfig(widgetName);

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
            var widgetConfig = configManager.GetWidgetConfig(widgetName);

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

            double left = this.Left;
            double top = this.Top;
            double right = this.Left + this.Width;
            double bottom = this.Top + this.Height;

            // Check distance from each edge
            double distanceLeft = Math.Abs(left - workingArea.Left);
            double distanceTop = Math.Abs(top - workingArea.Top);
            double distanceRight = Math.Abs(right - workingArea.Right);
            double distanceBottom = Math.Abs(bottom - workingArea.Bottom);

            // Find minimum distance
            double minDistance = Math.Min(Math.Min(distanceLeft, distanceTop),
                                         Math.Min(distanceRight, distanceBottom));

            // Snap if within threshold
            if (minDistance <= SnapDistance)
            {
                if (minDistance == distanceLeft)
                {
                    this.Left = workingArea.Left + SnapMargin;
                }
                else if (minDistance == distanceTop)
                {
                    this.Top = workingArea.Top + SnapMargin;
                }
                else if (minDistance == distanceRight)
                {
                    this.Left = workingArea.Right - this.Width - SnapMargin;
                }
                else if (minDistance == distanceBottom)
                {
                    this.Top = workingArea.Bottom - this.Height - SnapMargin;
                }
            }
        }

        /// <summary>
        /// Constrains the window to stay within the screen bounds.
        /// </summary>
        private void ConstrainToScreen()
        {
            var workingArea = GetCurrentScreenWorkingArea();

            double left = this.Left;
            double top = this.Top;

            // Constrain horizontal position
            if (left < workingArea.Left)
            {
                left = workingArea.Left;
            }
            else if (left + this.Width > workingArea.Right)
            {
                left = workingArea.Right - this.Width;
            }

            // Constrain vertical position
            if (top < workingArea.Top)
            {
                top = workingArea.Top;
            }
            else if (top + this.Height > workingArea.Bottom)
            {
                top = workingArea.Bottom - this.Height;
            }

            this.Left = left;
            this.Top = top;
        }

        private async void InitializeWebView()
        {
            try
            {
                string userDataFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Constants.AppName);

                Directory.CreateDirectory(userDataFolderPath);

                webViewEnvironment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolderPath,
                    browserExecutableFolder: null,
                    options: new CoreWebView2EnvironmentOptions
                    {
                        AdditionalBrowserArguments = "--enable-features=msWebView2EnableDraggableRegions"
                    });

                await WebView.EnsureCoreWebView2Async(webViewEnvironment);

                WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                // Initialize and add bridges
                cpuBridge = new CpuBridge();
                WebView.CoreWebView2.AddHostObjectToScript("cpu", cpuBridge);

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
            if (WebView?.CoreWebView2 != null && !isDevToolsOpen)
            {
                WebView.CoreWebView2.OpenDevToolsWindow();
                isDevToolsOpen = true;
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
                isDevToolsOpen = false;
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
            var widgetConfig = configManager.GetWidgetConfig(widgetName);

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
                WebView.CoreWebView2.RemoveHostObjectFromScript("cpu");

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

            cpuBridge?.Dispose();
            cpuBridge = null;

            webViewEnvironment = null;

            WebView?.Dispose();
            if (WidgetBorder != null)
            {
                WidgetBorder.Child = null;
            }
        }

        protected override async void OnClosing(CancelEventArgs e)
        {
            if (isClosing)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            isClosing = true;

            try
            {
                // Unsubscribe from config changes
                configManager.ConfigChanged -= OnConfigurationChanged;

                // Update config
                var widgetName = Path.GetFileName(WidgetPath);
                configManager.UpdateWidgetConfig(widgetName, widgetConfig =>
                {
                    widgetConfig.Active = false;

                    if (widgetConfig.SavePosition == true)
                    {
                        widgetConfig.WindowX = (int)Left;
                        widgetConfig.WindowY = (int)Top;
                    }
                });

                configManager.Save();

                // Close DevTools if open
                if (isDevToolsOpen)
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
