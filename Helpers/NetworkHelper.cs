using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions; // Added for validation
using System.Threading.Tasks;
using DiagnosticToolAllInOne.Helpers;

namespace DiagnosticToolAllInOne.Helpers
{
    [SupportedOSPlatform("windows")]
    public static class NetworkHelper
    {
        // Centralized logging helper - NOW USES Logger
        private static void LogNetworkError(string methodName, string message, Exception? ex = null, string? target = null)
        {
            string logTarget = string.IsNullOrEmpty(target) ? "" : $" (Target: {target})";
            string logMessage = $"[{nameof(NetworkHelper)}.{methodName}]{logTarget} - {message}";

            // Use the assumed Logger class
            if (ex != null)
            {
                // Log with exception details
                Logger.LogError(logMessage, ex);
            }
            else
            {
                // Log informational or warning messages without full exception
                // Determine level based on message content? For now, default to Warning.
                Logger.LogWarning(logMessage);
            }

            // --- Original Console Error Lines (Removed) ---
            // Console.Error.WriteLine($"[DEBUG_ERROR][{nameof(NetworkHelper)}.{methodName}] {(target ?? "")} - {message}");
            // if (ex != null)
            // {
            //     Console.Error.WriteLine($"    Exception Type: {ex.GetType().Name} - Message: {ex.Message}");
            // }
        }

        // --- Input Validation Helper (Updated) ---
        private static bool IsValidNetworkTarget(string? target, out string validationError)
        {
            validationError = string.Empty;
            const int MaxTargetLength = 255; // DNS max length is around 253

            // Basic checks first
            if (string.IsNullOrWhiteSpace(target))
            {
                validationError = "Target cannot be empty.";
                return false;
            }
            if (target.Length > MaxTargetLength)
            {
                validationError = "Target name is too long.";
                return false;
            }

            // Prevent obviously unsafe characters or patterns
            // Regex checks for whitespace, back/forward slashes, and other URL-unsafe chars (excluding allowed ones like :, ., -)
            // Also checks for '..' sequence explicitly.
            if (Regex.IsMatch(target, @"[\s\\/\?#@!$&'()*\+,;=]|(\.\.)"))
            {
                 // Special case: Allow square brackets and colons ONLY if it parses as IPv6
                 if (!IPAddress.TryParse(target, out var ip) || ip.AddressFamily != AddressFamily.InterNetworkV6)
                 {
                      validationError = "Target contains invalid characters (spaces, slashes, etc.) or patterns ('..').";
                      return false;
                 }
            }

            // Use Uri.CheckHostName for robust validation
            UriHostNameType hostNameType;
            try {
                 hostNameType = Uri.CheckHostName(target);
            } catch (ArgumentException argEx) {
                 validationError = $"Target format is invalid: {argEx.Message}";
                 LogNetworkError(nameof(IsValidNetworkTarget), $"Uri.CheckHostName failed for '{target}'", argEx, target);
                 return false;
            }

            switch (hostNameType)
            {
                case UriHostNameType.Dns:
                    if (target.StartsWith("-") || target.EndsWith("-")) {
                        validationError = "DNS hostname cannot start or end with a hyphen.";
                        return false;
                    }
                     if (target.Split('.').Any(label => label.Length > 63)) {
                          validationError = "DNS label exceeds maximum length of 63 characters.";
                          return false;
                     }
                    return true; // Valid DNS name format

                case UriHostNameType.IPv4:
                case UriHostNameType.IPv6:
                    return true; // Valid IP address format

                case UriHostNameType.Unknown: // Could be a simple name for local resolution
                     if (target.Contains(".")) { // Simple names usually don't have dots
                         validationError = "Target is not a recognized DNS name or IP address format.";
                         return false;
                     }
                     return true; // Allow simple names

                default:
                    validationError = $"Unsupported target format type: {hostNameType}.";
                    return false;
            }
        }


        // --- Ping Test ---
        public static async Task<PingResult> PerformPingAsync(string target, int timeout = 1000)
        {
            var result = new PingResult { Target = target };

            if (!IsValidNetworkTarget(target, out string validationError))
            {
                result.Status = "Invalid Target";
                result.Error = validationError;
                LogNetworkError(nameof(PerformPingAsync), $"Invalid target specified: {validationError}", null, target);
                return result;
            }

            IPAddress? targetIp = null;
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                // Resolve hostname if not already an IP
                if (!IPAddress.TryParse(target, out targetIp))
                {
                    DnsResolutionResult dnsResult = await PerformDnsResolutionAsync(target, timeout);
                    if (!dnsResult.Success || dnsResult.ResolvedIpAddresses == null || !dnsResult.ResolvedIpAddresses.Any())
                    {
                        result.Status = "Resolution Failed";
                        result.Error = dnsResult.Error ?? "Could not resolve hostname.";
                        sw.Stop();
                        // LogNetworkError handled within PerformDnsResolutionAsync
                        return result;
                    }
                    targetIp = dnsResult.ResolvedIpAddresses
                        .Select(ipStr => IPAddress.TryParse(ipStr, out var ip) ? ip : null)
                        .Where(ip => ip != null)
                        .FirstOrDefault(ip => ip!.AddressFamily == AddressFamily.InterNetwork) // Prefer IPv4
                        ?? IPAddress.Parse(dnsResult.ResolvedIpAddresses.First());
                    result.ResolvedIpAddress = targetIp.ToString();
                }
                else
                {
                     result.ResolvedIpAddress = targetIp.ToString();
                }

                 if (targetIp == null)
                 {
                     throw new InvalidOperationException("Target IP address could not be determined.");
                 }

                // Perform the ping
                using var pinger = new Ping();
                var reply = await pinger.SendPingAsync(targetIp, timeout);
                sw.Stop();

                result.Status = reply.Status.ToString();
                if (reply.Status == IPStatus.Success)
                {
                    result.RoundtripTimeMs = reply.RoundtripTime;
                }
                else
                {
                    // Provide user-friendly errors, log technical status
                    result.Error = reply.Status switch {
                        IPStatus.TimedOut => "Request timed out.",
                        IPStatus.DestinationHostUnreachable => "Destination host unreachable.",
                        IPStatus.DestinationNetworkUnreachable => "Destination network unreachable.",
                        IPStatus.TtlExpired => "TTL expired in transit.",
                        _ => $"Ping failed (Status code: {reply.Status})."
                    };
                    // Log the technical status code internally
                    LogNetworkError(nameof(PerformPingAsync), $"Ping failed. Status: {reply.Status}", null, target);
                }
            }
            // Catch specific exceptions and log using LogNetworkError
            catch (PingException pex)
            {
                 LogNetworkError(nameof(PerformPingAsync), "PingException occurred", pex, target);
                 result.Status = "Ping Error";
                 result.Error = "A network error occurred during ping operation.";
            }
            catch (SocketException sex)
            {
                 LogNetworkError(nameof(PerformPingAsync), "SocketException occurred", sex, target);
                 result.Status = "Socket Error";
                 result.Error = "A socket error occurred during ping setup or operation.";
            }
            catch (ArgumentException aex)
            {
                 LogNetworkError(nameof(PerformPingAsync), "ArgumentException occurred", aex, target);
                 result.Status = "Argument Error";
                 result.Error = "Internal argument error during ping setup.";
            }
            catch (ObjectDisposedException odex)
            {
                 LogNetworkError(nameof(PerformPingAsync), "ObjectDisposedException occurred", odex, target);
                 result.Status = "Object Disposed";
                 result.Error = "Internal error (object disposed) during ping.";
            }
             catch (InvalidOperationException ioex)
             {
                 LogNetworkError(nameof(PerformPingAsync), "InvalidOperationException occurred", ioex, target);
                 result.Status = "Setup Error";
                 result.Error = "Could not determine target IP address for ping.";
             }
            catch (Exception ex) // Catch-all
            {
                LogNetworkError(nameof(PerformPingAsync), "Unexpected error", ex, target);
                result.Status = "Unexpected Error";
                result.Error = "An unexpected error occurred during ping.";
            }
            finally
            {
                if(sw.IsRunning) sw.Stop();
            }
            return result;
        }

        // --- DNS Resolution Test ---
        public static async Task<DnsResolutionResult> PerformDnsResolutionAsync(string hostname, int timeout = 2000)
        {
            var result = new DnsResolutionResult { Hostname = hostname };

             if (!IsValidNetworkTarget(hostname, out string validationError))
             {
                 result.Success = false;
                 result.Error = validationError;
                 LogNetworkError(nameof(PerformDnsResolutionAsync), $"Invalid hostname specified: {validationError}", null, hostname);
                 return result;
             }

            var sw = Stopwatch.StartNew();
            try
            {
                 var resolutionTask = Task.Run(async () => await Dns.GetHostAddressesAsync(hostname));
                 var completedTask = await Task.WhenAny(resolutionTask, Task.Delay(timeout));
                 sw.Stop();

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
                         result.Error = "Hostname resolved but returned no addresses.";
                         LogNetworkError(nameof(PerformDnsResolutionAsync), "Resolved successfully but no addresses returned", null, hostname);
                     }
                 }
                 else if (completedTask is Task && completedTask != resolutionTask)
                 {
                      result.Success = false;
                      result.Error = $"DNS resolution timed out ({timeout} ms).";
                      LogNetworkError(nameof(PerformDnsResolutionAsync), "DNS resolution timed out", null, hostname);
                 }
                 else // Handle other task failure states
                 {
                     result.Success = false;
                     result.Error = "DNS resolution failed."; // Generic error first
                      if (resolutionTask.Exception != null)
                      {
                           var innerEx = resolutionTask.Exception.InnerExceptions.FirstOrDefault() ?? resolutionTask.Exception;
                           // Log full exception internally
                           LogNetworkError(nameof(PerformDnsResolutionAsync), "DNS resolution task failed", innerEx, hostname);
                           // Provide slightly more specific but safe error type for user
                            if(innerEx is SocketException se) {
                                result.Error = $"DNS resolution failed (Network Error: {se.SocketErrorCode}).";
                            } else {
                                result.Error = $"DNS resolution failed (Error Type: {innerEx.GetType().Name}).";
                            }
                      } else {
                           LogNetworkError(nameof(PerformDnsResolutionAsync), $"DNS resolution task finished with unexpected status {resolutionTask.Status}", null, hostname);
                           result.Error = "DNS resolution failed with an unexpected status.";
                      }
                 }
            }
            // Catch specific exceptions and log using LogNetworkError
            catch (SocketException se)
            {
                sw.Stop();
                LogNetworkError(nameof(PerformDnsResolutionAsync), "SocketException occurred during setup", se, hostname);
                result.Success = false;
                result.Error = $"DNS network error occurred (Code: {se.SocketErrorCode}).";
            }
            catch (ArgumentException ae)
            {
                 sw.Stop();
                 LogNetworkError(nameof(PerformDnsResolutionAsync), "ArgumentException occurred during setup", ae, hostname);
                 result.Success = false;
                 result.Error = "Invalid hostname format provided for DNS resolution.";
            }
            catch (Exception ex) // Catch unexpected errors
            {
                if(sw.IsRunning) sw.Stop();
                LogNetworkError(nameof(PerformDnsResolutionAsync), "Unexpected error", ex, hostname);
                result.Success = false;
                result.Error = "An unexpected error occurred during DNS resolution.";
            }
            return result;
        }

        // --- Traceroute ---
        public static async Task<List<TracerouteHop>> PerformTracerouteAsync(string target, int maxHops = 30, int timeout = 1000)
        {
            var results = new List<TracerouteHop>();
            IPAddress? targetIpAddress = null;

            if (!IsValidNetworkTarget(target, out string validationError))
            {
                 results.Add(new TracerouteHop { Hop = 0, Status = "Invalid Target", Error = validationError });
                 LogNetworkError(nameof(PerformTracerouteAsync), $"Invalid target specified: {validationError}", null, target);
                 return results;
            }
            maxHops = Math.Clamp(maxHops, 1, 64);
            timeout = Math.Clamp(timeout, 100, 5000);

            try
            {
                 if (!IPAddress.TryParse(target, out targetIpAddress))
                 {
                    var dnsResult = await PerformDnsResolutionAsync(target, timeout * 2);
                    if (!dnsResult.Success || dnsResult.ResolvedIpAddresses == null || !dnsResult.ResolvedIpAddresses.Any())
                    {
                        results.Add(new TracerouteHop { Hop = 0, Status = "DNS Resolution Failed", Error = dnsResult.Error ?? "Could not resolve target hostname." });
                        // LogNetworkError handled within PerformDnsResolutionAsync
                        return results;
                    }
                     targetIpAddress = dnsResult.ResolvedIpAddresses
                         .Select(ipStr => IPAddress.TryParse(ipStr, out var ip) ? ip : null)
                         .Where(ip => ip != null)
                         .FirstOrDefault(ip => ip!.AddressFamily == AddressFamily.InterNetwork)
                         ?? IPAddress.Parse(dnsResult.ResolvedIpAddresses.First());
                 }
                  if (targetIpAddress == null)
                  {
                      throw new InvalidOperationException("Target IP address could not be determined after resolution attempt.");
                  }
            }
            catch (Exception ex)
            {
                LogNetworkError(nameof(PerformTracerouteAsync), "Error resolving target", ex, target);
                results.Add(new TracerouteHop { Hop = 0, Status = "Resolution Error", Error = "Failed to resolve or determine target IP address." });
                return results;
            }

            var pingOptions = new PingOptions(1, true);
            var pingBuffer = Encoding.ASCII.GetBytes("DiagnosticToolTrace");

            // Console output removed - use Logger if needed for debugging hops
            // if (!Console.IsOutputRedirected) Console.WriteLine($"\nTracing route to {target} [{targetIpAddress}] over a maximum of {maxHops} hops:");

            for (int ttl = 1; ttl <= maxHops; ttl++)
            {
                pingOptions.Ttl = ttl;
                var hopInfo = new TracerouteHop { Hop = ttl };
                Stopwatch sw = Stopwatch.StartNew();
                PingReply? reply = null;

                try
                {
                    using var pinger = new Ping();
                    reply = await pinger.SendPingAsync(targetIpAddress, timeout, pingBuffer, pingOptions);
                    sw.Stop();

                    hopInfo.Status = reply.Status.ToString();
                    hopInfo.Address = reply.Address?.ToString() ?? "*";
                    hopInfo.RoundtripTimeMs = (reply.Status == IPStatus.Success || reply.Status == IPStatus.TtlExpired) ? reply.RoundtripTime : null;

                    // Console output removed
                    // if (!Console.IsOutputRedirected) Console.WriteLine($" {ttl,2} {(hopInfo.RoundtripTimeMs?.ToString() ?? "*").PadLeft(4)}ms  {hopInfo.Address,-17} ({hopInfo.Status})");

                    // Add user-friendly error message for specific failures
                    if (reply.Status != IPStatus.Success && reply.Status != IPStatus.TtlExpired && reply.Status != IPStatus.TimedOut)
                    {
                         hopInfo.Error = reply.Status switch {
                             IPStatus.DestinationHostUnreachable => "Host unreachable reported by router.",
                             IPStatus.DestinationNetworkUnreachable => "Network unreachable reported by router.",
                             IPStatus.DestinationProhibited => "Destination prohibited reported by router.",
                             IPStatus.ParameterProblem => "ICMP parameter problem.",
                             _ => $"Reply status: {reply.Status}"
                         };
                         // Log technical status internally
                         LogNetworkError(nameof(PerformTracerouteAsync), $"Hop {ttl} failed with status {reply.Status}", null, target);
                    }
                }
                // Catch specific exceptions and log using LogNetworkError
                catch (PingException pex)
                {
                    sw.Stop(); hopInfo.Status = "Ping Exception"; hopInfo.Address = "*";
                    hopInfo.Error = "A network error occurred sending probe.";
                    // Console output removed
                    LogNetworkError(nameof(PerformTracerouteAsync), $"PingException at hop {ttl}", pex, target);
                }
                catch (Exception ex)
                {
                     sw.Stop(); hopInfo.Status = "Hop Error"; hopInfo.Address = "*";
                     hopInfo.Error = "An unexpected error occurred processing this hop.";
                     // Console output removed
                     LogNetworkError(nameof(PerformTracerouteAsync), $"Unexpected error at hop {ttl}", ex, target);
                }
                finally
                {
                    if (hopInfo.Status == null) { hopInfo.Status = "Unknown Error"; hopInfo.Address = "*"; }
                    results.Add(hopInfo);
                }

                bool reachedTarget = reply?.Status == IPStatus.Success && reply?.Address != null && targetIpAddress.Equals(reply.Address);
                bool significantError = reply != null && reply.Status != IPStatus.TtlExpired && reply.Status != IPStatus.TimedOut && reply.Status != IPStatus.Success;

                if (reachedTarget)
                {
                    // Console output removed
                    break;
                }
                if (significantError)
                {
                     // Console output removed
                     break;
                }
            }

            // Console output removed
            // if (results.LastOrDefault()?.Status != IPStatus.Success.ToString() && results.LastOrDefault()?.Hop == maxHops) {
            //      if (!Console.IsOutputRedirected) Console.WriteLine($"Trace incomplete (maximum {maxHops} hops reached).");
            // }

            return results;
        }
    }
}
