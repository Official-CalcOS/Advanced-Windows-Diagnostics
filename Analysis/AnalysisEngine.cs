// Analysis/AnalysisEngine.cs
using System;
using System.Linq;
using System.Net.NetworkInformation; // For IPStatus comparison
using System.Runtime.Versioning;
using System.Threading.Tasks;
using DiagnosticToolAllInOne.Helpers; // May need format helpers etc.
using System.Collections.Generic;    // Needed for List<>

namespace DiagnosticToolAllInOne.Analysis
{
    [SupportedOSPlatform("windows")]
    public static class AnalysisEngine
    {
        // --- Actionable Prefixes ---
        private const string PfxAction = "[ACTION REQUIRED]";
        private const string PfxCritical = "[CRITICAL]"; // For critical events/findings
        private const string PfxInvestigate = "[INVESTIGATE]";
        private const string PfxRecommend = "[RECOMMENDED]";
        private const string PfxInfo = "[INFO]";

        // --- Known Critical Event IDs (Example - Consider moving to config) ---
        // This dictionary should ideally be loaded from the AppConfiguration
        // For simplicity here, we'll keep it static but acknowledge it should come from config.
        private static readonly Dictionary<string, List<long>> KnownCriticalEvents = new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase)
        {
            { "disk", new List<long> { 7, 11, 51, 55, 153 } }, // Common disk errors
            { "Ntfs", new List<long> { 55, 137 } }, // Filesystem corruption
            { "Kernel-Power", new List<long> { 41 } }, // Unexpected shutdown/reboot
            { "Application Error", new List<long> { 1000 } }, // Generic application crash
            { "Windows Error Reporting", new List<long> { 1001 } }, // Generic fault reporting
            { "volmgr", new List<long> { 46, 161 } } // Volume manager errors
            // Add more known critical source/ID pairs here
        };


        // --- Analysis Entry Point ---
        public static Task<AnalysisSummary> PerformAnalysisAsync(DiagnosticReport report, bool isAdmin, AppConfiguration? config) // Takes AppConfiguration
        {
            // FIX CS1061: Use the 'config' parameter directly, not data.Configuration
            var currentConfig = config ?? new AppConfiguration(); // Use passed config or defaults
            var thresholds = currentConfig.AnalysisThresholds;

            var summary = new AnalysisSummary();
            // Removed assignment to summary.Configuration as it no longer exists there
            var issues = summary.PotentialIssues;
            var suggestions = summary.Suggestions;
            var info = summary.Info;
            var criticalEventsFound = summary.CriticalEventsFound; // Use the new list

            // Basic validation
            if (report == null) {
                issues.Add($"{PfxCritical} Cannot perform analysis: Diagnostic report data is missing or null.");
                return Task.FromResult(summary);
            }
             if (report.System == null && report.Hardware == null && report.Software == null &&
                 report.Security == null && report.Performance == null && report.Network == null &&
                 report.Events == null && report.Stability == null) // Added Stability check
             {
                 issues.Add($"{PfxCritical} Cannot perform analysis: No diagnostic data sections were collected or available.");
                 return Task.FromResult(summary);
             }

            try
            {
                 // --- General Info ---
                 if (!isAdmin) info.Add($"{PfxInfo} Tool not run as Administrator - Some data requires elevation (e.g., AV/Firewall state, Process IDs for Network Connections, TPM details, some hardware serial numbers, log parsing)."); // Added admin info
                 // summary.Configuration = currentConfig; // REMOVED - Property doesn't exist on summary

                 // --- System Checks ---
                 if (report.System?.OperatingSystem?.Uptime?.TotalDays > thresholds.MaxUptimeDaysSuggestion)
                     suggestions.Add($"{PfxRecommend} System uptime is high ({report.System.OperatingSystem.Uptime?.TotalDays:0} days). Consider restarting if experiencing stability or performance issues.");

                 // ADDED: Pending Reboot Check
                 if (report.System?.IsRebootPending == true)
                     info.Add($"{PfxInfo} A system reboot is pending. Consider restarting the computer to apply updates or changes.");

                 // ADDED: System Integrity Check Analysis
                 if (report.System?.SystemIntegrity != null)
                 {
                     var si = report.System.SystemIntegrity;
                     if (!string.IsNullOrEmpty(si.LogParsingError))
                     {
                         info.Add($"{PfxInfo} Could not check System Integrity results: {si.LogParsingError}");
                     }
                     else
                     {
                         if (si.SfcScanResult?.Contains("found corrupt files", StringComparison.OrdinalIgnoreCase) == true &&
                             si.SfcScanResult?.Contains("unable to fix", StringComparison.OrdinalIgnoreCase) == true)
                         {
                             issues.Add($"{PfxInvestigate} SFC scan found corrupt files it could not repair ({FormatNullableDateTimeForLog(si.LastSfcScanTime)}). Run DISM commands (ScanHealth, RestoreHealth).");
                         }
                         else if (si.SfcScanResult?.Contains("found corrupt files", StringComparison.OrdinalIgnoreCase) == true)
                         {
                             info.Add($"{PfxInfo} SFC scan found and repaired corrupt files ({FormatNullableDateTimeForLog(si.LastSfcScanTime)}).");
                         }
                         else if (si.SfcScanResult != null) // Found SFC result, no corruption
                         {
                             info.Add($"{PfxInfo} SFC scan found no integrity violations ({FormatNullableDateTimeForLog(si.LastSfcScanTime)}).");
                         }

                         if (si.DismCheckHealthResult?.Contains("repairable", StringComparison.OrdinalIgnoreCase) == true)
                         {
                             suggestions.Add($"{PfxRecommend} DISM CheckHealth indicates the component store is repairable ({FormatNullableDateTimeForLog(si.LastDismCheckTime)}). Run 'DISM /Online /Cleanup-Image /RestoreHealth'.");
                         }
                         else if (si.DismCheckHealthResult != null) // Found DISM result, no corruption detected by CheckHealth
                         {
                              info.Add($"{PfxInfo} DISM CheckHealth found no component store corruption ({FormatNullableDateTimeForLog(si.LastDismCheckTime)}).");
                         }

                         // Suggest running scans if not done recently (e.g., > 7 days)
                         var sevenDaysAgo = DateTime.Now.AddDays(-7);
                         if (si.LastSfcScanTime == null || si.LastSfcScanTime < sevenDaysAgo)
                         {
                             suggestions.Add($"{PfxRecommend} Consider running System File Checker ('sfc /scannow') as admin to check for OS file corruption.");
                         }
                     }
                 }


                 // --- Hardware Checks ---
                 if (report.Hardware?.Memory?.PercentUsed > thresholds.HighMemoryUsagePercent)
                     issues.Add($"{PfxInvestigate} High Memory Usage ({report.Hardware.Memory.PercentUsed:0}%). Consider closing unused applications or check Task Manager for memory-intensive processes.");
                 else if (report.Hardware?.Memory?.PercentUsed > thresholds.ElevatedMemoryUsagePercent)
                     suggestions.Add($"{PfxRecommend} Memory Usage is Elevated ({report.Hardware.Memory.PercentUsed:0}%). Monitor application memory consumption.");

                 if (report.Hardware?.LogicalDisks != null) {
                     foreach(var disk in report.Hardware.LogicalDisks)
                     {
                         // FIX CS8602: Add null check for PercentFree
                         if (disk.PercentFree.HasValue && disk.PercentFree < thresholds.CriticalDiskSpacePercent)
                             issues.Add($"{PfxAction} Critically Low Disk Space on {disk.DeviceID} ({disk.PercentFree:0.#}% free). Free up space immediately.");
                         else if (disk.PercentFree.HasValue && disk.PercentFree < thresholds.LowDiskSpacePercent)
                             suggestions.Add($"{PfxRecommend} Low Disk Space on {disk.DeviceID} ({disk.PercentFree:0.#}% free). Consider freeing up space.");
                     }
                 }

                 // SMART Analysis (Using IsFailurePredicted from PhysicalDiskInfo.SmartStatus)
                 if (report.Hardware?.PhysicalDisks != null) {
                     foreach (var disk in report.Hardware.PhysicalDisks) {
                          if (disk.SmartStatus?.IsFailurePredicted == true) {
                               issues.Add($"{PfxAction} SMART predicts failure for Disk #{disk.Index} ({disk.Model}). BACK UP DATA IMMEDIATELY and replace the drive.");
                          } else if (disk.SmartStatus?.StatusText?.Contains("Error", StringComparison.OrdinalIgnoreCase) == true ||
                                     disk.SmartStatus?.StatusText?.Contains("Query Error", StringComparison.OrdinalIgnoreCase) == true) {
                               info.Add($"{PfxInfo} SMART status query for Disk #{disk.Index} ({disk.Model}) encountered an error: {disk.SmartStatus.Error ?? disk.SmartStatus.StatusText}. SMART monitoring may be unavailable or failing.");
                          } else if (disk.SmartStatus?.StatusText?.Contains("Not Supported", StringComparison.OrdinalIgnoreCase) == true) {
                               info.Add($"{PfxInfo} SMART status is Not Supported or Not Reported for Disk #{disk.Index} ({disk.Model}).");
                          } else if (disk.SmartStatus?.StatusText?.Contains("Requires Admin", StringComparison.OrdinalIgnoreCase) == true) {
                               info.Add($"{PfxInfo} SMART status check for Disk #{disk.Index} ({disk.Model}) requires Administrator privileges.");
                          }
                     }
                 }


                 if (report.Hardware?.Volumes != null) {
                     foreach (var vol in report.Hardware.Volumes)
                     {
                         if (vol.DriveLetter != null && vol.IsBitLockerProtected == false && vol.ProtectionStatus == "Protection Off")
                             suggestions.Add($"{PfxRecommend} Volume {vol.DriveLetter} is not BitLocker protected. Consider enabling encryption for data security.");
                         else if (vol.DriveLetter != null && vol.ProtectionStatus == "Not Found/Not Encryptable")
                             info.Add($"{PfxInfo} BitLocker status for Volume {vol.DriveLetter} indicates it may not be encryptable or BitLocker is not installed/configured.");
                         else if (vol.DriveLetter != null && vol.ProtectionStatus == "Requires Admin")
                             info.Add($"{PfxInfo} BitLocker status check for Volume {vol.DriveLetter} requires Administrator privileges.");
                     }
                 }

                 // --- Software Checks ---
                 if (report.Software?.RelevantServices != null) {
                    foreach(var svc in report.Software.RelevantServices)
                    {
                        string[] criticalSvcs = { "Winmgmt", "RpcSs", "Schedule", "Dhcp", "Dnscache", "BFE", "EventLog" };
                        if(criticalSvcs.Contains(svc.Name, StringComparer.OrdinalIgnoreCase) && svc.State != "Running")
                        {
                            issues.Add($"{PfxAction} Critical service '{svc.DisplayName}' ({svc.Name}) is not running (State: {svc.State}, StartMode: {svc.StartMode}). System instability likely. Attempt restart or investigate dependencies.");
                        }
                        else if (svc.StartMode == "Auto" && svc.State == "Stopped")
                        {
                            suggestions.Add($"{PfxInvestigate} Service '{svc.DisplayName}' ({svc.Name}) is set to Auto start but is currently Stopped. Check if this service should be running.");
                        }

                        string path = svc.PathName?.Trim('"') ?? "";
                        bool isMicrosoftPath = path.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                                               path.StartsWith(@"C:\Windows\", StringComparison.OrdinalIgnoreCase) ||
                                               path.StartsWith(@"C:\Program Files\WindowsApps\", StringComparison.OrdinalIgnoreCase);

                        if (svc.State == "Running" && !isMicrosoftPath && !string.IsNullOrEmpty(path) && path != "N/A" && path != "Requires Admin")
                        {
                             info.Add($"{PfxInfo} Non-Microsoft service '{svc.DisplayName}' ({svc.Name}) is running from path: {path}");
                        }
                         if (svc.PathName == "Requires Admin" && svc.State == "Running") // Add info if path requires admin for running service
                         {
                             info.Add($"{PfxInfo} Cannot determine path for running service '{svc.DisplayName}' ({svc.Name}) without Administrator privileges.");
                         }
                    }
                 }

                 // --- Security Checks ---
                 if (report.Security?.UacStatus != "Enabled") issues.Add($"{PfxAction} User Account Control (UAC) is Disabled or Unknown. Strongly recommended to keep UAC Enabled for security.");
                 if (report.Security?.Antivirus?.State != null && (report.Security.Antivirus.State.Contains("Disabled", StringComparison.OrdinalIgnoreCase) || report.Security.Antivirus.State.Contains("Snoozed", StringComparison.OrdinalIgnoreCase))) issues.Add($"{PfxAction} Antivirus '{report.Security.Antivirus.Name ?? "N/A"}' appears to be disabled or snoozed. Ensure real-time protection is active.");
                  if (report.Security?.Antivirus?.State != null && report.Security.Antivirus.State.Contains("Not up-to-date", StringComparison.OrdinalIgnoreCase)) suggestions.Add($"{PfxRecommend} Antivirus '{report.Security.Antivirus.Name ?? "N/A"}' definitions may be out of date. Update antivirus software.");
                 if (report.Security?.Antivirus?.State == "Requires Admin") info.Add($"{PfxInfo} Antivirus status check requires Administrator privileges."); // Add info if requires admin

                 if (report.Security?.Firewall?.State != null && (report.Security.Firewall.State.Contains("Disabled", StringComparison.OrdinalIgnoreCase) || report.Security.Firewall.State.Contains("Snoozed", StringComparison.OrdinalIgnoreCase))) issues.Add($"{PfxAction} Firewall '{report.Security.Firewall.Name ?? "N/A"}' appears to be disabled or snoozed. Ensure the firewall is active.");
                  if (report.Security?.Firewall?.State == "Requires Admin") info.Add($"{PfxInfo} Firewall status check requires Administrator privileges."); // Add info if requires admin

                 if (report.Security?.LocalUsers?.Any(u => u.Name == "Administrator" && u.IsLocal && !u.IsDisabled) ?? false) suggestions.Add($"{PfxRecommend} Built-in Administrator account is enabled. Consider disabling it if not strictly required and use another admin account.");
                 if (report.Security?.LocalUsers?.Any(u => u.Name == "Guest" && u.IsLocal && !u.IsDisabled) ?? false) issues.Add($"{PfxAction} Guest account is enabled. Strongly recommended to keep the Guest account Disabled for security.");
                 if (report.Security?.IsSecureBootEnabled == false) suggestions.Add($"{PfxRecommend} Secure Boot is reported as Disabled. Enable Secure Boot in UEFI settings for enhanced boot security (if supported).");
                 if (report.Security?.BiosMode?.Contains("Denied") == true) info.Add($"{PfxInfo} Secure Boot status check requires Administrator privileges."); // Add info if requires admin
                 if (report.Security?.Tpm != null && !(report.Security.Tpm.IsPresent == true && report.Security.Tpm.IsEnabled == true && report.Security.Tpm.IsActivated == true)) suggestions.Add($"{PfxInvestigate} TPM status is '{report.Security.Tpm.Status}'. Ensure TPM is enabled and activated in UEFI/BIOS for features like BitLocker and Windows 11 compatibility.");
                 if (report.Security?.Tpm?.Status == "Requires Admin") info.Add($"{PfxInfo} TPM status check requires Administrator privileges."); // Add info if requires admin


                 // --- Performance Checks ---
                  if (double.TryParse(report.Performance?.OverallCpuUsagePercent?.Trim('%', ' '), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double cpu))
                  {
                    if(cpu > thresholds.HighCpuUsagePercent) issues.Add($"{PfxInvestigate} CPU usage sample was very high ({cpu:0}%). Check Task Manager for resource-intensive processes if performance is slow.");
                    else if (cpu > thresholds.ElevatedCpuUsagePercent) suggestions.Add($"{PfxRecommend} CPU usage sample was high ({cpu:0}%). Monitor CPU usage under load.");
                  } else if (!string.IsNullOrEmpty(report.Performance?.OverallCpuUsagePercent) && report.Performance.OverallCpuUsagePercent.Contains("Error")) {
                     info.Add($"{PfxInfo} Could not analyze CPU usage due to collection error: {report.Performance.OverallCpuUsagePercent}");
                  }

                 if (double.TryParse(report.Performance?.TotalDiskQueueLength, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double queue))
                 {
                    if (queue > thresholds.HighDiskQueueLength)
                      suggestions.Add($"{PfxInvestigate} Average Disk Queue Length sample ({queue:0.##}) is high. Indicates potential disk bottleneck under load. Check disk activity in Resource Monitor.");
                 } else if (!string.IsNullOrEmpty(report.Performance?.TotalDiskQueueLength) && report.Performance.TotalDiskQueueLength.Contains("Error")) {
                      info.Add($"{PfxInfo} Could not analyze Disk Queue Length due to collection error: {report.Performance.TotalDiskQueueLength}");
                 }


                 // --- Network Checks ---
                 AnalyzePingResult(report.Network?.ConnectivityTests?.GatewayPing, "Default Gateway", thresholds.MaxPingLatencyWarningMs, issues, suggestions, info);
                 if(report.Network?.ConnectivityTests?.DnsPings != null) {
                    foreach(var dnsPing in report.Network.ConnectivityTests.DnsPings) {
                        if (dnsPing == null) continue;
                        AnalyzePingResult(dnsPing, $"DNS Server {dnsPing.Target ?? "?"}", thresholds.MaxPingLatencyWarningMs, issues, suggestions, info);
                    }
                 }
                 AnalyzeDnsResolution(report.Network?.ConnectivityTests?.DnsResolution, issues, suggestions, info);
                 AnalyzeTraceroute(report.Network?.ConnectivityTests, thresholds.MaxTracerouteHopLatencyWarningMs, suggestions, info);


                 // --- Event Log Checks (Including Deeper Analysis) ---
                 AnalyzeEventLogs(report.Events, thresholds, issues, suggestions, info, criticalEventsFound);


                 // --- Stability Checks (Crash Dumps) ---
                 if (report.Stability?.RecentCrashDumps?.Any() ?? false)
                 {
                     int dumpCount = report.Stability.RecentCrashDumps.Count;
                     issues.Add($"{PfxCritical} Found {dumpCount} recent system crash dump file(s) (Minidump or MEMORY.DMP). This indicates recent BSODs or critical system/application crashes. Analyze dumps using WinDbg.");
                     // Add details of latest dump?
                     var latestDump = report.Stability.RecentCrashDumps.OrderByDescending(d => d.Timestamp).FirstOrDefault();
                     if (latestDump != null)
                     {
                         info.Add($"{PfxInfo} Latest crash dump: '{latestDump.FileName}' ({FormatNullableDateTimeForLog(latestDump.Timestamp)})");
                     }
                 }


                 // --- Correlation Example (Disk Errors in Log + SMART) ---
                 bool diskErrorsInLog = report.Events?.SystemLogEntries?
                                             .Any(e => e != null && (e.EntryType == "Error" || e.EntryType == "Warning") &&
                                                (e.Source?.Equals("disk", StringComparison.OrdinalIgnoreCase) == true ||
                                                 e.Source?.Equals("Ntfs", StringComparison.OrdinalIgnoreCase) == true ||
                                                 e.Source?.Equals("volmgr", StringComparison.OrdinalIgnoreCase) == true)) ?? false;

                 bool smartPredictsFailure = report.Hardware?.PhysicalDisks?.Any(d => d.SmartStatus?.IsFailurePredicted == true) ?? false;

                 if (smartPredictsFailure && diskErrorsInLog)
                 {
                     // Already covered by the individual SMART failure message, potentially enhance it?
                     // suggestions.Add($"{PfxInvestigate} Disk-related errors found in Event Log AND SMART predicts failure. Drive replacement highly recommended.");
                 }
                 else if (diskErrorsInLog && !smartPredictsFailure) // Only add if SMART isn't already predicting failure
                 {
                     suggestions.Add($"{PfxInvestigate} Disk-related errors/warnings found in Event Log. Check Event Viewer for details and monitor disk health closely (e.g., using manufacturer tools).");
                 }


                 // --- Driver Date Check ---
                  DateTime driverWarningDate = DateTime.Now.AddYears(-thresholds.DriverAgeWarningYears);
                  if (report.Hardware?.Gpus != null) {
                       foreach(var gpu in report.Hardware.Gpus) {
                            if (gpu.DriverDate.HasValue && gpu.DriverDate < driverWarningDate) {
                                 suggestions.Add($"{PfxRecommend} Graphics driver for '{gpu.Name}' appears old (Date: {gpu.DriverDate:yyyy-MM-dd}). Consider updating from the manufacturer's website (NVIDIA/AMD/Intel).");
                            }
                       }
                  }
                  if (report.Network?.Adapters != null) {
                     foreach (var nic in report.Network.Adapters.Where(n => n.Status == OperationalStatus.Up)) {
                         if (nic.DriverDate.HasValue && nic.DriverDate < driverWarningDate) {
                              suggestions.Add($"{PfxRecommend} Network driver for '{nic.Description}' may be old (Date: {nic.DriverDate:yyyy-MM-dd}). Consider updating from the hardware or PC manufacturer's website.");
                         } else if (nic.DriverDate == null && !(report.Network.SpecificCollectionErrors?.ContainsKey($"DriverDate_{nic.Name ?? "Unknown"} ({nic.Id ?? "?"})") ?? false)) {
                              // Add info if driver date is missing and no specific error was logged for it
                              info.Add($"{PfxInfo} Driver date for active NIC '{nic.Description}' could not be determined (check requires WMI).");
                         }
                     }
                  }


                 // --- General Suggestions ---
                 if (issues.Any() || suggestions.Any(s => s.Contains("Disk") || s.Contains("Memory") || s.Contains("CPU") || s.Contains("driver") || s.Contains("Event Log")))
                 {
                    // Suggest SFC only if not already suggested by System Integrity check
                    if (!(report.System?.SystemIntegrity?.LastSfcScanTime > DateTime.Now.AddDays(-7))) // Check if recent scan info exists
                    {
                       suggestions.Add($"{PfxRecommend} Run System File Checker: Open Command Prompt or PowerShell as Admin and type 'sfc /scannow'.");
                    }
                    suggestions.Add($"{PfxRecommend} Check for and install pending Windows Updates.");
                    suggestions.Add($"{PfxRecommend} Perform a full system scan for malware using a reputable security tool.");
                    if (!suggestions.Any(s => s.Contains("driver")))
                    {
                        suggestions.Add($"{PfxRecommend} Ensure device drivers (especially Graphics, Network, Chipset) are up-to-date from manufacturer websites.");
                    }
                 }

                 if (!issues.Any() && !suggestions.Any() && !info.Any(i => !i.StartsWith(PfxInfo + " Tool not run as Admin")))
                 {
                      info.Add($"{PfxInfo} Analysis complete. No major issues or specific suggestions generated based on collected data and configured thresholds.");
                 }

            }
            catch (Exception ex)
            {
                 summary.SectionCollectionErrorMessage = $"[CRITICAL] Error occurred during the analysis process: {ex.Message}";
                 issues.Add($"{PfxCritical} Analysis engine encountered an error: {ex.Message}. Analysis may be incomplete.");
                 // Use Logger for internal error logging
                 Helpers.Logger.LogError($"[ANALYSIS ENGINE ERROR]", ex);
            }

            // Remove duplicates before returning
            summary.PotentialIssues = summary.PotentialIssues.Distinct().ToList();
            summary.Suggestions = summary.Suggestions.Distinct().ToList();
            summary.Info = summary.Info.Distinct().ToList();
            summary.CriticalEventsFound = summary.CriticalEventsFound.Distinct().ToList();

            return Task.FromResult(summary);
        }


        // --- Analysis Helper Methods ---
        // (Include AnalyzeEventLogs, AnalyzePingResult, AnalyzeDnsResolution, AnalyzeTraceroute, FormatNullableDateTimeForLog here)
        // ... (Copy from previous snippet or file content) ...
        // Helper to analyze event logs (System & Application)
        private static void AnalyzeEventLogs(EventLogInfo? eventLogInfo, AnalysisThresholds thresholds, List<string> issues, List<string> suggestions, List<string> info, List<CriticalEventDetails> criticalEventsFound)
        {
            if (eventLogInfo == null)
            {
                info.Add($"{PfxInfo} Event Log data not collected or unavailable for analysis.");
                return;
            }

            Action<string, List<EventEntry>?, int, int> analyzeLog =
                (logName, entries, issueThreshold, suggestionThreshold) =>
            {
                if (entries == null)
                {
                    info.Add($"{PfxInfo} {logName} Event Log entries are null.");
                    return;
                }

                // Check for collector errors first (like Access Denied)
                var collectorError = entries.FirstOrDefault(e => e.Source == null && e.Message != null && (e.Message.Contains("Access Denied") || e.Message.StartsWith("Error")));
                if (collectorError != null)
                {
                    info.Add($"{PfxInfo} Could not fully analyze {logName} Event Log: {collectorError.Message}");
                    // Don't proceed with counting if access was denied
                    if (collectorError.Message.Contains("Access Denied")) return;
                }

                // Filter out collector messages/errors before counting actual events and checking critical ones
                var actualEntries = entries.Where(e => e.Source != null).ToList();
                int errorWarningCount = actualEntries.Count(e => e.EntryType == "Error" || e.EntryType == "Warning");

                // Report counts based on thresholds
                if (errorWarningCount > issueThreshold)
                    issues.Add($"{PfxInvestigate} Found {errorWarningCount} recent Error/Warning events in {logName} log. Review logs using Event Viewer for specific critical errors.");
                else if (errorWarningCount > suggestionThreshold)
                    suggestions.Add($"{PfxRecommend} Found {errorWarningCount} recent Error/Warning events in {logName} log. Review logs for details if experiencing issues.");

                // Deeper analysis for known critical events
                foreach (var entry in actualEntries)
                {
                    // FIX CS8602: Add null check for entry.Source before accessing KnownCriticalEvents
                    if (entry.Source != null && KnownCriticalEvents.TryGetValue(entry.Source, out var criticalIds) && criticalIds.Contains(entry.InstanceId))
                    {
                         // Add to the dedicated list in the summary
                         criticalEventsFound.Add(new CriticalEventDetails
                         {
                             Timestamp = entry.TimeGenerated,
                             Source = entry.Source,
                             EventID = entry.InstanceId,
                             LogName = logName,
                             MessageExcerpt = entry.Message?.Length > 100 ? entry.Message.Substring(0, 100) + "..." : entry.Message ?? ""
                         });
                         // Also add a prominent issue message
                         issues.Add($"{PfxCritical} Known critical event found in {logName} Log: Source='{entry.Source}', ID={entry.InstanceId} at {entry.TimeGenerated:yyyy-MM-dd HH:mm:ss}. Investigate immediately.");
                    }
                }
            };

            analyzeLog("System", eventLogInfo.SystemLogEntries, thresholds.MaxSystemLogErrorsIssue, thresholds.MaxSystemLogErrorsSuggestion);
            analyzeLog("Application", eventLogInfo.ApplicationLogEntries, thresholds.MaxAppLogErrorsIssue, thresholds.MaxAppLogErrorsSuggestion);
        }


        // Ping analysis helper (Added actionable prefixes)
        private static void AnalyzePingResult(PingResult? result, string targetName, long latencyWarningMs, List<string> issues, List<string> suggestions, List<string> info)
        {
            if (result == null)
            {
                info.Add($"{PfxInfo} Ping test for {targetName} was not performed or data is missing.");
                return;
            }

            if (result.Status != IPStatus.Success.ToString())
            {
                string errorDetail = string.IsNullOrEmpty(result.Error) ? "" : $" ({result.Error})";
                if (targetName.Contains("Gateway", StringComparison.OrdinalIgnoreCase)) {
                     issues.Add($"{PfxInvestigate} Ping to {targetName} ({result.Target}) failed (Status: {result.Status}{errorDetail}). Potential local network issue (cable, router, adapter config).");
                } else {
                     suggestions.Add($"{PfxInvestigate} Ping to {targetName} ({result.Target}) failed (Status: {result.Status}{errorDetail}). Potential connectivity issue.");
                }
            }
            else if (result.RoundtripTimeMs.HasValue && result.RoundtripTimeMs > latencyWarningMs)
            {
                suggestions.Add($"{PfxRecommend} Ping latency to {targetName} ({result.Target}) is high ({result.RoundtripTimeMs} ms). May indicate network congestion or issues.");
            }
            // else info.Add($"{PfxInfo} Ping to {targetName} ({result.Target}) successful ({result.RoundtripTimeMs} ms)."); // Usually too verbose
        }

         // DNS analysis helper (Added actionable prefixes)
         private static void AnalyzeDnsResolution(DnsResolutionResult? result, List<string> issues, List<string> suggestions, List<string> info)
         {
            if (result == null)
            {
                info.Add($"{PfxInfo} DNS resolution test was not performed or data is missing.");
                return;
            }

            if (!result.Success)
            {
                 string errorDetail = string.IsNullOrEmpty(result.Error) ? "" : $" ({result.Error})";
                 suggestions.Add($"{PfxInvestigate} DNS resolution for '{result.Hostname}' failed{errorDetail}. Check DNS server settings and network connectivity.");
            }
            // else info.Add($"{PfxInfo} DNS resolution for '{result.Hostname}' successful ({result.ResolutionTimeMs} ms): {string.Join(", ", result.ResolvedIpAddresses ?? new List<string>())}"); // Too verbose?
         }

        // Traceroute analysis helper (Added actionable prefixes)
        private static void AnalyzeTraceroute(NetworkTestResults? tests, long hopLatencyWarningMs, List<string> suggestions, List<string> info)
        {
            if (tests?.TracerouteResults == null || !tests.TracerouteResults.Any())
            {
                 if (!string.IsNullOrEmpty(tests?.TracerouteTarget)) info.Add($"{PfxInfo} Traceroute to {tests.TracerouteTarget} was not performed or results are unavailable.");
                 return;
            }

            bool timeoutDetected = false;

            foreach(var hop in tests.TracerouteResults)
            {
                 if (hop == null) continue;

                if (hop.Status == IPStatus.TimedOut.ToString() || hop.Address == "*") {
                    timeoutDetected = true;
                }
                if (hop.RoundtripTimeMs.HasValue && hop.RoundtripTimeMs > hopLatencyWarningMs && (hop.Status == IPStatus.TtlExpired.ToString() || hop.Status == IPStatus.Success.ToString())) {
                    suggestions.Add($"{PfxRecommend} Traceroute to {tests.TracerouteTarget} shows high latency at hop {hop.Hop} ({hop.Address ?? "?"} - {hop.RoundtripTimeMs} ms).");
                }
                if (!string.IsNullOrEmpty(hop.Error)) {
                    info.Add($"{PfxInfo} Traceroute hop {hop.Hop} encountered an error: {hop.Error}");
                }
            }

            if (timeoutDetected) {
                suggestions.Add($"{PfxInvestigate} Traceroute to {tests.TracerouteTarget} encountered timeouts. This could indicate a firewall block, packet loss, or routing issue along the path. Review the hops prior to the timeout.");
            }
        }

        // Helper to format DateTime for log messages consistently
        private static string FormatNullableDateTimeForLog(DateTime? dt)
        {
            return dt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown Time";
        }

    }
}