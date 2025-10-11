
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using Wpf.Ui.Controls;

using MessageBox = System.Windows.MessageBox;

namespace Vekotin
{
    public partial class ControlPanel : FluentWindow
    {
        private NotifyIcon trayIcon;
        private List<WidgetWindow> activeWidgets = new List<WidgetWindow>();
        public ObservableCollection<WidgetListItem> AvailableWidgets { get; set; }

        public ControlPanel()
        {
            InitializeComponent();
            AvailableWidgets = new ObservableCollection<WidgetListItem>();
            DataContext = this;
            SetupTrayIcon();
            LoadAvailableWidgets();
        }
        
        private void SetupTrayIcon()
        {
            trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "Widget System"
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Show Control Panel", null, (s, e) =>
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
            });
            contextMenu.Items.Add("Exit", null, (s, e) =>
            {
                System.Windows.Application.Current.Shutdown();
            });

            trayIcon.ContextMenuStrip = contextMenu;
            trayIcon.DoubleClick += (s, e) =>
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
            };
        }
        

        private void LoadAvailableWidgets()
        {
            string widgetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "widgets");
            if (!Directory.Exists(widgetsPath))
            {
                Directory.CreateDirectory(widgetsPath);
            }

            AvailableWidgets.Clear();
            foreach (var dir in Directory.GetDirectories(widgetsPath))
            {
                string manifestPath = Path.Combine(dir, "widget.json");
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        var manifest = JsonSerializer.Deserialize<WidgetManifest>(
                            File.ReadAllText(manifestPath));
                        AvailableWidgets.Add(new WidgetListItem
                        {
                            Name = manifest.Name,
                            Path = dir,
                            Manifest = manifest
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error loading widget: {ex.Message}");
                    }
                }
            }
        }

        private void LoadWidget_Click(object sender, RoutedEventArgs e)
        {
            var selected = WidgetListBox.SelectedItem as WidgetListItem;
            if (selected != null)
            {
                var widget = new WidgetWindow(selected.Path, selected.Manifest);
                activeWidgets.Add(widget);
                widget.Closed += (s, ev) => activeWidgets.Remove(widget);
                widget.Show();
            }
            else
            {
                MessageBox.Show("Please select a widget to load.", "No Widget Selected",
                    System.Windows.MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CloseAllWidgets_Click(object sender, RoutedEventArgs e)
        {
            foreach (var widget in activeWidgets.ToList())
            {
                widget.Close();
            }
        }

        private void RefreshWidgets_Click(object sender, RoutedEventArgs e)
        {
            LoadAvailableWidgets();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        protected override void OnClosed(EventArgs e)
        {
            //trayIcon?.Dispose();
            foreach (var widget in activeWidgets.ToList())
            {
                widget.Close();
            }
            base.OnClosed(e);
        }
    }

    public class WidgetListItem
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public WidgetManifest Manifest { get; set; }
    }

    public class WidgetManifest
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
