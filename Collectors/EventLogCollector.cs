using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;

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
                eventInfo.SystemLogEntries = GetRecentLogEntries("System", 20);
                eventInfo.ApplicationLogEntries = GetRecentLogEntries("Application", 20);
            }
            catch(Exception ex) // Catch errors in the overall collection setup
            {
                 Console.Error.WriteLine($"[CRITICAL ERROR] Event Log Collection failed: {ex.Message}");
                 // Use the new property name
                 eventInfo.SectionCollectionErrorMessage = $"Critical failure during Event Log collection: {ex.Message}";
            }
            return Task.FromResult(eventInfo);
        }

        private static List<EventEntry> GetRecentLogEntries(string logName, int count)
        {
            var entries = new List<EventEntry>();
            try
            {
                // Check existence first
                if (!EventLog.Exists(logName))
                {
                    entries.Add(new EventEntry { Message = $"Log '{logName}' not found." });
                    return entries;
                }
            }
            catch (Exception ex)
            {
                // Error checking existence (permissions, etc.)
                entries.Add(new EventEntry { Message = $"Error checking log '{logName}' existence: {ex.Message}" });
                return entries;
            }

            // Try reading the log
            try
            {
                using var eventLog = new EventLog(logName);
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
                        Console.Error.WriteLine($"[WARN] Error reading single event log entry from '{logName}': {readEx.Message}");
                        // Optionally add a placeholder entry? entries.Add(new EventEntry { Message = "Error reading an entry." });
                    }
                }
                // If we finished looping and found no errors/warnings, add a message
                if (found == 0 && !entries.Any(e => e.Message?.StartsWith("Error") ?? false)) // Check we didn't only add error messages
                {
                    entries.Add(new EventEntry { Message = $"No recent Error/Warning entries found in '{logName}'." });
                }
            }
            catch (System.Security.SecurityException)
            {
                entries.Clear(); // Clear any potentially partial list
                entries.Add(new EventEntry { Message = $"Access Denied reading '{logName}'. Requires Admin." });
            }
            catch (Exception ex)
            {
                 entries.Clear();
                 entries.Add(new EventEntry { Message = $"Error reading '{logName}': {ex.Message}" });
            }
            return entries;
        }
    }
}