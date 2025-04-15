document.addEventListener('DOMContentLoaded', () => {
    const fileInput = document.getElementById('reportFile');
    const loadingStatus = document.getElementById('loading-status');
    const reportSectionsContainer = document.getElementById('report-sections');
    const reportMetadataContainer = document.getElementById('report-metadata');

    fileInput.addEventListener('change', (event) => {
        const file = event.target.files[0];
        if (!file) {
            loadingStatus.textContent = 'No file selected.';
            loadingStatus.className = 'error-message';
            return;
        }

        if (file.type !== 'application/json') {
             loadingStatus.textContent = 'Error: Please select a valid JSON file (.json).';
             loadingStatus.className = 'error-message';
             reportSectionsContainer.innerHTML = '<p>Please select a valid JSON report file.</p>';
             reportMetadataContainer.style.display = 'none';
             return;
         }

        loadingStatus.textContent = `Loading ${file.name}...`;
        loadingStatus.className = ''; // Clear error class
        reportSectionsContainer.innerHTML = '<h2>Loading Report Data...</h2>'; // Clear previous report
        reportMetadataContainer.style.display = 'none'; // Hide metadata while loading

        const reader = new FileReader();

        reader.onload = (e) => {
            try {
                const data = JSON.parse(e.target.result);
                console.log("Report data loaded:", data); // Log data for debugging
                loadingStatus.textContent = `Successfully loaded ${file.name}.`;
                loadingStatus.className = 'info-message'; // Use a different class for success
                displayReport(data);
                reportMetadataContainer.style.display = 'block'; // Show metadata
            } catch (error) {
                console.error('Error parsing JSON report:', error);
                loadingStatus.textContent = `Error parsing ${file.name}: ${error.message}. Ensure the file is valid JSON.`;
                loadingStatus.className = 'error-message';
                reportSectionsContainer.innerHTML = `<div class="error-message">Error parsing report: ${error.message}. Make sure the JSON file is valid.</div>`;
            }
        };

        reader.onerror = (e) => {
            console.error('Error reading file:', e);
            loadingStatus.textContent = `Error reading file ${file.name}.`;
            loadingStatus.className = 'error-message';
            reportSectionsContainer.innerHTML = `<div class="error-message">Error reading the selected file.</div>`;
        };

        reader.readAsText(file);
    });
});

function displayReport(report) {
    // Display Metadata
    document.getElementById('timestamp').textContent = report.ReportTimestamp ? new Date(report.ReportTimestamp).toLocaleString() : 'N/A';
    document.getElementById('ran-as-admin').textContent = report.RanAsAdmin !== undefined ? report.RanAsAdmin : 'N/A';

    const sectionsContainer = document.getElementById('report-sections');
    sectionsContainer.innerHTML = ''; // Clear loading message

    // --- Render each section ---
    // Pass the specific data object and the render function
    if (report.System) renderSection(sectionsContainer, "System Information", report.System, renderSystemInfo);
    if (report.Hardware) renderSection(sectionsContainer, "Hardware Information", report.Hardware, renderHardwareInfo);
    if (report.Software) renderSection(sectionsContainer, "Software & Configuration", report.Software, renderSoftwareInfo);
    if (report.Security) renderSection(sectionsContainer, "Security Information", report.Security, renderSecurityInfo);
    if (report.Performance) renderSection(sectionsContainer, "Performance Snapshot", report.Performance, renderPerformanceInfo);
    if (report.Network) renderSection(sectionsContainer, "Network Information", report.Network, renderNetworkInfo);
    if (report.Events) renderSection(sectionsContainer, "Recent Event Logs", report.Events, renderEventLogInfo);
    // Analysis section might not exist if not selected during run
    renderSection(sectionsContainer, "Analysis Summary", report.Analysis, renderAnalysisSummary); // Render even if null to show message

}

// Generic function to create a section container
function renderSection(container, title, data, renderContentFunc) {
    const sectionDiv = document.createElement('div');
    sectionDiv.className = 'section';
    sectionDiv.id = title.toLowerCase().replace(/[^a-z0-9]+/g, '-'); // Create an ID

    const titleElement = document.createElement('h2');
    titleElement.textContent = title;
    sectionDiv.appendChild(titleElement);

    // Check if data exists for the section
    if (!data) {
         sectionDiv.innerHTML += `<p><i>Section data was not collected or is unavailable.</i></p>`;
         container.appendChild(sectionDiv);
         return; // Don't proceed further for this section
     }

    // Display Overall Section Error (if exists)
    if (data.SectionCollectionErrorMessage) {
        const errorDiv = document.createElement('div');
        errorDiv.className = 'error-message critical-section-error'; // Add specific class
        errorDiv.textContent = `Critical Error collecting this section: ${data.SectionCollectionErrorMessage}`;
        sectionDiv.appendChild(errorDiv);
        // Optionally, stop rendering the content if a critical error occurred
        // container.appendChild(sectionDiv);
        // return;
    }

    // Display Specific Collection Errors (if exists)
    if (data.SpecificCollectionErrors && Object.keys(data.SpecificCollectionErrors).length > 0) {
        const errorList = document.createElement('ul');
        errorList.className = 'specific-errors-list';
        let errorHtml = '<h3>Collection Errors/Warnings:</h3>';
        for (const [key, value] of Object.entries(data.SpecificCollectionErrors)) {
            errorHtml += `<li><strong>${escapeHtml(key)}:</strong> ${escapeHtml(value)}</li>`;
        }
        errorList.innerHTML = errorHtml;
        sectionDiv.appendChild(errorList);
    }


    // Render the actual content using the specific function
    try {
        renderContentFunc(sectionDiv, data);
    } catch (renderError) {
         console.error(`Error rendering section "${title}":`, renderError);
         const errorDiv = document.createElement('div');
         errorDiv.className = 'error-message render-error';
         errorDiv.textContent = `Error displaying this section's content: ${renderError.message}`;
         // Append error below existing content if partial rendering occurred
         sectionDiv.appendChild(errorDiv);
    }


    container.appendChild(sectionDiv);
}

// --- Helper Functions ---
function safeGet(obj, path, defaultValue = 'N/A') {
    if (!obj) return defaultValue;
    // Navigate the path safely
    const value = path.split('.').reduce((o, p) => (o && o.hasOwnProperty(p) && o[p] !== null && o[p] !== undefined) ? o[p] : undefined, obj);
    // Return the value or default, handling empty strings optionally
    const result = value !== undefined ? value : defaultValue;
    // Treat empty string as 'N/A' unless it's the intended default
    // return (result === '' && defaultValue !== '') ? 'N/A' : result;
     return result; // Keep empty strings for now
}

function formatBytes(bytes) {
    if (bytes === 0 || typeof bytes !== 'number') return '0 B';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB', 'TB', 'PB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
}

function formatNullableDateTime(dateString, localeOptions = undefined) {
    if (!dateString) return 'N/A';
    try {
        return new Date(dateString).toLocaleString(undefined, localeOptions);
    } catch (e) {
        return 'Invalid Date';
    }
}

// Basic HTML escaping
function escapeHtml(unsafe) {
    if (!unsafe || typeof unsafe !== 'string') return unsafe;
    return unsafe
         .replace(/&/g, "&amp;")
         .replace(/</g, "&lt;")
         .replace(/>/g, "&gt;")
         .replace(/"/g, "&quot;")
         .replace(/'/g, "&#039;");
}

function createTable(headers, dataRows) {
    let tableHtml = '<table><thead><tr>';
    headers.forEach(h => tableHtml += `<th>${escapeHtml(h)}</th>`);
    tableHtml += '</tr></thead><tbody>';
    dataRows.forEach(row => {
        tableHtml += '<tr>';
        row.forEach(cell => tableHtml += `<td>${escapeHtml(cell ?? 'N/A')}</td>`);
        tableHtml += '</tr>';
    });
    tableHtml += '</tbody></table>';
    return tableHtml;
}

// --- Specific Section Rendering Functions (Implementations) ---

function renderSystemInfo(container, data) {
    let html = '<h3>Operating System</h3>';
    const os = data.OperatingSystem;
    if (os) {
        html += `<p><strong>Name:</strong> ${escapeHtml(safeGet(os, 'Name'))} (${escapeHtml(safeGet(os, 'Architecture'))})</p>
                 <p><strong>Version:</strong> ${escapeHtml(safeGet(os, 'Version'))} (Build: ${escapeHtml(safeGet(os, 'BuildNumber'))})</p>
                 <p><strong>Install Date:</strong> ${formatNullableDateTime(safeGet(os, 'InstallDate'))}</p>
                 <p><strong>Last Boot Time:</strong> ${formatNullableDateTime(safeGet(os, 'LastBootTime'))}</p>
                 <p><strong>System Uptime:</strong> ${safeGet(os, 'Uptime') ? formatTimespan(safeGet(os, 'Uptime')) : 'N/A'}</p>
                 <p><strong>System Drive:</strong> ${escapeHtml(safeGet(os, 'SystemDrive'))}</p>`;
    } else { html += '<p><i>Data unavailable.</i></p>'; }

    html += '<h3>Computer System</h3>';
    const cs = data.ComputerSystem;
    if (cs) {
        html += `<p><strong>Manufacturer:</strong> ${escapeHtml(safeGet(cs, 'Manufacturer'))}</p>
                 <p><strong>Model:</strong> ${escapeHtml(safeGet(cs, 'Model'))} (${escapeHtml(safeGet(cs, 'SystemType'))})</p>
                 <p><strong>Domain/Workgroup:</strong> ${escapeHtml(safeGet(cs, 'DomainOrWorkgroup'))} (PartOfDomain: ${safeGet(cs, 'PartOfDomain')})</p>
                 <p><strong>Executing User:</strong> ${escapeHtml(safeGet(cs, 'CurrentUser'))}</p>
                 <p><strong>Logged In User (WMI):</strong> ${escapeHtml(safeGet(cs, 'LoggedInUserWMI'))}</p>`;
    } else { html += '<p><i>Data unavailable.</i></p>'; }

     html += '<h3>Baseboard (Motherboard)</h3>';
     const bb = data.Baseboard;
     if(bb) {
          html += `<p><strong>Manufacturer:</strong> ${escapeHtml(safeGet(bb, 'Manufacturer'))}</p>
                   <p><strong>Product:</strong> ${escapeHtml(safeGet(bb, 'Product'))}</p>
                   <p><strong>Serial:</strong> ${escapeHtml(safeGet(bb, 'SerialNumber'))}</p>
                   <p><strong>Version:</strong> ${escapeHtml(safeGet(bb, 'Version'))}</p>`;
     } else { html += '<p><i>Data unavailable.</i></p>'; }

     html += '<h3>BIOS</h3>';
     const bios = data.BIOS;
     if(bios) {
         html += `<p><strong>Manufacturer:</strong> ${escapeHtml(safeGet(bios, 'Manufacturer'))}</p>
                  <p><strong>Version:</strong> ${escapeHtml(safeGet(bios, 'Version'))}</p>
                  <p><strong>Release Date:</strong> ${formatNullableDateTime(safeGet(bios, 'ReleaseDate'), { year: 'numeric', month: 'short', day: 'numeric' })}</p>
                  <p><strong>Serial:</strong> ${escapeHtml(safeGet(bios, 'SerialNumber'))}</p>`;
     } else { html += '<p><i>Data unavailable.</i></p>'; }

    html += '<h3>Time Zone</h3>';
    const tz = data.TimeZone;
    if(tz) {
         html += `<p><strong>Current Time Zone:</strong> ${escapeHtml(safeGet(tz, 'CurrentTimeZone'))}</p>
                  <p><strong>UTC Offset (Mins):</strong> ${escapeHtml(safeGet(tz, 'BiasMinutes'))}</p>`;
     } else { html += '<p><i>Data unavailable.</i></p>'; }

     html += '<h3>Power Plan</h3>';
     const pp = data.ActivePowerPlan;
     if(pp) {
          html += `<p><strong>Active Plan:</strong> ${escapeHtml(safeGet(pp, 'Name'))} (${escapeHtml(safeGet(pp, 'InstanceID'))})</p>`;
     } else { html += '<p><i>Data unavailable.</i></p>'; }

     html += `<p><strong>.NET Runtime:</strong> ${escapeHtml(safeGet(data, 'DotNetVersion'))}</p>`;


    container.innerHTML += html; // Append accumulated HTML
}


function renderHardwareInfo(container, data) {
    let html = '<h3>Processors</h3>';
    if (data.Processors && data.Processors.length > 0) {
        data.Processors.forEach(cpu => {
            html += `<div>
                        <p><strong>${escapeHtml(safeGet(cpu, 'Name'))}</strong></p>
                        <ul>
                            <li>Socket: ${escapeHtml(safeGet(cpu, 'Socket'))}, Cores: ${escapeHtml(safeGet(cpu, 'Cores'))}, Logical Processors: ${escapeHtml(safeGet(cpu, 'LogicalProcessors'))}</li>
                            <li>Max Speed: ${escapeHtml(safeGet(cpu, 'MaxSpeedMHz'))} MHz, L2 Cache: ${escapeHtml(safeGet(cpu, 'L2Cache'))}, L3 Cache: ${escapeHtml(safeGet(cpu, 'L3Cache'))}</li>
                        </ul>
                    </div>`;
        });
    } else { html += '<p><i>Data unavailable or none found.</i></p>'; }

    html += '<h3>Memory (RAM)</h3>';
    const mem = data.Memory;
    if (mem) {
        html += `<p><strong>Total Visible:</strong> ${escapeHtml(safeGet(mem, 'TotalVisible'))}</p>
                 <p><strong>Available:</strong> ${escapeHtml(safeGet(mem, 'Available'))}</p>
                 <p><strong>Used:</strong> ${escapeHtml(safeGet(mem, 'Used'))} (${escapeHtml(safeGet(mem, 'PercentUsed', 0).toFixed(2))}%)</p>`;
        if (mem.Modules && mem.Modules.length > 0) {
            html += '<h4>Physical Modules:</h4><ul>';
            mem.Modules.forEach(mod => {
                html += `<li>[${escapeHtml(safeGet(mod, 'DeviceLocator'))}] ${escapeHtml(safeGet(mod, 'Capacity'))} @ ${escapeHtml(safeGet(mod, 'SpeedMHz'))}MHz (${escapeHtml(safeGet(mod, 'MemoryType'))} / ${escapeHtml(safeGet(mod, 'FormFactor'))}) - Mfg: ${escapeHtml(safeGet(mod, 'Manufacturer'))}, Part#: ${escapeHtml(safeGet(mod, 'PartNumber'))}</li>`;
            });
            html += '</ul>';
        } else { html += '<p><i>Physical Modules: Data unavailable or none found.</i></p>'; }
    } else { html += '<p><i>Data unavailable.</i></p>'; }

     html += '<h3>Physical Disks</h3>';
     if(data.PhysicalDisks && data.PhysicalDisks.length > 0) {
         html += '<ul>';
         data.PhysicalDisks.forEach(disk => {
              html += `<li><strong>Disk #${escapeHtml(safeGet(disk, 'Index'))}: ${escapeHtml(safeGet(disk, 'Model'))}</strong>
                       <ul>
                           <li>Interface: ${escapeHtml(safeGet(disk, 'InterfaceType'))}, Size: ${escapeHtml(safeGet(disk, 'Size'))}, Partitions: ${escapeHtml(safeGet(disk, 'Partitions'))}, Serial: ${escapeHtml(safeGet(disk, 'SerialNumber'))}</li>
                           <li>Media: ${escapeHtml(safeGet(disk, 'MediaType'))}, Status: ${escapeHtml(safeGet(disk, 'Status'))}</li>
                           <li>SMART Status: ${renderSmartStatus(disk.SmartStatus)}</li>
                       </ul></li>`;
         });
         html += '</ul>';
     } else { html += '<p><i>Data unavailable or none found.</i></p>'; }

     html += '<h3>Logical Disks (Local Fixed)</h3>';
     if(data.LogicalDisks && data.LogicalDisks.length > 0) {
         html += '<ul>';
         data.LogicalDisks.forEach(ldisk => {
              html += `<li><strong>${escapeHtml(safeGet(ldisk, 'DeviceID'))} (${escapeHtml(safeGet(ldisk, 'VolumeName'))}) - ${escapeHtml(safeGet(ldisk, 'FileSystem'))}</strong>: Size ${escapeHtml(safeGet(ldisk, 'Size'))}, Free ${escapeHtml(safeGet(ldisk, 'FreeSpace'))} (${escapeHtml(safeGet(ldisk, 'PercentFree', 0).toFixed(1))}%)</li>`;
         });
         html += '</ul>';
     } else { html += '<p><i>Data unavailable or none found.</i></p>'; }

      html += '<h3>Volumes</h3>';
     if(data.Volumes && data.Volumes.length > 0) {
          html += '<ul>';
          data.Volumes.forEach(vol => {
               html += `<li><strong>${escapeHtml(safeGet(vol, 'DriveLetter', 'N/A'))} (${escapeHtml(safeGet(vol, 'Name', 'No Name'))}) - ${escapeHtml(safeGet(vol, 'FileSystem'))}</strong>: Capacity ${escapeHtml(safeGet(vol, 'Capacity'))}, Free ${escapeHtml(safeGet(vol, 'FreeSpace'))} (BitLocker: ${escapeHtml(safeGet(vol, 'ProtectionStatus'))})</li>`;
          });
          html += '</ul>';
     } else { html += '<p><i>Data unavailable or none found.</i></p>'; }

      html += '<h3>Video Controllers (GPU)</h3>';
     if(data.Gpus && data.Gpus.length > 0) {
         html += '<ul>';
         data.Gpus.forEach(gpu => {
             html += `<li><strong>${escapeHtml(safeGet(gpu, 'Name'))}</strong> (Status: ${escapeHtml(safeGet(gpu, 'Status'))})
                      <ul>
                          <li>VRAM: ${escapeHtml(safeGet(gpu, 'Vram'))}, Processor: ${escapeHtml(safeGet(gpu, 'VideoProcessor'))}</li>
                          <li>Driver: ${escapeHtml(safeGet(gpu, 'DriverVersion'))} (${formatNullableDateTime(safeGet(gpu, 'DriverDate'), { year: 'numeric', month: 'short', day: 'numeric' })})</li>
                          <li>Resolution: ${escapeHtml(safeGet(gpu, 'CurrentResolution'))}</li>
                      </ul></li>`;
         });
         html += '</ul>';
     } else { html += '<p><i>Data unavailable or none found.</i></p>'; }

     // Add Monitors, AudioDevices similarly

    container.innerHTML += html;
}

function renderSmartStatus(smartStatus) {
    if (!smartStatus) return 'N/A';
    let statusText = escapeHtml(safeGet(smartStatus, 'StatusText', 'Unknown'));
    if (smartStatus.IsFailurePredicted) statusText = `<strong style="color:red;">${statusText}</strong>`;
    if (safeGet(smartStatus, 'ReasonCode', '0') !== '0') statusText += ` (Reason: ${escapeHtml(safeGet(smartStatus, 'ReasonCode'))})`;
     // Append basic status if SMART isn't OK and the basic status differs
    if (smartStatus.StatusText !== 'OK' && safeGet(smartStatus, 'BasicStatusFromDiskDrive') !== 'N/A') {
        statusText += ` (Basic HW Status: ${escapeHtml(safeGet(smartStatus, 'BasicStatusFromDiskDrive'))})`;
    }
    if (smartStatus.Error) statusText += ` <span class="error-inline">[Error: ${escapeHtml(smartStatus.Error)}]</span>`;
    return statusText;
}

function renderSoftwareInfo(container, data) {
     let html = '<h3>Installed Applications</h3>';
     if (data.InstalledApplications && data.InstalledApplications.length > 0) {
        html += `<p>Count: ${data.InstalledApplications.length}</p>`;
        const headers = ['Name', 'Version', 'Publisher', 'Install Date'];
        const rows = data.InstalledApplications.slice(0, 50).map(app => [ // Limit rows for performance
             safeGet(app, 'Name'),
             safeGet(app, 'Version'),
             safeGet(app, 'Publisher'),
             formatNullableDateTime(safeGet(app, 'InstallDate'), { year: 'numeric', month: 'short', day: 'numeric' })
        ]);
        html += createTable(headers, rows);
        if (data.InstalledApplications.length > 50) html += '<p><i>(Showing first 50 applications)</i></p>';
     } else { html += '<p><i>Data unavailable or none found.</i></p>'; }

     html += '<h3>Windows Updates (Hotfixes)</h3>';
     if (data.WindowsUpdates && data.WindowsUpdates.length > 0) {
         html += '<ul>';
         data.WindowsUpdates.forEach(upd => {
             html += `<li><strong>${escapeHtml(safeGet(upd, 'HotFixID'))}</strong> (${escapeHtml(safeGet(upd, 'Description'))}) - Installed: ${formatNullableDateTime(safeGet(upd, 'InstalledOn'))}</li>`;
         });
         html += '</ul>';
     } else { html += '<p><i>Data unavailable or none found.</i></p>'; }

     html += '<h3>Relevant Services</h3>';
     if (data.RelevantServices && data.RelevantServices.length > 0) {
         const headers = ['Name', 'Display Name', 'State', 'Start Mode', 'Path'];
         const rows = data.RelevantServices.map(svc => [
              safeGet(svc, 'Name'),
              safeGet(svc, 'DisplayName'),
              safeGet(svc, 'State'),
              safeGet(svc, 'StartMode'),
              safeGet(svc, 'PathName')
         ]);
         html += createTable(headers, rows);
     } else { html += '<p><i>Data unavailable or none found.</i></p>'; }

     html += '<h3>Startup Programs</h3>';
     if (data.StartupPrograms && data.StartupPrograms.length > 0) {
         html += '<ul>';
         data.StartupPrograms.forEach(prog => {
             html += `<li>[${escapeHtml(safeGet(prog, 'Location'))}] <strong>${escapeHtml(safeGet(prog, 'Name'))}</strong> = ${escapeHtml(safeGet(prog, 'Command'))}</li>`;
         });
         html += '</ul>';
     } else { html += '<p><i>Data unavailable or none found.</i></p>'; }

     // Environment Variables (simplified display)
     html += '<h3>Environment Variables</h3>';
     if (data.SystemEnvironmentVariables) {
         html += '<h4>System:</h4><ul>';
         Object.entries(data.SystemEnvironmentVariables).slice(0, 20).forEach(([key, value]) => html += `<li><strong>${escapeHtml(key)}</strong>=${escapeHtml(value)}</li>`);
         if (Object.keys(data.SystemEnvironmentVariables).length > 20) html += '<li>... more</li>';
         html += '</ul>';
     }
     if (data.UserEnvironmentVariables) {
         html += '<h4>User:</h4><ul>';
         Object.entries(data.UserEnvironmentVariables).slice(0, 20).forEach(([key, value]) => html += `<li><strong>${escapeHtml(key)}</strong>=${escapeHtml(value)}</li>`);
          if (Object.keys(data.UserEnvironmentVariables).length > 20) html += '<li>... more</li>';
         html += '</ul>';
     }


    container.innerHTML += html;
}

function renderSecurityInfo(container, data) {
     let html = `<h3>Overview</h3>
                 <p><strong>Running as Admin:</strong> ${escapeHtml(safeGet(data, 'IsAdmin'))}</p>
                 <p><strong>UAC Status:</strong> ${escapeHtml(safeGet(data, 'UacStatus'))}</p>
                 <p><strong>Antivirus:</strong> ${escapeHtml(safeGet(data, 'Antivirus.Name'))} (State: ${escapeHtml(safeGet(data, 'Antivirus.State'))})</p>
                 <p><strong>Firewall:</strong> ${escapeHtml(safeGet(data, 'Firewall.Name'))} (State: ${escapeHtml(safeGet(data, 'Firewall.State'))})</p>`;

     html += '<h3>Local Users</h3>';
     if(data.LocalUsers && data.LocalUsers.length > 0) {
         html += '<ul>';
         data.LocalUsers.forEach(user => {
             html += `<li><strong>${escapeHtml(safeGet(user, 'Name'))}</strong> (SID: ${escapeHtml(safeGet(user, 'SID'))}) - Disabled: ${escapeHtml(safeGet(user, 'IsDisabled'))}, PwdReq: ${escapeHtml(safeGet(user, 'PasswordRequired'))}</li>`;
         });
         html += '</ul>';
     } else { html += '<p><i>Data unavailable or none found.</i></p>'; }

      html += '<h3>Local Groups</h3>';
     if(data.LocalGroups && data.LocalGroups.length > 0) {
         html += '<ul>';
         data.LocalGroups.slice(0,15).forEach(grp => { // Limit groups shown
             html += `<li><strong>${escapeHtml(safeGet(grp, 'Name'))}</strong> - ${escapeHtml(safeGet(grp, 'Description'))}</li>`;
         });
          if(data.LocalGroups.length > 15) html += '<li>... more</li>';
         html += '</ul>';
     } else { html += '<p><i>Data unavailable or none found.</i></p>'; }

     html += '<h3>Network Shares</h3>';
      if(data.NetworkShares && data.NetworkShares.length > 0) {
          html += '<ul>';
          data.NetworkShares.forEach(share => {
              html += `<li><strong>${escapeHtml(safeGet(share, 'Name'))}</strong> -> ${escapeHtml(safeGet(share, 'Path'))} (${escapeHtml(safeGet(share, 'Description'))})</li>`;
          });
          html += '</ul>';
      } else { html += '<p><i>Data unavailable or none found.</i></p>'; }


    container.innerHTML += html;
}

function renderPerformanceInfo(container, data) {
     let html = `<h3>Counters (Sampled)</h3>
                 <p><strong>CPU Usage:</strong> ${escapeHtml(safeGet(data, 'OverallCpuUsagePercent'))} %</p>
                 <p><strong>Available Memory:</strong> ${escapeHtml(safeGet(data, 'AvailableMemoryMB'))} MB</p>
                 <p><strong>Disk Queue Length:</strong> ${escapeHtml(safeGet(data, 'TotalDiskQueueLength'))}</p>`;

      html += '<h3>Top Processes by Memory (Working Set)</h3>';
      if(data.TopMemoryProcesses && data.TopMemoryProcesses.length > 0) {
          const headers = ['PID', 'Name', 'Memory', 'Status', 'Error'];
          const rows = data.TopMemoryProcesses.map(p => [
               safeGet(p, 'Pid'), safeGet(p, 'Name'), safeGet(p, 'MemoryUsage'), safeGet(p, 'Status'), safeGet(p, 'Error', '')
          ]);
          html += createTable(headers, rows);
      } else { html += '<p><i>Data unavailable or none found.</i></p>'; }

      html += '<h3>Top Processes by CPU Time (Snapshot)</h3>';
       if(data.TopCpuProcesses && data.TopCpuProcesses.length > 0) {
          const headers = ['PID', 'Name', 'Memory', 'Status', 'Error'];
           const rows = data.TopCpuProcesses.map(p => [
               safeGet(p, 'Pid'), safeGet(p, 'Name'), safeGet(p, 'MemoryUsage'), safeGet(p, 'Status'), safeGet(p, 'Error', '')
          ]);
          html += createTable(headers, rows);
      } else { html += '<p><i>Data unavailable or none found.</i></p>'; }


    container.innerHTML += html;
}

function renderNetworkInfo(container, data) {
     let html = '<h3>Network Adapters</h3>';
    if (data.Adapters && data.Adapters.length > 0) {
        data.Adapters.forEach(nic => {
             html += `<div>
                        <h4>${escapeHtml(safeGet(nic,'Name'))} (${escapeHtml(safeGet(nic, 'Description'))})</h4>
                        <ul>
                            <li>Status: ${escapeHtml(safeGet(nic, 'Status'))}, Type: ${escapeHtml(safeGet(nic, 'Type'))}, Speed: ${escapeHtml(safeGet(nic, 'SpeedMbps'))} Mbps</li>
                            <li>MAC: ${escapeHtml(safeGet(nic, 'MacAddress'))}</li>
                            <li>IPs: ${escapeHtml(safeGet(nic, 'IpAddresses', []).join(', '))}</li>
                            <li>Gateways: ${escapeHtml(safeGet(nic, 'Gateways', []).join(', '))}</li>
                            <li>DNS: ${escapeHtml(safeGet(nic, 'DnsServers', []).join(', '))}</li>
                            <li>DHCP Enabled: ${escapeHtml(safeGet(nic, 'DhcpEnabled'))} (Lease Obtained: ${formatNullableDateTime(safeGet(nic, 'DhcpLeaseObtained'))}, Expires: ${formatNullableDateTime(safeGet(nic, 'DhcpLeaseExpires'))})</li>
                        </ul>
                    </div>`;
        });
    } else { html += '<p><i>Data unavailable or none found.</i></p>'; }

    const renderListeners = (title, listeners) => {
        html += `<h3>Active ${title} Listeners</h3>`;
        if (listeners && listeners.length > 0) {
            const headers = ['Local Address', 'Port', 'PID', 'Process Name / Error'];
             const rows = listeners.map(l => [
                 safeGet(l, 'LocalAddress'),
                 safeGet(l, 'LocalPort'),
                 safeGet(l, 'OwningPid', '-'),
                 safeGet(l, 'OwningProcessName', 'N/A (Requires Admin/Advanced API)')
             ]);
             html += createTable(headers, rows);
        } else { html += '<p><i>Data unavailable or none found.</i></p>'; }
    };

    renderListeners('TCP', data.ActiveTcpListeners);
    renderListeners('UDP', data.ActiveUdpListeners);

    html += '<h3>Connectivity Tests</h3>';
    const tests = data.ConnectivityTests;
    if(tests) {
        const renderPing = (pingResult, defaultName) => {
            if (!pingResult) return `<li>Ping ${defaultName}: N/A</li>`;
            let pingHtml = `<li>Ping <strong>${escapeHtml(safeGet(pingResult, 'Target', defaultName))}</strong>: Status ${escapeHtml(safeGet(pingResult, 'Status'))}`;
            if (safeGet(pingResult, 'Status') === 'Success') pingHtml += ` (${escapeHtml(safeGet(pingResult, 'RoundtripTimeMs'))}ms)`;
            if (pingResult.Error) pingHtml += ` <span class="error-inline">[Error: ${escapeHtml(pingResult.Error)}]</span>`;
            pingHtml += '</li>';
            return pingHtml;
        };
        html += '<ul>';
        html += renderPing(tests.GatewayPing, 'Default Gateway');
        if (tests.DnsPings) tests.DnsPings.forEach(p => html += renderPing(p));
        html += '</ul>';

        if (tests.TracerouteResults) {
            html += `<h4>Traceroute to ${escapeHtml(safeGet(tests, 'TracerouteTarget'))}</h4>`;
            const headers = ['Hop', 'Time (ms)', 'Address', 'Status'];
             const rows = tests.TracerouteResults.map(h => [
                 safeGet(h, 'Hop'), safeGet(h, 'RoundtripTimeMs', '*'), safeGet(h, 'Address', '*'), safeGet(h, 'Status')
             ]);
             html += createTable(headers, rows);
        }

    } else { html += '<p><i>Data unavailable or none found.</i></p>'; }


    container.innerHTML += html;
}

function renderEventLogInfo(container, data) {
    const renderLog = (title, entries) => {
        let logHtml = `<h3>${title} Log (Recent Errors/Warnings)</h3>`;
        if (entries && entries.length > 0) {
             // Handle collector messages
             if (entries.length === 1 && !safeGet(entries[0], 'Source', null)) {
                 logHtml += `<p><i>${escapeHtml(safeGet(entries[0], 'Message'))}</i></p>`;
             } else {
                 logHtml += '<ul>';
                 const actualEntries = entries.filter(e => safeGet(e, 'Source', null));
                 actualEntries.slice(0, 20).forEach(entry => { // Limit entries shown
                    let msg = escapeHtml(safeGet(entry, 'Message'));
                    if (msg.length > 150) msg = msg.substring(0, 147) + '...';
                    logHtml += `<li>${formatNullableDateTime(safeGet(entry, 'TimeGenerated'))}: [${escapeHtml(safeGet(entry, 'EntryType'))}] ${escapeHtml(safeGet(entry, 'Source'))} (ID: ${escapeHtml(safeGet(entry, 'InstanceId'))}) - ${msg}</li>`;
                 });
                 if (actualEntries.length > 20) logHtml += '<li>... (more entries exist)</li>';
                  if (actualEntries.length === 0 && entries.length > 0 && !entries[0].Source) {
                      // This case means only collector messages existed, already handled above
                  } else if (actualEntries.length === 0) {
                     logHtml += '<li>No actual Error/Warning event entries found.</li>';
                  }
                 logHtml += '</ul>';
             }
        } else { logHtml += '<p><i>Data unavailable or none found.</i></p>'; }
        return logHtml;
    };

    container.innerHTML += renderLog('System', data.SystemLogEntries);
    container.innerHTML += renderLog('Application', data.ApplicationLogEntries);
}

function renderAnalysisSummary(container, data) {
     container.classList.add('analysis-section'); // Add class for specific styling
     let html = '';
     if (!data) {
         html += '<p><i>Analysis was not performed or data is unavailable.</i></p>';
         container.innerHTML += html;
         return;
     }

      // Display analysis errors first
     if (data.SectionCollectionErrorMessage) {
         html += `<div class="error-message critical-section-error">Analysis Error: ${escapeHtml(data.SectionCollectionErrorMessage)}</div>`;
     }


    if (data.PotentialIssues && data.PotentialIssues.length > 0) {
        html += `<h3 class="issues">Potential Issues Found</h3><ul>`;
        data.PotentialIssues.forEach(issue => { html += `<li>${escapeHtml(issue)}</li>`; });
        html += `</ul>`;
    }
     if (data.Suggestions && data.Suggestions.length > 0) {
        html += `<h3 class="suggestions">Suggestions</h3><ul>`;
        data.Suggestions.forEach(sugg => { html += `<li>${escapeHtml(sugg)}</li>`; });
        html += `</ul>`;
    }
     if (data.Info && data.Info.length > 0) {
        html += `<h3 class="info">Informational Notes</h3><ul>`;
        data.Info.forEach(info => { html += `<li>${escapeHtml(info)}</li>`; });
        html += `</ul>`;
    }
    if (!data.PotentialIssues?.length && !data.Suggestions?.length && !data.Info?.length && !data.SectionCollectionErrorMessage) {
         html += `<p>No specific issues, suggestions, or notes generated by the analysis.</p>`;
    }
     container.innerHTML += html;
}

// Helper to format TimeSpan object (like OS Uptime)
function formatTimespan(timespanObject) {
    if (!timespanObject) return 'N/A';
    // Assuming timespanObject is like { Days: d, Hours: h, Minutes: m, Seconds: s, TotalDays: td, ... }
    return `${safeGet(timespanObject, 'Days', 0)}d ${safeGet(timespanObject, 'Hours', 0)}h ${safeGet(timespanObject, 'Minutes', 0)}m ${safeGet(timespanObject, 'Seconds', 0)}s`;
}