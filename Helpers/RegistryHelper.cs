using Microsoft.Win32;
using System;
using System.Runtime.Versioning;

namespace DiagnosticToolAllInOne.Helpers
{
    [SupportedOSPlatform("windows")]
    public static class RegistryHelper
    {
        public static string ReadValue(RegistryKey baseKey, string subKeyPath, string valueName, string defaultValue = "N/A")
        {
            RegistryKey? regKey = null;
            try
            {
                regKey = baseKey.OpenSubKey(subKeyPath, false);
                var value = regKey?.GetValue(valueName);
                return value?.ToString() ?? defaultValue;
            }
            catch (System.Security.SecurityException) { return "Access Denied"; }
            catch (Exception ex) { return $"RegError: {ex.GetType().Name}"; }
            finally
            {
                regKey?.Dispose();
            }
        }
    }
}