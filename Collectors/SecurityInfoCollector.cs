using Microsoft.Win32;
using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using DiagnosticToolAllInOne.Helpers; // Use helpers

namespace DiagnosticToolAllInOne.Collectors
{
    [SupportedOSPlatform("windows")]
    public static class SecurityInfoCollector
    {
        private const string WMI_CIMV2 = @"root\cimv2";
        private const string WMI_SECURITY_CENTER2 = @"root\SecurityCenter2";
        // NEW: WMI Namespace for TPM
        private const string WMI_MS_TPM = @"root\cimv2\Security\MicrosoftTpm";
        // NEW: Registry key for Secure Boot
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
                    securityInfo.UacStatus = RegistryHelper.ReadValue(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA") switch
                    { "1" => "Enabled", "0" => "Disabled", _ => "Unknown/Not Found" };
                }
                catch(Exception ex)
                {
                    securityInfo.AddSpecificError("UACStatus", $"Failed to read UAC status: {ex.Message}");
                    securityInfo.UacStatus = "Error";
                }


                // --- AV/Firewall Status ---
                if (isAdmin)
                {
                    WmiHelper.ProcessWmiResults(
                        WmiHelper.Query("AntiVirusProduct", new[] { "displayName", "productState" }, WMI_SECURITY_CENTER2),
                        obj => {
                            securityInfo.Antivirus = new AntivirusInfo
                            {
                                Name = WmiHelper.GetProperty(obj, "displayName"),
                                RawProductState = WmiHelper.GetProperty(obj, "productState"),
                                State = FormatHelper.DecodeProductState(WmiHelper.GetProperty(obj, "productState"))
                            };
                        },
                        error => securityInfo.AddSpecificError("Antivirus", error)
                    );

                    WmiHelper.ProcessWmiResults(
                         WmiHelper.Query("FirewallProduct", new[] { "displayName", "productState" }, WMI_SECURITY_CENTER2),
                         obj => {
                            securityInfo.Firewall = new FirewallInfo
                            {
                                Name = WmiHelper.GetProperty(obj, "displayName"),
                                RawProductState = WmiHelper.GetProperty(obj, "productState"),
                                State = FormatHelper.DecodeProductState(WmiHelper.GetProperty(obj, "productState"))
                            };
                         },
                         error => securityInfo.AddSpecificError("Firewall", error)
                    );

                    // Set default message if object is still null after query attempt
                    if (securityInfo.Antivirus == null && !(securityInfo.SpecificCollectionErrors?.ContainsKey("Antivirus") ?? false))
                       securityInfo.Antivirus = new AntivirusInfo { State = "Not Found or WMI Access Denied" };
                    if (securityInfo.Firewall == null && !(securityInfo.SpecificCollectionErrors?.ContainsKey("Firewall") ?? false))
                       securityInfo.Firewall = new FirewallInfo { State = "Not Found or WMI Access Denied" };
                } else {
                    securityInfo.AddSpecificError("Antivirus", "Requires Admin");
                    securityInfo.Antivirus = new AntivirusInfo { State = "Requires Admin" };
                     securityInfo.AddSpecificError("Firewall", "Requires Admin");
                    securityInfo.Firewall = new FirewallInfo { State = "Requires Admin" };
                }

                // --- Local Users ---
                securityInfo.LocalUsers = new();
                 WmiHelper.ProcessWmiResults(
                     WmiHelper.Query("Win32_UserAccount", new[] { "Name", "FullName", "SID", "Disabled", "LocalAccount", "PasswordRequired", "PasswordChangeable" }, WMI_CIMV2, "LocalAccount = True"),
                     obj => {
                         securityInfo.LocalUsers.Add(new UserAccountInfo {
                            Name = WmiHelper.GetProperty(obj, "Name"),
                            FullName = WmiHelper.GetProperty(obj, "FullName"),
                            SID = WmiHelper.GetProperty(obj, "SID"),
                            IsDisabled = bool.TryParse(WmiHelper.GetProperty(obj, "Disabled"), out bool dis) && dis,
                            IsLocal = bool.TryParse(WmiHelper.GetProperty(obj, "LocalAccount"), out bool loc) && loc, // Should always be true due to WHERE clause
                            PasswordRequired = bool.TryParse(WmiHelper.GetProperty(obj, "PasswordRequired"), out bool req) && req,
                            PasswordChangeable = bool.TryParse(WmiHelper.GetProperty(obj, "PasswordChangeable"), out bool change) && change
                         });
                     },
                      error => securityInfo.AddSpecificError("LocalUsers", error)
                 );

                // --- Local Groups ---
                securityInfo.LocalGroups = new();
                 WmiHelper.ProcessWmiResults(
                     WmiHelper.Query("Win32_Group", new[] { "Name", "SID", "Description" }, WMI_CIMV2, "LocalAccount = True"),
                     obj => {
                        securityInfo.LocalGroups.Add(new GroupInfo {
                            Name = WmiHelper.GetProperty(obj, "Name"),
                            SID = WmiHelper.GetProperty(obj, "SID"),
                            Description = WmiHelper.GetProperty(obj, "Description")
                        });
                     },
                      error => securityInfo.AddSpecificError("LocalGroups", error)
                 );

                // --- Network Shares ---
                securityInfo.NetworkShares = new();
                 WmiHelper.ProcessWmiResults(
                     WmiHelper.Query("Win32_Share", null, WMI_CIMV2),
                     obj => {
                        securityInfo.NetworkShares.Add(new ShareInfo {
                            Name = WmiHelper.GetProperty(obj, "Name"),
                            Path = WmiHelper.GetProperty(obj, "Path"),
                            Description = WmiHelper.GetProperty(obj, "Description"),
                            Type = uint.TryParse(WmiHelper.GetProperty(obj, "Type"), out uint type) ? type : null
                        });
                     },
                      error => securityInfo.AddSpecificError("NetworkShares", error)
                 );

                // --- Secure Boot Status ---
                try
                {
                     string secureBootValue = RegistryHelper.ReadValue(Registry.LocalMachine, SECURE_BOOT_REG_PATH, SECURE_BOOT_VALUE_NAME, "Not Found");
                     if (secureBootValue == "1")
                     {
                         securityInfo.IsSecureBootEnabled = true;
                         securityInfo.BiosMode = "UEFI"; // Secure Boot requires UEFI
                     }
                     else if (secureBootValue == "0")
                     {
                         securityInfo.IsSecureBootEnabled = false;
                         // BIOS mode could be UEFI or Legacy if Secure Boot is off
                         securityInfo.BiosMode = "UEFI (Secure Boot Off) or Legacy";
                     }
                      else if (secureBootValue == "Access Denied")
                     {
                          securityInfo.IsSecureBootEnabled = null; // Unknown due to permissions
                          securityInfo.BiosMode = "Unknown (Access Denied)";
                          securityInfo.AddSpecificError("SecureBoot", "Access Denied reading Secure Boot registry key.");
                     }
                     else // Not Found or Error
                     {
                         securityInfo.IsSecureBootEnabled = false; // Treat as not enabled if key/value absent
                         securityInfo.BiosMode = "Legacy or Unknown"; // Assume Legacy or unknown if key missing
                         securityInfo.AddSpecificError("SecureBoot", $"Secure Boot registry value not found or error ({secureBootValue}). Assuming disabled/Legacy.");
                     }
                }
                catch(Exception ex)
                {
                     securityInfo.AddSpecificError("SecureBoot", $"Error checking Secure Boot status: {ex.Message}");
                     securityInfo.IsSecureBootEnabled = null;
                     securityInfo.BiosMode = "Error";
                }


                // --- TPM Status ---
                securityInfo.Tpm = new TpmInfo(); // Initialize
                if (isAdmin)
                {
                    bool tpmQueryRan = false;
                    WmiHelper.ProcessWmiResults(
                        WmiHelper.Query("Win32_Tpm", null, WMI_MS_TPM),
                        obj => {
                            tpmQueryRan = true; // At least one result found
                            securityInfo.Tpm.IsPresent = true; // Found the WMI object
                            securityInfo.Tpm.IsEnabled = bool.TryParse(WmiHelper.GetProperty(obj, "IsEnabled_InitialValue"), out bool enabled) && enabled;
                            securityInfo.Tpm.IsActivated = bool.TryParse(WmiHelper.GetProperty(obj, "IsActivated_InitialValue"), out bool activated) && activated;
                            // Note: SpecVersion from WMI is often Major.Minor.Revision.Patch, e.g., "2.0.1.1"
                            securityInfo.Tpm.SpecVersion = WmiHelper.GetProperty(obj, "SpecVersion");
                            securityInfo.Tpm.ManufacturerVersion = WmiHelper.GetProperty(obj, "ManufacturerVersion");
                            securityInfo.Tpm.ManufacturerIdTxt = WmiHelper.GetProperty(obj, "ManufacturerIdTxt");

                            if (securityInfo.Tpm.IsPresent == true && securityInfo.Tpm.IsEnabled == true && securityInfo.Tpm.IsActivated == true)
                                securityInfo.Tpm.Status = "Ready";
                            else if (securityInfo.Tpm.IsPresent == true)
                                securityInfo.Tpm.Status = "Present but Not Ready (Check Enabled/Activated state)";
                            else
                                securityInfo.Tpm.Status = "State Unknown"; // Should not happen if IsPresent is true
                        },
                        error => {
                            // If the error indicates the class/namespace wasn't found, TPM might not be present or service disabled
                            if (error.Contains("Invalid namespace") || error.Contains("Invalid class"))
                            {
                                securityInfo.Tpm.Status = "Not Present or WMI Service Issue";
                                securityInfo.Tpm.IsPresent = false;
                            }
                            else // Other errors (e.g., access denied though we check isAdmin)
                            {
                                securityInfo.Tpm.Status = "Error Querying";
                                securityInfo.Tpm.ErrorMessage = error;
                            }
                            securityInfo.AddSpecificError("TPM", error); // Log the specific WMI error
                        }
                    );

                    // If WMI query ran successfully but returned NO results, Win32_Tpm class exists but no instance.
                    if (tpmQueryRan == false && !(securityInfo.SpecificCollectionErrors?.ContainsKey("TPM") ?? false))
                    {
                         securityInfo.Tpm.IsPresent = false;
                         securityInfo.Tpm.Status = "Not Present (No WMI Instance)";
                    }
                } else {
                    securityInfo.Tpm.Status = "Requires Admin";
                    securityInfo.Tpm.ErrorMessage = "Requires Admin privileges for WMI TPM query.";
                    securityInfo.AddSpecificError("TPM", "Requires Admin");
                }

            }
             catch(Exception ex) // Catch errors in the overall collection setup
            {
                 Console.Error.WriteLine($"[CRITICAL ERROR] Security Info Collection failed: {ex.Message}");
                 securityInfo.SectionCollectionErrorMessage = $"Critical failure during Security Info collection: {ex.Message}";
            }
            return Task.FromResult(securityInfo);
        }
    }
}