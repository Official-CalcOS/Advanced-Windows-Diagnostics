// Collectors/PerformanceInfoCollector.cs
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
        private const int CounterSampleDelayMs = 500; // ms delay for counter sampling
        private const int TopProcessCount = 10;       // Number of top processes to retrieve

        public static async Task<PerformanceInfo> CollectAsync()
        {
            var perfInfo = new PerformanceInfo();
            try
            {
                Logger.LogDebug("Starting performance counter collection...");
                // --- Basic Counters (Run in parallel) ---
                var cpuTask = PerformanceHelper.GetSampledCounterValueAsync("Processor", "% Processor Time", "_Total", CounterSampleDelayMs);
                var memTask = PerformanceHelper.GetSampledCounterValueAsync("Memory", "Available MBytes", null, CounterSampleDelayMs); // Available MBytes is usually reliable
                var diskQueueTask = PerformanceHelper.GetSampledCounterValueAsync("PhysicalDisk", "Avg. Disk Queue Length", "_Total", CounterSampleDelayMs);

                await Task.WhenAll(cpuTask, memTask, diskQueueTask);
                Logger.LogDebug("Basic performance counters collected.");

                perfInfo.OverallCpuUsagePercent = cpuTask.Result;
                perfInfo.AvailableMemoryMB = memTask.Result;
                perfInfo.TotalDiskQueueLength = diskQueueTask.Result;

                // Log specific errors only if the result string indicates an error
                if (perfInfo.OverallCpuUsagePercent?.Contains("Error", StringComparison.OrdinalIgnoreCase) == true)
                    perfInfo.AddSpecificError("PerfCounter_CPU", perfInfo.OverallCpuUsagePercent);
                if (perfInfo.AvailableMemoryMB?.Contains("Error", StringComparison.OrdinalIgnoreCase) == true)
                    perfInfo.AddSpecificError("PerfCounter_Mem", perfInfo.AvailableMemoryMB);
                if (perfInfo.TotalDiskQueueLength?.Contains("Error", StringComparison.OrdinalIgnoreCase) == true)
                    perfInfo.AddSpecificError("PerfCounter_DiskQueue", perfInfo.TotalDiskQueueLength);

                // --- Top Processes ---
                Logger.LogDebug("Collecting top processes by memory...");
                // Pass perfInfo to allow adding specific errors during process collection
                perfInfo.TopMemoryProcesses = GetTopProcesses(TopProcessCount, perfInfo, ProcessSortCriteria.Memory);
                Logger.LogDebug($"Collected {perfInfo.TopMemoryProcesses?.Count ?? 0} top memory processes.");

                Logger.LogDebug("Collecting top processes by CPU time...");
                perfInfo.TopCpuProcesses = GetTopProcesses(TopProcessCount, perfInfo, ProcessSortCriteria.CpuTime);
                Logger.LogDebug($"Collected {perfInfo.TopCpuProcesses?.Count ?? 0} top CPU processes.");

            }
            catch (Exception ex) // Catch errors during setup/overall collection
            {
                Logger.LogError("[ERROR] Performance Info Collection failed", ex);
                perfInfo.SectionCollectionErrorMessage = $"Critical failure during performance collection: {ex.Message}";
            }
            return perfInfo;
        }

        private enum ProcessSortCriteria { Memory, CpuTime }

        // Refined helper for getting top process info
        private static List<ProcessUsageInfo> GetTopProcesses(int count, PerformanceInfo perfInfo, ProcessSortCriteria criteria)
        {
            var topProcessesInfo = new List<ProcessUsageInfo>();
            var processDataList = new List<(Process Process, long SortMetric, long WorkingSet, long CpuTimeMs, string? Error)>();
            Process[]? allProcesses = null;

            try
            {
                allProcesses = Process.GetProcesses();
                Logger.LogDebug($"Retrieved {allProcesses.Length} processes.");

                // Step 1: Gather raw data safely, handling potential errors per process
                foreach (var p in allProcesses)
                {
                    long workingSet = -1;
                    long cpuTimeMs = -1;
                    long sortMetric = -1;
                    string? initialError = null;
                    bool skipProcess = false;
                    Process? processRef = null; // Use nullable ref

                    try
                    {
                        processRef = p;
                        if (processRef == null || processRef.HasExited)
                        {
                            initialError = "Process Exited";
                            skipProcess = true;
                        }
                        else
                        {
                            try { workingSet = processRef.WorkingSet64; }
                            catch (Exception ex) {
                                workingSet = -1; initialError = $"WorkingSet Error: {ex.GetType().Name}";
                                // FIX CS8602: Use null-conditional operator
                                Logger.LogDebug($"Failed WorkingSet for PID {processRef?.Id ?? -1}: {ex.Message}");
                            }

                            try { cpuTimeMs = (long)processRef.TotalProcessorTime.TotalMilliseconds; }
                            catch (Exception ex) {
                                cpuTimeMs = -1; initialError = initialError ?? $"CpuTime Error: {ex.GetType().Name}";
                                // FIX CS8602: Use null-conditional operator
                                Logger.LogDebug($"Failed TotalProcessorTime for PID {processRef?.Id ?? -1}: {ex.Message}");
                             }

                            sortMetric = (criteria == ProcessSortCriteria.CpuTime) ? cpuTimeMs : workingSet;
                        }
                    }
                    catch (InvalidOperationException) { initialError = "Process Exited"; skipProcess = true; }
                    catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
                    { initialError = "Access Denied"; skipProcess = true; Logger.LogWarning($"Access Denied getting initial info for PID {processRef?.Id ?? -1}"); }
                    catch (Exception ex)
                    { initialError = $"Unexpected Error: {ex.GetType().Name}"; skipProcess = true; Logger.LogWarning($"Unexpected error getting initial info for PID {processRef?.Id ?? -1}", ex); }

                    if (!skipProcess && processRef != null)
                    {
                        processDataList.Add((processRef, sortMetric, workingSet, cpuTimeMs, initialError));
                    }
                    else
                    {
                        try { p?.Dispose(); } catch { /* Ignore disposal errors */ }
                    }
                } // End foreach

                // Step 2: Sort the collected data and take the top 'count'
                var sortedProcesses = processDataList
                    .Where(pd => pd.SortMetric >= 0)
                    .OrderByDescending(pd => pd.SortMetric)
                    .Take(count)
                    .ToList();

                Logger.LogDebug($"Sorted and took top {sortedProcesses.Count} processes based on {criteria}.");

                // Step 3: Populate the final ProcessUsageInfo list
                foreach (var pd in sortedProcesses)
                {
                    var proc = pd.Process; // Use non-nullable Process from the tuple
                    var info = new ProcessUsageInfo
                    {
                        Pid = proc.Id, // Safe to access Id here
                        Name = "N/A",
                        Status = "Unknown",
                        Error = pd.Error
                    };

                    try
                    {
                        if (proc.HasExited)
                        {
                            info.Status = "Exited";
                            info.Error = info.Error ?? "Process Exited after initial check";
                        }
                        else
                        {
                            info.Name = proc.ProcessName;
                            info.Status = "Running";
                            info.WorkingSetBytes = pd.WorkingSet >= 0 ? pd.WorkingSet : (long?)null;
                            info.TotalProcessorTimeMs = pd.CpuTimeMs >= 0 ? pd.CpuTimeMs : (long?)null;

                            if (info.WorkingSetBytes == null && criteria == ProcessSortCriteria.Memory && info.Error == null)
                                info.Error = "Working Set unavailable";
                            if (info.TotalProcessorTimeMs == null && criteria == ProcessSortCriteria.CpuTime && info.Error == null)
                                info.Error = "CPU Time unavailable";
                        }
                    }
                    catch (InvalidOperationException) { info.Status = "Exited"; info.Error = info.Error ?? "Process Exited during property access"; }
                    catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5) { info.Status = "Inaccessible"; info.Error = info.Error ?? "Access Denied"; }
                    catch (Exception ex)
                    {
                        info.Status = "Error"; info.Error = info.Error ?? $"Error accessing properties: {ex.GetType().Name}";
                        Logger.LogWarning($"Error getting details for PID {info.Pid} ({info.Name ?? "?"})", ex);
                    }

                    if (!string.IsNullOrEmpty(info.Error))
                    {
                        perfInfo.AddSpecificError($"Top{criteria}Process_{info.Pid}", $"{info.Name ?? "Unknown"}: {info.Error}");
                    }
                    topProcessesInfo.Add(info);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed during top process collection", ex);
                perfInfo.AddSpecificError($"Top{criteria}Processes_Overall", $"Failed: {ex.Message}");
                topProcessesInfo.Add(new ProcessUsageInfo { Error = $"Failed to list/process processes: {ex.Message}" });
            }
            finally
            {
                if (allProcesses != null) { foreach (var p in allProcesses) { try { p?.Dispose(); } catch { /* Ignore */ } } }
                foreach (var pd in processDataList) { try { pd.Process?.Dispose(); } catch { /* Ignore */ } }
                Logger.LogDebug("Finished GetTopProcesses.");
            }
            return topProcessesInfo;
        }
    }
}