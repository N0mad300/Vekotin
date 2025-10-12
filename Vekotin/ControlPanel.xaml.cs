
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
        static Config config = new Config();

        public ControlPanel()
        {
            InitializeComponent();
            AvailableWidgets = new ObservableCollection<WidgetListItem>();
            DataContext = this;
            LoadConfig();
            WatchConfig();
            SetupTrayIcon();
            LoadAvailableWidgets();
        }

        private void LoadConfig()
        {
            string appDataFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vekotin");
            string configPath = Path.Combine(appDataFolderPath, "vekotin.json");

            if (!File.Exists(configPath))
            {
                Directory.CreateDirectory(appDataFolderPath);

                var config = new Config
                {
                    Vekotin = new VekotinConfig { WidgetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Vekotin", "Widgets") },
                    Widgets = new Dictionary<string, WidgetConfig>()
                };

                string jsonString = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(configPath, jsonString);
            }

            var configJson = File.ReadAllText(configPath);
            config = JsonSerializer.Deserialize<Config>(configJson);
        }

        private void WatchConfig()
        {
            string appDataFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vekotin");
            string configPath = Path.Combine(appDataFolderPath, "vekotin.json");

            var watcher = new FileSystemWatcher(Path.GetDirectoryName(configPath) ?? appDataFolderPath, Path.GetFileName(configPath));
            watcher.NotifyFilter = NotifyFilters.LastWrite;
            watcher.Changed += (s, e) =>
            {
                try
                {
                    // Delay slightly to avoid file lock issues
                    Thread.Sleep(200);
                    LoadConfig();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reloading config: {ex.Message}");
                }
            };
            watcher.EnableRaisingEvents = true;
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
            string widgetsPath = config.Vekotin.WidgetPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Vekotin", "Widgets");
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

    public class Config
    {
        public VekotinConfig Vekotin { get; set; } = new();
        public Dictionary<string, WidgetConfig> Widgets { get; set; } = new();
    }

    public class VekotinConfig
    {
        public string? WidgetPath { get; set; }
    }

    public class WidgetConfig
    {
        public bool Active { get; set; }
        public int WindowX { get; set; }
        public int WindowY { get; set; }
        public bool? Draggable { get; set; }
        public bool? ClickThrough { get; set; }
        public bool? SnapEdges { get; set; }
        public bool? AlwaysOnTop { get; set; }
    }

    public class WidgetListItem
    {
        public string? Name { get; set; }
        public string Path { get; set; }
        public WidgetManifest Manifest { get; set; }
    }

    public class WidgetManifest
    {
        public string? Name { get; set; }
        public string? Author { get; set; }
        public string? Version { get; set; }
        public string? License { get; set; }
        public string? Description { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
