using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace Vekotin.Bridges
{
    [ComVisible(true)]
    public class DiskBridge : IDisposable
    {
        private readonly Dictionary<string, PerformanceCounter> _readSpeedCounters;
        private readonly Dictionary<string, PerformanceCounter> _writeSpeedCounters;
        private readonly Dictionary<string, string> _driveToInstanceMap;
        private bool _disposed = false;

        public DiskBridge()
        {
            _readSpeedCounters = new Dictionary<string, PerformanceCounter>();
            _writeSpeedCounters = new Dictionary<string, PerformanceCounter>();
            _driveToInstanceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        // === Drive Info ===

        /// <summary>
        /// Gets all available drive letters on the system.
        /// </summary>
        /// <returns>An array of drive letters (e.g., ["C:", "D:"]).</returns>
        public string[] GetDriveLetters()
        {
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => d.Name.TrimEnd('\\'))
                .ToArray();
        }

        /// <summary>
        /// Gets the type of the specified drive (Fixed, Removable, Network, etc.).
        /// </summary>
        public string GetDriveType(string driveLetter)
        {
            try
            {
                var drive = GetDriveInfo(driveLetter);
                return drive?.DriveType.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// Returns whether the drive is an SSD (true) or HDD (false).
        /// </summary>
        public bool IsDriveSSD(string driveLetter)
        {
            try
            {
                // Normalize
                driveLetter = driveLetter?.Trim().ToUpper();
                if (string.IsNullOrEmpty(driveLetter)) return false;
                if (!driveLetter.EndsWith(":")) driveLetter += ":";

                // Find the Win32_DiskDrive associated with this logical drive and get its Index
                ManagementObject targetDisk = null;
                var logicalToPartitionQuery = $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter}'}} WHERE AssocClass=Win32_LogicalDiskToPartition";
                using (var lpSearcher = new ManagementObjectSearcher(logicalToPartitionQuery))
                {
                    foreach (ManagementObject partition in lpSearcher.Get())
                    {
                        var diskAssocQuery = $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition";
                        using (var diskSearcher = new ManagementObjectSearcher(diskAssocQuery))
                        {
                            foreach (ManagementObject disk in diskSearcher.Get())
                            {
                                targetDisk = disk;
                                break;
                            }
                        }

                        if (targetDisk != null) break;
                    }
                }

                if (targetDisk == null)
                {
                    // Could not map logical to physical; fallback to false
                    return false;
                }

                // Extract disk index if available for mapping to MSFT_PhysicalDisk
                string indexStr = targetDisk["Index"]?.ToString();

                try
                {
                    var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
                    scope.Connect();

                    // If we have a numeric DeviceId / Index, prefer selecting by DeviceId
                    if (!string.IsNullOrEmpty(indexStr) && int.TryParse(indexStr, out var idx))
                    {
                        var mdQuery = new ObjectQuery($"SELECT * FROM MSFT_PhysicalDisk WHERE DeviceId = {idx}");
                        using (var mdSearcher = new ManagementObjectSearcher(scope, mdQuery))
                        {
                            foreach (ManagementObject queryObj in mdSearcher.Get())
                            {
                                if (Convert.ToInt16(queryObj["MediaType"]) == 4)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Storage WMI may not be available on older Windows — ignore and continue
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // === Capacity ===

        public float GetTotalSpace(string driveLetter)
        {
            try
            {
                var drive = GetDriveInfo(driveLetter);
                if (drive == null || !drive.IsReady) return 0;
                return drive.TotalSize / (1024f * 1024f);
            }
            catch
            {
                return 0;
            }
        }

        public float GetFreeSpace(string driveLetter)
        {
            try
            {
                var drive = GetDriveInfo(driveLetter);
                if (drive == null || !drive.IsReady) return 0;
                return drive.AvailableFreeSpace / (1024f * 1024f);
            }
            catch
            {
                return 0;
            }
        }

        public float GetUsedSpace(string driveLetter)
        {
            try
            {
                var drive = GetDriveInfo(driveLetter);
                if (drive == null || !drive.IsReady) return 0;
                return (drive.TotalSize - drive.AvailableFreeSpace) / (1024f * 1024f);
            }
            catch
            {
                return 0;
            }
        }

        public float GetUsage(string driveLetter)
        {
            try
            {
                var drive = GetDriveInfo(driveLetter);
                if (drive == null || !drive.IsReady) return 0;
                var usedBytes = drive.TotalSize - drive.AvailableFreeSpace;
                return (usedBytes / (float)drive.TotalSize) * 100f;
            }
            catch
            {
                return 0;
            }
        }

        // === I/O Speed ===

        public float GetReadSpeed(string driveLetter)
        {
            try
            {
                var instanceName = GetPhysicalDiskInstance(driveLetter);
                if (string.IsNullOrEmpty(instanceName)) return 0;

                if (!_readSpeedCounters.TryGetValue(driveLetter, out var counter))
                {
                    counter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", instanceName);
                    counter.NextValue(); // Prime it
                    _readSpeedCounters[driveLetter] = counter;
                    return 0; // First read always 0
                }

                return counter.NextValue();
            }
            catch
            {
                return 0;
            }
        }

        public float GetWriteSpeed(string driveLetter)
        {
            try
            {
                var instanceName = GetPhysicalDiskInstance(driveLetter);
                if (string.IsNullOrEmpty(instanceName)) return 0;

                if (!_writeSpeedCounters.TryGetValue(driveLetter, out var counter))
                {
                    counter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", instanceName);
                    counter.NextValue(); // Prime it
                    _writeSpeedCounters[driveLetter] = counter;
                    return 0;
                }

                return counter.NextValue();
            }
            catch
            {
                return 0;
            }
        }

        // === Helper Methods ===

        private DriveInfo GetDriveInfo(string driveLetter)
        {
            driveLetter = driveLetter.Trim().ToUpper();
            if (!driveLetter.EndsWith(":"))
                driveLetter += ":";
            return DriveInfo.GetDrives().FirstOrDefault(d => d.Name.StartsWith(driveLetter, StringComparison.OrdinalIgnoreCase));
        }

        private string GetPhysicalDiskInstance(string driveLetter)
        {
            if (_driveToInstanceMap.TryGetValue(driveLetter, out var cached))
                return cached;

            try
            {
                driveLetter = driveLetter.Trim().ToUpper();
                if (!driveLetter.EndsWith(":"))
                    driveLetter += ":";

                var query = $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter}'}} WHERE AssocClass=Win32_LogicalDiskToPartition";
                using (var searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject partition in searcher.Get())
                    {
                        var diskQuery = $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition";
                        using (var diskSearcher = new ManagementObjectSearcher(diskQuery))
                        {
                            foreach (ManagementObject disk in diskSearcher.Get())
                            {
                                var index = disk["Index"]?.ToString();
                                if (index != null)
                                {
                                    var instanceName = $"{index} {driveLetter}";
                                    _driveToInstanceMap[driveLetter] = instanceName;
                                    return instanceName;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // fallback: use drive letter itself
                _driveToInstanceMap[driveLetter] = driveLetter;
                return driveLetter.TrimEnd(':');
            }

            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;

            foreach (var counter in _readSpeedCounters.Values)
                counter?.Dispose();
            foreach (var counter in _writeSpeedCounters.Values)
                counter?.Dispose();

            _readSpeedCounters.Clear();
            _writeSpeedCounters.Clear();
            _driveToInstanceMap.Clear();

            _disposed = true;
        }
    }
}
