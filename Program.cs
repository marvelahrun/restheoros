// Program.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using Spectre.Console;
using System.Globalization;
using LibreHardwareMonitor.Hardware;

class Program
{
    static void Main(string[] args)
    {
        // Initialize LibreHardwareMonitor
        Computer computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = false,
            IsNetworkEnabled = false,
            IsStorageEnabled = false
        };

        computer.Open();

        // Warm-up CPU counter for fallback
        var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        cpuCounter.NextValue();

        Console.CancelKeyPress += (_, e) =>
        {
            computer.Close();
            Console.CursorVisible = true;
            e.Cancel = false;
        };

        Console.CursorVisible = false;
        const int refreshMs = 1000;

        while (true)
        {
            if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q) break;

            // Update hardware sensors
            foreach (var hardware in computer.Hardware)
            {
                hardware.Update();
                foreach (var subhardware in hardware.SubHardware)
                {
                    subhardware.Update();
                }
            }

            // Gather data from LibreHardwareMonitor
            var cpuData = GetCpuData(computer);
            var gpuData = GetGpuData(computer);
            var memData = GetMemoryData(computer);
            int? battery = GetBatteryPercent();

            // FPS placeholder (integrate PresentMon or RTSS later)
            string fps = "N/A";

            // Render UI
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine("[bold cyan]╔═════════════════════════ RESOURCE MONITOR ═════════════════════════╗[/]");
            AnsiConsole.MarkupLine($"[yellow]FPS:[/] {fps}    [green]Battery:[/] {(battery.HasValue ? battery.Value + "%" : "N/A")}");
            AnsiConsole.MarkupLine("─────────────────────────────────────────────────────────────────────────────");

            AnsiConsole.MarkupLine($"[bold]CPU:[/] {cpuData.Usage,5:F1}%  | {cpuData.Clock,6:F0} MHz | Temp: {cpuData.Temp} | Power: {cpuData.Power} | Fan: {cpuData.Fan}");
            AnsiConsole.MarkupLine($"[bold]GPU:[/] {gpuData.Usage} | {gpuData.Clock} | Temp: {gpuData.Temp} | Power: {gpuData.Power} | Fan: {gpuData.Fan}");
            AnsiConsole.MarkupLine("─────────────────────────────────────────────────────────────────────────────");
            AnsiConsole.MarkupLine($"[bold]Memory:[/] {memData.Used:F1} / {memData.Total:F1} GB ({memData.UsagePercent:F1}%)");
            AnsiConsole.MarkupLine($"[bold]VRAM:[/] {gpuData.VramUsage} | VRAM Used: {gpuData.VramUsed} / {gpuData.VramTotal}");
            AnsiConsole.MarkupLine("─────────────────────────────────────────────────────────────────────────────");
            AnsiConsole.MarkupLine("[grey]Press Q to quit[/]");

            Thread.Sleep(refreshMs);
        }

        computer.Close();
        Console.CursorVisible = true;
    }

    class CpuData
    {
        public float Usage;
        public double Clock;
        public string Temp = "N/A";
        public string Power = "N/A";
        public string Fan = "N/A";
    }

    static CpuData GetCpuData(Computer computer)
    {
        var data = new CpuData();
        
        foreach (var hardware in computer.Hardware)
        {
            if (hardware.HardwareType == HardwareType.Cpu)
            {
                foreach (var sensor in hardware.Sensors)
                {
                    if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Total"))
                    {
                        data.Usage = sensor.Value ?? 0f;
                    }
                    else if (sensor.SensorType == SensorType.Clock && sensor.Name.Contains("Core #1"))
                    {
                        data.Clock = sensor.Value ?? 0f;
                    }
                    else if (sensor.SensorType == SensorType.Temperature && (sensor.Name.Contains("Package") || sensor.Name.Contains("Core Average")))
                    {
                        if (sensor.Value.HasValue)
                            data.Temp = $"{sensor.Value:F1}°C";
                    }
                    else if (sensor.SensorType == SensorType.Power && sensor.Name.Contains("Package"))
                    {
                        if (sensor.Value.HasValue)
                            data.Power = $"{sensor.Value:F1} W";
                    }
                    else if (sensor.SensorType == SensorType.Fan)
                    {
                        if (sensor.Value.HasValue)
                            data.Fan = $"{sensor.Value:F0} RPM";
                    }
                }
            }
        }

        return data;
    }

    class GpuData
    {
        public string Usage = "N/A";
        public string Clock = "N/A";
        public string Temp = "N/A";
        public string Power = "N/A";
        public string Fan = "N/A";
        public string VramUsage = "N/A";
        public string VramUsed = "N/A";
        public string VramTotal = "N/A";
    }

    static GpuData GetGpuData(Computer computer)
    {
        var data = new GpuData();
        
        foreach (var hardware in computer.Hardware)
        {
            if (hardware.HardwareType == HardwareType.GpuNvidia || 
                hardware.HardwareType == HardwareType.GpuAmd || 
                hardware.HardwareType == HardwareType.GpuIntel)
            {
                double memUsed = 0, memTotal = 0;
                
                foreach (var sensor in hardware.Sensors)
                {
                    if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Core"))
                    {
                        if (sensor.Value.HasValue)
                            data.Usage = $"{sensor.Value:F1}%";
                    }
                    else if (sensor.SensorType == SensorType.Clock && sensor.Name.Contains("Core"))
                    {
                        if (sensor.Value.HasValue)
                            data.Clock = $"{sensor.Value:F0} MHz";
                    }
                    else if (sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("Core"))
                    {
                        if (sensor.Value.HasValue)
                            data.Temp = $"{sensor.Value:F1}°C";
                    }
                    else if (sensor.SensorType == SensorType.Power && (sensor.Name.Contains("Package") || sensor.Name.Contains("Total")))
                    {
                        if (sensor.Value.HasValue)
                            data.Power = $"{sensor.Value:F1} W";
                    }
                    else if (sensor.SensorType == SensorType.Fan)
                    {
                        if (sensor.Value.HasValue)
                            data.Fan = $"{sensor.Value:F0}%";
                    }
                    else if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Memory Used"))
                    {
                        memUsed = sensor.Value ?? 0;
                        data.VramUsed = $"{memUsed / 1024:F1} GB";
                    }
                    else if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Memory Total"))
                    {
                        memTotal = sensor.Value ?? 0;
                        data.VramTotal = $"{memTotal / 1024:F1} GB";
                    }
                    else if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Memory"))
                    {
                        if (sensor.Value.HasValue)
                            data.VramUsage = $"{sensor.Value:F1}%";
                    }
                }

                // Calculate VRAM usage if not directly available
                if (data.VramUsage == "N/A" && memTotal > 0)
                {
                    double pct = (memUsed / memTotal) * 100.0;
                    data.VramUsage = $"{pct:F1}%";
                }
                
                break; // Use first GPU found
            }
        }

        return data;
    }

    class MemData
    {
        public double Used;
        public double Total;
        public double UsagePercent;
    }

    static MemData GetMemoryData(Computer computer)
    {
        var data = new MemData();
        
        foreach (var hardware in computer.Hardware)
        {
            if (hardware.HardwareType == HardwareType.Memory)
            {
                foreach (var sensor in hardware.Sensors)
                {
                    if (sensor.SensorType == SensorType.Data && sensor.Name.Contains("Used"))
                    {
                        data.Used = sensor.Value ?? 0;
                    }
                    else if (sensor.SensorType == SensorType.Data && sensor.Name.Contains("Available"))
                    {
                        double available = sensor.Value ?? 0;
                        // Total is typically calculated from Used + Available
                        if (data.Used > 0)
                            data.Total = data.Used + available;
                    }
                    else if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Memory"))
                    {
                        data.UsagePercent = sensor.Value ?? 0;
                    }
                }
            }
        }

        return data;
    }

    // Battery percent via WMI (Win32_Battery) -- may be absent on desktops
    static int? GetBatteryPercent()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining FROM Win32_Battery");
            foreach (ManagementObject mo in searcher.Get())
            {
                var val = mo["EstimatedChargeRemaining"];
                if (val != null && int.TryParse(val.ToString(), out int pct))
                    return pct;
            }
        }
        catch { }
        return null;
    }
}