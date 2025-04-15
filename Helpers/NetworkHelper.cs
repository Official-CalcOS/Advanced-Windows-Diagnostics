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


namespace DiagnosticToolAllInOne.Helpers
{
    [SupportedOSPlatform("windows")]
    public static class NetworkHelper
    {
        public static async Task<PingResult> PerformPingAsync(string target, int timeout = 1000)
        {
            var result = new PingResult { Target = target };
             if (string.IsNullOrWhiteSpace(target))
            {
                result.Status = "Invalid Target";
                return result;
            }
            try
            {
                using var pinger = new Ping();
                var reply = await pinger.SendPingAsync(target, timeout);
                result.Status = reply.Status.ToString();
                if (reply.Status == IPStatus.Success)
                {
                    result.RoundtripTimeMs = reply.RoundtripTime;
                }
            }
            catch (Exception ex) when (ex is PingException || ex is SocketException || ex is ArgumentException || ex is InvalidOperationException || ex is ObjectDisposedException)
            {
                result.Status = "Error";
                result.Error = $"{ex.GetType().Name}: {ex.Message.Split('.')[0]}"; // Shorter error
            }
            catch (Exception ex) // Catch unexpected errors
            {
                result.Status = "Unexpected Error";
                result.Error = $"{ex.GetType().Name}: {ex.Message.Split('.')[0]}";
            }
            return result;
        }

        public static async Task<List<TracerouteHop>> PerformTracerouteAsync(string target, int maxHops = 30, int timeout = 1000)
        {
            var results = new List<TracerouteHop>();
            IPAddress? targetIpAddress = null;

            try
            {
                // Resolve target hostname or parse IP address
                if (IPAddress.TryParse(target, out targetIpAddress)) { /* Target is IP */ }
                else {
                    // Use Dns.GetHostAddressesAsync for potentially better async behavior
                    IPAddress[] addresses = await Dns.GetHostAddressesAsync(target);
                    targetIpAddress = addresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
                }

                if (targetIpAddress == null) { results.Add(new TracerouteHop { Hop = 0, Status = "Failed to resolve target or invalid IP" }); return results; }
            }
            catch (Exception ex) { results.Add(new TracerouteHop { Hop = 0, Status = $"DNS Resolution Error: {ex.Message}" }); return results; }

            var pingOptions = new PingOptions(1, true); // Start TTL at 1, don't fragment
            var pingBuffer = Encoding.ASCII.GetBytes("WinDiagAdvancedTrace"); // Arbitrary payload

            Console.WriteLine($"\nTracing route to {target} [{targetIpAddress}] over a maximum of {maxHops} hops:"); // Keep console output for now

            for (int ttl = 1; ttl <= maxHops; ttl++)
            {
                pingOptions.Ttl = ttl;
                var hopInfo = new TracerouteHop { Hop = ttl };
                Stopwatch sw = Stopwatch.StartNew();
                PingReply? reply = null;

                try
                {
                    using var pinger = new Ping();
                    // Use the resolved target IP address
                    reply = await pinger.SendPingAsync(targetIpAddress, timeout, pingBuffer, pingOptions);
                    sw.Stop(); // Stop timing immediately after reply/timeout

                    hopInfo.Status = reply.Status.ToString();
                    hopInfo.Address = reply.Address?.ToString() ?? "*"; // Use '*' for unknown address
                    hopInfo.RoundtripTimeMs = (reply.Status == IPStatus.Success || reply.Status == IPStatus.TtlExpired) ? reply.RoundtripTime : null; // Only record time if successful or TTL expired at hop

                    Console.WriteLine($" {ttl,2} {(hopInfo.RoundtripTimeMs?.ToString() ?? "*").PadLeft(4)}ms  {hopInfo.Address,-30} ({hopInfo.Status})"); // Keep console output here
                }
                catch (ObjectDisposedException) { sw.Stop(); hopInfo.Status = "Timeout (Internal)"; hopInfo.Address = "*"; Console.WriteLine($" {ttl,2}    * Timeout (Internal)");}
                catch (PingException pex) { sw.Stop(); hopInfo.Status = "Ping Exception"; hopInfo.Address = "*"; Console.WriteLine($" {ttl,2}    * Ping Exception: {pex.Message.Split('.')[0]}"); }
                catch (Exception ex) { sw.Stop(); hopInfo.Status = "General Error"; hopInfo.Address = "*"; Console.WriteLine($" {ttl,2}    * Error: {ex.GetType().Name}"); }
                finally { if (hopInfo.Status == null) { hopInfo.Status = "Unknown Error"; hopInfo.Address = "*"; } results.Add(hopInfo); }

                // Check if trace is complete
                 // Corrected CS0618: Use Equals method for IPAddress comparison
                bool reachedTarget = reply?.Status == IPStatus.Success && reply?.Address != null && targetIpAddress.Equals(reply.Address);
                bool errorOccurred = hopInfo.Status != IPStatus.TtlExpired.ToString() && hopInfo.Status != IPStatus.TimedOut.ToString() && hopInfo.Status != IPStatus.Success.ToString();

                if (reachedTarget || errorOccurred) break;
            }
            Console.WriteLine("Trace complete."); // Keep console output here
            return results;
        }
    }
}