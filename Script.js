document.addEventListener('DOMContentLoaded', () => {
    const fileInput = document.getElementById('reportFile');
    const loadingStatus = document.getElementById('loading-status');
    const reportSectionsContainer = document.getElementById('report-sections');
    const reportMetadataContainer = document.getElementById('report-metadata');
    const timestampSpan = document.getElementById('timestamp');
    const ranAsAdminSpan = document.getElementById('ran-as-admin');
    const configSourceSpan = document.getElementById('config-source');

    fileInput.addEventListener('change', handleFileSelect);

    // --- Event Delegation for Collapsible Headers ---
    // Attach listener to the container holding the sections
    reportSectionsContainer.addEventListener('click', function(event) {
        // Check if the clicked element is a toggle header
        if (event.target && event.target.classList.contains('collapsible-toggle')) {
            toggleCollapsible(event.target);
        }
    });
    // Also add listener for the initial metadata section toggle
     reportMetadataContainer.previousElementSibling.addEventListener('click', function(event) {
         if (event.target && event.target.classList.contains('collapsible-toggle')) {
             toggleCollapsible(event.target);
         }
     });

});

// --- File Handling ---
function handleFileSelect(event) {
    const file = event.target.files[0];
    const loadingStatus = document.getElementById('loading-status');
    const reportSectionsContainer = document.getElementById('report-sections');
    const reportMetadataContainer = document.getElementById('report-metadata');

    // Clear previous report and status
    reportSectionsContainer.innerHTML = '';
    reportMetadataContainer.style.display = 'none';
    loadingStatus.textContent = '';
    loadingStatus.className = 'status-message'; // Reset class

    if (!file) {
        loadingStatus.textContent = 'No file selected.';
        loadingStatus.classList.add('error');
        return;
    }

    // Basic MIME type check (less reliable than extension but good fallback)
    if (!file.type.includes('json') && !file.name.toLowerCase().endsWith('.json')) {
         loadingStatus.textContent = 'Error: Please select a valid JSON file (.json).';
         loadingStatus.classList.add('error');
         reportSectionsContainer.innerHTML = '<p class="placeholder-text error">Please select a valid JSON report file.</p>';
         return;
     }

    loadingStatus.textContent = `Loading ${file.name}...`;
    loadingStatus.classList.add('info');
    reportSectionsContainer.innerHTML = '<h2>Loading Report Data...</h2>'; // Loading indicator

    const reader = new FileReader();

    reader.onload = (e) => {
        try {
            const data = JSON.parse(e.target.result);
            console.log("Report data loaded:", data); // Log data for debugging
            loadingStatus.textContent = `Successfully loaded ${file.name}.`;
            loadingStatus.classList.remove('info', 'error');
            loadingStatus.classList.add('success');
            displayReport(data);
            reportMetadataContainer.style.display = 'block'; // Show metadata container (content is inside)
        } catch (error) {
            console.error('Error parsing JSON report:', error);
            loadingStatus.textContent = `Error parsing ${file.name}: ${error.message}. Ensure the file is valid JSON.`;
            loadingStatus.classList.remove('info', 'success');
            loadingStatus.classList.add('error');
            reportSectionsContainer.innerHTML = `<div class="error-message critical-section-error">Error parsing report: ${error.message}. Make sure the JSON file is valid.</div>`;
        }
    };

    reader.onerror = (e) => {
        console.error('Error reading file:', e);
        loadingStatus.textContent = `Error reading file ${file.name}.`;
         loadingStatus.classList.remove('info', 'success');
        loadingStatus.classList.add('error');
        reportSectionsContainer.innerHTML = `<div class="error-message critical-section-error">Error reading the selected file.</div>`;
    };

    reader.readAsText(file);
}


// --- Report Display ---
function displayReport(report) {
    // Display Metadata
    document.getElementById('timestamp').textContent = report.ReportTimestamp ? new Date(report.ReportTimestamp).toLocaleString() : 'N/A';
    document.getElementById('ran-as-admin').textContent = report.RanAsAdmin !== undefined ? report.RanAsAdmin : 'N/A';
    // Determine config source (simple check)
    document.getElementById('config-source').textContent = report.Configuration ? (report.Configuration.Source || 'Loaded (appsettings.json or defaults)') : 'Defaults/Not Included';

    const sectionsContainer = document.getElementById('report-sections');
    sectionsContainer.innerHTML = ''; // Clear loading message

    // --- Render each section ---
    // Order can be adjusted here
    renderSection(sectionsContainer, "Analysis Summary", report.Analysis, renderAnalysisSummary); // Analysis first?
    renderSection(sectionsContainer, "System Information", report.System, renderSystemInfo);
    renderSection(sectionsContainer, "Hardware Information", report.Hardware, renderHardwareInfo);
    renderSection(sectionsContainer, "Security Information", report.Security, renderSecurityInfo); // Security before Software?
    renderSection(sectionsContainer, "Software & Configuration", report.Software, renderSoftwareInfo);
    renderSection(sectionsContainer, "Network Information", report.Network, renderNetworkInfo);
    renderSection(sectionsContainer, "Performance Snapshot", report.Performance, renderPerformanceInfo);
    renderSection(sectionsContainer, "Recent Event Logs", report.Events, renderEventLogInfo);

    // Make all sections collapsible after rendering
    // Already handled by event delegation on reportSectionsContainer
}

// --- Collapsible Logic ---
function toggleCollapsible(headerElement) {
    headerElement.classList.toggle('active');
    const content = headerElement.nextElementSibling; // Get the div right after the h2
    if (content && content.classList.contains('collapsible-content')) {
        if (content.style.maxHeight && content.style.maxHeight !== '0px') {
             content.style.maxHeight = '0px'; // Collapse
             content.style.paddingTop = '0'; // Remove padding when collapsed
             content.style.paddingBottom = '0';
        } else {
             // Expand: Set max-height to content's scroll height + padding/margins
             content.style.maxHeight = content.scrollHeight + 40 + "px"; // Add buffer for padding
             content.style.paddingTop = null; // Restore default padding
             content.style.paddingBottom = null;
        }
    }
}


// --- Generic Section Rendering ---
function renderSection(container, title, data, renderContentFunc) {
    const sectionDiv = document.createElement('div');
    sectionDiv.className = 'section collapsible'; // Add collapsible class
    const sectionId = title.toLowerCase().replace(/[^a-z0-9]+/g, '-'); // Create an ID
    sectionDiv.id = sectionId;

    // Make the H2 the toggle trigger
    const titleElement = document.createElement('h2');
    titleElement.className = 'collapsible-toggle'; // Class to identify toggle headers
    titleElement.textContent = title;
    sectionDiv.appendChild(titleElement);

    // Content div that will be collapsed/expanded
    const contentDiv = document.createElement('div');
    contentDiv.className = 'collapsible-content';
    sectionDiv.appendChild(contentDiv); // Add content div

    // --- Content Population ---
    if (!data) {
         contentDiv.innerHTML = `<p class="info-message"><i>Section data was not collected or is unavailable.</i></p>`;
         container.appendChild(sectionDiv);
         return; // Don't proceed further for this section
     }

    // Display Overall Section Error (if exists)
    if (data.SectionCollectionErrorMessage) {
        const errorDiv = document.createElement('div');
        errorDiv.className = 'error-message critical-section-error'; // Specific class
        errorDiv.textContent = `Critical Error collecting this section: ${data.SectionCollectionErrorMessage}`;
        contentDiv.appendChild(errorDiv); // Append error to content div
        // Optionally, stop rendering the content if a critical error occurred
        // container.appendChild(sectionDiv);
        // return;
    }

    // Display Specific Collection Errors (if exists)
    if (data.SpecificCollectionErrors && Object.keys(data.SpecificCollectionErrors).length > 0) {
        const errorContainer = document.createElement('div'); // Use a div instead of ul for better styling
        errorContainer.className = 'specific-errors-container';
        let errorHtml = '<h3>Collection Warnings/Errors:</h3>';
        errorHtml += '<ul>'; // Keep ul for list items
        for (const [key, value] of Object.entries(data.SpecificCollectionErrors)) {
            errorHtml += `<li class="specific-error-item"><strong>${escapeHtml(key)}:</strong> ${escapeHtml(value)}</li>`;
        }
        errorHtml += '</ul>';
        errorContainer.innerHTML = errorHtml;
        contentDiv.appendChild(errorContainer); // Append errors to content div
    }

    // Render the actual content using the specific function, placing it inside contentDiv
    try {
        renderContentFunc(contentDiv, data); // Pass contentDiv to render into
    } catch (renderError) {
         console.error(`Error rendering section "${title}":`, renderError);
         const errorDiv = document.createElement('div');
         errorDiv.className = 'error-message render-error';
         errorDiv.textContent = `Error displaying this section's content: ${renderError.message}`;
         // Append error below existing content if partial rendering occurred
         contentDiv.appendChild(errorDiv); // Append to content div
    }

    container.appendChild(sectionDiv); // Append the whole section structure
}

// --- Helper Functions (escapeHtml, safeGet, formatBytes, formatNullableDateTime) ---
function safeGet(obj, path, defaultValue = 'N/A') {
    // Improved safeGet: Handles null/undefined along the path
    const value = path.split('.').reduce((o, p) => (o && o[p] != null) ? o[p] : undefined, obj);
    const result = value !== undefined ? value : defaultValue;
    // Treat empty string as 'N/A' unless the default is also empty string
    return (result === '' && defaultValue !== '') ? defaultValue : result;
}

function escapeHtml(unsafe) {
    if (unsafe === null || typeof unsafe === 'undefined') return ''; // Handle null/undefined gracefully
    if (typeof unsafe !== 'string') unsafe = String(unsafe); // Convert non-strings

    return unsafe
         .replace(/&/g, "&amp;")
         .replace(/</g, "&lt;")
         .replace(/>/g, "&gt;")
         .replace(/"/g, "&quot;")
         .replace(/'/g, "&#039;");
}

function formatBytes(bytesInput) {
    const bytes = Number(bytesInput); // Ensure it's a number
    if (isNaN(bytes) || bytes < 0) return 'N/A'; // Handle invalid input
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    // Ensure precision makes sense, avoid unnecessary .00 for Bytes/KB
    const precision = i < 2 ? 0 : (bytes < 1 * k * k * k ? 2 : 1); // Show decimals for MB+, more for GB+
    return parseFloat((bytes / Math.pow(k, i)).toFixed(precision)) + ' ' + sizes[i];
}

function formatNullableDateTime(dateString, localeOptions = undefined) {
    if (!dateString) return 'N/A';
    try {
        const date = new Date(dateString);
        // Check if date is valid after parsing
        if (isNaN(date.getTime())) { return 'Invalid Date'; }
        return date.toLocaleString(undefined, localeOptions);
    } catch (e) {
        console.warn("Date formatting error for:", dateString, e);
        return 'Invalid Date';
    }
}

// Helper to format TimeSpan object (like OS Uptime)
function formatTimespan(timespanObject) {
    if (!timespanObject) return 'N/A';
    // Assuming timespanObject is like { Days: d, Hours: h, Minutes: m, Seconds: s, ... }
    // Check if Days property exists and is a number
    if (typeof timespanObject.Days !== 'number') {
        // Try parsing if it's a string like "d.hh:mm:ss"
        if (typeof timespanObject === 'string' && timespanObject.includes('.')) {
            const parts = timespanObject.split(/[.:]/); // Split by dot or colon
            if (parts.length >= 3) { // Need at least days, hours, minutes
                const d = parseInt(parts[0], 10) || 0;
                const h = parseInt(parts[1], 10) || 0;
                const m = parseInt(parts[2], 10) || 0;
                const s = parseInt(parts[3], 10) || 0;
                 return `${d}d ${h}h ${m}m ${s}s`;
            }
        }
        return 'Invalid TimeSpan'; // Could not parse
    }
    // Original object format
    return `${safeGet(timespanObject, 'Days', 0)}d ${safeGet(timespanObject, 'Hours', 0)}h ${safeGet(timespanObject, 'Minutes', 0)}m ${safeGet(timespanObject, 'Seconds', 0)}s`;
}

// Basic Table Creation Helper (Can be enhanced)
function createTable(headers, dataRows, tableClass = 'data-table', sortableColumns = []) {
    let tableHtml = `<table class="${escapeHtml(tableClass)}"><thead><tr>`;
    headers.forEach((h, index) => {
        const sortClass = sortableColumns.includes(index) ? ' sortable' : '';
        tableHtml += `<th class="${sortClass}" data-column-index="${index}">${escapeHtml(h)}</th>`;
    });
    tableHtml += '</tr></thead><tbody>';
    if (dataRows && dataRows.length > 0) {
        dataRows.forEach(row => {
            tableHtml += '<tr>';
            // Ensure row has same number of cells as headers
            for (let i = 0; i < headers.length; i++) {
                const cellData = (Array.isArray(row) && i < row.length) ? row[i] : 'N/A';
                // Add specific classes based on content if needed (e.g., for status)
                 let cellClass = '';
                 if (typeof cellData === 'string') {
                     if (cellData.toLowerCase() === 'fail' || cellData.toLowerCase().includes('error') || cellData.toLowerCase().includes('issue')) cellClass = 'status-fail';
                     else if (cellData.toLowerCase() === 'pass') cellClass = 'status-pass';
                     else if (cellData.toLowerCase() === 'warning' || cellData.toLowerCase().includes('suggestion')) cellClass = 'status-warning';
                 }
                 tableHtml += `<td class="${cellClass}">${escapeHtml(cellData)}</td>`; // Use escapeHtml on cell data
            }
            tableHtml += '</tr>';
        });
    } else {
        tableHtml += `<tr><td colspan="${headers.length}" class="no-data">No data available.</td></tr>`;
    }
    tableHtml += '</tbody></table>';
    return tableHtml;
}


// --- Specific Section Rendering Functions (Updated Implementations) ---

function renderSystemInfo(container, data) {
    let html = '<h3>Operating System</h3>';
    const os = data.OperatingSystem;
    if (os) {
        html += `<p><strong>Name:</strong> ${escapeHtml(safeGet(os, 'Name'))} (${escapeHtml(safeGet(os, 'Architecture'))})</p>
                 <p><strong>Version:</strong> ${escapeHtml(safeGet(os, 'Version'))} (Build: ${escapeHtml(safeGet(os, 'BuildNumber'))})</p>
                 <p><strong>Install Date:</strong> ${formatNullableDateTime(safeGet(os, 'InstallDate'))}</p>
                 <p><strong>Last Boot Time:</strong> ${formatNullableDateTime(safeGet(os, 'LastBootTime'))}</p>
                 <p><strong>System Uptime:</strong> ${formatTimespan(safeGet(os, 'Uptime', null))}</p>
                 <p><strong>System Drive:</strong> ${escapeHtml(safeGet(os, 'SystemDrive'))}</p>`;
    } else { html += '<p class="info-message"><i>Data unavailable.</i></p>'; }

    html += '<h3>Computer System</h3>';
    const cs = data.ComputerSystem;
    if (cs) {
        html += `<p><strong>Manufacturer:</strong> ${escapeHtml(safeGet(cs, 'Manufacturer'))}</p>
                 <p><strong>Model:</strong> ${escapeHtml(safeGet(cs, 'Model'))} (${escapeHtml(safeGet(cs, 'SystemType'))})</p>
                 <p><strong>Domain/Workgroup:</strong> ${escapeHtml(safeGet(cs, 'DomainOrWorkgroup'))} (PartOfDomain: ${safeGet(cs, 'PartOfDomain', 'N/A')})</p>
                 <p><strong>Executing User:</strong> ${escapeHtml(safeGet(cs, 'CurrentUser'))}</p>
                 <p><strong>Logged In User (WMI):</strong> ${escapeHtml(safeGet(cs, 'LoggedInUserWMI'))}</p>`;
    } else { html += '<p class="info-message"><i>Data unavailable.</i></p>'; }

     html += '<h3>Baseboard (Motherboard)</h3>';
     const bb = data.Baseboard;
     if(bb) {
          html += `<p><strong>Manufacturer:</strong> ${escapeHtml(safeGet(bb, 'Manufacturer'))}</p>
                   <p><strong>Product:</strong> ${escapeHtml(safeGet(bb, 'Product'))}</p>
                   <p><strong>Serial:</strong> ${escapeHtml(safeGet(bb, 'SerialNumber'))}</p>
                   <p><strong>Version:</strong> ${escapeHtml(safeGet(bb, 'Version'))}</p>`;
     } else { html += '<p class="info-message"><i>Data unavailable.</i></p>'; }

     html += '<h3>BIOS</h3>';
     const bios = data.BIOS;
     if(bios) {
         html += `<p><strong>Manufacturer:</strong> ${escapeHtml(safeGet(bios, 'Manufacturer'))}</p>
                  <p><strong>Version:</strong> ${escapeHtml(safeGet(bios, 'Version'))}</p>
                  <p><strong>Release Date:</strong> ${formatNullableDateTime(safeGet(bios, 'ReleaseDate'), { year: 'numeric', month: 'short', day: 'numeric' })}</p>
                  <p><strong>Serial:</strong> ${escapeHtml(safeGet(bios, 'SerialNumber'))}</p>`;
     } else { html += '<p class="info-message"><i>Data unavailable.</i></p>'; }

    html += '<h3>Time Zone</h3>';
    const tz = data.TimeZone;
    if(tz) {
         html += `<p><strong>Current Time Zone:</strong> ${escapeHtml(safeGet(tz, 'CurrentTimeZone'))}</p>
                  <p><strong>UTC Offset (Mins):</strong> ${escapeHtml(safeGet(tz, 'BiasMinutes'))}</p>`;
     } else { html += '<p class="info-message"><i>Data unavailable.</i></p>'; }

     html += '<h3>Power Plan</h3>';
     const pp = data.ActivePowerPlan;
     if(pp) {
          html += `<p><strong>Active Plan:</strong> ${escapeHtml(safeGet(pp, 'Name'))} (${escapeHtml(safeGet(pp, 'InstanceID'))})</p>`;
     } else { html += '<p class="info-message"><i>Data unavailable.</i></p>'; }

     html += `<p><strong>.NET Runtime:</strong> ${escapeHtml(safeGet(data, 'DotNetVersion'))}</p>`;

    container.innerHTML += html; // Append accumulated HTML
}


function renderHardwareInfo(container, data) {
    let html = '<h3>Processors</h3>';
    if (data.Processors && data.Processors.length > 0) {
        data.Processors.forEach(cpu => {
            html += `<div class="subsection">
                        <p><strong>${escapeHtml(safeGet(cpu, 'Name'))}</strong></p>
                        <ul>
                            <li>Socket: ${escapeHtml(safeGet(cpu, 'Socket'))}, Cores: ${escapeHtml(safeGet(cpu, 'Cores'))}, Logical Processors: ${escapeHtml(safeGet(cpu, 'LogicalProcessors'))}</li>
                            <li>Max Speed: ${escapeHtml(safeGet(cpu, 'MaxSpeedMHz'))} MHz, L2 Cache: ${escapeHtml(safeGet(cpu, 'L2Cache'))}, L3 Cache: ${escapeHtml(safeGet(cpu, 'L3Cache'))}</li>
                        </ul>
                    </div>`;
        });
    } else { html += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }

    html += '<h3>Memory (RAM)</h3>';
    const mem = data.Memory;
    if (mem) {
        html += `<div class="subsection">
                    <p><strong>Total Visible:</strong> ${escapeHtml(safeGet(mem, 'TotalVisible'))} (${formatBytes(safeGet(mem,'TotalVisibleMemoryKB',0) * 1024)})</p>
                    <p><strong>Available:</strong> ${escapeHtml(safeGet(mem, 'Available'))}</p>
                    <p><strong>Used:</strong> ${escapeHtml(safeGet(mem, 'Used'))} (${escapeHtml(safeGet(mem, 'PercentUsed', 0).toFixed(2))}%)</p>`;
        if (mem.Modules && mem.Modules.length > 0) {
            html += '<h4>Physical Modules:</h4><ul>';
            mem.Modules.forEach(mod => {
                html += `<li>[${escapeHtml(safeGet(mod, 'DeviceLocator'))}] ${escapeHtml(safeGet(mod, 'Capacity'))} @ ${escapeHtml(safeGet(mod, 'SpeedMHz'))}MHz (${escapeHtml(safeGet(mod, 'MemoryType'))} / ${escapeHtml(safeGet(mod, 'FormFactor'))}) - Mfg: ${escapeHtml(safeGet(mod, 'Manufacturer'))}, Part#: ${escapeHtml(safeGet(mod, 'PartNumber'))}</li>`;
            });
            html += '</ul>';
        } else { html += '<p class="info-message"><i>Physical Modules: Data unavailable or none found.</i></p>'; }
        html += `</div>`; // End subsection
    } else { html += '<p class="info-message"><i>Data unavailable.</i></p>'; }

     html += '<h3>Physical Disks</h3>';
     if(data.PhysicalDisks && data.PhysicalDisks.length > 0) {
         html += '<div class="subsection"><ul>';
         data.PhysicalDisks.forEach(disk => {
              const systemDisk = safeGet(disk, 'IsSystemDisk', false) ? ' <strong class="highlight">(System Disk)</strong>' : '';
              html += `<li><strong>Disk #${escapeHtml(safeGet(disk, 'Index'))}${systemDisk}: ${escapeHtml(safeGet(disk, 'Model'))}</strong>
                       <ul>
                           <li>Interface: ${escapeHtml(safeGet(disk, 'InterfaceType'))}, Size: ${escapeHtml(safeGet(disk, 'Size'))} (${formatBytes(safeGet(disk,'SizeBytes', 0))}), Partitions: ${escapeHtml(safeGet(disk, 'Partitions'))}, Serial: ${escapeHtml(safeGet(disk, 'SerialNumber'))}</li>
                           <li>Media: ${escapeHtml(safeGet(disk, 'MediaType'))}, Status: ${escapeHtml(safeGet(disk, 'Status'))}</li>
                           <li>SMART Status: ${renderSmartStatus(disk.SmartStatus)}</li>
                       </ul></li>`;
         });
         html += '</ul></div>';
     } else { html += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }

     html += '<h3>Logical Disks (Local Fixed)</h3>';
     if(data.LogicalDisks && data.LogicalDisks.length > 0) {
          // Add filtering options here maybe later
         html += '<div class="subsection"><ul>';
         data.LogicalDisks.forEach(ldisk => {
              html += `<li><strong>${escapeHtml(safeGet(ldisk, 'DeviceID'))} (${escapeHtml(safeGet(ldisk, 'VolumeName'))}) - ${escapeHtml(safeGet(ldisk, 'FileSystem'))}</strong>: Size ${escapeHtml(safeGet(ldisk, 'Size'))}, Free ${escapeHtml(safeGet(ldisk, 'FreeSpace'))} (${escapeHtml(safeGet(ldisk, 'PercentFree', 0).toFixed(1))}%)</li>`;
         });
         html += '</ul></div>';
     } else { html += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }

      html += '<h3>Volumes</h3>';
     if(data.Volumes && data.Volumes.length > 0) {
          html += '<div class="subsection"><ul>';
          data.Volumes.forEach(vol => {
               html += `<li><strong>${escapeHtml(safeGet(vol, 'DriveLetter', 'N/A'))} (${escapeHtml(safeGet(vol, 'Name', 'No Name'))}) - ${escapeHtml(safeGet(vol, 'FileSystem'))}</strong>: Capacity ${escapeHtml(safeGet(vol, 'Capacity'))}, Free ${escapeHtml(safeGet(vol, 'FreeSpace'))}</li>
                        <li>Device ID: ${escapeHtml(safeGet(vol, 'DeviceID'))}</li>
                        <li>BitLocker Status: ${escapeHtml(safeGet(vol, 'ProtectionStatus'))}</li>`;
          });
          html += '</ul></div>';
     } else { html += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }

      html += '<h3>Video Controllers (GPU)</h3>';
     if(data.Gpus && data.Gpus.length > 0) {
         html += '<div class="subsection"><ul>';
         data.Gpus.forEach(gpu => {
             html += `<li><strong>${escapeHtml(safeGet(gpu, 'Name'))}</strong> (Status: ${escapeHtml(safeGet(gpu, 'Status'))})
                      <ul>
                          <li>VRAM: ${escapeHtml(safeGet(gpu, 'Vram'))}, Processor: ${escapeHtml(safeGet(gpu, 'VideoProcessor'))}</li>
                          <li>Driver: ${escapeHtml(safeGet(gpu, 'DriverVersion'))} (${formatNullableDateTime(safeGet(gpu, 'DriverDate'), { year: 'numeric', month: 'short', day: 'numeric' })})</li>
                          <li>Resolution: ${escapeHtml(safeGet(gpu, 'CurrentResolution'))}</li>
                      </ul></li>`;
         });
         html += '</ul></div>';
     } else { html += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }

      html += '<h3>Monitors</h3>';
     if (data.Monitors && data.Monitors.length > 0) {
         html += '<div class="subsection"><ul>';
         data.Monitors.forEach(mon => {
             html += `<li><strong>${escapeHtml(safeGet(mon, 'Name'))}</strong> (ID: ${escapeHtml(safeGet(mon, 'DeviceID'))})
                     <ul><li>Mfg: ${escapeHtml(safeGet(mon, 'Manufacturer'))}, Reported Res: ${escapeHtml(safeGet(mon, 'ReportedResolution'))}, PPI: ${escapeHtml(safeGet(mon, 'PpiLogical'))}</li></ul></li>`;
         });
         html += '</ul></div>';
     } else { html += '<p class="info-message"><i>Data unavailable or none detected.</i></p>'; }

      html += '<h3>Audio Devices</h3>';
     if (data.AudioDevices && data.AudioDevices.length > 0) {
         html += '<div class="subsection"><ul>';
         data.AudioDevices.forEach(audio => {
             html += `<li><strong>${escapeHtml(safeGet(audio, 'Name'))}</strong> (Product: ${escapeHtml(safeGet(audio, 'ProductName'))}, Mfg: ${escapeHtml(safeGet(audio, 'Manufacturer'))}, Status: ${escapeHtml(safeGet(audio, 'Status'))})</li>`;
         });
         html += '</ul></div>';
     } else { html += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }

    container.innerHTML += html;
}

function renderSmartStatus(smartStatus) {
    if (!smartStatus) return '<span class="status-na">N/A</span>';
    let statusText = escapeHtml(safeGet(smartStatus, 'StatusText', 'Unknown'));
    let statusClass = 'status-unknown'; // Default class
    if (smartStatus.IsFailurePredicted) {
        statusText = `<strong>${statusText}</strong>`;
        statusClass = 'status-fail';
    } else if (smartStatus.StatusText === 'OK') {
         statusClass = 'status-pass';
    } else if (smartStatus.StatusText === 'Not Supported' || smartStatus.StatusText === 'Query Error') {
         statusClass = 'status-warning';
    }

    if (safeGet(smartStatus, 'ReasonCode', '0') !== '0') statusText += ` (Reason: ${escapeHtml(safeGet(smartStatus, 'ReasonCode'))})`;
    if (smartStatus.StatusText !== 'OK' && safeGet(smartStatus, 'BasicStatusFromDiskDrive') !== 'N/A') {
        statusText += ` (Basic HW Status: ${escapeHtml(safeGet(smartStatus, 'BasicStatusFromDiskDrive'))})`;
    }
    if (smartStatus.Error) statusText += ` <span class="error-inline">[Error: ${escapeHtml(smartStatus.Error)}]</span>`;
    return `<span class="${statusClass}">${statusText}</span>`; // Wrap in span with class
}


// --- TODO: Implement Filtering/Sorting/Pagination ---
// Example Placeholder for Filtering UI
function addFilteringControls(container, sectionId) {
     if (sectionId === 'software-configuration') { // Example: Target installed apps
         const filterDiv = document.createElement('div');
         filterDiv.className = 'filter-controls';
         filterDiv.innerHTML = `
             <label for="app-publisher-filter">Filter by Publisher:</label>
             <input type="text" id="app-publisher-filter" placeholder="Enter publisher name...">
             <button id="app-filter-button">Filter</button>
             <button id="app-reset-button">Reset</button>
         `;
         // Add event listeners for the button/input in the main script part
         container.prepend(filterDiv); // Add controls at the top of the section
     }
     // Add other filters similarly
}

// --- Update renderSoftwareInfo (Example with Placeholder for Filtering) ---
function renderSoftwareInfo(container, data) {
     // Add filtering controls (example for apps)
     // addFilteringControls(container, 'software-configuration');

     let html = '<h3>Installed Applications</h3>';
     if (data.InstalledApplications && data.InstalledApplications.length > 0) {
        html += `<p>Count: ${data.InstalledApplications.length}</p>`;
        const headers = ['Name', 'Version', 'Publisher', 'Install Date'];
        const rows = data.InstalledApplications.slice(0, 50).map(app => [ // Limit rows for performance initially
             safeGet(app, 'Name'),
             safeGet(app, 'Version'),
             safeGet(app, 'Publisher'),
             formatNullableDateTime(safeGet(app, 'InstallDate'), { year: 'numeric', month: 'short', day: 'numeric' })
        ]);
        // Mark Name and Publisher as sortable (index 0 and 2)
        html += createTable(headers, rows, 'installed-apps-table', [0, 2]);
        if (data.InstalledApplications.length > 50) html += '<p><i>(Showing first 50 applications - pagination needed for full view)</i></p>';
        // Add Pagination controls placeholder here
         html += '<div class="pagination-controls" data-section="apps"><button disabled>Previous</button> <span>Page 1</span> <button>Next</button></div>';

     } else { html += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }

     html += '<h3>Windows Updates (Hotfixes)</h3>';
     if (data.WindowsUpdates && data.WindowsUpdates.length > 0) {
         html += '<div class="subsection"><ul>';
         data.WindowsUpdates.forEach(upd => {
             html += `<li><strong>${escapeHtml(safeGet(upd, 'HotFixID'))}</strong> (${escapeHtml(safeGet(upd, 'Description'))}) - Installed: ${formatNullableDateTime(safeGet(upd, 'InstalledOn'))}</li>`;
         });
         html += '</ul></div>';
     } else { html += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }

     html += '<h3>Relevant Services</h3>';
     if (data.RelevantServices && data.RelevantServices.length > 0) {
         const headers = ['Display Name', 'Name', 'State', 'Start Mode', 'Path'];
         const rows = data.RelevantServices.map(svc => [
              safeGet(svc, 'DisplayName'),
              safeGet(svc, 'Name'),
              safeGet(svc, 'State'),
              safeGet(svc, 'StartMode'),
              safeGet(svc, 'PathName')
         ]);
          // Mark Display Name and State as sortable (index 0 and 2)
         html += createTable(headers, rows, 'services-table', [0, 2]);
     } else { html += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }

     html += '<h3>Startup Programs</h3>';
     if (data.StartupPrograms && data.StartupPrograms.length > 0) {
         html += '<div class="subsection"><ul>';
         data.StartupPrograms.forEach(prog => {
             html += `<li>[${escapeHtml(safeGet(prog, 'Location'))}] <strong>${escapeHtml(safeGet(prog, 'Name'))}</strong> = ${escapeHtml(safeGet(prog, 'Command'))}</li>`;
         });
         html += '</ul></div>';
     } else { html += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }

     // Environment Variables (simplified display)
     html += '<h3>Environment Variables</h3>';
     if (data.SystemEnvironmentVariables) {
         html += '<h4>System:</h4><ul class="env-vars">';
         Object.entries(data.SystemEnvironmentVariables).slice(0, 20).forEach(([key, value]) => html += `<li><strong>${escapeHtml(key)}</strong>=${escapeHtml(value)}</li>`);
         if (Object.keys(data.SystemEnvironmentVariables).length > 20) html += '<li>... more</li>';
         html += '</ul>';
     }
     if (data.UserEnvironmentVariables) {
         html += '<h4>User:</h4><ul class="env-vars">';
         Object.entries(data.UserEnvironmentVariables).slice(0, 20).forEach(([key, value]) => html += `<li><strong>${escapeHtml(key)}</strong>=${escapeHtml(value)}</li>`);
          if (Object.keys(data.UserEnvironmentVariables).length > 20) html += '<li>... more</li>';
         html += '</ul>';
     }


    container.innerHTML += html;
    // Attach event listeners for sorting/pagination AFTER table is in DOM
    setupTableInteractivity(container);
}

function setupTableInteractivity(container) {
    // --- Basic Sorting Example ---
    container.querySelectorAll('th.sortable').forEach(header => {
        header.addEventListener('click', () => {
            const table = header.closest('table');
            const columnIndex = parseInt(header.dataset.columnIndex, 10);
            const tbody = table.querySelector('tbody');
            const rows = Array.from(tbody.querySelectorAll('tr'));
            const isAscending = header.classList.contains('sort-asc'); // Check current sort direction

            // Sort rows based on text content of the cell in the clicked column
            rows.sort((a, b) => {
                const cellA = a.cells[columnIndex]?.textContent.trim().toLowerCase() || '';
                const cellB = b.cells[columnIndex]?.textContent.trim().toLowerCase() || '';
                // Basic string comparison
                return cellA.localeCompare(cellB) * (isAscending ? -1 : 1); // Toggle direction
            });

            // Clear existing sort classes
            table.querySelectorAll('th.sortable').forEach(th => th.classList.remove('sort-asc', 'sort-desc'));
            // Add new sort class
            header.classList.toggle('sort-asc', !isAscending);
            header.classList.toggle('sort-desc', isAscending);

            // Re-append sorted rows
            rows.forEach(row => tbody.appendChild(row));
        });
    });

    // --- Basic Pagination Example (Placeholder) ---
    // Need to store full data separately and re-render table based on page
    container.querySelectorAll('.pagination-controls button').forEach(button => {
         button.addEventListener('click', () => {
             // Logic to change page and re-render the relevant table would go here
             console.log("Pagination button clicked - implementation needed.");
             alert("Pagination not fully implemented in this example.");
         });
     });
}


function renderSecurityInfo(container, data) {
     let html = `<h3>Overview</h3>
                 <div class="subsection">
                     <p><strong>Running as Admin:</strong> ${escapeHtml(safeGet(data, 'IsAdmin'))}</p>
                     <p><strong>UAC Status:</strong> <span class="status-${safeGet(data, 'UacStatus','N/A').toLowerCase()}">${escapeHtml(safeGet(data, 'UacStatus'))}</span></p>
                     <p><strong>Antivirus:</strong> ${escapeHtml(safeGet(data, 'Antivirus.Name'))} (<span class="status-${safeGet(data, 'Antivirus.State','N/A').toLowerCase().includes('enabled')?'pass':'fail'}">${escapeHtml(safeGet(data, 'Antivirus.State'))}</span>)</p>
                     <p><strong>Firewall:</strong> ${escapeHtml(safeGet(data, 'Firewall.Name'))} (<span class="status-${safeGet(data, 'Firewall.State','N/A').toLowerCase().includes('enabled')?'pass':'fail'}">${escapeHtml(safeGet(data, 'Firewall.State'))}</span>)</p>
                     <p><strong>Secure Boot Enabled:</strong> <span class="status-${safeGet(data, 'IsSecureBootEnabled', 'unknown')}">${escapeHtml(safeGet(data, 'IsSecureBootEnabled', 'Unknown/Error'))}</span></p>
                     <p><strong>BIOS Mode (Inferred):</strong> ${escapeHtml(safeGet(data, 'BiosMode', 'Unknown/Error'))}</p>
                 </div>`;

      html += '<h3>TPM (Trusted Platform Module)</h3>';
     if (data.Tpm) {
         const tpm = data.Tpm;
         const tpmReady = safeGet(tpm,'IsPresent',false) && safeGet(tpm,'IsEnabled',false) && safeGet(tpm,'IsActivated',false);
         html += `<div class="subsection">
                     <p><strong>Present:</strong> <span class="status-${safeGet(tpm,'IsPresent',false)}">${escapeHtml(safeGet(tpm, 'IsPresent', 'Unknown'))}</span></p>`;
         if(safeGet(tpm, 'IsPresent', false)) {
             html += `<p><strong>Enabled:</strong> <span class="status-${safeGet(tpm,'IsEnabled',false)}">${escapeHtml(safeGet(tpm, 'IsEnabled', 'Unknown'))}</span></p>
                      <p><strong>Activated:</strong> <span class="status-${safeGet(tpm,'IsActivated',false)}">${escapeHtml(safeGet(tpm, 'IsActivated', 'Unknown'))}</span></p>
                      <p><strong>Spec Version:</strong> ${escapeHtml(safeGet(tpm, 'SpecVersion'))}</p>
                      <p><strong>Manufacturer:</strong> ${escapeHtml(safeGet(tpm, 'ManufacturerIdTxt'))} (Version: ${escapeHtml(safeGet(tpm, 'ManufacturerVersion'))})</p>
                      <p><strong>Status Summary:</strong> <span class="status-${tpmReady ? 'pass' : 'warning'}">${escapeHtml(safeGet(tpm, 'Status'))}</span></p>`;
         }
         if(tpm.ErrorMessage) html += `<p class="error-inline">Error: ${escapeHtml(tpm.ErrorMessage)}</p>`;
         html += `</div>`;
     } else { html += '<p class="info-message"><i>Data unavailable.</i></p>'; }


     html += '<h3>Local Users</h3>';
     if(data.LocalUsers && data.LocalUsers.length > 0) {
         html += '<div class="subsection"><ul>';
         data.LocalUsers.forEach(user => {
             const status = safeGet(user, 'IsDisabled', false) ? 'Disabled' : 'Enabled';
             html += `<li><strong>${escapeHtml(safeGet(user, 'Name'))}</strong> (Status: ${status}, PwdReq: ${escapeHtml(safeGet(user, 'PasswordRequired'))}) - SID: ${escapeHtml(safeGet(user, 'SID'))}</li>`;
         });
         html += '</ul></div>';
     } else { html += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }

      html += '<h3>Local Groups</h3>';
     if(data.LocalGroups && data.LocalGroups.length > 0) {
         html += '<div class="subsection"><ul>';
         data.LocalGroups.slice(0,15).forEach(grp => { // Limit groups shown
             html += `<li><strong>${escapeHtml(safeGet(grp, 'Name'))}</strong> - ${escapeHtml(safeGet(grp, 'Description'))}</li>`;
         });
          if(data.LocalGroups.length > 15) html += '<li>... more</li>';
         html += '</ul></div>';
     } else { html += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }

     html += '<h3>Network Shares</h3>';
      if(data.NetworkShares && data.NetworkShares.length > 0) {
          html += '<div class="subsection"><ul>';
          data.NetworkShares.forEach(share => {
              html += `<li><strong>${escapeHtml(safeGet(share, 'Name'))}</strong> -> ${escapeHtml(safeGet(share, 'Path'))} (${escapeHtml(safeGet(share, 'Description'))})</li>`;
          });
          html += '</ul></div>';
      } else { html += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }


    container.innerHTML += html;
}

function renderPerformanceInfo(container, data) {
     let html = `<h3>Counters (Sampled)</h3>
                <div class="subsection">
                     <p><strong>CPU Usage:</strong> ${escapeHtml(safeGet(data, 'OverallCpuUsagePercent'))} %</p>
                     <p><strong>Available Memory:</strong> ${escapeHtml(safeGet(data, 'AvailableMemoryMB'))} MB</p>
                     <p><strong>Disk Queue Length:</strong> ${escapeHtml(safeGet(data, 'TotalDiskQueueLength'))}</p>
                </div>`;

      html += '<h3>Top Processes by Memory (Working Set)</h3>';
      if(data.TopMemoryProcesses && data.TopMemoryProcesses.length > 0) {
          const headers = ['PID', 'Name', 'Memory', 'Status', 'Error'];
          const rows = data.TopMemoryProcesses.map(p => [
               safeGet(p, 'Pid'), safeGet(p, 'Name'), safeGet(p, 'MemoryUsage'), safeGet(p, 'Status'), safeGet(p, 'Error', '')
          ]);
          html += createTable(headers, rows, 'processes-table');
      } else { html += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }

      html += '<h3>Top Processes by CPU Time (Snapshot)</h3>';
       if(data.TopCpuProcesses && data.TopCpuProcesses.length > 0) {
          const headers = ['PID', 'Name', 'Memory', 'Status', 'Error'];
           const rows = data.TopCpuProcesses.map(p => [
               safeGet(p, 'Pid'), safeGet(p, 'Name'), safeGet(p, 'MemoryUsage'), safeGet(p, 'Status'), safeGet(p, 'Error', '')
          ]);
          html += createTable(headers, rows, 'processes-table');
      } else { html += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }


    container.innerHTML += html;
}

function renderNetworkInfo(container, data) {
     let html = '<h3>Network Adapters</h3>';
     // Add Filter Control Example
     html += `<div class="filter-controls">
                 <label for="nic-status-filter">Filter by Status:</label>
                 <select id="nic-status-filter">
                     <option value="">All</option>
                     <option value="Up">Up</option>
                     <option value="Down">Down</option>
                     <option value="Testing">Testing</option>
                     <option value="Unknown">Unknown</option>
                     <option value="Dormant">Dormant</option>
                     <option value="NotPresent">NotPresent</option>
                     <option value="LowerLayerDown">LowerLayerDown</option>
                 </select>
              </div>`;

    html += '<div id="nic-list">'; // Container for the filterable list
    if (data.Adapters && data.Adapters.length > 0) {
        html += data.Adapters.map(nic => renderNic(nic)).join(''); // Use map and join
    } else { html += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }
    html += '</div>'; // Close nic-list container


    const renderListenersTable = (title, listeners) => {
        let tableHtml = `<h3>Active ${title} Listeners</h3>`;
        if (listeners && listeners.length > 0) {
            const headers = ['Local Address:Port', 'PID', 'Process Name'];
             const rows = listeners.map(l => [
                 `${safeGet(l, 'LocalAddress')}:${safeGet(l, 'LocalPort')}`,
                 safeGet(l, 'OwningPid', '-'),
                 safeGet(l, 'OwningProcessName', 'N/A') + (safeGet(l,'Error','') ? ` (${safeGet(l,'Error')})` : '')
             ]);
             tableHtml += createTable(headers, rows, 'listeners-table');
        } else { tableHtml += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }
        return tableHtml;
    };
    html += renderListenersTable('TCP', data.ActiveTcpListeners);
    html += renderListenersTable('UDP', data.ActiveUdpListeners);

     html += '<h3>Active TCP Connections</h3>';
    if (data.ActiveTcpConnections && data.ActiveTcpConnections.length > 0) {
        const headers = ['Local Addr:Port', 'Remote Addr:Port', 'State', 'PID', 'Process Name'];
         const rows = data.ActiveTcpConnections.map(c => [
              `${safeGet(c, 'LocalAddress')}:${safeGet(c, 'LocalPort')}`,
              `${safeGet(c, 'RemoteAddress')}:${safeGet(c, 'RemotePort')}`,
              safeGet(c, 'State'),
              safeGet(c, 'OwningPid', '-'),
              safeGet(c, 'OwningProcessName', 'N/A') + (safeGet(c,'Error','') ? ` (${safeGet(c,'Error')})` : '')
         ]);
         html += createTable(headers, rows, 'connections-table');
    } else { html += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }


    html += '<h3>Connectivity Tests</h3>';
    const tests = data.ConnectivityTests;
    if(tests) {
        html += '<div class="subsection"><ul>';
        html += renderPingHtml(tests.GatewayPing, 'Default Gateway');
        if (tests.DnsPings) tests.DnsPings.forEach(p => html += renderPingHtml(p));
        html += renderDnsResolutionHtml(tests.DnsResolution);
        html += '</ul>';

        if (tests.TracerouteResults) {
            html += `<h4>Traceroute to ${escapeHtml(safeGet(tests, 'TracerouteTarget'))}</h4>`;
            const headers = ['Hop', 'Time (ms)', 'Address', 'Status'];
             const rows = tests.TracerouteResults.map(h => [
                 safeGet(h, 'Hop'), safeGet(h, 'RoundtripTimeMs', '*'), safeGet(h, 'Address', '*'), safeGet(h, 'Status') + (safeGet(h,'Error','') ? ` (${safeGet(h,'Error')})` : '')
             ]);
             html += createTable(headers, rows, 'traceroute-table');
        }
         html += '</div>'; // End subsection
    } else { html += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }


    container.innerHTML += html;

    // Add event listener for the filter AFTER elements are in the DOM
    const nicFilter = container.querySelector('#nic-status-filter');
    if (nicFilter) {
         nicFilter.addEventListener('change', (e) => {
             const selectedStatus = e.target.value;
             const nicListContainer = container.querySelector('#nic-list');
             if (nicListContainer && data.Adapters) {
                 const filteredAdapters = selectedStatus
                     ? data.Adapters.filter(nic => safeGet(nic, 'Status') === selectedStatus)
                     : data.Adapters; // Show all if empty value selected

                 if (filteredAdapters.length > 0) {
                    nicListContainer.innerHTML = filteredAdapters.map(nic => renderNic(nic)).join('');
                 } else {
                    nicListContainer.innerHTML = '<p class="info-message"><i>No network adapters match the selected status.</i></p>';
                 }
             }
         });
     }
}

// Helper to render a single NIC (used by filter)
function renderNic(nic) {
    return `<div class="subsection nic-details" data-status="${escapeHtml(safeGet(nic,'Status'))}">
                <h4>${escapeHtml(safeGet(nic,'Name'))} (${escapeHtml(safeGet(nic, 'Description'))})</h4>
                <ul>
                    <li>Status: <strong class="status-${safeGet(nic,'Status','unknown').toLowerCase()}">${escapeHtml(safeGet(nic, 'Status'))}</strong>, Type: ${escapeHtml(safeGet(nic, 'Type'))}, Speed: ${escapeHtml(safeGet(nic, 'SpeedMbps'))} Mbps</li>
                    <li>MAC: ${escapeHtml(safeGet(nic, 'MacAddress'))}, Index: ${escapeHtml(safeGet(nic, 'InterfaceIndex'))}</li>
                    <li>IPs: ${escapeHtml(safeGet(nic, 'IpAddresses', []).join(', '))}</li>
                    <li>Gateways: ${escapeHtml(safeGet(nic, 'Gateways', []).join(', '))}</li>
                    <li>DNS Servers: ${escapeHtml(safeGet(nic, 'DnsServers', []).join(', '))}</li>
                    <li>DNS Suffix: ${escapeHtml(safeGet(nic, 'DnsSuffix'))}</li>
                    <li>WINS Servers: ${escapeHtml(safeGet(nic, 'WinsServers', []).join(', '))}</li>
                    <li>DHCP Enabled: ${escapeHtml(safeGet(nic, 'DhcpEnabled'))} (Lease Obtained: ${formatNullableDateTime(safeGet(nic, 'DhcpLeaseObtained'))}, Expires: ${formatNullableDateTime(safeGet(nic, 'DhcpLeaseExpires'))})</li>
                     <li>Driver Date: ${formatNullableDateTime(safeGet(nic, 'DriverDate'), { year: 'numeric', month: 'short', day: 'numeric' })}</li>
                </ul>
            </div>`;
}

// Helper to render Ping result as HTML list item
function renderPingHtml(pingResult, defaultName) {
    if (!pingResult) return `<li>Ping ${defaultName || 'Unknown Target'}: <span class="status-na">N/A</span></li>`;
    const target = escapeHtml(safeGet(pingResult, 'Target', defaultName || 'Unknown Target'));
    const resolvedIP = safeGet(pingResult, 'ResolvedIpAddress', null);
    const displayTarget = resolvedIP ? `${target} [${resolvedIP}]` : target;
    let statusClass = 'status-unknown';
    let statusText = escapeHtml(safeGet(pingResult, 'Status', 'N/A'));
    if (statusText === 'Success') statusClass = 'status-pass';
    else if (statusText.toLowerCase().includes('error') || statusText === 'TimedOut' || statusText === 'DestinationHostUnreachable' || statusText === 'HardwareError') statusClass = 'status-fail';
    else if (statusText === 'TtlExpired') statusClass = 'status-info'; // TTL Expired is normal for traceroute but maybe warning for direct ping

    let pingHtml = `<li>Ping <strong>${displayTarget}</strong>: Status <span class="${statusClass}">${statusText}</span>`;
    if (statusText === 'Success') pingHtml += ` (${escapeHtml(safeGet(pingResult, 'RoundtripTimeMs'))}ms)`;
    if (pingResult.Error) pingHtml += ` <span class="error-inline">[Error: ${escapeHtml(pingResult.Error)}]</span>`;
    pingHtml += '</li>';
    return pingHtml;
};
// Helper to render DNS result as HTML list item
function renderDnsResolutionHtml(dnsResult) {
     if (!dnsResult) return `<li>DNS Resolution Test: <span class="status-na">N/A</span></li>`;
     const target = escapeHtml(safeGet(dnsResult, 'Hostname', 'Default Host'));
     let statusClass = safeGet(dnsResult, 'Success', false) ? 'status-pass' : 'status-fail';
     let statusText = safeGet(dnsResult, 'Success', false) ? 'Success' : 'Fail';

     let dnsHtml = `<li>DNS Resolution Test (<strong>${target}</strong>): Status <span class="${statusClass}">${statusText}</span>`;
     if (safeGet(dnsResult, 'Success', false)) {
         dnsHtml += ` (${escapeHtml(safeGet(dnsResult, 'ResolutionTimeMs'))}ms)`;
         dnsHtml += ` -> IPs: ${escapeHtml(safeGet(dnsResult, 'ResolvedIpAddresses', []).join(', '))}`;
     }
     if (dnsResult.Error) dnsHtml += ` <span class="error-inline">[Error: ${escapeHtml(dnsResult.Error)}]</span>`;
     dnsHtml += '</li>';
     return dnsHtml;
}


function renderEventLogInfo(container, data) {
    const renderLog = (title, entries) => {
        let logHtml = `<h3>${title} Log (Recent Errors/Warnings)</h3>`;
        if (entries && entries.length > 0) {
             // Handle collector messages like Access Denied
             if (entries.length === 1 && !safeGet(entries[0], 'Source', null)) {
                 logHtml += `<p class="info-message"><i>${escapeHtml(safeGet(entries[0], 'Message'))}</i></p>`;
             } else {
                 const actualEntries = entries.filter(e => safeGet(e, 'Source', null)); // Filter out collector messages
                 if(actualEntries.length > 0) {
                     logHtml += '<ul class="event-log-list">';
                     actualEntries.slice(0, 20).forEach(entry => { // Limit entries shown
                        let msg = escapeHtml(safeGet(entry, 'Message')).replace(/\r?\n/g, ' '); // Replace newlines
                        if (msg.length > 150) msg = msg.substring(0, 147) + '...'; // Truncate
                        const entryTypeClass = `event-${safeGet(entry, 'EntryType', 'unknown').toLowerCase()}`;
                        logHtml += `<li class="${entryTypeClass}">
                                        <span class="event-time">${formatNullableDateTime(safeGet(entry, 'TimeGenerated'))}:</span>
                                        <span class="event-type">[${escapeHtml(safeGet(entry, 'EntryType'))}]</span>
                                        <span class="event-source">${escapeHtml(safeGet(entry, 'Source'))}</span>
                                        <span class="event-id">(ID: ${escapeHtml(safeGet(entry, 'InstanceId'))})</span>
                                        <span class="event-message">- ${msg}</span>
                                    </li>`;
                     });
                     if (actualEntries.length > 20) logHtml += '<li>... (more entries exist)</li>';
                     logHtml += '</ul>';
                 } else if (entries.length > 0 && !actualEntries.length) {
                     // Only collector messages were present
                      logHtml += '<p class="info-message"><i>Could not retrieve events: ';
                      logHtml += entries.map(e => escapeHtml(safeGet(e, 'Message'))).join('; ');
                      logHtml += '</i></p>';
                 } else {
                     // No entries at all (shouldn't happen if collector ran unless log empty)
                      logHtml += '<p class="info-message"><i>No recent Error/Warning entries found.</i></p>';
                 }
             }
        } else { logHtml += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }
        return logHtml;
    };

    container.innerHTML += renderLog('System', data.SystemLogEntries);
    container.innerHTML += renderLog('Application', data.ApplicationLogEntries);
}

function renderAnalysisSummary(container, data) {
     container.classList.add('analysis-section'); // Add class for specific styling
     let html = '';
     if (!data) {
         html += '<p class="info-message"><i>Analysis was not performed or data is unavailable.</i></p>';
         container.innerHTML += html;
         return;
     }

      // Display analysis errors first
     if (data.SectionCollectionErrorMessage) {
         html += `<div class="error-message critical-section-error">Analysis Engine Error: ${escapeHtml(data.SectionCollectionErrorMessage)}</div>`;
     }

     // Windows 11 Readiness
      if (data.Windows11Readiness && data.Windows11Readiness.Checks && data.Windows11Readiness.Checks.length > 0) {
         html += '<h3>Windows 11 Readiness Check</h3>';
         const overall = safeGet(data.Windows11Readiness, 'OverallResult', null);
         let overallStatus = 'INCOMPLETE/ERROR';
         if (overall === true) overallStatus = 'PASS';
         if (overall === false) overallStatus = 'FAIL';
         html += `<p><strong>Overall Status: <span class="status-${overallStatus.toLowerCase()}">${overallStatus}</span></strong></p>`;
         const headers = ['Component', 'Requirement', 'Status', 'Details'];
         const rows = data.Windows11Readiness.Checks.map(c => [
             safeGet(c,'ComponentChecked'), safeGet(c,'Requirement'), safeGet(c,'Status'), safeGet(c,'Details')
         ]);
         html += createTable(headers, rows, 'readiness-table');
      }


    // Issues, Suggestions, Info
    const renderList = (title, items, listClass) => {
        if (items && items.length > 0) {
            let listHtml = `<h3 class="${listClass}">${title}</h3><ul class="${listClass}-list">`;
            items.forEach(item => { listHtml += `<li>${escapeHtml(item)}</li>`; });
            listHtml += `</ul>`;
            return listHtml;
        }
        return '';
    };

    html += renderList('Potential Issues Found', data.PotentialIssues, 'issues');
    html += renderList('Suggestions', data.Suggestions, 'suggestions');
    html += renderList('Informational Notes', data.Info, 'info');

    if (!data.PotentialIssues?.length && !data.Suggestions?.length && !data.Info?.length && !data.SectionCollectionErrorMessage && !data.Windows11Readiness?.Checks?.length) {
         html += `<p class="info-message">No specific issues, suggestions, or notes generated by the analysis.</p>`;
    }

    // Optionally display thresholds used
     if (data.Configuration && data.Configuration.AnalysisThresholds) {
          html += '<h3>Analysis Thresholds Used</h3><ul class="thresholds-list">';
          const thresholds = data.Configuration.AnalysisThresholds;
          // Add key thresholds here
          html += `<li>Memory High/Elevated %: ${escapeHtml(thresholds.HighMemoryUsagePercent)}/${escapeHtml(thresholds.ElevatedMemoryUsagePercent)}</li>`;
          html += `<li>Disk Critical/Low Free %: ${escapeHtml(thresholds.CriticalDiskSpacePercent)}/${escapeHtml(thresholds.LowDiskSpacePercent)}</li>`;
           html += `<li>Driver Age Warning (Years): ${escapeHtml(thresholds.DriverAgeWarningYears)}</li>`;
           html += `<li>Ping Latency Warning (ms): ${escapeHtml(thresholds.MaxPingLatencyWarningMs)}</li>`;
          html += '</ul>';
     }


     container.innerHTML += html;
}