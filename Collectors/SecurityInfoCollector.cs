using Microsoft.Win32;
using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using DiagnosticToolAllInOne.Helpers;

namespace DiagnosticToolAllInOne.Collectors
{
    [SupportedOSPlatform("windows")]
    public static class SecurityInfoCollector
    {
        private const string WMI_CIMV2 = @"root\cimv2";
        private const string WMI_SECURITY_CENTER2 = @"root\SecurityCenter2";

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
                     // Corrected: Call AddSpecificError on the instance
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
                        // Corrected: Call AddSpecificError on the instance
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
                         // Corrected: Call AddSpecificError on the instance
                         error => securityInfo.AddSpecificError("Firewall", error)
                    );

                    // Set default message if object is still null after query attempt
                    // Corrected CS8602: Add null check for SpecificCollectionErrors
                    if (securityInfo.Antivirus == null && !(securityInfo.SpecificCollectionErrors?.ContainsKey("Antivirus") ?? false))
                       securityInfo.Antivirus = new AntivirusInfo { State = "Not Found or WMI Access Denied" };
                    // Corrected CS8602: Add null check for SpecificCollectionErrors
                    if (securityInfo.Firewall == null && !(securityInfo.SpecificCollectionErrors?.ContainsKey("Firewall") ?? false))
                       securityInfo.Firewall = new FirewallInfo { State = "Not Found or WMI Access Denied" };
                } else {
                    securityInfo.Antivirus = new AntivirusInfo { State = "Requires Admin" };
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
                      // Corrected: Call AddSpecificError on the instance
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
                      // Corrected: Call AddSpecificError on the instance
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
                      // Corrected: Call AddSpecificError on the instance
                      error => securityInfo.AddSpecificError("NetworkShares", error)
                 );
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