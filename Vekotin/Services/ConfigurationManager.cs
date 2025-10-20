using System.IO;
using System.Text.Json;

namespace Vekotin.Services
{
    /// <summary>
    /// Manages application and widget configuration with file watching and thread-safe operations.
    /// </summary>
    public class ConfigurationManager : IDisposable
    {
        private readonly string configPath;
        private readonly object configLock = new object();
        private Config config;
        private FileSystemWatcher? configWatcher;
        private Timer? reloadTimer;
        private bool disposed = false;

        public event EventHandler<ConfigChangedEventArgs>? ConfigChanged;

        /// <summary>
        /// Gets the current configuration (thread-safe).
        /// </summary>
        public Config Current
        {
            get
            {
                lock (configLock)
                {
                    return config;
                }
            }
        }

        public ConfigurationManager(string appDataFolderPath)
        {
            if (string.IsNullOrWhiteSpace(appDataFolderPath))
                throw new ArgumentException("App data folder path cannot be null or empty", nameof(appDataFolderPath));

            configPath = Path.Combine(appDataFolderPath, Constants.ConfigFileName);
            config = new Config();

            EnsureConfigExists(appDataFolderPath);
            Load();
            StartWatching();
        }

        /// <summary>
        /// Ensures config file exists with default values.
        /// </summary>
        private void EnsureConfigExists(string appDataFolderPath)
        {
            if (!File.Exists(configPath))
            {
                Directory.CreateDirectory(appDataFolderPath);

                var defaultConfig = new Config
                {
                    Vekotin = new VekotinConfig
                    {
                        WidgetPath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                            "Vekotin",
                            "Widgets")
                    },
                    Widgets = new Dictionary<string, WidgetConfig>()
                };

                SaveInternal(defaultConfig);
            }
        }

        /// <summary>
        /// Loads configuration from disk (thread-safe).
        /// </summary>
        public void Load()
        {
            lock (configLock)
            {
                try
                {
                    var configJson = File.ReadAllText(configPath);
                    var loadedConfig = JsonSerializer.Deserialize<Config>(configJson);

                    if (loadedConfig != null)
                    {
                        config = loadedConfig;
                        OnConfigChanged(ConfigChangeType.Loaded);
                    }
                }
                catch (JsonException ex)
                {
                    throw new ConfigurationException("Failed to parse configuration file", ex);
                }
                catch (IOException ex)
                {
                    throw new ConfigurationException("Failed to read configuration file", ex);
                }
            }
        }

        /// <summary>
        /// Saves current configuration to disk (thread-safe).
        /// </summary>
        public void Save()
        {
            lock (configLock)
            {
                SaveInternal(config);
                OnConfigChanged(ConfigChangeType.Saved);
            }
        }

        /// <summary>
        /// Internal save without locking (must be called within lock).
        /// </summary>
        private void SaveInternal(Config configToSave)
        {
            try
            {
                // Temporarily disable file watcher to avoid triggering reload
                if (configWatcher != null)
                {
                    configWatcher.EnableRaisingEvents = false;
                }

                var jsonString = JsonSerializer.Serialize(configToSave,
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(configPath, jsonString);
            }
            catch (IOException ex)
            {
                throw new ConfigurationException("Failed to save configuration file", ex);
            }
            finally
            {
                // Re-enable watcher after a short delay
                if (configWatcher != null)
                {
                    Task.Delay(500).ContinueWith(_ =>
                    {
                        if (configWatcher != null && !disposed)
                        {
                            configWatcher.EnableRaisingEvents = true;
                        }
                    });
                }
            }
        }

        /// <summary>
        /// Gets a widget's configuration, or null if not found.
        /// </summary>
        public WidgetConfig? GetWidgetConfig(string widgetName)
        {
            lock (configLock)
            {
                return config.Widgets.TryGetValue(widgetName, out var widgetConfig)
                    ? widgetConfig
                    : null;
            }
        }

        /// <summary>
        /// Updates or creates a widget's configuration.
        /// </summary>
        public void SetWidgetConfig(string widgetName, WidgetConfig widgetConfig)
        {
            if (string.IsNullOrWhiteSpace(widgetName))
                throw new ArgumentException("Widget name cannot be null or empty", nameof(widgetName));
            if (widgetConfig == null)
                throw new ArgumentNullException(nameof(widgetConfig));

            lock (configLock)
            {
                config.Widgets[widgetName] = widgetConfig;
            }
        }

        /// <summary>
        /// Updates a widget's configuration using an action.
        /// </summary>
        public void UpdateWidgetConfig(string widgetName, Action<WidgetConfig> updateAction)
        {
            if (string.IsNullOrWhiteSpace(widgetName))
                throw new ArgumentException("Widget name cannot be null or empty", nameof(widgetName));
            if (updateAction == null)
                throw new ArgumentNullException(nameof(updateAction));

            lock (configLock)
            {
                if (config.Widgets.TryGetValue(widgetName, out var widgetConfig))
                {
                    updateAction(widgetConfig);
                }
            }
        }

        /// <summary>
        /// Starts watching the configuration file for external changes.
        /// </summary>
        private void StartWatching()
        {
            var directory = Path.GetDirectoryName(configPath);
            var fileName = Path.GetFileName(configPath);

            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
                return;

            configWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };

            configWatcher.Changed += OnFileChanged;
            configWatcher.EnableRaisingEvents = true;
        }

        /// <summary>
        /// Handles file system change events with debouncing.
        /// </summary>
        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // Debounce: reset timer each time file changes
            reloadTimer?.Dispose();
            reloadTimer = new Timer(_ =>
            {
                try
                {
                    Load();
                }
                catch (Exception ex)
                {
                    // Log error but don't crash
                    Console.WriteLine($"Error reloading config: {ex.Message}");
                }
            }, null, 300, Timeout.Infinite);
        }

        /// <summary>
        /// Raises the ConfigChanged event.
        /// </summary>
        private void OnConfigChanged(ConfigChangeType changeType)
        {
            ConfigChanged?.Invoke(this, new ConfigChangedEventArgs(changeType, config));
        }

        public void Dispose()
        {
            if (!disposed)
            {
                configWatcher?.Dispose();
                reloadTimer?.Dispose();
                disposed = true;
            }
        }
    }

    /// <summary>
    /// Event arguments for configuration changes.
    /// </summary>
    public class ConfigChangedEventArgs : EventArgs
    {
        public ConfigChangeType ChangeType { get; }
        public Config Config { get; }

        public ConfigChangedEventArgs(ConfigChangeType changeType, Config config)
        {
            ChangeType = changeType;
            Config = config;
        }
    }

    /// <summary>
    /// Type of configuration change.
    /// </summary>
    public enum ConfigChangeType
    {
        Loaded,
        Saved,
        ExternalChange
    }

    /// <summary>
    /// Custom exception for configuration errors.
    /// </summary>
    public class ConfigurationException : Exception
    {
        public ConfigurationException(string message) : base(message) { }
        public ConfigurationException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    /// <summary>
    /// Global Vekotin config with app and widgets settings.
    /// </summary>
    public class Config
    {
        public VekotinConfig Vekotin { get; set; } = new();
        public Dictionary<string, WidgetConfig> Widgets { get; set; } = new();
    }

    /// <summary>
    /// Vekotin app settings.
    /// </summary>
    public class VekotinConfig
    {
        public string? WidgetPath { get; set; }
    }

    /// <summary>
    /// Widget settings.
    /// </summary>
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

    /// <summary>
    /// Widget manifest.
    /// </summary>
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
