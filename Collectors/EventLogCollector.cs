// Collectors/EventLogCollector.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using DiagnosticToolAllInOne.Helpers; // Assuming Logger is in Helpers


namespace DiagnosticToolAllInOne.Collectors
{
    [SupportedOSPlatform("windows")]
    public static class EventLogCollector
    {
        public static Task<EventLogInfo> CollectAsync()
        {
            var eventInfo = new EventLogInfo();
            try
            {
                // Collect from System and Application logs
                eventInfo.SystemLogEntries = GetRecentLogEntries("System", 20, eventInfo); // Pass eventInfo to add errors
                eventInfo.ApplicationLogEntries = GetRecentLogEntries("Application", 20, eventInfo); // Pass eventInfo
            }
            catch(Exception ex) // Catch errors in the overall collection setup
            {
                 // Use Logger
                 Logger.LogError($"[CRITICAL ERROR] Event Log Collection failed", ex);
                 eventInfo.SectionCollectionErrorMessage = $"Critical failure during Event Log collection: {ex.Message}";
            }
            // Use Task.FromResult for compatibility if method signature requires Task<>
            return Task.FromResult(eventInfo);
        }

        // Modified to accept EventLogInfo for adding specific errors
        private static List<EventEntry> GetRecentLogEntries(string logName, int count, EventLogInfo eventInfo)
        {
            var entries = new List<EventEntry>();
            string errorKeyBase = $"EventLog_{logName}"; // Base key for specific errors

            try
            {
                // Check existence first
                if (!EventLog.Exists(logName))
                {
                    string msg = $"Log '{logName}' not found.";
                    entries.Add(new EventEntry { Message = msg }); // Add message for reporting
                    eventInfo.AddSpecificError($"{errorKeyBase}_NotFound", msg); // Add specific error
                    return entries;
                }
            }
            catch (System.Security.SecurityException secEx)
            {
                 // Error checking existence (likely permissions)
                 string msg = $"Access Denied checking log '{logName}' existence. Requires Admin.";
                 entries.Add(new EventEntry { Message = msg });
                 eventInfo.AddSpecificError($"{errorKeyBase}_CheckAccessDenied", msg);
                 Logger.LogWarning($"SecurityException checking Event Log '{logName}' existence", secEx); // Use Logger
                 return entries; // Stop trying to read if we can't even check existence
            }
            catch (Exception ex)
            {
                // Other error checking existence
                string msg = $"Error checking log '{logName}' existence: {ex.Message}";
                entries.Add(new EventEntry { Message = msg });
                eventInfo.AddSpecificError($"{errorKeyBase}_CheckError", msg);
                Logger.LogError($"Error checking Event Log '{logName}' existence", ex); // Use Logger
                return entries; // Stop trying if existence check failed unexpectedly
            }

            // Try reading the log
            EventLog? eventLog = null;
            try
            {
                eventLog = new EventLog(logName);
                int maxIndex = eventLog.Entries.Count - 1;
                int found = 0;
                // Iterate backwards to get the most recent entries first
                for (int i = maxIndex; i >= 0 && found < count; i--)
                {
                    try
                    {
                        EventLogEntry entry = eventLog.Entries[i];
                        // Only collect Errors and Warnings
                        if (entry.EntryType == EventLogEntryType.Error || entry.EntryType == EventLogEntryType.Warning)
                        {
                            entries.Add(new EventEntry
                            {
                                TimeGenerated = entry.TimeGenerated,
                                EntryType = entry.EntryType.ToString(),
                                Source = entry.Source,
                                InstanceId = entry.InstanceId,
                                // Clean up message: remove newlines, trim, handle null
                                Message = entry.Message?.Replace("\r", "").Replace("\n", " ").Trim() ?? ""
                            });
                            found++;
                        }
                    }
                    catch (Exception readEx) // Ignore single entry read errors, log maybe?
                    {
                        // Log warning about single entry read error, but continue
                        Logger.LogWarning($"Error reading single event log entry from '{logName}' at index {i}: {readEx.Message}");
                        // Optionally add a specific error?
                        // eventInfo.AddSpecificError($"{errorKeyBase}_ReadEntryError_{i}", $"Failed: {readEx.Message}");
                    }
                }
                // If we finished looping and found no errors/warnings, add an informational message
                if (found == 0 && !entries.Any(e => e.Message?.StartsWith("Error") ?? false)) // Check we didn't only add error messages
                {
                    // This isn't really an error, just info
                    // entries.Add(new EventEntry { Message = $"No recent Error/Warning entries found in '{logName}'." });
                    Logger.LogDebug($"No recent Error/Warning entries found in '{logName}'.");
                }
            }
            catch (System.Security.SecurityException secEx) // Error opening/reading the log
            {
                entries.Clear(); // Clear any potentially partial list
                string msg = $"Access Denied reading '{logName}'. Requires Admin.";
                entries.Add(new EventEntry { Message = msg });
                eventInfo.AddSpecificError($"{errorKeyBase}_ReadAccessDenied", msg);
                Logger.LogWarning($"SecurityException reading Event Log '{logName}'", secEx); // Use Logger
            }
            catch (Exception ex) // Other errors opening/reading the log
            {
                 entries.Clear();
                 string msg = $"Error reading '{logName}': {ex.Message}";
                 entries.Add(new EventEntry { Message = msg });
                 eventInfo.AddSpecificError($"{errorKeyBase}_ReadError", msg);
                 Logger.LogError($"Error reading Event Log '{logName}'", ex); // Use Logger
            }
            finally
            {
                 eventLog?.Dispose(); // Ensure disposal
            }
            return entries;
        }
    }
}
