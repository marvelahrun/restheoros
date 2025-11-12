// Program.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using Spectre.Console;
using System.Globalization;

class Program
{
    static void Main(string[] args)
    {
        // Warm-up CPU counter
        var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        cpuCounter.NextValue();

        Console.CancelKeyPress += (_, e) =>
        {
            Console.CursorVisible = true;
            e.Cancel = false;
        };

        Console.CursorVisible = false;
        const int refreshMs = 1000;

        while (true)
        {
            if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q) break;

            // Gather data
            float cpuUsage = SafeCpuUsage(cpuCounter);
            double cpuFreq = GetCpuCurrentClockSpeedMHz(); // MHz
            var (memUsed, memTotal) = GetMemoryUsageGB();
            int? battery = GetBatteryPercent(); // null if not available

            // CPU sensors (may be unavailable on many systems)
            string cpuTemp = TryGetCpuTemperatureString();
            string cpuPower = TryGetCpuPowerString();
            string cpuFan = TryGetCpuFanString();

            // GPU (NVIDIA via nvidia-smi); returns N/A if not present
            var gpu = GetNvidiaSmiInfo();

            // FPS placeholder (integrate PresentMon or RTSS later)
            string fps = "N/A";

            // Render UI
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine("[bold cyan]╔═════════════════════════ RESOURCE MONITOR ═════════════════════════╗[/]");
            AnsiConsole.MarkupLine($"[yellow]FPS:[/] {fps}    [green]Battery:[/] {(battery.HasValue ? battery.Value + "%" : "N/A")}");
            AnsiConsole.MarkupLine("─────────────────────────────────────────────────────────────────────────────");

            AnsiConsole.MarkupLine($"[bold]CPU:[/] {cpuUsage,5:F1}%  | {cpuFreq,6:F0} MHz | Temp: {cpuTemp} | Power: {cpuPower} | Fan: {cpuFan}");
            AnsiConsole.MarkupLine($"[bold]GPU:[/] {gpu.Usage} | {gpu.ClockMHz} MHz | Temp: {gpu.Temp} | Power: {gpu.Power} | Fan: {gpu.Fan}");
            AnsiConsole.MarkupLine("─────────────────────────────────────────────────────────────────────────────");
            AnsiConsole.MarkupLine($"[bold]Memory:[/] {memUsed:F1} / {memTotal:F1} GB");
            AnsiConsole.MarkupLine($"[bold]VRAM:[/] {gpu.VramUsage} | VRAM Used: {gpu.VramUsed} / {gpu.VramTotal}");
            AnsiConsole.MarkupLine("─────────────────────────────────────────────────────────────────────────────");
            AnsiConsole.MarkupLine("[grey]Press Q to quit[/]");

            Thread.Sleep(refreshMs);
        }

        Console.CursorVisible = true;
    }

    // CPU usage safe (non-blocking)
    static float SafeCpuUsage(PerformanceCounter cpuCounter)
    {
        try
        {
            // Using a warmed-up counter: NextValue is instantaneous here
            return cpuCounter.NextValue();
        }
        catch
        {
            return 0f;
        }
    }

    // CPU current clock speed (MHz) via WMI (Win32_Processor.CurrentClockSpeed)
    static double GetCpuCurrentClockSpeedMHz()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT CurrentClockSpeed FROM Win32_Processor");
            foreach (ManagementObject mo in searcher.Get())
            {
                var val = mo["CurrentClockSpeed"];
                if (val != null && double.TryParse(val.ToString(), out double mhz))
                    return mhz;
            }
        }
        catch { }
        return double.NaN;
    }

    // Memory usage via WMI (returns usedGB, totalGB)
    static (double used, double total) GetMemoryUsageGB()
    {
        try
        {
            ulong totalMemory = 0, freeMemory = 0;
            using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                totalMemory = (ulong)obj["TotalVisibleMemorySize"];
                freeMemory = (ulong)obj["FreePhysicalMemory"];
            }

            double totalGB = totalMemory / 1024.0 / 1024.0;
            double usedGB = (totalMemory - freeMemory) / 1024.0 / 1024.0;
            return (usedGB, totalGB);
        }
        catch
        {
            return (0, 0);
        }
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

    // CPU temperature reading attempt (MSAcpi_ThermalZoneTemperature or Win32_TemperatureProbe)
    // Note: most modern Windows desktops/boards don't expose CPU temp via these WMI classes.
    static string TryGetCpuTemperatureString()
    {
        try
        {
            // MSAcpi_ThermalZoneTemperature returns tenths of Kelvin on some systems
            using var searcher = new ManagementObjectSearcher("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            foreach (ManagementObject mo in searcher.Get())
            {
                var cur = mo["CurrentTemperature"];
                if (cur != null && double.TryParse(cur.ToString(), out double t))
                {
                    // convert tenths of Kelvin to Celsius if necessary
                    double c = (t / 10.0) - 273.15;
                    return $"{c:F1}°C";
                }
            }
        }
        catch { }

        // fallback attempt: Win32_TemperatureProbe (rarely present)
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT CurrentReading FROM Win32_TemperatureProbe");
            foreach (ManagementObject mo in searcher.Get())
            {
                var cur = mo["CurrentReading"];
                if (cur != null && double.TryParse(cur.ToString(), out double r))
                {
                    // Not standardized; present raw
                    return $"{r:F1}";
                }
            }
        }
        catch { }

        return "N/A";
    }

    static string TryGetCpuPowerString()
    {
        // No standard cross-vendor WMI for CPU power draw; usually requires vendor tools or LibreHardwareMonitor.
        return "N/A";
    }

    static string TryGetCpuFanString()
    {
        // Fan speed typically not exposed via standard WMI.
        return "N/A";
    }

    // ---------------------------
    // NVIDIA GPU via nvidia-smi
    // ---------------------------
    class NvidiaInfo
    {
        public string Usage = "N/A";
        public string Temp = "N/A";
        public string Power = "N/A";
        public string VramUsage = "N/A";
        public string VramUsed = "N/A";
        public string VramTotal = "N/A";
        public string ClockMHz = "N/A";
        public string Fan = "N/A";
    }

    static NvidiaInfo GetNvidiaSmiInfo()
    {
        // If nvidia-smi is not on PATH or not installed, return N/A object.
        try
        {
            // Query fields: temp, gpu utilization, power draw, memory.used, memory.total, fan speed, clocks
            // Some fields may not be available depending on driver version.
            string args = "--query-gpu=temperature.gpu,utilization.gpu,power.draw,memory.used,memory.total,fan.speed,clocks.gr --format=csv,noheader,nounits";
            var psi = new ProcessStartInfo("nvidia-smi", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return new NvidiaInfo();
            string output = proc.StandardOutput.ReadToEnd().Trim();
            string err = proc.StandardError.ReadToEnd().Trim();
            proc.WaitForExit(500);

            if (string.IsNullOrWhiteSpace(output))
            {
                return new NvidiaInfo();
            }

            // For systems with multiple GPUs, nvidia-smi returns multiple lines.
            // We'll take the first GPU (line 0).
            var firstLine = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(firstLine)) return new NvidiaInfo();

            // CSV: temp, utilization, power, mem.used, mem.total, fan.speed, clocks.gr
            var parts = firstLine.Split(',').Select(p => p.Trim()).ToArray();
            var info = new NvidiaInfo();

            if (parts.Length >= 1) info.Temp = parts[0] + "°C";
            if (parts.Length >= 2) info.Usage = parts[1] + "%";
            if (parts.Length >= 3) info.Power = parts[2] + " W";
            if (parts.Length >= 4) info.VramUsed = parts[3] + " MiB";
            if (parts.Length >= 5) info.VramTotal = parts[4] + " MiB";
            if (parts.Length >= 6) info.Fan = parts[5] + " %";
            if (parts.Length >= 7) info.ClockMHz = parts[6] + " MHz";

            // compute VRAM usage percent if possible
            if (parts.Length >= 5 && double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out double used) &&
                double.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out double total))
            {
                double pct = total > 0 ? used / total * 100.0 : 0;
                info.VramUsage = $"{pct:F0}%";
            }

            return info;
        }
        catch
        {
            return new NvidiaInfo();
        }
    }
}
