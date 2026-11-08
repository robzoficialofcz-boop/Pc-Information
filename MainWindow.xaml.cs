using System.Windows;
using System.Windows.Threading;

namespace PcInformation;

public partial class MainWindow : Window
{
    private readonly HardwareMonitor _monitor = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _lastNetworkSample = DateTime.UtcNow;
    private long _lastReceived;
    private long _lastSent;
    private bool _networkSampleInitialized;
    private bool _isFullscreen;
    private WindowStyle _previousWindowStyle;
    private ResizeMode _previousResizeMode;
    private WindowState _previousWindowState;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            ComputerNameText.Text = Environment.MachineName;
            WindowsText.Text = $"{Environment.OSVersion.VersionString} • {(Environment.Is64BitOperatingSystem ? "64 bits" : "32 bits")}";
            await RefreshHardwareDetailsAsync();
            _timer.Tick += (_, _) => RefreshLiveData();
            _timer.Start();
            RefreshLiveData();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _monitor.Dispose();
        };
    }


    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape && _isFullscreen)
        {
            ExitFullscreen();
            e.Handled = true;
        }
    }

    private void ToggleFullscreen()
    {
        if (_isFullscreen) { ExitFullscreen(); return; }
        _previousWindowStyle = WindowStyle;
        _previousResizeMode = ResizeMode;
        _previousWindowState = WindowState;
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        _isFullscreen = true;
    }

    private void ExitFullscreen()
    {
        WindowState = WindowState.Normal;
        WindowStyle = _previousWindowStyle;
        ResizeMode = _previousResizeMode;
        WindowState = _previousWindowState;
        _isFullscreen = false;
    }

    private async Task RefreshHardwareDetailsAsync()
    {
        try
        {
            var info = await Task.Run(_monitor.GetHardwareInfo);
            CpuNameText.Text = info.CpuName;
            GpuNameText.Text = info.GpuName;
            BoardText.Text = info.Motherboard;
            BiosText.Text = info.Bios;
            CpuDetailText.Text = info.CpuName;
            SystemDetailText.Text = $"{info.OperatingSystem} • {info.Architecture}";
            GpuDetailText.Text = $"VRAM dedicada: {info.GpuMemory}";
            DrivesText.Text = info.Drives;
            StatusText.Text = info.SensorNote;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Algumas informações não puderam ser obtidas: {ex.Message}";
        }
    }

    private void RefreshLiveData()
    {
        try
        {
            var data = _monitor.GetLiveData();
            CpuUsageText.Text = $"{data.CpuLoad:0}%";
            CpuBar.Value = Clamp(data.CpuLoad);
            CpuTempText.Text = data.CpuTemperature is double cpuTemp
                ? $"Temperatura: {cpuTemp:0} °C"
                : "Temperatura: sensor indisponível";

            RamUsageText.Text = $"{data.RamUsed:0.0} GB";
            RamDetailText.Text = $"{data.RamTotal:0.0} GB totais • {data.RamAvailable:0.0} GB livres";
            RamBar.Value = Clamp(data.RamPercent);
            RamPercentText.Text = $"{data.RamPercent:0.0}% utilizado";

            GpuUsageText.Text = data.GpuLoad is double gpuLoad ? $"{gpuLoad:0}%" : "N/D";
            GpuBar.Value = data.GpuLoad is double load ? Clamp(load) : 0;
            GpuDetailText.Text = $"VRAM dedicada: {data.GpuMemoryText}" +
                (data.GpuTemperature is double gpuTemp ? $" • {gpuTemp:0} °C" : "");

            DiskUsageText.Text = $"{data.DiskUsed:0.0} GB";
            DiskDetailText.Text = $"{data.DiskTotal:0.0} GB totais • {data.DiskFree:0.0} GB livres";
            DiskBar.Value = Clamp(data.DiskPercent);
            DiskPercentText.Text = $"{data.DiskPercent:0.0}% utilizado";

            var now = DateTime.UtcNow;
            var elapsed = Math.Max(0.1, (now - _lastNetworkSample).TotalSeconds);
            if (_networkSampleInitialized)
            {
                DownloadText.Text = FormatRate((data.BytesReceived - _lastReceived) / elapsed);
                UploadText.Text = FormatRate((data.BytesSent - _lastSent) / elapsed);
            }
            else
            {
                DownloadText.Text = "Calculando...";
                UploadText.Text = "Calculando...";
                _networkSampleInitialized = true;
            }

            _lastReceived = data.BytesReceived;
            _lastSent = data.BytesSent;
            _lastNetworkSample = now;
            UpdatedText.Text = $"Atualizado às {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Falha temporária na leitura: {ex.Message}";
        }
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 100);

    private static string FormatRate(double bytesPerSecond)
    {
        var kb = Math.Max(0, bytesPerSecond) / 1024d;
        return kb >= 1024 ? $"{kb / 1024:0.00} MB/s" : $"{kb:0.0} KB/s";
    }
}