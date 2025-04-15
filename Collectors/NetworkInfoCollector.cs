// Collectors/NetworkInfoCollector.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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

        public static async Task<NetworkInfo> CollectAsync(string? tracerouteTarget, string? dnsTestTarget)
        {
            var netInfo = new NetworkInfo();
            netInfo.Adapters = new();
            netInfo.ActiveTcpListeners = new();
            netInfo.ActiveUdpListeners = new();
            netInfo.ActiveTcpConnections = new(); // Initialize new list
            netInfo.ConnectivityTests = new NetworkTestResults();

            try
            {
                // --- Get WMI Network Config (for DHCP/Lease info) ---
                List<WmiNetworkConfig>? wmiAdapterInfo = null;
                try
                {
                    wmiAdapterInfo = GetWmiNetworkConfig();
                    if(wmiAdapterInfo.Count == 0 && !AdminHelper.IsRunningAsAdmin())
                    {
                         netInfo.AddSpecificError("WMIConfig", "WMI Network Config access likely requires Administrator privileges.");
                    }
                     else if(wmiAdapterInfo.Count == 0)
                    {
                         netInfo.AddSpecificError("WMIConfig", "Failed to retrieve any WMI Network Adapter Configuration details (IPEnabled=True).");
                    }
                }
                catch (Exception wmiEx)
                {
                     netInfo.AddSpecificError("WMIConfig", $"Warning: Failed to get WMI Network Configuration data: {wmiEx.Message}");
                }

                // --- Get Network Interfaces (.NET API) ---
                NetworkInterface[] adapters = Array.Empty<NetworkInterface>();
                try
                {
                     adapters = NetworkInterface.GetAllNetworkInterfaces();
                }
                catch(NetworkInformationException netEx)
                {
                     netInfo.SectionCollectionErrorMessage = $"Critical Error: Failed to get network interfaces: {netEx.Message} (Code: {netEx.ErrorCode})";
                     // Optionally log the exception details here
                     Console.Error.WriteLine($"[CRITICAL NETWORK ERROR] Get All Network Interfaces failed: {netEx}");
                     return netInfo; // Cannot proceed without adapters
                }
                 catch(Exception ex)
                {
                     netInfo.SectionCollectionErrorMessage = $"Critical Error: Unexpected error getting network interfaces: {ex.Message}";
                     Console.Error.WriteLine($"[CRITICAL NETWORK ERROR] Get All Network Interfaces failed: {ex}");
                     return netInfo;
                }

                // --- Process Each Adapter ---
                foreach (var adapter in adapters)
                {
                    NetworkAdapterDetail? adapterDetail = null;
                    try
                    {
                        adapterDetail = new NetworkAdapterDetail { };
                         adapterDetail.Name = adapter.Name;
                         adapterDetail.Description = adapter.Description;
                         adapterDetail.Id = adapter.Id; // GUID
                         adapterDetail.Type = adapter.NetworkInterfaceType;
                         adapterDetail.Status = adapter.OperationalStatus;
                         adapterDetail.SpeedMbps = adapter.IsReceiveOnly ? -1 : adapter.Speed / 1_000_000;
                         adapterDetail.IsReceiveOnly = adapter.IsReceiveOnly;

                        // Get MAC Address
                        try { adapterDetail.MacAddress = adapter.GetPhysicalAddress()?.ToString(); }
                        catch (NotSupportedException) { adapterDetail.MacAddress = "N/A (Not Supported)"; }
                        catch (Exception macEx) {
                            adapterDetail.MacAddress = $"Error";
                             netInfo.AddSpecificError($"MAC-{adapter.Name}", $"Error getting MAC for '{adapter.Name}': {macEx.GetType().Name}");
                        }

                        // Get IP Properties (Only if Up or Loopback)
                        if (adapter.OperationalStatus == OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        {
                             IPInterfaceProperties? ipProps = null;
                             try { ipProps = adapter.GetIPProperties(); }
                             catch (NotSupportedException) { netInfo.AddSpecificError($"IPProps-{adapter.Name}", $"Info: IP properties not supported for adapter '{adapter.Name}'."); }
                             catch (NetworkInformationException ipEx) { netInfo.AddSpecificError($"IPProps-{adapter.Name}", $"Warning: Network error getting IP properties for '{adapter.Name}': {ipEx.Message}"); }
                             catch (Exception ipEx) { netInfo.AddSpecificError($"IPProps-{adapter.Name}", $"Warning: Error getting IP properties for '{adapter.Name}': {ipEx.GetType().Name} - {ipEx.Message}"); }

                             if (ipProps != null)
                             {
                                 // --- Get IP Addresses, Gateways, DNS Servers ---
                                 adapterDetail.IpAddresses = ipProps.UnicastAddresses?
                                     .Select(ip => ip.Address.ToString()).ToList() ?? new List<string>();
                                 adapterDetail.Gateways = ipProps.GatewayAddresses?
                                     .Select(gw => gw.Address.ToString()).ToList() ?? new List<string>();
                                 adapterDetail.DnsServers = ipProps.DnsAddresses?
                                     .Select(dns => dns.ToString()).ToList() ?? new List<string>(); // Use ToString() for IPAddress

                                 // --- Get DNS Suffix, WINS Servers, Interface Index ---
                                 adapterDetail.DnsSuffix = ipProps.DnsSuffix;
                                 adapterDetail.WinsServers = ipProps.WinsServersAddresses?
                                     .Select(wins => wins.ToString()).ToList() ?? new List<string>(); // Use ToString() for IPAddress

                                 try {
                                     // Prefer IPv4 index if available
                                     adapterDetail.InterfaceIndex = ipProps.GetIPv4Properties()?.Index;
                                     if (adapterDetail.InterfaceIndex == null || adapterDetail.InterfaceIndex <= 0) // Check if IPv4 index is valid
                                     {
                                         adapterDetail.InterfaceIndex = ipProps.GetIPv6Properties()?.Index;
                                     }
                                     if (adapterDetail.InterfaceIndex == null || adapterDetail.InterfaceIndex <= 0) // Check if IPv6 index is valid
                                     {
                                         adapterDetail.InterfaceIndex = -1; // Indicate failure or not found
                                     }
                                 }
                                 catch { adapterDetail.InterfaceIndex = -1; /* Handle potential platform specific issues */ }


                                 // --- Correlate with WMI data for DHCP/Lease ---
                                 if (wmiAdapterInfo != null && !string.IsNullOrEmpty(adapterDetail.MacAddress) && !adapterDetail.MacAddress.StartsWith("N/A") && !adapterDetail.MacAddress.StartsWith("Error"))
                                 {
                                     var macToCompare = adapterDetail.MacAddress?.Replace("-", "").Replace(":", "");
                                     if (!string.IsNullOrEmpty(macToCompare))
                                     {
                                         var wmiMatch = wmiAdapterInfo.FirstOrDefault(w => w.MACAddress?.Replace("-", "").Replace(":", "") == macToCompare);
                                         if (wmiMatch != null)
                                         {
                                             adapterDetail.DhcpEnabled = wmiMatch.DHCPEnabled;
                                             adapterDetail.DhcpLeaseObtained = wmiMatch.DHCPLeaseObtained;
                                             adapterDetail.DhcpLeaseExpires = wmiMatch.DHCPLeaseExpires;
                                             adapterDetail.WmiServiceName = wmiMatch.ServiceName;
                                         } else {
                                             // If no WMI match found for an active adapter, assume DHCP not enabled via WMI context
                                             adapterDetail.DhcpEnabled = false;
                                             // Add note only if WMI query itself didn't fail
                                             if (!(netInfo.SpecificCollectionErrors?.ContainsKey("WMIConfig") ?? false)) {
                                                 // Avoid adding too many errors if WMI just didn't return much
                                                 // netInfo.AddSpecificError($"DHCP-{adapter.Name}", $"Info: No WMI match found for MAC {adapterDetail.MacAddress}. DHCP status assumed false.");
                                             }
                                         }
                                     }
                                 } else if (wmiAdapterInfo == null) {
                                     // WMI failed entirely, cannot determine DHCP status
                                     adapterDetail.DhcpEnabled = false; // Default to false, error logged earlier
                                 }

                                  // --- Get Driver Date (Placeholder - Requires WMI/Registry) ---
                                  // This requires querying Win32_NetworkAdapter with WHERE InterfaceIndex=adapterDetail.InterfaceIndex
                                  // Or potentially registry lookups under HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}\<NNNN>
                                  adapterDetail.DriverDate = null; // Placeholder
                                  // Only add error if it's a physical adapter, not virtual/loopback etc.
                                  if (adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet || adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || adapter.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet) {
                                       netInfo.AddSpecificError($"DriverDate-{adapter.Name}", "Info: Driver date lookup not implemented.");
                                  }
                             }
                        }
                        netInfo.Adapters.Add(adapterDetail);
                    }
                    // Catch errors specific to processing a single adapter
                    catch (NotSupportedException nse) { netInfo.AddSpecificError($"Adapter-{adapter?.Name ?? "Unknown"}", $"Warning: Skipping adapter '{adapter?.Name ?? "Unknown"}' due to unsupported operation: {nse.Message}"); }
                    catch (NetworkInformationException nie) { netInfo.AddSpecificError($"Adapter-{adapter?.Name ?? "Unknown"}", $"Warning: Skipping adapter '{adapter?.Name ?? "Unknown"}' due to network error: {nie.Message} (Code: {nie.ErrorCode})"); }
                    catch (Exception ex) { netInfo.AddSpecificError($"Adapter-{adapter?.Name ?? "Unknown"}", $"Warning: Skipping adapter '{adapter?.Name ?? "Unknown"}' due to unexpected error: {ex.GetType().Name} - {ex.Message}"); }
                }

                // --- Active Listeners (TCP/UDP) ---
                try
                {
                    var ipGlobalProps = IPGlobalProperties.GetIPGlobalProperties();
                    var tcpListeners = ipGlobalProps.GetActiveTcpListeners();
                    foreach (var listener in tcpListeners) { netInfo.ActiveTcpListeners.Add(CreateActivePortInfo("TCP", listener)); }
                }
                catch (NetworkInformationException nie) { netInfo.AddSpecificError("TCPListeners", $"Error getting TCP listeners: {nie.Message} (Code: {nie.ErrorCode})"); }
                catch (Exception ex) { netInfo.AddSpecificError("TCPListeners", $"Error getting TCP listeners: {ex.Message}"); }

                try
                {
                     var ipGlobalProps = IPGlobalProperties.GetIPGlobalProperties();
                     var udpListeners = ipGlobalProps.GetActiveUdpListeners();
                     foreach (var listener in udpListeners) { netInfo.ActiveUdpListeners.Add(CreateActivePortInfo("UDP", listener)); }
                }
                catch (NetworkInformationException nie) { netInfo.AddSpecificError("UDPListeners", $"Error getting UDP listeners: {nie.Message} (Code: {nie.ErrorCode})"); }
                catch (Exception ex) { netInfo.AddSpecificError("UDPListeners", $"Error getting UDP listeners: {ex.Message}"); }

                // --- Active TCP Connections ---
                try
                {
                     var ipGlobalProps = IPGlobalProperties.GetIPGlobalProperties();
                     var tcpConnections = ipGlobalProps.GetActiveTcpConnections();
                     foreach (var conn in tcpConnections) { netInfo.ActiveTcpConnections.Add(CreateTcpConnectionInfo(conn)); }
                }
                catch (NetworkInformationException nie) { netInfo.AddSpecificError("TCPConnections", $"Error getting TCP connections: {nie.Message} (Code: {nie.ErrorCode})"); }
                catch (Exception ex) { netInfo.AddSpecificError("TCPConnections", $"Error getting TCP connections: {ex.Message}"); }


                // --- Connectivity Tests ---
                 try
                 {
                    // Ping Default Gateway
                    string? gatewayToPing = netInfo.Adapters?
                                                .Where(a => a.Status == OperationalStatus.Up && (a.Gateways?.Any(gw => !string.IsNullOrWhiteSpace(gw) && gw != "0.0.0.0") ?? false))
                                                .SelectMany(a => a.Gateways!)
                                                .FirstOrDefault(gw => IPAddress.TryParse(gw, out _));

                    if (!string.IsNullOrEmpty(gatewayToPing)) { netInfo.ConnectivityTests.GatewayPing = await NetworkHelper.PerformPingAsync(gatewayToPing); }
                    else { netInfo.ConnectivityTests.GatewayPing = new PingResult { Target = "Default Gateway", Status = "Not Found" }; }

                    // Ping Public DNS Servers
                    string[] dnsServersToPing = { "8.8.8.8", "1.1.1.1" }; // Common public DNS
                    netInfo.ConnectivityTests.DnsPings = new();
                    foreach (string dns in dnsServersToPing) { netInfo.ConnectivityTests.DnsPings.Add(await NetworkHelper.PerformPingAsync(dns)); }

                    // Perform DNS Resolution Test
                    if (!string.IsNullOrWhiteSpace(dnsTestTarget))
                    {
                        netInfo.ConnectivityTests.DnsResolution = await NetworkHelper.PerformDnsResolutionAsync(dnsTestTarget);
                    }
                    else
                    {
                        netInfo.ConnectivityTests.DnsResolution = new DnsResolutionResult { Hostname = dnsTestTarget ?? "Default", Success=false, Error="No DNS test hostname specified." };
                    }


                    // Perform Traceroute (if requested)
                    if (!string.IsNullOrWhiteSpace(tracerouteTarget))
                    {
                         netInfo.ConnectivityTests.TracerouteTarget = tracerouteTarget;
                         // Assign the result directly - type should match now
                         netInfo.ConnectivityTests.TracerouteResults = await NetworkHelper.PerformTracerouteAsync(tracerouteTarget);
                    }
                 }
                 catch(Exception connEx)
                 {
                      netInfo.AddSpecificError("ConnectivityTests", $"Error during connectivity tests: {connEx.Message}");
                 }

            }
            catch(Exception ex) // Catch errors in overall collection setup
            {
                 netInfo.SectionCollectionErrorMessage = $"Unexpected Error during Network Collection: {ex.Message}";
                 Console.Error.WriteLine($"[CRITICAL NETWORK ERROR] Overall collection failed: {ex}");
            }

            return netInfo;
        }

        // --- WMI Helper Call ---
        private static List<WmiNetworkConfig> GetWmiNetworkConfig()
        {
            var list = new List<WmiNetworkConfig>();
            string? wmiError = null; // Store potential error message

            // Query Win32_NetworkAdapterConfiguration for adapters that have IP enabled
            WmiHelper.ProcessWmiResults(
                 WmiHelper.Query(
                    "Win32_NetworkAdapterConfiguration",
                    new[] { "MACAddress", "DHCPEnabled", "DHCPLeaseObtained", "DHCPLeaseExpires", "ServiceName" },
                    WMI_CIMV2,
                    "IPEnabled = True" // Only get adapters that are IP-enabled
                 ),
                 // Action to process each WMI object found
                 obj => {
                     list.Add(new WmiNetworkConfig {
                         MACAddress = WmiHelper.GetProperty(obj, "MACAddress"),
                         DHCPEnabled = bool.TryParse(WmiHelper.GetProperty(obj, "DHCPEnabled"), out bool dhcp) && dhcp,
                         DHCPLeaseObtained = WmiHelper.ConvertCimDateTime(WmiHelper.GetProperty(obj, "DHCPLeaseObtained")),
                         DHCPLeaseExpires = WmiHelper.ConvertCimDateTime(WmiHelper.GetProperty(obj, "DHCPLeaseExpires")),
                         ServiceName = WmiHelper.GetProperty(obj, "ServiceName")
                     });
                 },
                 // Action to handle errors during the WMI query
                 error => wmiError = error // Store the error message
            );

            // If an error occurred during WMI query, log it and return an empty list
            if (wmiError != null)
            {
                 Console.Error.WriteLine($"[WARN] WMI Network Config query (Win32_NetworkAdapterConfiguration where IPEnabled=True) failed: {wmiError}");
                 // Optionally, throw an exception or handle it based on severity
                 return new List<WmiNetworkConfig>(); // Return empty list on error
            }
            return list; // Return the populated list
        }

        // Private helper class for WMI data (simple structure)
        private class WmiNetworkConfig
        {
            public string? MACAddress;
            public bool DHCPEnabled;
            public DateTime? DHCPLeaseObtained;
            public DateTime? DHCPLeaseExpires;
            public string? ServiceName;
        }


        // --- Helper to Create ActivePortInfo ---
        private static ActivePortInfo CreateActivePortInfo(string protocol, IPEndPoint listener)
        {
            var info = new ActivePortInfo
            {
                Protocol = protocol,
                LocalAddress = listener.Address.ToString(),
                LocalPort = listener.Port
                // PID and Process Name require P/Invoke (GetExtendedTcpTable/GetExtendedUdpTable)
                // OwningPid = PInvokeHelper.GetPidForPort(protocol, listener.Port), // Example call
                // OwningProcessName = PInvokeHelper.GetProcessName(OwningPid)      // Example call
            };
            // Add error handling here if PID lookup is implemented
            return info;
        }

         // --- Helper to Create TcpConnectionInfo ---
        private static TcpConnectionInfo CreateTcpConnectionInfo(TcpConnectionInformation conn)
        {
            var info = new TcpConnectionInfo
            {
                LocalAddress = conn.LocalEndPoint.Address.ToString(),
                LocalPort = conn.LocalEndPoint.Port,
                RemoteAddress = conn.RemoteEndPoint.Address.ToString(),
                RemotePort = conn.RemoteEndPoint.Port,
                State = conn.State
                 // PID and Process Name require P/Invoke (GetExtendedTcpTable)
                // OwningPid = PInvokeHelper.GetPidForTcpConnection(...), // Example call
                // OwningProcessName = PInvokeHelper.GetProcessName(OwningPid) // Example call
            };
             // Add error handling here if PID lookup is implemented
            return info;
        }
    }
}