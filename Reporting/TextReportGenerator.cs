// Reporting/TextReportGenerator.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation; // Required for IPStatus, OperationalStatus, etc.
using System.Text;
using DiagnosticToolAllInOne.Analysis; // For AnalysisSummary and AppConfiguration
using DiagnosticToolAllInOne.Helpers; // Added to access FormatHelper

namespace DiagnosticToolAllInOne.Reporting
{
    public static class TextReportGenerator
    {
        private const string Separator = "----------------------------------------";
        private const string NotAvailable = "N/A"; // Constant for N/A

        public static string GenerateReport(DiagnosticReport report)
        {
            var sb = new StringBuilder();

            // --- Report Header ---
            sb.AppendLine("========================================");
            sb.AppendLine("   Advanced Windows Diagnostic Tool Report");
            sb.AppendLine("========================================");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (Local Time) / {report.ReportTimestamp:u} (UTC)");
            sb.AppendLine($"Ran as Administrator: {report.RanAsAdmin}");
            if (report.Configuration != null)
            {
                string configFilePath = Path.Combine(AppContext.BaseDirectory, "web/appsettings.json"); // Adjusted path
                // Check if the config file actually exists (more reliable than just checking if Configuration object exists)
                sb.AppendLine($"Configuration Source: {(File.Exists(configFilePath) ? "appsettings.json" : "Defaults")}");
            }
            else
            {
                sb.AppendLine($"Configuration Source: Defaults"); // Indicate defaults if Configuration object is null
            }
            sb.AppendLine(Separator).AppendLine(); // Use AppendLine() for clarity


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
                    // Continue rendering specific errors even if critical error occurred at section level
                }

                // Display specific errors/warnings collected within the section
                if (data.SpecificCollectionErrors?.Any() ?? false)
                {
                    sb.AppendLine($"[{title} - Collection Warnings/Errors]:");
                    foreach (var kvp in data.SpecificCollectionErrors.OrderBy(e => e.Key)) // Sort errors by key
                    {
                        sb.AppendLine($"  - {kvp.Key}: {kvp.Value}");
                    }
                    sb.AppendLine(); // Add space after errors
                }

                // Render the actual data using the provided action, only if no critical section error stopped collection early
                if (string.IsNullOrEmpty(data.SectionCollectionErrorMessage) || !data.SectionCollectionErrorMessage.StartsWith("Critical failure")) // Render data unless a critical setup error occurred
                {
                    try { renderAction(data); }
                    catch (Exception ex)
                    {
                        // Log the full exception details internally if possible
                        Logger.LogError($"Error rendering section '{title}' content", ex);
                        sb.AppendLine($"[ERROR rendering section '{title}': {ex.Message}]");
                    }
                }
                else
                {
                    sb.AppendLine($"[Skipping content rendering for {title} due to critical collection error.]");
                }
                sb.AppendLine(); // Add blank line after each section
            };

            // --- Define Section Rendering Order ---
            // Define order explicitly
            var sectionsToRender = new List<(string Title, DiagnosticSection? Data, Action<object> RenderFunc)>
             {
                ("Analysis Summary", report.Analysis, data => RenderAnalysisSummary(sb, (AnalysisSummary)data, report.Configuration)),
                ("System Stability", report.Stability, data => RenderStabilityInfo(sb, (StabilityInfo)data)),
                ("System Information", report.System, data => RenderSystemInfo(sb, (SystemInfo)data)),
                ("Hardware Information", report.Hardware, data => RenderHardwareInfo(sb, (HardwareInfo)data)),
                ("Performance Snapshot", report.Performance, data => RenderPerformanceInfo(sb, (PerformanceInfo)data)),
                ("Network Information", report.Network, data => RenderNetworkInfo(sb, (NetworkInfo)data)),
                ("Security Information", report.Security, data => RenderSecurityInfo(sb, (SecurityInfo)data)),
                ("Software & Configuration", report.Software, data => RenderSoftwareInfo(sb, (SoftwareInfo)data)),
                ("Recent Event Logs", report.Events, data => RenderEventLogInfo(sb, (EventLogInfo)data))
             };

            // Append sections in the defined order
            foreach (var section in sectionsToRender)
            {
                AppendSection(section.Title, section.Data, section.RenderFunc);
            }


            return sb.ToString();
        }

        #region Render Methods (Updated for Refined Data Models)

        // --- UPDATED RenderSystemInfo ---
        private static void RenderSystemInfo(StringBuilder sb, SystemInfo data)
        {
            // OS Info
            if (data.OperatingSystem != null)
            {
                var os = data.OperatingSystem;
                sb.AppendLine(" Operating System:");
                sb.AppendLine($"  Name: {os.Name ?? NotAvailable} ({os.Architecture ?? NotAvailable})");
                sb.AppendLine($"  Version: {os.Version ?? NotAvailable} (Build: {os.BuildNumber ?? NotAvailable})");
                sb.AppendLine($"  Install Date: {FormatNullableDateTime(os.InstallDate, "yyyy-MM-dd HH:mm:ss")}");
                sb.AppendLine($"  Last Boot Time: {FormatNullableDateTime(os.LastBootTime, "yyyy-MM-dd HH:mm:ss")}");
                if (os.Uptime.HasValue) { var ts = os.Uptime.Value; sb.AppendLine($"  System Uptime: {ts.Days}d {ts.Hours}h {ts.Minutes}m {ts.Seconds}s"); }
                else { sb.AppendLine($"  System Uptime: {NotAvailable}"); }
                sb.AppendLine($"  System Drive: {os.SystemDrive ?? NotAvailable}");
            }
            else { sb.AppendLine(" Operating System: Data unavailable."); }

            // Computer System Info
            if (data.ComputerSystem != null)
            {
                var cs = data.ComputerSystem;
                sb.AppendLine("\n Computer System:");
                sb.AppendLine($"  Manufacturer: {cs.Manufacturer ?? NotAvailable}");
                sb.AppendLine($"  Model: {cs.Model ?? NotAvailable} ({cs.SystemType ?? NotAvailable})");
                sb.AppendLine($"  Domain/Workgroup: {cs.DomainOrWorkgroup ?? NotAvailable} (PartOfDomain: {cs.PartOfDomain})");
                sb.AppendLine($"  Executing User: {cs.CurrentUser ?? NotAvailable}");
                sb.AppendLine($"  Logged In User (WMI): {cs.LoggedInUserWMI ?? NotAvailable}");
            }
            else { sb.AppendLine("\n Computer System: Data unavailable."); }

            // Baseboard Info
            if (data.Baseboard != null)
            {
                var bb = data.Baseboard;
                sb.AppendLine("\n Baseboard (Motherboard):");
                sb.AppendLine($"  Manufacturer: {bb.Manufacturer ?? NotAvailable}");
                sb.AppendLine($"  Product: {bb.Product ?? NotAvailable}");
                sb.AppendLine($"  Serial Number: {bb.SerialNumber ?? NotAvailable}");
                sb.AppendLine($"  Version: {bb.Version ?? NotAvailable}");
            }
            else { sb.AppendLine("\n Baseboard (Motherboard): Data unavailable."); }

            // BIOS Info
            if (data.BIOS != null)
            {
                var bios = data.BIOS;
                sb.AppendLine("\n BIOS:");
                sb.AppendLine($"  Manufacturer: {bios.Manufacturer ?? NotAvailable}");
                sb.AppendLine($"  Version: {bios.Version ?? NotAvailable}");
                sb.AppendLine($"  Release Date: {FormatNullableDateTime(bios.ReleaseDate, "yyyy-MM-dd")}");
                sb.AppendLine($"  Serial Number: {bios.SerialNumber ?? NotAvailable}");
            }
            else { sb.AppendLine("\n BIOS: Data unavailable."); }

            // TimeZone Info
            if (data.TimeZone != null)
            {
                var tz = data.TimeZone;
                sb.AppendLine("\n Time Zone:");
                sb.AppendLine($"  Caption: {tz.CurrentTimeZone ?? NotAvailable}");
                sb.AppendLine($"  Standard Name: {tz.StandardName ?? NotAvailable}");
                sb.AppendLine($"  Daylight Name: {tz.DaylightName ?? NotAvailable}");
                sb.AppendLine($"  Bias (UTC Offset Mins): {tz.BiasMinutes?.ToString() ?? NotAvailable}");
            }
            else { sb.AppendLine("\n Time Zone: Data unavailable."); }

            // Power Plan
            if (data.ActivePowerPlan != null) { sb.AppendLine($"\n Active Power Plan: {data.ActivePowerPlan.Name ?? NotAvailable} ({data.ActivePowerPlan.InstanceID ?? ""})"); }
            else { sb.AppendLine("\n Active Power Plan: Data unavailable."); }

            // --- System Integrity (Updated Rendering) ---
            sb.AppendLine("\n System Integrity Check Info:");
            if (data.SystemIntegrity != null)
            {
                var si = data.SystemIntegrity;
                if (!string.IsNullOrEmpty(si.LogParsingError))
                {
                    sb.AppendLine($"  Error Checking Logs: {si.LogParsingError}");
                }

                sb.AppendLine($"  SFC Log Status: {(si.SfcLogFound == true ? "Found" : "Not Found/Access Denied")}");
                if (si.SfcLogFound == true)
                {
                    sb.AppendLine($"    Last Scan Time (UTC): {FormatNullableDateTime(si.LastSfcScanTime)}");
                    sb.AppendLine($"    Scan Result: {si.SfcScanResult ?? "Unknown"}");
                    sb.AppendLine($"    Corruption Found: {si.SfcCorruptionFound?.ToString() ?? "Unknown"}");
                    if (si.SfcCorruptionFound == true)
                    {
                        sb.AppendLine($"    Repairs Successful: {si.SfcRepairsSuccessful?.ToString() ?? "Unknown"}");
                    }
                }

                sb.AppendLine($"  DISM Log Status: {(si.DismLogFound == true ? "Found" : "Not Found/Access Denied")}");
                if (si.DismLogFound == true)
                {
                    sb.AppendLine($"    Last Check Time (UTC): {FormatNullableDateTime(si.LastDismCheckTime)}");
                    sb.AppendLine($"    Check Result: {si.DismCheckHealthResult ?? "Unknown"}");
                    sb.AppendLine($"    Corruption Detected: {si.DismCorruptionDetected?.ToString() ?? "Unknown"}");
                    if (si.DismCorruptionDetected == true)
                    {
                        sb.AppendLine($"    Store Repairable: {si.DismStoreRepairable?.ToString() ?? "Unknown"}");
                    }
                }
            }
            else
            {
                sb.AppendLine("  Data unavailable or collection failed.");
            }
            // --- End System Integrity Update ---

            // Pending Reboot
            sb.AppendLine($"\n Reboot Pending: {data.IsRebootPending?.ToString() ?? NotAvailable}");

            // .NET Version
            sb.AppendLine($"\n .NET Runtime (Executing): {data.DotNetVersion ?? NotAvailable}");
        }

        private static void RenderHardwareInfo(StringBuilder sb, HardwareInfo data)
        {
            // Processor
            if (data.Processors?.Any() ?? false)
            {
                sb.AppendLine(" Processor(s) (CPU):");
                foreach (var cpu in data.Processors)
                {
                    string l2Formatted = cpu.L2CacheSizeKB.HasValue ? FormatHelper.FormatBytes(cpu.L2CacheSizeKB.Value * 1024) : NotAvailable;
                    string l3Formatted = cpu.L3CacheSizeKB.HasValue ? FormatHelper.FormatBytes(cpu.L3CacheSizeKB.Value * 1024) : NotAvailable;
                    sb.AppendLine($"  - Name: {cpu.Name ?? NotAvailable}");
                    sb.AppendLine($"    Socket: {cpu.Socket ?? NotAvailable}, Cores: {cpu.Cores?.ToString() ?? NotAvailable}, Logical Processors: {cpu.LogicalProcessors?.ToString() ?? NotAvailable}");
                    sb.AppendLine($"    Max Speed: {cpu.MaxSpeedMHz?.ToString() ?? NotAvailable} MHz, L2 Cache: {l2Formatted}, L3 Cache: {l3Formatted}");
                }
            }
            else { sb.AppendLine(" Processor(s) (CPU): Data unavailable."); }

            // Memory
            if (data.Memory != null)
            {
                var mem = data.Memory;
                sb.AppendLine("\n Memory (RAM):");
                string totalFormatted = mem.TotalVisibleMemoryKB.HasValue ? FormatHelper.FormatBytes(mem.TotalVisibleMemoryKB.Value * 1024) : NotAvailable;
                string availableFormatted = mem.AvailableMemoryKB.HasValue ? FormatHelper.FormatBytes(mem.AvailableMemoryKB.Value * 1024) : NotAvailable;
                string usedFormatted = NotAvailable;
                double? percentUsed = mem.PercentUsed;

                if (mem.TotalVisibleMemoryKB.HasValue && mem.AvailableMemoryKB.HasValue && mem.TotalVisibleMemoryKB.Value > 0)
                {
                    ulong usedKB = mem.TotalVisibleMemoryKB.Value > mem.AvailableMemoryKB.Value ? mem.TotalVisibleMemoryKB.Value - mem.AvailableMemoryKB.Value : 0;
                    usedFormatted = FormatHelper.FormatBytes(usedKB * 1024);
                }

                sb.AppendLine($"  Total Visible: {totalFormatted} ({mem.TotalVisibleMemoryKB?.ToString("#,##0") ?? "?"} KB)");
                sb.AppendLine($"  Available: {availableFormatted} ({mem.AvailableMemoryKB?.ToString("#,##0") ?? "?"} KB)");
                sb.AppendLine($"  Used: {usedFormatted} ({percentUsed?.ToString("0.##") ?? "?"}%)");

                if (mem.Modules?.Any() ?? false)
                {
                    sb.AppendLine("  Physical Modules:");
                    foreach (var mod in mem.Modules)
                    {
                        string capacityFormatted = mod.CapacityBytes.HasValue ? FormatHelper.FormatBytes(mod.CapacityBytes.Value) : NotAvailable;
                        sb.AppendLine($"    - [{mod.DeviceLocator ?? "?"}] {capacityFormatted} @ {mod.SpeedMHz?.ToString() ?? "?"}MHz ({mod.MemoryType ?? "?"} / {mod.FormFactor ?? "?"}) Mfg: {mod.Manufacturer ?? "?"}, Part#: {mod.PartNumber ?? "?"}, Bank: {mod.BankLabel ?? "?"}");
                    }
                }
                else { sb.AppendLine("  Physical Modules: Data unavailable or none found."); }
            }
            else { sb.AppendLine("\n Memory (RAM): Data unavailable."); }

            // Physical Disks
            if (data.PhysicalDisks?.Any() ?? false)
            {
                sb.AppendLine("\n Physical Disks:");
                foreach (var disk in data.PhysicalDisks.OrderBy(d => d.Index))
                {
                    string systemDiskIndicator = disk.IsSystemDisk == true ? " (System Disk)" : "";
                    string sizeFormatted = disk.SizeBytes.HasValue ? FormatHelper.FormatBytes(disk.SizeBytes.Value) : NotAvailable;
                    sb.AppendLine($"  Disk #{disk.Index}{systemDiskIndicator}: {disk.Model ?? NotAvailable} ({disk.MediaType ?? "Unknown media"})");
                    sb.AppendLine($"    Interface: {disk.InterfaceType ?? NotAvailable}, Size: {sizeFormatted} ({disk.SizeBytes?.ToString("#,##0") ?? "?"} Bytes), Partitions: {disk.Partitions?.ToString() ?? NotAvailable}, Serial: {disk.SerialNumber ?? NotAvailable}, Status: {disk.Status ?? NotAvailable}");
                    string smartDisplay = NotAvailable;
                    if (disk.SmartStatus != null)
                    {
                        smartDisplay = $"{disk.SmartStatus.StatusText ?? "Unknown"}";
                        if (disk.SmartStatus.IsFailurePredicted) smartDisplay = $"!!! FAILURE PREDICTED !!! (Reason: {disk.SmartStatus.ReasonCode ?? NotAvailable})";
                        else if (disk.SmartStatus.Error != null) smartDisplay += $" (Error: {disk.SmartStatus.Error})";
                        if (disk.SmartStatus.StatusText?.ToUpperInvariant() != "OK" &&
                            !string.IsNullOrEmpty(disk.SmartStatus.BasicStatusFromDiskDrive) &&
                            disk.SmartStatus.BasicStatusFromDiskDrive.ToUpperInvariant() != "OK" &&
                            disk.SmartStatus.BasicStatusFromDiskDrive.ToUpperInvariant() != NotAvailable)
                        {
                            smartDisplay += $" (Basic HW Status: {disk.SmartStatus.BasicStatusFromDiskDrive})";
                        }
                    }
                    sb.AppendLine($"    SMART Status: {smartDisplay}");
                }
            }
            else { sb.AppendLine("\n Physical Disks: Data unavailable or none found."); }

            // Logical Disks
            if (data.LogicalDisks?.Any() ?? false)
            {
                sb.AppendLine("\n Logical Disks (Local Fixed):");
                foreach (var disk in data.LogicalDisks.OrderBy(d => d.DeviceID))
                {
                    string sizeFormatted = disk.SizeBytes.HasValue ? FormatHelper.FormatBytes(disk.SizeBytes.Value) : NotAvailable;
                    string freeFormatted = disk.FreeSpaceBytes.HasValue ? FormatHelper.FormatBytes(disk.FreeSpaceBytes.Value) : NotAvailable;
                    sb.AppendLine($"  {disk.DeviceID ?? "?"} ({disk.VolumeName ?? NotAvailable}) - {disk.FileSystem ?? NotAvailable}");
                    sb.AppendLine($"    Size: {sizeFormatted} ({disk.SizeBytes?.ToString("#,##0") ?? "?"} Bytes), Free: {freeFormatted} ({disk.PercentFree?.ToString("0.#") ?? "?"}% Free)");
                }
            }
            else { sb.AppendLine("\n Logical Disks (Local Fixed): Data unavailable or none found."); }

            // Volumes
            if (data.Volumes?.Any() ?? false)
            {
                sb.AppendLine("\n Volumes:");
                foreach (var vol in data.Volumes.OrderBy(v => v.DriveLetter))
                {
                    string capacityFormatted = vol.CapacityBytes.HasValue ? FormatHelper.FormatBytes(vol.CapacityBytes.Value) : NotAvailable;
                    string freeFormatted = vol.FreeSpaceBytes.HasValue ? FormatHelper.FormatBytes(vol.FreeSpaceBytes.Value) : NotAvailable;
                    sb.AppendLine($"  {vol.DriveLetter ?? NotAvailable} ({vol.Name ?? "No Name"}) - {vol.FileSystem ?? NotAvailable}");
                    sb.AppendLine($"    Capacity: {capacityFormatted}, Free: {freeFormatted}");
                    sb.AppendLine($"    Device ID: {vol.DeviceID ?? NotAvailable}");
                    sb.AppendLine($"    BitLocker Status: {vol.ProtectionStatus ?? NotAvailable}");
                }
            }
            else { sb.AppendLine("\n Volumes: Data unavailable or none found."); }

            // GPU
            if (data.Gpus?.Any() ?? false)
            {
                sb.AppendLine("\n Video Controllers (GPU):");
                foreach (var gpu in data.Gpus)
                {
                    string vramFormatted = gpu.AdapterRAMBytes.HasValue ? FormatHelper.FormatBytes(gpu.AdapterRAMBytes.Value) : NotAvailable;
                    string currentResFormatted = (gpu.CurrentHorizontalResolution.HasValue && gpu.CurrentVerticalResolution.HasValue)
                        ? $"{gpu.CurrentHorizontalResolution.Value}x{gpu.CurrentVerticalResolution.Value}" + (gpu.CurrentRefreshRate.HasValue ? $" @ {gpu.CurrentRefreshRate.Value} Hz" : "")
                        : NotAvailable;
                    sb.AppendLine($"  - Name: {gpu.Name ?? NotAvailable} (Status: {gpu.Status ?? NotAvailable})");
                    sb.AppendLine($"    VRAM: {vramFormatted}, Video Proc: {gpu.VideoProcessor ?? NotAvailable}");
                    sb.AppendLine($"    Driver: {gpu.DriverVersion ?? NotAvailable} (Date: {FormatNullableDateTime(gpu.DriverDate, "yyyy-MM-dd")})");
                    sb.AppendLine($"    Current Resolution: {currentResFormatted}");
                    sb.AppendLine($"    WDDM Version: {gpu.WddmVersion ?? NotAvailable}");
                }
            }
            else { sb.AppendLine("\n Video Controllers (GPU): Data unavailable or none found."); }

            // Monitors
            if (data.Monitors?.Any() ?? false)
            {
                sb.AppendLine("\n Monitors:");
                foreach (var mon in data.Monitors)
                {
                    string reportedResFormatted = (mon.ScreenWidth.HasValue && mon.ScreenHeight.HasValue) ? $"{mon.ScreenWidth.Value}x{mon.ScreenHeight.Value}" : NotAvailable;
                    string ppiFormatted = (mon.PixelsPerXLogicalInch.HasValue && mon.PixelsPerYLogicalInch.HasValue) ? $"{mon.PixelsPerXLogicalInch.Value}x{mon.PixelsPerYLogicalInch.Value}" : NotAvailable;
                    string diagonalFormatted = mon.DiagonalSizeInches.HasValue ? $"{mon.DiagonalSizeInches.Value:0.#} inches" : NotAvailable;
                    sb.AppendLine($"  - Name: {mon.Name ?? NotAvailable} (ID: {mon.DeviceID ?? NotAvailable})");
                    sb.AppendLine($"    Mfg: {mon.Manufacturer ?? NotAvailable}, Resolution: {reportedResFormatted}, PPI: {ppiFormatted}, Diagonal: {diagonalFormatted}");
                    sb.AppendLine($"    PnP Device ID: {mon.PnpDeviceID ?? NotAvailable}");
                }
            }
            else { sb.AppendLine("\n Monitors: Data unavailable or none detected."); }

            // Audio
            if (data.AudioDevices?.Any() ?? false)
            {
                sb.AppendLine("\n Audio Devices:");
                foreach (var audio in data.AudioDevices)
                {
                    sb.AppendLine($"  - {audio.Name ?? NotAvailable} (Product: {audio.ProductName ?? NotAvailable}, Mfg: {audio.Manufacturer ?? NotAvailable}, Status: {audio.Status ?? NotAvailable})");
                }
            }
            else { sb.AppendLine("\n Audio Devices: Data unavailable or none found."); }
        }

        private static void RenderSoftwareInfo(StringBuilder sb, SoftwareInfo data)
        {
            // Installed Applications
            if (data.InstalledApplications?.Any() ?? false)
            {
                sb.AppendLine(" Installed Applications:");
                sb.AppendLine($"  Count: {data.InstalledApplications.Count}");
                int count = 0;
                foreach (var app in data.InstalledApplications.OrderBy(a => a.Name))
                {
                    sb.AppendLine($"  - {app.Name ?? NotAvailable} (Version: {app.Version ?? NotAvailable}, Publisher: {app.Publisher ?? NotAvailable}, Installed: {FormatNullableDateTime(app.InstallDate, "yyyy-MM-dd")})");
                    count++;
                    if (count >= 100 && data.InstalledApplications.Count > 100) { sb.AppendLine($"    (... and {data.InstalledApplications.Count - count} more)"); break; }
                }
            }
            else { sb.AppendLine(" Installed Applications: Data unavailable or none found."); }

            // Windows Updates
            if (data.WindowsUpdates?.Any() ?? false)
            {
                sb.AppendLine("\n Installed Windows Updates (Hotfixes):");
                foreach (var upd in data.WindowsUpdates.OrderByDescending(u => u.InstalledOn ?? DateTime.MinValue))
                {
                    sb.AppendLine($"  - {upd.HotFixID ?? NotAvailable} ({upd.Description ?? NotAvailable}) - Installed: {FormatNullableDateTime(upd.InstalledOn)}");
                }
            }
            else { sb.AppendLine("\n Installed Windows Updates (Hotfixes): Data unavailable or none found."); }

            // Services
            if (data.RelevantServices?.Any() ?? false)
            {
                sb.AppendLine("\n Relevant Services (Running, Critical, or Non-Microsoft):");
                foreach (var svc in data.RelevantServices.OrderBy(s => s.DisplayName ?? s.Name))
                {
                    sb.AppendLine($"  - {svc.DisplayName ?? svc.Name ?? NotAvailable} ({svc.Name ?? NotAvailable})");
                    sb.AppendLine($"    State: {svc.State ?? NotAvailable}, StartMode: {svc.StartMode ?? NotAvailable}, Status: {svc.Status ?? NotAvailable}");
                    sb.AppendLine($"    Path: {svc.PathName ?? NotAvailable}");
                }
            }
            else { sb.AppendLine("\n Relevant Services: Data unavailable or none found."); }

            // Startup Programs
            if (data.StartupPrograms?.Any() ?? false)
            {
                sb.AppendLine("\n Startup Programs:");
                foreach (var prog in data.StartupPrograms.OrderBy(p => p.Location).ThenBy(p => p.Name))
                {
                    sb.AppendLine($"  - [{prog.Location ?? NotAvailable}] {prog.Name ?? NotAvailable} = {prog.Command ?? NotAvailable}");
                }
            }
            else { sb.AppendLine("\n Startup Programs: Data unavailable or none found."); }

            // Environment Variables
            Action<string, Dictionary<string, string>?> renderEnvVars = (title, vars) =>
            {
                sb.AppendLine($"\n {title} Environment Variables:");
                if (vars?.Any() ?? false)
                {
                    int count = 0;
                    foreach (var kvp in vars.OrderBy(v => v.Key))
                    {
                        sb.AppendLine($"  {kvp.Key}={kvp.Value}"); // Value is already string
                        count++;
                        if (count >= 30 && vars.Count > 30) { sb.AppendLine($"    (... and {vars.Count - count} more)"); break; }
                    }
                }
                else { sb.AppendLine("  Data unavailable or none found."); }
            };
            renderEnvVars("System", data.SystemEnvironmentVariables);
            renderEnvVars("User", data.UserEnvironmentVariables);
        }

        private static void RenderSecurityInfo(StringBuilder sb, SecurityInfo data)
        {
            sb.AppendLine($" Running as Administrator: {data.IsAdmin}");
            sb.AppendLine($" UAC Status: {data.UacStatus ?? NotAvailable}");
            sb.AppendLine($" Antivirus: {data.Antivirus?.Name ?? NotAvailable} (State: {data.Antivirus?.State ?? "Requires Admin or Not Found"})");
            sb.AppendLine($" Firewall: {data.Firewall?.Name ?? NotAvailable} (State: {data.Firewall?.State ?? "Requires Admin or Not Found"})");
            sb.AppendLine($" Secure Boot Enabled: {data.IsSecureBootEnabled?.ToString() ?? "Unknown/Error"}");
            sb.AppendLine($" BIOS Mode (Inferred): {data.BiosMode ?? "Unknown/Error"}");

            if (data.Tpm != null)
            {
                var tpm = data.Tpm;
                sb.AppendLine("\n TPM (Trusted Platform Module):");
                sb.AppendLine($"  Present: {tpm.IsPresent?.ToString() ?? "Unknown"}");
                if (tpm.IsPresent == true)
                {
                    sb.AppendLine($"  Enabled: {tpm.IsEnabled?.ToString() ?? "Unknown"}");
                    sb.AppendLine($"  Activated: {tpm.IsActivated?.ToString() ?? "Unknown"}");
                    sb.AppendLine($"  Spec Version: {tpm.SpecVersion ?? NotAvailable}");
                    sb.AppendLine($"  Manufacturer: {tpm.ManufacturerIdTxt ?? NotAvailable} (Version: {tpm.ManufacturerVersion ?? NotAvailable})");
                    sb.AppendLine($"  Status Summary: {tpm.Status ?? NotAvailable}");
                }
                if (!string.IsNullOrEmpty(tpm.ErrorMessage)) sb.AppendLine($"  Error: {tpm.ErrorMessage}");
            }
            else { sb.AppendLine("\n TPM (Trusted Platform Module): Data unavailable."); }

            if (data.LocalUsers?.Any() ?? false)
            {
                sb.AppendLine("\n Local User Accounts:");
                foreach (var user in data.LocalUsers.OrderBy(u => u.Name))
                {
                    sb.AppendLine($"  - {user.Name ?? NotAvailable} (SID: {user.SID ?? NotAvailable})");
                    sb.AppendLine($"    Disabled: {user.IsDisabled}, PwdRequired: {user.PasswordRequired}, PwdChangeable: {user.PasswordChangeable}, IsLocal: {user.IsLocal}");
                }
            }
            else { sb.AppendLine("\n Local User Accounts: Data unavailable or none found."); }

            if (data.LocalGroups?.Any() ?? false)
            {
                sb.AppendLine("\n Local Groups:");
                int count = 0;
                foreach (var grp in data.LocalGroups.OrderBy(g => g.Name))
                {
                    sb.AppendLine($"  - {grp.Name ?? NotAvailable} (SID: {grp.SID ?? NotAvailable}) - {grp.Description ?? NotAvailable}");
                    count++; if (count >= 20 && data.LocalGroups.Count > 20) { sb.AppendLine($"    (... and {data.LocalGroups.Count - count} more)"); break; }
                }
            }
            else { sb.AppendLine("\n Local Groups: Data unavailable or none found."); }

            if (data.NetworkShares?.Any() ?? false)
            {
                sb.AppendLine("\n Network Shares:");
                foreach (var share in data.NetworkShares.OrderBy(s => s.Name))
                {
                    sb.AppendLine($"  - {share.Name ?? NotAvailable} -> {share.Path ?? NotAvailable} ({share.Description ?? NotAvailable}, Type: {share.Type?.ToString() ?? "?"})");
                }
            }
            else { sb.AppendLine("\n Network Shares: Data unavailable or none found."); }
        }


        private static void RenderPerformanceInfo(StringBuilder sb, PerformanceInfo data)
        {
            sb.AppendLine(" Performance Counters (Snapshot/Sampled):");
            sb.AppendLine($"  Overall CPU Usage: {data.OverallCpuUsagePercent ?? NotAvailable} %");
            sb.AppendLine($"  Available Memory: {data.AvailableMemoryMB ?? NotAvailable} MB");
            sb.AppendLine($"  Avg. Disk Queue Length (Total): {data.TotalDiskQueueLength ?? NotAvailable}");

            Action<string, List<ProcessUsageInfo>?, bool> renderProcs = (title, procs, isCpuTable) =>
            {
                sb.AppendLine($"\n Top Processes by {title}:");
                if (procs?.Any() ?? false)
                {
                    sb.AppendLine("  PID    Name                            " + (isCpuTable ? "CPU Time (ms) " : "Memory Usage   "));
                    sb.AppendLine("  ------ ------------------------------- ------------");
                    foreach (var p in procs)
                    { // Already sorted by collector
                        string name = p.Name ?? NotAvailable;
                        if (name.Length > 30) name = name.Substring(0, 27) + "...";

                        string metricValue = NotAvailable;
                        if (isCpuTable)
                        {
                            metricValue = p.TotalProcessorTimeMs?.ToString("#,##0") ?? NotAvailable;
                        }
                        else
                        {
                            metricValue = p.WorkingSetBytes.HasValue
                                ? FormatHelper.FormatBytes((ulong)p.WorkingSetBytes.Value)
                                : NotAvailable;
                        }

                        sb.Append($"  {p.Pid,-6} {name,-31} {metricValue,-12}");
                        if (p.Status != "Running") sb.Append($" ({p.Status})");
                        if (!string.IsNullOrEmpty(p.Error)) sb.Append($" [Error: {p.Error}]");
                        sb.AppendLine();
                    }
                }
                else if (!string.IsNullOrEmpty(data.SectionCollectionErrorMessage)) { sb.AppendLine($"  Data unavailable due to section error: {data.SectionCollectionErrorMessage}"); }
                else { sb.AppendLine("  Data unavailable or none found."); }
            };
            renderProcs("Memory (Working Set)", data.TopMemoryProcesses, false);
            renderProcs("Total CPU Time", data.TopCpuProcesses, true);
        }

        private static void RenderNetworkInfo(StringBuilder sb, NetworkInfo data)
        {
            if (data.Adapters?.Any() ?? false)
            {
                sb.AppendLine(" Network Adapters:");
                foreach (var adapter in data.Adapters.OrderBy(a => a.Description ?? a.Name))
                {
                    sb.AppendLine($"\n  {adapter.Name} ({adapter.Description})");
                    sb.AppendLine($"    Status: {adapter.Status}, Type: {adapter.Type}, Speed: {(adapter.SpeedMbps >= 0 ? $"{adapter.SpeedMbps} Mbps" : NotAvailable)}, MAC: {adapter.MacAddress ?? NotAvailable}");
                    sb.AppendLine($"    ID: {adapter.Id}, Index: {adapter.InterfaceIndex?.ToString() ?? NotAvailable}");
                    sb.AppendLine($"    PnPDeviceID: {adapter.PnpDeviceID ?? NotAvailable}");
                    sb.AppendLine($"    IP Addresses: {FormatList(adapter.IpAddresses)}");
                    sb.AppendLine($"    Gateways: {FormatList(adapter.Gateways)}");
                    sb.AppendLine($"    DNS Servers: {FormatList(adapter.DnsServers)}");
                    sb.AppendLine($"    DNS Suffix: {adapter.DnsSuffix ?? NotAvailable}");
                    sb.AppendLine($"    WINS Servers: {FormatList(adapter.WinsServers)}");
                    sb.AppendLine($"    DHCP Enabled: {adapter.DhcpEnabled} (Lease Obtained: {FormatNullableDateTime(adapter.DhcpLeaseObtained)}, Expires: {FormatNullableDateTime(adapter.DhcpLeaseExpires)})");
                    sb.AppendLine($"    WMI Service Name: {adapter.WmiServiceName ?? NotAvailable}");
                    sb.AppendLine($"    Driver Date: {FormatNullableDateTime(adapter.DriverDate, "yyyy-MM-dd")}");
                }
            }
            else { sb.AppendLine(" Network Adapters: No adapters found or data unavailable."); }

            Action<string, List<ActivePortInfo>?> renderListeners = (title, listeners) =>
            {
                sb.AppendLine($"\n Active {title} Listeners:");
                if (listeners?.Any() ?? false)
                {
                    sb.AppendLine("  Local Address:Port       PID    Process Name");
                    sb.AppendLine("  ------------------------ ------ ------------------------------");
                    foreach (var l in listeners.OrderBy(p => p.LocalPort))
                    {
                        string localEndpoint = $"{l.LocalAddress}:{l.LocalPort}";
                        sb.AppendLine($"  {localEndpoint,-24} {l.OwningPid?.ToString() ?? "-",-6} {l.OwningProcessName ?? NotAvailable}");
                        if (!string.IsNullOrEmpty(l.Error)) sb.AppendLine($"      Error: {l.Error}");
                    }
                }
                else { sb.AppendLine("  Data unavailable or none found."); }
            };
            renderListeners("TCP", data.ActiveTcpListeners);
            renderListeners("UDP", data.ActiveUdpListeners);

            if (data.ActiveTcpConnections?.Any() ?? false)
            {
                sb.AppendLine("\n Active TCP Connections:");
                sb.AppendLine("  Local Address:Port       Remote Address:Port      State           PID    Process Name");
                sb.AppendLine("  ------------------------ ------------------------ --------------- ------ ------------------------------");
                foreach (var conn in data.ActiveTcpConnections.OrderBy(c => c.State).ThenBy(c => c.LocalPort))
                {
                    string localEp = $"{conn.LocalAddress}:{conn.LocalPort}";
                    string remoteEp = $"{conn.RemoteAddress}:{conn.RemotePort}";
                    sb.AppendLine($"  {localEp,-24} {remoteEp,-24} {conn.State,-15} {conn.OwningPid?.ToString() ?? "-",-6} {conn.OwningProcessName ?? NotAvailable}");
                    if (!string.IsNullOrEmpty(conn.Error)) sb.AppendLine($"      Error: {conn.Error}");
                }
            }
            else { sb.AppendLine("\n Active TCP Connections: Data unavailable or none found."); }


            if (data.ConnectivityTests != null)
            {
                sb.AppendLine("\n Connectivity Tests:");
                RenderPingResult(sb, data.ConnectivityTests.GatewayPing, "Default Gateway");
                if (data.ConnectivityTests.DnsPings?.Any() ?? false)
                {
                    foreach (var ping in data.ConnectivityTests.DnsPings) RenderPingResult(sb, ping);
                }
                else { sb.AppendLine("  DNS Server Ping Tests: Not performed or data unavailable."); }

                RenderDnsResolutionResult(sb, data.ConnectivityTests.DnsResolution);

                if (data.ConnectivityTests.TracerouteResults?.Any() ?? false)
                {
                    sb.AppendLine($"\n  Traceroute to {data.ConnectivityTests.TracerouteTarget ?? "Unknown Target"}:");
                    sb.AppendLine("    Hop  RTT (ms)  Address           Status");
                    sb.AppendLine("    ---  --------  ----------------- -----------------");
                    foreach (var hop in data.ConnectivityTests.TracerouteResults)
                    {
                        sb.AppendLine($"    {hop.Hop,3}  {(hop.RoundtripTimeMs?.ToString() ?? "*"),8}  {hop.Address ?? "*",-17} {hop.Status ?? NotAvailable} {(string.IsNullOrEmpty(hop.Error) ? "" : $"(Error: {hop.Error})")}");
                    }
                }
                else if (!string.IsNullOrEmpty(data.ConnectivityTests.TracerouteTarget)) { sb.AppendLine("\n  Traceroute: Not performed or failed."); }
            }
            else { sb.AppendLine("\n Connectivity Tests: Data unavailable."); }
        }

        private static void RenderPingResult(StringBuilder sb, PingResult? result, string? defaultTargetName = null)
        {
            if (result == null) { sb.AppendLine($"  Ping Test ({defaultTargetName ?? "Unknown Target"}): Data unavailable."); return; }
            string target = string.IsNullOrEmpty(result.Target) ? defaultTargetName ?? "Unknown Target" : result.Target;
            string resolvedIP = string.IsNullOrEmpty(result.ResolvedIpAddress) ? "" : $" [{result.ResolvedIpAddress}]";
            sb.Append($"  Ping Test ({target}{resolvedIP}): Status = {result.Status ?? NotAvailable}");
            if (result.Status == IPStatus.Success.ToString()) { sb.Append($" ({result.RoundtripTimeMs ?? 0} ms)"); }
            if (!string.IsNullOrEmpty(result.Error)) { sb.Append($" (Error: {result.Error})"); }
            sb.AppendLine();
        }

        private static void RenderDnsResolutionResult(StringBuilder sb, DnsResolutionResult? result)
        {
            if (result == null) { sb.AppendLine($"  DNS Resolution Test: Data unavailable."); return; }
            string target = result.Hostname ?? "Default Host";
            sb.Append($"  DNS Resolution Test ({target}): Success = {result.Success}");
            if (result.Success)
            {
                sb.Append($" ({result.ResolutionTimeMs ?? 0} ms)");
                sb.Append($" -> IPs: {FormatList(result.ResolvedIpAddresses)}");
            }
            if (!string.IsNullOrEmpty(result.Error)) { sb.Append($" (Error: {result.Error})"); }
            sb.AppendLine();
        }

        private static void RenderStabilityInfo(StringBuilder sb, StabilityInfo data)
        {
            sb.AppendLine(" Recent Crash Dumps:");
            if (data.RecentCrashDumps?.Any() ?? false)
            {
                sb.AppendLine("  File Name             | Timestamp (UTC)       | Size");
                sb.AppendLine("  ----------------------|-----------------------|-------------");
                foreach (var dump in data.RecentCrashDumps)
                {
                    string sizeFormatted = dump.FileSizeBytes.HasValue
                                            ? FormatHelper.FormatBytes((ulong)dump.FileSizeBytes.Value)
                                            : NotAvailable;
                    sb.AppendLine($"  {dump.FileName?.PadRight(21) ?? NotAvailable.PadRight(21)} | {FormatNullableDateTime(dump.Timestamp, "yyyy-MM-dd HH:mm:ss"),-21} | {sizeFormatted}");
                }
            }
            else { sb.AppendLine("  No recent crash dump files found in standard locations."); }
        }


        private static void RenderEventLogInfo(StringBuilder sb, EventLogInfo data)
        {
            Action<string, List<EventEntry>?> renderLog = (logName, entries) =>
            {
                sb.AppendLine($"\n {logName} Event Log (Recent Errors/Warnings):");
                if (entries?.Any() ?? false)
                {
                    if (entries.Count == 1 && entries[0].Source == null) { sb.AppendLine($"  [{entries[0].Message}]"); return; }

                    var actualEntries = entries.Where(e => e.Source != null).ToList();
                    if (!actualEntries.Any())
                    {
                        if (entries.Any(e => e.Source == null))
                        {
                            foreach (var entry in entries.Where(e => e.Source == null)) sb.AppendLine($"  [Collector Message: {entry.Message}]");
                        }
                        else
                        {
                            sb.AppendLine($"  No recent Error/Warning entries found.");
                        }
                        return;
                    }

                    int count = 0;
                    foreach (var entry in actualEntries.OrderBy(e => e.EntryType == "Error" ? 0 : 1).ThenByDescending(e => e.TimeGenerated))
                    {
                        string msg = entry.Message?.Replace("\r", "").Replace("\n", " ").Trim() ?? "";
                        if (msg.Length > 150) msg = msg.Substring(0, 147) + "...";
                        sb.AppendLine($"  - {FormatNullableDateTime(entry.TimeGenerated)} [{entry.EntryType?.ToUpperInvariant()}] {entry.Source ?? "?"} (ID:{entry.InstanceId})");
                        sb.AppendLine($"      Message: {msg}");
                        count++;
                        if (count >= 20 && actualEntries.Count > 20) { sb.AppendLine($"    (... and {actualEntries.Count - count} more Error/Warning entries)"); break; }
                    }
                }
                else { sb.AppendLine("  Data unavailable or none found."); }
            };
            renderLog("System", data.SystemLogEntries);
            sb.AppendLine();
            renderLog("Application", data.ApplicationLogEntries);
        }


        private static void RenderAnalysisSummary(StringBuilder sb, AnalysisSummary data, AppConfiguration? config)
        {
            if (data == null) { sb.AppendLine("Analysis data unavailable or analysis did not run."); return; }
            bool anyContent = false;

            if (!string.IsNullOrEmpty(data.SectionCollectionErrorMessage))
            { sb.AppendLine($"[ANALYSIS ENGINE ERROR: {data.SectionCollectionErrorMessage}]"); anyContent = true; }

            if (data.Windows11Readiness?.Checks?.Any() ?? false)
            {
                anyContent = true;
                sb.AppendLine("\n>>> Windows 11 Readiness Check:");
                string overallStatus = data.Windows11Readiness.OverallResult switch { true => "PASS", false => "FAIL", _ => "INCOMPLETE/ERROR" };
                sb.AppendLine($"  Overall Status: {overallStatus}");
                sb.AppendLine("  " + "Component".PadRight(17) + "| " + "Status".PadRight(15) + "| Details");
                sb.AppendLine("  -----------------|-----------------|-----------------------------------------");
                foreach (var check in data.Windows11Readiness.Checks.OrderBy(c => c.ComponentChecked))
                {
                    sb.AppendLine($"  {check.ComponentChecked?.PadRight(16) ?? "General".PadRight(16)} | {check.Status?.PadRight(15) ?? "Unknown".PadRight(15)} | {check.Details ?? ""}");
                }
                sb.AppendLine();
            }

            if (data.CriticalEventsFound?.Any() ?? false)
            {
                anyContent = true;
                sb.AppendLine("\n>>> Critical Events Found:");
                foreach (var ev in data.CriticalEventsFound.OrderByDescending(e => e.Timestamp))
                {
                    sb.AppendLine($"  - CRITICAL: {FormatNullableDateTime(ev.Timestamp)} [{ev.LogName}] {ev.Source} (ID:{ev.EventID})");
                    sb.AppendLine($"      Message: {ev.MessageExcerpt}");
                }
                sb.AppendLine();
            }

            Action<string, List<string>> renderFindingList = (title, items) =>
            {
                if (items?.Any() ?? false)
                {
                    anyContent = true;
                    sb.AppendLine($"\n>>> {title}:");
                    foreach (var item in items) sb.AppendLine($"  - {item}");
                    sb.AppendLine();
                }
            };

            renderFindingList("Potential Issues Found", data.PotentialIssues);
            renderFindingList("Suggestions", data.Suggestions);
            renderFindingList("Informational Notes", data.Info);

            var analysisConfig = config;
            if (analysisConfig != null && anyContent)
            {
                sb.AppendLine("\n--- Analysis Thresholds Used ---");
                var thresholds = analysisConfig.AnalysisThresholds;
                sb.AppendLine($"  Memory High/Elevated %: {thresholds.HighMemoryUsagePercent}/{thresholds.ElevatedMemoryUsagePercent}");
                sb.AppendLine($"  Disk Critical/Low Free %: {thresholds.CriticalDiskSpacePercent}/{thresholds.LowDiskSpacePercent}");
                sb.AppendLine($"  CPU High/Elevated %: {thresholds.HighCpuUsagePercent}/{thresholds.ElevatedCpuUsagePercent}");
                sb.AppendLine($"  High Disk Queue Length: {thresholds.HighDiskQueueLength}");
                sb.AppendLine($"  Max Sys/App Log Errors (Issue): {thresholds.MaxSystemLogErrorsIssue}/{thresholds.MaxAppLogErrorsIssue}");
                sb.AppendLine($"  Max Sys/App Log Errors (Suggest): {thresholds.MaxSystemLogErrorsSuggestion}/{thresholds.MaxAppLogErrorsSuggestion}");
                sb.AppendLine($"  Max Uptime Suggestion (Days): {thresholds.MaxUptimeDaysSuggestion}");
                sb.AppendLine($"  Driver Age Warning (Years): {thresholds.DriverAgeWarningYears}");
                sb.AppendLine($"  Ping/Traceroute Latency Warn (ms): {thresholds.MaxPingLatencyWarningMs}/{thresholds.MaxTracerouteHopLatencyWarningMs}");
            }

            if (!anyContent && string.IsNullOrEmpty(data.SectionCollectionErrorMessage))
            {
                sb.AppendLine("No specific issues, suggestions, or critical events found by the analysis based on collected data and configured thresholds.");
            }
        }
        #endregion

        #region Formatting Helpers (Local to this class)
        private static string FormatNullableDateTime(DateTime? dt, string format = "yyyy-MM-dd HH:mm:ss")
        { return dt.HasValue ? dt.Value.ToString(format) : NotAvailable; }

        private static string FormatList(List<string>? list, string separator = ", ")
        {
            if (list == null || !list.Any()) return NotAvailable;
            var filteredList = list.Where(s => !string.IsNullOrEmpty(s)).ToList();
            return filteredList.Any() ? string.Join(separator, filteredList) : NotAvailable;
        }
        #endregion
    }
}