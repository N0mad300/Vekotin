using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;

using Microsoft.Web.WebView2.Core;
using Vekotin.Bridges;

namespace Vekotin
{
    public partial class WidgetWindow : Window
    {
        private Config config;
        private string configPath;
        private string widgetPath;
        private WidgetManifest manifest;
        private CoreWebView2Environment? _webViewEnvironment;

        private bool isWebViewCleanedUp = false;

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x80;

        public WidgetWindow(string widgetPath, WidgetManifest manifest, Config config, string configPath)
        {
            this.widgetPath = widgetPath;
            this.manifest = manifest;
            this.config = config;
            this.configPath = configPath;

            InitializeComponent();
            InitializeWidget();
        }

        private void InitializeWidget()
        {
            this.Title = manifest.Name;
            this.Width = manifest.Width;
            this.Height = manifest.Height;

            config.Widgets.TryGetValue(Path.GetFileName(widgetPath), out var widgetConfig);
            this.Left = widgetConfig?.WindowX ?? 100;
            this.Top = widgetConfig?.WindowY ?? 100;

            InitializeWebView();
        }

        private async void InitializeWebView()
        {
            try
            {
                string userDataFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Vekotin");
                Directory.CreateDirectory(userDataFolderPath);

                var _webViewEnvironment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolderPath,
                    browserExecutableFolder: null,
                    options: new CoreWebView2EnvironmentOptions
                    {
                        AdditionalBrowserArguments = "--enable-features=msWebView2EnableDraggableRegions"
                    });

                await WebView.EnsureCoreWebView2Async(_webViewEnvironment);

                WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                // Bridges
                WebView.CoreWebView2.AddHostObjectToScript("cpu", new CpuBridge());

                string htmlPath = Path.Combine(widgetPath, "index.html");
                if (File.Exists(htmlPath))
                {
                    WebView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
                }
                else
                {
                    WebView.NavigateToString(
                        "<html><body style='display:flex;justify-content:center;align-items:center;height:100vh;margin:0;font-family:sans-serif;'>" +
                        "<div style='text-align:center;'><h1>Widget Not Found</h1><p>index.html is missing</p></div>" +
                        "</body></html>");
                }

                WebView.CoreWebView2.WebMessageReceived += WebView_WebMessageReceived;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error initializing WebView2: {ex.Message}\n\nMake sure WebView2 Runtime is installed.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                this.Close();
            }
        }

        private void WebView_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string message = e.TryGetWebMessageAsString();
            System.Windows.MessageBox.Show($"Widget message: {message}", "Message from Widget");
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
        }

        public async Task RemoveWebView()
        {
            if (WebView?.CoreWebView2 != null)
            {
                // Unsubscribe from all events first
                WebView.CoreWebView2.WebMessageReceived -= WebView_WebMessageReceived;

                // Remove bridges
                WebView.CoreWebView2.RemoveHostObjectFromScript("cpu");

                // Clean browsing data
                await WebView.CoreWebView2.Profile.ClearBrowsingDataAsync();
            }
            _webViewEnvironment = null;

            WebView?.Dispose();

            WidgetBorder.Child = null;
            WebView = null;
        }

        protected override async void OnClosing(CancelEventArgs e)
        {
            if (config.Widgets.TryGetValue($"{Path.GetFileName(widgetPath)}", out WidgetConfig? widgetConfig))
            {
                widgetConfig.Active = false;
                if (widgetConfig.SavePosition == true)
                {
                    widgetConfig.WindowY = Convert.ToInt32(this.Top);
                    widgetConfig.WindowX = Convert.ToInt32(this.Left);
                }
            }

            // Update JSON config file
            string jsonString = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, jsonString);

            if (!isWebViewCleanedUp)
            {
                // Cancel the close event
                e.Cancel = true;

                // Do cleanup asynchronously
                await RemoveWebView();
                isWebViewCleanedUp = true;

                // Now actually close the window
                Close();
                return;
            }

            base.OnClosing(e);
        }
    }
}
