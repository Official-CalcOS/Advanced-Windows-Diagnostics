using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using DiagnosticToolAllInOne.Helpers;

namespace DiagnosticToolAllInOne.Collectors
{
    [SupportedOSPlatform("windows")]
    public static class SoftwareInfoCollector
    {
        private const string WMI_CIMV2 = @"root\cimv2";

        public static Task<SoftwareInfo> CollectAsync() // Doesn't strictly need isAdmin passed for its core functions
        {
             var softwareInfo = new SoftwareInfo();
             bool isAdmin = AdminHelper.IsRunningAsAdmin(); // Check internally if needed for PathName

            try
            {
                // --- Installed Applications (Registry) ---
                try
                {
                    softwareInfo.InstalledApplications = new();
                    Action<RegistryKey, RegistryView> readUninstallKey = (baseKeyHive, view) =>
                    {
                        RegistryKey? baseKey = null; RegistryKey? uninstallKey = null;
                        try
                        {
                            baseKey = RegistryKey.OpenBaseKey(baseKeyHive.Name == "HKEY_LOCAL_MACHINE" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, view);
                            uninstallKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                            if (uninstallKey == null) return;
                            foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                            {
                                RegistryKey? appKey = null;
                                try {
                                    appKey = uninstallKey.OpenSubKey(subKeyName);
                                    if (appKey != null) {
                                        string? displayName = appKey.GetValue("DisplayName") as string;
                                        object? systemComponent = appKey.GetValue("SystemComponent");
                                        if (!string.IsNullOrWhiteSpace(displayName) && (systemComponent == null || Convert.ToInt32(systemComponent) == 0)) {
                                            DateTime? installDate = null;
                                            string? dateStr = appKey.GetValue("InstallDate") as string;
                                            if (!string.IsNullOrEmpty(dateStr) && dateStr.Length == 8 && int.TryParse(dateStr.Substring(0, 4), out int y) && int.TryParse(dateStr.Substring(4, 2), out int m) && int.TryParse(dateStr.Substring(6, 2), out int d)) { try { installDate = new DateTime(y, m, d); } catch { } }
                                            var appInfo = new InstalledApplicationInfo { Name = displayName, Version = appKey.GetValue("DisplayVersion") as string, Publisher = appKey.GetValue("Publisher") as string, InstallLocation = appKey.GetValue("InstallLocation") as string, InstallDate = installDate };
                                            if (!softwareInfo.InstalledApplications.Any(a => a.Name == appInfo.Name && a.Version == appInfo.Version)) { softwareInfo.InstalledApplications.Add(appInfo); }
                                        }
                                    }
                                }
                                catch (System.Security.SecurityException) { }
                                catch (Exception ex) { Console.Error.WriteLine($"[WARN] Error reading registry subkey {subKeyName}: {ex.Message}"); }
                                finally { appKey?.Dispose(); }
                            }
                        }
                        catch (System.Security.SecurityException) { Console.Error.WriteLine($"[WARN] Security exception accessing registry hive {baseKeyHive.Name}/{view}. Some applications may be missed."); }
                        catch (Exception ex) { Console.Error.WriteLine($"[ERROR] Failed reading registry hive {baseKeyHive.Name}/{view}: {ex.Message}"); }
                        finally { uninstallKey?.Dispose(); baseKey?.Dispose(); }
                    };
                    readUninstallKey(Registry.LocalMachine, RegistryView.Registry64);
                    readUninstallKey(Registry.LocalMachine, RegistryView.Registry32);
                    readUninstallKey(Registry.CurrentUser, RegistryView.Registry64);
                    readUninstallKey(Registry.CurrentUser, RegistryView.Registry32);
                    softwareInfo.InstalledApplications = softwareInfo.InstalledApplications.OrderBy(a => a.Name).ToList();
                } catch (Exception regEx) {
                     // Corrected: Call AddSpecificError on the instance
                     softwareInfo.AddSpecificError("InstalledApps", $"Failed to retrieve installed applications: {regEx.Message}");
                }


                // --- Windows Updates ---
                softwareInfo.WindowsUpdates = new();
                 WmiHelper.ProcessWmiResults(
                     WmiHelper.Query("Win32_QuickFixEngineering", null, WMI_CIMV2),
                     obj => {
                         softwareInfo.WindowsUpdates.Add(new WindowsUpdateInfo {
                             HotFixID = WmiHelper.GetProperty(obj, "HotFixID"),
                             Description = WmiHelper.GetProperty(obj, "Description"),
                             InstalledOn = WmiHelper.ConvertCimDateTime(WmiHelper.GetProperty(obj, "InstalledOn"))
                         });
                     },
                      // Corrected: Call AddSpecificError on the instance
                      error => softwareInfo.AddSpecificError("WindowsUpdates", error)
                 );

                // --- Services ---
                softwareInfo.RelevantServices = new();
                string[] criticalServices = { "Winmgmt", "Spooler", "Schedule", "Themes", "AudioSrv", "BITS", "wuauserv", "SecurityHealthService", "MsMpEng", "WinDefend", "MpsSvc", "Dnscache", "Dhcp", "RpcSs", "BFE" };
                 WmiHelper.ProcessWmiResults(
                     WmiHelper.Query("Win32_Service", null, WMI_CIMV2),
                     obj => {
                         string name = WmiHelper.GetProperty(obj, "Name");
                         string state = WmiHelper.GetProperty(obj, "State");
                         string pathName = WmiHelper.GetProperty(obj, "PathName", "");
                         bool isMicrosoft = pathName.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) || pathName.StartsWith(@"C:\Windows\", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(pathName);
                         bool keepService = criticalServices.Contains(name, StringComparer.OrdinalIgnoreCase) || state != "Stopped" || (!isMicrosoft && pathName != "N/A" && pathName != "Requires Admin");
                         if (keepService) {
                             softwareInfo.RelevantServices.Add(new ServiceInfo {
                                 Name = name,
                                 DisplayName = WmiHelper.GetProperty(obj, "DisplayName"),
                                 State = state,
                                 StartMode = WmiHelper.GetProperty(obj, "StartMode"),
                                 PathName = WmiHelper.GetProperty(obj, "PathName", isAdmin ? "N/A" : "Requires Admin"),
                                 Status = WmiHelper.GetProperty(obj, "Status")
                             });
                         }
                     },
                      // Corrected: Call AddSpecificError on the instance
                      error => softwareInfo.AddSpecificError("Services", error)
                 );
                softwareInfo.RelevantServices = softwareInfo.RelevantServices.OrderBy(s => s.Name).ToList();


                // --- Startup Programs ---
                try {
                    softwareInfo.StartupPrograms = new();
                    Action<RegistryKey, RegistryView, string> checkStartupReg = (baseKeyHive, view, path) => {
                        RegistryKey? baseKey = null; RegistryKey? regKey = null;
                        try {
                            baseKey = RegistryKey.OpenBaseKey(baseKeyHive.Name == "HKEY_LOCAL_MACHINE" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, view);
                            regKey = baseKey.OpenSubKey(path); if (regKey == null) return; string location = $"Reg:{baseKeyHive.Name.Replace("HKEY_", "")}\\{view}\\{path.Split('\\').Last()}";
                            foreach (var valueName in regKey.GetValueNames()) { softwareInfo.StartupPrograms.Add(new StartupProgramInfo { Location = location, Name = valueName, Command = regKey.GetValue(valueName)?.ToString() }); }
                        } catch (Exception ex) { Console.Error.WriteLine($"[WARN] Error reading startup registry {path} ({view}): {ex.Message}"); }
                        finally { regKey?.Dispose(); baseKey?.Dispose(); }
                    };
                    checkStartupReg(Registry.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"); checkStartupReg(Registry.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"); checkStartupReg(Registry.CurrentUser, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"); checkStartupReg(Registry.CurrentUser, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                    checkStartupReg(Registry.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run"); checkStartupReg(Registry.CurrentUser, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run");
                    Action<Environment.SpecialFolder, string> checkStartupFolder = (folder, locationName) => { try { string path = Environment.GetFolderPath(folder); if (Directory.Exists(path)) { foreach (string file in Directory.EnumerateFiles(path, "*.lnk").Concat(Directory.EnumerateFiles(path, "*.exe"))) { softwareInfo.StartupPrograms.Add(new StartupProgramInfo { Location = locationName, Name = Path.GetFileName(file), Command = file }); } } } catch (Exception ex) { Console.Error.WriteLine($"[WARN] Error reading startup folder {folder}: {ex.Message}");} };
                    checkStartupFolder(Environment.SpecialFolder.CommonStartup, "Folder:Common Startup"); checkStartupFolder(Environment.SpecialFolder.Startup, "Folder:User Startup");
                    softwareInfo.StartupPrograms = softwareInfo.StartupPrograms.OrderBy(s => s.Location).ThenBy(s => s.Name).ToList();
                } catch (Exception startupEx) {
                     // Corrected: Call AddSpecificError on the instance
                     softwareInfo.AddSpecificError("StartupPrograms", $"Failed to retrieve startup programs: {startupEx.Message}");
                }


                // --- Environment Variables ---
                try { softwareInfo.SystemEnvironmentVariables = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Machine).Cast<System.Collections.DictionaryEntry>().OrderBy(v => v.Key.ToString()).ToDictionary(v => v.Key.ToString()!, v => v.Value?.ToString() ?? ""); }
                catch (Exception sysEnvEx) {
                     // Corrected: Call AddSpecificError on the instance
                     softwareInfo.AddSpecificError("SystemEnvVars", $"Failed to get system environment variables: {sysEnvEx.Message}");
                }

                try { softwareInfo.UserEnvironmentVariables = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.User).Cast<System.Collections.DictionaryEntry>().OrderBy(v => v.Key.ToString()).ToDictionary(v => v.Key.ToString()!, v => v.Value?.ToString() ?? ""); }
                catch (Exception userEnvEx) {
                    // Corrected: Call AddSpecificError on the instance
                     softwareInfo.AddSpecificError("UserEnvVars", $"Failed to get user environment variables: {userEnvEx.Message}");
                }

            }
             catch(Exception ex) // Catch errors in the overall collection setup
            {
                 Console.Error.WriteLine($"[CRITICAL ERROR] Software Info Collection failed: {ex.Message}");
                 softwareInfo.SectionCollectionErrorMessage = $"Critical failure during Software Info collection: {ex.Message}";
            }
            return Task.FromResult(softwareInfo);
        }
    }
}