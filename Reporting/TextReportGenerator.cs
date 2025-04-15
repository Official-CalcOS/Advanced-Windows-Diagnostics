// Reporting/TextReportGenerator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;

namespace DiagnosticToolAllInOne.Reporting
{
    public static class TextReportGenerator
    {
        private const string Separator = "----------------------------------------";

        public static string GenerateReport(DiagnosticReport report)
        {
            var sb = new StringBuilder();

            Action<string, DiagnosticSection?, Action<object>> AppendSection = (title, data, renderAction) =>
            {
                sb.AppendLine(Separator);
                sb.AppendLine($"--- {title} ---");
                sb.AppendLine(Separator);

                if (data == null)
                {
                    sb.AppendLine($"[{title} data not collected or collection failed]");
                    sb.AppendLine();
                    return;
                }

                if (!string.IsNullOrEmpty(data.SectionCollectionErrorMessage))
                {
                    sb.AppendLine($"[CRITICAL ERROR collecting {title}: {data.SectionCollectionErrorMessage}]");
                }

                if (data.SpecificCollectionErrors?.Any() ?? false)
                {
                    sb.AppendLine($"[{title} - Specific Collection Errors/Warnings]:");
                    foreach(var kvp in data.SpecificCollectionErrors) { sb.AppendLine($"  - {kvp.Key}: {kvp.Value}"); }
                    sb.AppendLine();
                }

                try { renderAction(data); }
                catch (Exception ex) { sb.AppendLine($"[ERROR rendering section '{title}': {ex.Message}]"); }
                sb.AppendLine();
            };

            // Append Sections
            AppendSection("System Information", report.System, data => RenderSystemInfo(sb, (SystemInfo)data));
            AppendSection("Hardware Information", report.Hardware, data => RenderHardwareInfo(sb, (HardwareInfo)data));
            AppendSection("Software & Configuration", report.Software, data => RenderSoftwareInfo(sb, (SoftwareInfo)data));
            AppendSection("Security Information", report.Security, data => RenderSecurityInfo(sb, (SecurityInfo)data));
            AppendSection("Performance Snapshot", report.Performance, data => RenderPerformanceInfo(sb, (PerformanceInfo)data));
            AppendSection("Network Information", report.Network, data => RenderNetworkInfo(sb, (NetworkInfo)data));
            AppendSection("Recent Event Logs", report.Events, data => RenderEventLogInfo(sb, (EventLogInfo)data));
            AppendSection("Analysis Summary", report.Analysis, data => RenderAnalysisSummary(sb, (AnalysisSummary)data));


            return sb.ToString();
        }

        #region Render Methods

        private static void RenderSystemInfo(StringBuilder sb, SystemInfo data)
        {
            // OS Info
            if (data.OperatingSystem != null)
            {
                sb.AppendLine(" Operating System:");
                sb.AppendLine($"  Name: {data.OperatingSystem.Name ?? "N/A"} ({data.OperatingSystem.Architecture ?? "N/A"})");
                // Highlight build number as it's relevant for Win11
                sb.AppendLine($"  Version: {data.OperatingSystem.Version ?? "N/A"} (Build: {data.OperatingSystem.BuildNumber ?? "N/A"}) <---");
                sb.AppendLine($"  Install Date: {FormatNullableDateTime(data.OperatingSystem.InstallDate, "yyyy-MM-dd HH:mm:ss")}");
                sb.AppendLine($"  Last Boot Time: {FormatNullableDateTime(data.OperatingSystem.LastBootTime, "yyyy-MM-dd HH:mm:ss")}");
                if (data.OperatingSystem.Uptime.HasValue) { var ts = data.OperatingSystem.Uptime.Value; sb.AppendLine($"  System Uptime: {ts.Days}d {ts.Hours}h {ts.Minutes}m {ts.Seconds}s"); }
                sb.AppendLine($"  System Drive: {data.OperatingSystem.SystemDrive ?? "N/A"}");
            } else { sb.AppendLine(" Operating System: Data unavailable."); }

            // Computer System Info (No changes needed)
            if (data.ComputerSystem != null)
            {
                sb.AppendLine("\n Computer System:");
                sb.AppendLine($"  Manufacturer: {data.ComputerSystem.Manufacturer ?? "N/A"}");
                sb.AppendLine($"  Model: {data.ComputerSystem.Model ?? "N/A"} ({data.ComputerSystem.SystemType ?? "N/A"})");
                sb.AppendLine($"  Domain/Workgroup: {data.ComputerSystem.DomainOrWorkgroup ?? "N/A"} (PartOfDomain: {data.ComputerSystem.PartOfDomain})");
                sb.AppendLine($"  Executing User: {data.ComputerSystem.CurrentUser ?? "N/A"}");
                sb.AppendLine($"  Logged In User (WMI): {data.ComputerSystem.LoggedInUserWMI ?? "N/A"}");
            } else { sb.AppendLine("\n Computer System: Data unavailable."); }

            // Baseboard Info (No changes needed)
             if (data.Baseboard != null)
            {
                sb.AppendLine("\n Baseboard (Motherboard):");
                sb.AppendLine($"  Manufacturer: {data.Baseboard.Manufacturer ?? "N/A"}");
                sb.AppendLine($"  Product: {data.Baseboard.Product ?? "N/A"}");
                sb.AppendLine($"  Serial Number: {data.Baseboard.SerialNumber ?? "N/A"}");
                sb.AppendLine($"  Version: {data.Baseboard.Version ?? "N/A"}");
            } else { sb.AppendLine("\n Baseboard (Motherboard): Data unavailable."); }

            // BIOS Info (No changes needed)
            if (data.BIOS != null)
            {
                 sb.AppendLine("\n BIOS:");
                 sb.AppendLine($"  Manufacturer: {data.BIOS.Manufacturer ?? "N/A"}");
                 sb.AppendLine($"  Version: {data.BIOS.Version ?? "N/A"}");
                 sb.AppendLine($"  Release Date: {FormatNullableDateTime(data.BIOS.ReleaseDate, "yyyy-MM-dd")}");
                 sb.AppendLine($"  Serial Number: {data.BIOS.SerialNumber ?? "N/A"}");
            } else { sb.AppendLine("\n BIOS: Data unavailable."); }

            // TimeZone Info (No changes needed)
            if (data.TimeZone != null)
            {
                sb.AppendLine("\n Time Zone:");
                sb.AppendLine($"  Caption: {data.TimeZone.CurrentTimeZone ?? "N/A"}");
                sb.AppendLine($"  Bias (UTC Offset Mins): {data.TimeZone.BiasMinutes?.ToString() ?? "N/A"}");
            } else { sb.AppendLine("\n Time Zone: Data unavailable."); }

             // Power Plan (No changes needed)
            if (data.ActivePowerPlan != null) { sb.AppendLine($"\n Active Power Plan: {data.ActivePowerPlan.Name ?? "N/A"} ({data.ActivePowerPlan.InstanceID ?? ""})"); }
            else { sb.AppendLine("\n Active Power Plan: Data unavailable."); }

            // .NET Version (No changes needed)
             sb.AppendLine($"\n .NET Runtime (Executing): {data.DotNetVersion ?? "N/A"}");
        }

        private static void RenderHardwareInfo(StringBuilder sb, HardwareInfo data)
        {
            // Processor (Highlight relevant specs)
            if (data.Processors?.Any() ?? false) {
                sb.AppendLine(" Processor(s) (CPU):");
                foreach(var cpu in data.Processors) {
                     sb.AppendLine($"  - Name: {cpu.Name ?? "N/A"}");
                     sb.AppendLine($"    Socket: {cpu.Socket ?? "N/A"}, Cores: {cpu.Cores?.ToString() ?? "N/A"} <---, Logical Processors: {cpu.LogicalProcessors?.ToString() ?? "N/A"}");
                     sb.AppendLine($"    Max Speed: {cpu.MaxSpeedMHz?.ToString() ?? "N/A"} MHz <---, L2 Cache: {cpu.L2Cache ?? "N/A"}, L3 Cache: {cpu.L3Cache ?? "N/A"}");
                }
            } else { sb.AppendLine(" Processor(s) (CPU): Data unavailable."); }

            // Memory (Highlight total)
            if (data.Memory != null) {
                sb.AppendLine("\n Memory (RAM):");
                sb.AppendLine($"  Total Visible: {data.Memory.TotalVisible ?? "N/A"} ({data.Memory.TotalVisibleMemoryKB?.ToString("#,##0") ?? "?"} KB) <---, Available: {data.Memory.Available ?? "N/A"}, Used: {data.Memory.Used ?? "N/A"} ({data.Memory.PercentUsed:0.##}% Used)");
                 if(data.Memory.Modules?.Any() ?? false) {
                    sb.AppendLine("  Physical Modules:");
                    foreach(var mod in data.Memory.Modules) {
                        sb.AppendLine($"    - [{mod.DeviceLocator ?? "?"}] {mod.Capacity ?? "?"} @ {mod.SpeedMHz?.ToString() ?? "?"}MHz ({mod.MemoryType ?? "?"} / {mod.FormFactor ?? "?"}) Mfg: {mod.Manufacturer ?? "?"}, Part#: {mod.PartNumber ?? "?"}, Bank: {mod.BankLabel ?? "?"}");
                    }
                 } else { sb.AppendLine("  Physical Modules: Data unavailable or none found."); }
            } else { sb.AppendLine("\n Memory (RAM): Data unavailable."); }

            // Physical Disks (Highlight size)
            if (data.PhysicalDisks?.Any() ?? false) {
                sb.AppendLine("\n Physical Disks:");
                foreach(var disk in data.PhysicalDisks.OrderBy(d => d.Index)) {
                    // Add indicator if identified as System Disk (assuming collector/analysis populated it)
                    string systemDiskIndicator = disk.IsSystemDisk == true ? " (System Disk)" : "";
                    sb.AppendLine($"  Disk #{disk.Index}{systemDiskIndicator}: {disk.Model ?? "N/A"} ({disk.MediaType ?? "Unknown media"})");
                    sb.AppendLine($"    Interface: {disk.InterfaceType ?? "N/A"}, Size: {disk.Size ?? "N/A"} ({disk.SizeBytes?.ToString("#,##0") ?? "?"} Bytes) <---, Partitions: {disk.Partitions?.ToString() ?? "N/A"}, Serial: {disk.SerialNumber ?? "N/A"}, Status: {disk.Status ?? "N/A"}");
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
                 // Note about system disk mapping if not implemented
                 // sb.AppendLine("    (Note: System disk identification may be approximate without full partition mapping)");
            } else { sb.AppendLine("\n Physical Disks: Data unavailable or none found."); }

            // Logical Disks (Highlight size)
            if (data.LogicalDisks?.Any() ?? false) {
                sb.AppendLine("\n Logical Disks (Local Fixed):");
                 foreach(var disk in data.LogicalDisks.OrderBy(d => d.DeviceID)) {
                    // ** CS1061 FIX: Removed access to data.OperatingSystem here **
                    // The system drive identification logic needs to be in the SystemInfo renderer or the Analysis engine.
                    // For now, just render the disk info without the indicator.
                    // string systemDiskIndicator = data.OperatingSystem?.SystemDrive == disk.DeviceID ? " (System Drive)" : ""; // <-- Removed this line
                    sb.AppendLine($"  {disk.DeviceID ?? "?"} ({disk.VolumeName ?? "N/A"}) - {disk.FileSystem ?? "N/A"}"); // Render without indicator
                    sb.AppendLine($"    Size: {disk.Size ?? "N/A"} ({disk.SizeBytes?.ToString("#,##0") ?? "?"} Bytes), Free: {disk.FreeSpace ?? "N/A"} ({disk.PercentFree:0.#}% Free)");
                 }
            } else { sb.AppendLine("\n Logical Disks (Local Fixed): Data unavailable or none found."); }

            // Volumes (No changes needed)
             if (data.Volumes?.Any() ?? false) {
                sb.AppendLine("\n Volumes:");
                foreach(var vol in data.Volumes)
                {
                   sb.AppendLine($"  {vol.DriveLetter ?? "N/A"} ({vol.Name ?? "No Name"}) - {vol.FileSystem ?? "N/A"}");
                   sb.AppendLine($"    Capacity: {vol.Capacity ?? "N/A"}, Free: {vol.FreeSpace ?? "N/A"}");
                   sb.AppendLine($"    BitLocker Status: {vol.ProtectionStatus ?? "N/A"}");
                }
             }
             else { sb.AppendLine("\n Volumes: Data unavailable or none found."); }

             // GPU (Highlight resolution)
            if (data.Gpus?.Any() ?? false) {
                sb.AppendLine("\n Video Controllers (GPU):");
                 foreach(var gpu in data.Gpus) {
                    sb.AppendLine($"  - Name: {gpu.Name ?? "N/A"} (Status: {gpu.Status ?? "N/A"})");
                    sb.AppendLine($"    VRAM: {gpu.Vram ?? "N/A"}, Video Proc: {gpu.VideoProcessor ?? "N/A"}");
                    sb.AppendLine($"    Driver: {gpu.DriverVersion ?? "N/A"} (Date: {FormatNullableDateTime(gpu.DriverDate, "yyyy-MM-dd")})");
                    sb.AppendLine($"    Current Resolution: {gpu.CurrentResolution ?? "N/A"} <---");
                    // sb.AppendLine($"    WDDM Version: ? (Check dxdiag)"); // Note about WDDM
                 }
            } else { sb.AppendLine("\n Video Controllers (GPU): Data unavailable or none found."); }

            // Monitors (Highlight resolution)
            if (data.Monitors?.Any() ?? false) {
                sb.AppendLine("\n Monitors:");
                 foreach(var mon in data.Monitors) {
                    sb.AppendLine($"  - Name: {mon.Name ?? "N/A"} (ID: {mon.DeviceID ?? "N/A"})");
                    sb.AppendLine($"    Mfg: {mon.Manufacturer ?? "N/A"}, Res: {mon.ReportedResolution ?? "N/A"} <---, PPI: {mon.PpiLogical ?? "N/A"}");
                    // sb.AppendLine($"    Diagonal Size: ? (Manual Check Recommended)"); // Note about diagonal size
                 }
            } else { sb.AppendLine("\n Monitors: Data unavailable or none detected."); }

            // Audio (No changes needed)
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
             // Installed Applications (No changes needed)
             if (data.InstalledApplications?.Any() ?? false) {
                sb.AppendLine(" Installed Applications:");
                sb.AppendLine($"  Count: {data.InstalledApplications.Count}");
                 int count = 0;
                 foreach(var app in data.InstalledApplications) {
                     sb.AppendLine($"  - {app.Name ?? "N/A"} (Version: {app.Version ?? "N/A"}, Publisher: {app.Publisher ?? "N/A"}, Installed: {FormatNullableDateTime(app.InstallDate, "yyyy-MM-dd")})");
                     count++; if (count >= 50 && data.InstalledApplications.Count > 50) { sb.AppendLine("    (Showing first 50 applications)"); break; }
                 }
             }
             else { sb.AppendLine(" Installed Applications: Data unavailable or none found."); }

             // Windows Updates (No changes needed)
            if (data.WindowsUpdates?.Any() ?? false) {
                sb.AppendLine("\n Installed Windows Updates (Hotfixes):");
                foreach(var upd in data.WindowsUpdates) {
                     sb.AppendLine($"  - {upd.HotFixID ?? "N/A"} ({upd.Description ?? "N/A"}) - Installed: {FormatNullableDateTime(upd.InstalledOn)}");
                }
            }
            else { sb.AppendLine("\n Installed Windows Updates (Hotfixes): Data unavailable or none found."); }

            // Services (No changes needed)
             if (data.RelevantServices?.Any() ?? false) {
                sb.AppendLine("\n Relevant Services (Running, Critical, or Non-Microsoft):");
                foreach(var svc in data.RelevantServices) {
                    sb.AppendLine($"  - {svc.DisplayName ?? svc.Name ?? "N/A"} ({svc.Name ?? "N/A"})");
                    sb.AppendLine($"    State: {svc.State ?? "N/A"}, StartMode: {svc.StartMode ?? "N/A"}, Status: {svc.Status ?? "N/A"}");
                    sb.AppendLine($"    Path: {svc.PathName ?? "N/A"}");
                }
             }
             else { sb.AppendLine("\n Relevant Services: Data unavailable or none found."); }

            // Startup Programs (No changes needed)
            if (data.StartupPrograms?.Any() ?? false) {
                sb.AppendLine("\n Startup Programs:");
                foreach(var prog in data.StartupPrograms) {
                    sb.AppendLine($"  - [{prog.Location ?? "N/A"}] {prog.Name ?? "N/A"} = {prog.Command ?? "N/A"}");
                }
            }
            else { sb.AppendLine("\n Startup Programs: Data unavailable or none found."); }

            // Environment Variables (No changes needed)
            Action<string, Dictionary<string, string>?> renderEnvVars = (title, vars) => {
                sb.AppendLine($"\n {title} Environment Variables:");
                if (vars?.Any() ?? false) {
                    int count = 0;
                    foreach (var kvp in vars) {
                        sb.AppendLine($"  {kvp.Key}={kvp.Value}");
                        count++;
                        if (count >= 20 && vars.Count > 20) { sb.AppendLine("    (Showing first 20 variables)"); break; }
                    }
                } else { sb.AppendLine("  Data unavailable or none found."); }
            };
            renderEnvVars("System", data.SystemEnvironmentVariables);
            renderEnvVars("User", data.UserEnvironmentVariables);
        }

        // *** UPDATED Security Renderer ***
        private static void RenderSecurityInfo(StringBuilder sb, SecurityInfo data)
        {
             sb.AppendLine($" Running as Administrator: {data.IsAdmin}");
             sb.AppendLine($" UAC Status: {data.UacStatus ?? "N/A"}");
             sb.AppendLine($" Antivirus: {data.Antivirus?.Name ?? "N/A"} (State: {data.Antivirus?.State ?? "Requires Admin or Not Found"})");
             sb.AppendLine($" Firewall: {data.Firewall?.Name ?? "N/A"} (State: {data.Firewall?.State ?? "Requires Admin or Not Found"})");
             // NEW: Add Secure Boot and BIOS Mode
             sb.AppendLine($" Secure Boot Enabled: {data.IsSecureBootEnabled?.ToString() ?? "Unknown/Error"} <---");
             sb.AppendLine($" BIOS Mode (Inferred): {data.BiosMode ?? "Unknown/Error"} <---");

             // NEW: Add TPM Info
             if (data.Tpm != null)
             {
                sb.AppendLine("\n TPM (Trusted Platform Module):");
                sb.AppendLine($"  Present: {data.Tpm.IsPresent?.ToString() ?? "Unknown"}");
                if (data.Tpm.IsPresent == true) {
                     sb.AppendLine($"  Enabled: {data.Tpm.IsEnabled?.ToString() ?? "Unknown"}");
                     sb.AppendLine($"  Activated: {data.Tpm.IsActivated?.ToString() ?? "Unknown"}");
                     sb.AppendLine($"  Spec Version: {data.Tpm.SpecVersion ?? "N/A"} <---"); // Key for Win11
                     sb.AppendLine($"  Manufacturer: {data.Tpm.ManufacturerIdTxt ?? "N/A"} (Version: {data.Tpm.ManufacturerVersion ?? "N/A"})");
                     sb.AppendLine($"  Status: {data.Tpm.Status ?? "N/A"}");
                }
                if (!string.IsNullOrEmpty(data.Tpm.ErrorMessage)) sb.AppendLine($"  Error: {data.Tpm.ErrorMessage}");
             } else { sb.AppendLine("\n TPM (Trusted Platform Module): Data unavailable."); }

             // Local Users (No changes needed)
             if(data.LocalUsers?.Any() ?? false) {
                sb.AppendLine("\n Local User Accounts:");
                foreach(var user in data.LocalUsers) {
                     sb.AppendLine($"  - {user.Name ?? "N/A"} (SID: {user.SID ?? "N/A"})");
                     sb.AppendLine($"    Disabled: {user.IsDisabled}, PwdRequired: {user.PasswordRequired}, PwdChangeable: {user.PasswordChangeable}, IsLocal: {user.IsLocal}");
                }
             }
             else { sb.AppendLine("\n Local User Accounts: Data unavailable or none found."); }

             // Local Groups (No changes needed)
             if(data.LocalGroups?.Any() ?? false) {
                sb.AppendLine("\n Local Groups:");
                int count = 0;
                foreach(var grp in data.LocalGroups) {
                     sb.AppendLine($"  - {grp.Name ?? "N/A"} (SID: {grp.SID ?? "N/A"}) - {grp.Description ?? "N/A"}");
                     count++; if (count >= 15 && data.LocalGroups.Count > 15) { sb.AppendLine("    (Showing first 15 groups)"); break; }
                }
             }
             else { sb.AppendLine("\n Local Groups: Data unavailable or none found."); }

            // Network Shares (No changes needed)
            if(data.NetworkShares?.Any() ?? false) {
                sb.AppendLine("\n Network Shares:");
                foreach(var share in data.NetworkShares) {
                    sb.AppendLine($"  - {share.Name ?? "N/A"} -> {share.Path ?? "N/A"} ({share.Description ?? "N/A"}, Type: {share.Type?.ToString() ?? "?"})");
                }
            }
            else { sb.AppendLine("\n Network Shares: Data unavailable or none found."); }
        }

        private static void RenderPerformanceInfo(StringBuilder sb, PerformanceInfo data)
        {
            // No changes needed here unless adding more detailed counters
            sb.AppendLine(" Performance Counters (Snapshot/Sampled):");
            sb.AppendLine($"  Overall CPU Usage: {data.OverallCpuUsagePercent ?? "N/A"} %");
            sb.AppendLine($"  Available Memory: {data.AvailableMemoryMB ?? "N/A"} MB");
            sb.AppendLine($"  Avg. Disk Queue Length (Total): {data.TotalDiskQueueLength ?? "N/A"}");

             Action<string, List<ProcessUsageInfo>?> renderProcs = (title, procs) => {
                 sb.AppendLine($"\n Top Processes by {title}:");
                 if (procs?.Any() ?? false) {
                     foreach(var p in procs) { sb.AppendLine($"  - PID: {p.Pid}, Name: {p.Name ?? "N/A"}, Memory: {p.MemoryUsage ?? "N/A"}, Status: {p.Status ?? "N/A"}{(string.IsNullOrEmpty(p.Error) ? "" : $" (Error: {p.Error})")}"); }
                 } else if (!string.IsNullOrEmpty(data.SectionCollectionErrorMessage)) { sb.AppendLine($"  Data unavailable due to section error: {data.SectionCollectionErrorMessage}"); }
                 else { sb.AppendLine("  Data unavailable or none found."); }
             };
             renderProcs("Memory (Working Set)", data.TopMemoryProcesses);
             renderProcs("Total CPU Time (Snapshot)", data.TopCpuProcesses);
        }

        private static void RenderNetworkInfo(StringBuilder sb, NetworkInfo data)
        {
             // No changes needed here
            if (data.Adapters?.Any() ?? false) {
                sb.AppendLine(" Network Adapters:");
                foreach(var adapter in data.Adapters) {
                    sb.AppendLine($"  {adapter.Name} ({adapter.Description})");
                    sb.AppendLine($"    ID: {adapter.Id}");
                    sb.AppendLine($"    Type: {adapter.Type}, Status: {adapter.Status}, Speed: {adapter.SpeedMbps} Mbps, IsReceiveOnly: {adapter.IsReceiveOnly}");
                    sb.AppendLine($"    MAC: {adapter.MacAddress ?? "N/A"}");
                    sb.AppendLine($"    IP Addresses: {FormatList(adapter.IpAddresses)}");
                    sb.AppendLine($"    Gateways: {FormatList(adapter.Gateways)}");
                    sb.AppendLine($"    DNS Servers: {FormatList(adapter.DnsServers)}");
                    sb.AppendLine($"    DHCP Enabled: {adapter.DhcpEnabled} (Lease Obtained: {FormatNullableDateTime(adapter.DhcpLeaseObtained)}, Expires: {FormatNullableDateTime(adapter.DhcpLeaseExpires)})");
                     sb.AppendLine($"    WMI Service Name: {adapter.WmiServiceName ?? "N/A"}");
                }
            }
            else { sb.AppendLine(" Network Adapters: No adapters found or data unavailable."); }

             Action<string, List<ActivePortInfo>?> renderListeners = (title, listeners) => {
                 sb.AppendLine($"\n Active {title} Listeners:");
                 if (listeners?.Any() ?? false) {
                     foreach (var l in listeners) { sb.AppendLine($"  - Local: {l.LocalAddress}:{l.LocalPort}, PID: {l.OwningPid?.ToString() ?? "?"} ({l.OwningProcessName ?? "N/A"})"); }
                 } else { sb.AppendLine("  Data unavailable or none found."); }
             };
             renderListeners("TCP", data.ActiveTcpListeners);
             renderListeners("UDP", data.ActiveUdpListeners);

            if (data.ConnectivityTests != null) {
                sb.AppendLine("\n Connectivity Tests:");
                RenderPingResult(sb, data.ConnectivityTests.GatewayPing, "Default Gateway");
                if (data.ConnectivityTests.DnsPings?.Any() ?? false) { foreach(var ping in data.ConnectivityTests.DnsPings) RenderPingResult(sb, ping); }
                else { sb.AppendLine("  DNS Ping Tests: Not performed or data unavailable."); }
                if (data.ConnectivityTests.TracerouteResults?.Any() ?? false) {
                    sb.AppendLine($"\n  Traceroute to {data.ConnectivityTests.TracerouteTarget ?? "Unknown Target"}:");
                    sb.AppendLine("    Hop  RTT (ms)  Address           Status");
                    sb.AppendLine("    ---  --------  ----------------- -----------------");
                    foreach (var hop in data.ConnectivityTests.TracerouteResults) { sb.AppendLine($"    {hop.Hop,3}  {hop.RoundtripTimeMs?.ToString() ?? "*",8}  {hop.Address ?? "*",-17} {hop.Status ?? "N/A"}"); }
                } else if (!string.IsNullOrEmpty(data.ConnectivityTests.TracerouteTarget)) { sb.AppendLine("\n  Traceroute: Not performed or failed."); }
            }
            else { sb.AppendLine("\n Connectivity Tests: Data unavailable."); }
        }
        private static void RenderPingResult(StringBuilder sb, PingResult? result, string? defaultTargetName = null)
        {
             if(result == null) { sb.AppendLine($"  Ping Test ({defaultTargetName ?? "Unknown Target"}): Data unavailable."); return; }
             string target = string.IsNullOrEmpty(result.Target) ? defaultTargetName ?? "Unknown Target" : result.Target;
             sb.Append($"  Ping Test ({target}): Status = {result.Status ?? "N/A"}");
             if (result.Status == IPStatus.Success.ToString()) { sb.Append($" ({result.RoundtripTimeMs ?? 0} ms)"); }
             if (!string.IsNullOrEmpty(result.Error)) { sb.Append($" (Error: {result.Error})"); }
             sb.AppendLine();
        }


        private static void RenderEventLogInfo(StringBuilder sb, EventLogInfo data)
        {
             // No changes needed here
             Action<string, List<EventEntry>?> renderLog = (logName, entries) => {
                 sb.AppendLine($"\n {logName} Event Log (Recent Errors/Warnings - Max 20):");
                 if (entries?.Any() ?? false) {
                    int count = 0;
                    // Handle collector messages
                    if (entries.Count == 1 && entries[0].Source == null) { sb.AppendLine($"  [{entries[0].Message}]"); return; }

                    var actualEntries = entries.Where(e => e.Source != null).ToList();
                    if(!actualEntries.Any()) {
                        // Check if the only messages were collector errors
                         if (entries.Count > 0 && entries.All(e => e.Source == null)) {
                              foreach(var entry in entries) sb.AppendLine($"  [{entry.Message}]");
                         } else {
                              sb.AppendLine($"  No recent Error/Warning entries found.");
                         }
                         return;
                    }

                    foreach (var entry in actualEntries) {
                         string msg = entry.Message?.Length > 150 ? entry.Message.Substring(0, 147) + "..." : entry.Message ?? "";
                         sb.AppendLine($"  - {FormatNullableDateTime(entry.TimeGenerated)} [{entry.EntryType}] {entry.Source ?? "?"} (ID:{entry.InstanceId}) - {msg}");
                         count++; if (count >= 20) break;
                    }
                 } else if (!string.IsNullOrEmpty(data.SectionCollectionErrorMessage)) { sb.AppendLine($"  Data unavailable due to section error: {data.SectionCollectionErrorMessage}"); }
                 else { sb.AppendLine("  Data unavailable or none found."); }
             };
            renderLog("System", data.SystemLogEntries);
            sb.AppendLine();
            renderLog("Application", data.ApplicationLogEntries);
        }


        private static void RenderAnalysisSummary(StringBuilder sb, AnalysisSummary data)
        {
             if (data == null) { sb.AppendLine("Analysis data unavailable or analysis did not run."); return; }
             bool anyContent = false;

             if (!string.IsNullOrEmpty(data.SectionCollectionErrorMessage))
             { sb.AppendLine($"[ANALYSIS ERROR: {data.SectionCollectionErrorMessage}]"); anyContent = true; }

            // NEW: Render structured Windows 11 Readiness results if present
            if (data.Windows11Readiness?.Checks?.Any() ?? false)
            {
                anyContent = true;
                sb.AppendLine(">>> Windows 11 Readiness Check:");
                string overallStatus = data.Windows11Readiness.OverallResult switch {
                    true => "PASS", false => "FAIL", _ => "INCOMPLETE" };
                sb.AppendLine($"  Overall Status: {overallStatus}");
                sb.AppendLine("  Component        | Status         | Details");
                sb.AppendLine("  --------------- | -------------- | -----------------------------------");
                foreach(var check in data.Windows11Readiness.Checks.OrderBy(c => c.ComponentChecked))
                {
                    sb.AppendLine($"  {check.ComponentChecked?.PadRight(15) ?? "General".PadRight(15)} | {check.Status?.PadRight(14) ?? "Unknown".PadRight(14)} | {check.Details ?? ""}");
                }
                sb.AppendLine();
            }


            // Render existing issues/suggestions/info
            if (data.PotentialIssues?.Any() ?? false) { anyContent = true; sb.AppendLine(">>> Potential Issues Found:"); foreach (var issue in data.PotentialIssues) sb.AppendLine($"  - [ISSUE] {issue}"); sb.AppendLine(); }
            if (data.Suggestions?.Any() ?? false) { anyContent = true; sb.AppendLine(">>> Suggestions:"); foreach (var suggestion in data.Suggestions) sb.AppendLine($"  - [SUGGESTION] {suggestion}"); sb.AppendLine(); }
            if (data.Info?.Any() ?? false) { anyContent = true; sb.AppendLine(">>> Informational Notes:"); foreach (var info in data.Info) sb.AppendLine($"  - [INFO] {info}"); sb.AppendLine(); }

            if (!anyContent) { sb.AppendLine("No specific issues, suggestions, or notes generated by the analysis based on collected data."); }
        }
        #endregion

        #region Formatting Helpers
        private static string FormatNullableDateTime(DateTime? dt, string format = "g")
        { return dt.HasValue ? dt.Value.ToString(format) : "N/A"; }

        private static string FormatList(List<string>? list)
        { if (list == null || !list.Any()) return "N/A"; return string.Join(", ", list); }
        #endregion
    }
}