using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

using Microsoft.Web.WebView2.Core;

using Vekotin.Bridges;

namespace Vekotin
{
    public partial class WidgetWindow : Window
    {
        private string widgetPath;
        private WidgetManifest manifest;
        private bool isClickThrough = false;

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
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
                await WebView.EnsureCoreWebView2Async(null);

                WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                // Bridges
                WebView.CoreWebView2.AddHostObjectToScript("cpu", new CpuBridge());
                WebView.CoreWebView2.AddHostObjectToScript("ram", new RamBridge());

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

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!isClickThrough)
            {
                this.DragMove();
            }
        }

        private void ToggleClickThrough_Click(object sender, RoutedEventArgs e)
        {
            isClickThrough = !isClickThrough;

            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

            if (isClickThrough)
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
                ClickThroughMenuItem.Header = "✓ Click-Through Enabled";
            }
            else
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
                ClickThroughMenuItem.Header = "Enable Click-Through";
            }
        }

        private void CloseWidget_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void OpenDevTools_Click(object sender, RoutedEventArgs e)
        {
            if (WebView?.CoreWebView2 != null)
            {
                WebView.CoreWebView2.OpenDevToolsWindow();
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
        }
    }
}
