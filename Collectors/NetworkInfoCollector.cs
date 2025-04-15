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
using DiagnosticToolAllInOne.Helpers;


namespace DiagnosticToolAllInOne.Collectors
{
    [SupportedOSPlatform("windows")]
    public static class NetworkInfoCollector
    {
        private const string WMI_CIMV2 = @"root\cimv2";

        public static async Task<NetworkInfo> CollectAsync(string? tracerouteTarget)
        {
            var netInfo = new NetworkInfo();
            netInfo.Adapters = new();
            netInfo.ActiveTcpListeners = new();
            netInfo.ActiveUdpListeners = new();
            netInfo.ConnectivityTests = new NetworkTestResults();

            try
            {
                // --- Get WMI Network Config ---
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
                         netInfo.AddSpecificError("WMIConfig", "Failed to retrieve any WMI Network Adapter Configuration details.");
                    }
                }
                catch (Exception wmiEx)
                {
                     netInfo.AddSpecificError("WMIConfig", $"Warning: Failed to get WMI Network Configuration data: {wmiEx.Message}");
                }

                // --- Get Network Interfaces ---
                NetworkInterface[] adapters = Array.Empty<NetworkInterface>();
                try
                {
                     adapters = NetworkInterface.GetAllNetworkInterfaces();
                }
                catch(NetworkInformationException netEx)
                {
                     netInfo.SectionCollectionErrorMessage = $"Critical Error: Failed to get network interfaces: {netEx.Message}";
                     return netInfo;
                }
                 catch(Exception ex)
                {
                     netInfo.SectionCollectionErrorMessage = $"Critical Error: Unexpected error getting network interfaces: {ex.Message}";
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
                         adapterDetail.Id = adapter.Id;
                         adapterDetail.Type = adapter.NetworkInterfaceType;
                         adapterDetail.Status = adapter.OperationalStatus;
                         adapterDetail.SpeedMbps = adapter.IsReceiveOnly ? -1 : adapter.Speed / 1_000_000;
                         adapterDetail.IsReceiveOnly = adapter.IsReceiveOnly;

                        try { adapterDetail.MacAddress = adapter.GetPhysicalAddress()?.ToString(); }
                        catch (NotSupportedException) { adapterDetail.MacAddress = "N/A (Not Supported)"; }
                        catch (Exception macEx) {
                            adapterDetail.MacAddress = $"Error";
                             netInfo.AddSpecificError($"MAC-{adapter.Name}", $"Error getting MAC for '{adapter.Name}': {macEx.GetType().Name}");
                        }

                        if (adapter.OperationalStatus == OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        {
                             IPInterfaceProperties? ipProps = null;
                             try { ipProps = adapter.GetIPProperties(); }
                             catch (NotSupportedException) { netInfo.AddSpecificError($"IPProps-{adapter.Name}", $"Info: IP properties not supported for adapter '{adapter.Name}'."); }
                             catch (NetworkInformationException ipEx) { netInfo.AddSpecificError($"IPProps-{adapter.Name}", $"Warning: Network error getting IP properties for '{adapter.Name}': {ipEx.Message}"); }
                             catch (Exception ipEx) { netInfo.AddSpecificError($"IPProps-{adapter.Name}", $"Warning: Error getting IP properties for '{adapter.Name}': {ipEx.GetType().Name} - {ipEx.Message}"); }

                             if (ipProps != null)
                             {
                                 // Keep suppression for now, as direct string conversion is the goal here.
                                 #pragma warning disable CS0618 // Suppress warning for IPAddress.Address usage for string conversion
                                 adapterDetail.IpAddresses = ipProps.UnicastAddresses?.Select(ip => ip.Address.ToString()).ToList() ?? new List<string>();
                                 #pragma warning restore CS0618
                                 adapterDetail.Gateways = ipProps.GatewayAddresses?.Select(gw => gw.Address.ToString()).ToList() ?? new List<string>();
                                 adapterDetail.DnsServers = ipProps.DnsAddresses?.Select(dns => dns.Address.ToString()).ToList() ?? new List<string>();

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
                                         } else { adapterDetail.DhcpEnabled = false; }
                                     }
                                 } else if (wmiAdapterInfo == null) { adapterDetail.DhcpEnabled = false; }
                             }
                        }
                        netInfo.Adapters.Add(adapterDetail);
                    }
                    catch (NotSupportedException nse) { netInfo.AddSpecificError($"Adapter-{adapter?.Name ?? "Unknown"}", $"Warning: Skipping adapter '{adapter?.Name ?? "Unknown"}' due to unsupported operation: {nse.Message}"); }
                    catch (NetworkInformationException nie) { netInfo.AddSpecificError($"Adapter-{adapter?.Name ?? "Unknown"}", $"Warning: Skipping adapter '{adapter?.Name ?? "Unknown"}' due to network error: {nie.Message} (Code: {nie.ErrorCode})"); }
                    catch (Exception ex) { netInfo.AddSpecificError($"Adapter-{adapter?.Name ?? "Unknown"}", $"Warning: Skipping adapter '{adapter?.Name ?? "Unknown"}' due to unexpected error: {ex.GetType().Name} - {ex.Message}"); }
                }

                // --- Active Listeners ---
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


                // --- Connectivity Tests ---
                 try
                 {
                    string? gatewayToPing = netInfo.Adapters?
                                                .Where(a => a.Status == OperationalStatus.Up && (a.Gateways?.Any(gw => !string.IsNullOrWhiteSpace(gw) && gw != "0.0.0.0") ?? false))
                                                .SelectMany(a => a.Gateways!)
                                                .FirstOrDefault(gw => IPAddress.TryParse(gw, out _));

                    if (!string.IsNullOrEmpty(gatewayToPing)) { netInfo.ConnectivityTests.GatewayPing = await NetworkHelper.PerformPingAsync(gatewayToPing); }
                    else { netInfo.ConnectivityTests.GatewayPing = new PingResult { Target = "Default Gateway", Status = "Not Found" }; }

                    string[] dnsServersToPing = { "8.8.8.8", "1.1.1.1" };
                    netInfo.ConnectivityTests.DnsPings = new();
                    foreach (string dns in dnsServersToPing) { netInfo.ConnectivityTests.DnsPings.Add(await NetworkHelper.PerformPingAsync(dns)); }

                    if (!string.IsNullOrWhiteSpace(tracerouteTarget))
                    {
                         netInfo.ConnectivityTests.TracerouteTarget = tracerouteTarget;
                         netInfo.ConnectivityTests.TracerouteResults = await NetworkHelper.PerformTracerouteAsync(tracerouteTarget);
                    }
                 }
                 catch(Exception connEx)
                 {
                      netInfo.AddSpecificError("ConnectivityTests", $"Error during connectivity tests: {connEx.Message}");
                 }

            }
            catch(Exception ex)
            {
                 netInfo.SectionCollectionErrorMessage = $"Unexpected Error during Network Collection: {ex.Message}";
            }

            return netInfo;
        }

        // --- WMI Helper Call ---
        private static List<WmiNetworkConfig> GetWmiNetworkConfig()
        {
            var list = new List<WmiNetworkConfig>();
            string? wmiError = null;

            WmiHelper.ProcessWmiResults(
                 WmiHelper.Query(
                    "Win32_NetworkAdapterConfiguration",
                    new[] { "MACAddress", "DHCPEnabled", "DHCPLeaseObtained", "DHCPLeaseExpires", "ServiceName" },
                    WMI_CIMV2, // Use constant defined in this class
                    "IPEnabled = True"
                 ),
                 // Corrected CS0649: Assign values inside the lambda
                 obj => {
                     list.Add(new WmiNetworkConfig {
                         MACAddress = WmiHelper.GetProperty(obj, "MACAddress"),
                         DHCPEnabled = bool.TryParse(WmiHelper.GetProperty(obj, "DHCPEnabled"), out bool dhcp) && dhcp,
                         DHCPLeaseObtained = WmiHelper.ConvertCimDateTime(WmiHelper.GetProperty(obj, "DHCPLeaseObtained")),
                         DHCPLeaseExpires = WmiHelper.ConvertCimDateTime(WmiHelper.GetProperty(obj, "DHCPLeaseExpires")),
                         ServiceName = WmiHelper.GetProperty(obj, "ServiceName")
                     });
                 },
                 error => wmiError = error
            );

            if (wmiError != null)
            {
                 Console.Error.WriteLine($"[WARN] WMI Network Config query failed: {wmiError}");
                 return new List<WmiNetworkConfig>(); // Return empty list on error
            }
            return list;
        }

        // Private helper class for WMI data
        private class WmiNetworkConfig
        {
            public string? MACAddress;
            public bool DHCPEnabled;
            public DateTime? DHCPLeaseObtained;
            public DateTime? DHCPLeaseExpires;
            public string? ServiceName;
        }


        // --- Active Port Helper ---
        private static ActivePortInfo CreateActivePortInfo(string protocol, IPEndPoint listener)
        {
            var info = new ActivePortInfo
            {
                Protocol = protocol,
                LocalAddress = listener.Address.ToString(),
                LocalPort = listener.Port,
                OwningPid = null,
                OwningProcessName = "N/A (Lookup requires Admin/Advanced API)",
                Error = null
            };
            return info;
        }
    }
}