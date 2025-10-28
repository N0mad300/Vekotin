using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Management;

namespace Vekotin.Bridges
{
    [ComVisible(true)]
    public class RamBridge : IDisposable
    {
        private readonly PerformanceCounter _ramUsageCounter;
        private readonly ManagementObject _osInfo;
        private bool _disposed = false;

        public RamBridge()
        {
            // Initialize Performance Counter for memory usage
            _ramUsageCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");

            // Get static OS information (includes total visible memory)
            var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
            _osInfo = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
        }

        /// <summary>
        /// Gets the current RAM usage percentage (0–100).
        /// </summary>
        /// <returns>A float representing the percentage of memory currently in use.</returns>
        public float GetUsage()
        {
            return _ramUsageCounter.NextValue();
        }

        /// <summary>
        /// Gets the total visible system memory in megabytes (MB).
        /// </summary>
        /// <returns>Total physical memory in MB.</returns>
        public float GetTotalMemory()
        {
            if (_osInfo == null) return 0;
            // TotalVisibleMemorySize is in kilobytes
            return Convert.ToSingle((ulong)_osInfo["TotalVisibleMemorySize"] / 1024f);
        }

        /// <summary>
        /// Gets the amount of free memory available in megabytes (MB).
        /// </summary>
        /// <returns>Free physical memory in MB.</returns>
        public float GetFreeMemory()
        {
            if (_osInfo == null) return 0;
            _osInfo.Get(); // refresh values
            return Convert.ToSingle((ulong)_osInfo["FreePhysicalMemory"] / 1024f);
        }

        /// <summary>
        /// Gets the amount of used memory in megabytes (MB).
        /// </summary>
        /// <returns>Used physical memory in MB.</returns>
        public float GetUsedMemory()
        {
            var total = GetTotalMemory();
            var free = GetFreeMemory();
            return total - free;
        }

        /// <summary>
        /// Gets the amount of committed (page file) memory in use, in MB.
        /// </summary>
        /// <returns>Committed memory in MB.</returns>
        public float GetCommittedMemory()
        {
            var committedPercent = GetUsage() / 100f;
            var total = GetTotalMemory();
            return total * committedPercent;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _ramUsageCounter?.Dispose();
                _osInfo?.Dispose();
                _disposed = true;
            }
        }
    }
}

