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
        // --- Analysis Entry Point ---
        // Now accepts AppConfiguration for thresholds
        public static Task<AnalysisSummary> PerformAnalysisAsync(DiagnosticReport report, bool isAdmin, AppConfiguration? config)
        {
            // Use provided config or default if null
            var currentConfig = config ?? new AppConfiguration();
            var thresholds = currentConfig.AnalysisThresholds; // Convenience accessor

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
                 summary.Configuration = currentConfig; // Add config used to summary for reference

                 // --- System Checks ---
                 // Use threshold from config
                 if (report.System?.OperatingSystem?.Uptime?.TotalDays > thresholds.MaxUptimeDaysSuggestion)
                     suggestions.Add($"System uptime is high ({report.System.OperatingSystem.Uptime?.TotalDays:0} days). Consider restarting if experiencing stability or performance issues.");

                 // --- Hardware Checks ---
                 // Use thresholds from config
                 if (report.Hardware?.Memory?.PercentUsed > thresholds.HighMemoryUsagePercent)
                     issues.Add($"High Memory Usage ({report.Hardware.Memory.PercentUsed:0}%). Consider closing unused applications or investigate memory-hungry processes.");
                 else if (report.Hardware?.Memory?.PercentUsed > thresholds.ElevatedMemoryUsagePercent)
                     suggestions.Add($"Memory Usage is Elevated ({report.Hardware.Memory.PercentUsed:0}%). Monitor application memory consumption.");

                 if (report.Hardware?.LogicalDisks != null) {
                     foreach(var disk in report.Hardware.LogicalDisks)
                     {
                         // Use thresholds from config
                         if (disk.PercentFree < thresholds.CriticalDiskSpacePercent)
                             issues.Add($"Critically Low Disk Space on {disk.DeviceID} ({disk.PercentFree:0.#}% free). Free up space immediately.");
                         else if (disk.PercentFree < thresholds.LowDiskSpacePercent)
                             suggestions.Add($"Low Disk Space on {disk.DeviceID} ({disk.PercentFree:0.#}% free). Consider freeing up space.");
                     }
                 }

                 if (report.Hardware?.PhysicalDisks != null) {
                    foreach(var disk in report.Hardware.PhysicalDisks)
                    {
                         // Check specific SMART status first
                         if (disk.SmartStatus?.IsFailurePredicted ?? false)
                             issues.Add($"SMART predicts imminent failure for Disk #{disk.Index} ({disk.Model ?? "N/A"}). BACK UP DATA IMMEDIATELY and replace the drive. (Reason Code: {disk.SmartStatus.ReasonCode ?? "N/A"})");
                         // Check basic disk status if SMART doesn't predict failure or is unavailable
                         else if (disk.Status != null && !disk.Status.Equals("OK", StringComparison.OrdinalIgnoreCase))
                             suggestions.Add($"Physical Disk #{disk.Index} ({disk.Model ?? "N/A"}) reports non-OK status: '{disk.Status}'. Investigate drive health. (SMART Status: {disk.SmartStatus?.StatusText ?? "N/A"})");
                         // Add informational note if SMART is unsupported
                         else if (disk.SmartStatus?.StatusText == "Not Supported")
                             info.Add($"SMART status query not supported for Disk #{disk.Index} ({disk.Model ?? "N/A"}). Basic Status: {disk.Status ?? "N/A"}.");
                         else if (disk.SmartStatus?.StatusText == "Query Error")
                             info.Add($"SMART status query failed for Disk #{disk.Index} ({disk.Model ?? "N/A"}). Error: {disk.SmartStatus.Error}. Basic Status: {disk.Status ?? "N/A"}.");
                    }
                 }

                 if (report.Hardware?.Volumes != null) {
                     foreach (var vol in report.Hardware.Volumes)
                     {
                         // Check if BitLocker status indicates it's applicable and off
                         if (vol.DriveLetter != null && vol.IsBitLockerProtected == false && vol.ProtectionStatus == "Protection Off")
                             suggestions.Add($"Volume {vol.DriveLetter} is not BitLocker protected. Consider enabling encryption for data security.");
                         // Note: "Not Found/Not Encryptable" status is informational, not necessarily a suggestion.
                         else if (vol.DriveLetter != null && vol.ProtectionStatus == "Not Found/Not Encryptable")
                             info.Add($"BitLocker status for Volume {vol.DriveLetter} indicates it may not be encryptable or BitLocker is not installed/configured.");
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
                        // Example: Check for services set to Auto that are stopped
                        else if (svc.StartMode == "Auto" && svc.State == "Stopped")
                        {
                            suggestions.Add($"Service '{svc.DisplayName}' ({svc.Name}) is set to Auto start but is currently Stopped. Investigate if this service should be running.");
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
                 if (report.Security?.IsSecureBootEnabled == false) suggestions.Add("Secure Boot is reported as Disabled. Enable Secure Boot in UEFI settings for enhanced boot security (if supported).");
                 if (report.Security?.Tpm != null && !(report.Security.Tpm.IsPresent == true && report.Security.Tpm.IsEnabled == true && report.Security.Tpm.IsActivated == true)) suggestions.Add($"TPM status is '{report.Security.Tpm.Status}'. Ensure TPM is enabled and activated in UEFI/BIOS for features like BitLocker and Windows 11 compatibility.");


                 // --- Performance Checks ---
                 // Use thresholds from config
                  if (double.TryParse(report.Performance?.OverallCpuUsagePercent?.Trim('%', ' '), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double cpu))
                  {
                    if(cpu > thresholds.HighCpuUsagePercent) issues.Add($"CPU usage sample was very high ({cpu:0}%). Check Task Manager for resource-intensive processes if performance is slow.");
                    else if (cpu > thresholds.ElevatedCpuUsagePercent) suggestions.Add($"CPU usage sample was high ({cpu:0}%). Monitor CPU usage under load.");
                  } else if (!string.IsNullOrEmpty(report.Performance?.OverallCpuUsagePercent) && report.Performance.OverallCpuUsagePercent.Contains("Error")) {
                     info.Add($"Could not analyze CPU usage due to collection error: {report.Performance.OverallCpuUsagePercent}");
                  }

                 // Use threshold from config
                 if (double.TryParse(report.Performance?.TotalDiskQueueLength, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double queue))
                 {
                    if (queue > thresholds.HighDiskQueueLength) // Higher threshold for modern systems/SSDs
                      suggestions.Add($"Average Disk Queue Length sample ({queue:0.##}) is high. Indicates potential disk bottleneck under load. Check disk activity in Resource Monitor.");
                 } else if (!string.IsNullOrEmpty(report.Performance?.TotalDiskQueueLength) && report.Performance.TotalDiskQueueLength.Contains("Error")) {
                      info.Add($"Could not analyze Disk Queue Length due to collection error: {report.Performance.TotalDiskQueueLength}");
                 }


                 // --- Network Checks ---
                 // Analyze Ping Results
                 AnalyzePingResult(report.Network?.ConnectivityTests?.GatewayPing, "Default Gateway", thresholds.MaxPingLatencyWarningMs, issues, suggestions, info);
                 if(report.Network?.ConnectivityTests?.DnsPings != null) {
                    foreach(var dnsPing in report.Network.ConnectivityTests.DnsPings) {
                        // Use null-conditional operator ?. for safety on dnsPing.Target
                        AnalyzePingResult(dnsPing, $"DNS Server {dnsPing?.Target ?? "?"}", thresholds.MaxPingLatencyWarningMs, issues, suggestions, info);
                    }
                 }

                 // Analyze DNS Resolution Result
                 AnalyzeDnsResolution(report.Network?.ConnectivityTests?.DnsResolution, issues, suggestions, info);

                 // Analyze Traceroute (Example)
                 AnalyzeTraceroute(report.Network?.ConnectivityTests, thresholds.MaxTracerouteHopLatencyWarningMs, suggestions, info);

                 // --- Event Log Checks ---
                 bool systemLogAccessible = !(report.Events?.SystemLogEntries?.Any(e => e.Message?.Contains("Access Denied") ?? false) ?? false);
                 bool appLogAccessible = !(report.Events?.ApplicationLogEntries?.Any(e => e.Message?.Contains("Access Denied") ?? false) ?? false);

                 // Count actual error/warning entries, ignoring collector messages/errors
                 int systemErrors = report.Events?.SystemLogEntries?.Count(e => e.Source != null && (e.EntryType == "Error" || e.EntryType == "Warning")) ?? 0;
                 int appErrors = report.Events?.ApplicationLogEntries?.Count(e => e.Source != null && (e.EntryType == "Error" || e.EntryType == "Warning")) ?? 0;

                 // Use thresholds from config
                 if (systemErrors > thresholds.MaxSystemLogErrorsIssue) issues.Add($"Found {systemErrors} recent Error/Warning events in System log. Review logs using Event Viewer for specific critical errors.");
                 else if (systemErrors > thresholds.MaxSystemLogErrorsSuggestion) suggestions.Add($"Found {systemErrors} recent Error/Warning events in System log. Review logs for details if experiencing issues.");
                 else if (!systemLogAccessible) info.Add("Could not fully analyze System Event Log due to access restrictions.");

                 // Use thresholds from config
                 if (appErrors > thresholds.MaxAppLogErrorsIssue) issues.Add($"Found {appErrors} recent Error/Warning events in Application log. Review logs using Event Viewer for application crashes or errors.");
                 else if (appErrors > thresholds.MaxAppLogErrorsSuggestion) suggestions.Add($"Found {appErrors} recent Error/Warning events in Application log. Review logs for details if applications are misbehaving.");
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

                 // --- Driver Date Check (Example) ---
                  DateTime driverWarningDate = DateTime.Now.AddYears(-thresholds.DriverAgeWarningYears);
                  // Check GPU Drivers
                  if (report.Hardware?.Gpus != null) {
                       foreach(var gpu in report.Hardware.Gpus) {
                            if (gpu.DriverDate.HasValue && gpu.DriverDate < driverWarningDate) {
                                 suggestions.Add($"Graphics driver for '{gpu.Name}' appears old (Date: {gpu.DriverDate:yyyy-MM-dd}). Consider updating from the manufacturer's website (NVIDIA/AMD/Intel).");
                            }
                       }
                  }
                  // Check Network Drivers (Requires DriverDate to be populated in NetworkInfoCollector)
                  if (report.Network?.Adapters != null) {
                     foreach (var nic in report.Network.Adapters.Where(n => n.Status == OperationalStatus.Up)) {
                         if (nic.DriverDate.HasValue && nic.DriverDate < driverWarningDate) {
                              suggestions.Add($"Network driver for '{nic.Description}' may be old (Date: {nic.DriverDate:yyyy-MM-dd}). Consider updating from the hardware or PC manufacturer's website.");
                         } else if (nic.DriverDate == null && !nic.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase) && !nic.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) {
                              // Avoid warning for loopback/virtual adapters where date might not apply
                              // info.Add($"Driver date for network adapter '{nic.Description}' could not be determined."); // Add info if needed
                         }
                     }
                  }


                 // --- General Suggestions ---
                 // Trigger if any issues OR specific hardware/performance suggestions exist
                 if (issues.Any() || suggestions.Any(s => s.Contains("Disk") || s.Contains("Memory") || s.Contains("CPU") || s.Contains("driver")))
                 {
                    suggestions.Add("Run System File Checker: Open Command Prompt or PowerShell as Admin and type 'sfc /scannow'.");
                    suggestions.Add("Check for and install pending Windows Updates.");
                    suggestions.Add("Perform a full system scan for malware using a reputable security tool.");
                    // Refined driver suggestion
                    if (!suggestions.Any(s => s.Contains("driver"))) // Avoid duplicate if specific driver suggestions exist
                    {
                        suggestions.Add("Ensure device drivers (especially Graphics, Network, Chipset) are up-to-date from manufacturer websites.");
                    }
                 }

                 if (!issues.Any() && !suggestions.Any() && !info.Any(i => !i.StartsWith("Tool not run as Admin") && !i.Contains("SMART status query")))
                 {
                      info.Add("Analysis complete. No major issues or specific suggestions generated based on collected data and configured thresholds.");
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


        // --- Analysis Helper Methods ---

        // Corrected method signature to use List<> generic type
        private static void AnalyzePingResult(PingResult? result, string targetName, long latencyWarningMs, List<string> issues, List<string> suggestions, List<string> info)
        {
            if (result == null)
            {
                info.Add($"Ping test for {targetName} was not performed or data is missing.");
                return;
            }

            if (result.Status != IPStatus.Success.ToString())
            {
                string errorDetail = string.IsNullOrEmpty(result.Error) ? "" : $" ({result.Error})";
                // Gateway failure is usually more critical
                if (targetName.Contains("Gateway", StringComparison.OrdinalIgnoreCase)) {
                     issues.Add($"Ping to {targetName} ({result.Target}) failed (Status: {result.Status}{errorDetail}). Potential local network issue (cable, router, adapter config).");
                } else {
                     suggestions.Add($"Ping to {targetName} ({result.Target}) failed (Status: {result.Status}{errorDetail}). Potential connectivity issue.");
                }
            }
            else if (result.RoundtripTimeMs.HasValue && result.RoundtripTimeMs > latencyWarningMs)
            {
                suggestions.Add($"Ping latency to {targetName} ({result.Target}) is high ({result.RoundtripTimeMs} ms). May indicate network congestion or issues.");
            }
            else if (result.RoundtripTimeMs.HasValue) // Success and acceptable latency
            {
                // info.Add($"Ping to {targetName} ({result.Target}) successful ({result.RoundtripTimeMs} ms)."); // Usually too verbose
            }
        }

         // Corrected method signature to use List<> generic type
         private static void AnalyzeDnsResolution(DnsResolutionResult? result, List<string> issues, List<string> suggestions, List<string> info)
         {
            if (result == null)
            {
                info.Add("DNS resolution test was not performed or data is missing.");
                return;
            }

            if (!result.Success)
            {
                 string errorDetail = string.IsNullOrEmpty(result.Error) ? "" : $" ({result.Error})";
                 suggestions.Add($"DNS resolution for '{result.Hostname}' failed{errorDetail}. Check DNS server settings and network connectivity.");
            } else {
                 // info.Add($"DNS resolution for '{result.Hostname}' successful ({result.ResolutionTimeMs} ms): {string.Join(", ", result.ResolvedIpAddresses ?? new List<string>())}"); // Too verbose?
            }
         }

        // Corrected method signature to use List<> generic type
        private static void AnalyzeTraceroute(NetworkTestResults? tests, long hopLatencyWarningMs, List<string> suggestions, List<string> info)
        {
            if (tests?.TracerouteResults == null || !tests.TracerouteResults.Any())
            {
                 if (!string.IsNullOrEmpty(tests?.TracerouteTarget)) info.Add($"Traceroute to {tests.TracerouteTarget} was not performed or results are unavailable.");
                 return;
            }

            bool timeoutDetected = false;
            // bool highLatencyDetected = false; // Removed - CS0219 Unused variable

            foreach(var hop in tests.TracerouteResults)
            {
                // Check for timeout status or '*' address which also indicates timeout/no reply
                if (hop.Status == IPStatus.TimedOut.ToString() || hop.Address == "*") {
                    timeoutDetected = true;
                }
                 // Check for high latency on hops that successfully replied (Status TTL Expired or Success)
                if (hop.RoundtripTimeMs.HasValue && hop.RoundtripTimeMs > hopLatencyWarningMs && (hop.Status == IPStatus.TtlExpired.ToString() || hop.Status == IPStatus.Success.ToString())) {
                    // highLatencyDetected = true; // Variable removed
                    suggestions.Add($"Traceroute to {tests.TracerouteTarget} shows high latency at hop {hop.Hop} ({hop.Address ?? "?"} - {hop.RoundtripTimeMs} ms).");
                }
                 // Log specific errors encountered during trace?
                 // Accessing hop.Error should now work
                if (!string.IsNullOrEmpty(hop.Error)) {
                    info.Add($"Traceroute hop {hop.Hop} encountered an error: {hop.Error}");
                }
            }

            if (timeoutDetected) {
                // Provide a more nuanced suggestion if timeouts occur
                suggestions.Add($"Traceroute to {tests.TracerouteTarget} encountered timeouts. This could indicate a firewall block, packet loss, or routing issue along the path. Review the hops prior to the timeout.");
            }
            // else if (!highLatencyDetected) { info.Add($"Traceroute to {tests.TracerouteTarget} completed without significant timeouts or high latency hops."); } // Too verbose?
        }

    }
}