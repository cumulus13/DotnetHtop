using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

// Define the classes to represent the structure of the config.json file
public class ColorMapping
{
    public required int Percentage { get; set; }
    public required string BackgroundColor { get; set; }
    public required string ForegroundColor { get; set; }
}

public class Config
{
    public required List<ColorMapping> CpuThresholds { get; set; }
    public required List<ColorMapping> MemoryThresholds { get; set; }
    public required string DefaultForegroundColor { get; set; }
}

public class ProcessInfo
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public required double CpuUsage { get; set; }
    public required long MemoryUsageMb { get; set; }
}

public class Program
{
    private static Config _config = GetDefaultConfig();
    private static ConsoleColor _defaultForegroundColor;
    private static bool _sortByCpu = true;
    private static bool _sortDescending = true;
    private static PerformanceCounter? _cpuCounter;
    private static PerformanceCounter? _memoryCounter;
    private static ulong _totalPhysicalMemoryMb;

    private static (ConsoleColor background, ConsoleColor foreground) GetColors(double usagePercentage, List<ColorMapping> thresholds)
    {
        var colorMap = thresholds.FirstOrDefault(t => usagePercentage >= t.Percentage);

        if (colorMap != null)
        {
            Enum.TryParse(colorMap.BackgroundColor, out ConsoleColor backgroundColor);
            Enum.TryParse(colorMap.ForegroundColor, out ConsoleColor foregroundColor);
            return (backgroundColor, foregroundColor);
        }

        return (ConsoleColor.Black, _defaultForegroundColor);
    }

    public static void Main(string[] args)
    {
        try
        {
            // Initialize performance counters
            InitializePerformanceCounters();
            
            // Load configuration from config.json
            LoadConfig();

            int width = Console.WindowWidth;

            // Print static header and instructions only once
            Console.Title = ".NET Process Monitor";
            PrintHeader(width);
            
            // Capture the starting cursor position for the dynamic content
            int startingCursorTop = Console.CursorTop;

            // Main monitoring loop
            MonitorProcesses(startingCursorTop);
        }
        finally
        {
            // Cleanup resources
            Cleanup();
        }
    }

    private static void PrintHeader(int width)
    {
        string separator = width >= 25 ? new string('-', width) : new string('-', 80);
        
        Console.WriteLine(separator);
        Console.WriteLine("Press 'Q' to quit, 'C' to sort by CPU, 'M' to sort by Memory.");
        Console.WriteLine("Press 'A' for ascending, 'D' for descending.");
        Console.WriteLine(separator);
        Console.WriteLine($"{"PID",-8} {"Process Name",-40} {"CPU %",-10} {"Memory (MB)",-15}");
        Console.WriteLine(separator);
    }

    private static void MonitorProcesses(int startingCursorTop)
    {
        // Reusable collections to reduce allocations
        var processInfos = new List<ProcessInfo>(100);
        var cpuTimes1 = new Dictionary<int, TimeSpan>(100);
        var cpuTimes2 = new Dictionary<int, TimeSpan>(100);
        
        int iterationCount = 0;

        while (true)
        {
            try
            {
                // Set the cursor position to the start of the dynamic content area
                Console.SetCursorPosition(0, startingCursorTop);

                // Clear previous data
                cpuTimes1.Clear();
                cpuTimes2.Clear();
                processInfos.Clear();

                // Get a snapshot of all processes and their initial CPU times
                Process[] processes = Process.GetProcesses();
                
                foreach (var process in processes)
                {
                    try
                    {
                        cpuTimes1[process.Id] = process.TotalProcessorTime;
                    }
                    catch (Exception)
                    {
                        // Ignore processes we can't access
                    }
                    finally
                    {
                        // Don't dispose here as we need the process object later
                    }
                }

                // Wait for 1 second to get a new snapshot
                Thread.Sleep(1000);

                // Get a new list of processes and their new CPU times
                // Reuse the same array reference
                foreach (var process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            cpuTimes2[process.Id] = process.TotalProcessorTime;
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore processes we can't access or that have exited
                    }
                }

                // Calculate the CPU and memory usage for each process
                foreach (var process in processes)
                {
                    try
                    {
                        if (process.HasExited)
                            continue;

                        // Calculate CPU usage as a percentage
                        double cpuUsage = 0;
                        if (cpuTimes1.TryGetValue(process.Id, out var time1) && 
                            cpuTimes2.TryGetValue(process.Id, out var time2))
                        {
                            var timeElapsed = time2 - time1;
                            cpuUsage = Math.Min(timeElapsed.TotalMilliseconds / 10.0 / Environment.ProcessorCount, 100.0);
                        }
                        
                        // Calculate memory usage in MB
                        var memoryUsageMb = process.WorkingSet64 / (1024 * 1024);

                        processInfos.Add(new ProcessInfo
                        {
                            Id = process.Id,
                            Name = process.ProcessName,
                            CpuUsage = cpuUsage,
                            MemoryUsageMb = memoryUsageMb
                        });
                    }
                    catch (Exception)
                    {
                        // Catch processes that have exited during the refresh
                    }
                }

                // Dispose all process objects
                foreach (var process in processes)
                {
                    process?.Dispose();
                }

                // Sort the process list based on user selection
                IOrderedEnumerable<ProcessInfo> sortedProcesses;
                if (_sortByCpu)
                {
                    sortedProcesses = _sortDescending
                        ? processInfos.OrderByDescending(p => p.CpuUsage)
                        : processInfos.OrderBy(p => p.CpuUsage);
                }
                else
                {
                    sortedProcesses = _sortDescending
                        ? processInfos.OrderByDescending(p => p.MemoryUsageMb)
                        : processInfos.OrderBy(p => p.MemoryUsageMb);
                }

                // Display the sorted list with colors
                DisplayProcessList(sortedProcesses, startingCursorTop);

                // Check for user input
                if (Console.KeyAvailable)
                {
                    if (!HandleUserInput())
                        break; // Exit if user pressed 'Q'
                }

                // Periodic garbage collection (every 10 iterations)
                iterationCount++;
                if (iterationCount % 10 == 0)
                {
                    GC.Collect(0, GCCollectionMode.Optimized);
                }
            }
            catch (Exception ex)
            {
                Console.SetCursorPosition(0, startingCursorTop);
                Console.WriteLine($"Error in monitoring loop: {ex.Message}");
                Thread.Sleep(2000);
            }
        }
    }

    private static void DisplayProcessList(IOrderedEnumerable<ProcessInfo> sortedProcesses, int startingCursorTop)
    {
        int linesWritten = 0;
        int maxLines = Console.WindowHeight - startingCursorTop - 1;
        
        foreach (var info in sortedProcesses.Take(maxLines))
        {
            var (cpuBackground, cpuForeground) = GetColors(info.CpuUsage, _config.CpuThresholds);
            
            // Calculate memory usage percentage
            double memoryPercentage = _totalPhysicalMemoryMb > 0 
                ? ((double)info.MemoryUsageMb / _totalPhysicalMemoryMb) * 100.0 
                : 0;
            var (memBackground, memForeground) = GetColors(memoryPercentage, _config.MemoryThresholds);
            
            // Set the default console colors
            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = _defaultForegroundColor;

            // Format and print the row with colors
            Console.Write($"{info.Id,-8}");
            Console.Write($"{info.Name.Truncate(40),-40}");
            
            // Apply CPU colors
            Console.BackgroundColor = cpuBackground;
            Console.ForegroundColor = cpuForeground;
            Console.Write($"{info.CpuUsage:F2}%".PadRight(10));
            
            // Apply Memory colors
            Console.BackgroundColor = memBackground;
            Console.ForegroundColor = memForeground;
            Console.Write($"{info.MemoryUsageMb} MB".PadRight(15));
            
            // Reset colors and move to the next line
            Console.ResetColor();
            Console.WriteLine();
            linesWritten++;
        }
        
        // Clear any remaining lines from the previous update
        for (int i = linesWritten; i < maxLines; i++)
        {
            Console.WriteLine(new string(' ', Console.WindowWidth));
        }
    }

    private static bool HandleUserInput()
    {
        var key = Console.ReadKey(true).KeyChar;
        switch (char.ToUpper(key))
        {
            case 'Q':
                Console.Clear();
                Console.WriteLine("Exiting application...");
                return false;
            case 'C':
                _sortByCpu = true;
                break;
            case 'M':
                _sortByCpu = false;
                break;
            case 'D':
                _sortDescending = true;
                break;
            case 'A':
                _sortDescending = false;
                break;
        }
        return true;
    }

    private static void InitializePerformanceCounters()
    {
        try
        {
            // Get total physical memory using WMI or Performance Counter
            _totalPhysicalMemoryMb = GetTotalPhysicalMemory();
            
            // Initialize memory counter for available memory tracking (optional)
            _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
        }
        catch (Exception)
        {
            _totalPhysicalMemoryMb = 8192; // Default to 8GB if we can't determine
        }
    }

    private static ulong GetTotalPhysicalMemory()
    {
        try
        {
            // Method 1: Using GC.GetGCMemoryInfo (most reliable for .NET 8)
            var gcMemoryInfo = GC.GetGCMemoryInfo();
            if (gcMemoryInfo.TotalAvailableMemoryBytes > 0)
            {
                return (ulong)(gcMemoryInfo.TotalAvailableMemoryBytes / (1024 * 1024));
            }
        }
        catch { }

        try
        {
            // Method 2: Using Performance Counter
            using var totalMemoryCounter = new PerformanceCounter("Memory", "Available Bytes");
            var availableBytes = (long)totalMemoryCounter.NextValue();
            
            // Get commit limit which approximates total physical + page file
            using var commitLimitCounter = new PerformanceCounter("Memory", "Commit Limit");
            var commitLimit = (long)commitLimitCounter.NextValue();
            
            // Estimate total physical memory
            return (ulong)(commitLimit / (1024 * 1024));
        }
        catch { }

        // Method 3: Fallback to default
        return 8192; // 8GB default
    }

    private static void LoadConfig()
    {
        try
        {
            string exeDir = AppContext.BaseDirectory;
            string configPath = Path.Combine(exeDir, "config.json");
            var jsonString = File.ReadAllText(configPath);
            
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            
            var loadedConfig = JsonSerializer.Deserialize<Config>(jsonString, options);
            if (loadedConfig != null)
            {
                _config = loadedConfig;
            }
            
            // Sort thresholds in descending order
            _config.CpuThresholds = _config.CpuThresholds.OrderByDescending(t => t.Percentage).ToList();
            _config.MemoryThresholds = _config.MemoryThresholds.OrderByDescending(t => t.Percentage).ToList();

            Enum.TryParse(_config.DefaultForegroundColor, out _defaultForegroundColor);
        }
        catch (FileNotFoundException)
        {
            Console.WriteLine("config.json not found. Using default values.");
            _config = GetDefaultConfig();
            _defaultForegroundColor = ConsoleColor.Cyan;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Error reading config.json: {ex.Message}. Using default values.");
            _config = GetDefaultConfig();
            _defaultForegroundColor = ConsoleColor.Cyan;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An unexpected error occurred: {ex.Message}. Using default values.");
            _config = GetDefaultConfig();
            _defaultForegroundColor = ConsoleColor.Cyan;
        }
    }

    private static Config GetDefaultConfig()
    {
        return new Config
        {
            CpuThresholds = new List<ColorMapping>
            {
                new ColorMapping { Percentage = 95, BackgroundColor = "Red", ForegroundColor = "White" },
                new ColorMapping { Percentage = 85, BackgroundColor = "Magenta", ForegroundColor = "White" },
                new ColorMapping { Percentage = 75, BackgroundColor = "Yellow", ForegroundColor = "Black" },
                new ColorMapping { Percentage = 60, BackgroundColor = "DarkYellow", ForegroundColor = "Black" },
                new ColorMapping { Percentage = 49, BackgroundColor = "Green", ForegroundColor = "Black" }
            },
            MemoryThresholds = new List<ColorMapping>
            {
                new ColorMapping { Percentage = 95, BackgroundColor = "Red", ForegroundColor = "White" },
                new ColorMapping { Percentage = 85, BackgroundColor = "Magenta", ForegroundColor = "White" },
                new ColorMapping { Percentage = 75, BackgroundColor = "Yellow", ForegroundColor = "Black" },
                new ColorMapping { Percentage = 60, BackgroundColor = "DarkYellow", ForegroundColor = "Black" },
                new ColorMapping { Percentage = 49, BackgroundColor = "Green", ForegroundColor = "Black" }
            },
            DefaultForegroundColor = "Cyan"
        };
    }

    private static void Cleanup()
    {
        try
        {
            _cpuCounter?.Dispose();
            _memoryCounter?.Dispose();
            
            // Final garbage collection on exit
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            Console.ResetColor();
        }
        catch (Exception)
        {
            // Ignore cleanup errors
        }
    }
}

public static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}