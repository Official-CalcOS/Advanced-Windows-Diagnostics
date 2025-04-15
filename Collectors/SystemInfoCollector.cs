// Collectors/SystemInfoCollector.cs
using System;
using System.Management; // If needed directly, else use helper
using System.Runtime.Versioning;
using System.Threading.Tasks;
using DiagnosticToolAllInOne.Helpers; // Use helpers

namespace DiagnosticToolAllInOne.Collectors
{
    [SupportedOSPlatform("windows")]
    public static class SystemInfoCollector
    {
        // WMI/System Constants specific to this collector if needed
        private const string WMI_CIMV2 = @"root\cimv2";

        public static Task<SystemInfo> CollectAsync(bool isAdmin) // Pass isAdmin flag
        {
            var systemInfo = new SystemInfo { DotNetVersion = Environment.Version.ToString() };

            try // Wrap the whole collection in try/catch
            {
                // --- OS Info ---
                systemInfo.OperatingSystem = new OSInfo();
                 WmiHelper.ProcessWmiResults(
                     WmiHelper.Query("Win32_OperatingSystem", null, WMI_CIMV2),
                     obj => {
                         systemInfo.OperatingSystem.Name = WmiHelper.GetProperty(obj, "Caption");
                         systemInfo.OperatingSystem.Architecture = WmiHelper.GetProperty(obj, "OSArchitecture");
                         systemInfo.OperatingSystem.Version = WmiHelper.GetProperty(obj, "Version");
                         systemInfo.OperatingSystem.BuildNumber = WmiHelper.GetProperty(obj, "BuildNumber");
                         systemInfo.OperatingSystem.InstallDate = WmiHelper.ConvertCimDateTime(WmiHelper.GetProperty(obj, "InstallDate"));
                         systemInfo.OperatingSystem.LastBootTime = WmiHelper.ConvertCimDateTime(WmiHelper.GetProperty(obj, "LastBootUpTime"));
                         if (systemInfo.OperatingSystem.LastBootTime.HasValue)
                         {
                             systemInfo.OperatingSystem.Uptime = DateTime.Now - systemInfo.OperatingSystem.LastBootTime.Value;
                         }
                         systemInfo.OperatingSystem.SystemDrive = WmiHelper.GetProperty(obj, "SystemDrive");
                     },
                      // Corrected: Call AddSpecificError on the instance
                      error => systemInfo.AddSpecificError("OSInfo", error)
                 );

                // --- Computer System Info ---
                systemInfo.ComputerSystem = new ComputerSystemInfo { CurrentUser = Environment.UserName };
                 WmiHelper.ProcessWmiResults(
                      WmiHelper.Query("Win32_ComputerSystem", null, WMI_CIMV2),
                      obj => {
                         systemInfo.ComputerSystem.Manufacturer = WmiHelper.GetProperty(obj, "Manufacturer");
                         systemInfo.ComputerSystem.Model = WmiHelper.GetProperty(obj, "Model");
                         systemInfo.ComputerSystem.SystemType = WmiHelper.GetProperty(obj, "SystemType");
                         systemInfo.ComputerSystem.PartOfDomain = bool.TryParse(WmiHelper.GetProperty(obj, "PartOfDomain"), out bool domain) && domain;
                         systemInfo.ComputerSystem.DomainOrWorkgroup = systemInfo.ComputerSystem.PartOfDomain
                             ? WmiHelper.GetProperty(obj, "Domain")
                             : WmiHelper.GetProperty(obj, "Workgroup", "N/A");
                         systemInfo.ComputerSystem.LoggedInUserWMI = WmiHelper.GetProperty(obj, "UserName"); // User logged in per WMI
                      },
                       // Corrected: Call AddSpecificError on the instance
                       error => systemInfo.AddSpecificError("ComputerSystem", error)
                 );

                // --- Baseboard Info ---
                systemInfo.Baseboard = new BaseboardInfo();
                 WmiHelper.ProcessWmiResults(
                     WmiHelper.Query("Win32_BaseBoard", null, WMI_CIMV2),
                     obj => {
                         systemInfo.Baseboard.Manufacturer = WmiHelper.GetProperty(obj, "Manufacturer");
                         systemInfo.Baseboard.Product = WmiHelper.GetProperty(obj, "Product");
                         systemInfo.Baseboard.SerialNumber = WmiHelper.GetProperty(obj, "SerialNumber", isAdmin ? "N/A" : "Requires Admin");
                         systemInfo.Baseboard.Version = WmiHelper.GetProperty(obj, "Version");
                     },
                      // Corrected: Call AddSpecificError on the instance
                      error => systemInfo.AddSpecificError("Baseboard", error)
                 );

                // --- BIOS Info ---
                systemInfo.BIOS = new BiosInfo();
                 WmiHelper.ProcessWmiResults(
                     WmiHelper.Query("Win32_BIOS", null, WMI_CIMV2),
                     obj => {
                         systemInfo.BIOS.Manufacturer = WmiHelper.GetProperty(obj, "Manufacturer");
                         systemInfo.BIOS.Version = $"{WmiHelper.GetProperty(obj, "Version")} / {WmiHelper.GetProperty(obj, "SMBIOSBIOSVersion")}";
                         systemInfo.BIOS.ReleaseDate = WmiHelper.ConvertCimDateTime(WmiHelper.GetProperty(obj, "ReleaseDate"));
                         systemInfo.BIOS.SerialNumber = WmiHelper.GetProperty(obj, "SerialNumber", isAdmin ? "N/A" : "Requires Admin");
                     },
                      // Corrected: Call AddSpecificError on the instance
                      error => systemInfo.AddSpecificError("BIOS", error)
                 );

                // --- TimeZone Info ---
                systemInfo.TimeZone = new TimeZoneConfig();
                 WmiHelper.ProcessWmiResults(
                     WmiHelper.Query("Win32_TimeZone", null, WMI_CIMV2),
                     obj => {
                         systemInfo.TimeZone.CurrentTimeZone = WmiHelper.GetProperty(obj, "Caption");
                         systemInfo.TimeZone.StandardName = WmiHelper.GetProperty(obj, "StandardName");
                         systemInfo.TimeZone.DaylightName = WmiHelper.GetProperty(obj, "DaylightName");
                         systemInfo.TimeZone.BiasMinutes = int.TryParse(WmiHelper.GetProperty(obj, "Bias"), out int bias) ? bias : null;
                     },
                      // Corrected: Call AddSpecificError on the instance
                      error => systemInfo.AddSpecificError("TimeZone", error)
                 );

                // --- Power Plan ---
                 WmiHelper.ProcessWmiResults(
                     WmiHelper.Query("Win32_PowerPlan", new[] { "InstanceID", "ElementName", "IsActive" }, WMI_CIMV2, "IsActive = True"),
                     obj => {
                         systemInfo.ActivePowerPlan = new PowerPlanInfo
                         {
                             Name = WmiHelper.GetProperty(obj, "ElementName"),
                             InstanceID = WmiHelper.GetProperty(obj, "InstanceID"),
                             IsActive = true
                         };
                     },
                     // Corrected: Call AddSpecificError on the instance
                     error => systemInfo.AddSpecificError("PowerPlan", error)
                 );
                // If ActivePowerPlan is still null after attempt, WMI might have failed or no plan is active
                // Corrected CS8602: Add null check before accessing SpecificCollectionErrors
                if(systemInfo.ActivePowerPlan == null && !(systemInfo.SpecificCollectionErrors?.ContainsKey("PowerPlan") ?? false))
                {
                     // Corrected: Call AddSpecificError on the instance
                     systemInfo.AddSpecificError("PowerPlan", "No active power plan found or query failed silently.");
                }
            }
            catch (Exception ex) // Catch errors in the overall collection setup
            {
                 Console.Error.WriteLine($"[CRITICAL ERROR] System Info Collection failed: {ex.Message}");
                 systemInfo.SectionCollectionErrorMessage = $"Critical failure during System Info collection: {ex.Message}";
            }

            return Task.FromResult(systemInfo); // Return completed task with data
        }
    }
}