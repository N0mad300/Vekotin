
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

using Wpf.Ui.Controls;

using Vekotin.Services;

namespace Vekotin
{
    public partial class ControlPanel : FluentWindow
    {
        private readonly List<WidgetWindow> activeWidgets = new();
        private readonly ConfigurationManager configManager;

        public ObservableCollection<WidgetListItem> AvailableWidgets { get; set; }

        public ControlPanel()
        {
            InitializeComponent();

            AvailableWidgets = new ObservableCollection<WidgetListItem>();
            DataContext = this;

            // Initialize configuration manager
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),Constants.AppName);

            configManager = new ConfigurationManager(appDataPath);
            configManager.ConfigChanged += OnConfigChanged;

            LoadAvailableWidgets();
        }

        /// <summary>
        /// Handles external configuration changes.
        /// </summary>
        private void OnConfigChanged(object? sender, ConfigChangedEventArgs e)
        {
            if (e.ChangeType == ConfigChangeType.Loaded)
            {
                // Refresh UI
                Dispatcher.Invoke(() =>
                {
                    // Refresh selected widget info here
                    var selected = WidgetListBox.SelectedItem as WidgetListItem;
                    if (selected != null)
                    {
                        OnWidgetSelected(selected);
                    }
                });
            }
        }

        private void LoadAvailableWidgets()
        {
            var config = configManager.Current;
            string widgetsPath = config.Vekotin.WidgetPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Vekotin", "Widgets");

            if (!Directory.Exists(widgetsPath))
            {
                Directory.CreateDirectory(widgetsPath);
            }

            AvailableWidgets.Clear();
            foreach (var dir in Directory.GetDirectories(widgetsPath))
            {
                string manifestPath = Path.Combine(dir, Constants.WidgetManifestFileName);
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        var manifest = JsonSerializer.Deserialize<WidgetManifest>(File.ReadAllText(manifestPath));

                        if (manifest != null && ValidateManifest(manifest))
                        {
                            AvailableWidgets.Add(new WidgetListItem
                            {
                                Name = manifest.Name,
                                Path = dir,
                                Manifest = manifest
                            });
                        }
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"Invalid widget manifest in {dir}: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error loading widget from {dir}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Validates a widget manifest.
        /// </summary>
        private bool ValidateManifest(WidgetManifest manifest)
        {
            if (string.IsNullOrWhiteSpace(manifest.Name)) return false;
            if (manifest.Width <= 0 || manifest.Height <= 0) return false;
            return true;
        }

        private void WidgetListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is WidgetListItem selected)
            {
                OnWidgetSelected(selected);
            }
        }

        private void OnWidgetSelected(WidgetListItem selected)
        {
            var widgetName = Path.GetFileName(selected.Path);
            var widgetConfig = configManager.GetWidgetConfig(widgetName);

            // Update toggle switches
            DraggableToggleSwitch.IsChecked = widgetConfig?.Draggable ?? false;
            ClickThroughToggleSwitch.IsChecked = widgetConfig?.ClickThrough ?? false;
            KeepOnScreenToggleSwitch.IsChecked = widgetConfig?.KeepOnScreen ?? false;
            SavePositionToggleSwitch.IsChecked = widgetConfig?.SavePosition ?? false;
            SnapToEdgesToggleSwitch.IsChecked = widgetConfig?.SnapToEdges ?? false;

            // Update button state
            if (widgetConfig?.Active == true)
            {
                WidgetOpenButton.Content = "Close Widget";
                WidgetOpenButton.Appearance = ControlAppearance.Danger;
            }
            else
            {
                WidgetOpenButton.Content = "Load Widget";
                WidgetOpenButton.Appearance = ControlAppearance.Primary;
            }
        }

        private void OpenWidget_Click(object sender, RoutedEventArgs e)
        {
            if (WidgetListBox.SelectedItem is not WidgetListItem selected)
                return;

            var widgetName = Path.GetFileName(selected.Path);
            var existingWindow = FindWidgetWindow(selected.Path);

            if (existingWindow != null)
            {
                // Widget is open, close it
                existingWindow.Close();
            }
            else
            {
                // Widget is not open, create and show it
                var widgetConfig = configManager.GetWidgetConfig(widgetName);

                if (widgetConfig == null)
                {
                    // Create new config with current toggle values
                    widgetConfig = new WidgetConfig
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
                    configManager.SetWidgetConfig(widgetName, widgetConfig);
                }
                else
                {
                    // Update existing config
                    widgetConfig.Active = true;
                    UpdateWidgetOptions(widgetConfig);
                }

                configManager.Save();

                var widget = new WidgetWindow(
                    selected.Path,
                    selected.Manifest,
                    configManager);

                activeWidgets.Add(widget);
                widget.Closed += (s, ev) => activeWidgets.Remove(widget);
                widget.Closed += OnWidgetClosed;
                widget.Show();

                // Update button
                WidgetOpenButton.Content = "Close Widget";
                WidgetOpenButton.Appearance = ControlAppearance.Danger;
            }
        }

        private void OnWidgetClosed(object? sender, EventArgs e)
        {
            WidgetOpenButton.Content = "Load Widget";
            WidgetOpenButton.Appearance = ControlAppearance.Primary;
        }

        private void OpenDevTools_Click(object sender, RoutedEventArgs e)
        {
            if (WidgetListBox.SelectedItem is WidgetListItem selected)
            {
                FindWidgetWindow(selected.Path)?.OpenDevTools();
            }
        }

        private void Toggle_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleSwitch toggleControl || WidgetListBox.SelectedItem is not WidgetListItem selected)
                return;

            var widgetName = Path.GetFileName(selected.Path);

            configManager.UpdateWidgetConfig(widgetName, widgetConfig =>
            {
                switch (toggleControl.Name)
                {
                    case "DraggableToggleSwitch":
                        widgetConfig.Draggable = toggleControl.IsChecked;
                        break;
                    case "ClickThroughToggleSwitch":
                        widgetConfig.ClickThrough = toggleControl.IsChecked;
                        break;
                    case "KeepOnScreenToggleSwitch":
                        widgetConfig.KeepOnScreen = toggleControl.IsChecked;
                        break;
                    case "SavePositionToggleSwitch":
                        widgetConfig.SavePosition = toggleControl.IsChecked;
                        break;
                    case "SnapToEdgesToggleSwitch":
                        widgetConfig.SnapToEdges = toggleControl.IsChecked;
                        break;
                }
            });

            configManager.Save();
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
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private WidgetWindow? FindWidgetWindow(string path)
        {
            return activeWidgets.FirstOrDefault(w => w.WidgetPath == path);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        protected override void OnClosed(EventArgs e)
        {
            configManager?.Dispose();

            foreach (var widget in activeWidgets.ToList())
            {
                widget.Close();
            }

            base.OnClosed(e);
        }
    }

    public class WidgetListItem
    {
        public string? Name { get; set; }
        public string Path { get; set; }
        public WidgetManifest Manifest { get; set; }
    }
}
