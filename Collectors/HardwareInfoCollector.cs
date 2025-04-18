// Collectors/HardwareInfoCollector.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using DiagnosticToolAllInOne.Helpers; // Ensure all helpers are referenced

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
                // --- Processor Info ---
                WmiHelper.ProcessWmiResults(
                    WmiHelper.Query("Win32_Processor", null, WMI_CIMV2),
                    obj =>
                    {
                        hardwareInfo.Processors ??= new List<ProcessorInfo>(); // Initialize if null
                        hardwareInfo.Processors.Add(new ProcessorInfo
                        {
                            Name = WmiHelper.GetProperty(obj, "Name"),
                            Socket = WmiHelper.GetProperty(obj, "SocketDesignation"),
                            Cores = uint.TryParse(WmiHelper.GetProperty(obj, "NumberOfCores"), out uint cores) ? cores : null,
                            LogicalProcessors = uint.TryParse(WmiHelper.GetProperty(obj, "NumberOfLogicalProcessors"), out uint lps) ? lps : null,
                            MaxSpeedMHz = uint.TryParse(WmiHelper.GetProperty(obj, "MaxClockSpeed"), out uint speed) ? speed : null,
                            L2CacheSizeKB = ulong.TryParse(WmiHelper.GetProperty(obj, "L2CacheSize", "0"), out ulong l2kb) ? l2kb : null,
                            L3CacheSizeKB = ulong.TryParse(WmiHelper.GetProperty(obj, "L3CacheSize", "0"), out ulong l3kb) ? l3kb : null
                        });
                    },
                    error => hardwareInfo.ProcessorErrorMessage = error
                );


                // --- Memory Info ---
                hardwareInfo.Memory = new MemoryInfo();
                string? totalVisibleMemKBStr = null;
                string? freePhysicalMemKBStr = null;
                WmiHelper.ProcessWmiResults(
                    WmiHelper.Query("Win32_OperatingSystem", new[] { "TotalVisibleMemorySize", "FreePhysicalMemory" }, WMI_CIMV2),
                    obj =>
                    {
                        totalVisibleMemKBStr = WmiHelper.GetProperty(obj, "TotalVisibleMemorySize");
                        freePhysicalMemKBStr = WmiHelper.GetProperty(obj, "FreePhysicalMemory");
                    },
                    error => hardwareInfo.AddSpecificError("MemoryOS_WMI", error)
                );

                if (hardwareInfo.Memory != null)
                {
                    if (ulong.TryParse(totalVisibleMemKBStr, out ulong totalKB) && totalKB > 0)
                    {
                        hardwareInfo.Memory.TotalVisibleMemoryKB = totalKB;

                        if (ulong.TryParse(freePhysicalMemKBStr, out ulong freeKB))
                        {
                            hardwareInfo.Memory.AvailableMemoryKB = freeKB;
                            ulong usedKB = totalKB > freeKB ? totalKB - freeKB : 0;
                            hardwareInfo.Memory.PercentUsed = (double)usedKB / totalKB * 100.0;
                        }
                        else
                        {
                             hardwareInfo.AddSpecificError("MemoryCalc", "Failed to parse FreePhysicalMemory from WMI.");
                             hardwareInfo.Memory.AvailableMemoryKB = null;
                             hardwareInfo.Memory.PercentUsed = null;
                        }
                    }
                    else if (!(hardwareInfo.SpecificCollectionErrors?.ContainsKey("MemoryOS_WMI") ?? false))
                    {
                        hardwareInfo.AddSpecificError("MemoryCalc", "Failed to parse TotalVisibleMemorySize from WMI or value was zero.");
                        hardwareInfo.Memory.TotalVisibleMemoryKB = null;
                        hardwareInfo.Memory.AvailableMemoryKB = null;
                        hardwareInfo.Memory.PercentUsed = null;
                    }

                    // Physical Modules
                    WmiHelper.ProcessWmiResults(
                        WmiHelper.Query("Win32_PhysicalMemory", null, WMI_CIMV2),
                        obj =>
                        {
                            hardwareInfo.Memory.Modules ??= new List<MemoryModuleInfo>();
                            hardwareInfo.Memory.Modules.Add(new MemoryModuleInfo
                            {
                                DeviceLocator = WmiHelper.GetProperty(obj, "DeviceLocator"),
                                CapacityBytes = ulong.TryParse(WmiHelper.GetProperty(obj, "Capacity", "0"), out ulong capBytes) ? capBytes : null,
                                SpeedMHz = uint.TryParse(WmiHelper.GetProperty(obj, "Speed"), out uint speed) ? speed : null,
                                MemoryType = FormatHelper.GetMemoryTypeDescription(WmiHelper.GetProperty(obj, "MemoryType")),
                                FormFactor = FormatHelper.GetFormFactorDescription(WmiHelper.GetProperty(obj, "FormFactor")),
                                BankLabel = WmiHelper.GetProperty(obj, "BankLabel"),
                                Manufacturer = WmiHelper.GetProperty(obj, "Manufacturer"),
                                PartNumber = WmiHelper.GetProperty(obj, "PartNumber")
                            });
                        },
                        error => hardwareInfo.AddSpecificError("MemoryModules_WMI", error)
                    );
                     if((hardwareInfo.Memory.Modules == null || !hardwareInfo.Memory.Modules.Any()) && !(hardwareInfo.SpecificCollectionErrors?.ContainsKey("MemoryModules_WMI") ?? false))
                     {
                         hardwareInfo.AddSpecificError("MemoryModules_NotFound", "WMI query succeeded but returned no physical memory modules.");
                     }
                }
                else
                {
                    hardwareInfo.AddSpecificError("MemoryObject_Init", "MemoryInfo object could not be initialized.");
                }


                // --- Physical Disk Info ---
                hardwareInfo.PhysicalDisks = new List<PhysicalDiskInfo>(); // Initialize
                WmiHelper.ProcessWmiResults(
                   WmiHelper.Query("Win32_DiskDrive", null, WMI_CIMV2),
                   obj =>
                   {
                       uint diskIndex = uint.Parse(WmiHelper.GetProperty(obj, "Index", "999"));
                       string diskStatus = WmiHelper.GetProperty(obj, "Status");
                       ulong sizeBytes = ulong.TryParse(WmiHelper.GetProperty(obj, "Size", "0"), out ulong sizeB) ? sizeB : 0;
                       string interfaceType = WmiHelper.GetProperty(obj, "InterfaceType");
                       string model = WmiHelper.GetProperty(obj, "Model");

                       string serialNumber = WmiHelper.GetProperty(obj, "SerialNumber", isAdmin ? "N/A" : "Requires Admin");
                       if (serialNumber == "Requires Admin")
                       {
                           hardwareInfo.AddSpecificError($"PhysicalDisk_{diskIndex}_Serial", "Requires Admin");
                       }

                       var disk = new PhysicalDiskInfo
                       {
                           Index = diskIndex,
                           Model = model,
                           MediaType = WmiHelper.GetProperty(obj, "MediaType"),
                           InterfaceType = interfaceType,
                           SizeBytes = sizeBytes > 0 ? sizeBytes : null,
                           Partitions = uint.TryParse(WmiHelper.GetProperty(obj, "Partitions"), out uint parts) ? parts : null,
                           SerialNumber = serialNumber,
                           Status = diskStatus,
                           // NOTE: IsSystemDisk is NOT set here. It needs to be set after SystemInfo is collected.
                           IsSystemDisk = null,
                           SmartStatus = GetSmartStatus(diskIndex, isAdmin, diskStatus, interfaceType, model, hardwareInfo)
                       };
                       hardwareInfo.PhysicalDisks.Add(disk);
                   },
                   error => hardwareInfo.PhysicalDiskErrorMessage = error
                );

                // --- REMOVED: System Disk Mapping Logic ---
                // This logic requires SystemInfo data which is not available here.
                // It should be performed after all collections are complete, e.g., in Program.cs.


                // --- Logical Disk Info ---
                hardwareInfo.LogicalDisks = new List<LogicalDiskInfo>(); // Initialize
                WmiHelper.ProcessWmiResults(
                    WmiHelper.Query("Win32_LogicalDisk", null, WMI_CIMV2, "DriveType=3"),
                    obj =>
                    {
                        string sizeStr = WmiHelper.GetProperty(obj, "Size", "0");
                        string freeStr = WmiHelper.GetProperty(obj, "FreeSpace", "0");
                        ulong size = 0;
                        ulong free = 0;
                        double? percentFree = null;
                        if (ulong.TryParse(sizeStr, out size) && size > 0 && ulong.TryParse(freeStr, out free))
                        { percentFree = (double)free / size * 100.0; }

                        hardwareInfo.LogicalDisks.Add(new LogicalDiskInfo
                        {
                            DeviceID = WmiHelper.GetProperty(obj, "DeviceID"),
                            VolumeName = WmiHelper.GetProperty(obj, "VolumeName"),
                            FileSystem = WmiHelper.GetProperty(obj, "FileSystem"),
                            SizeBytes = size > 0 ? size : null,
                            FreeSpaceBytes = free,
                            PercentFree = percentFree
                        });
                    },
                     error => hardwareInfo.LogicalDiskErrorMessage = error
                );
                 if ((hardwareInfo.LogicalDisks == null || !hardwareInfo.LogicalDisks.Any()) && !(hardwareInfo.SpecificCollectionErrors?.ContainsKey("LogicalDisk") ?? false))
                 {
                      hardwareInfo.AddSpecificError("LogicalDisk_NotFound", "WMI query succeeded but returned no logical disks (DriveType=3).");
                 }


                // --- Volume Info ---
                hardwareInfo.Volumes = new List<VolumeInfo>(); // Initialize
                WmiHelper.ProcessWmiResults(
                   WmiHelper.Query("Win32_Volume", new[] { "Name", "DeviceID", "DriveLetter", "FileSystem", "Capacity", "FreeSpace" }, WMI_CIMV2, "DriveType=3"),
                   obj =>
                   {
                       string? deviceID = WmiHelper.GetProperty(obj, "DeviceID");
                       string? protectionStatus = GetBitLockerStatus(deviceID, isAdmin, hardwareInfo);
                       hardwareInfo.Volumes.Add(new VolumeInfo
                       {
                           Name = WmiHelper.GetProperty(obj, "Name"),
                           DeviceID = deviceID,
                           DriveLetter = WmiHelper.GetProperty(obj, "DriveLetter"),
                           FileSystem = WmiHelper.GetProperty(obj, "FileSystem"),
                           CapacityBytes = ulong.TryParse(WmiHelper.GetProperty(obj, "Capacity", "0"), out ulong capBytes) ? capBytes : null,
                           FreeSpaceBytes = ulong.TryParse(WmiHelper.GetProperty(obj, "FreeSpace", "0"), out ulong freeBytes) ? freeBytes : null,
                           IsBitLockerProtected = protectionStatus?.Contains("Protection On", StringComparison.OrdinalIgnoreCase),
                           ProtectionStatus = protectionStatus
                       });
                   },
                    error => hardwareInfo.VolumeErrorMessage = error
                );
                if ((hardwareInfo.Volumes == null || !hardwareInfo.Volumes.Any()) && !(hardwareInfo.SpecificCollectionErrors?.ContainsKey("Volume") ?? false))
                {
                     hardwareInfo.AddSpecificError("Volume_NotFound", "WMI query succeeded but returned no volumes (DriveType=3).");
                }


                // --- GPU Info ---
                hardwareInfo.Gpus = new List<GpuInfo>(); // Initialize
                WmiHelper.ProcessWmiResults(
                    WmiHelper.Query("Win32_VideoController", null, WMI_CIMV2),
                    obj =>
                    {
                        uint.TryParse(WmiHelper.GetProperty(obj, "CurrentHorizontalResolution"), out uint horizRes);
                        uint.TryParse(WmiHelper.GetProperty(obj, "CurrentVerticalResolution"), out uint vertRes);
                        uint.TryParse(WmiHelper.GetProperty(obj, "CurrentRefreshRate"), out uint refreshRate);

                        hardwareInfo.Gpus.Add(new GpuInfo
                        {
                            Name = WmiHelper.GetProperty(obj, "Name"),
                            AdapterRAMBytes = ulong.TryParse(WmiHelper.GetProperty(obj, "AdapterRAM", "0"), out ulong ramBytes) ? ramBytes : null,
                            DriverVersion = WmiHelper.GetProperty(obj, "DriverVersion"),
                            DriverDate = WmiHelper.ConvertCimDateTime(WmiHelper.GetProperty(obj, "DriverDate")),
                            VideoProcessor = WmiHelper.GetProperty(obj, "VideoProcessor"),
                            CurrentHorizontalResolution = horizRes > 0 ? horizRes : null,
                            CurrentVerticalResolution = vertRes > 0 ? vertRes : null,
                            CurrentRefreshRate = refreshRate > 0 ? refreshRate : null,
                            Status = WmiHelper.GetProperty(obj, "Status"),
                            WddmVersion = WmiHelper.GetProperty(obj,"WddmVersion", null)
                        });
                    },
                    error => hardwareInfo.GpuErrorMessage = error
                 );


                // --- Monitor Info ---
                hardwareInfo.Monitors = new List<MonitorInfo>(); // Initialize
                WmiHelper.ProcessWmiResults(
                   WmiHelper.Query("Win32_DesktopMonitor", null, WMI_CIMV2),
                   obj =>
                   {
                       uint.TryParse(WmiHelper.GetProperty(obj, "ScreenWidth"), out uint screenW);
                       uint.TryParse(WmiHelper.GetProperty(obj, "ScreenHeight"), out uint screenH);
                       uint.TryParse(WmiHelper.GetProperty(obj, "PixelsPerXLogicalInch"), out uint ppiX);
                       uint.TryParse(WmiHelper.GetProperty(obj, "PixelsPerYLogicalInch"), out uint ppiY);

                       double? diagonal = null;
                       if (screenW > 0 && screenH > 0 && ppiX > 0)
                       {
                           double widthInches = (double)screenW / ppiX;
                           double heightInches = (double)screenH / ppiX;
                           diagonal = Math.Sqrt(widthInches * widthInches + heightInches * heightInches);
                       }

                       hardwareInfo.Monitors.Add(new MonitorInfo
                       {
                           Name = WmiHelper.GetProperty(obj, "Name"),
                           DeviceID = WmiHelper.GetProperty(obj, "DeviceID"),
                           PnpDeviceID = WmiHelper.GetProperty(obj, "PNPDeviceID"),
                           Manufacturer = WmiHelper.GetProperty(obj, "MonitorManufacturer"),
                           ScreenWidth = screenW > 0 ? screenW : null,
                           ScreenHeight = screenH > 0 ? screenH : null,
                           PixelsPerXLogicalInch = ppiX > 0 ? ppiX : null,
                           PixelsPerYLogicalInch = ppiY > 0 ? ppiY : null,
                           DiagonalSizeInches = diagonal
                       });
                   },
                    error => hardwareInfo.MonitorErrorMessage = error
               );


                // --- Audio Devices ---
                hardwareInfo.AudioDevices = new List<AudioDeviceInfo>(); // Initialize
                WmiHelper.ProcessWmiResults(
                    WmiHelper.Query("Win32_SoundDevice", null, WMI_CIMV2),
                    obj =>
                    {
                        hardwareInfo.AudioDevices.Add(new AudioDeviceInfo
                        {
                            Name = WmiHelper.GetProperty(obj, "Name"),
                            ProductName = WmiHelper.GetProperty(obj, "ProductName"),
                            Manufacturer = WmiHelper.GetProperty(obj, "Manufacturer"),
                            Status = WmiHelper.GetProperty(obj, "Status")
                        });
                    },
                    error => hardwareInfo.AudioErrorMessage = error
                 );
                 if ((hardwareInfo.AudioDevices == null || !hardwareInfo.AudioDevices.Any()) && !(hardwareInfo.SpecificCollectionErrors?.ContainsKey("Audio") ?? false))
                 {
                      hardwareInfo.AddSpecificError("Audio_NotFound", "WMI query succeeded but returned no sound devices.");
                 }

            }
            catch (Exception ex)
            {
                Logger.LogError($"[CRITICAL ERROR] Failed during Hardware Info Collection setup", ex);
                hardwareInfo.SectionCollectionErrorMessage = $"Critical failure during hardware collection: {ex.Message}";
            }

            await Task.CompletedTask;
            return hardwareInfo;
        }

        // --- GetSmartStatus and GetBitLockerStatus Methods ---
        // (These remain unchanged from the previous corrected version)

        private static SmartStatusInfo GetSmartStatus(uint diskIndex, bool isAdmin, string diskDriveStatus, string interfaceType, string model, HardwareInfo hardwareInfo)
        {
            var status = new SmartStatusInfo { StatusText = "Unknown", BasicStatusFromDiskDrive = diskDriveStatus };
            string errorKey = $"PhysicalDisk_{diskIndex}_SMART"; // Key for specific errors

            if (!isAdmin)
            {
                status.StatusText = "Requires Admin";
                status.Error = "Requires Admin privileges for WMI SMART query.";
                hardwareInfo.AddSpecificError(errorKey, "Requires Admin");
                return status;
            }

            bool isLikelyNVMe = interfaceType.Contains("NVME", StringComparison.OrdinalIgnoreCase) ||
                                interfaceType.Contains("STORAGE_BUS_TYPE.NVME", StringComparison.OrdinalIgnoreCase) ||
                                model.Contains("NVME", StringComparison.OrdinalIgnoreCase);

            string query = $"SELECT PredictFailure, Reason FROM MSStorageDriver_FailurePredictStatus WHERE InstanceName LIKE '%PHYSICALDRIVE{diskIndex}%'";
            bool foundWmiStatus = false;
            string? wmiError = null;
            WmiHelper.ProcessWmiResults(
                WmiHelper.Query(query, null, WMI_WMI),
                obj =>
                {
                    foundWmiStatus = true;
                    // FIX CS8625: Check obj["PredictFailure"] is not null before casting
                    if (obj["PredictFailure"] != null) {
                        status.IsFailurePredicted = (bool)obj["PredictFailure"];
                        status.ReasonCode = obj["Reason"]?.ToString(); // ReasonCode is nullable string
                        status.StatusText = status.IsFailurePredicted ? "Failure Predicted" : "OK";
                        status.Error = null;
                    } else {
                         status.StatusText = "Query Error (Null PredictFailure)";
                         status.Error = "WMI returned null for PredictFailure property.";
                         hardwareInfo.AddSpecificError(errorKey, status.Error);
                    }
                },
                error => wmiError = error
            );

            if (foundWmiStatus && status.Error == null) // Check if error was set during processing
            {
                // Status already set
            }
            else if (!string.IsNullOrEmpty(wmiError))
            {
                status.Error = wmiError;
                hardwareInfo.AddSpecificError(errorKey, $"Query Error: {wmiError}");

                if (wmiError.Contains("InvalidQuery") || wmiError.Contains("NotFound") || wmiError.Contains("Not supported"))
                { status.StatusText = isLikelyNVMe ? "Not Supported (NVMe via WMI)" : "Not Supported"; }
                else if (wmiError.Contains("Invalid namespace") || wmiError.Contains("Invalid class"))
                { status.StatusText = "Not Supported (WMI Scope)"; }
                else { status.StatusText = "Query Error"; }
            }
             else if (!foundWmiStatus && status.Error == null) // No rows found and no processing error occurred
            {
                status.StatusText = "Not Reported";
                status.Error = "No SMART data returned via MSStorageDriver_FailurePredictStatus.";
                 hardwareInfo.AddSpecificError(errorKey, status.Error);
            }

            if (status.StatusText != "OK" && status.StatusText != "Not Supported (NVMe via WMI)")
            {
                 if (!string.IsNullOrEmpty(diskDriveStatus) && !diskDriveStatus.Equals("OK", StringComparison.OrdinalIgnoreCase))
                 {
                      status.StatusText += $" (Basic Status: {diskDriveStatus})";
                      status.Error = string.IsNullOrEmpty(status.Error) ? $"Basic disk status is '{diskDriveStatus}'." : $"{status.Error}; Basic disk status is '{diskDriveStatus}'.";
                 }
            }
            return status;
        }

        private static string GetBitLockerStatus(string? volumeDeviceID, bool isAdmin, HardwareInfo hardwareInfo)
        {
            string errorKey = $"Volume_{(volumeDeviceID?.Replace("\\", "_").Replace("?", "").Replace("{", "").Replace("}", "") ?? "Unknown")}_Bitlocker";

            if (string.IsNullOrEmpty(volumeDeviceID)) return "Invalid Volume ID";
            if (!isAdmin) { hardwareInfo.AddSpecificError(errorKey, "Requires Admin"); return "Requires Admin"; }

            string status = "Unknown";
            bool found = false;
            string? queryError = null;

            try
            {
                string escapedDeviceID = volumeDeviceID.Replace(@"\", @"\\").Replace("\"", "\\\"");
                string condition = $"DeviceID = \"{escapedDeviceID}\"";

                WmiHelper.ProcessWmiResults(
                   WmiHelper.Query("Win32_EncryptableVolume", new[] { "DeviceID", "ProtectionStatus" }, WMI_MSVOLENC, condition),
                   mo =>
                   {
                       found = true;
                       status = WmiHelper.GetProperty(mo, "ProtectionStatus") switch {
                           "0" => "Protection Off", "1" => "Protection On", "2" => "Protection Unknown",
                           _ => $"Unknown Status Code ({WmiHelper.GetProperty(mo, "ProtectionStatus")})" };
                   },
                   error => queryError = error
               );

                if (!found && queryError == null) { status = "Not Found/Not Encryptable"; }
                else if (queryError != null)
                {
                    hardwareInfo.AddSpecificError(errorKey, $"Query Error: {queryError}");
                    if (queryError.Contains("Invalid namespace") || queryError.Contains("NotFound")) { status = "BitLocker WMI Scope/Class Not Found"; }
                    else if (queryError.Contains("Access Denied")) { status = "Access Denied (BitLocker Check)"; }
                    else { status = $"Error checking BitLocker: {queryError}"; }
                }
            }
            catch (Exception ex)
            {
                status = $"Error checking BitLocker: {ex.GetType().Name}";
                hardwareInfo.AddSpecificError(errorKey, $"Unexpected Error: {ex.Message}");
                Logger.LogError($"[CRITICAL ERROR] BitLocker Check failed unexpectedly for volume {volumeDeviceID}", ex);
            }
            return status;
        }

    }
}