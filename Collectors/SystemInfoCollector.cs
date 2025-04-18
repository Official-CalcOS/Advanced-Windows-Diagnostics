// Collectors/SystemInfoCollector.cs
using System;
using System.Globalization;
using System.IO;
using System.Linq; // Needed for Directory.GetFiles, File.GetLastWriteTime etc.
using System.Management;
using System.Runtime.Versioning;
using System.Text; // Required for Encoding
using System.Text.RegularExpressions; // Required for Regex
using System.Threading.Tasks;
using DiagnosticToolAllInOne.Helpers;
using Microsoft.Win32;
using System.Collections.Generic; // Required for IEnumerable<>

namespace DiagnosticToolAllInOne.Collectors
{
    [SupportedOSPlatform("windows")]
    public static class SystemInfoCollector
    {
        private const string WMI_CIMV2 = @"root\cimv2";
        private const int LogSearchMaxLines = 50000; // Max lines to search back in logs (performance optimization)
        private const int LogReadBufferSize = 65536; // Buffer size for reading logs

        // Registry keys to check for pending reboots
        private static readonly string[] RebootPendingKeys = {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
            @"SYSTEM\CurrentControlSet\Control\Session Manager" // Check for PendingFileRenameOperations value
        };
        private const string PENDING_FILE_RENAME_OPS_VALUE = "PendingFileRenameOperations";


        // Method to check for pending reboot flags
        private static bool CheckPendingReboot(SystemInfo systemInfo) // Pass systemInfo to add errors
        {
            try
            {
                // Check Windows Update Auto Update key
                if (RegistryHelper.ReadValue(Registry.LocalMachine, RebootPendingKeys[0], "RebootRequired", "0") == "1") // Assuming RebootRequired value doesn't exist if not needed
                {
                    // No need to add specific error here, the flag itself is the info
                    return true;
                }

                // Check Component Based Servicing key
                RegistryKey? cbsKey = null;
                try
                {
                    cbsKey = Registry.LocalMachine.OpenSubKey(RebootPendingKeys[1]);
                    if (cbsKey != null && cbsKey.GetSubKeyNames().Length > 0) // Presence of subkeys indicates reboot needed
                    {
                        return true;
                    }
                }
                finally { cbsKey?.Dispose(); }


                // Check Session Manager Pending File Rename Operations
                // Note: Reading MultiString values needs more than RegistryHelper currently offers.
                // This checks for the *existence* of the value which often indicates a pending reboot.
                RegistryKey? smKey = null;
                try
                {
                    smKey = Registry.LocalMachine.OpenSubKey(RebootPendingKeys[2]);
                    if (smKey?.GetValue(PENDING_FILE_RENAME_OPS_VALUE) != null)
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    // Handle potential errors reading this specific value (e.g., unexpected format)
                    systemInfo.AddSpecificError("PendingReboot_SMCheck", $"Error checking Session Manager key: {ex.Message}");
                    Logger.LogWarning("Error checking Session Manager PendingFileRenameOperations", ex);
                }
                finally { smKey?.Dispose(); }

                // If none of the above flags were found
                return false;
            }
            catch (System.Security.SecurityException secEx)
            {
                systemInfo.AddSpecificError("PendingReboot", $"Access Denied checking registry keys. Run as Admin.");
                Logger.LogWarning("Access Denied checking pending reboot status", secEx);
                return false; // Cannot determine
            }
            catch (Exception ex)
            {
                systemInfo.AddSpecificError("PendingReboot", $"Error checking status: {ex.Message}");
                Logger.LogError("Error checking pending reboot status", ex);
                return false; // Cannot determine
            }
        }

        // --- UPDATED: System Integrity Log Check with Parsing ---
        private static SystemIntegrityInfo CheckSystemIntegrity(SystemInfo systemInfo)
        {
            var integrityInfo = new SystemIntegrityInfo();
            string cbsLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "CBS", "CBS.log");
            string dismLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "DISM", "dism.log");

            // SFC Check
            try
            {
                if (File.Exists(cbsLogPath))
                {
                    integrityInfo.SfcLogFound = true;
                    ParseSfcLog(cbsLogPath, integrityInfo);
                }
                else
                {
                    integrityInfo.SfcLogFound = false;
                    integrityInfo.SfcScanResult = "CBS.log not found.";
                    Logger.LogInfo("CBS.log not found for SFC check.");
                }
            }
            catch (UnauthorizedAccessException uaEx)
            {
                integrityInfo.SfcLogFound = false; // Cannot confirm if found without access
                integrityInfo.SfcScanResult = "Access Denied";
                integrityInfo.LogParsingError = (integrityInfo.LogParsingError ?? "") + $"SFC Log Access Denied. Requires Admin. ";
                systemInfo.AddSpecificError("SystemIntegrity_SFC", "Access Denied reading CBS.log. Requires Admin.");
                Logger.LogWarning($"Access Denied reading CBS.log: {uaEx.Message}", uaEx);
            }
            catch (IOException ioEx)
            {
                integrityInfo.SfcLogFound = false; // Assume not accessible/found
                integrityInfo.SfcScanResult = "IO Error";
                integrityInfo.LogParsingError = (integrityInfo.LogParsingError ?? "") + $"SFC Log IO Error: {ioEx.Message}. ";
                systemInfo.AddSpecificError("SystemIntegrity_SFC", $"IO Error reading CBS.log: {ioEx.Message}");
                Logger.LogError("IO Error reading CBS.log", ioEx);
            }
            catch (Exception ex)
            {
                integrityInfo.SfcLogFound = false;
                integrityInfo.SfcScanResult = "Error Parsing";
                integrityInfo.LogParsingError = (integrityInfo.LogParsingError ?? "") + $"SFC Log Unexpected Error: {ex.Message}. ";
                systemInfo.AddSpecificError("SystemIntegrity_SFC", $"Unexpected error parsing CBS.log: {ex.Message}");
                Logger.LogError("Error parsing CBS.log", ex);
            }

            // DISM Check
            try
            {
                if (File.Exists(dismLogPath))
                {
                    integrityInfo.DismLogFound = true;
                    ParseDismLog(dismLogPath, integrityInfo);
                }
                else
                {
                    integrityInfo.DismLogFound = false;
                    integrityInfo.DismCheckHealthResult = "dism.log not found.";
                    Logger.LogInfo("dism.log not found for DISM check.");
                }
            }
            catch (UnauthorizedAccessException uaEx)
            {
                integrityInfo.DismLogFound = false;
                integrityInfo.DismCheckHealthResult = "Access Denied";
                integrityInfo.LogParsingError = (integrityInfo.LogParsingError ?? "") + $"DISM Log Access Denied. Requires Admin. ";
                systemInfo.AddSpecificError("SystemIntegrity_DISM", "Access Denied reading dism.log. Requires Admin.");
                Logger.LogWarning($"Access Denied reading dism.log: {uaEx.Message}", uaEx);
            }
            catch (IOException ioEx)
            {
                integrityInfo.DismLogFound = false;
                integrityInfo.DismCheckHealthResult = "IO Error";
                integrityInfo.LogParsingError = (integrityInfo.LogParsingError ?? "") + $"DISM Log IO Error: {ioEx.Message}. ";
                systemInfo.AddSpecificError("SystemIntegrity_DISM", $"IO Error reading dism.log: {ioEx.Message}");
                Logger.LogError("IO Error reading dism.log", ioEx);
            }
            catch (Exception ex)
            {
                integrityInfo.DismLogFound = false;
                integrityInfo.DismCheckHealthResult = "Error Parsing";
                integrityInfo.LogParsingError = (integrityInfo.LogParsingError ?? "") + $"DISM Log Unexpected Error: {ex.Message}. ";
                systemInfo.AddSpecificError("SystemIntegrity_DISM", $"Unexpected error parsing dism.log: {ex.Message}");
                Logger.LogError("Error parsing dism.log", ex);
            }

            return integrityInfo;
        }

        // --- NEW: SFC Log Parsing Helper ---
        private static void ParseSfcLog(string logPath, SystemIntegrityInfo info)
        {
            string startMarker = "Verify and Repair Transaction completed.";
            string successMarker = "successfully repaired"; // Part of "found corrupt files and successfully repaired them"
            string failMarker = "unable to fix"; // Part of "found corrupt files but was unable to fix some of them"
            string noViolationMarker = "did not find any integrity violations";
            // Regex to capture timestamp at the start of SFC log lines
            Regex dateRegex = new Regex(@"^(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2}),\s*(Info|Error|Warning)", RegexOptions.Compiled);

            DateTime? lastScanTime = null;
            string lastResult = "Unknown";
            bool corruptionFound = false;
            bool repairsSuccessful = false;
            bool scanDetected = false;

            try
            {
                // Read the log file efficiently, searching backwards
                foreach (string line in ReadLinesBackwards(logPath, LogSearchMaxLines))
                {
                    // Extract timestamp if available
                    Match dateMatch = dateRegex.Match(line);
                    DateTime currentLineTime = DateTime.MinValue;
                    if (dateMatch.Success && DateTime.TryParse(dateMatch.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out currentLineTime))
                    {
                        // Track the latest timestamp seen within the relevant log section
                        if (scanDetected && currentLineTime > (lastScanTime ?? DateTime.MinValue))
                        {
                            lastScanTime = currentLineTime;
                        }
                    }

                    if (line.Contains(startMarker))
                    {
                        scanDetected = true;
                        // If we hit the start marker and haven't found a result yet, assume the scan completed but result wasn't obvious
                        if(lastResult == "Unknown") {
                            // If we haven't found success/fail markers, check for the 'no violation' marker closer to the start
                            // This requires reading forward or more complex state, so we'll stick to simple backward scan for now.
                            // Defaulting to Unknown if specific result markers not found after start marker.
                        }
                         // Stop searching backwards once we find the completion marker for the *most recent* scan
                         break;
                    }

                    // Check for result markers *after* potentially detecting the start (or assume most recent result)
                    if (line.Contains(noViolationMarker))
                    {
                        lastResult = "No Violations";
                        corruptionFound = false;
                        repairsSuccessful = true; // Implicitly true if no violations
                         if (scanDetected && currentLineTime > (lastScanTime ?? DateTime.MinValue)) lastScanTime = currentLineTime;
                    }
                    else if (line.Contains(failMarker))
                    {
                        lastResult = "Unrepairable";
                        corruptionFound = true;
                        repairsSuccessful = false;
                        if (scanDetected && currentLineTime > (lastScanTime ?? DateTime.MinValue)) lastScanTime = currentLineTime;
                    }
                    else if (line.Contains(successMarker))
                    {
                        lastResult = "Repaired";
                        corruptionFound = true;
                        repairsSuccessful = true;
                        if (scanDetected && currentLineTime > (lastScanTime ?? DateTime.MinValue)) lastScanTime = currentLineTime;
                    }
                }
            }
            catch (Exception ex) {
                info.SfcScanResult = "Error Parsing";
                info.LogParsingError = (info.LogParsingError ?? "") + $"Error during SFC log parsing: {ex.Message}. ";
                Logger.LogError($"Error parsing SFC log '{logPath}'", ex);
                return; // Stop processing on error
            }

            // Assign results
            info.LastSfcScanTime = lastScanTime;
            if (scanDetected) {
                 info.SfcScanResult = lastResult;
                 info.SfcCorruptionFound = corruptionFound;
                 info.SfcRepairsSuccessful = repairsSuccessful;
            }
            else {
                 info.SfcScanResult = "Not Run Recently / No Scan Found"; // Scan markers not found within searched lines
                 info.LastSfcScanTime = null; // No scan time if no scan detected
            }

             // If parsing finished but result is still Unknown, log it
            if (info.SfcScanResult == "Unknown") {
                 Logger.LogWarning("SFC log parsing finished, but final result remained 'Unknown'. Log format might have changed.");
            }
        }

        // --- NEW: DISM Log Parsing Helper ---
        private static void ParseDismLog(string logPath, SystemIntegrityInfo info)
        {
            // Look for relevant DISM operations and their outcomes
            string checkHealthStart = "Checking System Health"; // CheckHealth marker
            string scanHealthStart = "Scanning System Health"; // ScanHealth marker (often follows CheckHealth)
            string noCorruptionMarker = "No component store corruption detected.";
            string repairableMarker = "The component store is repairable.";
            string notRepairableMarker = "The component store is not repairable."; // Less common
            string operationSuccess = "The operation completed successfully."; // Generic success marker often near results
            Regex dateRegex = new Regex(@"^(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2}),\s*(Info|Error|Warning)", RegexOptions.Compiled);

            DateTime? lastCheckTime = null;
            string lastResult = "Unknown";
            bool corruptionDetected = false;
            bool storeRepairable = false;
            bool operationDetected = false;

            try
            {
                // Read backwards
                foreach (string line in ReadLinesBackwards(logPath, LogSearchMaxLines))
                {
                    // Extract timestamp
                    Match dateMatch = dateRegex.Match(line);
                    DateTime currentLineTime = DateTime.MinValue;
                     if (dateMatch.Success && DateTime.TryParse(dateMatch.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out currentLineTime))
                     {
                         if (operationDetected && currentLineTime > (lastCheckTime ?? DateTime.MinValue))
                         {
                             lastCheckTime = currentLineTime;
                         }
                     }

                    // Detect relevant operations
                     if (!operationDetected && (line.Contains(checkHealthStart) || line.Contains(scanHealthStart)))
                     {
                         operationDetected = true;
                         // Use timestamp from the operation start line if it's the latest we have
                         if (currentLineTime > (lastCheckTime ?? DateTime.MinValue)) lastCheckTime = currentLineTime;
                     }


                    // Check for result markers - prioritize specific markers over generic success
                    if (line.Contains(noCorruptionMarker))
                    {
                        lastResult = "No Corruption";
                        corruptionDetected = false;
                        storeRepairable = false; // Not repairable if no corruption
                        if (operationDetected && currentLineTime > (lastCheckTime ?? DateTime.MinValue)) lastCheckTime = currentLineTime;
                        // If we find a definitive 'no corruption' message, we can often stop for this scan instance
                         if (operationDetected) break;
                    }
                    else if (line.Contains(repairableMarker))
                    {
                        lastResult = "Repairable";
                        corruptionDetected = true;
                        storeRepairable = true;
                        if (operationDetected && currentLineTime > (lastCheckTime ?? DateTime.MinValue)) lastCheckTime = currentLineTime;
                         // If we find a definitive 'repairable' message, we can often stop
                         if (operationDetected) break;
                    }
                     else if (line.Contains(notRepairableMarker))
                    {
                        lastResult = "Not Repairable";
                        corruptionDetected = true;
                        storeRepairable = false;
                        if (operationDetected && currentLineTime > (lastCheckTime ?? DateTime.MinValue)) lastCheckTime = currentLineTime;
                         if (operationDetected) break;
                    }
                    // Look for the generic success marker *near* a detected operation, but only if a specific result isn't found yet.
                    // This is less reliable. For simplicity, we primarily rely on the specific result markers.
                    // if (operationDetected && lastResult == "Unknown" && line.Contains(operationSuccess)) {
                    //     // If operation completed successfully but we haven't seen specific corruption markers,
                    //     // it *might* imply no corruption was found, but this is weak.
                    //     // lastResult = "No Corruption (Inferred)";
                    // }

                     // If we detect the start of an *earlier* operation, stop searching backwards
                     if (operationDetected && (line.Contains(checkHealthStart) || line.Contains(scanHealthStart)) && currentLineTime < lastCheckTime)
                     {
                         break;
                     }
                }
            }
            catch (Exception ex) {
                info.DismCheckHealthResult = "Error Parsing";
                info.LogParsingError = (info.LogParsingError ?? "") + $"Error during DISM log parsing: {ex.Message}. ";
                Logger.LogError($"Error parsing DISM log '{logPath}'", ex);
                return; // Stop processing on error
            }

            // Assign results
             info.LastDismCheckTime = lastCheckTime;
            if (operationDetected)
            {
                 info.DismCheckHealthResult = lastResult;
                 info.DismCorruptionDetected = corruptionDetected;
                 info.DismStoreRepairable = storeRepairable;
            }
            else
            {
                 info.DismCheckHealthResult = "Not Run Recently / No Scan Found";
                 info.LastDismCheckTime = null;
            }

            // If parsing finished but result is still Unknown, log it
            if (info.DismCheckHealthResult == "Unknown") {
                 Logger.LogWarning("DISM log parsing finished, but final result remained 'Unknown'. Log format might have changed or operation didn't complete clearly.");
            }
        }

        // --- Helper to Read Lines Backwards Efficiently (Basic Implementation) ---
        // Note: More robust implementations exist, but this avoids reading the whole file into memory.
        private static IEnumerable<string> ReadLinesBackwards(string path, int maxLinesToRead)
        {
            List<string> allLines = new List<string>();
            bool readSuccess = false;
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, LogReadBufferSize)) // Allow sharing for active logs
                using (StreamReader reader = new StreamReader(fs, Encoding.UTF8, true, LogReadBufferSize)) // Auto-detect encoding
                {
                    string? line;
                    // Read all lines into memory (with some buffer management)
                    while ((line = reader.ReadLine()) != null)
                    {
                        allLines.Add(line);
                        if (allLines.Count > maxLinesToRead * 2) // Keep buffer size somewhat managed
                        {
                            allLines.RemoveRange(0, allLines.Count - maxLinesToRead);
                        }
                    }
                }
                readSuccess = true; // Mark success if reading finishes
            }
            catch (Exception ex) {
                Logger.LogWarning($"Error reading file backwards '{path}': {ex.Message}");
                // Exception occurred, readSuccess remains false
            }

            // Yield results only if read was successful and lines exist
            if (readSuccess && allLines.Count > 0)
            {
                // Return in reverse order, limited by maxLinesToRead
                for (int i = allLines.Count - 1; i >= 0 && (allLines.Count - 1 - i) < maxLinesToRead; i--)
                {
                    yield return allLines[i]; // Yield is now OUTSIDE the try-catch
                }
            }
            // If readSuccess is false, the method yields nothing, effectively returning an empty sequence.
        }



        public static Task<SystemInfo> CollectAsync(bool isAdmin)
        {
            var systemInfo = new SystemInfo { DotNetVersion = Environment.Version.ToString() };

            try
            {
                // --- OS Info ---
                systemInfo.OperatingSystem = new OSInfo();
                WmiHelper.ProcessWmiResults( // No 'new', no assignment to 'searcher'
                    WmiHelper.Query("Win32_OperatingSystem", null, WMI_CIMV2), // The query results are passed in
                    obj =>
                    { // Action to perform for each result object
                        systemInfo.OperatingSystem.Name = WmiHelper.GetProperty(obj, "Caption");
                        systemInfo.OperatingSystem.Architecture = WmiHelper.GetProperty(obj, "OSArchitecture");
                        systemInfo.OperatingSystem.Version = WmiHelper.GetProperty(obj, "Version");
                        systemInfo.OperatingSystem.BuildNumber = WmiHelper.GetProperty(obj, "BuildNumber");
                        systemInfo.OperatingSystem.InstallDate = WmiHelper.ConvertCimDateTime(WmiHelper.GetProperty(obj, "InstallDate"));
                        systemInfo.OperatingSystem.LastBootTime = WmiHelper.ConvertCimDateTime(WmiHelper.GetProperty(obj, "LastBootUpTime"));
                        if (systemInfo.OperatingSystem.LastBootTime.HasValue)
                        {
                            systemInfo.OperatingSystem.Uptime = DateTime.Now - systemInfo.OperatingSystem.LastBootTime.Value;
                        }

                        systemInfo.OperatingSystem.SystemDrive = WmiHelper.GetProperty(obj, "SystemDrive");
                    },
                    error => systemInfo.AddSpecificError("OSInfo", error) // Action to perform if there's an error during processing
                );

                // --- Computer System Info ---
                systemInfo.ComputerSystem = new ComputerSystemInfo { CurrentUser = Environment.UserName };
                WmiHelper.ProcessWmiResults(
                     WmiHelper.Query("Win32_ComputerSystem", null, WMI_CIMV2),
                     obj =>
                     {
                         systemInfo.ComputerSystem.Manufacturer = WmiHelper.GetProperty(obj, "Manufacturer");
                         systemInfo.ComputerSystem.Model = WmiHelper.GetProperty(obj, "Model");
                         systemInfo.ComputerSystem.SystemType = WmiHelper.GetProperty(obj, "SystemType");
                         systemInfo.ComputerSystem.PartOfDomain = bool.TryParse(WmiHelper.GetProperty(obj, "PartOfDomain"), out bool domain) && domain;
                         systemInfo.ComputerSystem.DomainOrWorkgroup = systemInfo.ComputerSystem.PartOfDomain
                             ? WmiHelper.GetProperty(obj, "Domain")
                             : WmiHelper.GetProperty(obj, "Workgroup", "N/A");
                         systemInfo.ComputerSystem.LoggedInUserWMI = WmiHelper.GetProperty(obj, "UserName");
                     },
                      error => systemInfo.AddSpecificError("ComputerSystem", error)
                );

                // --- Baseboard Info ---
                systemInfo.Baseboard = new BaseboardInfo();
                WmiHelper.ProcessWmiResults(
                    WmiHelper.Query("Win32_BaseBoard", null, WMI_CIMV2),
                    obj =>
                    {
                        systemInfo.Baseboard.Manufacturer = WmiHelper.GetProperty(obj, "Manufacturer");
                        systemInfo.Baseboard.Product = WmiHelper.GetProperty(obj, "Product");
                        systemInfo.Baseboard.SerialNumber = WmiHelper.GetProperty(obj, "SerialNumber", isAdmin ? "N/A" : "Requires Admin");
                        systemInfo.Baseboard.Version = WmiHelper.GetProperty(obj, "Version");
                    },
                     error => systemInfo.AddSpecificError("Baseboard", error)
                );

                // --- BIOS Info ---
                systemInfo.BIOS = new BiosInfo();
                WmiHelper.ProcessWmiResults(
                    WmiHelper.Query("Win32_BIOS", null, WMI_CIMV2),
                    obj =>
                    {
                        systemInfo.BIOS.Manufacturer = WmiHelper.GetProperty(obj, "Manufacturer");
                        systemInfo.BIOS.Version = $"{WmiHelper.GetProperty(obj, "Version")} / {WmiHelper.GetProperty(obj, "SMBIOSBIOSVersion")}";
                        systemInfo.BIOS.ReleaseDate = WmiHelper.ConvertCimDateTime(WmiHelper.GetProperty(obj, "ReleaseDate"));
                        systemInfo.BIOS.SerialNumber = WmiHelper.GetProperty(obj, "SerialNumber", isAdmin ? "N/A" : "Requires Admin");
                    },
                     error => systemInfo.AddSpecificError("BIOS", error)
                );

                // --- TimeZone Info ---
                systemInfo.TimeZone = new TimeZoneConfig();
                WmiHelper.ProcessWmiResults(
                    WmiHelper.Query("Win32_TimeZone", null, WMI_CIMV2),
                    obj =>
                    {
                        systemInfo.TimeZone.CurrentTimeZone = WmiHelper.GetProperty(obj, "Caption");
                        systemInfo.TimeZone.StandardName = WmiHelper.GetProperty(obj, "StandardName");
                        systemInfo.TimeZone.DaylightName = WmiHelper.GetProperty(obj, "DaylightName");
                        systemInfo.TimeZone.BiasMinutes = int.TryParse(WmiHelper.GetProperty(obj, "Bias"), out int bias) ? bias : null;
                    },
                     error => systemInfo.AddSpecificError("TimeZone", error)
                );

                // --- Power Plan (Existing code - slight improvement to error) ---
                WmiHelper.ProcessWmiResults(
                    WmiHelper.Query("Win32_PowerPlan", new[] { "InstanceID", "ElementName", "IsActive" }, WMI_CIMV2, "IsActive = True"),
                    obj =>
                    {
                        systemInfo.ActivePowerPlan = new PowerPlanInfo
                        {
                            Name = WmiHelper.GetProperty(obj, "ElementName"),
                            InstanceID = WmiHelper.GetProperty(obj, "InstanceID"),
                            IsActive = true
                        };
                    },
                    error => systemInfo.AddSpecificError("PowerPlan", $"WMI Query Error: {error}") // Add specific error detail
                );
                // Add error if still null after query attempt (and no WMI error was explicitly caught by ProcessWmiResults)
                if (systemInfo.ActivePowerPlan == null && !(systemInfo.SpecificCollectionErrors?.ContainsKey("PowerPlan") ?? false))
                {
                    // Check if WMI query failed silently or returned no results
                    var queryResult = WmiHelper.Query("Win32_PowerPlan", new[] { "InstanceID" }, WMI_CIMV2, "IsActive = True"); // Re-query minimally
                    if (!queryResult.Success || queryResult.Results == null || queryResult.Results.Count == 0)
                    {
                        systemInfo.AddSpecificError("PowerPlan", "No active power plan found or WMI query failed/returned no results.");
                    }
                    else
                    {
                        // This case should be rare - query worked but processing somehow failed?
                        systemInfo.AddSpecificError("PowerPlan", "Failed to process active power plan data after successful query.");
                    }
                    queryResult.Results?.Dispose(); // Dispose results from the check query
                }

                // --- ADDED: Call Pending Reboot Check ---
                systemInfo.IsRebootPending = CheckPendingReboot(systemInfo); // Pass systemInfo

                // --- UPDATED: Call System Integrity Check ---
                systemInfo.SystemIntegrity = CheckSystemIntegrity(systemInfo); // Pass systemInfo

            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CRITICAL ERROR] System Info Collection failed: {ex.Message}");
                systemInfo.SectionCollectionErrorMessage = $"Critical failure during System Info collection: {ex.Message}";
                Logger.LogError("System Info collection failed", ex); // Use Logger
            }

            return Task.FromResult(systemInfo);
        }
    }
}