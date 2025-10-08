using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

using Microsoft.Web.WebView2.Core;

using Vekotin.Bridges;

namespace Vekotin
{
    public partial class WidgetWindow : Window
    {
        private string widgetPath;
        private WidgetManifest manifest;
        private CoreWebView2Environment _webViewEnvironment;

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x80;

        public WidgetWindow(string widgetPath, WidgetManifest manifest)
        {
            this.widgetPath = widgetPath;
            this.manifest = manifest;
            InitializeComponent();
            InitializeWidget();
        }

        private void InitializeWidget()
        {
            this.Title = manifest.Name;
            this.Width = manifest.Width;
            this.Height = manifest.Height;
            this.Left = 100;
            this.Top = 100;

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

                // Clean browsing data
                await WebView.CoreWebView2.Profile.ClearBrowsingDataAsync();

                // Remove bridges
                WebView.CoreWebView2.RemoveHostObjectFromScript("cpu");
            }

            WebView?.Dispose();

            WidgetBorder.Child = null;
            WebView = null;
            _webViewEnvironment = null;
        }

        protected override async void OnClosing(CancelEventArgs e)
        {
            await RemoveWebView();
            base.OnClosing(e);
        }
    }
}
