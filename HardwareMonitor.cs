using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace PcInformation;

public sealed class HardwareMonitor : IDisposable
{
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMotherboardEnabled = true,
        IsMemoryEnabled = true,
        IsStorageEnabled = true
    };

    private readonly object _sensorLock = new();
    private readonly NetworkInterface[] _networkInterfaces;
    private long _lastIdle, _lastKernel, _lastUser;
    private bool _cpuTimesInitialized;

    public HardwareMonitor()
    {
        try { _computer.Open(); } catch { }
        _networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        n.OperationalStatus == OperationalStatus.Up)
            .ToArray();
    }

    public HardwareInfo GetHardwareInfo()
    {
        var cpu = ReadWmi("Win32_Processor", "Name");
        var boardManufacturer = ReadWmi("Win32_BaseBoard", "Manufacturer");
        var boardProduct = ReadWmi("Win32_BaseBoard", "Product");
        var biosManufacturer = ReadWmi("Win32_BIOS", "Manufacturer");
        var biosVersion = ReadWmi("Win32_BIOS", "SMBIOSBIOSVersion");
        var gpuNames = ReadAllWmi("Win32_VideoController", "Name");
        var gpuMemoryBytes = ReadAllWmiUInt64("Win32_VideoController", "AdapterRAM").FirstOrDefault();
        var driveLines = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => $"{d.Name}  •  {ToGB(d.TotalSize):0.0} GB")
            .ToArray();

        var board = $"{boardManufacturer} {boardProduct}".Trim();
        var bios = $"{biosManufacturer} • {biosVersion}".Trim(' ', '•');

        return new HardwareInfo(
            string.IsNullOrWhiteSpace(cpu) ? "CPU não identificada" : cpu,
            gpuNames.Count > 0 ? string.Join(" / ", gpuNames) : "GPU não identificada",
            string.IsNullOrWhiteSpace(board) ? "Placa-mãe não identificada" : board,
            string.IsNullOrWhiteSpace(bios) ? "BIOS não identificada" : bios,
            gpuMemoryBytes > 0 ? $"{ToGB(gpuMemoryBytes):0.0} GB (WMI; pode ser aproximado)" : "Não informado pelo driver",
            string.Join(Environment.NewLine, driveLines),
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            "Sensores são exibidos somente quando o hardware e o driver fornecem esses dados. Valores indisponíveis não são estimados."
        );
    }

    public LiveData GetLiveData()
    {
        var (ramTotal, ramAvailable) = GetMemoryStatus();
        var ramUsed = Math.Max(0, ramTotal - ramAvailable);
        var ramPercent = ramTotal > 0 ? ramUsed / ramTotal * 100 : 0;
        var cpuLoad = GetCpuLoad();

        double? cpuTemp = null, gpuLoad = null, gpuTemp = null;
        string gpuMemory = "indisponível";

        lock (_sensorLock)
        {
            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    UpdateHardwareRecursive(hardware);
                    foreach (var sensor in GetSensorsRecursive(hardware))
                    {
                        if (sensor.Value is not float value) continue;

                        if (hardware.HardwareType == HardwareType.Cpu)
                        {
                            if (sensor.SensorType == SensorType.Temperature &&
                                sensor.Name.Contains("CPU Package", StringComparison.OrdinalIgnoreCase))
                                cpuTemp ??= value;

                            if (sensor.SensorType == SensorType.Load &&
                                sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                                cpuLoad = value;
                        }

                        if (hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                        {
                            if (sensor.SensorType == SensorType.Load &&
                                (sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                                 sensor.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase)))
                                gpuLoad ??= value;

                            if (sensor.SensorType == SensorType.Temperature)
                                gpuTemp ??= value;

                            if (sensor.SensorType == SensorType.SmallData &&
                                sensor.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase))
                                gpuMemory = $"{value:0.0} MB (sensor)";

                            if (sensor.SensorType == SensorType.Data &&
                                sensor.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase))
                                gpuMemory = $"{value:0.0} GB (sensor)";
                        }
                    }
                }
            }
            catch
            {
                // Nem todo hardware expõe sensores compatíveis.
            }
        }

        var (diskTotal, diskFree) = GetDiskTotals();
        var (received, sent) = GetNetworkTotals();
        var diskUsed = Math.Max(0, diskTotal - diskFree);
        var diskPercent = diskTotal > 0 ? diskUsed / diskTotal * 100 : 0;

        return new LiveData(cpuLoad, cpuTemp, ramTotal, ramAvailable, ramUsed, ramPercent,
            gpuLoad, gpuTemp, gpuMemory, diskTotal, diskFree, diskUsed, diskPercent, received, sent);
    }

    private static IEnumerable<ISensor> GetSensorsRecursive(IHardware hardware)
    {
        foreach (var sensor in hardware.Sensors) yield return sensor;
        foreach (var sub in hardware.SubHardware)
            foreach (var sensor in GetSensorsRecursive(sub))
                yield return sensor;
    }

    private static void UpdateHardwareRecursive(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware) UpdateHardwareRecursive(sub);
    }

    private double GetCpuLoad()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;

        long i = ToLong(idle), k = ToLong(kernel), u = ToLong(user);
        if (!_cpuTimesInitialized)
        {
            _lastIdle = i;
            _lastKernel = k;
            _lastUser = u;
            _cpuTimesInitialized = true;
            return 0;
        }

        long idleDelta = i - _lastIdle;
        long totalDelta = (k - _lastKernel) + (u - _lastUser);
        _lastIdle = i;
        _lastKernel = k;
        _lastUser = u;

        return totalDelta > 0
            ? Math.Clamp((1d - (double)idleDelta / totalDelta) * 100d, 0, 100)
            : 0;
    }

    private static long ToLong(FILETIME t) => ((long)t.dwHighDateTime << 32) | (uint)t.dwLowDateTime;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public int dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    private static (double total, double available) GetMemoryStatus()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status)) return (0, 0);

        const double gb = 1024d * 1024d * 1024d;
        return (status.ullTotalPhys / gb, status.ullAvailPhys / gb);
    }

    private static (double total, double free) GetDiskTotals()
    {
        double total = 0, free = 0;
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            total += drive.TotalSize;
            free += drive.AvailableFreeSpace;
        }

        const double gb = 1024d * 1024d * 1024d;
        return (total / gb, free / gb);
    }

    private (long received, long sent) GetNetworkTotals()
    {
        long received = 0, sent = 0;
        foreach (var adapter in _networkInterfaces)
        {
            try
            {
                var stats = adapter.GetIPv4Statistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
            catch { }
        }
        return (received, sent);
    }

    private static string ReadWmi(string className, string property)
        => ReadAllWmi(className, property).FirstOrDefault() ?? "";

    private static List<string> ReadAllWmi(string className, string property)
    {
        var result = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {className}");
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    var value = item[property]?.ToString();
                    if (!string.IsNullOrWhiteSpace(value)) result.Add(value);
                }
            }
        }
        catch { }
        return result;
    }

    private static List<ulong> ReadAllWmiUInt64(string className, string property)
    {
        var result = new List<ulong>();
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {className}");
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    try
                    {
                        if (item[property] is not null) result.Add(Convert.ToUInt64(item[property]));
                    }
                    catch { }
                }
            }
        }
        catch { }
        return result;
    }

    private static double ToGB(double bytes) => bytes / (1024d * 1024d * 1024d);

    public void Dispose()
    {
        try { _computer.Close(); } catch { }
    }
}

public sealed record HardwareInfo(
    string CpuName, string GpuName, string Motherboard, string Bios,
    string GpuMemory, string Drives, string OperatingSystem, string Architecture, string SensorNote);

public sealed record LiveData(
    double CpuLoad, double? CpuTemperature,
    double RamTotal, double RamAvailable, double RamUsed, double RamPercent,
    double? GpuLoad, double? GpuTemperature, string GpuMemoryText,
    double DiskTotal, double DiskFree, double DiskUsed, double DiskPercent,
    long BytesReceived, long BytesSent);
