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

                // Display overall section error first if it exists
                if (!string.IsNullOrEmpty(data.SectionCollectionErrorMessage))
                {
                    sb.AppendLine($"[CRITICAL ERROR collecting {title}: {data.SectionCollectionErrorMessage}]");
                    // Optionally skip rendering content if a critical error occurred
                    // sb.AppendLine(); return;
                }

                // Display specific errors collected within the section
                if (data.SpecificCollectionErrors?.Any() ?? false)
                {
                    sb.AppendLine($"[{title} - Specific Collection Errors/Warnings]:");
                    foreach(var kvp in data.SpecificCollectionErrors)
                    {
                        sb.AppendLine($"  - {kvp.Key}: {kvp.Value}");
                    }
                    sb.AppendLine(); // Add a blank line after specific errors
                }


                // Attempt to render the section's content
                try
                {
                    renderAction(data);
                }
                catch (Exception ex) { sb.AppendLine($"[ERROR rendering section '{title}': {ex.Message}]"); }
                sb.AppendLine();
            };

            // --- Append Sections ---
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
                sb.AppendLine($"  Version: {data.OperatingSystem.Version ?? "N/A"} (Build: {data.OperatingSystem.BuildNumber ?? "N/A"})");
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
                sb.AppendLine($"  Serial Number: {data.Baseboard.SerialNumber ?? "N/A"}"); // Already handles admin check in collector
                sb.AppendLine($"  Version: {data.Baseboard.Version ?? "N/A"}");
            } else { sb.AppendLine("\n Baseboard (Motherboard): Data unavailable."); }

            // BIOS Info
            if (data.BIOS != null)
            {
                 sb.AppendLine("\n BIOS:");
                 sb.AppendLine($"  Manufacturer: {data.BIOS.Manufacturer ?? "N/A"}");
                 sb.AppendLine($"  Version: {data.BIOS.Version ?? "N/A"}");
                 sb.AppendLine($"  Release Date: {FormatNullableDateTime(data.BIOS.ReleaseDate, "yyyy-MM-dd")}");
                 sb.AppendLine($"  Serial Number: {data.BIOS.SerialNumber ?? "N/A"}"); // Already handles admin check
            } else { sb.AppendLine("\n BIOS: Data unavailable."); }

            // TimeZone Info
            if (data.TimeZone != null)
            {
                sb.AppendLine("\n Time Zone:");
                sb.AppendLine($"  Caption: {data.TimeZone.CurrentTimeZone ?? "N/A"}");
                // sb.AppendLine($"  Standard Name: {data.TimeZone.StandardName ?? "N/A"}"); // Can be verbose
                // sb.AppendLine($"  Daylight Name: {data.TimeZone.DaylightName ?? "N/A"}");
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
                sb.AppendLine($"  Total Visible: {data.Memory.TotalVisible ?? "N/A"}, Available: {data.Memory.Available ?? "N/A"}, Used: {data.Memory.Used ?? "N/A"} ({data.Memory.PercentUsed:0.##}% Used)");
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
                    sb.AppendLine($"  Disk #{disk.Index}: {disk.Model ?? "N/A"} ({disk.MediaType ?? "Unknown media"})");
                    sb.AppendLine($"    Interface: {disk.InterfaceType ?? "N/A"}, Size: {disk.Size ?? "N/A"}, Partitions: {disk.Partitions?.ToString() ?? "N/A"}, Serial: {disk.SerialNumber ?? "N/A"}, Status: {disk.Status ?? "N/A"}");
                     // Display SMART Status
                     string smartDisplay = "N/A";
                     if (disk.SmartStatus != null) {
                         smartDisplay = $"{disk.SmartStatus.StatusText ?? "Unknown"}";
                         if (!string.IsNullOrEmpty(disk.SmartStatus.ReasonCode) && disk.SmartStatus.ReasonCode != "0") smartDisplay += $" (Reason Code: {disk.SmartStatus.ReasonCode})";
                         if (!string.IsNullOrEmpty(disk.SmartStatus.Error)) smartDisplay += $" [Error: {disk.SmartStatus.Error}]";
                         // Include basic status if SMART wasn't explicitly OK
                         if(disk.SmartStatus.StatusText != "OK" && !string.IsNullOrEmpty(disk.SmartStatus.BasicStatusFromDiskDrive) && disk.SmartStatus.BasicStatusFromDiskDrive != disk.Status) {
                            smartDisplay += $" (Basic HW Status: {disk.SmartStatus.BasicStatusFromDiskDrive})";
                         }
                     }
                    sb.AppendLine($"    SMART Status: {smartDisplay}");
                }
            } else { sb.AppendLine("\n Physical Disks: Data unavailable or none found."); }

            // Logical Disks
            if (data.LogicalDisks?.Any() ?? false) {
                sb.AppendLine("\n Logical Disks (Local Fixed):");
                 foreach(var disk in data.LogicalDisks.OrderBy(d => d.DeviceID)) {
                    sb.AppendLine($"  {disk.DeviceID ?? "?"} ({disk.VolumeName ?? "N/A"}) - {disk.FileSystem ?? "N/A"}");
                    sb.AppendLine($"    Size: {disk.Size ?? "N/A"}, Free: {disk.FreeSpace ?? "N/A"} ({disk.PercentFree:0.#}% Free)");
                 }
            } else { sb.AppendLine("\n Logical Disks (Local Fixed): Data unavailable or none found."); }

            // Volumes
             if (data.Volumes?.Any() ?? false) {
                sb.AppendLine("\n Volumes:");
                 foreach(var vol in data.Volumes.OrderBy(v => v.DriveLetter)) {
                    sb.Append($"  {vol.DriveLetter ?? "N/A"}: {vol.Name ?? "N/A"} ({vol.FileSystem ?? "?"}) Cap: {vol.Capacity ?? "?"}, Free: {vol.FreeSpace ?? "?"}");
                     // Only show BitLocker if status is not "Requires Admin" or "Invalid Volume ID" etc.
                     if (vol.ProtectionStatus != null && !vol.ProtectionStatus.StartsWith("Requires") && !vol.ProtectionStatus.StartsWith("Invalid") && !vol.ProtectionStatus.StartsWith("Error")) {
                         sb.Append($" BitLocker: {vol.ProtectionStatus}");
                     } else if (!string.IsNullOrEmpty(vol.ProtectionStatus)) {
                         sb.Append($" BitLocker: ({vol.ProtectionStatus})"); // Show error/status in parentheses
                     }
                     sb.AppendLine();
                 }
            } else { sb.AppendLine("\n Volumes: Data unavailable or none found."); }

             // GPU
            if (data.Gpus?.Any() ?? false) {
                sb.AppendLine("\n Video Controllers (GPU):");
                 foreach(var gpu in data.Gpus) {
                    sb.AppendLine($"  - Name: {gpu.Name ?? "N/A"} (Status: {gpu.Status ?? "N/A"})");
                    sb.AppendLine($"    VRAM: {gpu.Vram ?? "N/A"}, Video Proc: {gpu.VideoProcessor ?? "N/A"}");
                    sb.AppendLine($"    Driver: {gpu.DriverVersion ?? "N/A"} (Date: {FormatNullableDateTime(gpu.DriverDate, "yyyy-MM-dd")})");
                    sb.AppendLine($"    Resolution: {gpu.CurrentResolution ?? "N/A"}");
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
                    sb.AppendLine($"  - Product: {audio.ProductName ?? audio.Name ?? "N/A"} (Status: {audio.Status ?? "N/A"})");
                    sb.AppendLine($"    Manufacturer: {audio.Manufacturer ?? "N/A"}");
                 }
            } else { sb.AppendLine("\n Audio Devices: Data unavailable or none found."); }
        }

        private static void RenderSoftwareInfo(StringBuilder sb, SoftwareInfo data)
        {
             // Installed Applications
             if (data.InstalledApplications?.Any() ?? false) {
                 sb.AppendLine($" Installed Applications (from Registry - Count: {data.InstalledApplications.Count}):");
                 // Maybe limit display? For now, show all.
                 foreach(var app in data.InstalledApplications.OrderBy(a => a.Name)) {
                      sb.Append($"  - {app.Name ?? "N/A"} (Ver: {app.Version ?? "N/A"})");
                      if (!string.IsNullOrEmpty(app.Publisher)) sb.Append($" Publisher: {app.Publisher}");
                      if (app.InstallDate.HasValue) sb.Append($" Date: {app.InstallDate:yyyy-MM-dd}");
                      sb.AppendLine();
                 }
             } else { sb.AppendLine(" Installed Applications: Data unavailable or none found."); }

             // Windows Updates
            if (data.WindowsUpdates?.Any() ?? false) {
                sb.AppendLine("\n Installed Windows Updates (Hotfixes):");
                foreach(var upd in data.WindowsUpdates.OrderBy(u => u.HotFixID)) {
                     sb.AppendLine($"  - {upd.HotFixID ?? "N/A"} ({upd.Description ?? "N/A"}) Installed: {FormatNullableDateTime(upd.InstalledOn)}");
                }
            } else { sb.AppendLine("\n Installed Windows Updates (Hotfixes): Data unavailable or none found."); }

            // Services
             if (data.RelevantServices?.Any() ?? false) {
                sb.AppendLine("\n Relevant Services (Critical or Non-Stopped):");
                sb.AppendLine("  Name                 | State      | StartMode  | Path (Requires Admin)");
                sb.AppendLine("  -------------------- | ---------- | ---------- | ---------------------");
                 foreach(var svc in data.RelevantServices.OrderBy(s=>s.Name)) {
                    sb.AppendLine($"  {svc.Name?.PadRight(20) ?? "N/A".PadRight(20)} | {svc.State?.PadRight(10) ?? "N/A".PadRight(10)} | {svc.StartMode?.PadRight(10) ?? "N/A".PadRight(10)} | {svc.PathName ?? "N/A"}");
                 }
             } else { sb.AppendLine("\n Relevant Services: Data unavailable or none found."); }

            // Startup Programs
            if (data.StartupPrograms?.Any() ?? false) {
                sb.AppendLine("\n Startup Programs (Common Locations):");
                foreach(var prog in data.StartupPrograms.OrderBy(s => s.Location).ThenBy(s => s.Name)) {
                    sb.AppendLine($"  - [{prog.Location}] '{prog.Name}' = {prog.Command}");
                }
            } else { sb.AppendLine("\n Startup Programs: Data unavailable or none found."); }

            // Environment Variables
            Action<string, Dictionary<string, string>?> renderEnvVars = (title, vars) => {
                if (vars != null) {
                    sb.AppendLine($"\n Environment Variables ({title}):");
                     foreach(var kvp in vars.OrderBy(k => k.Key)) {
                        sb.AppendLine($"  - {kvp.Key}={kvp.Value}");
                     }
                } else { sb.AppendLine($"\n Environment Variables ({title}): Data unavailable."); }
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

             // Local Users
             if(data.LocalUsers?.Any() ?? false) {
                sb.AppendLine("\n Local User Accounts:");
                foreach(var user in data.LocalUsers.OrderBy(u => u.Name)) {
                    string details = $"[Disabled: {user.IsDisabled}, PwdReq: {user.PasswordRequired}, PwdChangeable: {user.PasswordChangeable}]";
                    sb.AppendLine($"  - {user.Name ?? "N/A"} (SID: {user.SID ?? "N/A"}) {details}");
                }
             } else { sb.AppendLine("\n Local User Accounts: Data unavailable or none found."); }

             // Local Groups
             if(data.LocalGroups?.Any() ?? false) {
                 sb.AppendLine("\n Local Groups:");
                 foreach(var grp in data.LocalGroups.OrderBy(g => g.Name)) {
                    sb.AppendLine($"  - {grp.Name ?? "N/A"} (SID: {grp.SID ?? "N/A"}) Desc: {grp.Description ?? "N/A"}");
                 }
             } else { sb.AppendLine("\n Local Groups: Data unavailable or none found."); }

            // Network Shares
            if(data.NetworkShares?.Any() ?? false) {
                sb.AppendLine("\n Network Shares:");
                foreach(var share in data.NetworkShares.OrderBy(s => s.Name)) {
                    sb.AppendLine($"  - \\\\{Environment.MachineName}\\{share.Name} -> {share.Path ?? "N/A"} (Type: {share.Type ?? 0}, Desc: {share.Description ?? "N/A"})");
                }
            } else { sb.AppendLine("\n Network Shares: Data unavailable or none found."); }
        }
        private static void RenderPerformanceInfo(StringBuilder sb, PerformanceInfo data)
        {
            sb.AppendLine(" Performance Counters (Snapshot/Sampled):");
            sb.AppendLine($"  Overall CPU Usage: {data.OverallCpuUsagePercent ?? "N/A"} %");
            sb.AppendLine($"  Available Memory: {data.AvailableMemoryMB ?? "N/A"} MB");
            sb.AppendLine($"  Avg. Disk Queue Length (Total): {data.TotalDiskQueueLength ?? "N/A"}");

             Action<string, List<ProcessUsageInfo>?> renderProcs = (title, procs) => {
                if (procs?.Any() ?? false) {
                    sb.AppendLine($"\n Top Processes by {title}:");
                    sb.AppendLine("  PID   | Memory    | Status          | Name / Error");
                    sb.AppendLine("  ----- | --------- | --------------- | --------------------");
                    foreach(var p in procs) { // Already sorted by collector
                         string mem = string.IsNullOrEmpty(p.Error) ? (p.MemoryUsage ?? "?").PadLeft(9) : "N/A".PadLeft(9);
                         string status = (p.Status ?? "?").PadRight(15);
                        sb.AppendLine($"  {p.Pid,5} | {mem} | {status} | {p.Name ?? p.Error ?? "?"}");
                    }
                } else { sb.AppendLine($"\n Top Processes by {title}: Data unavailable or none found."); }
             };

             renderProcs("Memory (Working Set)", data.TopMemoryProcesses);
             renderProcs("Total CPU Time (Snapshot)", data.TopCpuProcesses); // Note: Still snapshot CPU Time
        }


        // *** UPDATED Network Renderer ***
        private static void RenderNetworkInfo(StringBuilder sb, NetworkInfo data)
        {
            // NOTE: Specific errors/warnings are now displayed by the main AppendSection logic

            // --- Render Adapters ---
            if (data.Adapters?.Any() ?? false)
            {
                sb.AppendLine(" Network Adapters:");
                foreach (var nic in data.Adapters.OrderBy(n => n.Status != OperationalStatus.Up).ThenBy(n => n.Name))
                {
                    sb.AppendLine($"\n  Interface: {nic.Name} ({nic.Description ?? "N/A"})");
                    sb.AppendLine($"    Type: {nic.Type}, Status: {nic.Status}, Speed: {(nic.SpeedMbps < 0 ? "N/A" : nic.SpeedMbps + " Mbps")}");
                    sb.AppendLine($"    MAC: {nic.MacAddress ?? "N/A"}");
                    sb.AppendLine($"    IP Addresses: {FormatList(nic.IpAddresses)}");
                    sb.AppendLine($"    Gateways: {FormatList(nic.Gateways)}");
                    sb.AppendLine($"    DNS Servers: {FormatList(nic.DnsServers)}");
                    string dhcpLeaseObt = FormatNullableDateTime(nic.DhcpLeaseObtained, "g");
                    string dhcpLeaseExp = FormatNullableDateTime(nic.DhcpLeaseExpires, "g");
                    sb.AppendLine($"    DHCP Enabled: {nic.DhcpEnabled} (Lease Obtained: {dhcpLeaseObt}, Expires: {dhcpLeaseExp})");
                }
            } else { sb.AppendLine(" Network Adapters: No adapters found or data unavailable."); }

            // --- Render Listeners ---
            string listenerHeader = "  Local Address:Port         | PID   | Process Name / Error";
            string listenerSeparator= "  -------------------------- | ----- | --------------------";

             Action<string, List<ActivePortInfo>?> renderListeners = (title, listeners) => {
                if (listeners != null) // Check if list exists
                {
                    var validListeners = listeners.Where(p => string.IsNullOrEmpty(p.Error)).ToList();
                    var errorListeners = listeners.Where(p => !string.IsNullOrEmpty(p.Error)).ToList(); // Example: PID lookup error

                    if (validListeners.Any() || errorListeners.Any())
                    {
                        sb.AppendLine($"\n Active {title} Listeners:");
                        sb.AppendLine(listenerHeader); sb.AppendLine(listenerSeparator);
                        foreach (var port in validListeners.OrderBy(p => p.LocalPort))
                        {
                             string localEp = ($"{port.LocalAddress}:{port.LocalPort}").PadRight(26);
                             // PID/Process Name requires Admin/PInvoke - show placeholder if null
                             string pidPad = (port.OwningPid?.ToString() ?? "-").PadLeft(5);
                             string procName = port.OwningProcessName ?? "N/A (Lookup requires Admin/Advanced API)";
                            sb.AppendLine($"  {localEp} | {pidPad} | {procName}");
                        }
                         // Optionally list errors if needed:
                         // foreach(var errPort in errorListeners) { sb.AppendLine($"  Error getting info for port {errPort.LocalPort}: {errPort.Error}"); }
                    } else { sb.AppendLine($"\n Active {title} Listeners: None found."); }
                } else { sb.AppendLine($"\n Active {title} Listeners: Data unavailable (collection might have failed)."); }
             };

             renderListeners("TCP", data.ActiveTcpListeners);
             renderListeners("UDP", data.ActiveUdpListeners);


            // --- Render Connectivity Tests ---
            if (data.ConnectivityTests != null)
            {
                sb.AppendLine("\n Connectivity Tests:");
                RenderPingResult(sb, data.ConnectivityTests.GatewayPing, "Default Gateway");
                if (data.ConnectivityTests.DnsPings != null)
                {
                    foreach (var ping in data.ConnectivityTests.DnsPings.OrderBy(p => p.Target)) RenderPingResult(sb, ping);
                } else { sb.AppendLine("  DNS Pings: Data unavailable."); }

                if (data.ConnectivityTests.TracerouteResults != null)
                {
                    sb.AppendLine($"\n Traceroute to {data.ConnectivityTests.TracerouteTarget ?? "?"}:");
                    sb.AppendLine("  Hop | Time (ms) | Address                         | Status");
                    sb.AppendLine("  --- | --------- | ------------------------------- | ------");
                    foreach (var hop in data.ConnectivityTests.TracerouteResults.OrderBy(h => h.Hop))
                    {
                         string timePad = (hop.RoundtripTimeMs?.ToString() ?? "*").PadLeft(7);
                         string addrPad = (hop.Address ?? "*").PadRight(31);
                        sb.AppendLine($"  {hop.Hop,3} | {timePad} | {addrPad} | {hop.Status ?? "?"}");
                    }
                     if (data.ConnectivityTests.TracerouteResults.All(h => h.Status == IPStatus.TimedOut.ToString())) { sb.AppendLine("  *** All hops timed out. Check firewall or network path. ***"); }
                     else if (data.ConnectivityTests.TracerouteResults.LastOrDefault()?.Status != IPStatus.Success.ToString()) { sb.AppendLine("  *** Trace did not reach the target successfully. ***"); }
                }
            } else { sb.AppendLine("\n Connectivity Tests: Data unavailable."); }
        }
        private static void RenderPingResult(StringBuilder sb, PingResult? result, string? defaultTargetName = null)
        {
             if (result == null) { sb.AppendLine($"  - Ping {defaultTargetName ?? "Unknown"}: Result unavailable."); return; };
            string target = result.Target ?? defaultTargetName ?? "Unknown";
            sb.Append($"  - Ping {target,-15}: Status={result.Status ?? "N/A"}");
            if (result.Status == IPStatus.Success.ToString() && result.RoundtripTimeMs.HasValue)
                sb.Append($" (Time={result.RoundtripTimeMs}ms)");
            if (!string.IsNullOrEmpty(result.Error)) sb.Append($" [Error: {result.Error}]");
            sb.AppendLine();
        }


        private static void RenderEventLogInfo(StringBuilder sb, EventLogInfo data)
        {
             Action<string, List<EventEntry>?> renderLog = (logName, entries) =>
            {
                sb.AppendLine($" {logName} Log (Recent Errors/Warnings):");
                if (entries == null) { sb.AppendLine("    Data unavailable."); return; }
                if (!entries.Any()) { sb.AppendLine("    No entries collected."); return; }

                // Check for placeholder/error messages generated by the collector itself
                if (entries.Count == 1 && entries[0].Source == null && entries[0].InstanceId == 0) {
                    sb.AppendLine($"    {entries[0].Message}");
                    return;
                }

                int count = 0; int displayLimit = 20; // Limit displayed entries
                var actualEntries = entries.Where(e => e.Source != null).ToList(); // Filter out collector messages

                if (!actualEntries.Any()) { sb.AppendLine("    No actual Error/Warning entries found in collected data."); return; }

                foreach (var entry in actualEntries.Take(displayLimit))
                {
                    sb.AppendLine($"    ------------------------------------");
                    sb.AppendLine($"    Time: {entry.TimeGenerated:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine($"    Type: {entry.EntryType}, Source: {entry.Source}, Event ID: {entry.InstanceId}");
                    string msg = entry.Message ?? ""; msg = msg.Replace("\r", "").Replace("\n", " ").Trim();
                    if (msg.Length > 250) msg = msg.Substring(0, 247) + "...";
                    sb.AppendLine($"    Message: {msg}");
                    count++;
                }
                if (count == 0) sb.AppendLine("    No recent Error/Warning entries found matching criteria."); // Should not happen if actualEntries.Any() is true
                else if (actualEntries.Count > displayLimit) sb.AppendLine($"    ... (limited to first {displayLimit} entries)");
            };
            renderLog("System", data.SystemLogEntries);
            sb.AppendLine();
            renderLog("Application", data.ApplicationLogEntries);
        }


        private static void RenderAnalysisSummary(StringBuilder sb, AnalysisSummary data)
        {
             if (data == null) { sb.AppendLine("Analysis data unavailable or analysis did not run."); return; }
             bool anyContent = false;

             // Display analysis errors first
             if (!string.IsNullOrEmpty(data.SectionCollectionErrorMessage))
             {
                 sb.AppendLine($"[ANALYSIS ERROR: {data.SectionCollectionErrorMessage}]");
                 anyContent = true;
             }

            if (data.PotentialIssues?.Any() ?? false) { anyContent = true; sb.AppendLine(">>> Potential Issues Found:"); foreach (var issue in data.PotentialIssues) sb.AppendLine($"  - [ISSUE] {issue}"); sb.AppendLine(); }
            if (data.Suggestions?.Any() ?? false) { anyContent = true; sb.AppendLine(">>> Suggestions:"); foreach (var suggestion in data.Suggestions) sb.AppendLine($"  - [SUGGESTION] {suggestion}"); sb.AppendLine(); }
            if (data.Info?.Any() ?? false) { anyContent = true; sb.AppendLine(">>> Informational Notes:"); foreach (var info in data.Info) sb.AppendLine($"  - [INFO] {info}"); sb.AppendLine(); }

            if (!anyContent) { sb.AppendLine("No specific issues, suggestions, or notes generated by the analysis based on collected data."); }
        }
        #endregion

        #region Formatting Helpers
        private static string FormatNullableDateTime(DateTime? dt, string format = "g")
        {
            return dt.HasValue ? dt.Value.ToString(format) : "N/A";
        }

        private static string FormatList(List<string>? list)
        {
            if (list == null || !list.Any()) return string.Empty;
            return string.Join(", ", list);
        }
        #endregion
    }
}