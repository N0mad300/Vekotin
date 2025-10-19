
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace Vekotin
{
    public partial class ControlPanel : FluentWindow
    {
        private List<WidgetWindow> activeWidgets = new List<WidgetWindow>();
        public ObservableCollection<WidgetListItem> AvailableWidgets { get; set; }
        private string appDataFolderPath;
        private string configPath;
        private Config config = new Config();
        private FileSystemWatcher? configWatcher;

        public ControlPanel()
        {
            InitializeComponent();

            AvailableWidgets = new ObservableCollection<WidgetListItem>();
            DataContext = this;

            appDataFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vekotin");
            configPath = Path.Combine(appDataFolderPath, "vekotin.json");

            LoadConfig();
            WatchConfig();
            LoadAvailableWidgets();
        }

        private void LoadConfig()
        {
            if (!File.Exists(configPath))
            {
                Directory.CreateDirectory(appDataFolderPath);

                var config = new Config
                {
                    Vekotin = new VekotinConfig { WidgetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Vekotin", "Widgets") },
                    Widgets = new Dictionary<string, WidgetConfig>()
                };

                SaveConfig();
            }

            var configJson = File.ReadAllText(configPath);
            config = JsonSerializer.Deserialize<Config>(configJson);
        }

        private void WatchConfig()
        {
            configWatcher = new FileSystemWatcher(Path.GetDirectoryName(configPath) ?? appDataFolderPath, Path.GetFileName(configPath));
            configWatcher.NotifyFilter = NotifyFilters.LastWrite;
            configWatcher.Changed += (s, e) =>
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
            configWatcher.EnableRaisingEvents = true;
        }

        private void SaveConfig()
        {
            try
            {
                string jsonString = JsonSerializer.Serialize(config,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, jsonString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save config: {ex.Message}");
            }
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

        private void WidgetListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var listBox = sender as ListBox;
            var selected = listBox?.SelectedItem as WidgetListItem;

            if (selected != null)
            {
                OnWidgetSelected(selected);
            }
        }

        private void OnWidgetSelected(WidgetListItem selected)
        {
            if (config.Widgets.TryGetValue(Path.GetFileName(selected.Path), out WidgetConfig? widgetConfig))
            {
                DraggableToggleSwitch.IsChecked = widgetConfig.Draggable;
                ClickThroughToggleSwitch.IsChecked = widgetConfig.ClickThrough;
                KeepOnScreenToggleSwitch.IsChecked = widgetConfig.KeepOnScreen;
                SavePositionToggleSwitch.IsChecked = widgetConfig.SavePosition;
                SnapToEdgesToggleSwitch.IsChecked = widgetConfig.SnapToEdges;
            }
            else
            {
                DraggableToggleSwitch.IsChecked = false;
                ClickThroughToggleSwitch.IsChecked = false;
                KeepOnScreenToggleSwitch.IsChecked = false;
                SavePositionToggleSwitch.IsChecked = false;
                SnapToEdgesToggleSwitch.IsChecked = false;
            }

            if (widgetConfig != null)
            {
                if (widgetConfig.Active == true)
                {
                    WidgetOpenButton.Background = (Brush)new BrushConverter().ConvertFromString("#DC3545");
                    WidgetOpenButton.Content = "Close Widget";
                }
                else
                {
                    WidgetOpenButton.ClearValue(BackgroundProperty);
                    WidgetOpenButton.Content = "Load Widget";
                }
            }
            else
            {
                WidgetOpenButton.ClearValue(BackgroundProperty);
                WidgetOpenButton.Content = "Load Widget";
            }
        }

        private void OpenWidget_Click(object sender, RoutedEventArgs e)
        {
            var selected = WidgetListBox.SelectedItem as WidgetListItem;
            if (selected != null)
            {
                WidgetWindow? widgetWindow = FindWidgetWindow(selected.Path);
                if (widgetWindow == null)
                {
                    if (!config.Widgets.ContainsKey(Path.GetFileName(selected.Path)))
                    {
                        config.Widgets[Path.GetFileName(selected.Path)] = new WidgetConfig
                        {
                            Active = true,
                            WindowX = 100,
                            WindowY = 100,
                            Draggable = DraggableToggleSwitch.IsChecked,
                            ClickThrough = ClickThroughToggleSwitch.IsChecked,
                            KeepOnScreen = KeepOnScreenToggleSwitch.IsChecked,
                            SavePosition = SavePositionToggleSwitch.IsChecked,
                            SnapToEdges = SnapToEdgesToggleSwitch.IsChecked
                        };
                    }
                    else
                    {
                        if (config.Widgets.TryGetValue($"{Path.GetFileName(selected.Path)}", out WidgetConfig? widgetConfig))
                        {
                            widgetConfig.Active = true;
                            UpdateWidgetOptions(widgetConfig);
                        }
                    }

                    SaveConfig();

                    var widget = new WidgetWindow(selected.Path, selected.Manifest, config, configPath);
                    activeWidgets.Add(widget);
                    widget.Closed += (s, ev) => activeWidgets.Remove(widget);
                    widget.Closed += OnWidgetClosed;
                    widget.Show();

                    // Update "Load Widget" button style
                    WidgetOpenButton.Background = (Brush)new BrushConverter().ConvertFromString("#DC3545");
                    WidgetOpenButton.Content = "Close Widget";
                }
                else
                {
                    widgetWindow.Close();
                }
            }
        }

        private void OnWidgetClosed(object sender, EventArgs e)
        {
            WidgetOpenButton.ClearValue(BackgroundProperty);
            WidgetOpenButton.Content = "Load Widget";
        }

        private void OpenDevTools_Click(object sender, RoutedEventArgs e)
        {
            var selected = WidgetListBox.SelectedItem as WidgetListItem;
            if (selected != null)
            {
                WidgetWindow? widgetWindow = FindWidgetWindow(selected.Path);
                if (widgetWindow != null)
                {
                    widgetWindow.OpenDevTools();
                }
            }
        }

        private void Toggle_Checked(object sender, RoutedEventArgs e)
        {
            var selected = WidgetListBox.SelectedItem as WidgetListItem;
            if (selected != null)
            {
                if (config.Widgets.TryGetValue($"{Path.GetFileName(selected.Path)}", out WidgetConfig? widgetConfig))
                {
                    UpdateWidgetOptions(widgetConfig);
                    SaveConfig();
                }
            }
        }

        private void UpdateWidgetOptions(WidgetConfig widgetConfig)
        {
            widgetConfig.Draggable = DraggableToggleSwitch.IsChecked;
            widgetConfig.ClickThrough = ClickThroughToggleSwitch.IsChecked;
            widgetConfig.KeepOnScreen = KeepOnScreenToggleSwitch.IsChecked;
            widgetConfig.SavePosition = SavePositionToggleSwitch.IsChecked;
            widgetConfig.SnapToEdges = SnapToEdgesToggleSwitch.IsChecked;
        }

        private void RefreshWidgets_Click(object sender, RoutedEventArgs e)
        {
            LoadAvailableWidgets();
        }

        private void ShowMenuItem_Click(object sender, RoutedEventArgs e)
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private WidgetWindow? FindWidgetWindow(string path)
        {
            foreach (WidgetWindow widgetWindow in activeWidgets)
            {
                if (widgetWindow.widgetPath == path)
                {
                    return widgetWindow;
                }
            }
            return null;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        protected override void OnClosed(EventArgs e)
        {
            configWatcher?.Dispose();
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
        public bool? KeepOnScreen { get; set; }
        public bool? SavePosition { get; set; }
        public bool? SnapToEdges { get; set; }
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
