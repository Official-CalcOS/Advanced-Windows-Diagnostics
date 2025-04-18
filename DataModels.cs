// DataModels.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations; // Required for validation attributes
using System.Net.NetworkInformation;
using System.Text.Json.Serialization;

// Keep all files in the same namespace for simplicity
namespace DiagnosticToolAllInOne
{
    #region Base and Core Models

    // Base class for common section properties
    public abstract class DiagnosticSection
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SectionCollectionErrorMessage { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? SpecificCollectionErrors { get; set; }

        public void AddSpecificError(string key, string message)
        {
            SpecificCollectionErrors ??= new Dictionary<string, string>();
            // Log if overwriting? For now, just overwrite.
            SpecificCollectionErrors[key] = message;
        }
    }

    // Main report container
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
        public StabilityInfo? Stability { get; set; }
        public AnalysisSummary? Analysis { get; set; }
        // Include the configuration used for the report generation
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public AppConfiguration? Configuration { get; set; }
    }

    #endregion

    #region System Information Models

    public class SystemInfo : DiagnosticSection
    {
        public OSInfo? OperatingSystem { get; set; }
        public ComputerSystemInfo? ComputerSystem { get; set; }
        public BiosInfo? BIOS { get; set; }
        public BaseboardInfo? Baseboard { get; set; }
        public string? DotNetVersion { get; set; }
        public TimeZoneConfig? TimeZone { get; set; }
        public PowerPlanInfo? ActivePowerPlan { get; set; }
        public SystemIntegrityInfo? SystemIntegrity { get; set; }
        public bool? IsRebootPending { get; set; } // Changed from string
    }

    public class OSInfo
    {
        public string? Name { get; set; }
        public string? Architecture { get; set; }
        public string? Version { get; set; }
        public string? BuildNumber { get; set; }
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
        public string? CurrentUser { get; set; } // User running the tool
        public string? LoggedInUserWMI { get; set; } // User logged into the console session per WMI
    }

    public class BiosInfo
    {
        public string? Manufacturer { get; set; }
        public string? Version { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public string? SerialNumber { get; set; } // Often requires admin
    }

    public class BaseboardInfo
    {
        public string? Manufacturer { get; set; }
        public string? Product { get; set; }
        public string? SerialNumber { get; set; } // Often requires admin
        public string? Version { get; set; }
    }

    public class TimeZoneConfig
    {
        public string? CurrentTimeZone { get; set; } // e.g., "(UTC+10:00) Brisbane"
        public string? StandardName { get; set; } // e.g., "E. Australia Standard Time"
        public string? DaylightName { get; set; } // e.g., "E. Australia Daylight Time"
        public int? BiasMinutes { get; set; } // Offset from UTC in minutes
    }

    public class PowerPlanInfo
    {
        public string? Name { get; set; }
        public string? InstanceID { get; set; } // GUID
        public bool IsActive { get; set; }
    }

    // *** UPDATED SystemIntegrityInfo ***
    public class SystemIntegrityInfo
    {
        // SFC Scan Info
        public bool? SfcLogFound { get; set; }
        public string? SfcScanResult { get; set; } // Summary: "No Violations", "Repaired", "Unrepairable", "Unknown", "Not Run Recently", "Error Parsing"
        public bool? SfcCorruptionFound { get; set; }
        public bool? SfcRepairsSuccessful { get; set; }
        public DateTime? LastSfcScanTime { get; set; } // End time of the last detected scan in the log

        // DISM Scan Info
        public bool? DismLogFound { get; set; }
        public string? DismCheckHealthResult { get; set; } // Summary: "No Corruption", "Repairable", "Not Repairable", "Unknown", "Not Run Recently", "Error Parsing"
        public bool? DismCorruptionDetected { get; set; } // Based on CheckHealth/ScanHealth
        public bool? DismStoreRepairable { get; set; } // Based on CheckHealth/ScanHealth
        public DateTime? LastDismCheckTime { get; set; } // End time of the last relevant DISM operation in the log

        // Errors
        public string? LogParsingError { get; set; } // Global error during log access/parsing
    }

    #endregion

    #region Hardware Information Models

    public class HardwareInfo : DiagnosticSection
    {
        public List<ProcessorInfo> Processors { get; set; } = new();
        public MemoryInfo? Memory { get; set; }
        public List<PhysicalDiskInfo> PhysicalDisks { get; set; } = new();
        public List<LogicalDiskInfo> LogicalDisks { get; set; } = new();
        public List<VolumeInfo> Volumes { get; set; } = new();
        public List<GpuInfo> Gpus { get; set; } = new();
        public List<MonitorInfo> Monitors { get; set; } = new();
        public List<AudioDeviceInfo> AudioDevices { get; set; } = new();

        // Removed specific error properties - use SpecificCollectionErrors
        [JsonIgnore] public string? ProcessorErrorMessage { set { if(value != null) AddSpecificError("Processor", value); } }
        [JsonIgnore] public string? PhysicalDiskErrorMessage { set { if(value != null) AddSpecificError("PhysicalDisk", value); } }
        [JsonIgnore] public string? LogicalDiskErrorMessage { set { if(value != null) AddSpecificError("LogicalDisk", value); } }
        [JsonIgnore] public string? VolumeErrorMessage { set { if(value != null) AddSpecificError("Volume", value); } }
        [JsonIgnore] public string? GpuErrorMessage { set { if(value != null) AddSpecificError("GPU", value); } }
        [JsonIgnore] public string? MonitorErrorMessage { set { if(value != null) AddSpecificError("Monitor", value); } }
        [JsonIgnore] public string? AudioErrorMessage { set { if(value != null) AddSpecificError("Audio", value); } }
    }

    public class ProcessorInfo
    {
        public string? Name { get; set; }
        public string? Socket { get; set; }
        public uint? Cores { get; set; }
        public uint? LogicalProcessors { get; set; }
        public uint? MaxSpeedMHz { get; set; }
        public ulong? L2CacheSizeKB { get; set; } // Store raw KB value
        public ulong? L3CacheSizeKB { get; set; } // Store raw KB value
    }

    public class MemoryInfo
    {
        public double? PercentUsed { get; set; } // Calculated percentage
        public ulong? TotalVisibleMemoryKB { get; set; } // Raw KB value from WMI
        public ulong? AvailableMemoryKB { get; set; } // Raw KB value from WMI
        public List<MemoryModuleInfo> Modules { get; set; } = new();
    }

    public class MemoryModuleInfo
    {
        public string? DeviceLocator { get; set; } // e.g., "ChannelA-DIMM0"
        public ulong? CapacityBytes { get; set; } // Store raw bytes
        public uint? SpeedMHz { get; set; }
        public string? MemoryType { get; set; } // Description from FormatHelper
        public string? FormFactor { get; set; } // Description from FormatHelper
        public string? BankLabel { get; set; }
        public string? Manufacturer { get; set; }
        public string? PartNumber { get; set; }
    }

    public class PhysicalDiskInfo
    {
        public uint Index { get; set; }
        public string? Model { get; set; }
        public string? MediaType { get; set; } // e.g., "Fixed hard disk media"
        public string? InterfaceType { get; set; } // e.g., "SCSI", "NVMe"
        public ulong? SizeBytes { get; set; } // Store raw bytes
        public uint? Partitions { get; set; }
        public string? SerialNumber { get; set; } // Often requires admin
        public string? Status { get; set; } // e.g., "OK"
        public SmartStatusInfo? SmartStatus { get; set; }
        public bool? IsSystemDisk { get; set; } // Flag if this is the OS disk
    }

    public class SmartStatusInfo
    {
        public bool IsFailurePredicted { get; set; } // True if SMART predicts failure
        public string? StatusText { get; set; } // e.g., "OK", "Failure Predicted", "Not Supported"
        public string? ReasonCode { get; set; } // WMI reason code if failure predicted
        public string? BasicStatusFromDiskDrive { get; set; } // Status from Win32_DiskDrive
        public string? Error { get; set; } // Error message if SMART query failed
    }

    public class LogicalDiskInfo
    {
        public string? DeviceID { get; set; } // e.g., "C:"
        public string? VolumeName { get; set; }
        public string? FileSystem { get; set; } // e.g., "NTFS"
        public double? PercentFree { get; set; } // Calculated percentage
        public ulong? SizeBytes { get; set; } // Store raw bytes
        public ulong? FreeSpaceBytes { get; set; } // Store raw bytes
    }

    public class VolumeInfo
    {
        public string? Name { get; set; } // Volume label
        public string? DeviceID { get; set; } // GUID, e.g., "\\?\Volume{...}\"
        public string? DriveLetter { get; set; } // e.g., "C:"
        public string? FileSystem { get; set; }
        public ulong? CapacityBytes { get; set; } // Store raw bytes
        public ulong? FreeSpaceBytes { get; set; } // Store raw bytes
        public bool? IsBitLockerProtected { get; set; } // Derived from ProtectionStatus
        public string? ProtectionStatus { get; set; } // e.g., "Protection On", "Protection Off", "Requires Admin"
    }

    public class GpuInfo
    {
        public string? Name { get; set; }
        public ulong? AdapterRAMBytes { get; set; } // Store raw bytes
        public string? DriverVersion { get; set; }
        public DateTime? DriverDate { get; set; }
        public string? VideoProcessor { get; set; } // e.g., "NVIDIA GeForce RTX 3080"
        public uint? CurrentHorizontalResolution { get; set; }
        public uint? CurrentVerticalResolution { get; set; }
        public uint? CurrentRefreshRate { get; set; }
        public string? Status { get; set; } // WMI Status (e.g., "OK")
        public string? WddmVersion { get; set; } // Added for driver model info
    }

    public class MonitorInfo
    {
        public string? Name { get; set; } // e.g., "Generic PnP Monitor"
        public string? DeviceID { get; set; } // Monitor Device ID from WMI
        public string? PnpDeviceID { get; set; } // PNPDeviceID from WMI
        public string? Manufacturer { get; set; }
        public uint? ScreenWidth { get; set; } // Reported max width
        public uint? ScreenHeight { get; set; } // Reported max height
        public uint? PixelsPerXLogicalInch { get; set; }
        public uint? PixelsPerYLogicalInch { get; set; }
        public double? DiagonalSizeInches { get; set; } // Calculated if possible
    }

    public class AudioDeviceInfo
    {
        public string? Name { get; set; } // e.g., "Realtek High Definition Audio"
        public string? ProductName { get; set; }
        public string? Manufacturer { get; set; }
        public string? Status { get; set; } // e.g., "OK"
    }

    #endregion

    #region Software Information Models

    public class SoftwareInfo : DiagnosticSection
    {
        public List<InstalledApplicationInfo> InstalledApplications { get; set; } = new();
        public List<WindowsUpdateInfo> WindowsUpdates { get; set; } = new();
        public List<ServiceInfo> RelevantServices { get; set; } = new();
        public List<StartupProgramInfo> StartupPrograms { get; set; } = new();
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
        public string? HotFixID { get; set; } // e.g., "KB5034122"
        public string? Description { get; set; }
        public DateTime? InstalledOn { get; set; }
    }

    public class ServiceInfo
    {
        public string? Name { get; set; } // Service name (e.g., "Winmgmt")
        public string? DisplayName { get; set; } // Friendly name (e.g., "Windows Management Instrumentation")
        public string? State { get; set; } // e.g., "Running", "Stopped"
        public string? StartMode { get; set; } // e.g., "Auto", "Manual", "Disabled"
        public string? PathName { get; set; } // Path to executable (may need admin)
        public string? Status { get; set; } // WMI Status (e.g., "OK")
    }

    public class StartupProgramInfo
    {
        public string? Location { get; set; } // e.g., "Reg:HKLM\Run", "Folder:Common Startup"
        public string? Name { get; set; } // Name of registry value or filename
        public string? Command { get; set; } // Command line or file path
    }

    #endregion

    #region Security Information Models

    public class SecurityInfo : DiagnosticSection
    {
        public bool? IsAdmin { get; set; }
        public string? UacStatus { get; set; } // e.g., "Enabled", "Disabled", "Requires Admin"
        public AntivirusInfo? Antivirus { get; set; }
        public FirewallInfo? Firewall { get; set; }
        public List<UserAccountInfo> LocalUsers { get; set; } = new();
        public List<GroupInfo> LocalGroups { get; set; } = new();
        public List<ShareInfo> NetworkShares { get; set; } = new();
        public TpmInfo? Tpm { get; set; }
        public bool? IsSecureBootEnabled { get; set; } // Status from registry
        public string? BiosMode { get; set; } // Inferred based on Secure Boot status (e.g., "UEFI", "Legacy")
    }

    public class AntivirusInfo
    {
        public string? Name { get; set; }
        public string? State { get; set; } // Parsed state (e.g., "Enabled and Up-to-date", "Disabled")
        public string? RawProductState { get; set; } // Raw value from WMI SecurityCenter2
    }

    public class FirewallInfo
    {
        public string? Name { get; set; }
        public string? State { get; set; } // Parsed state
        public string? RawProductState { get; set; } // Raw value from WMI SecurityCenter2
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
        public uint? Type { get; set; } // 0=DiskDrive, 1=PrintQueue, 2=Device, 3=IPC, 2147483648=DiskDriveAdmin, ...
    }

    public class TpmInfo
    {
        public bool? IsPresent { get; set; } = false; // If TPM Win32_Tpm class instance exists
        public bool? IsEnabled { get; set; } = false; // From WMI IsEnabled_InitialValue
        public bool? IsActivated { get; set; } = false; // From WMI IsActivated_InitialValue
        public string? ManufacturerVersion { get; set; }
        public string? ManufacturerIdTxt { get; set; }
        public string? SpecVersion { get; set; }
        public string? Status { get; set; } // e.g., "Ready", "Not Present", "Requires Admin", "Error Querying"
        public string? ErrorMessage { get; set; } // Specific error if Status is "Error Querying"
    }

    #endregion

    #region Performance Information Models

    public class PerformanceInfo : DiagnosticSection
    {
        // Store counter values as strings as returned by helper (which handles formatting/errors)
        public string? OverallCpuUsagePercent { get; set; }
        public string? AvailableMemoryMB { get; set; }
        public string? TotalDiskQueueLength { get; set; }
        public List<ProcessUsageInfo> TopMemoryProcesses { get; set; } = new();
        public List<ProcessUsageInfo> TopCpuProcesses { get; set; } = new();
    }

    public class ProcessUsageInfo
    {
        public int Pid { get; set; }
        public string? Name { get; set; }
        // Removed MemoryUsage (string) - format from WorkingSetBytes in presentation
        public long? WorkingSetBytes { get; set; } // Raw working set size in bytes
        public double? CpuUsagePercent { get; set; } // NOTE: This is typically not accurate for a snapshot
        public long? TotalProcessorTimeMs { get; set; } // Accumulated CPU time
        public string? Status { get; set; } // e.g., "Running", "Exited", "Inaccessible"
        public string? Error { get; set; } // Error message if data retrieval failed
    }
    #endregion

    #region Network Information Models

    public class NetworkInfo : DiagnosticSection
    {
        public List<NetworkAdapterDetail> Adapters { get; set; } = new();
        public List<ActivePortInfo> ActiveTcpListeners { get; set; } = new();
        public List<ActivePortInfo> ActiveUdpListeners { get; set; } = new();
        public List<TcpConnectionInfo> ActiveTcpConnections { get; set; } = new();
        public NetworkTestResults? ConnectivityTests { get; set; }
    }

    public class NetworkAdapterDetail
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Id { get; set; }
        public int? InterfaceIndex { get; set; }
        public string? PnpDeviceID { get; set; } // From WMI Win32_NetworkAdapter
        public NetworkInterfaceType Type { get; set; }
        public OperationalStatus Status { get; set; }
        public string? MacAddress { get; set; }
        public long SpeedMbps { get; set; } // Speed in Mbps
        public bool IsReceiveOnly { get; set; }
        public List<string> IpAddresses { get; set; } = new();
        public List<string> Gateways { get; set; } = new();
        public List<string> DnsServers { get; set; } = new();
        public string? DnsSuffix { get; set; }
        public List<string> WinsServers { get; set; } = new();
        public DateTime? DriverDate { get; set; } // From WMI Win32_NetworkAdapter
        public bool DhcpEnabled { get; set; } // From WMI Win32_NetworkAdapterConfiguration
        public DateTime? DhcpLeaseObtained { get; set; }
        public DateTime? DhcpLeaseExpires { get; set; }
        public string? WmiServiceName { get; set; } // Service name from WMI Config
    }

    public class ActivePortInfo // Used for both TCP and UDP listeners
    {
        public string? Protocol { get; set; } // "TCP" or "UDP"
        public string? LocalAddress { get; set; }
        public int LocalPort { get; set; }
        public int? OwningPid { get; set; }
        public string? OwningProcessName { get; set; } // May be "Requires Admin", "Lookup Failed", "PID Not Found", or name
        public string? Error { get; set; } // If PID lookup had issues
    }

    public class TcpConnectionInfo
    {
        public string? LocalAddress { get; set; }
        public int LocalPort { get; set; }
        public string? RemoteAddress { get; set; }
        public int RemotePort { get; set; }
        public TcpState State { get; set; }
        public int? OwningPid { get; set; }
        public string? OwningProcessName { get; set; }
        public string? Error { get; set; }
    }

    public class NetworkTestResults
    {
        public PingResult? GatewayPing { get; set; }
        public List<PingResult> DnsPings { get; set; } = new();
        public DnsResolutionResult? DnsResolution { get; set; }
        public List<TracerouteHop>? TracerouteResults { get; set; } = new(); // Store list of hops
        public string? TracerouteTarget { get; set; } // Hostname/IP traced
    }

    public class PingResult
    {
        public string? Target { get; set; }
        public string? Status { get; set; } // e.g., "Success", "TimedOut"
        public long? RoundtripTimeMs { get; set; }
        public string? ResolvedIpAddress { get; set; } // IP used for ping if target was hostname
        public string? Error { get; set; } // Error message if ping failed
    }

    public class DnsResolutionResult
    {
        public string? Hostname { get; set; }
        public bool Success { get; set; }
        public List<string> ResolvedIpAddresses { get; set; } = new();
        public long? ResolutionTimeMs { get; set; }
        public string? Error { get; set; }
    }

    public class TracerouteHop
    {
        public int Hop { get; set; }
        public string? Address { get; set; } // IP address of the hop, or "*" if timeout
        public long? RoundtripTimeMs { get; set; } // Null if timeout or error
        public string? Status { get; set; } // e.g., "Success", "TimedOut", "TtlExpired"
        public string? Error { get; set; } // Specific error message if applicable
    }

    #endregion

    #region Event Log Models

    public class EventLogInfo : DiagnosticSection
    {
        public List<EventEntry> SystemLogEntries { get; set; } = new();
        public List<EventEntry> ApplicationLogEntries { get; set; } = new();
    }

    public class EventEntry
    {
        public DateTime TimeGenerated { get; set; }
        public string? EntryType { get; set; } // "Error", "Warning", "Information", etc.
        public string? Source { get; set; } // e.g., "Application Error", "disk"
        public long InstanceId { get; set; } // Event ID
        public string? Message { get; set; } // Trimmed message content
    }

    #endregion

    #region Stability Models (Crash Dumps)

    public class StabilityInfo : DiagnosticSection
    {
        public List<CrashDumpInfo> RecentCrashDumps { get; set; } = new();
    }

    public class CrashDumpInfo
    {
        public string? FileName { get; set; }
        public string? FilePath { get; set; }
        public DateTime? Timestamp { get; set; } // Last write time
        public long? FileSizeBytes { get; set; } // Store raw bytes
    }

    #endregion

    #region Analysis Models

    public class AnalysisSummary : DiagnosticSection
    {
        public List<string> PotentialIssues { get; set; } = new();
        public List<string> Suggestions { get; set; } = new();
        public List<string> Info { get; set; } = new();
        public Windows11Readiness? Windows11Readiness { get; set; }
        // Removed Configuration property - Use the one in DiagnosticReport
        public List<CriticalEventDetails> CriticalEventsFound { get; set; } = new(); // Specific list for critical events
    }

    public class Windows11Readiness // Example analysis component
    {
        public bool? OverallResult { get; set; } // True = Pass, False = Fail, Null = Incomplete/Error
        public List<Win11CheckResult> Checks { get; set; } = new();
    }

    public class Win11CheckResult
    {
        public string Requirement { get; set; } = string.Empty;
        public string? Status { get; set; } // e.g., "Pass", "Fail", "Check Manually"
        public string? Details { get; set; } // Current value or reason for failure
        public string? ComponentChecked { get; set; } // e.g., "CPU", "TPM", "Secure Boot"
    }

    public class CriticalEventDetails
    {
        public DateTime Timestamp { get; set; }
        public string? Source { get; set; }
        public long EventID { get; set; }
        public string? MessageExcerpt { get; set; } // First part of the message
        public string? LogName { get; set; } // "System" or "Application"
    }

    #endregion

    #region Configuration Models

    // Main configuration container, usually bound from appsettings.json
    public class AppConfiguration
    {
        [Required(ErrorMessage = "AnalysisThresholds section is required in configuration.")]
        public AnalysisThresholds AnalysisThresholds { get; set; } = new();

        [Required(ErrorMessage = "NetworkSettings section is required in configuration.")]
        public NetworkSettings NetworkSettings { get; set; } = new();
    }

    // Thresholds used by the AnalysisEngine
    public class AnalysisThresholds
    {
        [Range(0.0, 100.0)] public double HighMemoryUsagePercent { get; set; } = 90.0;
        [Range(0.0, 100.0)] public double ElevatedMemoryUsagePercent { get; set; } = 80.0;
        [Range(0.0, 100.0)] public double CriticalDiskSpacePercent { get; set; } = 5.0;
        [Range(0.0, 100.0)] public double LowDiskSpacePercent { get; set; } = 15.0;
        [Range(0.0, 100.0)] public double HighCpuUsagePercent { get; set; } = 90.0;
        [Range(0.0, 100.0)] public double ElevatedCpuUsagePercent { get; set; } = 75.0;
        [Range(0.0, 100.0)] public double HighDiskQueueLength { get; set; } = 5.0;
        [Range(0, 1000)] public int MaxSystemLogErrorsIssue { get; set; } = 15;
        [Range(0, 1000)] public int MaxSystemLogErrorsSuggestion { get; set; } = 5;
        [Range(0, 1000)] public int MaxAppLogErrorsIssue { get; set; } = 100;
        [Range(0, 1000)] public int MaxAppLogErrorsSuggestion { get; set; } = 7;
        [Range(1, 365)] public int MaxUptimeDaysSuggestion { get; set; } = 30; // Changed default
        [Range(0, 10)] public int DriverAgeWarningYears { get; set; } = 2; // Changed default
        [Range(1, 10000)] public long MaxPingLatencyWarningMs { get; set; } = 150; // Increased default
        [Range(1, 10000)] public long MaxTracerouteHopLatencyWarningMs { get; set; } = 200; // Increased default

        // Note: KnownCriticalEventIDs moved directly into AnalysisEngine for simpler configuration management
    }

    // Network related settings
    public class NetworkSettings
    {
        [Required(AllowEmptyStrings = false)]
        [StringLength(253, MinimumLength = 3)]
        // Basic regex for hostname/IP - may need refinement for edge cases
        [RegularExpression(@"^(([a-zA-Z0-9]|[a-zA-Z0-9][a-zA-Z0-9\-]*[a-zA-Z0-9])\.)*([A-Za-z0-9]|[A-Za-z0-9][A-Za-z0-9\-]*[A-Za-z0-9])$|^((25[0-5]|(2[0-4]|1\d|[1-9]|)\d)\.?\b){4}$|\[([a-fA-F0-9:]+)\]", ErrorMessage = "Invalid hostname or IP address format.")]
        public string DefaultDnsTestHostname { get; set; } = "www.google.com"; // Changed default

         // Could add other network settings like default traceroute target, timeouts etc.
    }

    #endregion
}