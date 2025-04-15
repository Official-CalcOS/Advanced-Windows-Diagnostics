// Collectors/HardwareInfoCollector.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using DiagnosticToolAllInOne.Helpers;

namespace DiagnosticToolAllInOne.Collectors
{
    [SupportedOSPlatform("windows")]
    public static class HardwareInfoCollector
    {
        private const string WMI_CIMV2 = @"root\cimv2";
        private const string WMI_WMI = @"root\wmi";
        private const string WMI_MSVOLENC = @"root\cimv2\Security\MicrosoftVolumeEncryption";


        public static async Task<HardwareInfo> CollectAsync(bool isAdmin)
        {
            var hardwareInfo = new HardwareInfo();

            try
            {
                // --- Processor Info --- (No changes needed for raw values here)
                WmiHelper.ProcessWmiResults(
                    WmiHelper.Query("Win32_Processor", null, WMI_CIMV2),
                    obj => {
                        hardwareInfo.Processors ??= new(); // Ensure list is initialized
                        hardwareInfo.Processors.Add(new ProcessorInfo
                        {
                             Name = WmiHelper.GetProperty(obj, "Name"),
                             Socket = WmiHelper.GetProperty(obj, "SocketDesignation"),
                             Cores = uint.TryParse(WmiHelper.GetProperty(obj, "NumberOfCores"), out uint cores) ? cores : null,
                             LogicalProcessors = uint.TryParse(WmiHelper.GetProperty(obj, "NumberOfLogicalProcessors"), out uint lps) ? lps : null,
                             MaxSpeedMHz = uint.TryParse(WmiHelper.GetProperty(obj, "MaxClockSpeed"), out uint speed) ? speed : null,
                             L2Cache = FormatHelper.FormatBytes(ulong.Parse(WmiHelper.GetProperty(obj, "L2CacheSize", "0")) * 1024), // KB to Bytes
                             L3Cache = FormatHelper.FormatBytes(ulong.Parse(WmiHelper.GetProperty(obj, "L3CacheSize", "0")) * 1024)  // KB to Bytes
                        });
                    },
                    error => hardwareInfo.ProcessorErrorMessage = error
                );


                // --- Memory Info ---
                hardwareInfo.Memory = new MemoryInfo();
                string? totalVisibleMemKBStr = null; // Renamed variable
                string? freePhysicalMemKBStr = null; // Renamed variable
                WmiHelper.ProcessWmiResults(
                    WmiHelper.Query("Win32_OperatingSystem", new[] { "TotalVisibleMemorySize", "FreePhysicalMemory" }, WMI_CIMV2),
                    obj => {
                        totalVisibleMemKBStr = WmiHelper.GetProperty(obj, "TotalVisibleMemorySize");
                        freePhysicalMemKBStr = WmiHelper.GetProperty(obj, "FreePhysicalMemory");
                    },
                    error => hardwareInfo.AddSpecificError("MemoryOS", error)
                );

                if (hardwareInfo.Memory != null)
                {
                    // Store raw value first
                    if (ulong.TryParse(totalVisibleMemKBStr, out ulong totalKB))
                    {
                         hardwareInfo.Memory.TotalVisibleMemoryKB = totalKB; // Store raw value
                    }

                    if (totalKB > 0 && ulong.TryParse(freePhysicalMemKBStr, out ulong freeKB))
                    {
                        ulong usedKB = totalKB > freeKB ? totalKB - freeKB : 0;
                        hardwareInfo.Memory.TotalVisible = FormatHelper.FormatBytes(totalKB * 1024);
                        hardwareInfo.Memory.Available = FormatHelper.FormatBytes(freeKB * 1024);
                        hardwareInfo.Memory.Used = FormatHelper.FormatBytes(usedKB * 1024);
                        hardwareInfo.Memory.PercentUsed = totalKB > 0 ? (double)usedKB / totalKB * 100.0 : 0;
                    } else if (!(hardwareInfo.SpecificCollectionErrors?.ContainsKey("MemoryOS") ?? false)){
                        hardwareInfo.AddSpecificError("MemoryCalc", "Failed to parse memory values from WMI.");
                    }

                    // Physical Modules (No changes needed here)
                    WmiHelper.ProcessWmiResults(
                        WmiHelper.Query("Win32_PhysicalMemory", null, WMI_CIMV2),
                        obj => {
                            hardwareInfo.Memory.Modules ??= new List<MemoryModuleInfo>();
                            hardwareInfo.Memory.Modules.Add(new MemoryModuleInfo
                            {
                                DeviceLocator = WmiHelper.GetProperty(obj, "DeviceLocator"),
                                Capacity = FormatHelper.FormatBytes(ulong.Parse(WmiHelper.GetProperty(obj, "Capacity", "0"))),
                                SpeedMHz = uint.TryParse(WmiHelper.GetProperty(obj, "Speed"), out uint speed) ? speed : null,
                                MemoryType = FormatHelper.GetMemoryTypeDescription(WmiHelper.GetProperty(obj, "MemoryType")),
                                FormFactor = FormatHelper.GetFormFactorDescription(WmiHelper.GetProperty(obj, "FormFactor")),
                                BankLabel = WmiHelper.GetProperty(obj, "BankLabel"),
                                Manufacturer = WmiHelper.GetProperty(obj, "Manufacturer"),
                                PartNumber = WmiHelper.GetProperty(obj, "PartNumber")
                            });
                        },
                         error => hardwareInfo.AddSpecificError("MemoryModules", error)
                    );
                } else {
                     hardwareInfo.AddSpecificError("MemoryObject", "MemoryInfo object could not be initialized.");
                }


                // --- Physical Disk Info ---
                hardwareInfo.PhysicalDisks = new();
                 WmiHelper.ProcessWmiResults(
                    WmiHelper.Query("Win32_DiskDrive", null, WMI_CIMV2),
                    obj => {
                        uint diskIndex = uint.Parse(WmiHelper.GetProperty(obj, "Index", "999"));
                        string diskStatus = WmiHelper.GetProperty(obj, "Status");
                        ulong sizeBytes = ulong.Parse(WmiHelper.GetProperty(obj, "Size", "0")); // Get raw bytes

                        var disk = new PhysicalDiskInfo
                        {
                            Index = diskIndex,
                            Model = WmiHelper.GetProperty(obj, "Model"),
                            MediaType = WmiHelper.GetProperty(obj, "MediaType"),
                            InterfaceType = WmiHelper.GetProperty(obj, "InterfaceType"),
                            SizeBytes = sizeBytes, // Store raw bytes
                            Size = FormatHelper.FormatBytes(sizeBytes), // Format for display
                            Partitions = uint.TryParse(WmiHelper.GetProperty(obj, "Partitions"), out uint parts) ? parts : null,
                            SerialNumber = WmiHelper.GetProperty(obj, "SerialNumber", isAdmin ? "N/A" : "Requires Admin"),
                            Status = diskStatus,
                            SmartStatus = GetSmartStatus(diskIndex, isAdmin, diskStatus)
                        };
                        hardwareInfo.PhysicalDisks.Add(disk);
                    },
                    error => hardwareInfo.PhysicalDiskErrorMessage = error
                 );

                 // --- TODO: Add logic here to map SystemDrive to PhysicalDiskInfo.IsSystemDisk ---
                 // This requires more complex WMI queries (Win32_LogicalDiskToPartition, Win32_DiskDriveToDiskPartition)
                 // For now, analysis engine will need to approximate using LogicalDisk or note the limitation.


                // --- Logical Disk Info ---
                hardwareInfo.LogicalDisks = new();
                WmiHelper.ProcessWmiResults(
                    WmiHelper.Query("Win32_LogicalDisk", null, WMI_CIMV2, "DriveType=3"),
                    obj => {
                        string sizeStr = WmiHelper.GetProperty(obj, "Size", "0");
                        string freeStr = WmiHelper.GetProperty(obj, "FreeSpace", "0");
                        ulong size = 0;
                        ulong free = 0;
                        double percentFree = 0;
                        if (ulong.TryParse(sizeStr, out size) && ulong.TryParse(freeStr, out free) && size > 0)
                        { percentFree = (double)free / size * 100.0; }

                        hardwareInfo.LogicalDisks.Add(new LogicalDiskInfo
                        {
                             DeviceID = WmiHelper.GetProperty(obj, "DeviceID"),
                             VolumeName = WmiHelper.GetProperty(obj, "VolumeName"),
                             FileSystem = WmiHelper.GetProperty(obj, "FileSystem"),
                             SizeBytes = size, // Store raw bytes
                             FreeSpaceBytes = free, // Store raw bytes
                             Size = FormatHelper.FormatBytes(size),
                             FreeSpace = FormatHelper.FormatBytes(free),
                             PercentFree = percentFree
                        });
                    },
                     error => hardwareInfo.LogicalDiskErrorMessage = error
                );


                // --- Volume Info --- (No changes needed here)
                hardwareInfo.Volumes = new();
                 WmiHelper.ProcessWmiResults(
                    WmiHelper.Query("Win32_Volume", new[] { "Name", "DeviceID", "DriveLetter", "FileSystem", "Capacity", "FreeSpace" }, WMI_CIMV2, "DriveType=3"),
                    obj => {
                        string? deviceID = WmiHelper.GetProperty(obj, "DeviceID");
                        string? protectionStatus = GetBitLockerStatus(deviceID, isAdmin);
                        hardwareInfo.Volumes.Add(new VolumeInfo
                        {
                             Name = WmiHelper.GetProperty(obj, "Name"),
                             DeviceID = deviceID,
                             DriveLetter = WmiHelper.GetProperty(obj, "DriveLetter"),
                             FileSystem = WmiHelper.GetProperty(obj, "FileSystem"),
                             Capacity = FormatHelper.FormatBytes(ulong.Parse(WmiHelper.GetProperty(obj, "Capacity", "0"))),
                             FreeSpace = FormatHelper.FormatBytes(ulong.Parse(WmiHelper.GetProperty(obj, "FreeSpace", "0"))),
                             IsBitLockerProtected = protectionStatus?.Contains("Protection On", StringComparison.OrdinalIgnoreCase),
                             ProtectionStatus = protectionStatus
                        });
                    },
                     error => hardwareInfo.VolumeErrorMessage = error
                );


                // --- GPU Info ---
                hardwareInfo.Gpus = new();
                WmiHelper.ProcessWmiResults(
                    WmiHelper.Query("Win32_VideoController", null, WMI_CIMV2),
                    obj => {
                         uint.TryParse(WmiHelper.GetProperty(obj, "CurrentHorizontalResolution"), out uint horizRes);
                         uint.TryParse(WmiHelper.GetProperty(obj, "CurrentVerticalResolution"), out uint vertRes);

                        hardwareInfo.Gpus.Add(new GpuInfo {
                             Name = WmiHelper.GetProperty(obj, "Name"),
                             Vram = FormatHelper.FormatBytes(ulong.Parse(WmiHelper.GetProperty(obj, "AdapterRAM", "0"))),
                             DriverVersion = WmiHelper.GetProperty(obj, "DriverVersion"),
                             DriverDate = WmiHelper.ConvertCimDateTime(WmiHelper.GetProperty(obj, "DriverDate")),
                             VideoProcessor = WmiHelper.GetProperty(obj, "VideoProcessor"),
                             CurrentHorizontalResolution = horizRes, // Store raw value
                             CurrentVerticalResolution = vertRes,   // Store raw value
                             CurrentResolution = $"{horizRes}x{vertRes} @ {WmiHelper.GetProperty(obj, "CurrentRefreshRate")} Hz", // Formatted
                             Status = WmiHelper.GetProperty(obj, "Status")
                         });
                    },
                    error => hardwareInfo.GpuErrorMessage = error
                 );


                // --- Monitor Info ---
                hardwareInfo.Monitors = new();
                 WmiHelper.ProcessWmiResults(
                    WmiHelper.Query("Win32_DesktopMonitor", null, WMI_CIMV2), // Basic info
                    obj => {
                         uint.TryParse(WmiHelper.GetProperty(obj, "ScreenWidth"), out uint screenW);
                         uint.TryParse(WmiHelper.GetProperty(obj, "ScreenHeight"), out uint screenH);

                        hardwareInfo.Monitors.Add(new MonitorInfo {
                            Name = WmiHelper.GetProperty(obj, "Name"),
                            DeviceID = WmiHelper.GetProperty(obj, "DeviceID"),
                            Manufacturer = WmiHelper.GetProperty(obj, "MonitorManufacturer"),
                            ScreenWidth = screenW, // Store raw value
                            ScreenHeight = screenH, // Store raw value
                            ReportedResolution = $"{screenW}x{screenH}", // Formatted
                            PpiLogical = $"{WmiHelper.GetProperty(obj, "PixelsPerXLogicalInch")}x{WmiHelper.GetProperty(obj, "PixelsPerYLogicalInch")}"
                        });
                    },
                     error => hardwareInfo.MonitorErrorMessage = error
                );
                 // --- TODO: Potentially add WMI query to root\wmi for WmiMonitorBasicDisplayParams for size ---
                 // This is often unreliable or requires Admin privileges.


                // --- Audio Devices --- (No changes needed here)
                hardwareInfo.AudioDevices = new();
                WmiHelper.ProcessWmiResults(
                    WmiHelper.Query("Win32_SoundDevice", null, WMI_CIMV2),
                    obj => {
                         hardwareInfo.AudioDevices.Add(new AudioDeviceInfo {
                            Name = WmiHelper.GetProperty(obj, "Name"),
                            ProductName = WmiHelper.GetProperty(obj, "ProductName"),
                            Manufacturer = WmiHelper.GetProperty(obj, "Manufacturer"),
                            Status = WmiHelper.GetProperty(obj, "StatusInfo")
                         });
                    },
                    error => hardwareInfo.AudioErrorMessage = error
                 );

            }
            catch(Exception ex)
            {
                 Console.Error.WriteLine($"[CRITICAL ERROR] Failed during Hardware Info Collection setup: {ex.Message}");
                 hardwareInfo.SectionCollectionErrorMessage = $"Critical failure during hardware collection: {ex.Message}";
            }

            await Task.CompletedTask;
            return hardwareInfo;
        }

        // Helper methods GetSmartStatus and GetBitLockerStatus remain the same
        private static SmartStatusInfo GetSmartStatus(uint diskIndex, bool isAdmin, string diskDriveStatus)
        {
            var status = new SmartStatusInfo { StatusText = "Unknown", BasicStatusFromDiskDrive = diskDriveStatus };
            if (!isAdmin) { status.StatusText = "Requires Admin"; status.Error = "Requires Admin privileges for WMI SMART query."; return status; }
            string query = $"SELECT PredictFailure, Reason FROM MSStorageDriver_FailurePredictStatus WHERE InstanceName LIKE '%PHYSICALDRIVE{diskIndex}%'";
            bool foundWmiStatus = false; string? wmiError = null;
            WmiHelper.ProcessWmiResults( WmiHelper.Query(query, null, WMI_WMI), obj => { foundWmiStatus = true; status.IsFailurePredicted = (bool)obj["PredictFailure"]; status.ReasonCode = obj["Reason"]?.ToString(); status.StatusText = status.IsFailurePredicted ? "Failure Predicted" : "OK"; status.Error = null; }, error => wmiError = error );
            if (foundWmiStatus) { /* StatusText set in lambda */ }
            else if (!string.IsNullOrEmpty(wmiError)) { status.Error = wmiError; if (wmiError.Contains("Not supported", StringComparison.OrdinalIgnoreCase) || wmiError.Contains("Invalid namespace", StringComparison.OrdinalIgnoreCase) || wmiError.Contains("Invalid class", StringComparison.OrdinalIgnoreCase)) { status.StatusText = "Not Supported"; status.Error = $"SMART WMI query failed or not supported: {wmiError}"; } else { status.StatusText = "Query Error"; } }
            else { status.StatusText = "Not Reported"; status.Error = "No SMART data returned via MSStorageDriver_FailurePredictStatus."; }
            if (status.StatusText != "OK") { if (!string.IsNullOrEmpty(diskDriveStatus) && !diskDriveStatus.Equals("OK", StringComparison.OrdinalIgnoreCase)) { status.StatusText += $" (Basic Status: {diskDriveStatus})"; status.Error = string.IsNullOrEmpty(status.Error) ? $"Basic disk status is '{diskDriveStatus}'." : $"{status.Error}; Basic disk status is '{diskDriveStatus}'."; } }
            return status;
        }
        private static string GetBitLockerStatus(string? volumeDeviceID, bool isAdmin)
        {
            if (!isAdmin) return "Requires Admin"; if (string.IsNullOrEmpty(volumeDeviceID)) return "Invalid Volume ID";
            string status = "Unknown"; bool found = false; string? queryError = null;
            try { string escapedDeviceID = volumeDeviceID.Replace(@"\", @"\\").Replace("\"", "\\\""); string condition = $"DeviceID = \"{escapedDeviceID}\"";
                 WmiHelper.ProcessWmiResults( WmiHelper.Query("Win32_EncryptableVolume", new[] { "DeviceID", "ProtectionStatus" }, WMI_MSVOLENC, condition), mo => { found = true; status = WmiHelper.GetProperty(mo, "ProtectionStatus") switch { "0" => "Protection Off", "1" => "Protection On", "2" => "Protection Unknown", _ => $"Unknown Status Code ({WmiHelper.GetProperty(mo, "ProtectionStatus")})" }; }, error => queryError = error );
                if (!found && queryError == null) { status = "Not Found/Not Encryptable"; }
                else if (queryError != null) { if (queryError.Contains("Invalid namespace", StringComparison.OrdinalIgnoreCase) || queryError.Contains("Not Found", StringComparison.OrdinalIgnoreCase)) { status = "BitLocker WMI Scope/Class Not Found"; } else if (queryError.Contains("Access Denied", StringComparison.OrdinalIgnoreCase)) { status = "Access Denied (BitLocker Check)"; } else { status = $"Error checking BitLocker: {queryError}"; } }
            } catch (Exception ex) { status = $"Error checking BitLocker: {ex.GetType().Name}"; Console.Error.WriteLine($"[CRITICAL ERROR] BitLocker Check failed unexpectedly: {ex.Message}"); }
            return status;
        }
    }
}