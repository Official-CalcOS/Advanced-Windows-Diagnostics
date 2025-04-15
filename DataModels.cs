using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Text.Json.Serialization; // Required for JsonIgnoreCondition

// Keep all files in the same namespace for simplicity
namespace DiagnosticToolAllInOne
{
    #region Data Models (for structured data & JSON)

    // Simple base class for common properties
    public abstract class DiagnosticSection
    {
        // General error message for the entire section if a critical failure occurred during collection setup
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SectionCollectionErrorMessage { get; set; }

        // Dictionary to hold errors for specific sub-parts of the collection within a section
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? SpecificCollectionErrors { get; set; }

        // Helper method to add specific errors - NOW PUBLIC
        public void AddSpecificError(string key, string message)
        {
            SpecificCollectionErrors ??= new Dictionary<string, string>();
            // Avoid overwriting if an error for this key already exists, maybe append? For now, overwrite.
            SpecificCollectionErrors[key] = message;
        }
    }

    public class SystemInfo : DiagnosticSection
    {
        public OSInfo? OperatingSystem { get; set; }
        public ComputerSystemInfo? ComputerSystem { get; set; }
        public BiosInfo? BIOS { get; set; }
        public BaseboardInfo? Baseboard { get; set; }
        public string? DotNetVersion { get; set; }
        public TimeZoneConfig? TimeZone { get; set; }
        public PowerPlanInfo? ActivePowerPlan { get; set; }
    }

    public class OSInfo
    {
        public string? Name { get; set; }
        public string? Architecture { get; set; }
        public string? Version { get; set; }
        public string? BuildNumber { get; set; } // Important for Win11 version check
        public uint? BuildNumberUint => uint.TryParse(BuildNumber, out uint result) ? result : null; // Helper for easier comparison
        public DateTime? InstallDate { get; set; }
        public DateTime? LastBootTime { get; set; }
        public TimeSpan? Uptime { get; set; }
        public string? SystemDrive { get; set; }
    }

    public class ComputerSystemInfo
    {
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? SystemType { get; set; }
        public string? DomainOrWorkgroup { get; set; }
        public bool PartOfDomain { get; set; }
        public string? CurrentUser { get; set; } // User context of the running process
        public string? LoggedInUserWMI { get; set; } // User reported by WMI ComputerSystem
    }

    public class BiosInfo
    {
        public string? Manufacturer { get; set; }
        public string? Version { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public string? SerialNumber { get; set; }
        // Could potentially add a property here indicating UEFI mode if reliably detectable via BIOS WMI info
        // public string? BiosMode { get; set; } // Example: Legacy/UEFI (often needs registry/other checks)
    }

    public class BaseboardInfo
    {
        public string? Manufacturer { get; set; }
        public string? Product { get; set; }
        public string? SerialNumber { get; set; }
        public string? Version { get; set; }
    }

    public class TimeZoneConfig
    {
        public string? CurrentTimeZone { get; set; }
        public string? StandardName { get; set; }
        public string? DaylightName { get; set; }
        public int? BiasMinutes { get; set; }
    }

    public class PowerPlanInfo
    {
        public string? Name { get; set; }
        public string? InstanceID { get; set; }
        public bool IsActive { get; set; }
    }


    public class HardwareInfo : DiagnosticSection
    {
        public List<ProcessorInfo>? Processors { get; set; } = new();
        public MemoryInfo? Memory { get; set; }
        public List<PhysicalDiskInfo>? PhysicalDisks { get; set; } = new();
        public List<LogicalDiskInfo>? LogicalDisks { get; set; } = new();
        public List<VolumeInfo>? Volumes { get; set; } = new();
        public List<GpuInfo>? Gpus { get; set; } = new();
        public List<MonitorInfo>? Monitors { get; set; } = new();
        public List<AudioDeviceInfo>? AudioDevices { get; set; } = new();

        // Example specific error properties using the base dictionary
        [JsonIgnore] public string? ProcessorErrorMessage { get => SpecificCollectionErrors?.GetValueOrDefault("Processor"); set => AddSpecificError("Processor", value ?? string.Empty); }
        [JsonIgnore] public string? GpuErrorMessage { get => SpecificCollectionErrors?.GetValueOrDefault("GPU"); set => AddSpecificError("GPU", value ?? string.Empty); }
        [JsonIgnore] public string? AudioErrorMessage { get => SpecificCollectionErrors?.GetValueOrDefault("Audio"); set => AddSpecificError("Audio", value ?? string.Empty); }
        [JsonIgnore] public string? PhysicalDiskErrorMessage { get => SpecificCollectionErrors?.GetValueOrDefault("PhysicalDisk"); set => AddSpecificError("PhysicalDisk", value ?? string.Empty); }
        [JsonIgnore] public string? LogicalDiskErrorMessage { get => SpecificCollectionErrors?.GetValueOrDefault("LogicalDisk"); set => AddSpecificError("LogicalDisk", value ?? string.Empty); }
        [JsonIgnore] public string? VolumeErrorMessage { get => SpecificCollectionErrors?.GetValueOrDefault("Volume"); set => AddSpecificError("Volume", value ?? string.Empty); }
        [JsonIgnore] public string? MemoryErrorMessage { get => SpecificCollectionErrors?.GetValueOrDefault("Memory"); set => AddSpecificError("Memory", value ?? string.Empty); }
        [JsonIgnore] public string? MonitorErrorMessage { get => SpecificCollectionErrors?.GetValueOrDefault("Monitor"); set => AddSpecificError("Monitor", value ?? string.Empty); }
        // Add more getters/setters mapping to SpecificCollectionErrors dictionary as needed

    }

    public class ProcessorInfo
    {
        public string? Name { get; set; }
        public string? Socket { get; set; }
        public uint? Cores { get; set; } // Needed for Win11 Check
        public uint? LogicalProcessors { get; set; }
        public uint? MaxSpeedMHz { get; set; } // Needed for Win11 Check
        public string? L2Cache { get; set; }
        public string? L3Cache { get; set; }
    }

    public class MemoryInfo
    {
        public string? TotalVisible { get; set; }
        public string? Available { get; set; }
        public string? Used { get; set; }
        public double? PercentUsed { get; set; }
        public ulong? TotalVisibleMemoryKB { get; set; } // Raw value from WMI for easier Win11 check
        public List<MemoryModuleInfo>? Modules { get; set; } = new();
    }

    public class MemoryModuleInfo
    {
        public string? DeviceLocator { get; set; }
        public string? Capacity { get; set; }
        public uint? SpeedMHz { get; set; }
        public string? MemoryType { get; set; }
        public string? FormFactor { get; set; }
        public string? BankLabel { get; set; }
        public string? Manufacturer { get; set; }
        public string? PartNumber { get; set; }
    }

    public class PhysicalDiskInfo
    {
        public uint Index { get; set; }
        public string? Model { get; set; }
        public string? MediaType { get; set; }
        public string? InterfaceType { get; set; }
        public string? Size { get; set; } // Formatted size
        public ulong? SizeBytes { get; set; } // Raw size in bytes for easier Win11 check
        public uint? Partitions { get; set; }
        public string? SerialNumber { get; set; }
        public string? Status { get; set; } // Status from Win32_DiskDrive
        public SmartStatusInfo? SmartStatus { get; set; }
        // Add property to indicate if this is the system disk (needs to be populated by collector/analysis)
        public bool? IsSystemDisk { get; set; }
    }

    public class SmartStatusInfo
    {
        public bool IsFailurePredicted { get; set; } // From MSStorageDriver_FailurePredictStatus
        public string? StatusText { get; set; } // e.g., "OK", "Failure Predicted", "Not Supported", "Error"
        public string? ReasonCode { get; set; } // Optional raw reason code from WMI
        public string? BasicStatusFromDiskDrive { get; set; } // Fallback status from Win32_DiskDrive.Status
        public string? Error { get; set; } // If querying failed
    }


    public class LogicalDiskInfo
    {
        public string? DeviceID { get; set; }
        public string? VolumeName { get; set; }
        public string? FileSystem { get; set; }
        public string? Size { get; set; }
        public string? FreeSpace { get; set; }
        public double? PercentFree { get; set; }
        public ulong? SizeBytes { get; set; } // Raw value if needed
        public ulong? FreeSpaceBytes { get; set; } // Raw value if needed
    }

    public class VolumeInfo
    {
        public string? Name { get; set; }
        public string? DeviceID { get; set; } // GUID
        public string? DriveLetter { get; set; }
        public string? FileSystem { get; set; }
        public string? Capacity { get; set; }
        public string? FreeSpace { get; set; }
        public bool? IsBitLockerProtected { get; set; }
        public string? ProtectionStatus { get; set; } // Needs Win32_EncryptableVolume WMI
    }

    public class GpuInfo
    {
        public string? Name { get; set; }
        public string? Vram { get; set; } // Reported VRAM
        public string? DriverVersion { get; set; }
        public DateTime? DriverDate { get; set; } // Added for analysis
        public string? VideoProcessor { get; set; }
        public string? CurrentResolution { get; set; }
        public uint? CurrentHorizontalResolution { get; set; } // Raw value for easier Win11 check
        public uint? CurrentVerticalResolution { get; set; } // Raw value for easier Win11 check
        public string? Status { get; set; }
        // public string? WddmVersion { get; set; } // Difficult to get reliably via WMI/basic APIs
    }

    public class MonitorInfo
    {
        public string? Name { get; set; }
        public string? DeviceID { get; set; }
        public string? Manufacturer { get; set; }
        public string? ReportedResolution { get; set; }
        public uint? ScreenWidth { get; set; } // Raw value for easier Win11 check
        public uint? ScreenHeight { get; set; } // Raw value for easier Win11 check
        public string? PpiLogical { get; set; }
        // public double? DiagonalSizeInches { get; set; } // Difficult to get reliably
    }

    public class AudioDeviceInfo
    {
        public string? Name { get; set; }
        public string? ProductName { get; set; }
        public string? Manufacturer { get; set; }
        public string? Status { get; set; }
    }


    public class SoftwareInfo : DiagnosticSection
    {
        public List<InstalledApplicationInfo>? InstalledApplications { get; set; } = new();
        public List<WindowsUpdateInfo>? WindowsUpdates { get; set; } = new();
        public List<ServiceInfo>? RelevantServices { get; set; } = new();
        public List<StartupProgramInfo>? StartupPrograms { get; set; } = new();
        public Dictionary<string, string>? SystemEnvironmentVariables { get; set; }
        public Dictionary<string, string>? UserEnvironmentVariables { get; set; }
    }

    public class InstalledApplicationInfo
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Publisher { get; set; }
        public string? InstallLocation { get; set; }
        public DateTime? InstallDate { get; set; }
    }

    public class WindowsUpdateInfo
    {
        public string? HotFixID { get; set; }
        public string? Description { get; set; }
        public DateTime? InstalledOn { get; set; }
    }

    public class ServiceInfo
    {
        public string? Name { get; set; }
        public string? DisplayName { get; set; }
        public string? State { get; set; } // Running, Stopped, Paused etc.
        public string? StartMode { get; set; } // Auto, Manual, Disabled
        public string? PathName { get; set; }
        public string? Status { get; set; } // OK, Degraded etc.
    }

    public class StartupProgramInfo
    {
        public string? Location { get; set; } // e.g., Registry Run key, Startup Folder
        public string? Name { get; set; }
        public string? Command { get; set; }
    }

    public class SecurityInfo : DiagnosticSection
    {
        public bool? IsAdmin { get; set; }
        public string? UacStatus { get; set; }
        public AntivirusInfo? Antivirus { get; set; }
        public FirewallInfo? Firewall { get; set; }
        public List<UserAccountInfo>? LocalUsers { get; set; } = new();
        public List<GroupInfo>? LocalGroups { get; set; } = new();
        public List<ShareInfo>? NetworkShares { get; set; } = new();
        public TpmInfo? Tpm { get; set; } // Added for Win11 TPM check
        public bool? IsSecureBootEnabled { get; set; } // Added for Win11 Secure Boot check
        public string? BiosMode { get; set; } // Example: "Legacy", "UEFI". Can be determined via Secure Boot status or other means.
    }

    public class AntivirusInfo
    {
        public string? Name { get; set; }
        public string? State { get; set; }
        public string? RawProductState { get; set; }
    }

    public class FirewallInfo
    {
        public string? Name { get; set; }
        public string? State { get; set; }
        public string? RawProductState { get; set; }
    }

    public class UserAccountInfo
    {
        public string? Name { get; set; }
        public string? FullName { get; set; }
        public string? SID { get; set; }
        public bool IsDisabled { get; set; }
        public bool IsLocal { get; set; }
        public bool PasswordRequired { get; set; }
        public bool PasswordChangeable { get; set; }
    }

    public class GroupInfo
    {
        public string? Name { get; set; }
        public string? SID { get; set; }
        public string? Description { get; set; }
    }

    public class ShareInfo
    {
        public string? Name { get; set; }
        public string? Path { get; set; }
        public string? Description { get; set; }
        public uint? Type { get; set; } // Disk Drive, Print Queue etc.
    }

    // NEW: Added for TPM Check (Win11)
    public class TpmInfo
    {
        public bool? IsPresent { get; set; } = false; // Default to false
        public bool? IsEnabled { get; set; } = false;
        public bool? IsActivated { get; set; } = false;
        public string? ManufacturerVersion { get; set; }
        public string? ManufacturerIdTxt { get; set; }
        public string? SpecVersion { get; set; } // Key for Win11 check (e.g., "2.0")
        public string? Status { get; set; } // General status message ("Ready", "Not Ready", "Error", etc.)
        public string? ErrorMessage { get; set; } // If collection failed
    }


    public class PerformanceInfo : DiagnosticSection
    {
        public string? OverallCpuUsagePercent { get; set; }
        public string? AvailableMemoryMB { get; set; }
        public string? TotalDiskQueueLength { get; set; }
        public List<ProcessUsageInfo>? TopMemoryProcesses { get; set; } = new();
        public List<ProcessUsageInfo>? TopCpuProcesses { get; set; } = new();
    }

    public class ProcessUsageInfo
    {
        public int Pid { get; set; }
        public string? Name { get; set; }
        public string? MemoryUsage { get; set; } // Formatted string
        public double? CpuUsagePercent { get; set; } // Requires Performance Counter access
        public string? Status { get; set; } // Running, Not Responding, etc.
        public string? Error { get; set; } // If data couldn't be retrieved
    }


    public class NetworkInfo : DiagnosticSection
    {
        public List<NetworkAdapterDetail>? Adapters { get; set; } = new();
        public List<ActivePortInfo>? ActiveTcpListeners { get; set; } = new();
        public List<ActivePortInfo>? ActiveUdpListeners { get; set; } = new();
        public List<TcpConnectionInfo>? ActiveTcpConnections { get; set; } = new(); // Added
        public NetworkTestResults? ConnectivityTests { get; set; }
    }

    public class NetworkAdapterDetail // Using System.Net.NetworkInformation
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Id { get; set; }
        public int? InterfaceIndex { get; set; } // Added
        public NetworkInterfaceType Type { get; set; }
        public OperationalStatus Status { get; set; }
        public string? MacAddress { get; set; }
        public long SpeedMbps { get; set; }
        public bool IsReceiveOnly { get; set; }
        public List<string>? IpAddresses { get; set; } = new();
        public List<string>? Gateways { get; set; } = new();
        public List<string>? DnsServers { get; set; } = new();
        public string? DnsSuffix { get; set; } // Added
        public List<string>? WinsServers { get; set; } // Added
        public DateTime? DriverDate { get; set; } // Added for analysis - requires WMI/Registry lookup
        public bool DhcpEnabled { get; set; } // May need WMI fallback
        public DateTime? DhcpLeaseObtained { get; set; } // May need WMI
        public DateTime? DhcpLeaseExpires { get; set; } // May need WMI
        public string? WmiServiceName { get; set; } // From WMI
    }

    public class ActivePortInfo
    {
        public string? Protocol { get; set; } // TCP/UDP
        public string? LocalAddress { get; set; }
        public int LocalPort { get; set; }
        // --- PID/Process lookup requires complex P/Invoke ---
        public int? OwningPid { get; set; } = null; // Set to null or actual PID if lookup implemented
        public string? OwningProcessName { get; set; } = "N/A (Lookup Not Implemented)"; // Indicate status
        public string? Error { get; set; } // If PID lookup failed or wasn't attempted
    }

    // Added for GetActiveTcpConnections
    public class TcpConnectionInfo
    {
        public string? LocalAddress { get; set; }
        public int LocalPort { get; set; }
        public string? RemoteAddress { get; set; }
        public int RemotePort { get; set; }
        public TcpState State { get; set; }
        // --- PID/Process lookup requires complex P/Invoke (GetExtendedTcpTable) ---
        public int? OwningPid { get; set; } = null;
        public string? OwningProcessName { get; set; } = "N/A (Lookup Not Implemented)";
        public string? Error { get; set; }
    }

    public class NetworkTestResults
    {
        public PingResult? GatewayPing { get; set; }
        public List<PingResult>? DnsPings { get; set; } = new();
        public DnsResolutionResult? DnsResolution { get; set; } // Added
        public List<TracerouteHop>? TracerouteResults { get; set; } // Only if requested
        public string? TracerouteTarget { get; set; }
    }

    public class PingResult
    {
        public string? Target { get; set; }
        public string? Status { get; set; } // IPStatus string or custom status like "Error", "Not Found"
        public long? RoundtripTimeMs { get; set; }
        public string? ResolvedIpAddress { get; set; } // IP address resolved from target hostname
        public string? Error { get; set; }
    }

    // Added
    public class DnsResolutionResult
    {
        public string? Hostname { get; set; }
        public bool Success { get; set; }
        public List<string>? ResolvedIpAddresses { get; set; } = new();
        public long? ResolutionTimeMs { get; set; }
        public string? Error { get; set; }
    }

    // --- Moved from NetworkHelper.cs ---
    // --- Also added Error property ---
    public class TracerouteHop
    {
        public int Hop { get; set; }
        public string? Address { get; set; }
        public long? RoundtripTimeMs { get; set; }
        public string? Status { get; set; } // IPStatus string
        public string? Error { get; set; } // Store error message if hop failed unusually
    }


    public class EventLogInfo : DiagnosticSection
    {
        public List<EventEntry>? SystemLogEntries { get; set; } = new();
        public List<EventEntry>? ApplicationLogEntries { get; set; } = new();
    }

    public class EventEntry
    {
        public DateTime TimeGenerated { get; set; }
        public string? EntryType { get; set; } // Error, Warning, Information etc.
        public string? Source { get; set; }
        public long InstanceId { get; set; }
        public string? Message { get; set; }
    }


    // Main container for all data
    public class DiagnosticReport
    {
        public DateTime ReportTimestamp { get; set; } = DateTime.UtcNow;
        public bool RanAsAdmin { get; set; }
        public SystemInfo? System { get; set; }
        public HardwareInfo? Hardware { get; set; }
        public SoftwareInfo? Software { get; set; }
        public SecurityInfo? Security { get; set; }
        public PerformanceInfo? Performance { get; set; }
        public NetworkInfo? Network { get; set; }
        public EventLogInfo? Events { get; set; }
        public AnalysisSummary? Analysis { get; set; }
        // Include configuration used for the report
        public AppConfiguration? Configuration { get; set; }
    }

    // Analysis Results
    public class AnalysisSummary : DiagnosticSection // Inherit for consistency if analysis can have errors
    {
        public List<string> PotentialIssues { get; set; } = new(); // Use a class/struct for more detail?
        public List<string> Suggestions { get; set; } = new();
        public List<string> Info { get; set; } = new();
        public Windows11Readiness? Windows11Readiness { get; set; } // Specific section for Win11 results
        // --- Added Configuration property ---
        public AppConfiguration? Configuration { get; set; } // Store config used for this analysis
    }

    // NEW: Specific class to hold Windows 11 readiness check results
    public class Windows11Readiness
    {
        public bool? OverallResult { get; set; } // Null if checks incomplete, true if pass, false if fail
        public List<Win11CheckResult> Checks { get; set; } = new();
    }

    public class Win11CheckResult
    {
        public string Requirement { get; set; } = string.Empty;
        public string? Status { get; set; } // e.g., "Pass", "Fail", "Info", "Warning", "Check Manually", "Error"
        public string? Details { get; set; } // e.g., "CPU Speed: 3.2 GHz", "TPM 2.0 Not Found", "Verify on MS website"
        public string? ComponentChecked { get; set; } // e.g., "CPU", "TPM", "Storage"
    }

    #endregion


    #region Configuration Models (Example - for appsettings.json)

    /* Example appsettings.json structure:
    {
      "AppConfiguration": {
        "AnalysisThresholds": {
          "HighMemoryUsagePercent": 90.0,
          "ElevatedMemoryUsagePercent": 80.0,
          "CriticalDiskSpacePercent": 5.0,
          "LowDiskSpacePercent": 15.0,
          "HighCpuUsagePercent": 95.0,
          "ElevatedCpuUsagePercent": 80.0,
          "HighDiskQueueLength": 5.0,
          "MaxSystemLogErrorsIssue": 15,      // Increased from 10
          "MaxSystemLogErrorsSuggestion": 5,  // Increased from 3
          "MaxAppLogErrorsIssue": 20,         // Increased from 10
          "MaxAppLogErrorsSuggestion": 7,   // Increased from 3
          "MaxUptimeDaysSuggestion": 30,
          "DriverAgeWarningYears": 2,
          "MaxPingLatencyWarningMs": 100,
          "MaxTracerouteHopLatencyWarningMs": 150
        },
        "NetworkSettings": {
           "DefaultDnsTestHostname": "www.cloudflare.com" // Changed default
        }
      }
    }
    */

    public class AppConfiguration
    {
        public AnalysisThresholds AnalysisThresholds { get; set; } = new(); // Provide defaults
        public NetworkSettings NetworkSettings { get; set; } = new();
    }

    public class AnalysisThresholds
    {
        public double HighMemoryUsagePercent { get; set; } = 90.0;
        public double ElevatedMemoryUsagePercent { get; set; } = 80.0;
        public double CriticalDiskSpacePercent { get; set; } = 5.0;
        public double LowDiskSpacePercent { get; set; } = 15.0;
        public double HighCpuUsagePercent { get; set; } = 95.0;
        public double ElevatedCpuUsagePercent { get; set; } = 80.0;
        public double HighDiskQueueLength { get; set; } = 5.0;
        public int MaxSystemLogErrorsIssue { get; set; } = 15; // Example default change
        public int MaxSystemLogErrorsSuggestion { get; set; } = 5; // Example default change
        public int MaxAppLogErrorsIssue { get; set; } = 20; // Example default change
        public int MaxAppLogErrorsSuggestion { get; set; } = 7; // Example default change
        public int MaxUptimeDaysSuggestion { get; set; } = 30;
        public int DriverAgeWarningYears { get; set; } = 2; // Added
        public long MaxPingLatencyWarningMs { get; set; } = 100; // Added
        public long MaxTracerouteHopLatencyWarningMs { get; set; } = 150; // Added
    }

     public class NetworkSettings
     {
         public string DefaultDnsTestHostname { get; set; } = "www.cloudflare.com"; // Added, changed default
     }

    #endregion

}