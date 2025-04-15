using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using DiagnosticToolAllInOne.Helpers;


namespace DiagnosticToolAllInOne.Collectors
{
    [SupportedOSPlatform("windows")]
    public static class PerformanceInfoCollector
    {
        private const int CounterSampleDelayMs = 500; // Delay between counter samples

        public static async Task<PerformanceInfo> CollectAsync()
        {
             var perfInfo = new PerformanceInfo();
             try
             {
                 // --- Basic Counters - Sampled over a short interval ---
                 var cpuTask = PerformanceHelper.GetSampledCounterValueAsync("Processor", "% Processor Time", "_Total", CounterSampleDelayMs);
                 var memTask = PerformanceHelper.GetSampledCounterValueAsync("Memory", "Available MBytes", null, CounterSampleDelayMs); // Available MBytes usually doesn't need sampling, but keep consistent
                 var diskTask = PerformanceHelper.GetSampledCounterValueAsync("PhysicalDisk", "Avg. Disk Queue Length", "_Total", CounterSampleDelayMs);

                 await Task.WhenAll(cpuTask, memTask, diskTask);

                 perfInfo.OverallCpuUsagePercent = cpuTask.Result;
                 perfInfo.AvailableMemoryMB = memTask.Result;
                 perfInfo.TotalDiskQueueLength = diskTask.Result;

                 // --- Top Processes by Memory (Working Set) ---
                 // This is still a snapshot, as getting average memory usage is more complex
                 perfInfo.TopMemoryProcesses = GetProcessUsageInfo(p => p.WorkingSet64, 10);

                 // --- Top Processes by CPU ---
                 // Getting accurate CPU % per process requires more complex sampling or WMI Process calls
                 // This remains TotalProcessorTime snapshot based
                 perfInfo.TopCpuProcesses = GetProcessUsageInfo(p => (long)(p.TotalProcessorTime.TotalMilliseconds), 10, true);
             }
              catch(Exception ex)
             {
                  Console.Error.WriteLine($"[ERROR] Performance Info Collection failed: {ex.Message}");
                  perfInfo.SectionCollectionErrorMessage = ex.Message;
             }
            return perfInfo;
        }

        // Helper for process info gathering - Copied from original Program.cs (Consider moving to ProcessHelper)
        private static List<ProcessUsageInfo> GetProcessUsageInfo(Func<Process, long> orderByMetric, int count, bool isCpuTime = false)
        {
            var processInfos = new List<ProcessUsageInfo>();
            List<Process>? processes = null;
            try
            {
                // Wrap individual process access in try-catch
                 processes = Process.GetProcesses()
                     .Select(p => {
                         try { return new { Process = p, Metric = orderByMetric(p) }; }
                         catch { return new { Process = p, Metric = 0L }; } // Handle processes that exit or deny access
                     })
                     .OrderByDescending(x => x.Metric)
                     .Select(x => x.Process)
                     .Take(count * 2) // Take a bit more initially in case some fail later
                     .ToList();


                int collectedCount = 0;
                foreach (var p in processes)
                {
                    if (collectedCount >= count) { p.Dispose(); continue; } // Dispose excess processes

                    var info = new ProcessUsageInfo { Pid = p.Id };
                    bool success = false;
                    try
                    {
                        info.Name = p.ProcessName;
                        info.MemoryUsage = FormatHelper.FormatBytes((ulong)p.WorkingSet64);
                        // Responding check can be unreliable, consider removing or using cautiously
                        // info.Status = p.Responding ? "Running" : "Not Responding";
                        info.Status = "Running"; // Assume running if accessible
                        success = true;
                    }
                    catch (InvalidOperationException) { info.Error = "Process has exited"; info.Status = "Exited"; }
                    catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5) { info.Error = "Access Denied"; info.Status = "Inaccessible"; } // Access is denied
                    catch (Exception ex) { info.Error = $"Error: {ex.GetType().Name}"; info.Status = "Error"; }
                    finally
                    {
                        // Add if we got basic info or a specific error status
                        if (success || !string.IsNullOrEmpty(info.Error))
                        {
                            processInfos.Add(info);
                            if(success) collectedCount++;
                        }
                        p.Dispose(); // Dispose the process object
                    }
                }
            }
            catch (Exception ex) // Catch errors getting the process list itself
            {
                Console.Error.WriteLine($"[ERROR] Failed to list processes: {ex.Message}");
                processInfos.Add(new ProcessUsageInfo { Error = $"Failed to list processes: {ex.Message}" });
            }
            finally
            {
                 // Ensure any remaining process objects in the initial list are disposed if loop exited early
                 processes?.Where(p => !p.HasExited).ToList().ForEach(p => { try { p.Dispose();} catch {} });
            }
            // Return only the requested count, prioritizing successfully collected ones
            return processInfos.OrderBy(pi => !string.IsNullOrEmpty(pi.Error)).Take(count).ToList();
        }
    }
}