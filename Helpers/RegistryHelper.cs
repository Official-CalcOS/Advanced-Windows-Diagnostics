using Microsoft.Win32;
using System;
using System.Runtime.Versioning;
using System.Linq;
using System.IO;
using DiagnosticToolAllInOne.Helpers;

namespace DiagnosticToolAllInOne.Helpers
{
    [SupportedOSPlatform("windows")]
    public static class RegistryHelper
    {
        // Reads a specific value from the registry.
        // Returns defaultValue ("N/A" or "Not Found") if key/value doesn't exist or an error string on failure.
        public static string ReadValue(RegistryKey baseKey, string subKeyPath, string valueName, string defaultValue = "Not Found")
        {
            RegistryKey? regKey = null;
            // Correctly get the last part of the base key name for logging
            string baseKeyName = baseKey.Name.Contains('\\') ? baseKey.Name.Split('\\').LastOrDefault() ?? baseKey.Name : baseKey.Name;
            string keyPathForLog = $"{baseKeyName}\\{subKeyPath}"; // For logging

            try
            {
                // Attempt to open the key with read-only access.
                regKey = baseKey.OpenSubKey(subKeyPath, false);

                // Check if the key exists.
                if (regKey == null)
                {
                     // Key not found is not necessarily an error, return the specified default.
                     // Logger.LogDebug($"[Registry Helper INFO] Key '{keyPathForLog}' not found."); // Optional debug log
                     return defaultValue;
                }

                // Try to get the value.
                object? value = regKey.GetValue(valueName);

                // Check if the value itself exists within the key.
                if (value == null)
                {
                    // Logger.LogDebug($"[Registry Helper INFO] Value '{valueName}' not found in key '{keyPathForLog}'."); // Optional debug log
                    return defaultValue;
                }

                // Return the value as a string.
                return value.ToString() ?? defaultValue; // Use defaultValue if ToString() returns null
            }
            catch (System.Security.SecurityException secEx)
            {
                // Permissions issue accessing the key or value.
                // Use Logger instead of Console.Error
                Logger.LogWarning($"[Registry Helper] Access Denied reading '{keyPathForLog}\\{valueName}'. Details: {secEx.Message}");
                return "Access Denied"; // User-friendly error
            }
            catch (ObjectDisposedException odEx)
            {
                 // Key was closed prematurely.
                 // Use Logger instead of Console.Error
                 Logger.LogError($"[Registry Helper] Object Disposed Exception reading '{keyPathForLog}\\{valueName}'.", odEx);
                 return "Error (Object Disposed)"; // Technical but indicates internal issue
            }
            catch (ArgumentException argEx)
             {
                 // Invalid key/value name format.
                 // Use Logger instead of Console.Error
                 Logger.LogError($"[Registry Helper] Invalid Argument reading '{keyPathForLog}\\{valueName}'.", argEx);
                 return "Error (Invalid Argument)";
             }
            catch (IOException ioEx)
             {
                 // Registry IO errors.
                 // Use Logger instead of Console.Error
                 Logger.LogError($"[Registry Helper] IO Exception reading '{keyPathForLog}\\{valueName}'.", ioEx);
                 return "Error (Registry IO)";
             }
            catch (Exception ex) // Catch other potential exceptions
            {
                 // Log details for debugging, return generic error.
                 // Use Logger instead of Console.Error
                 Logger.LogError($"[Registry Helper] Unexpected Error reading '{keyPathForLog}\\{valueName}'. Type: {ex.GetType().Name}", ex);
                return $"Error (Registry Read)"; // Generic user error
            }
            finally
            {
                // Ensure the key handle is always released.
                regKey?.Dispose();
            }
        }
    }
}
