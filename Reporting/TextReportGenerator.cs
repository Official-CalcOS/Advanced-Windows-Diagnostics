// Reporting/TextReportGenerator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.IO; // <--- ADDED for File.Exists

namespace DiagnosticToolAllInOne.Reporting
{
    public static class TextReportGenerator
    {
        private const string Separator = "----------------------------------------";

        public static string GenerateReport(DiagnosticReport report)
        {
            var sb = new StringBuilder();

            // --- Report Header ---
            sb.AppendLine("========================================");
            sb.AppendLine("   Advanced Windows Diagnostic Tool Report");
            sb.AppendLine("========================================");
            sb.AppendLine($"Generated: {DateTime.Now} (Local Time) / {report.ReportTimestamp:u} (UTC)");
            sb.AppendLine($"Ran as Administrator: {report.RanAsAdmin}");
            if(report.Configuration != null) {
                 // Use Path.Combine for robust path handling
                 string configFilePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                 // Check if the config file actually exists
                 sb.AppendLine($"Configuration Source: {(File.Exists(configFilePath) ? "appsettings.json" : "Defaults")}"); // Indicate config source using System.IO.File
            }
            sb.AppendLine(Separator + "\n");


            // --- Section Rendering Logic ---
            Action<string, DiagnosticSection?, Action<object>> AppendSection = (title, data, renderAction) =>
            {
                sb.AppendLine(Separator);
                sb.AppendLine($"--- {title} ---");
                sb.AppendLine(Separator);

                if (data == null)
                {
                    sb.AppendLine($"[{title} data not collected or section skipped]");
                    sb.AppendLine();
                    return;
                }

                // Display critical section error first if it exists
                if (!string.IsNullOrEmpty(data.SectionCollectionErrorMessage))
                {
                    sb.AppendLine($"[CRITICAL ERROR collecting {title}: {data.SectionCollectionErrorMessage}]");
                    // Decide if you want to stop rendering the rest of the section on critical error
                    // sb.AppendLine(); return;
                }

                // Display specific errors/warnings collected within the section
                if (data.SpecificCollectionErrors?.Any() ?? false)
                {
                    sb.AppendLine($"[{title} - Collection Warnings/Errors]:");
                    foreach(var kvp in data.SpecificCollectionErrors) { sb.AppendLine($"  - {kvp.Key}: {kvp.Value}"); }
                    sb.AppendLine();
                }

                // Render the actual data using the provided action
                try { renderAction(data); }
                catch (Exception ex) { sb.AppendLine($"[ERROR rendering section '{title}': {ex.Message}]"); }
                sb.AppendLine(); // Add blank line after each section
            };

            // Append Sections using the helper action
            AppendSection("System Information", report.System, data => RenderSystemInfo(sb, (SystemInfo)data));
            AppendSection("Hardware Information", report.Hardware, data => RenderHardwareInfo(sb, (HardwareInfo)data));
            AppendSection("Software & Configuration", report.Software, data => RenderSoftwareInfo(sb, (SoftwareInfo)data));
            AppendSection("Security Information", report.Security, data => RenderSecurityInfo(sb, (SecurityInfo)data));
            AppendSection("Performance Snapshot", report.Performance, data => RenderPerformanceInfo(sb, (PerformanceInfo)data));
            AppendSection("Network Information", report.Network, data => RenderNetworkInfo(sb, (NetworkInfo)data));
            AppendSection("Recent Event Logs", report.Events, data => RenderEventLogInfo(sb, (EventLogInfo)data));
            // Pass config to analysis renderer
            AppendSection("Analysis Summary", report.Analysis, data => RenderAnalysisSummary(sb, (AnalysisSummary)data, report.Analysis?.Configuration ?? report.Configuration));


            return sb.ToString();
        }

        #region Render Methods (Updated)

        private static void RenderSystemInfo(StringBuilder sb, SystemInfo data)
        {
            // OS Info
            if (data.OperatingSystem != null)
            {
                sb.AppendLine(" Operating System:");
                sb.AppendLine($"  Name: {data.OperatingSystem.Name ?? "N/A"} ({data.OperatingSystem.Architecture ?? "N/A"})");
                sb.AppendLine($"  Version: {data.OperatingSystem.Version ?? "N/A"} (Build: {data.OperatingSystem.BuildNumber ?? "N/A"})"); // Highlight build number for Win11
                sb.AppendLine($"  Install Date: {FormatNullableDateTime(data.OperatingSystem.InstallDate, "yyyy-MM-dd HH:mm:ss")}");
                sb.AppendLine($"  Last Boot Time: {FormatNullableDateTime(data.OperatingSystem.LastBootTime, "yyyy-MM-dd HH:mm:ss")}");
                if (data.OperatingSystem.Uptime.HasValue) { var ts = data.OperatingSystem.Uptime.Value; sb.AppendLine($"  System Uptime: {ts.Days}d {ts.Hours}h {ts.Minutes}m {ts.Seconds}s"); }
                sb.AppendLine($"  System Drive: {data.OperatingSystem.SystemDrive ?? "N/A"}");
            } else { sb.AppendLine(" Operating System: Data unavailable."); }

            // Computer System Info
            if (data.ComputerSystem != null)
            {
                sb.AppendLine("\n Computer System:");
                sb.AppendLine($"  Manufacturer: {data.ComputerSystem.Manufacturer ?? "N/A"}");
                sb.AppendLine($"  Model: {data.ComputerSystem.Model ?? "N/A"} ({data.ComputerSystem.SystemType ?? "N/A"})");
                sb.AppendLine($"  Domain/Workgroup: {data.ComputerSystem.DomainOrWorkgroup ?? "N/A"} (PartOfDomain: {data.ComputerSystem.PartOfDomain})");
                sb.AppendLine($"  Executing User: {data.ComputerSystem.CurrentUser ?? "N/A"}");
                sb.AppendLine($"  Logged In User (WMI): {data.ComputerSystem.LoggedInUserWMI ?? "N/A"}");
            } else { sb.AppendLine("\n Computer System: Data unavailable."); }

            // Baseboard Info
             if (data.Baseboard != null)
            {
                sb.AppendLine("\n Baseboard (Motherboard):");
                sb.AppendLine($"  Manufacturer: {data.Baseboard.Manufacturer ?? "N/A"}");
                sb.AppendLine($"  Product: {data.Baseboard.Product ?? "N/A"}");
                sb.AppendLine($"  Serial Number: {data.Baseboard.SerialNumber ?? "N/A"}");
                sb.AppendLine($"  Version: {data.Baseboard.Version ?? "N/A"}");
            } else { sb.AppendLine("\n Baseboard (Motherboard): Data unavailable."); }

            // BIOS Info
            if (data.BIOS != null)
            {
                 sb.AppendLine("\n BIOS:");
                 sb.AppendLine($"  Manufacturer: {data.BIOS.Manufacturer ?? "N/A"}");
                 sb.AppendLine($"  Version: {data.BIOS.Version ?? "N/A"}");
                 sb.AppendLine($"  Release Date: {FormatNullableDateTime(data.BIOS.ReleaseDate, "yyyy-MM-dd")}");
                 sb.AppendLine($"  Serial Number: {data.BIOS.SerialNumber ?? "N/A"}");
            } else { sb.AppendLine("\n BIOS: Data unavailable."); }

            // TimeZone Info
            if (data.TimeZone != null)
            {
                sb.AppendLine("\n Time Zone:");
                sb.AppendLine($"  Caption: {data.TimeZone.CurrentTimeZone ?? "N/A"}");
                sb.AppendLine($"  Bias (UTC Offset Mins): {data.TimeZone.BiasMinutes?.ToString() ?? "N/A"}");
            } else { sb.AppendLine("\n Time Zone: Data unavailable."); }

             // Power Plan
            if (data.ActivePowerPlan != null) { sb.AppendLine($"\n Active Power Plan: {data.ActivePowerPlan.Name ?? "N/A"} ({data.ActivePowerPlan.InstanceID ?? ""})"); }
            else { sb.AppendLine("\n Active Power Plan: Data unavailable."); }

            // .NET Version
             sb.AppendLine($"\n .NET Runtime (Executing): {data.DotNetVersion ?? "N/A"}");
        }

        private static void RenderHardwareInfo(StringBuilder sb, HardwareInfo data)
        {
            // Processor
            if (data.Processors?.Any() ?? false) {
                sb.AppendLine(" Processor(s) (CPU):");
                foreach(var cpu in data.Processors) {
                     sb.AppendLine($"  - Name: {cpu.Name ?? "N/A"}");
                     sb.AppendLine($"    Socket: {cpu.Socket ?? "N/A"}, Cores: {cpu.Cores?.ToString() ?? "N/A"}, Logical Processors: {cpu.LogicalProcessors?.ToString() ?? "N/A"}");
                     sb.AppendLine($"    Max Speed: {cpu.MaxSpeedMHz?.ToString() ?? "N/A"} MHz, L2 Cache: {cpu.L2Cache ?? "N/A"}, L3 Cache: {cpu.L3Cache ?? "N/A"}");
                }
            } else { sb.AppendLine(" Processor(s) (CPU): Data unavailable."); }

            // Memory
            if (data.Memory != null) {
                sb.AppendLine("\n Memory (RAM):");
                sb.AppendLine($"  Total Visible: {data.Memory.TotalVisible ?? "N/A"} ({data.Memory.TotalVisibleMemoryKB?.ToString("#,##0") ?? "?"} KB), Available: {data.Memory.Available ?? "N/A"}, Used: {data.Memory.Used ?? "N/A"} ({data.Memory.PercentUsed:0.##}% Used)");
                 if(data.Memory.Modules?.Any() ?? false) {
                    sb.AppendLine("  Physical Modules:");
                    foreach(var mod in data.Memory.Modules) {
                        sb.AppendLine($"    - [{mod.DeviceLocator ?? "?"}] {mod.Capacity ?? "?"} @ {mod.SpeedMHz?.ToString() ?? "?"}MHz ({mod.MemoryType ?? "?"} / {mod.FormFactor ?? "?"}) Mfg: {mod.Manufacturer ?? "?"}, Part#: {mod.PartNumber ?? "?"}, Bank: {mod.BankLabel ?? "?"}");
                    }
                 } else { sb.AppendLine("  Physical Modules: Data unavailable or none found."); }
            } else { sb.AppendLine("\n Memory (RAM): Data unavailable."); }

            // Physical Disks
            if (data.PhysicalDisks?.Any() ?? false) {
                sb.AppendLine("\n Physical Disks:");
                foreach(var disk in data.PhysicalDisks.OrderBy(d => d.Index)) {
                    string systemDiskIndicator = disk.IsSystemDisk == true ? " (System Disk)" : ""; // Assuming populated
                    sb.AppendLine($"  Disk #{disk.Index}{systemDiskIndicator}: {disk.Model ?? "N/A"} ({disk.MediaType ?? "Unknown media"})");
                    sb.AppendLine($"    Interface: {disk.InterfaceType ?? "N/A"}, Size: {disk.Size ?? "N/A"} ({disk.SizeBytes?.ToString("#,##0") ?? "?"} Bytes), Partitions: {disk.Partitions?.ToString() ?? "N/A"}, Serial: {disk.SerialNumber ?? "N/A"}, Status: {disk.Status ?? "N/A"}");
                     string smartDisplay = "N/A";
                     if (disk.SmartStatus != null)
                     {
                         smartDisplay = $"{disk.SmartStatus.StatusText ?? "Unknown"}";
                         if (disk.SmartStatus.IsFailurePredicted) smartDisplay = $"!!! FAILURE PREDICTED !!! (Reason: {disk.SmartStatus.ReasonCode ?? "N/A"})";
                         else if (disk.SmartStatus.Error != null) smartDisplay += $" (Error: {disk.SmartStatus.Error})";
                         if (disk.SmartStatus.StatusText != "OK" && !string.IsNullOrEmpty(disk.SmartStatus.BasicStatusFromDiskDrive)) smartDisplay += $" (Basic HW Status: {disk.SmartStatus.BasicStatusFromDiskDrive})";
                     }
                     sb.AppendLine($"    SMART Status: {smartDisplay}");
                }
                 // Note about system disk mapping if not implemented or uncertain
                 // sb.AppendLine("    (Note: System disk identification relies on WMI mapping and might be approximate)");
            } else { sb.AppendLine("\n Physical Disks: Data unavailable or none found."); }

            // Logical Disks
            if (data.LogicalDisks?.Any() ?? false) {
                sb.AppendLine("\n Logical Disks (Local Fixed):");
                 foreach(var disk in data.LogicalDisks.OrderBy(d => d.DeviceID)) {
                    sb.AppendLine($"  {disk.DeviceID ?? "?"} ({disk.VolumeName ?? "N/A"}) - {disk.FileSystem ?? "N/A"}");
                    sb.AppendLine($"    Size: {disk.Size ?? "N/A"} ({disk.SizeBytes?.ToString("#,##0") ?? "?"} Bytes), Free: {disk.FreeSpace ?? "N/A"} ({disk.PercentFree:0.#}% Free)");
                 }
            } else { sb.AppendLine("\n Logical Disks (Local Fixed): Data unavailable or none found."); }

            // Volumes
             if (data.Volumes?.Any() ?? false) {
                sb.AppendLine("\n Volumes:");
                foreach(var vol in data.Volumes)
                {
                   sb.AppendLine($"  {vol.DriveLetter ?? "N/A"} ({vol.Name ?? "No Name"}) - {vol.FileSystem ?? "N/A"}");
                   sb.AppendLine($"    Capacity: {vol.Capacity ?? "N/A"}, Free: {vol.FreeSpace ?? "N/A"}");
                   sb.AppendLine($"    Device ID: {vol.DeviceID ?? "N/A"}");
                   sb.AppendLine($"    BitLocker Status: {vol.ProtectionStatus ?? "N/A"}");
                }
             }
             else { sb.AppendLine("\n Volumes: Data unavailable or none found."); }

             // GPU
            if (data.Gpus?.Any() ?? false) {
                sb.AppendLine("\n Video Controllers (GPU):");
                 foreach(var gpu in data.Gpus) {
                    sb.AppendLine($"  - Name: {gpu.Name ?? "N/A"} (Status: {gpu.Status ?? "N/A"})");
                    sb.AppendLine($"    VRAM: {gpu.Vram ?? "N/A"}, Video Proc: {gpu.VideoProcessor ?? "N/A"}");
                    // Render driver date
                    sb.AppendLine($"    Driver: {gpu.DriverVersion ?? "N/A"} (Date: {FormatNullableDateTime(gpu.DriverDate, "yyyy-MM-dd")})");
                    sb.AppendLine($"    Current Resolution: {gpu.CurrentResolution ?? "N/A"}");
                 }
            } else { sb.AppendLine("\n Video Controllers (GPU): Data unavailable or none found."); }

            // Monitors
            if (data.Monitors?.Any() ?? false) {
                sb.AppendLine("\n Monitors:");
                 foreach(var mon in data.Monitors) {
                    sb.AppendLine($"  - Name: {mon.Name ?? "N/A"} (ID: {mon.DeviceID ?? "N/A"})");
                    sb.AppendLine($"    Mfg: {mon.Manufacturer ?? "N/A"}, Res: {mon.ReportedResolution ?? "N/A"}, PPI: {mon.PpiLogical ?? "N/A"}");
                 }
            } else { sb.AppendLine("\n Monitors: Data unavailable or none detected."); }

            // Audio
            if (data.AudioDevices?.Any() ?? false) {
                sb.AppendLine("\n Audio Devices:");
                foreach(var audio in data.AudioDevices) {
                    sb.AppendLine($"  - {audio.Name ?? "N/A"} (Product: {audio.ProductName ?? "N/A"}, Mfg: {audio.Manufacturer ?? "N/A"}, Status: {audio.Status ?? "N/A"})");
                }
            }
            else { sb.AppendLine("\n Audio Devices: Data unavailable or none found."); }
        }

        private static void RenderSoftwareInfo(StringBuilder sb, SoftwareInfo data)
        {
             // Installed Applications
             if (data.InstalledApplications?.Any() ?? false) {
                sb.AppendLine(" Installed Applications:");
                sb.AppendLine($"  Count: {data.InstalledApplications.Count}");
                 int count = 0;
                 // Sort apps alphabetically for easier reading
                 foreach(var app in data.InstalledApplications.OrderBy(a => a.Name)) {
                     sb.AppendLine($"  - {app.Name ?? "N/A"} (Version: {app.Version ?? "N/A"}, Publisher: {app.Publisher ?? "N/A"}, Installed: {FormatNullableDateTime(app.InstallDate, "yyyy-MM-dd")})");
                     count++;
                     // Limit output for extremely long lists
                     if (count >= 100 && data.InstalledApplications.Count > 100) { sb.AppendLine($"    (... and {data.InstalledApplications.Count - count} more)"); break; }
                 }
             }
             else { sb.AppendLine(" Installed Applications: Data unavailable or none found."); }

             // Windows Updates
            if (data.WindowsUpdates?.Any() ?? false) {
                sb.AppendLine("\n Installed Windows Updates (Hotfixes):");
                // Sort updates by date descending
                foreach(var upd in data.WindowsUpdates.OrderByDescending(u => u.InstalledOn ?? DateTime.MinValue)) {
                     sb.AppendLine($"  - {upd.HotFixID ?? "N/A"} ({upd.Description ?? "N/A"}) - Installed: {FormatNullableDateTime(upd.InstalledOn)}");
                }
            }
            else { sb.AppendLine("\n Installed Windows Updates (Hotfixes): Data unavailable or none found."); }

            // Services
             if (data.RelevantServices?.Any() ?? false) {
                sb.AppendLine("\n Relevant Services (Running, Critical, or Non-Microsoft):");
                // Sort services by name
                foreach(var svc in data.RelevantServices.OrderBy(s => s.DisplayName ?? s.Name)) {
                    sb.AppendLine($"  - {svc.DisplayName ?? svc.Name ?? "N/A"} ({svc.Name ?? "N/A"})");
                    sb.AppendLine($"    State: {svc.State ?? "N/A"}, StartMode: {svc.StartMode ?? "N/A"}, Status: {svc.Status ?? "N/A"}");
                    sb.AppendLine($"    Path: {svc.PathName ?? "N/A"}");
                }
             }
             else { sb.AppendLine("\n Relevant Services: Data unavailable or none found."); }

            // Startup Programs
            if (data.StartupPrograms?.Any() ?? false) {
                sb.AppendLine("\n Startup Programs:");
                 // Sort by location then name
                foreach(var prog in data.StartupPrograms.OrderBy(p => p.Location).ThenBy(p => p.Name)) {
                    sb.AppendLine($"  - [{prog.Location ?? "N/A"}] {prog.Name ?? "N/A"} = {prog.Command ?? "N/A"}");
                }
            }
            else { sb.AppendLine("\n Startup Programs: Data unavailable or none found."); }

            // Environment Variables
            Action<string, Dictionary<string, string>?> renderEnvVars = (title, vars) => {
                sb.AppendLine($"\n {title} Environment Variables:");
                if (vars?.Any() ?? false) {
                    int count = 0;
                    foreach (var kvp in vars.OrderBy(v => v.Key)) { // Sort alphabetically
                        sb.AppendLine($"  {kvp.Key}={kvp.Value}");
                        count++;
                        // Limit output
                        if (count >= 30 && vars.Count > 30) { sb.AppendLine($"    (... and {vars.Count - count} more)"); break; }
                    }
                } else { sb.AppendLine("  Data unavailable or none found."); }
            };
            renderEnvVars("System", data.SystemEnvironmentVariables);
            renderEnvVars("User", data.UserEnvironmentVariables);
        }

        private static void RenderSecurityInfo(StringBuilder sb, SecurityInfo data)
        {
             sb.AppendLine($" Running as Administrator: {data.IsAdmin}");
             sb.AppendLine($" UAC Status: {data.UacStatus ?? "N/A"}");
             sb.AppendLine($" Antivirus: {data.Antivirus?.Name ?? "N/A"} (State: {data.Antivirus?.State ?? "Requires Admin or Not Found"})");
             sb.AppendLine($" Firewall: {data.Firewall?.Name ?? "N/A"} (State: {data.Firewall?.State ?? "Requires Admin or Not Found"})");
             // Render Secure Boot and BIOS Mode
             sb.AppendLine($" Secure Boot Enabled: {data.IsSecureBootEnabled?.ToString() ?? "Unknown/Error"}");
             sb.AppendLine($" BIOS Mode (Inferred): {data.BiosMode ?? "Unknown/Error"}");

             // Render TPM Info
             if (data.Tpm != null)
             {
                sb.AppendLine("\n TPM (Trusted Platform Module):");
                sb.AppendLine($"  Present: {data.Tpm.IsPresent?.ToString() ?? "Unknown"}");
                if (data.Tpm.IsPresent == true) {
                     sb.AppendLine($"  Enabled: {data.Tpm.IsEnabled?.ToString() ?? "Unknown"}");
                     sb.AppendLine($"  Activated: {data.Tpm.IsActivated?.ToString() ?? "Unknown"}");
                     sb.AppendLine($"  Spec Version: {data.Tpm.SpecVersion ?? "N/A"}"); // Key for Win11
                     sb.AppendLine($"  Manufacturer: {data.Tpm.ManufacturerIdTxt ?? "N/A"} (Version: {data.Tpm.ManufacturerVersion ?? "N/A"})");
                     sb.AppendLine($"  Status Summary: {data.Tpm.Status ?? "N/A"}");
                }
                if (!string.IsNullOrEmpty(data.Tpm.ErrorMessage)) sb.AppendLine($"  Error: {data.Tpm.ErrorMessage}");
             } else { sb.AppendLine("\n TPM (Trusted Platform Module): Data unavailable."); }

             // Local Users
             if(data.LocalUsers?.Any() ?? false) {
                sb.AppendLine("\n Local User Accounts:");
                foreach(var user in data.LocalUsers.OrderBy(u => u.Name)) {
                     sb.AppendLine($"  - {user.Name ?? "N/A"} (SID: {user.SID ?? "N/A"})");
                     sb.AppendLine($"    Disabled: {user.IsDisabled}, PwdRequired: {user.PasswordRequired}, PwdChangeable: {user.PasswordChangeable}, IsLocal: {user.IsLocal}");
                }
             }
             else { sb.AppendLine("\n Local User Accounts: Data unavailable or none found."); }

             // Local Groups
             if(data.LocalGroups?.Any() ?? false) {
                sb.AppendLine("\n Local Groups:");
                int count = 0;
                foreach(var grp in data.LocalGroups.OrderBy(g => g.Name)) {
                     sb.AppendLine($"  - {grp.Name ?? "N/A"} (SID: {grp.SID ?? "N/A"}) - {grp.Description ?? "N/A"}");
                     count++; if (count >= 20 && data.LocalGroups.Count > 20) { sb.AppendLine($"    (... and {data.LocalGroups.Count - count} more)"); break; }
                }
             }
             else { sb.AppendLine("\n Local Groups: Data unavailable or none found."); }

            // Network Shares
            if(data.NetworkShares?.Any() ?? false) {
                sb.AppendLine("\n Network Shares:");
                foreach(var share in data.NetworkShares.OrderBy(s => s.Name)) {
                    sb.AppendLine($"  - {share.Name ?? "N/A"} -> {share.Path ?? "N/A"} ({share.Description ?? "N/A"}, Type: {share.Type?.ToString() ?? "?"})");
                }
            }
            else { sb.AppendLine("\n Network Shares: Data unavailable or none found."); }
        }

        private static void RenderPerformanceInfo(StringBuilder sb, PerformanceInfo data)
        {
            sb.AppendLine(" Performance Counters (Snapshot/Sampled):");
            sb.AppendLine($"  Overall CPU Usage: {data.OverallCpuUsagePercent ?? "N/A"} %");
            sb.AppendLine($"  Available Memory: {data.AvailableMemoryMB ?? "N/A"} MB");
            sb.AppendLine($"  Avg. Disk Queue Length (Total): {data.TotalDiskQueueLength ?? "N/A"}");

             Action<string, List<ProcessUsageInfo>?> renderProcs = (title, procs) => {
                 sb.AppendLine($"\n Top Processes by {title}:");
                 if (procs?.Any() ?? false) {
                     // Sort processes by name for consistency
                     foreach(var p in procs.OrderBy(pi => pi.Name ?? string.Empty)) {
                         sb.Append($"  - PID: {p.Pid,-6} Name: {p.Name ?? "N/A", -25}");
                         if (p.MemoryUsage != null) sb.Append($" Memory: {p.MemoryUsage,-12}");
                         // sb.Append($" Status: {p.Status ?? "N/A"}"); // Status can be verbose
                         if (!string.IsNullOrEmpty(p.Error)) sb.Append($" (Error: {p.Error})");
                         sb.AppendLine();
                     }
                 } else if (!string.IsNullOrEmpty(data.SectionCollectionErrorMessage)) { sb.AppendLine($"  Data unavailable due to section error: {data.SectionCollectionErrorMessage}"); }
                 else { sb.AppendLine("  Data unavailable or none found."); }
             };
             renderProcs("Memory (Working Set)", data.TopMemoryProcesses);
             renderProcs("Total CPU Time (Snapshot)", data.TopCpuProcesses);
        }

        // --- Updated Network Rendering ---
        private static void RenderNetworkInfo(StringBuilder sb, NetworkInfo data)
        {
            if (data.Adapters?.Any() ?? false) {
                sb.AppendLine(" Network Adapters:");
                // Sort adapters by description or name
                foreach(var adapter in data.Adapters.OrderBy(a => a.Description ?? a.Name)) {
                    sb.AppendLine($"\n  {adapter.Name} ({adapter.Description})");
                    sb.AppendLine($"    Status: {adapter.Status}, Type: {adapter.Type}, Speed: {adapter.SpeedMbps} Mbps, MAC: {adapter.MacAddress ?? "N/A"}");
                    sb.AppendLine($"    ID: {adapter.Id}, Index: {adapter.InterfaceIndex?.ToString() ?? "N/A"}"); // Added Index
                    sb.AppendLine($"    IP Addresses: {FormatList(adapter.IpAddresses)}");
                    sb.AppendLine($"    Gateways: {FormatList(adapter.Gateways)}");
                    sb.AppendLine($"    DNS Servers: {FormatList(adapter.DnsServers)}");
                    sb.AppendLine($"    DNS Suffix: {adapter.DnsSuffix ?? "N/A"}"); // Added Suffix
                    sb.AppendLine($"    WINS Servers: {FormatList(adapter.WinsServers)}"); // Added WINS
                    sb.AppendLine($"    DHCP Enabled: {adapter.DhcpEnabled} (Lease Obtained: {FormatNullableDateTime(adapter.DhcpLeaseObtained)}, Expires: {FormatNullableDateTime(adapter.DhcpLeaseExpires)})");
                    sb.AppendLine($"    WMI Service Name: {adapter.WmiServiceName ?? "N/A"}");
                    // Assuming DriverDate might be populated
                    sb.AppendLine($"    Driver Date: {FormatNullableDateTime(adapter.DriverDate, "yyyy-MM-dd")}"); // Added Driver Date
                }
            }
            else { sb.AppendLine(" Network Adapters: No adapters found or data unavailable."); }

             Action<string, List<ActivePortInfo>?> renderListeners = (title, listeners) => {
                 sb.AppendLine($"\n Active {title} Listeners:");
                 if (listeners?.Any() ?? false) {
                      sb.AppendLine("  Local Address:Port       PID    Process Name");
                      sb.AppendLine("  ------------------------ ------ ------------------------------");
                     // Sort listeners by port number
                     foreach (var l in listeners.OrderBy(p => p.LocalPort)) {
                         string localEndpoint = $"{l.LocalAddress}:{l.LocalPort}";
                         sb.AppendLine($"  {localEndpoint,-24} {l.OwningPid?.ToString() ?? "-",-6} {l.OwningProcessName ?? "N/A"}");
                         if (!string.IsNullOrEmpty(l.Error)) sb.AppendLine($"      Error: {l.Error}");
                     }
                 } else { sb.AppendLine("  Data unavailable or none found."); }
             };
             renderListeners("TCP", data.ActiveTcpListeners);
             renderListeners("UDP", data.ActiveUdpListeners);

             // Render TCP Connections
             if (data.ActiveTcpConnections?.Any() ?? false) {
                 sb.AppendLine("\n Active TCP Connections:");
                 sb.AppendLine("  Local Address:Port       Remote Address:Port      State           PID    Process Name");
                 sb.AppendLine("  ------------------------ ------------------------ --------------- ------ ------------------------------");
                 // Sort connections maybe by state then local port?
                 foreach(var conn in data.ActiveTcpConnections.OrderBy(c => c.State).ThenBy(c => c.LocalPort)) {
                     string localEp = $"{conn.LocalAddress}:{conn.LocalPort}";
                     string remoteEp = $"{conn.RemoteAddress}:{conn.RemotePort}";
                     sb.AppendLine($"  {localEp,-24} {remoteEp,-24} {conn.State,-15} {conn.OwningPid?.ToString() ?? "-",-6} {conn.OwningProcessName ?? "N/A"}");
                     if (!string.IsNullOrEmpty(conn.Error)) sb.AppendLine($"      Error: {conn.Error}");
                 }
             } else { sb.AppendLine("\n Active TCP Connections: Data unavailable or none found."); }


            // Render Connectivity Tests
            if (data.ConnectivityTests != null) {
                sb.AppendLine("\n Connectivity Tests:");
                RenderPingResult(sb, data.ConnectivityTests.GatewayPing, "Default Gateway");
                if (data.ConnectivityTests.DnsPings?.Any() ?? false) {
                     foreach(var ping in data.ConnectivityTests.DnsPings) RenderPingResult(sb, ping);
                } else { sb.AppendLine("  DNS Server Ping Tests: Not performed or data unavailable."); }

                RenderDnsResolutionResult(sb, data.ConnectivityTests.DnsResolution); // Added DNS resolution result

                if (data.ConnectivityTests.TracerouteResults?.Any() ?? false) {
                    sb.AppendLine($"\n  Traceroute to {data.ConnectivityTests.TracerouteTarget ?? "Unknown Target"}:");
                    sb.AppendLine("    Hop  RTT (ms)  Address           Status");
                    sb.AppendLine("    ---  --------  ----------------- -----------------");
                    foreach (var hop in data.ConnectivityTests.TracerouteResults) {
                        // Now accessing hop.Error should work
                        sb.AppendLine($"    {hop.Hop,3}  {(hop.RoundtripTimeMs?.ToString() ?? "*"),8}  {hop.Address ?? "*",-17} {hop.Status ?? "N/A"} {(string.IsNullOrEmpty(hop.Error) ? "" : $"(Error: {hop.Error})")}");
                    }
                } else if (!string.IsNullOrEmpty(data.ConnectivityTests.TracerouteTarget)) { sb.AppendLine("\n  Traceroute: Not performed or failed."); }
            }
            else { sb.AppendLine("\n Connectivity Tests: Data unavailable."); }
        }

        // --- Updated Ping/DNS Render Helpers ---
        private static void RenderPingResult(StringBuilder sb, PingResult? result, string? defaultTargetName = null)
        {
             if(result == null) { sb.AppendLine($"  Ping Test ({defaultTargetName ?? "Unknown Target"}): Data unavailable."); return; }
             string target = string.IsNullOrEmpty(result.Target) ? defaultTargetName ?? "Unknown Target" : result.Target;
             string resolvedIP = string.IsNullOrEmpty(result.ResolvedIpAddress) ? "" : $" [{result.ResolvedIpAddress}]"; // Show resolved IP
             sb.Append($"  Ping Test ({target}{resolvedIP}): Status = {result.Status ?? "N/A"}");
             if (result.Status == IPStatus.Success.ToString()) { sb.Append($" ({result.RoundtripTimeMs ?? 0} ms)"); }
             if (!string.IsNullOrEmpty(result.Error)) { sb.Append($" (Error: {result.Error})"); }
             sb.AppendLine();
        }

        private static void RenderDnsResolutionResult(StringBuilder sb, DnsResolutionResult? result)
        {
             if(result == null) { sb.AppendLine($"  DNS Resolution Test: Data unavailable."); return; }
             string target = result.Hostname ?? "Default Host";
             sb.Append($"  DNS Resolution Test ({target}): Success = {result.Success}");
             if (result.Success) {
                 sb.Append($" ({result.ResolutionTimeMs ?? 0} ms)");
                 sb.Append($" -> IPs: {FormatList(result.ResolvedIpAddresses)}");
             }
             if (!string.IsNullOrEmpty(result.Error)) { sb.Append($" (Error: {result.Error})"); }
             sb.AppendLine();
        }


        private static void RenderEventLogInfo(StringBuilder sb, EventLogInfo data)
        {
             Action<string, List<EventEntry>?> renderLog = (logName, entries) => {
                 sb.AppendLine($"\n {logName} Event Log (Recent Errors/Warnings):");
                 if (entries?.Any() ?? false) {
                    // Handle collector messages (like Access Denied)
                    if (entries.Count == 1 && entries[0].Source == null) { sb.AppendLine($"  [{entries[0].Message}]"); return; }

                    var actualEntries = entries.Where(e => e.Source != null).ToList();
                    if(!actualEntries.Any()) {
                        // Check if the only messages were collector errors
                         if (entries.Count > 0 && entries.All(e => e.Source == null)) {
                              foreach(var entry in entries) sb.AppendLine($"  [{entry.Message}]"); // Display collector messages
                         } else {
                              sb.AppendLine($"  No recent Error/Warning entries found.");
                         }
                         return;
                    }

                    int count = 0;
                    // Sort by time descending
                    foreach (var entry in actualEntries.OrderByDescending(e => e.TimeGenerated)) {
                         string msg = entry.Message?.Replace("\r", "").Replace("\n", " ").Trim() ?? ""; // Clean message
                         if (msg.Length > 150) msg = msg.Substring(0, 147) + "..."; // Truncate long messages
                         sb.AppendLine($"  - {FormatNullableDateTime(entry.TimeGenerated)} [{entry.EntryType}] {entry.Source ?? "?"} (ID:{entry.InstanceId})");
                         sb.AppendLine($"      Message: {msg}");
                         count++;
                         // Limit output
                         if (count >= 20 && actualEntries.Count > 20) { sb.AppendLine($"    (... and {actualEntries.Count - count} more Error/Warning entries)"); break; }
                    }
                 } else if (!string.IsNullOrEmpty(data.SectionCollectionErrorMessage)) { sb.AppendLine($"  Data unavailable due to section error: {data.SectionCollectionErrorMessage}"); }
                 else { sb.AppendLine("  Data unavailable or none found."); }
             };
            renderLog("System", data.SystemLogEntries);
            sb.AppendLine(); // Add space between logs
            renderLog("Application", data.ApplicationLogEntries);
        }


        // --- Updated Analysis Summary Rendering ---
        private static void RenderAnalysisSummary(StringBuilder sb, AnalysisSummary data, AppConfiguration? config)
        {
             if (data == null) { sb.AppendLine("Analysis data unavailable or analysis did not run."); return; }
             bool anyContent = false;

             // Display analysis errors first
             if (!string.IsNullOrEmpty(data.SectionCollectionErrorMessage))
             { sb.AppendLine($"[ANALYSIS ENGINE ERROR: {data.SectionCollectionErrorMessage}]"); anyContent = true; }

            // Render structured Windows 11 Readiness results if present
            if (data.Windows11Readiness?.Checks?.Any() ?? false)
            {
                anyContent = true;
                sb.AppendLine("\n>>> Windows 11 Readiness Check:");
                string overallStatus = data.Windows11Readiness.OverallResult switch {
                    true => "PASS", false => "FAIL", _ => "INCOMPLETE/ERROR" };
                sb.AppendLine($"  Overall Status: {overallStatus}");
                sb.AppendLine("  Component        | Status         | Details");
                sb.AppendLine("  -----------------|----------------|-----------------------------------------");
                foreach(var check in data.Windows11Readiness.Checks.OrderBy(c => c.ComponentChecked))
                {
                    string statusColor = check.Status?.ToUpperInvariant() switch { "PASS" => "", "FAIL" => "!", "WARNING" => "?", _ => "" }; // Add indicators
                    sb.AppendLine($"  {check.ComponentChecked?.PadRight(16) ?? "General".PadRight(16)} | {statusColor}{check.Status?.PadRight(14) ?? "Unknown".PadRight(14)} | {check.Details ?? ""}");
                }
                sb.AppendLine();
            }

            // Render issues/suggestions/info with clear labels
            if (data.PotentialIssues?.Any() ?? false) {
                anyContent = true;
                sb.AppendLine("\n>>> Potential Issues Found:");
                foreach (var issue in data.PotentialIssues) sb.AppendLine($"  - [ISSUE] {issue}");
                sb.AppendLine();
            }
            if (data.Suggestions?.Any() ?? false) {
                anyContent = true;
                sb.AppendLine("\n>>> Suggestions:");
                 foreach (var suggestion in data.Suggestions) sb.AppendLine($"  - [SUGGESTION] {suggestion}");
                 sb.AppendLine();
            }
            if (data.Info?.Any() ?? false) {
                anyContent = true;
                sb.AppendLine("\n>>> Informational Notes:");
                foreach (var info in data.Info) sb.AppendLine($"  - [INFO] {info}");
                sb.AppendLine();
            }

            // Use the configuration stored within the AnalysisSummary if available, otherwise use the main report config
            var analysisConfig = data.Configuration ?? config;

            // Display thresholds used if config was available
            if(analysisConfig != null && anyContent) // Only show if analysis ran and config was present
            {
                sb.AppendLine("\n--- Analysis Thresholds Used ---");
                sb.AppendLine($"  Memory High/Elevated %: {analysisConfig.AnalysisThresholds.HighMemoryUsagePercent}/{analysisConfig.AnalysisThresholds.ElevatedMemoryUsagePercent}");
                sb.AppendLine($"  Disk Critical/Low Free %: {analysisConfig.AnalysisThresholds.CriticalDiskSpacePercent}/{analysisConfig.AnalysisThresholds.LowDiskSpacePercent}");
                sb.AppendLine($"  CPU High/Elevated %: {analysisConfig.AnalysisThresholds.HighCpuUsagePercent}/{analysisConfig.AnalysisThresholds.ElevatedCpuUsagePercent}");
                sb.AppendLine($"  High Disk Queue Length: {analysisConfig.AnalysisThresholds.HighDiskQueueLength}");
                sb.AppendLine($"  Max Sys/App Log Errors (Issue): {analysisConfig.AnalysisThresholds.MaxSystemLogErrorsIssue}/{analysisConfig.AnalysisThresholds.MaxAppLogErrorsIssue}");
                sb.AppendLine($"  Driver Age Warning (Years): {analysisConfig.AnalysisThresholds.DriverAgeWarningYears}");
                sb.AppendLine($"  Ping/Traceroute Latency Warn (ms): {analysisConfig.AnalysisThresholds.MaxPingLatencyWarningMs}/{analysisConfig.AnalysisThresholds.MaxTracerouteHopLatencyWarningMs}");
                // Add other relevant thresholds here
            }

            if (!anyContent && string.IsNullOrEmpty(data.SectionCollectionErrorMessage)) {
                 sb.AppendLine("No specific issues, suggestions, or notes generated by the analysis based on collected data and configured thresholds.");
            }
        }
        #endregion

        #region Formatting Helpers
        private static string FormatNullableDateTime(DateTime? dt, string format = "yyyy-MM-dd HH:mm:ss") // Default format
        { return dt.HasValue ? dt.Value.ToString(format) : "N/A"; }

        private static string FormatList(List<string>? list, string separator = ", ")
        {
             if (list == null || !list.Any()) return "N/A";
             // Filter out null or empty strings before joining
             var filteredList = list.Where(s => !string.IsNullOrEmpty(s)).ToList();
             return filteredList.Any() ? string.Join(separator, filteredList) : "N/A";
        }
        #endregion
    }
}