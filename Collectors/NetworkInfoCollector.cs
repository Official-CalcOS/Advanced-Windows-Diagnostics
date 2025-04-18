// Collectors/NetworkInfoCollector.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using DiagnosticToolAllInOne.Helpers; // Ensure all helpers are referenced

namespace DiagnosticToolAllInOne.Collectors
{
    [SupportedOSPlatform("windows")]
    public static class NetworkInfoCollector
    {
        private const string WMI_CIMV2 = @"root\cimv2";
        private const int AF_INET = 2; // IPv4

        // Removed static cache - will fetch WMI data within CollectAsync

        public static async Task<NetworkInfo> CollectAsync(string? tracerouteTarget, string? dnsTestTarget)
        {
            var netInfo = new NetworkInfo();
            netInfo.Adapters = new List<NetworkAdapterDetail>(); // Initialize lists
            netInfo.ActiveTcpListeners = new List<ActivePortInfo>();
            netInfo.ActiveUdpListeners = new List<ActivePortInfo>();
            netInfo.ActiveTcpConnections = new List<TcpConnectionInfo>();
            netInfo.ConnectivityTests = new NetworkTestResults();

            bool isAdmin = AdminHelper.IsRunningAsAdmin(); // Check once
            bool pidLookupAttempted = false; // Track if we tried PID lookups
            bool pidLookupSuccess = false; // Track if lookup succeeded without critical errors
            Dictionary<string, uint> tcpConnectionPids = new();
            Dictionary<string, uint> udpListenerPids = new();
            Dictionary<string, WmiNetworkAdapterData>? wmiNicDataCache = null; // Fetch within this call

            // --- Perform PID Lookup (Requires Admin) ---
            if (isAdmin)
            {
                pidLookupAttempted = true; // Mark that we tried
                try
                {
                    Logger.LogDebug("Attempting TCP/UDP PID lookup...");
                    tcpConnectionPids = PInvokeHelper.GetTcpConnectionPids();
                    udpListenerPids = PInvokeHelper.GetUdpListenerPids();
                    pidLookupSuccess = true;
                    Logger.LogDebug($"PID lookup successful. TCP: {tcpConnectionPids.Count}, UDP: {udpListenerPids.Count}");
                }
                catch (System.ComponentModel.Win32Exception winEx)
                {
                    pidLookupSuccess = false;
                    netInfo.AddSpecificError("PIDLookup_Error", $"Failed (Win32 Error {winEx.NativeErrorCode}): {winEx.Message}");
                    Logger.LogWarning($"Network PID lookup failed (Win32 Error {winEx.NativeErrorCode})", winEx);
                }
                catch (Exception ex)
                {
                    pidLookupSuccess = false;
                    netInfo.AddSpecificError("PIDLookup_Error", $"Failed (General Error): {ex.Message}");
                    Logger.LogError("Network PID lookup failed", ex);
                }
            }
            else
            {
                pidLookupAttempted = false; // Explicitly false if not admin
                pidLookupSuccess = false;
                netInfo.AddSpecificError("PIDLookup_Skipped", "Requires Admin");
                Logger.LogInfo("PID lookup skipped: Requires Admin privileges.");
            }

            // --- Pre-fetch WMI Network Adapter Data (Driver Date/PnP ID) ---
            try
            {
                 Logger.LogDebug("Fetching WMI Network Adapter data (Win32_NetworkAdapter)...");
                 wmiNicDataCache = GetWmiNetworkAdapterData(netInfo); // Pass netInfo to add errors directly
                 Logger.LogDebug($"Fetched WMI data for {wmiNicDataCache.Count} adapters.");
            }
            catch(Exception ex)
            {
                netInfo.AddSpecificError("WMI_DriverDate_Overall", $"Failed fetching Win32_NetworkAdapter: {ex.Message}");
                Logger.LogError("Failed to pre-fetch WMI Network Adapter data", ex);
                 wmiNicDataCache = new Dictionary<string, WmiNetworkAdapterData>(); // Ensure it's not null
            }


            try // Wrap the main collection logic
            {
                // --- Get WMI Network Config (for DHCP/Lease info) ---
                List<WmiNetworkConfig>? wmiConfigInfo = null;
                try
                {
                    Logger.LogDebug("Fetching WMI Network Configuration data (Win32_NetworkAdapterConfiguration)...");
                    wmiConfigInfo = GetWmiNetworkConfig();
                    if ((wmiConfigInfo?.Count ?? 0) == 0 && !isAdmin)
                    {
                        netInfo.AddSpecificError("WMIConfig_Access", "WMI Network Config access likely requires Administrator privileges for detailed info.");
                    }
                    else if ((wmiConfigInfo?.Count ?? 0) == 0 && isAdmin)
                    {
                        netInfo.AddSpecificError("WMIConfig_NotFound", "WMI query for Win32_NetworkAdapterConfiguration returned no results where IPEnabled=True. Check WMI service health.");
                    }
                    Logger.LogDebug($"Fetched WMI config for {wmiConfigInfo?.Count ?? 0} IP-enabled adapters.");
                }
                catch (Exception wmiEx)
                {
                    netInfo.AddSpecificError("WMIConfig_Error", $"Failed to get WMI Network Configuration data: {wmiEx.Message}");
                     Logger.LogWarning("Failed to get WMI Network Configuration data", wmiEx);
                     wmiConfigInfo = new List<WmiNetworkConfig>(); // Ensure list is not null
                }


                // --- Get Network Interfaces (.NET API) ---
                NetworkInterface[] adapters = Array.Empty<NetworkInterface>();
                try
                {
                    Logger.LogDebug("Getting network interfaces via NetworkInterface.GetAllNetworkInterfaces()...");
                    adapters = NetworkInterface.GetAllNetworkInterfaces();
                    Logger.LogDebug($"Found {adapters.Length} network interfaces.");
                }
                catch (NetworkInformationException netEx)
                {
                    netInfo.SectionCollectionErrorMessage = $"Critical Error: Failed to get network interfaces: {netEx.Message} (Code: {netEx.ErrorCode})";
                    Logger.LogError($"[CRITICAL NETWORK ERROR] Get All Network Interfaces failed", netEx);
                    return netInfo; // Cannot proceed without adapters
                }
                catch (Exception ex)
                {
                    netInfo.SectionCollectionErrorMessage = $"Critical Error: Unexpected error getting network interfaces: {ex.Message}";
                    Logger.LogError($"[CRITICAL NETWORK ERROR] Get All Network Interfaces failed", ex);
                    return netInfo;
                }

                // --- Process Each Adapter ---
                foreach (var adapter in adapters)
                {
                    if (adapter == null) continue; // Skip null adapters (shouldn't happen with GetAll...)

                    string adapterIdForError = $"{adapter.Name ?? "Unknown"} ({adapter.Id ?? "?"})"; // More unique ID for error logs
                    Logger.LogDebug($"Processing adapter: {adapterIdForError}");
                    var adapterDetail = new NetworkAdapterDetail // Initialize detail object
                    {
                         Name = adapter.Name,
                         Description = adapter.Description,
                         Id = adapter.Id,
                         Type = adapter.NetworkInterfaceType,
                         Status = adapter.OperationalStatus,
                         SpeedMbps = -1, // Default
                         IsReceiveOnly = adapter.IsReceiveOnly,
                         DhcpEnabled = false // Default
                    };

                    try
                    {
                        if (!adapterDetail.IsReceiveOnly && adapter.Speed > 0)
                        {
                             adapterDetail.SpeedMbps = adapter.Speed / 1_000_000;
                        }

                        try { adapterDetail.MacAddress = adapter.GetPhysicalAddress()?.ToString(); }
                        catch (NotSupportedException) { adapterDetail.MacAddress = "N/A (Not Supported)"; }
                        catch (Exception macEx)
                        {
                            adapterDetail.MacAddress = "Error";
                            netInfo.AddSpecificError($"MAC_{adapterIdForError}", $"Error getting MAC: {macEx.GetType().Name}");
                            Logger.LogWarning($"Error getting MAC for '{adapterIdForError}'", macEx);
                        }

                        // Match WMI driver/PnP data using description (primary key from .NET is Description)
                        WmiNetworkAdapterData? wmiNicMatch = FindWmiNicData(wmiNicDataCache, adapter.Description);
                        adapterDetail.PnpDeviceID = wmiNicMatch?.PnpDeviceID;
                        adapterDetail.DriverDate = wmiNicMatch?.DriverDate;

                        // Get IP Properties only if Up or Loopback
                        if (adapter.OperationalStatus == OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        {
                            IPInterfaceProperties? ipProps = null;
                            try { ipProps = adapter.GetIPProperties(); }
                            catch (NotSupportedException) { netInfo.AddSpecificError($"IPProps_{adapterIdForError}", "Info: IP properties not supported."); }
                            catch (NetworkInformationException ipEx) { netInfo.AddSpecificError($"IPProps_{adapterIdForError}", $"Warning: Network error getting IP properties: {ipEx.Message}"); Logger.LogWarning($"Network error getting IP props for {adapterIdForError}", ipEx); }
                            catch (Exception ipEx) { netInfo.AddSpecificError($"IPProps_{adapterIdForError}", $"Warning: Error getting IP properties: {ipEx.GetType().Name}"); Logger.LogWarning($"Error getting IP props for {adapterIdForError}", ipEx); }

                            if (ipProps != null)
                            {
                                adapterDetail.IpAddresses = ipProps.UnicastAddresses?.Select(ip => ip?.Address?.ToString()).Where(s => s != null).ToList() ?? new List<string>();
                                adapterDetail.Gateways = ipProps.GatewayAddresses?.Select(gw => gw?.Address?.ToString()).Where(s => s != null).ToList() ?? new List<string>();
                                adapterDetail.DnsServers = ipProps.DnsAddresses?.Select(dns => dns?.ToString()).Where(s => s != null).ToList() ?? new List<string>();
                                adapterDetail.DnsSuffix = ipProps.DnsSuffix;
                                adapterDetail.WinsServers = ipProps.WinsServersAddresses?.Select(wins => wins?.ToString()).Where(s => s != null).ToList() ?? new List<string>();

                                try { adapterDetail.InterfaceIndex = ipProps.GetIPv4Properties()?.Index ?? ipProps.GetIPv6Properties()?.Index ?? -1; }
                                catch { adapterDetail.InterfaceIndex = -1; }

                                // Match WMI Config Data (DHCP) using MAC address
                                if (wmiConfigInfo != null && !string.IsNullOrEmpty(adapterDetail.MacAddress) && !adapterDetail.MacAddress.StartsWith("N/A") && !adapterDetail.MacAddress.StartsWith("Error"))
                                {
                                    var macToCompare = adapterDetail.MacAddress.Replace("-", "").Replace(":", "");
                                    var wmiConfMatch = wmiConfigInfo.FirstOrDefault(w => w.MACAddress?.Replace("-", "").Replace(":", "") == macToCompare);
                                    if (wmiConfMatch != null)
                                    {
                                        adapterDetail.DhcpEnabled = wmiConfMatch.DHCPEnabled;
                                        adapterDetail.DhcpLeaseObtained = wmiConfMatch.DHCPLeaseObtained;
                                        adapterDetail.DhcpLeaseExpires = wmiConfMatch.DHCPLeaseExpires;
                                        adapterDetail.WmiServiceName = wmiConfMatch.ServiceName; // Get service name from config entry
                                    }
                                    else { adapterDetail.DhcpEnabled = false; Logger.LogDebug($"No WMI config match for MAC {macToCompare} ({adapterIdForError})"); }
                                }
                                else { adapterDetail.DhcpEnabled = false; }

                                if (adapterDetail.DriverDate == null && !(netInfo.SpecificCollectionErrors?.ContainsKey("WMI_DriverDate_Overall") ?? false))
                                {
                                    // Add info only if WMI query didn't fail globally and we just couldn't find a match
                                    netInfo.AddSpecificError($"DriverDate_{adapterIdForError}", "Info: Driver date could not be determined via WMI.");
                                    Logger.LogDebug($"Could not find matching WMI driver date for '{adapterIdForError}'.");
                                }
                            }
                        }
                        netInfo.Adapters.Add(adapterDetail); // Add populated detail object
                        Logger.LogDebug($"Finished processing adapter: {adapterIdForError}");
                    }
                    // Catch errors specific to processing a single adapter
                    catch (NotSupportedException nse) { netInfo.AddSpecificError($"Adapter_{adapterIdForError}", $"Warning: Skipping adapter due to unsupported operation: {nse.Message}"); Logger.LogWarning($"Skipping adapter '{adapterIdForError}' due to unsupported operation", nse); }
                    catch (NetworkInformationException nie) { netInfo.AddSpecificError($"Adapter_{adapterIdForError}", $"Warning: Skipping adapter due to network error: {nie.Message} (Code: {nie.ErrorCode})"); Logger.LogWarning($"Skipping adapter '{adapterIdForError}' due to network error", nie); }
                    catch (Exception ex) { netInfo.AddSpecificError($"Adapter_{adapterIdForError}", $"Warning: Skipping adapter due to unexpected error: {ex.GetType().Name}"); Logger.LogError($"Skipping adapter '{adapterIdForError}' due to unexpected error", ex); }
                } // End foreach adapter


                // --- Active Listeners & Connections ---
                try
                {
                    Logger.LogDebug("Getting active TCP/UDP listeners and connections...");
                    var ipGlobalProps = IPGlobalProperties.GetIPGlobalProperties();

                    // TCP Listeners
                    var tcpListeners = ipGlobalProps.GetActiveTcpListeners();
                    Logger.LogDebug($"Processing {tcpListeners?.Length ?? 0} TCP Listeners.");
                    foreach (var listener in tcpListeners ?? Enumerable.Empty<IPEndPoint>()) // Handle potential null
                    {
                        if (listener == null) continue;
                        var portInfo = CreateActivePortInfo("TCP", listener, tcpConnectionPids, udpListenerPids, pidLookupSuccess, isAdmin, netInfo);
                        if (portInfo != null) netInfo.ActiveTcpListeners.Add(portInfo);
                        else Logger.LogWarning($"CreateActivePortInfo returned null for TCP listener {listener?.Address}:{listener?.Port}");
                    }

                    // UDP Listeners
                    var udpListeners = ipGlobalProps.GetActiveUdpListeners();
                    Logger.LogDebug($"Processing {udpListeners?.Length ?? 0} UDP Listeners.");
                    foreach (var listener in udpListeners ?? Enumerable.Empty<IPEndPoint>()) // Handle potential null
                    {
                        if (listener == null) continue;
                        var portInfo = CreateActivePortInfo("UDP", listener, tcpConnectionPids, udpListenerPids, pidLookupSuccess, isAdmin, netInfo);
                        if (portInfo != null) netInfo.ActiveUdpListeners.Add(portInfo);
                         else Logger.LogWarning($"CreateActivePortInfo returned null for UDP listener {listener?.Address}:{listener?.Port}");
                    }

                    // TCP Connections
                    var tcpConnections = ipGlobalProps.GetActiveTcpConnections();
                     Logger.LogDebug($"Processing {tcpConnections?.Length ?? 0} TCP Connections.");
                    foreach (var conn in tcpConnections ?? Enumerable.Empty<TcpConnectionInformation>()) // Handle potential null
                    {
                        if (conn == null) continue; // Check for null connection info
                        netInfo.ActiveTcpConnections.Add(CreateTcpConnectionInfo(conn, tcpConnectionPids, pidLookupSuccess, isAdmin, netInfo));
                    }
                }
                catch (NetworkInformationException nie) { netInfo.AddSpecificError("PortConnections_Error", $"Error getting active ports/connections: {nie.Message} (Code: {nie.ErrorCode})"); Logger.LogWarning("Error getting active ports/connections", nie); }
                catch (Exception ex) { netInfo.AddSpecificError("PortConnections_Error", $"Error getting active ports/connections: {ex.Message}"); Logger.LogError("Error getting active ports/connections", ex); }


                // --- Connectivity Tests ---
                 Logger.LogDebug("Starting connectivity tests...");
                try
                {
                    string? gatewayToPing = netInfo.Adapters?
                                                .Where(a => a != null && a.Status == OperationalStatus.Up && (a.Gateways?.Any(gw => !string.IsNullOrWhiteSpace(gw) && gw != "0.0.0.0") ?? false))
                                                .SelectMany(a => a.Gateways!) // Should be non-null due to Any()
                                                .FirstOrDefault(gw => IPAddress.TryParse(gw, out _));

                    if (!string.IsNullOrEmpty(gatewayToPing)) {
                        Logger.LogDebug($"Pinging Default Gateway: {gatewayToPing}");
                        netInfo.ConnectivityTests.GatewayPing = await NetworkHelper.PerformPingAsync(gatewayToPing);
                    }
                    else {
                        Logger.LogDebug("Default Gateway not found for ping test.");
                        netInfo.ConnectivityTests.GatewayPing = new PingResult { Target = "Default Gateway", Status = "Not Found", Error="No valid gateway found on active adapters." };
                        netInfo.AddSpecificError("Ping_Gateway", "Info: No valid default gateway found to ping.");
                    }

                    string[] dnsServersToPing = { "8.8.8.8", "1.1.1.1" }; // Common public DNS
                    netInfo.ConnectivityTests.DnsPings = new List<PingResult>();
                    foreach (string dns in dnsServersToPing) {
                        Logger.LogDebug($"Pinging DNS Server: {dns}");
                        netInfo.ConnectivityTests.DnsPings.Add(await NetworkHelper.PerformPingAsync(dns));
                    }

                    if (!string.IsNullOrWhiteSpace(dnsTestTarget)) {
                        Logger.LogDebug($"Testing DNS resolution for: {dnsTestTarget}");
                        netInfo.ConnectivityTests.DnsResolution = await NetworkHelper.PerformDnsResolutionAsync(dnsTestTarget);
                    } else {
                        netInfo.ConnectivityTests.DnsResolution = new DnsResolutionResult { Hostname = dnsTestTarget ?? "Default", Success = false, Error = "No DNS test hostname specified." };
                        netInfo.AddSpecificError("DNS_Resolution", "Info: No DNS test hostname specified.");
                        Logger.LogDebug("DNS resolution test skipped (no target specified).");
                    }

                    if (!string.IsNullOrWhiteSpace(tracerouteTarget)) {
                        Logger.LogDebug($"Performing Traceroute to: {tracerouteTarget}");
                        netInfo.ConnectivityTests.TracerouteTarget = tracerouteTarget;
                        netInfo.ConnectivityTests.TracerouteResults = await NetworkHelper.PerformTracerouteAsync(tracerouteTarget);
                    } else {
                        Logger.LogDebug("Traceroute skipped (no target specified).");
                        // Optionally add info: netInfo.AddSpecificError("Traceroute", "Info: No traceroute target specified.");
                    }
                     Logger.LogDebug("Connectivity tests finished.");
                }
                catch (Exception connEx) {
                    netInfo.AddSpecificError("ConnectivityTests_Error", $"Error during connectivity tests: {connEx.Message}");
                    Logger.LogError("Error during connectivity tests", connEx);
                }
            }
            catch (Exception ex) // Catch errors in overall collection setup
            {
                netInfo.SectionCollectionErrorMessage = $"Unexpected Error during Network Collection: {ex.Message}";
                Logger.LogError($"[CRITICAL NETWORK ERROR] Overall collection failed", ex);
            }

            return netInfo;
        }

        // --- WMI Helper Call ---
        private static List<WmiNetworkConfig> GetWmiNetworkConfig()
        {
            var list = new List<WmiNetworkConfig>();
            string? wmiError = null;

            WmiHelper.ProcessWmiResults(
                 WmiHelper.Query("Win32_NetworkAdapterConfiguration", new[] { "MACAddress", "DHCPEnabled", "DHCPLeaseObtained", "DHCPLeaseExpires", "ServiceName" }, WMI_CIMV2, "IPEnabled = True"),
                 obj => {
                     // Check if obj is null before accessing properties
                     if (obj == null) return;

                     list.Add(new WmiNetworkConfig {
                         MACAddress = WmiHelper.GetProperty(obj, "MACAddress", ""), // Default to empty string if null
                         DHCPEnabled = bool.TryParse(WmiHelper.GetProperty(obj, "DHCPEnabled", "false"), out bool dhcp) && dhcp,
                         DHCPLeaseObtained = WmiHelper.ConvertCimDateTime(WmiHelper.GetProperty(obj, "DHCPLeaseObtained")),
                         DHCPLeaseExpires = WmiHelper.ConvertCimDateTime(WmiHelper.GetProperty(obj, "DHCPLeaseExpires")),
                         ServiceName = WmiHelper.GetProperty(obj, "ServiceName", "") // Default to empty string if null
                     });
                 },
                 error => wmiError = error
            );

            if (wmiError != null)
            {
                 Logger.LogWarning($"WMI Network Config query (Win32_NetworkAdapterConfiguration) failed: {wmiError}");
                 throw new Exception($"WMI Network Config query failed: {wmiError}"); // Throw to indicate failure
            }
            return list;
        }

        // Private helper class for WMI config data
        private class WmiNetworkConfig
        {
            public string? MACAddress;
            public bool DHCPEnabled;
            public DateTime? DHCPLeaseObtained;
            public DateTime? DHCPLeaseExpires;
            public string? ServiceName;
        }

        // Private helper class for WMI adapter data
        private class WmiNetworkAdapterData
        {
            public string? PnpDeviceID { get; set; }
            public DateTime? DriverDate { get; set; }
            public string? Description { get; set; }
        }

        // Helper to pre-fetch WMI Driver Dates and PnPDeviceIDs
        private static Dictionary<string, WmiNetworkAdapterData> GetWmiNetworkAdapterData(NetworkInfo netInfo) // Pass netInfo
        {
            var wmiData = new Dictionary<string, WmiNetworkAdapterData>(StringComparer.OrdinalIgnoreCase); // Use Description as key (case-insensitive)
            string? wmiError = null;

            // Query adapters that have a NetConnectionID (usually physical/visible adapters) and a PnPDeviceID
            WmiHelper.ProcessWmiResults(
                WmiHelper.Query("Win32_NetworkAdapter", new[] { "PNPDeviceID", "DriverDate", "Description", "NetConnectionID" }, WMI_CIMV2, "NetConnectionID IS NOT NULL AND PNPDeviceID IS NOT NULL"),
                obj => {
                    if (obj == null) return;
                    string? description = WmiHelper.GetProperty(obj, "Description", null);
                    if (!string.IsNullOrEmpty(description))
                    {
                        // Use TryAdd for potential duplicate Descriptions (though less likely than PnpID)
                        wmiData.TryAdd(description, new WmiNetworkAdapterData {
                             PnpDeviceID = WmiHelper.GetProperty(obj, "PNPDeviceID", null),
                             DriverDate = WmiHelper.ConvertCimDateTime(WmiHelper.GetProperty(obj, "DriverDate")),
                             Description = description // Store the key itself
                         });
                    }
                },
                error => wmiError = error
            );

            if (wmiError != null)
            {
                netInfo.AddSpecificError("DriverDate_WMI_Error", $"Warning: Failed to query Win32_NetworkAdapter: {wmiError}");
                 Logger.LogWarning($"Failed to query Win32_NetworkAdapter for driver dates: {wmiError}");
            }
             if (wmiData.Count == 0 && wmiError == null) {
                  netInfo.AddSpecificError("DriverDate_WMI_NotFound", "Warning: WMI query for Win32_NetworkAdapter returned no results with NetConnectionID/PNPDeviceID.");
                  Logger.LogDebug("WMI query for Win32_NetworkAdapter returned no results for driver date pre-fetch.");
             }
            return wmiData;
        }

        // Helper to find WMI NIC Data from Cache based on Description
        private static WmiNetworkAdapterData? FindWmiNicData(Dictionary<string, WmiNetworkAdapterData>? cache, string? description)
        {
             if (string.IsNullOrEmpty(description) || cache == null || cache.Count == 0) return null;
             // Use case-insensitive lookup on the dictionary keyed by Description
             cache.TryGetValue(description, out var data);
             return data;
        }


        // Helper to Create ActivePortInfo (Uses PID lookup) - Added netInfo parameter
        private static ActivePortInfo? CreateActivePortInfo(string protocol, IPEndPoint? listener,
            Dictionary<string, uint> tcpPidLookup, Dictionary<string, uint> udpPidLookup,
            bool lookupSucceeded, bool isAdmin, NetworkInfo netInfo)
        {
            if (listener?.Address == null) // Simplified null check
            {
                Logger.LogWarning($"CreateActivePortInfo received null listener or listener address for protocol {protocol}.");
                return null;
            }

            var info = new ActivePortInfo
            {
                Protocol = protocol,
                LocalAddress = listener.Address.ToString(),
                LocalPort = listener.Port,
                OwningPid = null, // Default to null
                OwningProcessName = "N/A", // Default
                Error = null
            };
            string endpointKey = $"{info.LocalAddress}:{info.LocalPort}"; // Key for error reporting

            if (!isAdmin)
            {
                info.OwningProcessName = "Requires Admin";
                info.Error = "Requires Admin"; // Set error if admin is needed but not available
            }
            else if (!lookupSucceeded && netInfo.SpecificCollectionErrors?.ContainsKey("PIDLookup_Error") == true) // Check if lookup failed specifically
            {
                info.OwningProcessName = "Lookup Failed";
                info.Error = "PID lookup failed"; // Set error if lookup failed
            }
            else if (lookupSucceeded) // Only try getting PID if admin and lookup succeeded
            {
                string key = $"{info.LocalAddress}:{info.LocalPort}";
                var lookupToUse = (protocol.Equals("UDP", StringComparison.OrdinalIgnoreCase)) ? udpPidLookup : tcpPidLookup;

                if (lookupToUse.TryGetValue(key, out uint pid))
                {
                    info.OwningPid = (int)pid;
                    info.OwningProcessName = PInvokeHelper.GetProcessName((int)pid); // Get name (handles errors internally)
                    // Check for specific errors from GetProcessName and report them
                    if (info.OwningProcessName == "Access Denied" || info.OwningProcessName == "Lookup Error" || info.OwningProcessName == "Process Exited")
                    {
                        info.Error = info.OwningProcessName;
                        netInfo.AddSpecificError($"PID_Lookup_{protocol}_{endpointKey}", info.Error); // Add specific error to report
                    }
                    else if (string.IsNullOrEmpty(info.OwningProcessName))
                    {
                        info.OwningProcessName = "PID Not Found"; // Case where GetProcessById fails for other reasons
                    }
                }
                else
                {
                    info.OwningProcessName = "PID Not Found"; // PID wasn't in the table
                }
            }

            return info;
        }


        // Helper to Create TcpConnectionInfo (Uses PID lookup) - Added netInfo parameter
        private static TcpConnectionInfo CreateTcpConnectionInfo(TcpConnectionInformation? conn, // Allow null conn
            Dictionary<string, uint> pidLookup, bool lookupSucceeded, bool isAdmin, NetworkInfo netInfo)
        {
            // Initialize with defaults, check for null conn early
            var info = new TcpConnectionInfo
            {
                LocalAddress = conn?.LocalEndPoint?.Address?.ToString() ?? "Unknown",
                LocalPort = conn?.LocalEndPoint?.Port ?? 0,
                RemoteAddress = conn?.RemoteEndPoint?.Address?.ToString() ?? "Unknown",
                RemotePort = conn?.RemoteEndPoint?.Port ?? 0,
                State = conn?.State ?? TcpState.Unknown,
                OwningPid = null,
                OwningProcessName = "N/A",
                Error = null
            };

            // Return early if essential connection info is missing
            if (info.LocalAddress == "Unknown" || info.RemoteAddress == "Unknown" || conn == null) {
                 info.Error = "Invalid connection data";
                 return info;
            }

            string endpointKey = $"{info.LocalAddress}:{info.LocalPort}-{info.RemoteAddress}:{info.RemotePort}"; // Key for error reporting

            if (!isAdmin)
            {
                info.OwningProcessName = "Requires Admin";
                info.Error = "Requires Admin";
            }
            else if (!lookupSucceeded && netInfo.SpecificCollectionErrors?.ContainsKey("PIDLookup_Error") == true)
            {
                info.OwningProcessName = "Lookup Failed";
                info.Error = "PID lookup failed";
            }
            else if (lookupSucceeded)
            {
                // Use the specific key format for connections
                string key = $"{info.LocalAddress}:{info.LocalPort}-{info.RemoteAddress}:{info.RemotePort}";

                if (pidLookup.TryGetValue(key, out uint pid))
                {
                    info.OwningPid = (int)pid;
                    info.OwningProcessName = PInvokeHelper.GetProcessName((int)pid);
                    if (info.OwningProcessName == "Access Denied" || info.OwningProcessName == "Lookup Error" || info.OwningProcessName == "Process Exited")
                    {
                        info.Error = info.OwningProcessName;
                        netInfo.AddSpecificError($"PID_Lookup_TCPConn_{endpointKey}", info.Error);
                    }
                     else if (string.IsNullOrEmpty(info.OwningProcessName))
                    {
                        info.OwningProcessName = "PID Not Found";
                    }
                }
                else
                {
                    info.OwningProcessName = "PID Not Found";
                }
            }
            return info;
        }
    }
}