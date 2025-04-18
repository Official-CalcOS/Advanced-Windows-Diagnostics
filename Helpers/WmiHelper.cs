// Helpers/WmiHelper.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using DiagnosticToolAllInOne.Helpers;


namespace DiagnosticToolAllInOne.Helpers
{
    // Represents the result of a WMI query attempt
    public record WmiQueryResult(bool Success, ManagementObjectCollection? Results = null, string? ErrorMessage = null);

    [SupportedOSPlatform("windows")]
    public static class WmiHelper
    {
        // Constants for common WMI scopes
        internal const string WMI_CIMV2 = @"root\cimv2";
        internal const string WMI_WMI = @"root\wmi";
        internal const string WMI_MSVOLENC = @"root\cimv2\Security\MicrosoftVolumeEncryption";
        internal const string WMI_MS_TPM = @"root\cimv2\Security\MicrosoftTpm";
        internal const string WMI_SECURITY_CENTER2 = @"root\SecurityCenter2";

        // --- WMI Query Executor ---
        public static WmiQueryResult Query(string wmiClass, string[]? properties, string scope = WMI_CIMV2, string condition = "")
        {
            ManagementObjectSearcher? searcher = null;
            ManagementScope? managementScope = null;
            ManagementObjectCollection? results = null;
            string queryDescription = $"{wmiClass} in {scope}"; // For logging

            try
            {
                string propertyList = properties != null && properties.Length > 0 ? string.Join(",", properties) : "*";
                string queryString = $"SELECT {propertyList} FROM {wmiClass}";
                if (!string.IsNullOrWhiteSpace(condition))
                {
                    queryString += $" WHERE {condition}";
                }

                var options = new ConnectionOptions { Impersonation = ImpersonationLevel.Impersonate, EnablePrivileges = true, Timeout = TimeSpan.FromSeconds(30) };
                managementScope = new ManagementScope(scope, options);
                managementScope.Connect(); // Can throw if scope doesn't exist

                if (!managementScope.IsConnected)
                {
                    string errorMsg = $"Failed to connect to WMI scope '{scope}'.";
                    // Use Logger
                    Logger.LogWarning($"[WMI HELPER] {errorMsg} (Query: {queryDescription})");
                    return new WmiQueryResult(false, ErrorMessage: errorMsg);
                }

                searcher = new ManagementObjectSearcher(managementScope, new ObjectQuery(queryString));
                results = searcher.Get(); // Executes the query

                // Caller MUST dispose results via ProcessWmiResults
                return new WmiQueryResult(true, Results: results);
            }
            catch (ManagementException mex)
            {
                results?.Dispose(); // Dispose if exception occurred after Get()
                string errorCode = mex.ErrorCode.ToString();
                string detailedError = $"WMI Error: {errorCode} ({mex.Message})";

                // Handle common non-critical errors for specific features
                bool isKnownScopeFeature = scope.Equals(WMI_MS_TPM, StringComparison.OrdinalIgnoreCase) ||
                                           scope.Equals(WMI_SECURITY_CENTER2, StringComparison.OrdinalIgnoreCase) ||
                                           scope.Equals(WMI_MSVOLENC, StringComparison.OrdinalIgnoreCase) ||
                                          (scope.Equals(WMI_WMI, StringComparison.OrdinalIgnoreCase) && wmiClass.StartsWith("MSStorageDriver", StringComparison.OrdinalIgnoreCase)); // SMART specific

                if (mex.ErrorCode == ManagementStatus.NotFound || mex.ErrorCode == ManagementStatus.InvalidClass || mex.ErrorCode == ManagementStatus.InvalidNamespace || mex.ErrorCode == ManagementStatus.InvalidQuery)
                {
                    if (isKnownScopeFeature || mex.ErrorCode == ManagementStatus.InvalidQuery)
                    {
                        string userFriendlyError = $"Feature Info: {errorCode} - {mex.Message}"; // Include code for interpretation
                        // Log technical details but return slightly friendlier error message
                         Logger.LogInfo($"[WMI HELPER] WMI query for {queryDescription} failed (likely feature not available/applicable): {detailedError}");
                        // Return success=true but with null results and an informative message
                        return new WmiQueryResult(true, Results: null, ErrorMessage: userFriendlyError);
                    }
                     if (wmiClass.Equals("Win32_PowerPlan", StringComparison.OrdinalIgnoreCase) && mex.ErrorCode == ManagementStatus.InvalidClass)
                    {
                         string userFriendlyError = $"WMI class '{wmiClass}' not found. WMI Repository may need repair (Code: {errorCode}).";
                         Logger.LogWarning($"[WMI HELPER] Query Failed for {queryDescription}. {detailedError}"); // Use Logger
                         return new WmiQueryResult(false, ErrorMessage: userFriendlyError); // Treat as failure
                    }
                }

                // Generic WMI error message for other cases
                string genericError = $"WMI query failed for {wmiClass} (Code: {errorCode}). Check WMI service/permissions.";
                 Logger.LogWarning($"[WMI HELPER] Query Failed for {queryDescription}. {detailedError}"); // Use Logger
                return new WmiQueryResult(false, ErrorMessage: genericError);
            }
            catch (UnauthorizedAccessException uaex)
            {
                results?.Dispose();
                string error = $"Access Denied querying WMI for {queryDescription}. Run as Administrator.";
                // Use Logger
                Logger.LogWarning($"[WMI HELPER] {error} Details: {uaex.Message}");
                return new WmiQueryResult(false, ErrorMessage: error);
            }
            catch (System.Runtime.InteropServices.COMException comEx)
            {
                results?.Dispose();
                uint hResult = (uint)comEx.ErrorCode;
                string userFriendlyError = $"WMI/COM error querying {wmiClass} (HRESULT: 0x{hResult:X}). Check WMI service.";
                // Use Logger
                Logger.LogError($"[WMI HELPER] COM Error querying {queryDescription}. HRESULT: 0x{hResult:X}", comEx);
                return new WmiQueryResult(false, ErrorMessage: userFriendlyError);
            }
            catch (Exception ex)
            {
                results?.Dispose();
                string error = $"Unexpected error querying WMI for {queryDescription}.";
                // Use Logger
                Logger.LogError($"[WMI HELPER] Generic Error querying {queryDescription}", ex);
                return new WmiQueryResult(false, ErrorMessage: error);
            }
            finally
            {
                searcher?.Dispose();
                // Caller (ProcessWmiResults) MUST dispose the results collection
            }
        }

        // --- WMI Property Getter ---
        public static string GetProperty(ManagementBaseObject obj, string propertyName, string defaultValue = "N/A")
        {
            if (obj == null) return defaultValue;
            object? value = null;
            try
            {
                // Use GetPropertyValue for potentially better handling of missing properties
                value = obj.GetPropertyValue(propertyName);

                if (value == null) return defaultValue;
                if (value is string s && string.IsNullOrWhiteSpace(s)) return defaultValue;

                // Handle array properties by joining non-null elements
                if (value is Array array)
                {
                    var nonNullStrings = array.Cast<object>()
                                              .Where(o => o != null)
                                              .Select(o => o.ToString()?.Trim() ?? "")
                                              .Where(str => !string.IsNullOrWhiteSpace(str))
                                              .ToArray();
                    return nonNullStrings.Length > 0 ? string.Join(", ", nonNullStrings) : defaultValue;
                }

                return value.ToString() ?? defaultValue;
            }
            catch (ManagementException mex) when (mex.ErrorCode == ManagementStatus.NotFound)
            {
                 // Property doesn't exist on this specific object instance
                 // Logger.LogDebug($"[WMI HELPER] Property '{propertyName}' not found on WMI object."); // Optional debug log
                 return defaultValue;
            }
            catch (ManagementException mex) // Other WMI errors accessing property
            {
                // Use Logger
                Logger.LogWarning($"[WMI HELPER] ManagementException accessing property '{propertyName}': {mex.Message}");
                return defaultValue; // Return default on error
            }
            catch (Exception ex) // Catch-all for other unexpected errors
            {
                // Use Logger
                Logger.LogError($"[WMI HELPER] Exception accessing property '{propertyName}'", ex);
                return "Error Reading Property"; // Indicate error occurred
            }
        }


        // --- WMI DateTime Converter ---
        public static DateTime? ConvertCimDateTime(string? wmiDate)
        {
            if (string.IsNullOrWhiteSpace(wmiDate) || wmiDate.Equals("N/A", StringComparison.OrdinalIgnoreCase) || wmiDate.Length < 14)
                return null;
            try
            {
                // ManagementDateTimeConverter handles the specific WMI format including timezone offsets
                return ManagementDateTimeConverter.ToDateTime(wmiDate);
            }
            catch (ArgumentOutOfRangeException) // Handles invalid date/time components
            {
                // Use Logger
                Logger.LogWarning($"[WMI HELPER] Could not convert invalid CIM DateTime format: {wmiDate}");
                return null;
            }
            catch (Exception ex) // Other potential conversion errors
            {
                // Use Logger
                Logger.LogError($"[WMI HELPER] Error converting CIM DateTime '{wmiDate}'", ex);
                return null;
            }
        }

        // --- Helper to process results safely ---
        // Modified to catch errors during single object processing
        [SupportedOSPlatform("windows")]
        public static void ProcessWmiResults(WmiQueryResult queryResult, Action<ManagementObject> processObject, Action<string> onError)
        {
            // Ensure results are disposed even if processing is skipped or fails
            using (queryResult.Results) // using handles null check for Results
            {
                // Handle query failure or info messages from Query() method
                if (!queryResult.Success || !string.IsNullOrEmpty(queryResult.ErrorMessage))
                {
                    if (!string.IsNullOrEmpty(queryResult.ErrorMessage))
                    {
                        onError(queryResult.ErrorMessage); // Pass the detailed message from Query()
                    }
                    // If query fundamentally failed, don't attempt to process results
                    if (!queryResult.Success)
                    {
                        return;
                    }
                    // If Success is true but ErrorMessage exists (e.g., feature info), we might still have results=null
                }

                // If successful and results exist, proceed to process
                if (queryResult.Success && queryResult.Results != null)
                {
                    try
                    {
                        // --- ADDED: Safer Count Check ---
                        int count = 0;
                        bool canEnumerate = true;
                        try
                        {
                            // Check count safely - Count can sometimes throw exceptions too
                            count = queryResult.Results.Count;
                            // Logger.LogDebug($"[WMI HELPER] Query returned {count} results."); // Optional debug
                        }
                        catch (ManagementException mex) // Catch specific WMI errors getting count
                        {
                            string countError = $"Error getting WMI result count (Code: {mex.ErrorCode}): {mex.Message}";
                            Logger.LogWarning($"[WMI HELPER] {countError}");
                            onError(countError); // Report error getting count
                            // If count fails with specific errors, maybe don't enumerate?
                            // Example: InvalidClass might mean enumeration will also fail.
                            if (mex.ErrorCode == ManagementStatus.InvalidClass || mex.ErrorCode == ManagementStatus.InvalidNamespace || mex.ErrorCode == ManagementStatus.InvalidQuery)
                            {
                                canEnumerate = false;
                            }
                            // Fallthrough to enumeration attempt for other errors? Decide based on testing.
                        }
                        catch (Exception countEx) // Catch other errors getting count
                        {
                            string countError = $"Unexpected error getting WMI result count: {countEx.Message}";
                            Logger.LogWarning($"[WMI HELPER] {countError}", countEx);
                            onError(countError); // Report other errors getting count
                            // Might still try to enumerate depending on the error
                        }
                        // --- End Safer Count Check ---


                        // If enumeration is deemed possible
                        if (canEnumerate)
                        {
                            if (count == 0 && queryResult.ErrorMessage == null)
                            {
                                // Logger.LogDebug("[WMI HELPER] WMI query returned 0 results."); // Optional debug log
                                return; // No objects to process
                            }

                            // Process each object, catching errors within the loop
                            foreach (ManagementBaseObject baseObj in queryResult.Results) // This iteration can also throw
                            {
                                // Dispose each ManagementObject after processing
                                using (ManagementObject obj = (ManagementObject)baseObj)
                                {
                                    try
                                    {
                                        processObject(obj);
                                    }
                                    catch (Exception processEx) // Catch error processing THIS specific object
                                    {
                                        Logger.LogWarning($"[WMI HELPER] Exception during processing single WMI object: {processEx.Message}", processEx);
                                        string objectIdentifier = "UnknownObject";
                                        try { objectIdentifier = obj.Path?.RelativePath ?? "UnknownObject"; } catch { /* Ignore errors getting path */ }
                                        onError($"Error processing WMI object '{objectIdentifier}': {processEx.Message}"); // Add specific error for analysis
                                    }
                                }
                            }
                        } // end if(canEnumerate)
                    }
                    catch (ManagementException mex) // Error during the enumeration itself (e.g., iterating queryResult.Results)
                    {
                        string errorMsg = $"Error enumerating WMI results (Code: {mex.ErrorCode}). Details: {mex.Message}";
                        onError(errorMsg); // Report enumeration error
                        Logger.LogError($"[WMI HELPER] {errorMsg}", mex); // Use Logger
                    }
                    catch (Exception ex) // Other unexpected errors during enumeration/processing loop
                    {
                        string errorMsg = $"Generic error processing WMI results. Details: {ex.Message}";
                        onError(errorMsg); // Report generic processing error
                        Logger.LogError($"[WMI HELPER] {errorMsg}", ex); // Use Logger
                    }
                }
                // Results collection is disposed by the outer using statement
            }
        }
    }
}
