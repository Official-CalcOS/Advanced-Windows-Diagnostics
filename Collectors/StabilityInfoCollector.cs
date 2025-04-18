using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using DiagnosticToolAllInOne.Helpers; // Assuming Logger is in Helpers

namespace DiagnosticToolAllInOne.Collectors
{
    [SupportedOSPlatform("windows")]
    public static class StabilityInfoCollector
    {
        public static Task<StabilityInfo> CollectAsync()
        {
            var stabilityInfo = new StabilityInfo();
            // Standard Windows paths for crash dumps
            string minidumpPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
            string memoryDmpPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "MEMORY.DMP");
            int maxDumpsToList = 10; // Limit how many dumps we list for performance/readability

            try
            {
                // --- Check Minidump Folder ---
                if (Directory.Exists(minidumpPath))
                {
                    try
                    {
                        // Use FileInfo to get LastWriteTime easily
                        var dumpFiles = Directory.EnumerateFiles(minidumpPath, "*.dmp")
                                                .Select(f => new FileInfo(f))
                                                .OrderByDescending(fi => fi.LastWriteTimeUtc) // Get most recent first
                                                .Take(maxDumpsToList); // Limit the number of files processed

                        stabilityInfo.RecentCrashDumps ??= new List<CrashDumpInfo>(); // Initialize list if needed

                        foreach (var file in dumpFiles)
                        {
                            stabilityInfo.RecentCrashDumps.Add(new CrashDumpInfo
                            {
                                FileName = file.Name,
                                FilePath = file.FullName,
                                Timestamp = file.LastWriteTimeUtc,
                                FileSizeBytes = file.Length
                            });
                        }
                    }
                    // Handle specific access denied errors
                    catch (UnauthorizedAccessException uaEx)
                    {
                         stabilityInfo.AddSpecificError("MinidumpAccess", $"Access Denied reading Minidump folder: {uaEx.Message}");
                         Logger.LogWarning("Access denied reading Minidump folder", uaEx); // Use Logger helper
                    }
                    // Handle other errors reading the minidump folder
                    catch (Exception ex)
                    {
                         stabilityInfo.AddSpecificError("MinidumpReadError", $"Error reading Minidump folder: {ex.Message}");
                         Logger.LogError("Error reading Minidump folder", ex); // Use Logger helper
                    }
                }
                else // Minidump folder doesn't exist
                {
                    stabilityInfo.AddSpecificError("MinidumpNotFound", $"Minidump folder not found at '{minidumpPath}'.");
                    Logger.LogInfo("Minidump folder not found."); // Use Logger helper
                }

                // --- Check for MEMORY.DMP ---
                try
                {
                    var memoryDmpInfo = new FileInfo(memoryDmpPath);
                    if (memoryDmpInfo.Exists)
                    {
                        stabilityInfo.RecentCrashDumps ??= new List<CrashDumpInfo>(); // Initialize if needed

                        // Only add if we haven't already hit the limit from minidumps
                        if (stabilityInfo.RecentCrashDumps.Count < maxDumpsToList)
                        {
                            stabilityInfo.RecentCrashDumps.Add(new CrashDumpInfo
                            {
                                FileName = memoryDmpInfo.Name,
                                FilePath = memoryDmpInfo.FullName,
                                Timestamp = memoryDmpInfo.LastWriteTimeUtc,
                                FileSizeBytes = memoryDmpInfo.Length
                            });
                            // Re-sort if needed after adding MEMORY.DMP
                            stabilityInfo.RecentCrashDumps = stabilityInfo.RecentCrashDumps.OrderByDescending(d => d.Timestamp).ToList();
                        }
                         else { Logger.LogDebug($"MEMORY.DMP found but max dump count ({maxDumpsToList}) already reached from Minidump folder.");}
                    }
                }
                 // Handle specific access denied errors
                catch (UnauthorizedAccessException uaEx)
                {
                     stabilityInfo.AddSpecificError("MemoryDmpAccess", $"Access Denied checking MEMORY.DMP: {uaEx.Message}");
                     Logger.LogWarning("Access denied checking MEMORY.DMP", uaEx); // Use Logger helper
                }
                // Handle other errors checking MEMORY.DMP
                catch (Exception ex)
                {
                     stabilityInfo.AddSpecificError("MemoryDmpCheckError", $"Error checking MEMORY.DMP: {ex.Message}");
                     Logger.LogError("Error checking MEMORY.DMP", ex); // Use Logger helper
                }

                // Log if no dumps found and no errors occurred during check
                if (stabilityInfo.RecentCrashDumps != null &&
                    !stabilityInfo.RecentCrashDumps.Any() &&
                    !(stabilityInfo.SpecificCollectionErrors?.Any() ?? false) )
                {
                    Logger.LogInfo("No recent crash dump files found in standard locations."); // Use Logger helper
                }
            }
            catch (Exception ex) // Catch unexpected errors during setup (e.g., Environment.GetFolderPath)
            {
                stabilityInfo.SectionCollectionErrorMessage = $"Critical failure during Stability Info collection setup: {ex.Message}";
                Logger.LogError("Stability Info collection failed during setup", ex); // Use Logger helper
            }

            return Task.FromResult(stabilityInfo); // Return completed task as operations are synchronous
        }
    }
}