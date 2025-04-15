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
        // --- Ping Test ---
        public static async Task<PingResult> PerformPingAsync(string target, int timeout = 1000)
        {
            var result = new PingResult { Target = target };
             if (string.IsNullOrWhiteSpace(target))
            {
                result.Status = "Invalid Target";
                return result;
            }

            IPAddress? targetIp = null;
            Stopwatch sw = Stopwatch.StartNew(); // Time the entire operation including potential resolution

            try
            {
                // Try to resolve hostname first if it's not an IP
                if (!IPAddress.TryParse(target, out targetIp))
                {
                    try
                    {
                        // Use Dns.GetHostAddressesAsync for potentially better async behavior
                        IPAddress[] addresses = await Dns.GetHostAddressesAsync(target);
                        // Prefer IPv4 for ping if available
                        targetIp = addresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                                   ?? addresses.FirstOrDefault();

                        if (targetIp == null)
                        {
                            result.Status = "Resolution Failed";
                            result.Error = "Could not resolve hostname to an IP address.";
                            sw.Stop();
                            return result;
                        }
                         result.ResolvedIpAddress = targetIp.ToString(); // Store resolved IP
                    }
                    catch (SocketException se)
                    {
                         result.Status = "Resolution Error";
                         result.Error = $"DNS resolution failed: {se.SocketErrorCode}"; // More specific error
                         sw.Stop();
                         return result;
                    }
                    catch (Exception ex)
                    {
                         result.Status = "Resolution Error";
                         result.Error = $"DNS resolution failed: {ex.Message.Split('.')[0]}";
                         sw.Stop();
                         return result;
                    }
                }
                else
                {
                    // Target was already an IP address
                     result.ResolvedIpAddress = targetIp.ToString();
                }

                // Perform the ping
                using var pinger = new Ping();
                // Send ping to the resolved IP address
                var reply = await pinger.SendPingAsync(targetIp, timeout);
                sw.Stop(); // Stop timing after reply

                result.Status = reply.Status.ToString();
                if (reply.Status == IPStatus.Success)
                {
                    result.RoundtripTimeMs = reply.RoundtripTime;
                }
                // Add specific error details for common failures
                else if(reply.Status == IPStatus.TimedOut) { result.Error = "Request timed out."; }
                else if(reply.Status == IPStatus.DestinationHostUnreachable) { result.Error = "Destination host unreachable."; }
                // Add other relevant status mappings if needed
            }
            // Catch exceptions during the Ping itself
            catch (PingException pex) { result.Status = "Ping Error"; result.Error = $"{pex.GetType().Name}: {pex.Message.Split('.')[0]}"; }
            catch (SocketException sex) { result.Status = "Socket Error"; result.Error = $"{sex.GetType().Name} ({sex.SocketErrorCode})"; } // Include SocketErrorCode
            catch (ArgumentException aex) { result.Status = "Argument Error"; result.Error = $"{aex.GetType().Name}: {aex.Message.Split('.')[0]}"; }
            // Note: InvalidOperationException removed due to CS0160, covered by general Exception
            // catch (InvalidOperationException ioex) { result.Status = "Invalid Op Error"; result.Error = $"{ioex.GetType().Name}: {ioex.Message.Split('.')[0]}"; }
            catch (ObjectDisposedException odex) { result.Status = "Object Disposed Error"; result.Error = $"{odex.GetType().Name}: {odex.Message.Split('.')[0]}"; }
            catch (Exception ex) // Catch unexpected errors
            {
                result.Status = "Unexpected Error";
                result.Error = $"{ex.GetType().Name}: {ex.Message.Split('.')[0]}";
                 Console.Error.WriteLine($"[Ping Unexpected Error] Target: {target}, Error: {ex}"); // Log details
            }
            finally
            {
                if(sw.IsRunning) sw.Stop(); // Ensure stopwatch is stopped
            }
            return result;
        }

        // --- DNS Resolution Test ---
        public static async Task<DnsResolutionResult> PerformDnsResolutionAsync(string hostname, int timeout = 2000) // Added timeout
        {
            var result = new DnsResolutionResult { Hostname = hostname };
            if (string.IsNullOrWhiteSpace(hostname))
            {
                result.Success = false;
                result.Error = "Invalid hostname.";
                return result;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                 // Use Task.Run with a timeout mechanism
                 var resolutionTask = Dns.GetHostAddressesAsync(hostname);
                 var completedTask = await Task.WhenAny(resolutionTask, Task.Delay(timeout));

                 sw.Stop(); // Stop timer after task completes or times out

                 if (completedTask == resolutionTask && resolutionTask.Status == TaskStatus.RanToCompletion)
                 {
                     IPAddress[] addresses = resolutionTask.Result;
                     if (addresses != null && addresses.Length > 0)
                     {
                         result.Success = true;
                         result.ResolvedIpAddresses = addresses.Select(ip => ip.ToString()).ToList();
                         result.ResolutionTimeMs = sw.ElapsedMilliseconds;
                     }
                     else
                     {
                         result.Success = false;
                         result.Error = "Hostname resolved but returned no IP addresses.";
                     }
                 }
                 else if (completedTask is Task timeoutTask && timeoutTask.IsCompleted) // Check if it was the delay task
                 {
                      result.Success = false;
                      result.Error = $"DNS resolution timed out after {timeout} ms.";
                 }
                 else // Handle other task failure states (Faulted, Canceled)
                 {
                     result.Success = false;
                      result.Error = $"DNS resolution task failed. Status: {resolutionTask.Status}";
                      if (resolutionTask.Exception != null)
                      {
                           var innerEx = resolutionTask.Exception.InnerExceptions.FirstOrDefault() ?? resolutionTask.Exception;
                           result.Error += $". Error: {innerEx.GetType().Name} - {innerEx.Message}";
                           if(innerEx is SocketException se) { result.Error += $" (SocketError: {se.SocketErrorCode})"; }
                      }
                 }
            }
            catch (SocketException se) // Catch specific DNS errors
            {
                sw.Stop();
                result.Success = false;
                result.Error = $"DNS Socket Error: {se.SocketErrorCode}";
            }
            catch (ArgumentException ae) // Catch invalid hostname format errors
            {
                 sw.Stop();
                 result.Success = false;
                 result.Error = $"Invalid hostname format: {ae.Message}";
            }
            catch (Exception ex) // Catch unexpected errors
            {
                if(sw.IsRunning) sw.Stop();
                result.Success = false;
                result.Error = $"Unexpected DNS Error: {ex.GetType().Name} - {ex.Message}";
                 Console.Error.WriteLine($"[DNS Unexpected Error] Hostname: {hostname}, Error: {ex}"); // Log details
            }
            return result;
        }

        // --- Traceroute ---
        // Now returns List<DiagnosticToolAllInOne.TracerouteHop> from DataModels
        public static async Task<List<TracerouteHop>> PerformTracerouteAsync(string target, int maxHops = 30, int timeout = 1000)
        {
            var results = new List<TracerouteHop>(); // Correct type
            IPAddress? targetIpAddress = null;

            try
            {
                // Resolve target hostname or parse IP address
                if (IPAddress.TryParse(target, out targetIpAddress)) { /* Target is IP */ }
                else {
                    // Use Dns.GetHostAddressesAsync for potentially better async behavior
                    var dnsResult = await PerformDnsResolutionAsync(target, timeout * 2); // Use slightly longer timeout for initial resolution
                    if (!dnsResult.Success || dnsResult.ResolvedIpAddresses == null || !dnsResult.ResolvedIpAddresses.Any())
                    {
                        results.Add(new TracerouteHop { Hop = 0, Status = $"Failed to resolve target '{target}': {dnsResult.Error}" });
                        return results;
                    }
                    // Prefer IPv4 if available
                    targetIpAddress = dnsResult.ResolvedIpAddresses
                        .Select(ipStr => IPAddress.TryParse(ipStr, out var ip) ? ip : null)
                        .Where(ip => ip != null)
                        .FirstOrDefault(ip => ip!.AddressFamily == AddressFamily.InterNetwork)
                        ?? IPAddress.Parse(dnsResult.ResolvedIpAddresses.First()); // Fallback to first resolved IP
                }

                if (targetIpAddress == null) { results.Add(new TracerouteHop { Hop = 0, Status = "Failed to resolve target or invalid IP" }); return results; }
            }
            catch (Exception ex) { results.Add(new TracerouteHop { Hop = 0, Status = $"DNS Resolution Error: {ex.Message}" }); return results; }

            var pingOptions = new PingOptions(1, true); // Start TTL at 1, don't fragment
            var pingBuffer = Encoding.ASCII.GetBytes("WinDiagAdvancedTrace"); // Arbitrary payload

            if(!Console.IsOutputRedirected) Console.WriteLine($"\nTracing route to {target} [{targetIpAddress}] over a maximum of {maxHops} hops:"); // Keep console output for now

            for (int ttl = 1; ttl <= maxHops; ttl++)
            {
                pingOptions.Ttl = ttl;
                var hopInfo = new TracerouteHop { Hop = ttl }; // Correct type
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
                    // Only record time if successful or TTL expired at hop, otherwise it's meaningless
                    hopInfo.RoundtripTimeMs = (reply.Status == IPStatus.Success || reply.Status == IPStatus.TtlExpired) ? reply.RoundtripTime : null;

                    if(!Console.IsOutputRedirected) Console.WriteLine($" {ttl,2} {(hopInfo.RoundtripTimeMs?.ToString() ?? "*").PadLeft(4)}ms  {hopInfo.Address,-30} ({hopInfo.Status})"); // Keep console output here
                }
                catch (ObjectDisposedException) { sw.Stop(); hopInfo.Status = "Timeout (Internal)"; hopInfo.Address = "*"; if(!Console.IsOutputRedirected) Console.WriteLine($" {ttl,2}    * Timeout (Internal)");}
                catch (PingException pex) { sw.Stop(); hopInfo.Status = "Ping Exception"; hopInfo.Address = "*"; if(!Console.IsOutputRedirected) Console.WriteLine($" {ttl,2}    * Ping Exception: {pex.Message.Split('.')[0]}"); hopInfo.Error = pex.Message.Split('.')[0]; }
                catch (Exception ex) { sw.Stop(); hopInfo.Status = "General Error"; hopInfo.Address = "*"; if(!Console.IsOutputRedirected) Console.WriteLine($" {ttl,2}    * Error: {ex.GetType().Name}"); hopInfo.Error = ex.Message.Split('.')[0]; }
                finally {
                    if (hopInfo.Status == null) { hopInfo.Status = "Unknown Error"; hopInfo.Address = "*"; }
                    results.Add(hopInfo);
                 }

                // Check if trace is complete
                // Use Equals method for IPAddress comparison
                bool reachedTarget = reply?.Status == IPStatus.Success && reply?.Address != null && targetIpAddress.Equals(reply.Address);
                // Consider error if status is not TTL Exceeded, Timeout, or Success
                bool significantError = reply != null && reply.Status != IPStatus.TtlExpired && reply.Status != IPStatus.TimedOut && reply.Status != IPStatus.Success;

                if (reachedTarget || significantError) break;
            }
            if(!Console.IsOutputRedirected) Console.WriteLine("Trace complete."); // Keep console output here
            return results;
        }

        // --- TracerouteHop class definition REMOVED - Moved to DataModels.cs ---

    }
}