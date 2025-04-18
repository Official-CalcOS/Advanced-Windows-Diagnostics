// Collectors/SecurityInfoCollector.cs
using Microsoft.Win32;
using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using DiagnosticToolAllInOne.Helpers; // Ensure all helpers are referenced
using System.Collections.Generic;


namespace DiagnosticToolAllInOne.Collectors
{
    [SupportedOSPlatform("windows")]
    public static class SecurityInfoCollector
    {
        private const string WMI_CIMV2 = @"root\cimv2";
        private const string WMI_SECURITY_CENTER2 = @"root\SecurityCenter2";
        private const string WMI_MS_TPM = @"root\cimv2\Security\MicrosoftTpm";
        private const string SECURE_BOOT_REG_PATH = @"SYSTEM\CurrentControlSet\Control\SecureBoot\State";
        private const string SECURE_BOOT_VALUE_NAME = "UEFISecureBootEnabled";


        public static Task<SecurityInfo> CollectAsync(bool isAdmin)
        {
            var securityInfo = new SecurityInfo { IsAdmin = isAdmin };

            try
            {
                // --- UAC Status ---
                try
                {
                    string uacValue = RegistryHelper.ReadValue(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA");
                    securityInfo.UacStatus = uacValue switch
                    {
                        "1" => "Enabled",
                        "0" => "Disabled",
                        "Access Denied" => "Access Denied", // Propagate access denied
                        _ => "Unknown/Not Found" // Covers "Not Found" and other errors
                    };
                    if (securityInfo.UacStatus == "Access Denied")
                    {
                        securityInfo.AddSpecificError("UACStatus", "Requires Admin"); // Add specific error
                    }
                }
                catch (Exception ex) // Catch unexpected errors during registry read setup
                {
                    securityInfo.AddSpecificError("UACStatus_Overall", $"Failed to read UAC status: {ex.Message}");
                    securityInfo.UacStatus = "Error";
                    Logger.LogError("Failed to read UAC status", ex); // Use Logger
                }


                if (isAdmin)
                {
                    // Antivirus
                    WmiHelper.ProcessWmiResults(
                        // Corrected WMI Query: Added properties and namespace, removed trailing comma
                        WmiHelper.Query("AntiVirusProduct", new[] { "displayName", "productState" }, WMI_SECURITY_CENTER2),
                        obj =>
                        {
                            // Populate AntivirusInfo properties
                            securityInfo.Antivirus = new AntivirusInfo
                            {
                                Name = WmiHelper.GetProperty(obj, "displayName"),
                                State = ParseProductState(WmiHelper.GetProperty(obj, "productState")) // Use helper to parse state
                            };
                            // Only take the first one found for simplicity
                            return; // Exit lambda after processing the first result
                        },
                        error => securityInfo.AddSpecificError("Antivirus_WMI", error)
                    );

                    // Firewall
                    WmiHelper.ProcessWmiResults(
                         // Corrected WMI Query: Added properties and namespace, removed trailing comma
                         WmiHelper.Query("FirewallProduct", new[] { "displayName", "productState" }, WMI_SECURITY_CENTER2),
                         obj =>
                         {
                             // Populate FirewallInfo properties
                             securityInfo.Firewall = new FirewallInfo
                             {
                                 Name = WmiHelper.GetProperty(obj, "displayName"),
                                 State = ParseProductState(WmiHelper.GetProperty(obj, "productState")) // Use helper to parse state
                             };
                             // Only take the first one found for simplicity
                             return; // Exit lambda after processing the first result
                         },
                         error => securityInfo.AddSpecificError("Firewall_WMI", error)
                    );

                    // Set default message if object is still null AND no specific WMI error was recorded
                    if (securityInfo.Antivirus == null && !(securityInfo.SpecificCollectionErrors?.ContainsKey("Antivirus_WMI") ?? false))
                    {
                        securityInfo.Antivirus = new AntivirusInfo { State = "Not Found (No product reported by WMI)" };
                        securityInfo.AddSpecificError("Antivirus_NotFound", "WMI query succeeded but returned no AntivirusProduct instances.");
                    }
                    if (securityInfo.Firewall == null && !(securityInfo.SpecificCollectionErrors?.ContainsKey("Firewall_WMI") ?? false))
                    {
                        securityInfo.Firewall = new FirewallInfo { State = "Not Found (No product reported by WMI)" };
                        securityInfo.AddSpecificError("Firewall_NotFound", "WMI query succeeded but returned no FirewallProduct instances.");
                    }
                }
                else // Not Admin
                {
                    // Explicitly state requires admin and add specific error
                    securityInfo.Antivirus = new AntivirusInfo { State = "Requires Admin" };
                    securityInfo.AddSpecificError("Antivirus", "Requires Admin");
                    securityInfo.Firewall = new FirewallInfo { State = "Requires Admin" };
                    securityInfo.AddSpecificError("Firewall", "Requires Admin");
                }

                // --- Local Users ---
                securityInfo.LocalUsers = new List<UserAccountInfo>(); // Initialize
                WmiHelper.ProcessWmiResults(
                    WmiHelper.Query("Win32_UserAccount", new[] { "Name", "FullName", "SID", "Disabled", "LocalAccount", "PasswordRequired", "PasswordChangeable" }, WMI_CIMV2, "LocalAccount = True"),
                    obj =>
                    {
                        securityInfo.LocalUsers.Add(new UserAccountInfo
                        {
                            Name = WmiHelper.GetProperty(obj, "Name"),
                            FullName = WmiHelper.GetProperty(obj, "FullName"),
                            SID = WmiHelper.GetProperty(obj, "SID"),
                            IsDisabled = bool.TryParse(WmiHelper.GetProperty(obj, "Disabled"), out bool dis) && dis,
                            IsLocal = bool.TryParse(WmiHelper.GetProperty(obj, "LocalAccount"), out bool loc) && loc,
                            PasswordRequired = bool.TryParse(WmiHelper.GetProperty(obj, "PasswordRequired"), out bool req) && req,
                            PasswordChangeable = bool.TryParse(WmiHelper.GetProperty(obj, "PasswordChangeable"), out bool change) && change
                        });
                    },
                     error => securityInfo.AddSpecificError("LocalUsers_WMI", error) // Log WMI specific error
                );

                // --- Local Groups ---
                securityInfo.LocalGroups = new List<GroupInfo>(); // Initialize
                WmiHelper.ProcessWmiResults(
                    WmiHelper.Query("Win32_Group", new[] { "Name", "SID", "Description" }, WMI_CIMV2, "LocalAccount = True"),
                    obj =>
                    {
                        securityInfo.LocalGroups.Add(new GroupInfo
                        {
                            Name = WmiHelper.GetProperty(obj, "Name"),
                            SID = WmiHelper.GetProperty(obj, "SID"),
                            Description = WmiHelper.GetProperty(obj, "Description")
                        });
                    },
                     error => securityInfo.AddSpecificError("LocalGroups_WMI", error) // Log WMI specific error
                );

                // --- Network Shares ---
                securityInfo.NetworkShares = new List<ShareInfo>(); // Initialize
                WmiHelper.ProcessWmiResults(
                    WmiHelper.Query("Win32_Share", null, WMI_CIMV2),
                    obj =>
                    {
                        securityInfo.NetworkShares.Add(new ShareInfo
                        {
                            Name = WmiHelper.GetProperty(obj, "Name"),
                            Path = WmiHelper.GetProperty(obj, "Path"),
                            Description = WmiHelper.GetProperty(obj, "Description"),
                            Type = uint.TryParse(WmiHelper.GetProperty(obj, "Type"), out uint type) ? type : null
                        });
                    },
                     error => securityInfo.AddSpecificError("NetworkShares_WMI", error) // Log WMI specific error
                );

                // --- Secure Boot Status ---
                try
                {
                    string secureBootValue = RegistryHelper.ReadValue(Registry.LocalMachine, SECURE_BOOT_REG_PATH, SECURE_BOOT_VALUE_NAME, "Not Found");
                    if (secureBootValue == "1")
                    {
                        securityInfo.IsSecureBootEnabled = true;
                        securityInfo.BiosMode = "UEFI";
                    }
                    else if (secureBootValue == "0")
                    {
                        securityInfo.IsSecureBootEnabled = false;
                        securityInfo.BiosMode = "UEFI (Secure Boot Off) or Legacy";
                    }
                    else if (secureBootValue == "Access Denied")
                    {
                        securityInfo.IsSecureBootEnabled = null;
                        securityInfo.BiosMode = "Unknown (Access Denied)";
                        securityInfo.AddSpecificError("SecureBoot", "Access Denied reading registry key (Requires Admin)."); // Specify admin needed
                    }
                    else // Not Found or Error
                    {
                        securityInfo.IsSecureBootEnabled = false; // Default to false if not found or error
                        securityInfo.BiosMode = "Legacy or Unknown";
                        if(secureBootValue != "Not Found") // Only add error if it wasn't simply not found
                           securityInfo.AddSpecificError("SecureBoot", $"Registry value error ({secureBootValue}). Assuming disabled/Legacy.");
                    }
                }
                catch (Exception ex)
                {
                    securityInfo.AddSpecificError("SecureBoot_Overall", $"Error checking Secure Boot status: {ex.Message}");
                    securityInfo.IsSecureBootEnabled = null;
                    securityInfo.BiosMode = "Error";
                    Logger.LogError("Error checking Secure Boot status", ex); // Use Logger
                }


                // --- TPM Status (Requires Admin) ---
                securityInfo.Tpm = new TpmInfo(); // Initialize
                if (isAdmin)
                {
                    bool tpmQueryRan = false;
                    string? tpmWmiError = null;

                    WmiHelper.ProcessWmiResults(
                        WmiHelper.Query("Win32_Tpm", null, WMI_MS_TPM),
                        obj =>
                        {
                            tpmQueryRan = true;
                            securityInfo.Tpm.IsPresent = true;
                            securityInfo.Tpm.IsEnabled = bool.TryParse(WmiHelper.GetProperty(obj, "IsEnabled_InitialValue"), out bool enabled) && enabled;
                            securityInfo.Tpm.IsActivated = bool.TryParse(WmiHelper.GetProperty(obj, "IsActivated_InitialValue"), out bool activated) && activated;
                            securityInfo.Tpm.SpecVersion = WmiHelper.GetProperty(obj, "SpecVersion");
                            securityInfo.Tpm.ManufacturerVersion = WmiHelper.GetProperty(obj, "ManufacturerVersion");
                            securityInfo.Tpm.ManufacturerIdTxt = WmiHelper.GetProperty(obj, "ManufacturerIdTxt");

                            if (securityInfo.Tpm.IsPresent == true && securityInfo.Tpm.IsEnabled == true && securityInfo.Tpm.IsActivated == true)
                                securityInfo.Tpm.Status = "Ready";
                            else if (securityInfo.Tpm.IsPresent == true)
                                securityInfo.Tpm.Status = "Present but Not Ready (Check Enabled/Activated state)";
                            else
                                securityInfo.Tpm.Status = "State Unknown";

                            return; // Process only the first instance if multiple exist
                        },
                        error =>
                        {
                            tpmWmiError = error; // Capture WMI error
                            // Log specific error and set status based on error type
                            securityInfo.AddSpecificError("TPM_WMI", error);
                            if (error.Contains("Invalid namespace") || error.Contains("Invalid class") || error.Contains("NotFound"))
                            {
                                securityInfo.Tpm.Status = "Not Present or WMI Service Issue";
                                securityInfo.Tpm.IsPresent = false;
                            }
                            else
                            {
                                securityInfo.Tpm.Status = "Error Querying";
                                securityInfo.Tpm.ErrorMessage = error;
                            }
                        }
                    );

                    // If WMI query succeeded but returned NO results
                    if (tpmQueryRan == false && tpmWmiError == null)
                    {
                        securityInfo.Tpm.IsPresent = false;
                        securityInfo.Tpm.Status = "Not Present (No WMI Instance)";
                        securityInfo.AddSpecificError("TPM_NotFound", "WMI query succeeded but returned no Win32_Tpm instance.");
                    }
                }
                else // Not Admin
                {
                    securityInfo.Tpm.Status = "Requires Admin";
                    securityInfo.Tpm.ErrorMessage = "Requires Admin privileges for WMI TPM query.";
                    // Add specific error indicating admin needed
                    securityInfo.AddSpecificError("TPM", "Requires Admin");
                }

            }
            catch (Exception ex) // Catch errors in the overall collection setup
            {
                // Use Logger
                Logger.LogError($"[CRITICAL ERROR] Security Info Collection failed", ex);
                securityInfo.SectionCollectionErrorMessage = $"Critical failure during Security Info collection: {ex.Message}";
            }
            // Use Task.FromResult for compatibility if method signature requires Task<>
            return Task.FromResult(securityInfo);
        }

        // Helper method to parse the productState from SecurityCenter2 WMI queries
        // See: https://learn.microsoft.com/en-us/previous-versions/windows/desktop/legacy/ms741080(v=vs.85)
        // And: https://learn.microsoft.com/en-us/windows/win32/wmisdk/wmi-security-best-practices
        private static string ParseProductState(string? productStateStr)
        {
            if (string.IsNullOrEmpty(productStateStr) || !int.TryParse(productStateStr, out int productState))
            {
                return "Unknown State";
            }

            // The state is a bitmask. We are interested in the second byte (bits 8-15) for enabled/disabled status
            // and the third byte (bits 16-23) for definition status.
            int stateByte = (productState >> 8) & 0xFF; // Bits 8-15
            int definitionByte = (productState >> 16) & 0xFF; // Bits 16-23

            bool isEnabled = (stateByte & 0x10) == 0x10; // Bit 12 (0x10 within the byte) means enabled
            bool isSnoozed = (stateByte & 0x10) == 0x00 && (stateByte & 0x01) == 0x01; // Heuristic: Enabled bit off, Snooze bit on?
            bool definitionsUpToDate = definitionByte == 0x00; // 0x00 means up to date

            if (isEnabled && definitionsUpToDate) return "Enabled and Up-to-date";
            if (isEnabled && !definitionsUpToDate) return "Enabled but Not up-to-date";
            if (!isEnabled && isSnoozed) return "Snoozed"; // Check Snoozed before generic Disabled
            if (!isEnabled) return "Disabled";

            return "Unknown State"; // Fallback
        }
    }
}
