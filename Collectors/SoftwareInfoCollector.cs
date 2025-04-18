// Collectors/SoftwareInfoCollector.cs
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using DiagnosticToolAllInOne.Helpers; // Ensure all helpers are referenced

namespace DiagnosticToolAllInOne.Collectors
{
    [SupportedOSPlatform("windows")]
    public static class SoftwareInfoCollector
    {
        private const string WMI_CIMV2 = @"root\cimv2";

        public static Task<SoftwareInfo> CollectAsync()
        {
             var softwareInfo = new SoftwareInfo();
             bool isAdmin = AdminHelper.IsRunningAsAdmin(); // Check internally if needed

            try
            {
                // --- Installed Applications (Registry) ---
                // Refactored with per-key error handling
                try
                {
                    softwareInfo.InstalledApplications = new List<InstalledApplicationInfo>(); // Initialize
                    Action<RegistryKey, RegistryView> readUninstallKey = (baseKeyHive, view) =>
                    {
                        RegistryKey? baseKey = null;
                        RegistryKey? uninstallKey = null;
                        string hiveName = baseKeyHive.Name; // For logging

                        try
                        {
                            // Open base key (HKLM or HKCU) with the specified view (32/64 bit)
                            baseKey = RegistryKey.OpenBaseKey(hiveName == "HKEY_LOCAL_MACHINE" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, view);
                            uninstallKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");

                            if (uninstallKey == null)
                            {
                                Logger.LogDebug($"Uninstall key not found in {hiveName}\\{view}.");
                                return; // Key doesn't exist, nothing to do
                            }

                            // Iterate through application subkeys
                            foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                            {
                                RegistryKey? appKey = null;
                                try
                                {
                                    // Open individual application key
                                    appKey = uninstallKey.OpenSubKey(subKeyName);
                                    if (appKey != null)
                                    {
                                        // Read application details
                                        string? displayName = appKey.GetValue("DisplayName") as string;
                                        object? systemComponent = appKey.GetValue("SystemComponent");

                                        // Filter out system components and entries without display names
                                        if (!string.IsNullOrWhiteSpace(displayName) && (systemComponent == null || Convert.ToInt32(systemComponent) == 0))
                                        {
                                            DateTime? installDate = null;
                                            string? dateStr = appKey.GetValue("InstallDate") as string;
                                            // Parse install date string strictly
                                            if (!string.IsNullOrEmpty(dateStr) &&
                                                DateTime.TryParseExact(dateStr, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
                                            {
                                                installDate = parsedDate;
                                            }

                                            var appInfo = new InstalledApplicationInfo
                                            {
                                                Name = displayName,
                                                Version = appKey.GetValue("DisplayVersion") as string,
                                                Publisher = appKey.GetValue("Publisher") as string,
                                                InstallLocation = appKey.GetValue("InstallLocation") as string,
                                                InstallDate = installDate
                                            };

                                            // Avoid adding duplicates based on Name, Version, and Publisher
                                            if (!softwareInfo.InstalledApplications.Any(a => a.Name == appInfo.Name && a.Version == appInfo.Version && a.Publisher == appInfo.Publisher))
                                            {
                                                 softwareInfo.InstalledApplications.Add(appInfo);
                                            }
                                        }
                                    }
                                }
                                // --- Refined Error Handling for individual app key ---
                                catch (System.Security.SecurityException secEx)
                                {
                                    // Log specific error for this app key, but continue loop
                                    softwareInfo.AddSpecificError($"InstalledApp_{subKeyName}", $"Access Denied reading key. ({secEx.Message})");
                                    Logger.LogWarning($"Security exception reading registry subkey {hiveName}\\{view}\\...\\{subKeyName}: {secEx.Message}");
                                }
                                catch (IOException ioEx)
                                {
                                    // Log specific IO error, continue loop
                                    softwareInfo.AddSpecificError($"InstalledApp_{subKeyName}", $"IO Error reading key. ({ioEx.Message})");
                                    Logger.LogWarning($"IO exception reading registry subkey {hiveName}\\{view}\\...\\{subKeyName}: {ioEx.Message}");
                                }
                                catch (Exception ex)
                                {
                                    // Log other unexpected errors for this key, continue loop
                                    softwareInfo.AddSpecificError($"InstalledApp_{subKeyName}", $"Unexpected error: {ex.GetType().Name} - {ex.Message}");
                                    Logger.LogWarning($"Error reading registry subkey {hiveName}\\{view}\\...\\{subKeyName}: {ex.GetType().Name} - {ex.Message}", ex);
                                }
                                finally
                                {
                                    appKey?.Dispose(); // Ensure individual app key is disposed
                                }
                            } // End foreach subKeyName
                        }
                        catch (System.Security.SecurityException secEx)
                        {
                             // Error opening the main Uninstall key
                             softwareInfo.AddSpecificError($"InstalledAppsRegistry_{hiveName}_{view}", $"Security exception accessing base uninstall key: {secEx.Message}");
                             Logger.LogWarning($"Security exception accessing registry hive {hiveName}/{view}. Some applications may be missed.", secEx);
                        }
                        catch (Exception ex)
                        {
                             // Other error opening the main Uninstall key
                             softwareInfo.AddSpecificError($"InstalledAppsRegistry_{hiveName}_{view}", $"Failed reading base uninstall key: {ex.Message}");
                             Logger.LogError($"Failed reading registry hive {hiveName}/{view}", ex);
                        }
                        finally
                        {
                            uninstallKey?.Dispose(); // Dispose main uninstall key handle
                            baseKey?.Dispose();      // Dispose base hive handle
                        }
                    }; // End Action readUninstallKey

                    // Read from all standard locations
                    readUninstallKey(Registry.LocalMachine, RegistryView.Registry64);
                    readUninstallKey(Registry.LocalMachine, RegistryView.Registry32);
                    readUninstallKey(Registry.CurrentUser, RegistryView.Registry64);
                    readUninstallKey(Registry.CurrentUser, RegistryView.Registry32);

                    // Sort the final list
                    softwareInfo.InstalledApplications = softwareInfo.InstalledApplications.OrderBy(a => a.Name).ToList();

                } catch (Exception regEx) {
                     // Catch errors during the setup phase of reading registry
                     softwareInfo.AddSpecificError("InstalledApps_Overall", $"Failed to retrieve installed applications: {regEx.Message}");
                     Logger.LogError($"[ERROR] Installed Apps Collection overall failure", regEx);
                }


                // --- Windows Updates ---
                try
                {
                    softwareInfo.WindowsUpdates = new List<WindowsUpdateInfo>(); // Initialize
                    WmiHelper.ProcessWmiResults(
                        WmiHelper.Query("Win32_QuickFixEngineering", null, WMI_CIMV2),
                        obj => {
                            softwareInfo.WindowsUpdates.Add(new WindowsUpdateInfo {
                                HotFixID = WmiHelper.GetProperty(obj, "HotFixID"),
                                Description = WmiHelper.GetProperty(obj, "Description"),
                                InstalledOn = WmiHelper.ConvertCimDateTime(WmiHelper.GetProperty(obj, "InstalledOn"))
                            });
                        },
                        error => softwareInfo.AddSpecificError("WindowsUpdates_WMI", error) // Log WMI specific error
                    );
                }
                catch (Exception wuEx) {
                    softwareInfo.AddSpecificError("WindowsUpdates_Overall", $"Failed to retrieve Windows Updates: {wuEx.Message}");
                    Logger.LogError($"[ERROR] Windows Updates Collection failure", wuEx);
                }


                // --- Services ---
                try
                {
                    softwareInfo.RelevantServices = new List<ServiceInfo>(); // Initialize
                    // Define services to always include, plus logic for running/non-MS services
                    string[] criticalServices = { "Winmgmt", "Spooler", "Schedule", "Themes", "AudioSrv", "BITS", "wuauserv", "SecurityHealthService", "MsMpEng", "WinDefend", "MpsSvc", "Dnscache", "Dhcp", "RpcSs", "BFE" };
                    WmiHelper.ProcessWmiResults(
                        WmiHelper.Query("Win32_Service", null, WMI_CIMV2),
                        obj => {
                            string name = WmiHelper.GetProperty(obj, "Name");
                            string state = WmiHelper.GetProperty(obj, "State");
                            string pathName = WmiHelper.GetProperty(obj, "PathName", ""); // Default to empty string if null
                            // Determine if it's likely a Microsoft service based on path
                            bool isMicrosoft = pathName.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                                               pathName.StartsWith(@"C:\Windows\", StringComparison.OrdinalIgnoreCase) ||
                                               pathName.StartsWith(@"C:\Program Files\WindowsApps\", StringComparison.OrdinalIgnoreCase) ||
                                               string.IsNullOrWhiteSpace(pathName); // Treat empty path as likely system/MS

                            // Determine if service should be included in the "Relevant" list
                            bool keepService = criticalServices.Contains(name, StringComparer.OrdinalIgnoreCase) // Always keep critical ones
                                            || state != "Stopped" // Keep any service that isn't stopped
                                            || (!isMicrosoft && pathName != "N/A" && pathName != "Requires Admin"); // Keep non-MS services with valid paths

                            if (keepService) {
                                // Get PathName, handling admin requirement
                                string finalPathName = WmiHelper.GetProperty(obj, "PathName", isAdmin ? "N/A" : "Requires Admin");
                                if(finalPathName == "Requires Admin") softwareInfo.AddSpecificError($"ServicePath_{name}", "Requires Admin");

                                softwareInfo.RelevantServices.Add(new ServiceInfo {
                                    Name = name,
                                    DisplayName = WmiHelper.GetProperty(obj, "DisplayName"),
                                    State = state,
                                    StartMode = WmiHelper.GetProperty(obj, "StartMode"),
                                    PathName = finalPathName,
                                    Status = WmiHelper.GetProperty(obj, "Status")
                                });
                            }
                        },
                         error => softwareInfo.AddSpecificError("Services_WMI", error) // Log WMI specific error
                    );
                    softwareInfo.RelevantServices = softwareInfo.RelevantServices.OrderBy(s => s.Name).ToList();
                }
                catch (Exception svcEx)
                {
                    softwareInfo.AddSpecificError("Services_Overall", $"Failed to retrieve Services: {svcEx.Message}");
                    Logger.LogError($"[ERROR] Services Collection failure", svcEx);
                }


                // --- Startup Programs ---
                // Refactored with per-location/folder error handling
                try {
                    softwareInfo.StartupPrograms = new List<StartupProgramInfo>(); // Initialize

                    // Helper Action for Registry Startup Items
                    Action<RegistryKey, RegistryView, string> checkStartupReg = (baseKeyHive, view, path) => {
                        RegistryKey? baseKey = null;
                        RegistryKey? regKey = null;
                        string locationId = $"Reg_{baseKeyHive.Name.Replace("HKEY_", "")}_{view}_{path.Split('\\').Last()}"; // Unique ID for error reporting
                        try {
                            baseKey = RegistryKey.OpenBaseKey(baseKeyHive.Name == "HKEY_LOCAL_MACHINE" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, view);
                            regKey = baseKey.OpenSubKey(path);
                            if (regKey == null) return; // Key doesn't exist

                            string locationDisplay = $"Reg:{baseKeyHive.Name.Replace("HKEY_", "")}\\{view}\\{path.Split('\\').Last()}";

                            foreach (var valueName in regKey.GetValueNames()) {
                                try {
                                     softwareInfo.StartupPrograms.Add(new StartupProgramInfo {
                                         Location = locationDisplay,
                                         Name = valueName,
                                         Command = regKey.GetValue(valueName)?.ToString()
                                     });
                                } catch (Exception valEx) {
                                     // Error reading a specific value within the key
                                     softwareInfo.AddSpecificError($"{locationId}_Value_{valueName}", $"Error reading value: {valEx.Message}");
                                     Logger.LogWarning($"Error reading startup registry value {locationDisplay}\\{valueName}: {valEx.Message}", valEx);
                                }
                            }
                        } catch (System.Security.SecurityException secEx){
                             softwareInfo.AddSpecificError(locationId, $"Access Denied reading key: {secEx.Message}");
                             Logger.LogWarning($"Access Denied reading startup registry {path} ({view}): {secEx.Message}", secEx);
                        }
                        catch (Exception ex) {
                             softwareInfo.AddSpecificError(locationId, $"Error reading key: {ex.Message}");
                             Logger.LogWarning($"Error reading startup registry {path} ({view}): {ex.Message}", ex);
                        }
                        finally { regKey?.Dispose(); baseKey?.Dispose(); }
                    };

                    // Check standard registry run locations
                    checkStartupReg(Registry.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                    checkStartupReg(Registry.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                    checkStartupReg(Registry.CurrentUser, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                    checkStartupReg(Registry.CurrentUser, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                    // Check policy run locations
                    checkStartupReg(Registry.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run");
                    checkStartupReg(Registry.CurrentUser, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run");


                    // Helper Action for Startup Folders
                    Action<Environment.SpecialFolder, string> checkStartupFolder = (folder, locationName) => {
                        string locationId = $"Folder_{folder}"; // Unique ID for error reporting
                        try {
                            string path = Environment.GetFolderPath(folder);
                            if (Directory.Exists(path)) {
                                // Combine enumeration for .lnk and .exe files
                                foreach (string file in Directory.EnumerateFiles(path, "*.lnk").Concat(Directory.EnumerateFiles(path, "*.exe"))) {
                                     try {
                                          softwareInfo.StartupPrograms.Add(new StartupProgramInfo {
                                              Location = locationName,
                                              Name = Path.GetFileName(file),
                                              Command = file // Store the full path as the command
                                          });
                                     } catch (Exception fileEx) {
                                          // Error processing a specific file within the folder
                                          softwareInfo.AddSpecificError($"{locationId}_File_{Path.GetFileName(file)}", $"Error processing file: {fileEx.Message}");
                                          Logger.LogWarning($"Error processing startup file {file}: {fileEx.Message}", fileEx);
                                     }
                                }
                            } else {
                                 Logger.LogDebug($"Startup folder path not found: {path} (Folder: {folder})");
                            }
                        } catch (Exception ex) {
                             softwareInfo.AddSpecificError(locationId, $"Error accessing folder: {ex.Message}");
                             Logger.LogWarning($"Error reading startup folder {folder}: {ex.Message}", ex);
                        }
                    };

                    // Check standard startup folders
                    checkStartupFolder(Environment.SpecialFolder.CommonStartup, "Folder:Common Startup");
                    checkStartupFolder(Environment.SpecialFolder.Startup, "Folder:User Startup");

                    // Sort the final list
                    softwareInfo.StartupPrograms = softwareInfo.StartupPrograms.OrderBy(s => s.Location).ThenBy(s => s.Name).ToList();

                } catch (Exception startupEx) {
                     // Catch errors during the overall setup phase
                     softwareInfo.AddSpecificError("StartupPrograms_Overall", $"Failed to retrieve startup programs: {startupEx.Message}");
                     Logger.LogError($"[ERROR] Startup Programs Collection failure", startupEx);
                }


                // --- Environment Variables ---
                try {
                    softwareInfo.SystemEnvironmentVariables = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Machine)
                        .Cast<System.Collections.DictionaryEntry>()
                        .OrderBy(v => v.Key.ToString())
                        .ToDictionary(v => v.Key.ToString()!, v => v.Value?.ToString() ?? "");
                }
                catch (Exception sysEnvEx) {
                     softwareInfo.AddSpecificError("SystemEnvVars", $"Failed to get system environment variables: {sysEnvEx.Message}");
                     Logger.LogError($"[ERROR] System Env Vars Collection failure", sysEnvEx);
                }

                try {
                    softwareInfo.UserEnvironmentVariables = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.User)
                        .Cast<System.Collections.DictionaryEntry>()
                        .OrderBy(v => v.Key.ToString())
                        .ToDictionary(v => v.Key.ToString()!, v => v.Value?.ToString() ?? "");
                }
                catch (Exception userEnvEx) {
                    softwareInfo.AddSpecificError("UserEnvVars", $"Failed to get user environment variables: {userEnvEx.Message}");
                    Logger.LogError($"[ERROR] User Env Vars Collection failure", userEnvEx);
                }

            }
             catch(Exception ex) // Catch errors in the overall collection setup/outer try block
            {
                 Logger.LogError($"[CRITICAL ERROR] Software Info Collection failed", ex);
                 softwareInfo.SectionCollectionErrorMessage = $"Critical failure during Software Info collection: {ex.Message}";
            }
            // Use Task.FromResult for compatibility if method signature requires Task<>
            return Task.FromResult(softwareInfo);
        }
    }
}
