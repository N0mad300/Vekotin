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

        private const uint WM_CLOSE = 0x0010;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x80;

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

            InitializeWebView();
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

            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
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
