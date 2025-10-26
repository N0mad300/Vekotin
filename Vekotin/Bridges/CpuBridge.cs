using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Management;

namespace Vekotin.Bridges
{
    [ComVisible(true)]
    public class CpuBridge : IDisposable
    {
        private readonly PerformanceCounter _usageCounter;
        private readonly PerformanceCounter _clockSpeedCounter;
        private readonly ManagementObject _cpuInfo;
        private bool _disposed = false;

        public CpuBridge()
        {
            _usageCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _usageCounter.NextValue(); // First call returns 0, so prime it

            _clockSpeedCounter = new PerformanceCounter("Processor Information", "% of Maximum Frequency", "_Total");
            _clockSpeedCounter.NextValue(); // First call returns 0, so prime it

            // Get the static CPU informations
            var searcher = new ManagementObjectSearcher("select * from Win32_Processor");
            _cpuInfo = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
        }

        // === Dynamic Informations ===

        /// <summary>
        /// Gets the current total CPU usage across all cores as a percentage.
        /// </summary>
        /// <returns>A float representing the current usage percentage.</returns>
        public float GetUsage()
        {
            return _usageCounter.NextValue();
        }

        /// <summary>
        /// Gets the current clock speed of the CPU in MHz.
        /// </summary>
        /// <returns>An integer representing the current clock speed in MHz.</returns>
        public int GetCurrentClockSpeed()
        {
            if (_cpuInfo == null) return 0;

            // The "% of Maximum Frequency" counter gives a percentage. We multiply it by the max speed.
            var maxSpeed = (uint)_cpuInfo["MaxClockSpeed"];
            var currentSpeedPercentage = _clockSpeedCounter.NextValue() / 100.0f;

            return (int)(maxSpeed * currentSpeedPercentage);
        }

        // === Static Informations ===

        /// <summary>
        /// Gets the full name of the CPU.
        /// </summary>
        /// <returns>A string containing the processor name.</returns>
        public string GetName()
        {
            return _cpuInfo?["Name"]?.ToString() ?? "Unknown CPU";
        }

        /// <summary>
        /// Gets the number of physical cores in the CPU.
        /// </summary>
        /// <returns>An integer representing the number of cores.</returns>
        public int GetCoreCount()
        {
            if (_cpuInfo == null) return 0;
            return (int)(uint)_cpuInfo["NumberOfCores"];
        }

        /// <summary>
        /// Gets the number of logical processors (threads).
        /// </summary>
        /// <returns>An integer representing the number of logical processors.</returns>
        public int GetLogicalProcessorCount()
        {
            if (_cpuInfo == null) return 0;
            return (int)(uint)_cpuInfo["NumberOfLogicalProcessors"];
        }

        /// <summary>
        /// Gets the maximum designed clock speed of the CPU in MHz.
        /// </summary>
        /// <returns>An integer representing the max clock speed in MHz.</returns>
        public int GetMaxClockSpeed()
        {
            if (_cpuInfo == null) return 0;
            return (int)(uint)_cpuInfo["MaxClockSpeed"];
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _usageCounter?.Dispose();
                _clockSpeedCounter?.Dispose();
                _cpuInfo?.Dispose();
                _disposed = true;
            }
        }
    }
}
