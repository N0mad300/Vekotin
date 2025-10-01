using System.Management;
using System.Runtime.InteropServices;

namespace Vekotin.Bridges
{
    [ComVisible(true)]
    public class RamBridge
    {
        public double GetUsagePercentage()
        {
            using (var wmiObject = new ManagementObjectSearcher("select * from Win32_OperatingSystem"))
            {
                var memoryValues = wmiObject.Get().Cast<ManagementObject>().Select(mo => new {
                    TotalVisibleMemorySize = double.Parse(mo["TotalVisibleMemorySize"].ToString()),
                    FreePhysicalMemory = double.Parse(mo["FreePhysicalMemory"].ToString())
                }).FirstOrDefault();

                if (memoryValues != null)
                {
                    var usedMemory = memoryValues.TotalVisibleMemorySize - memoryValues.FreePhysicalMemory;
                    var percent = (usedMemory / memoryValues.TotalVisibleMemorySize) * 100;
                    return Math.Round(percent, 2);
                }
            }
            return 0;
        }
    }
}
