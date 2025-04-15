// Helpers/WmiHelper.cs
using System;
using System.Collections.Generic; // Required for List
using System.Linq;
using System.Management;
using System.Runtime.Versioning;

namespace DiagnosticToolAllInOne.Helpers
{
    // Represents the result of a WMI query attempt
    public record WmiQueryResult(bool Success, ManagementObjectCollection? Results = null, string? ErrorMessage = null);

    [SupportedOSPlatform("windows")]
    public static class WmiHelper
    {
        // --- WMI Query Executor ---
        // Returns a WmiQueryResult indicating success/failure and holding results or an error message.
        public static WmiQueryResult Query(string wmiClass, string[]? properties, string scope = @"root\cimv2", string condition = "")
        {
            ManagementObjectSearcher? searcher = null;
            ManagementScope? managementScope = null;
            ManagementObjectCollection? results = null;

            try
            {
                string propertyList = properties != null && properties.Length > 0 ? string.Join(",", properties) : "*";
                string query = $"SELECT {propertyList} FROM {wmiClass}";
                if (!string.IsNullOrWhiteSpace(condition))
                {
                    query += $" WHERE {condition}";
                }

                var options = new ConnectionOptions { Impersonation = ImpersonationLevel.Impersonate, EnablePrivileges = true, Timeout = TimeSpan.FromSeconds(30) }; // Increased timeout slightly
                managementScope = new ManagementScope(scope, options);
                managementScope.Connect();

                if (!managementScope.IsConnected)
                {
                    return new WmiQueryResult(false, ErrorMessage: $"Failed to connect to scope {scope} for class {wmiClass}.");
                }

                searcher = new ManagementObjectSearcher(managementScope, new ObjectQuery(query));
                results = searcher.Get(); // This executes the query

                // IMPORTANT: We return the results collection directly. The caller MUST dispose it.
                return new WmiQueryResult(true, Results: results);
            }
            catch (ManagementException mex)
            {
                searcher?.Dispose();
                results?.Dispose(); // Dispose if exception occurred after Get() but before return
                string error = $"WMI Query Failed for {wmiClass} in {scope}. {mex.Message.Split('.')[0]} (Code: {mex.ErrorCode})";
                Console.Error.WriteLine($"[WMI HELPER ERROR] {error}"); // Log detailed error internally
                return new WmiQueryResult(false, ErrorMessage: error);
            }
            catch (UnauthorizedAccessException uaex)
            {
                searcher?.Dispose();
                results?.Dispose();
                string error = $"Access Denied querying {wmiClass} in {scope}. {uaex.Message}";
                Console.Error.WriteLine($"[WMI HELPER ERROR] {error}");
                return new WmiQueryResult(false, ErrorMessage: error);
            }
            catch (System.Runtime.InteropServices.COMException comEx)
            {
                searcher?.Dispose();
                results?.Dispose();
                uint errorCode = (uint)comEx.ErrorCode;
                 // Don't treat common "Not Found" or "Service Disabled" for SecurityCenter2 as critical errors here, let caller decide.
                bool ignoreError = (scope == @"root\SecurityCenter2" && (errorCode == 0x8004100E || errorCode == 0x80070422));

                if (ignoreError)
                {
                    Console.WriteLine($"[WMI HELPER INFO] COM Error ignored for {wmiClass} in {scope}: {comEx.Message} (HRESULT: {comEx.HResult:X})");
                    // Return success but with null results, indicating the query ran but found nothing relevant (expected in this case).
                    return new WmiQueryResult(true, Results: null);
                }
                else
                {
                    string error = $"COM Error querying {wmiClass} in {scope}: {comEx.Message} (HRESULT: {comEx.HResult:X})";
                    Console.Error.WriteLine($"[WMI HELPER ERROR] {error}");
                    return new WmiQueryResult(false, ErrorMessage: error);
                }
            }
            catch (Exception ex)
            {
                searcher?.Dispose();
                results?.Dispose();
                string error = $"Generic Error querying WMI class {wmiClass} in {scope}: {ex.GetType().Name} - {ex.Message}";
                Console.Error.WriteLine($"[WMI HELPER ERROR] {error}");
                return new WmiQueryResult(false, ErrorMessage: error);
            }
            finally
            {
                 // Dispose the searcher, but NOT the results collection - the caller needs it.
                 searcher?.Dispose();
            }
        }

        // --- WMI Property Getter --- (No changes needed)
        public static string GetProperty(ManagementBaseObject obj, string propertyName, string defaultValue = "N/A")
        {
            if (obj == null) return defaultValue;
            try
            {
                object? value = obj[propertyName];
                if (value == null || (value is string s && string.IsNullOrWhiteSpace(s))) return defaultValue;

                if (value is Array array)
                {
                    var nonNullStrings = array.Cast<object>().Select(o => o?.ToString() ?? "").Where(str => !string.IsNullOrWhiteSpace(str)).ToArray();
                    return nonNullStrings.Length > 0 ? string.Join(", ", nonNullStrings) : defaultValue;
                }
                return value.ToString() ?? defaultValue;
            }
            catch (ManagementException) { return defaultValue; } // Property might not exist
            catch (Exception) { return "Error Reading Property"; }
        }


        // --- WMI DateTime Converter --- (No changes needed)
        public static DateTime? ConvertCimDateTime(string? wmiDate)
        {
             if (string.IsNullOrWhiteSpace(wmiDate) || wmiDate.Equals("N/A", StringComparison.OrdinalIgnoreCase)) return null;
            try { return ManagementDateTimeConverter.ToDateTime(wmiDate); }
            catch { return null; } // Keep simple for now
        }

        // --- Helper to process results safely ---
        // Use this in collectors to handle the WmiQueryResult and ManagementObjectCollection disposal
        public static void ProcessWmiResults(WmiQueryResult queryResult, Action<ManagementObject> processObject, Action<string> onError)
        {
            if (!queryResult.Success || queryResult.Results == null)
            {
                if (!string.IsNullOrEmpty(queryResult.ErrorMessage))
                {
                    onError(queryResult.ErrorMessage);
                }
                // Dispose results even if success is false but results somehow got populated before error
                queryResult.Results?.Dispose();
                return;
            }

            try
            {
                if (queryResult.Results.Count == 0) return; // No objects to process

                foreach (ManagementBaseObject baseObj in queryResult.Results)
                {
                    using (ManagementObject obj = (ManagementObject)baseObj) // Cast and use 'using' for disposal
                    {
                        try
                        {
                            processObject(obj);
                        }
                        catch (Exception processEx)
                        {
                            // Log errors during processing of a specific WMI object
                            Console.Error.WriteLine($"[WMI HELPER] Exception during processing WMI object: {processEx.Message}");
                            // Optionally pass this error up if needed: onError($"Error processing object: {processEx.Message}");
                        }
                    }
                }
            }
            catch (ManagementException mex) // Error during enumeration
            {
                 onError($"Error enumerating WMI results: {mex.Message} (Code: {mex.ErrorCode})");
            }
            catch (Exception ex) // Other errors during enumeration
            {
                onError($"Generic error processing WMI results: {ex.Message}");
            }
            finally
            {
                queryResult.Results.Dispose(); // Ensure the collection is always disposed
            }
        }
    }
}