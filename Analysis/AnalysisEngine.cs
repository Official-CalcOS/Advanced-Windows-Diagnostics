// Analysis/AnalysisEngine.cs
using System;
using System.Linq;
using System.Net.NetworkInformation; // For IPStatus comparison
using System.Runtime.Versioning;
using System.Threading.Tasks;
using DiagnosticToolAllInOne.Helpers; // May need format helpers etc.

namespace DiagnosticToolAllInOne.Analysis
{
    [SupportedOSPlatform("windows")]
    public static class AnalysisEngine
    {
        // --- Configurable Thresholds (Example - consider loading from config file) ---
        private const double HIGH_MEMORY_USAGE_PERCENT = 90.0;
        private const double ELEVATED_MEMORY_USAGE_PERCENT = 80.0;
        private const double CRITICAL_DISK_SPACE_PERCENT = 5.0;
        private const double LOW_DISK_SPACE_PERCENT = 15.0;
        private const double HIGH_CPU_USAGE_PERCENT = 95.0;
        private const double ELEVATED_CPU_USAGE_PERCENT = 80.0;
        private const double HIGH_DISK_QUEUE_LENGTH = 5.0;
        private const int MAX_SYSTEM_LOG_ERRORS_ISSUE = 10;
        private const int MAX_SYSTEM_LOG_ERRORS_SUGGESTION = 3;
        private const int MAX_APP_LOG_ERRORS_ISSUE = 10;
        private const int MAX_APP_LOG_ERRORS_SUGGESTION = 3;
        private const int MAX_UPTIME_DAYS_SUGGESTION = 30;
        // --- ---

        public static Task<AnalysisSummary> PerformAnalysisAsync(DiagnosticReport report, bool isAdmin)
        {
            var summary = new AnalysisSummary();
            var issues = summary.PotentialIssues;
            var suggestions = summary.Suggestions;
            var info = summary.Info;

            // Basic validation
            if (report == null) {
                issues.Add("Cannot perform analysis: Diagnostic report data is missing or null.");
                return Task.FromResult(summary);
            }
             if (report.System == null && report.Hardware == null && report.Software == null &&
                 report.Security == null && report.Performance == null && report.Network == null &&
                 report.Events == null)
             {
                 issues.Add("Cannot perform analysis: No diagnostic data sections were collected or available.");
                 return Task.FromResult(summary);
             }

            try // Wrap analysis in a try/catch block
            {
                 if (!isAdmin) info.Add("Tool not run as Administrator - some checks might be incomplete or data inaccessible.");

                 // --- System Checks ---
                 if (report.System?.OperatingSystem?.Uptime?.TotalDays > MAX_UPTIME_DAYS_SUGGESTION) suggestions.Add($"System uptime is high ({report.System.OperatingSystem.Uptime?.TotalDays:0} days). Consider restarting if experiencing stability or performance issues.");

                 // --- Hardware Checks ---
                 if (report.Hardware?.Memory?.PercentUsed > HIGH_MEMORY_USAGE_PERCENT) issues.Add($"High Memory Usage ({report.Hardware.Memory.PercentUsed:0}%). Consider closing unused applications or investigate memory-hungry processes.");
                 else if (report.Hardware?.Memory?.PercentUsed > ELEVATED_MEMORY_USAGE_PERCENT) suggestions.Add($"Memory Usage is Elevated ({report.Hardware.Memory.PercentUsed:0}%). Monitor application memory consumption.");

                 if (report.Hardware?.LogicalDisks != null) {
                     foreach(var disk in report.Hardware.LogicalDisks)
                     {
                         if (disk.PercentFree < CRITICAL_DISK_SPACE_PERCENT) issues.Add($"Critically Low Disk Space on {disk.DeviceID} ({disk.PercentFree:0.#}% free). Free up space immediately.");
                         else if (disk.PercentFree < LOW_DISK_SPACE_PERCENT) suggestions.Add($"Low Disk Space on {disk.DeviceID} ({disk.PercentFree:0.#}% free). Consider freeing up space.");
                     }
                 }

                 if (report.Hardware?.PhysicalDisks != null) {
                    foreach(var disk in report.Hardware.PhysicalDisks)
                    {
                         // Check specific SMART status first
                         if (disk.SmartStatus?.IsFailurePredicted ?? false) issues.Add($"SMART predicts imminent failure for Disk #{disk.Index} ({disk.Model ?? "N/A"}). BACK UP DATA IMMEDIATELY and replace the drive. (Reason Code: {disk.SmartStatus.ReasonCode ?? "N/A"})");
                         // Check basic disk status if SMART doesn't predict failure or is unavailable
                         else if (disk.Status != null && !disk.Status.Equals("OK", StringComparison.OrdinalIgnoreCase)) suggestions.Add($"Physical Disk #{disk.Index} ({disk.Model ?? "N/A"}) reports non-OK status: '{disk.Status}'. Investigate drive health. (SMART Status: {disk.SmartStatus?.StatusText ?? "N/A"})");
                         // Add informational note if SMART is unsupported
                         else if (disk.SmartStatus?.StatusText == "Not Supported") info.Add($"SMART status query not supported for Disk #{disk.Index} ({disk.Model ?? "N/A"}). Basic Status: {disk.Status ?? "N/A"}.");
                         else if (disk.SmartStatus?.StatusText == "Query Error") info.Add($"SMART status query failed for Disk #{disk.Index} ({disk.Model ?? "N/A"}). Error: {disk.SmartStatus.Error}. Basic Status: {disk.Status ?? "N/A"}.");
                    }
                 }

                 if (report.Hardware?.Volumes != null) {
                     foreach (var vol in report.Hardware.Volumes)
                     {
                         // Check if BitLocker status indicates it's applicable and off
                         if (vol.DriveLetter != null && vol.IsBitLockerProtected == false && vol.ProtectionStatus == "Protection Off") suggestions.Add($"Volume {vol.DriveLetter} is not BitLocker protected. Consider enabling encryption for data security.");
                         // Note: "Not Found/Not Encryptable" status is informational, not necessarily a suggestion.
                         else if (vol.DriveLetter != null && vol.ProtectionStatus == "Not Found/Not Encryptable") info.Add($"BitLocker status for Volume {vol.DriveLetter} indicates it may not be encryptable or BitLocker is not installed/configured.");
                     }
                 }

                 // --- Software Checks ---
                 if (report.Software?.RelevantServices != null) {
                    foreach(var svc in report.Software.RelevantServices)
                    {
                        // Example: Check critical services status
                        string[] criticalSvcs = { "Winmgmt", "RpcSs", "Schedule", "Dhcp", "Dnscache", "BFE", "EventLog" }; // Added BFE, EventLog
                        if(criticalSvcs.Contains(svc.Name, StringComparer.OrdinalIgnoreCase) && svc.State != "Running")
                        {
                            issues.Add($"Critical service '{svc.DisplayName}' ({svc.Name}) is not running (State: {svc.State}, StartMode: {svc.StartMode}). System instability may occur.");
                        }
                        // Example: Check for non-Microsoft services running (refined path check)
                        string path = svc.PathName?.Trim('"') ?? ""; // Trim quotes
                        bool isMicrosoftPath = path.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                                               path.StartsWith(@"C:\Windows\", StringComparison.OrdinalIgnoreCase) ||
                                               path.StartsWith(@"C:\Program Files\WindowsApps\", StringComparison.OrdinalIgnoreCase); // Add common Windows app path

                        if (svc.State == "Running" && !isMicrosoftPath && !string.IsNullOrEmpty(path) && path != "N/A" && path != "Requires Admin")
                        {
                             info.Add($"Non-Microsoft service '{svc.DisplayName}' ({svc.Name}) is running from path: {path}");
                        }
                    }
                 }

                 // --- Security Checks ---
                 if (report.Security?.UacStatus != "Enabled") issues.Add("User Account Control (UAC) is Disabled or Unknown. It is strongly recommended to keep UAC Enabled for security.");
                 if (report.Security?.Antivirus?.State != null && (report.Security.Antivirus.State.Contains("Disabled", StringComparison.OrdinalIgnoreCase) || report.Security.Antivirus.State.Contains("Snoozed", StringComparison.OrdinalIgnoreCase))) issues.Add($"Antivirus '{report.Security.Antivirus.Name ?? "N/A"}' appears to be disabled or snoozed. Ensure real-time protection is active.");
                  if (report.Security?.Antivirus?.State != null && report.Security.Antivirus.State.Contains("Not up-to-date", StringComparison.OrdinalIgnoreCase)) suggestions.Add($"Antivirus '{report.Security.Antivirus.Name ?? "N/A"}' definitions may be out of date. Update antivirus software.");
                 if (report.Security?.Firewall?.State != null && (report.Security.Firewall.State.Contains("Disabled", StringComparison.OrdinalIgnoreCase) || report.Security.Firewall.State.Contains("Snoozed", StringComparison.OrdinalIgnoreCase))) issues.Add($"Firewall '{report.Security.Firewall.Name ?? "N/A"}' appears to be disabled or snoozed. Ensure the firewall is active.");
                 if (report.Security?.LocalUsers?.Any(u => u.Name == "Administrator" && u.IsLocal && !u.IsDisabled) ?? false) suggestions.Add("Built-in Administrator account is enabled. Consider disabling it if not strictly required and use another admin account.");
                 if (report.Security?.LocalUsers?.Any(u => u.Name == "Guest" && u.IsLocal && !u.IsDisabled) ?? false) issues.Add("Guest account is enabled. It is strongly recommended to keep the Guest account Disabled for security.");

                 // --- Performance Checks ---
                 // Note: These are snapshot values (or short samples), sustained high values are more concerning.
                  if (double.TryParse(report.Performance?.OverallCpuUsagePercent?.Trim('%', ' '), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double cpu))
                  {
                    if(cpu > HIGH_CPU_USAGE_PERCENT) issues.Add($"CPU usage sample was very high ({cpu:0}%). Check Task Manager for resource-intensive processes if performance is slow.");
                    else if (cpu > ELEVATED_CPU_USAGE_PERCENT) suggestions.Add($"CPU usage sample was high ({cpu:0}%). Monitor CPU usage under load.");
                  } else if (!string.IsNullOrEmpty(report.Performance?.OverallCpuUsagePercent) && report.Performance.OverallCpuUsagePercent.Contains("Error")) {
                     info.Add($"Could not analyze CPU usage due to collection error: {report.Performance.OverallCpuUsagePercent}");
                  }

                 if (double.TryParse(report.Performance?.TotalDiskQueueLength, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double queue))
                 {
                    if (queue > HIGH_DISK_QUEUE_LENGTH) // Higher threshold for modern systems/SSDs
                      suggestions.Add($"Average Disk Queue Length sample ({queue:0.##}) is high. Indicates potential disk bottleneck under load. Check disk activity in Resource Monitor.");
                 } else if (!string.IsNullOrEmpty(report.Performance?.TotalDiskQueueLength) && report.Performance.TotalDiskQueueLength.Contains("Error")) {
                      info.Add($"Could not analyze Disk Queue Length due to collection error: {report.Performance.TotalDiskQueueLength}");
                 }


                 // --- Network Checks ---
                 if (report.Network?.ConnectivityTests?.GatewayPing?.Status != null && report.Network.ConnectivityTests.GatewayPing.Status != IPStatus.Success.ToString()) issues.Add($"Ping to Default Gateway ({report.Network.ConnectivityTests.GatewayPing.Target}) failed (Status: {report.Network.ConnectivityTests.GatewayPing.Status}). Potential local network issue (cable, router, adapter config).");
                 if (report.Network?.ConnectivityTests?.DnsPings?.Any(p => p.Status != IPStatus.Success.ToString()) ?? false)
                 {
                     var failedDns = report.Network.ConnectivityTests.DnsPings.Where(p => p.Status != IPStatus.Success.ToString()).Select(p => p.Target);
                     suggestions.Add($"Ping to public DNS server(s) ({string.Join(", ", failedDns)}) failed. Potential internet connectivity or DNS resolution issue.");
                 }
                 // Add analysis for Traceroute if needed (e.g., high latency hops, timeouts)


                 // --- Event Log Checks ---
                 bool systemLogAccessible = !(report.Events?.SystemLogEntries?.Any(e => e.Message?.Contains("Access Denied") ?? false) ?? false);
                 bool appLogAccessible = !(report.Events?.ApplicationLogEntries?.Any(e => e.Message?.Contains("Access Denied") ?? false) ?? false);

                 // Count actual error/warning entries, ignoring collector messages/errors
                 int systemErrors = report.Events?.SystemLogEntries?.Count(e => e.Source != null && (e.EntryType == "Error" || e.EntryType == "Warning")) ?? 0;
                 int appErrors = report.Events?.ApplicationLogEntries?.Count(e => e.Source != null && (e.EntryType == "Error" || e.EntryType == "Warning")) ?? 0;

                 if (systemErrors > MAX_SYSTEM_LOG_ERRORS_ISSUE) issues.Add($"Found {systemErrors} recent Error/Warning events in System log. Review logs using Event Viewer for specific critical errors.");
                 else if (systemErrors > MAX_SYSTEM_LOG_ERRORS_SUGGESTION) suggestions.Add($"Found {systemErrors} recent Error/Warning events in System log. Review logs for details if experiencing issues.");
                 else if (!systemLogAccessible) info.Add("Could not fully analyze System Event Log due to access restrictions.");

                 if (appErrors > MAX_APP_LOG_ERRORS_ISSUE) issues.Add($"Found {appErrors} recent Error/Warning events in Application log. Review logs using Event Viewer for application crashes or errors.");
                 else if (appErrors > MAX_APP_LOG_ERRORS_SUGGESTION) suggestions.Add($"Found {appErrors} recent Error/Warning events in Application log. Review logs for details if applications are misbehaving.");
                  else if (!appLogAccessible) info.Add("Could not fully analyze Application Event Log due to access restrictions.");


                 // --- Correlation Example ---
                 // If disk errors were found in system event log, check SMART status again.
                 bool diskErrorsInLog = report.Events?.SystemLogEntries?
                                             .Any(e => (e.EntryType == "Error" || e.EntryType == "Warning") &&
                                                       (e.Source?.Equals("disk", StringComparison.OrdinalIgnoreCase) == true ||
                                                        e.Source?.Equals("Ntfs", StringComparison.OrdinalIgnoreCase) == true ||
                                                        e.Source?.Equals("volmgr", StringComparison.OrdinalIgnoreCase) == true)) ?? false;

                 if (diskErrorsInLog && (report.Hardware?.PhysicalDisks?.Any(d => d.SmartStatus?.StatusText != "OK" && d.SmartStatus?.StatusText != "Not Supported") ?? false))
                 {
                      issues.Add("Disk-related errors found in Event Log AND SMART status is not 'OK'. Strongly recommend checking disk health and backing up data.");
                 }
                 else if (diskErrorsInLog)
                 {
                      suggestions.Add("Disk-related errors/warnings found in Event Log. Check Event Viewer for details and monitor disk health (SMART status reported OK or was unavailable).");
                 }


                 // --- General Suggestions ---
                 if (issues.Any() || suggestions.Any(s => s.Contains("Disk") || s.Contains("Memory"))) // Trigger general suggestions on any issue or specific hardware suggestions
                 {
                    suggestions.Add("Run System File Checker: Open Command Prompt or PowerShell as Admin and type 'sfc /scannow'.");
                    suggestions.Add("Check for and install pending Windows Updates.");
                    suggestions.Add("Perform a full system scan for malware using a reputable security tool.");
                    suggestions.Add("Ensure device drivers (especially Graphics, Network, Chipset) are up-to-date from manufacturer websites.");
                 }

                 if (!issues.Any() && !suggestions.Any() && !info.Any(i => !i.StartsWith("Tool not run as Admin") && !i.Contains("SMART status query")))
                 {
                      info.Add("Analysis complete. No major issues flagged based on collected data and basic checks.");
                 }

            }
            catch (Exception ex)
            {
                 // Catch errors during the analysis process itself
                 summary.SectionCollectionErrorMessage = $"[CRITICAL] Error occurred during the analysis process: {ex.Message}";
                 issues.Add($"[CRITICAL] Analysis engine encountered an error: {ex.Message}. Analysis may be incomplete.");
                 Console.Error.WriteLine($"[ANALYSIS ENGINE ERROR]: {ex}"); // Log full exception
            }

            // Remove duplicates before returning
            summary.PotentialIssues = summary.PotentialIssues.Distinct().ToList();
            summary.Suggestions = summary.Suggestions.Distinct().ToList();
            summary.Info = summary.Info.Distinct().ToList();

            return Task.FromResult(summary);
        }
    }
}