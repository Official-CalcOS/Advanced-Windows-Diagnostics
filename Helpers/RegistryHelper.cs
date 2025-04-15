using Microsoft.Win32;
using System;
using System.Runtime.Versioning;

namespace DiagnosticToolAllInOne.Helpers
{
    [SupportedOSPlatform("windows")]
    public static class RegistryHelper
    {
        // Existing method is sufficient, no changes needed for reading Secure Boot key.
        public static string ReadValue(RegistryKey baseKey, string subKeyPath, string valueName, string defaultValue = "N/A")
        {
            RegistryKey? regKey = null;
            try
            {
                // Attempt to open the key with read-only access.
                regKey = baseKey.OpenSubKey(subKeyPath, false);

                // Check if the key exists. If not, return defaultValue (e.g., "Not Found").
                if (regKey == null) return defaultValue; // Changed from "N/A" to make absence clearer

                // Try to get the value.
                object? value = regKey.GetValue(valueName);

                // Return the value as a string, or defaultValue if the value itself doesn't exist or is null.
                return value?.ToString() ?? defaultValue; // Value exists but is null, or doesn't exist
            }
            catch (System.Security.SecurityException)
            {
                // Permissions issue accessing the key or value.
                Console.Error.WriteLine($"[Registry Helper] Access Denied reading HKLM\\{subKeyPath}\\{valueName}");
                return "Access Denied";
            }
            catch (ObjectDisposedException)
            {
                 // Key was closed prematurely - should not happen with this structure but handle defensively.
                 Console.Error.WriteLine($"[Registry Helper] Object Disposed Exception reading HKLM\\{subKeyPath}\\{valueName}");
                 return "Error (Object Disposed)";
            }
            catch (Exception ex)
            {
                // Catch other potential exceptions (e.g., IOException).
                 Console.Error.WriteLine($"[Registry Helper] Generic Error reading HKLM\\{subKeyPath}\\{valueName}: {ex.GetType().Name} - {ex.Message}");
                return $"RegError: {ex.GetType().Name}";
            }
            finally
            {
                // Ensure the key handle is released.
                regKey?.Dispose();
            }
        }
    }
}