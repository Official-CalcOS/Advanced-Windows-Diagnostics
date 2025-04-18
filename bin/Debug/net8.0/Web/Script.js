// In Web/Script.js

// --- Helper Functions ---
function safeGet(obj, path, defaultValue = 'N/A') {
    // Safely access nested properties of an object
    const value = path.split('.').reduce((o, p) => (o && o[p] != null) ? o[p] : undefined, obj);
    // Ensure defaultValue is returned if value is undefined OR null, unless defaultValue itself is null
    const result = (value === undefined || (value === null && defaultValue !== null)) ? defaultValue : value;
    // Handle empty string vs defaultValue specifically ONLY if defaultValue is not empty string
    return (result === '' && defaultValue !== '') ? defaultValue : result;
}

function escapeHtml(unsafe) {
    // Basic HTML escaping
    if (unsafe === null || typeof unsafe === 'undefined') return '';
    if (typeof unsafe !== 'string') unsafe = String(unsafe); // Ensure it's a string
    return unsafe
         .replace(/&/g, "&amp;")
         .replace(/</g, "&lt;")
         .replace(/>/g, "&gt;")
         .replace(/"/g, "&quot;")
         .replace(/'/g, "&#039;");
}

function formatBytes(bytesInput) {
    // Format bytes into KB, MB, GB etc.
    const bytes = Number(bytesInput);
    if (isNaN(bytes) || bytes < 0 || bytesInput === null || typeof bytesInput === 'undefined') return 'N/A'; // Added undefined check
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'];
    // if (bytes <= 0) return '0 B'; // Already handled by bytes === 0 check
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    if (i >= sizes.length) return 'Very Large'; // Handle extremely large values
    // Adjust precision based on size
    const precision = i < 2 ? 0 : (bytes < 1 * k * k * k ? 2 : 1); // GB threshold
    const formattedValue = parseFloat((bytes / Math.pow(k, i)).toFixed(precision));
    const finalSize = sizes[i] || 'B'; // Fallback to 'B'
    // Final check for NaN just in case
    return isNaN(formattedValue) ? 'N/A' : formattedValue + ' ' + finalSize;
}

function formatNullableDateTime(dateString, localeOptions = undefined) {
    // Format ISO-like date strings, handling nulls and potential WMI formats
    if (!dateString) return 'N/A';
    try {
        let processedDateString = dateString;
        // Attempt to handle WMI CIM DateTime format (yyyymmddhhmmss.mmmmmms(+-)zzz) if no standard separators are present
        if (typeof dateString === 'string' && dateString.length >= 14 && !dateString.includes('-') && !dateString.includes(':') && !dateString.includes('T')) {
             // Basic parsing - might need refinement for timezone offsets if present
             const year = dateString.substring(0, 4);
             const month = dateString.substring(4, 6);
             const day = dateString.substring(6, 8);
             const hour = dateString.substring(8, 10);
             const minute = dateString.substring(10, 12);
             const second = dateString.substring(12, 14);
             // Check if parsed parts are numeric before constructing ISO string
             if (!isNaN(parseInt(year)) && !isNaN(parseInt(month)) && !isNaN(parseInt(day)) &&
                 !isNaN(parseInt(hour)) && !isNaN(parseInt(minute)) && !isNaN(parseInt(second))) {
                 // Construct as UTC for consistency if no offset is handled, assumes WMI time is local if no offset given
                 // More robust parsing might involve checking for '+' or '-' offset indicator
                 processedDateString = `${year}-${month}-${day}T${hour}:${minute}:${second}`; // Assume local if no Z or offset
             }
        }
        const date = new Date(processedDateString);
        // Check if the date is valid after parsing
        if (isNaN(date.getTime())) {
            console.warn("Could not parse date string:", dateString);
            return 'Invalid/NA';
        }
        // Use default locale formatting options if none provided
        const defaultOptions = { year: 'numeric', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' };
        return date.toLocaleString(undefined, localeOptions || defaultOptions);
    } catch (e) {
        console.warn("Date formatting error for:", dateString, e);
        return 'Invalid/NA'; // Return specific error indicator
    }
}

function formatTimespan(timespanInput) {
    // Format TimeSpan objects or strings (like from C# TimeSpan.ToString())
    if (!timespanInput) return 'N/A';
    let days = 0, hours = 0, minutes = 0, seconds = 0;

    // Handle C# TimeSpan object structure if passed directly via JSON
    if (typeof timespanInput === 'object' && typeof safeGet(timespanInput, 'TotalSeconds', null) === 'number') {
        let totalSeconds = safeGet(timespanInput, 'TotalSeconds', 0);
        if (totalSeconds < 0) return 'Invalid TimeSpan'; // Negative duration is invalid
        days = Math.floor(totalSeconds / (24 * 3600));
        totalSeconds %= (24 * 3600);
        hours = Math.floor(totalSeconds / 3600);
        totalSeconds %= 3600;
        minutes = Math.floor(totalSeconds / 60);
        seconds = Math.floor(totalSeconds % 60);
    }
    // Handle string format like "d.hh:mm:ss" or "hh:mm:ss"
    else if (typeof timespanInput === 'string') {
        const mainParts = timespanInput.split('.'); // Split days from time
        let dayPart = '0';
        let timePart = '';

        // Refined parsing for various formats (d.hh:mm:ss.fff, hh:mm:ss.fff, d.hh:mm:ss, hh:mm:ss)
        if (timespanInput.includes('.')) { // Might have days or just fractional seconds
            if (mainParts.length >= 2 && mainParts[1].includes(':')) { // d.hh:mm:ss... format
                 dayPart = mainParts[0];
                 timePart = mainParts[1];
            } else if (mainParts[0].includes(':') && mainParts.length <= 2) { // hh:mm:ss.fff format
                 dayPart = '0';
                 timePart = mainParts[0]; // Time part might include fractional seconds
            } else if (!mainParts[0].includes(':') && mainParts.length === 2) { // Could be d.fff, invalid
                 console.warn("Ambiguous TimeSpan string format:", timespanInput);
                 return `Invalid Uptime (${timespanInput})`;
            } else { // Assume mainParts[0] is the time if it contains ':'
                 dayPart = '0';
                 timePart = mainParts[0];
            }
        } else if (timespanInput.includes(':')) { // No '.', must be hh:mm:ss format
             dayPart = '0';
             timePart = timespanInput;
        } else { // No '.' or ':', likely just seconds or invalid
             const totalSecs = parseInt(timespanInput, 10);
             if (!isNaN(totalSecs) && totalSecs >= 0) {
                  // Calculate d/h/m/s from total seconds
                  days = Math.floor(totalSecs / (24 * 3600));
                  let remainder = totalSecs % (24 * 3600);
                  hours = Math.floor(remainder / 3600);
                  remainder %= 3600;
                  minutes = Math.floor(remainder / 60);
                  seconds = Math.floor(remainder % 60);
                  // Skip further timePart parsing
                  timePart = ''; // Clear timePart as we've handled it
             } else {
                 console.warn("Could not parse TimeSpan string format:", timespanInput);
                 return `Invalid Uptime (${timespanInput})`;
             }
        }

        days = parseInt(dayPart, 10) || 0; // Parse days (will be 0 if dayPart was '0')

        if (timePart) { // Only parse timePart if it wasn't handled by total seconds case
            const timeParts = timePart.split(':'); // Split hh:mm:ss
            if (timeParts.length >= 3) {
                 hours = parseInt(timeParts[0], 10) || 0;
                 minutes = parseInt(timeParts[1], 10) || 0;
                 // Handle seconds potentially having fractional parts
                 seconds = parseInt(timeParts[2].split('.')[0], 10) || 0; // Take integer part of seconds
            } else {
                 console.warn("Could not parse time part of TimeSpan string:", timePart);
                 return `Invalid Uptime (${timespanInput})`;
            }
        }
         // Basic validation of parsed values
         if (hours >= 24 || minutes >= 60 || seconds >= 60 || days < 0 || hours < 0 || minutes < 0 || seconds < 0) {
              console.warn("Parsed TimeSpan resulted in invalid values:", { days, hours, minutes, seconds }, "Original:", timespanInput);
              return `Invalid Uptime Data (${timespanInput})`;
         }
    }
     else {
         console.warn("Could not parse TimeSpan format:", timespanInput);
         return 'Invalid TimeSpan';
     }

    // Return formatted string
    return `${days}d ${hours}h ${minutes}m ${seconds}s`;
}

function createTable(headers, dataRows, tableClass = 'data-table', sortableColumns = []) {
    // Creates an HTML table string from headers and data rows
    let tableHtml = `<div class="table-container"><table class="${escapeHtml(tableClass)}"><thead><tr>`;
    // Create table headers, adding sortable class if applicable
    headers.forEach((h, index) => {
        const sortClass = sortableColumns.includes(index) ? ' sortable' : '';
        tableHtml += `<th class="${sortClass}" data-column-index="${index}">${escapeHtml(h)}</th>`;
    });
    tableHtml += '</tr></thead><tbody>';
    // Create table rows
    if (dataRows && dataRows.length > 0) {
        dataRows.forEach(row => {
            tableHtml += '<tr>';
            for (let i = 0; i < headers.length; i++) {
                // Handle potential missing cells in a row
                const cellData = (Array.isArray(row) && i < row.length) ? row[i] : 'N/A';
                // Escape the raw data before checking its type or content
                const escapedCellData = escapeHtml(cellData);
                let cellClass = '';

                // Apply basic status classes based on escaped cell content
                if (typeof escapedCellData === 'string') {
                    const lowerCellData = escapedCellData.toLowerCase();
                    // Define keywords for status checking
                    const failKeywords = ['fail', 'error', 'issue', 'failure predicted', 'disabled', 'stopped', 'access denied', 'unreachable', 'critical', 'action required'];
                    const passKeywords = ['pass', 'ok', 'success', 'enabled', 'running', 'protection on'];
                    const warnKeywords = ['warning', 'suggestion', 'elevated', 'unknown', 'query error', 'snoozed', 'not up-to-date', 'requires admin', 'lookup failed', 'investigate'];
                    const infoKeywords = ['info', 'not supported', 'manual', 'n/a', 'protection off']; // Protection Off is informational

                    // Check keywords
                    if (failKeywords.some(kw => lowerCellData.includes(kw))) cellClass = 'status-fail';
                    else if (passKeywords.some(kw => lowerCellData.includes(kw))) cellClass = 'status-pass';
                    else if (warnKeywords.some(kw => lowerCellData.includes(kw))) cellClass = 'status-warning';
                    else if (infoKeywords.some(kw => lowerCellData.includes(kw))) cellClass = 'status-info';
                }
                // Add the cell with its content and class
                tableHtml += `<td class="${cellClass}">${escapedCellData}</td>`;
            }
            tableHtml += '</tr>';
        });
    } else {
        // Display message if no data rows
        tableHtml += `<tr><td colspan="${headers.length}" class="no-data">No data available.</td></tr>`;
    }
    tableHtml += '</tbody></table></div>';
    return tableHtml;
}

// --- Event Listeners Setup ---
document.addEventListener('DOMContentLoaded', () => {
    console.log("DOM fully loaded and parsed");
    const fileInput = document.getElementById('reportFile');
    const loadingStatus = document.getElementById('loading-status');
    const reportSectionsContainer = document.getElementById('report-sections');
    const reportMetadataContent = document.getElementById('report-metadata-content'); // Use correct ID
    const searchBox = document.getElementById('searchBox');
    const clearSearchBtn = document.getElementById('clearSearchBtn');
    const searchCountDisplay = document.getElementById('search-match-count'); // Get search count span
    let highlightTags = []; // Store highlighted elements for easy removal

    // Check if essential elements exist
    if (!fileInput) console.error("File input element 'reportFile' not found.");
    if (!loadingStatus) console.error("Loading status element 'loading-status' not found.");
    if (!reportSectionsContainer) console.error("Report sections container 'report-sections' not found.");
    if (!reportMetadataContent) console.error("Report metadata content container 'report-metadata-content' not found.");
    if (!searchBox) console.error("Search box element 'searchBox' not found.");
    if (!clearSearchBtn) console.error("Clear search button 'clearSearchBtn' not found.");
    if (!searchCountDisplay) console.error("Search count display element 'search-match-count' not found.");


    if (fileInput) {
        fileInput.addEventListener('change', handleFileSelect);
    }

    // --- Event Delegation for Collapsible Headers ---
    // Attach one listener to the body to handle clicks on any collapsible toggle
    document.body.addEventListener('click', function(event) {
        // Use closest() to find the toggle element, even if the click was on a child (like the span inside)
        const toggle = event.target.closest('.collapsible-toggle');
        if (toggle) {
            console.log("Toggle clicked:", toggle.textContent.trim());
            toggleCollapsible(toggle); // Pass the H2 element itself
        }
    });


    // --- Search Setup ---
     if (searchBox) {
         // Use 'input' event for real-time filtering as user types
         searchBox.addEventListener('input', handleSearch);
     }
     if (clearSearchBtn) {
         clearSearchBtn.addEventListener('click', () => {
             if (searchBox) searchBox.value = ''; // Clear input
             handleSearch(); // Trigger search logic to remove highlights/filters
         });
     }

    // --- Search Implementation ---
    function handleSearch() {
        const searchTerm = searchBox ? searchBox.value.trim().toLowerCase() : '';
        // console.log("Searching for:", searchTerm);
        removeHighlights(); // Clear previous highlights

        // If search term is empty, ensure all 'no-match' classes are removed and exit
        if (!searchTerm) {
            document.querySelectorAll('.no-match').forEach(el => el.classList.remove('no-match'));
            if (searchCountDisplay) searchCountDisplay.textContent = ''; // Clear count
            return;
        }

        let foundMatchOverall = false;
        // Search within metadata and main report sections
        document.querySelectorAll('#report-metadata-content, #report-sections').forEach(container => {
            // Target specific elements likely to contain relevant text
            // Include the section title span itself
            const elementsToSearch = container.querySelectorAll('p, li, td, th, h3, h4, .specific-error-item, .section-title-text');
            elementsToSearch.forEach(element => {
                // Avoid searching within script/style tags or already highlighted spans
                if (element.closest('script, style, .highlight-search')) return;

                const text = element.textContent.toLowerCase();
                if (text.includes(searchTerm)) {
                    highlightText(element, searchTerm); // Highlight matches within the element
                    foundMatchOverall = true;

                    // Ensure the section containing the match is visible and expanded
                    let sectionDiv = element.closest('.section');
                    if (sectionDiv) {
                         sectionDiv.classList.remove('no-match'); // Make section visible
                         const toggle = sectionDiv.querySelector('.collapsible-toggle');
                         if (toggle) {
                             expandSection(toggle); // Expand the section
                         }
                         // Remove no-match from intermediate parents if needed
                         let parent = element.parentElement;
                         while (parent && parent !== sectionDiv) {
                              parent.classList.remove('no-match');
                              parent = parent.parentElement;
                         }
                    }
                     element.classList.remove('no-match'); // Ensure element itself is not faded

                }
            });
        });
        // console.log("Search found match:", foundMatchOverall);

        // After highlighting, mark elements/sections that *don't* contain highlights as 'no-match'
        document.querySelectorAll('.section').forEach(section => {
            // Check if the section itself or any descendant has a highlight
            const hasHighlightInside = section.querySelector('.highlight-search');
            const titleSpan = section.querySelector('.section-title-text');
            const titleHasHighlight = titleSpan ? titleSpan.querySelector('.highlight-search') : false;

            if (!hasHighlightInside && !titleHasHighlight) {
                section.classList.add('no-match'); // Mark the whole section if no highlight found within title or content
            } else {
                section.classList.remove('no-match'); // Ensure sections with highlights are visible
            }
        });

        // Update search count display
        if (searchCountDisplay) {
            searchCountDisplay.textContent = `${highlightTags.length} match(es)`;
        }
    }


    function highlightText(element, searchTerm) {
        const lowerSearchTerm = searchTerm.toLowerCase();
        if (element.dataset.highlighted === 'true') return; // Avoid re-processing elements

        const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT, null, false);
        let node;
        const nodesToModify = []; // Collect nodes containing the term

        while (node = walker.nextNode()) {
            if (node.parentElement && (node.parentElement.tagName === 'SCRIPT' || node.parentElement.tagName === 'STYLE' || node.parentElement.classList.contains('highlight-search'))) {
                continue;
            }
            const lowerText = node.nodeValue.toLowerCase();
            if (lowerText.includes(lowerSearchTerm)) {
                nodesToModify.push({ node: node, term: searchTerm });
            }
        }

        // Process collected nodes
        nodesToModify.forEach(item => {
            const { node, term } = item;
            if (!node.parentNode || !document.body.contains(node)) return; // Check if node is still valid

            const text = node.nodeValue;
            const lowerText = text.toLowerCase();
            const lowerTerm = term.toLowerCase();
            let currentIndex = 0;
            const fragment = document.createDocumentFragment();

            while (currentIndex < text.length) {
                const termIndex = lowerText.indexOf(lowerTerm, currentIndex);
                if (termIndex === -1) {
                    fragment.appendChild(document.createTextNode(text.substring(currentIndex)));
                    break;
                }
                if (termIndex > currentIndex) {
                    fragment.appendChild(document.createTextNode(text.substring(currentIndex, termIndex)));
                }
                const matchText = text.substring(termIndex, termIndex + term.length);
                const span = document.createElement('span');
                span.className = 'highlight-search';
                span.textContent = matchText;
                fragment.appendChild(span);
                highlightTags.push(span); // Track the span
                currentIndex = termIndex + term.length;
            }

            try {
                node.parentNode.replaceChild(fragment, node);
            } catch (e) {
                console.warn("Error replacing node during highlight:", e, node);
            }
        });
        element.dataset.highlighted = 'true'; // Mark parent element to avoid re-walking its text nodes
    }


    function removeHighlights() {
        // Restore original text nodes by removing highlight spans
        // Iterate backwards because replacing nodes modifies the live collection/structure
        for (let i = highlightTags.length - 1; i >= 0; i--) {
            const span = highlightTags[i];
            if (span && span.parentNode) {
                const parent = span.parentNode;
                // Replace the span with its text content
                parent.replaceChild(document.createTextNode(span.textContent || ''), span);
                // Merge adjacent text nodes that might have been split during highlighting
                parent.normalize();
            }
        }
        highlightTags = []; // Clear the array of tracked spans
        // Remove .no-match class from all elements
        document.querySelectorAll('.no-match').forEach(el => el.classList.remove('no-match'));
        // Remove the marker attribute
        document.querySelectorAll('[data-highlighted="true"]').forEach(el => el.removeAttribute('data-highlighted'));
        // Clear search count display
         if (searchCountDisplay) searchCountDisplay.textContent = '';
        // console.log("Highlights removed.");
    }


// --- File Handling ---
function handleFileSelect(event) {
    console.log("handleFileSelect called");
    const file = event.target.files[0];
    const loadingStatus = document.getElementById('loading-status');
    const reportSectionsContainer = document.getElementById('report-sections');
    const reportMetadataContent = document.getElementById('report-metadata-content');

    // Clear previous report content and status
    if (reportSectionsContainer) reportSectionsContainer.innerHTML = '<p class="placeholder-text">Please select a JSON report file.</p>';
    if (reportMetadataContent) reportMetadataContent.innerHTML = '<p class="placeholder-text">Awaiting import...</p>'; // Clear metadata placeholder too
    else console.warn("Metadata content container ('report-metadata-content') not found during clear.");

    if (loadingStatus) {
        loadingStatus.textContent = '';
        loadingStatus.className = 'status-message'; // Reset classes
    } else { console.error("Loading status element 'loading-status' not found."); return; }

     // Reset search
     if (searchBox) searchBox.value = '';
     removeHighlights();

    if (!file) {
        loadingStatus.textContent = 'No file selected.';
        loadingStatus.classList.add('info');
        return;
    }

    // Basic file type validation
    if (!file.type.includes('json') && !file.name.toLowerCase().endsWith('.json')) {
         loadingStatus.textContent = 'Error: Please select a valid JSON file (.json).';
         loadingStatus.classList.add('error');
         if (reportSectionsContainer) reportSectionsContainer.innerHTML = '<p class="placeholder-text error">Please select a valid JSON report file.</p>';
         return;
     }

    // Indicate loading
    loadingStatus.textContent = `Loading ${file.name}...`;
    loadingStatus.classList.add('info');
    if (reportSectionsContainer) reportSectionsContainer.innerHTML = '<h2>Loading Report Data...</h2>';
    if (reportMetadataContent) reportMetadataContent.innerHTML = '<p>Loading...</p>'; // Update metadata placeholder

    const reader = new FileReader();

    reader.onload = (e) => {
        console.log("FileReader onload triggered");
        try {
            window.reportData = JSON.parse(e.target.result); // Store globally for potential re-use (e.g., filtering)
            // Basic validation of parsed data structure
            if (!window.reportData || typeof window.reportData !== 'object') {
                throw new Error("Invalid report structure: Root is not an object.");
            }
            // Check for a key section to guess if it's the right format
            if (safeGet(window.reportData, 'System', null) === null && safeGet(window.reportData, 'Hardware', null) === null) {
                console.warn("Report data might be missing expected sections (System, Hardware).");
                // throw new Error("Invalid report structure: Missing key sections (System/Hardware).");
            }

            console.log("Report data loaded and parsed successfully.");
            loadingStatus.textContent = `Successfully loaded ${file.name}.`;
            loadingStatus.classList.remove('info', 'error'); // Clear previous status classes
            loadingStatus.classList.add('success');

            // Display the parsed report data
            displayReport(window.reportData);

        } catch (error) {
            console.error('Error parsing JSON report:', error);
            window.reportData = null; // Clear invalid data
            loadingStatus.textContent = `Error parsing ${file.name}: ${error.message}. Ensure the file is valid JSON.`;
            loadingStatus.classList.remove('info', 'success');
            loadingStatus.classList.add('error');
            if (reportSectionsContainer) reportSectionsContainer.innerHTML = `<div class="error-message critical-section-error">Error parsing report: ${escapeHtml(error.message)}. Make sure the JSON file is valid.</div>`;
            if (reportMetadataContent) reportMetadataContent.innerHTML = '<p class="error-inline">Could not load metadata.</p>';
        }
    };

    reader.onerror = (e) => {
        // Handle file reading errors
        console.error('Error reading file:', e);
        window.reportData = null; // Clear data on read error
        loadingStatus.textContent = `Error reading file ${file.name}.`;
         loadingStatus.classList.remove('info', 'success');
        loadingStatus.classList.add('error');
        if (reportSectionsContainer) reportSectionsContainer.innerHTML = `<div class="error-message critical-section-error">Error reading the selected file.</div>`;
         if (reportMetadataContent) reportMetadataContent.innerHTML = '<p class="error-inline">Could not load metadata.</p>';
    };

    // Read the file content as text
    reader.readAsText(file);
}


// --- Report Display ---
function displayReport(report) {
    console.log("displayReport called");
    const sectionsContainer = document.getElementById('report-sections');
    const metadataContent = document.getElementById('report-metadata-content'); // Use correct ID
    const metadataSectionDiv = metadataContent ? metadataContent.closest('.section.collapsible') : null; // Find parent section

    if (!sectionsContainer || !metadataSectionDiv || !metadataContent) {
        console.error("Essential report display elements missing:", { sectionsContainer: !!sectionsContainer, metadataSectionDiv: !!metadataSectionDiv, metadataContent: !!metadataContent });
        if(sectionsContainer) sectionsContainer.innerHTML = "<p class='error-message'>Error: Could not initialize report display elements. Check console for details.</p>";
        return;
    }
     console.log("Report display elements found.");

    // Display Metadata
    try {
         // Populate metadata section
         metadataContent.innerHTML = `
              <p><strong>Generated (UTC):</strong> <span id="timestamp">${formatNullableDateTime(safeGet(report, 'ReportTimestamp', null))}</span></p>
              <p><strong>Ran as Admin:</strong> <span id="ran-as-admin" class="status-${safeGet(report, 'RanAsAdmin', false) ? 'pass' : 'warning'}">${safeGet(report, 'RanAsAdmin', 'N/A')}</span></p>
              <p><strong>Configuration Used:</strong> <span id="config-source">${safeGet(report, 'Configuration.AnalysisThresholds', null) ? 'Loaded File/Defaults' : 'Defaults/Not Included'}</span></p>
         `;
          // console.log("Metadata rendered.");
    } catch (e) {
         console.error("Error rendering metadata:", e);
         metadataContent.innerHTML = `<p class="error-inline">Error displaying metadata: ${e.message}</p>`;
    }

    // Clear previous sections (like loading message)
    sectionsContainer.innerHTML = '';

    // --- Render each section ---
    // console.log("Rendering sections...");
    try {
        // Define the order sections should appear in the report
        const sectionRenderOrder = [
            { title: "Analysis Summary", key: "Analysis", func: renderAnalysisSummary },
            { title: "System Stability", key: "Stability", func: renderStabilityInfo },
            { title: "System Information", key: "System", func: renderSystemInfo },
            { title: "Hardware Information", key: "Hardware", func: renderHardwareInfo },
            { title: "Performance Snapshot", key: "Performance", func: renderPerformanceInfo },
            { title: "Network Information", key: "Network", func: renderNetworkInfo },
            { title: "Security Information", key: "Security", func: renderSecurityInfo },
            { title: "Software & Configuration", key: "Software", func: renderSoftwareInfo },
            { title: "Recent Event Logs", key: "Events", func: renderEventLogInfo }
        ];

        // Render sections based on the defined order
        sectionRenderOrder.forEach(section => {
            renderSection(sectionsContainer, section.title, safeGet(report, section.key, null), section.func);
        });

        // console.log("Finished rendering sections.");

        // Ensure metadata content is expanded initially after rendering content
        const metadataToggle = metadataSectionDiv.querySelector('.collapsible-toggle');
        if (metadataContent && metadataToggle) {
            // console.log("Expanding metadata section.");
            expandSection(metadataToggle); // Expand metadata by default
        }
        // Collapse other sections by default after rendering
        sectionsContainer.querySelectorAll('.collapsible-toggle').forEach(toggle => {
            // console.log("Collapsing section:", toggle.textContent.trim());
            collapseSection(toggle); // Collapse all other sections
        });

    } catch (e) {
         console.error("Error during section rendering process:", e);
         sectionsContainer.innerHTML = `<p class='error-message'>A critical error occurred while rendering report sections: ${e.message}</p>`;
    }
}


// --- Collapsible Logic (with ARIA attributes) ---
function expandSection(headerElement) {
    if (!headerElement || headerElement.classList.contains('active')) return;
    headerElement.classList.add('active');
    headerElement.setAttribute('aria-expanded', 'true'); // Set ARIA expanded state
    const content = headerElement.nextElementSibling;
    if (content && content.classList.contains('collapsible-content')) {
        content.style.maxHeight = content.scrollHeight + "px";
        content.style.paddingTop = null; // Restore padding if needed
        content.style.paddingBottom = null;
        content.style.borderTopWidth = null; // Restore border if needed
        // console.log("Expanding section:", headerElement.textContent.trim(), "to height:", content.scrollHeight);
    } else {
        console.warn("Expand failed: Content element not found for toggle:", headerElement.textContent.trim());
    }
}

function collapseSection(headerElement) {
    if (!headerElement || !headerElement.classList.contains('active')) return;
    headerElement.classList.remove('active');
    headerElement.setAttribute('aria-expanded', 'false'); // Set ARIA expanded state
    const content = headerElement.nextElementSibling;
    if (content && content.classList.contains('collapsible-content')) {
        content.style.maxHeight = '0px'; // Collapse
        // console.log("Collapsing section:", headerElement.textContent.trim());
        // Removing padding immediately can look smoother than waiting for timeout
        // content.style.paddingTop = '0';
        // content.style.paddingBottom = '0';
        // content.style.borderTopWidth = '0';
    } else {
        console.warn("Collapse failed: Content element not found for toggle:", headerElement.textContent.trim());
    }
}

function toggleCollapsible(headerElement) {
    if (!headerElement) return;
    if (headerElement.classList.contains('active')) {
        collapseSection(headerElement);
    } else {
        expandSection(headerElement);
    }
}


// --- Generic Section Rendering (with ARIA attributes) ---
function renderSection(container, title, data, renderContentFunc) {
     // console.log(`Rendering section: ${title}`);
    const sectionDiv = document.createElement('div');
    const sectionIdBase = title.toLowerCase().replace(/[^a-z0-9]+/g, '-'); // Create a base safe ID
    const headerId = `${sectionIdBase}-heading`;
    const contentId = `${sectionIdBase}-content`;
    sectionDiv.className = 'section collapsible';
    sectionDiv.id = sectionIdBase; // ID for the whole section div

    // Create title header (H2) - acts as the toggle
    const titleElement = document.createElement('h2');
    titleElement.className = 'collapsible-toggle';
    titleElement.id = headerId; // ID for ARIA
    // Set ARIA attributes for accessibility
    titleElement.setAttribute('aria-expanded', 'false'); // Start collapsed (will be overridden for metadata)
    titleElement.setAttribute('aria-controls', contentId);
    // Use a span inside for easier text targeting if needed
    titleElement.innerHTML = `<span class="section-title-text">${escapeHtml(title)}</span>`;
    sectionDiv.appendChild(titleElement);

    // Create content container div
    const contentDiv = document.createElement('div');
    contentDiv.className = 'collapsible-content';
    contentDiv.id = contentId; // ID for ARIA
    contentDiv.setAttribute('role', 'region'); // ARIA role
    contentDiv.setAttribute('aria-labelledby', headerId); // Link content to header
    sectionDiv.appendChild(contentDiv);


    // Handle case where data for the section is missing entirely
    if (data === null || data === undefined) {
         contentDiv.innerHTML = `<p class="info-message"><i>Section data was not collected or is unavailable.</i></p>`;
         container.appendChild(sectionDiv);
         // collapseSection(titleElement); // Default collapse handled after all sections rendered
         return;
     }

    // Display critical section-level collection errors first
    const sectionError = safeGet(data, 'SectionCollectionErrorMessage', null);
    if (sectionError) {
        const errorDiv = document.createElement('div');
        errorDiv.className = 'error-message critical-section-error';
        errorDiv.textContent = `Critical Error collecting this section: ${escapeHtml(sectionError)}`;
        contentDiv.appendChild(errorDiv);
        // expandSection(titleElement); // Optionally expand on critical error
    }

    // Display Specific Collection Errors
    const specificErrors = safeGet(data, 'SpecificCollectionErrors', null);
    if (specificErrors && typeof specificErrors === 'object' && Object.keys(specificErrors).length > 0) {
        const errorContainer = document.createElement('div');
        errorContainer.className = 'specific-errors-container';
        let errorHtml = '<h3>Collection Warnings/Errors:</h3><ul>';
        for (const [key, value] of Object.entries(specificErrors)) {
            errorHtml += `<li class="specific-error-item"><strong>${escapeHtml(key)}:</strong> ${escapeHtml(value)}</li>`; // Use value directly
        }
        errorHtml += '</ul>';
        errorContainer.innerHTML = errorHtml;
        contentDiv.appendChild(errorContainer);
        // expandSection(titleElement); // Optionally expand on specific errors
    }

    // Render the actual content using the provided function
    try {
        renderContentFunc(contentDiv, data);
         // console.log(`Successfully rendered content for: ${title}`);
    } catch (renderError) {
         console.error(`Error rendering section "${title}":`, renderError);
         const errorDiv = document.createElement('div');
         errorDiv.className = 'error-message render-error';
         errorDiv.textContent = `Error displaying this section's content: ${escapeHtml(renderError.message)}`;
         contentDiv.appendChild(errorDiv);
         expandSection(titleElement); // Expand section if rendering failed
    }

    container.appendChild(sectionDiv);
}

// --- Specific Section Rendering Functions ---

// --- UPDATED: renderSystemInfo (Aligns with refined DataModels.cs) ---
function renderSystemInfo(container, data) {
    if (!data) { container.innerHTML += '<p class="info-message"><i>System Info data unavailable.</i></p>'; return; }
    let html = '';

    html += '<h3>Operating System</h3>';
    const os = safeGet(data, 'OperatingSystem', null);
    if (os) {
        html += `<div class="subsection">
                    <p><strong>Name:</strong> ${escapeHtml(safeGet(os, 'Name'))} (${escapeHtml(safeGet(os, 'Architecture'))})</p>
                    <p><strong>Version:</strong> ${escapeHtml(safeGet(os, 'Version'))} (Build: ${escapeHtml(safeGet(os, 'BuildNumber'))})</p>
                    <p><strong>Install Date:</strong> ${formatNullableDateTime(safeGet(os, 'InstallDate'))}</p>
                    <p><strong>Last Boot Time:</strong> ${formatNullableDateTime(safeGet(os, 'LastBootTime'))}</p>
                    <p><strong>System Uptime:</strong> ${formatTimespan(safeGet(os, 'Uptime', null))}</p>
                    <p><strong>System Drive:</strong> ${escapeHtml(safeGet(os, 'SystemDrive'))}</p>
                </div>`;
    } else { html += '<p class="info-message"><i>Operating System data unavailable.</i></p>'; }

    html += '<h3>Computer System</h3>';
    const cs = safeGet(data, 'ComputerSystem', null);
    if (cs) {
        html += `<div class="subsection">
                    <p><strong>Manufacturer:</strong> ${escapeHtml(safeGet(cs, 'Manufacturer'))}</p>
                    <p><strong>Model:</strong> ${escapeHtml(safeGet(cs, 'Model'))} (${escapeHtml(safeGet(cs, 'SystemType'))})</p>
                    <p><strong>Domain/Workgroup:</strong> ${escapeHtml(safeGet(cs, 'DomainOrWorkgroup'))} (PartOfDomain: ${safeGet(cs, 'PartOfDomain', 'N/A')})</p>
                    <p><strong>Executing User:</strong> ${escapeHtml(safeGet(cs, 'CurrentUser'))}</p>
                    <p><strong>Logged In User (WMI):</strong> ${escapeHtml(safeGet(cs, 'LoggedInUserWMI'))}</p>
                </div>`;
    } else { html += '<p class="info-message"><i>Computer System data unavailable.</i></p>'; }

    html += '<h3>Baseboard (Motherboard)</h3>';
    const bb = safeGet(data, 'Baseboard', null);
    if(bb) {
        html += `<div class="subsection">
                    <p><strong>Manufacturer:</strong> ${escapeHtml(safeGet(bb, 'Manufacturer'))}</p>
                    <p><strong>Product:</strong> ${escapeHtml(safeGet(bb, 'Product'))}</p>
                    <p><strong>Serial:</strong> ${escapeHtml(safeGet(bb, 'SerialNumber'))}</p>
                    <p><strong>Version:</strong> ${escapeHtml(safeGet(bb, 'Version'))}</p>
                </div>`;
    } else { html += '<p class="info-message"><i>Baseboard data unavailable.</i></p>'; }

    html += '<h3>BIOS</h3>';
    const bios = safeGet(data, 'BIOS', null);
    if(bios) {
        html += `<div class="subsection">
                    <p><strong>Manufacturer:</strong> ${escapeHtml(safeGet(bios, 'Manufacturer'))}</p>
                    <p><strong>Version:</strong> ${escapeHtml(safeGet(bios, 'Version'))}</p>
                    <p><strong>Release Date:</strong> ${formatNullableDateTime(safeGet(bios, 'ReleaseDate'), { year: 'numeric', month: 'short', day: 'numeric' })}</p>
                    <p><strong>Serial:</strong> ${escapeHtml(safeGet(bios, 'SerialNumber'))}</p>
                </div>`;
    } else { html += '<p class="info-message"><i>BIOS data unavailable.</i></p>'; }

    html += '<h3>Time Zone</h3>';
    const tz = safeGet(data, 'TimeZone', null);
    if(tz) {
        html += `<div class="subsection">
                    <p><strong>Current Time Zone:</strong> ${escapeHtml(safeGet(tz, 'CurrentTimeZone'))}</p>
                    <p><strong>Standard Name:</strong> ${escapeHtml(safeGet(tz, 'StandardName'))}</p>
                    <p><strong>Daylight Name:</strong> ${escapeHtml(safeGet(tz, 'DaylightName'))}</p>
                    <p><strong>UTC Offset (Mins):</strong> ${escapeHtml(safeGet(tz, 'BiasMinutes'))}</p>
                </div>`;
    } else { html += '<p class="info-message"><i>Time Zone data unavailable.</i></p>'; }

    html += '<h3>Power Plan</h3>';
    const pp = safeGet(data, 'ActivePowerPlan', null);
    if(pp) {
        html += `<div class="subsection"><p><strong>Active Plan:</strong> ${escapeHtml(safeGet(pp, 'Name'))} (${escapeHtml(safeGet(pp, 'InstanceID'))})</p></div>`;
    } else { html += '<p class="info-message"><i>Active Power Plan data unavailable.</i></p>'; }

    // *** UPDATED System Integrity Subsection ***
    html += '<h3>System Integrity WIP - this will return nothing for now</h3>';
    const si = safeGet(data, 'SystemIntegrity', null);
    if (si) {
        html += `<div class="subsection">`;
        const logParsingError = safeGet(si, 'LogParsingError', null);
        if (logParsingError) {
            // Display global parsing error prominently
            html += `<p class="error-inline"><strong>Log Parsing Error:</strong> ${escapeHtml(logParsingError)}</p>`;
        } else {
            // SFC Info
            html += `<p><strong>SFC Log Status:</strong> ${safeGet(si, 'SfcLogFound') ? 'Found' : 'Not Found/Inaccessible'}</p>`;
            if (safeGet(si, 'SfcLogFound') === true) {
                const sfcScanResult = escapeHtml(safeGet(si, 'SfcScanResult', 'Unknown'));
                const sfcCorruption = safeGet(si, 'SfcCorruptionFound', null);
                const sfcRepaired = safeGet(si, 'SfcRepairsSuccessful', null);
                const sfcScanTime = formatNullableDateTime(safeGet(si, 'LastSfcScanTime'));

                html += `<ul>`;
                html += `<li>Last Scan Time (UTC): ${sfcScanTime}</li>`;
                html += `<li>Scan Result: <span class="status-${sfcScanResult.toLowerCase().includes('no violation') ? 'pass' : (sfcScanResult.toLowerCase().includes('unrepairable') ? 'fail' : (sfcScanResult.toLowerCase().includes('repaired') ? 'pass' : 'info'))}">${sfcScanResult}</span></li>`;
                // Conditionally show Corruption/Repair status if result implies corruption was possible
                if (!sfcScanResult.toLowerCase().includes('no violation')) {
                    html += `<li>Corruption Found: ${sfcCorruption === null ? 'Unknown' : (sfcCorruption ? 'Yes' : 'No')}</li>`;
                    if (sfcCorruption === true) {
                        html += `<li>Repairs Successful: ${sfcRepaired === null ? 'Unknown' : (sfcRepaired ? 'Yes' : 'No')}</li>`;
                    }
                }
                html += `</ul>`;
            }

            // DISM Info
            html += `<p style="margin-top: 10px;"><strong>DISM Log Status:</strong> ${safeGet(si, 'DismLogFound') ? 'Found' : 'Not Found/Inaccessible'}</p>`;
            if (safeGet(si, 'DismLogFound') === true) {
                const dismResult = escapeHtml(safeGet(si, 'DismCheckHealthResult', 'Unknown'));
                const dismCorruption = safeGet(si, 'DismCorruptionDetected', null);
                const dismRepairable = safeGet(si, 'DismStoreRepairable', null);
                const dismCheckTime = formatNullableDateTime(safeGet(si, 'LastDismCheckTime'));

                html += `<ul>`;
                html += `<li>Last Check Time (UTC): ${dismCheckTime}</li>`;
                html += `<li>CheckHealth Result: <span class="status-${dismResult.toLowerCase().includes('no corruption') ? 'pass' : (dismResult.toLowerCase().includes('repairable') ? 'warning' : (dismResult.toLowerCase().includes('not repairable') ? 'fail' : 'info'))}">${dismResult}</span></li>`;
                // Conditionally show Corruption/Repairable status
                if (!dismResult.toLowerCase().includes('no corruption')) {
                    html += `<li>Corruption Detected: ${dismCorruption === null ? 'Unknown' : (dismCorruption ? 'Yes' : 'No')}</li>`;
                    if (dismCorruption === true) {
                        html += `<li>Store Repairable: ${dismRepairable === null ? 'Unknown' : (dismRepairable ? 'Yes' : 'No')}</li>`;
                    }
                }
                html += `</ul>`;
            }
        }
        html += `</div>`; // End subsection
    } else { html += '<p class="info-message"><i>System Integrity check data unavailable.</i></p>'; }
    // *** END UPDATED System Integrity Subsection ***

    html += '<h3>Pending Reboot Status</h3>';
    const rebootPending = safeGet(data, 'IsRebootPending', null);
    if (rebootPending !== null) {
        html += `<div class="subsection"><p><strong>Reboot Pending:</strong> <span class="status-${rebootPending ? 'warning' : 'pass'}">${rebootPending ? 'Yes' : 'No'}</span></p></div>`;
    } else { html += '<p class="info-message"><i>Pending reboot status could not be determined.</i></p>'; }

    html += `<p><strong>.NET Runtime (Executing):</strong> ${escapeHtml(safeGet(data, 'DotNetVersion'))}</p>`;

    container.innerHTML += html;
}

// --- UPDATED: renderHardwareInfo (Aligns with refined DataModels.cs) ---
function renderHardwareInfo(container, data) {
    if (!data) { container.innerHTML += '<p class="info-message"><i>Hardware Info data unavailable.</i></p>'; return; }
    let html = '';

    html += '<h3>Processors</h3>';
    const processors = safeGet(data, 'Processors', []);
    if (processors.length > 0) {
        processors.forEach(cpu => {
            const l2 = formatBytes(safeGet(cpu, 'L2CacheSizeKB', 0) * 1024); // Format L2 Cache from KB
            const l3 = formatBytes(safeGet(cpu, 'L3CacheSizeKB', 0) * 1024); // Format L3 Cache from KB
            html += `<div class="subsection">
                        <p><strong>${escapeHtml(safeGet(cpu, 'Name'))}</strong></p>
                        <ul>
                            <li>Socket: ${escapeHtml(safeGet(cpu, 'Socket'))}, Cores: ${escapeHtml(safeGet(cpu, 'Cores'))}, Logical Processors: ${escapeHtml(safeGet(cpu, 'LogicalProcessors'))}</li>
                            <li>Max Speed: ${escapeHtml(safeGet(cpu, 'MaxSpeedMHz'))} MHz, L2 Cache: ${l2}, L3 Cache: ${l3}</li>
                        </ul>
                    </div>`;
        });
    } else { html += '<p class="info-message"><i>Processor data unavailable or none found.</i></p>'; }

    html += '<h3>Memory (RAM)</h3>';
    const mem = safeGet(data, 'Memory', null);
    if (mem) {
        const totalKB = safeGet(mem, 'TotalVisibleMemoryKB', 0);
        const availableKB = safeGet(mem, 'AvailableMemoryKB', -1); // Use -1 to indicate not available vs 0
        const totalFormatted = formatBytes(totalKB * 1024);
        const availableFormatted = formatBytes(availableKB * 1024);
        let usedFormatted = 'N/A';
        if (totalKB > 0 && availableKB >= 0) {
             const usedKB = totalKB - availableKB;
             usedFormatted = formatBytes(usedKB * 1024);
        }
        const percentUsedFormatted = escapeHtml(safeGet(mem, 'PercentUsed', 0).toFixed(2));

        html += `<div class="subsection">
                    <p><strong>Total Visible:</strong> ${escapeHtml(totalFormatted)} (${escapeHtml(totalKB)} KB)</p>
                    <p><strong>Available:</strong> ${escapeHtml(availableFormatted)} ${availableKB >= 0 ? '(' + escapeHtml(availableKB) + ' KB)' : ''}</p>
                    <p><strong>Used:</strong> ${escapeHtml(usedFormatted)} (${percentUsedFormatted}%)</p>`;

        const modules = safeGet(mem, 'Modules', []);
        if (modules.length > 0) {
            html += '<h4>Physical Modules:</h4><ul>';
            modules.forEach(mod => {
                const capacityFormatted = formatBytes(safeGet(mod, 'CapacityBytes', 0));
                html += `<li>[${escapeHtml(safeGet(mod, 'DeviceLocator'))}] ${escapeHtml(capacityFormatted)} @ ${escapeHtml(safeGet(mod, 'SpeedMHz'))}MHz (${escapeHtml(safeGet(mod, 'MemoryType'))} / ${escapeHtml(safeGet(mod, 'FormFactor'))}) - Mfg: ${escapeHtml(safeGet(mod, 'Manufacturer'))}, Part#: ${escapeHtml(safeGet(mod, 'PartNumber'))}, Bank: ${escapeHtml(safeGet(mod, 'BankLabel'))}</li>`;
            });
            html += '</ul>';
        } else { html += '<p class="info-message"><i>Physical Modules: Data unavailable or none found.</i></p>'; }
        html += `</div>`;
    } else { html += '<p class="info-message"><i>Memory data unavailable.</i></p>'; }

     html += '<h3>Physical Disks</h3>';
     const physicalDisks = safeGet(data, 'PhysicalDisks', []);
     if(physicalDisks.length > 0) {
         html += '<div class="subsection"><ul>';
         physicalDisks.forEach(disk => {
              const systemDisk = safeGet(disk, 'IsSystemDisk', false) ? ' <strong class="highlight">(System Disk)</strong>' : '';
              const sizeFormatted = formatBytes(safeGet(disk, 'SizeBytes', 0)); // Format from Bytes
              html += `<li><strong>Disk #${escapeHtml(safeGet(disk, 'Index'))}${systemDisk}: ${escapeHtml(safeGet(disk, 'Model'))}</strong>
                       <ul>
                           <li>Interface: ${escapeHtml(safeGet(disk, 'InterfaceType'))}, Size: ${escapeHtml(sizeFormatted)}, Partitions: ${escapeHtml(safeGet(disk, 'Partitions'))}, Serial: ${escapeHtml(safeGet(disk, 'SerialNumber'))}</li>
                           <li>Media: ${escapeHtml(safeGet(disk, 'MediaType'))}, Status: ${escapeHtml(safeGet(disk, 'Status'))}</li>
                           <li>SMART Status: ${renderSmartStatus(safeGet(disk, 'SmartStatus', null))}</li>
                       </ul></li>`;
         });
         html += '</ul></div>';
     } else { html += '<p class="info-message"><i>Physical Disk data unavailable or none found.</i></p>'; }

     html += '<h3>Logical Disks (Local Fixed)</h3>';
     const logicalDisks = safeGet(data, 'LogicalDisks', []);
     if(logicalDisks.length > 0) {
         html += '<div class="subsection"><ul>';
         logicalDisks.forEach(ldisk => {
               const sizeFormatted = formatBytes(safeGet(ldisk,'SizeBytes', 0)); // Format from Bytes
               const freeFormatted = formatBytes(safeGet(ldisk,'FreeSpaceBytes', 0)); // Format from Bytes
               const percentFree = safeGet(ldisk, 'PercentFree', null);
              html += `<li><strong>${escapeHtml(safeGet(ldisk, 'DeviceID'))} (${escapeHtml(safeGet(ldisk, 'VolumeName'))}) - ${escapeHtml(safeGet(ldisk, 'FileSystem'))}</strong>: Size ${escapeHtml(sizeFormatted)}, Free ${escapeHtml(freeFormatted)} (${percentFree !== null ? escapeHtml(percentFree.toFixed(1)) + '%' : 'N/A'})</li>`;
         });
         html += '</ul></div>';
     } else { html += '<p class="info-message"><i>Logical Disk data unavailable or none found.</i></p>'; }

      html += '<h3>Volumes</h3>';
     const volumes = safeGet(data, 'Volumes', []);
     if(volumes.length > 0) {
          html += '<div class="subsection"><ul>';
          volumes.forEach(vol => {
               const capacityFormatted = formatBytes(safeGet(vol, 'CapacityBytes', 0)); // Format from Bytes
               const freeFormatted = formatBytes(safeGet(vol, 'FreeSpaceBytes', 0)); // Format from Bytes
               html += `<li><strong>${escapeHtml(safeGet(vol, 'DriveLetter', 'N/A'))} (${escapeHtml(safeGet(vol, 'Name', 'No Name'))}) - ${escapeHtml(safeGet(vol, 'FileSystem'))}</strong>: Capacity ${escapeHtml(capacityFormatted)}, Free ${escapeHtml(freeFormatted)}</li>
                        <li>Device ID: ${escapeHtml(safeGet(vol, 'DeviceID'))}</li>
                        <li>BitLocker Status: ${escapeHtml(safeGet(vol, 'ProtectionStatus'))}</li>`;
          });
          html += '</ul></div>';
     } else { html += '<p class="info-message"><i>Volume data unavailable or none found.</i></p>'; }

      html += '<h3>Video Controllers (GPU)</h3>';
     const gpus = safeGet(data, 'Gpus', []);
     if(gpus.length > 0) {
         html += '<div class="subsection"><ul>';
         gpus.forEach(gpu => {
             const vramFormatted = formatBytes(safeGet(gpu, 'AdapterRAMBytes', 0)); // Format VRAM from bytes
             const hRes = safeGet(gpu, 'CurrentHorizontalResolution', 0);
             const vRes = safeGet(gpu, 'CurrentVerticalResolution', 0);
             const refresh = safeGet(gpu, 'CurrentRefreshRate', 0);
             const currentResFormatted = (hRes > 0 && vRes > 0) ? `${hRes}x${vRes}${refresh > 0 ? ' @ ' + refresh + ' Hz' : ''}` : 'N/A'; // Format resolution

             html += `<li><strong>${escapeHtml(safeGet(gpu, 'Name'))}</strong> (Status: ${escapeHtml(safeGet(gpu, 'Status'))})
                      <ul>
                          <li>VRAM: ${escapeHtml(vramFormatted)}, Processor: ${escapeHtml(safeGet(gpu, 'VideoProcessor'))}</li>
                          <li>Driver: ${escapeHtml(safeGet(gpu, 'DriverVersion'))} (${formatNullableDateTime(safeGet(gpu, 'DriverDate'), { year: 'numeric', month: 'short', day: 'numeric' })})</li>
                          <li>Resolution: ${escapeHtml(currentResFormatted)}</li>
                           <li>WDDM Version: ${escapeHtml(safeGet(gpu, 'WddmVersion'))}</li>
                      </ul></li>`;
         });
         html += '</ul></div>';
     } else { html += '<p class="info-message"><i>GPU data unavailable or none found.</i></p>'; }

      html += '<h3>Monitors</h3>';
      const monitors = safeGet(data, 'Monitors', []);
     if (monitors.length > 0) {
         html += '<div class="subsection"><ul>';
         monitors.forEach(mon => {
              const screenW = safeGet(mon, 'ScreenWidth', 0);
              const screenH = safeGet(mon, 'ScreenHeight', 0);
              const reportedResFormatted = (screenW > 0 && screenH > 0) ? `${screenW}x${screenH}` : 'N/A';
              const ppiX = safeGet(mon, 'PixelsPerXLogicalInch', 0);
              const ppiY = safeGet(mon, 'PixelsPerYLogicalInch', 0);
              const ppiFormatted = (ppiX > 0 && ppiY > 0) ? `${ppiX}x${ppiY}` : 'N/A';
              const diagonal = safeGet(mon, 'DiagonalSizeInches', null);
              const diagonalFormatted = diagonal ? `${diagonal.toFixed(1)} inches` : 'N/A';

             html += `<li><strong>${escapeHtml(safeGet(mon, 'Name'))}</strong> (ID: ${escapeHtml(safeGet(mon, 'DeviceID'))})
                     <ul>
                        <li>Mfg: ${escapeHtml(safeGet(mon, 'Manufacturer'))}, Resolution: ${escapeHtml(reportedResFormatted)}, PPI: ${escapeHtml(ppiFormatted)}</li>
                        <li>Diagonal Size: ${escapeHtml(diagonalFormatted)}</li>
                    </ul></li>`;
         });
         html += '</ul></div>';
     } else { html += '<p class="info-message"><i>Monitor data unavailable or none detected.</i></p>'; }

      html += '<h3>Audio Devices</h3>';
      const audioDevs = safeGet(data, 'AudioDevices', []);
     if (audioDevs.length > 0) {
         html += '<div class="subsection"><ul>';
         audioDevs.forEach(audio => {
             html += `<li><strong>${escapeHtml(safeGet(audio, 'Name'))}</strong> (Product: ${escapeHtml(safeGet(audio, 'ProductName'))}, Mfg: ${escapeHtml(safeGet(audio, 'Manufacturer'))}, Status: ${escapeHtml(safeGet(audio, 'Status'))})</li>`;
         });
         html += '</ul></div>';
     } else { html += '<p class="info-message"><i>Audio Device data unavailable or none found.</i></p>'; }

    container.innerHTML += html;
}

// --- UPDATED: renderPerformanceInfo (Aligns with refined DataModels.cs) ---
function renderPerformanceInfo(container, data) {
    if (!data) { container.innerHTML += '<p class="info-message"><i>Performance Info data unavailable.</i></p>'; return; }
    let html = '';

    html += `<h3>Counters (Sampled)</h3>
             <div class="subsection">
                  <p><strong>CPU Usage:</strong> ${escapeHtml(safeGet(data, 'OverallCpuUsagePercent'))} %</p>
                  <p><strong>Available Memory:</strong> ${escapeHtml(safeGet(data, 'AvailableMemoryMB'))} MB</p>
                  <p><strong>Disk Queue Length:</strong> ${escapeHtml(safeGet(data, 'TotalDiskQueueLength'))}</p>
             </div>`;

     const renderProcTable = (title, procListKey) => {
         let procHtml = `<h3>Top Processes by ${title}</h3>`;
         const processes = safeGet(data, procListKey, []);
         if(processes.length > 0) {
             const isCpuTable = title.toLowerCase().includes('cpu');
             // Use WorkingSetBytes for memory table, TotalProcessorTimeMs for CPU
             const headers = isCpuTable
                 ? ['PID', 'Name', 'Total CPU Time (ms)', 'Status', 'Error']
                 : ['PID', 'Name', 'Memory Usage (Working Set)', 'Status', 'Error'];

             const rows = processes.map(p => {
                 let mainValue = isCpuTable
                     ? safeGet(p, 'TotalProcessorTimeMs', 'N/A')
                     : formatBytes(safeGet(p, 'WorkingSetBytes', 0)); // Format memory from raw bytes

                 return [
                     safeGet(p, 'Pid'),
                     safeGet(p, 'Name'),
                     mainValue, // Either CPU Time or Formatted Memory
                     safeGet(p, 'Status'),
                     safeGet(p, 'Error', '')
                 ];
             });
             procHtml += createTable(headers, rows, 'processes-table');
         } else {
             procHtml += '<p class="info-message"><i>Data unavailable or none found.</i></p>';
         }
         return procHtml;
     };
     html += renderProcTable('Memory (Working Set)', 'TopMemoryProcesses');
     html += renderProcTable('Total CPU Time', 'TopCpuProcesses');

   container.innerHTML += html;
}

// --- UPDATED: renderNic (Aligns with refined DataModels.cs) ---
function renderNic(nic) {
    if (!nic) return '';
    const status = safeGet(nic,'Status', 'Unknown').toLowerCase();
    const ipAddresses = safeGet(nic, 'IpAddresses', []);
    const gateways = safeGet(nic, 'Gateways', []);
    const dnsServers = safeGet(nic, 'DnsServers', []);
    const winsServers = safeGet(nic, 'WinsServers', []);

    // Format speed - assuming SpeedMbps is already Mbps
    const speedFormatted = safeGet(nic, 'SpeedMbps', -1) >= 0 ? `${safeGet(nic, 'SpeedMbps')} Mbps` : 'N/A';

    return `<div class="subsection nic-details" data-status="${escapeHtml(safeGet(nic,'Status'))}">
                <h4>${escapeHtml(safeGet(nic,'Name'))} (${escapeHtml(safeGet(nic, 'Description'))})</h4>
                <ul>
                    <li>Status: <strong class="status-${status}">${escapeHtml(safeGet(nic, 'Status'))}</strong>, Type: ${escapeHtml(safeGet(nic, 'Type'))}, Speed: ${escapeHtml(speedFormatted)}</li>
                    <li>MAC: ${escapeHtml(safeGet(nic, 'MacAddress'))}, Index: ${escapeHtml(safeGet(nic, 'InterfaceIndex'))}</li>
                    <li>IPs: ${ipAddresses.length > 0 ? escapeHtml(ipAddresses.join(', ')) : 'N/A'}</li>
                    <li>Gateways: ${gateways.length > 0 ? escapeHtml(gateways.join(', ')) : 'N/A'}</li>
                    <li>DNS Servers: ${dnsServers.length > 0 ? escapeHtml(dnsServers.join(', ')) : 'N/A'}</li>
                    <li>DNS Suffix: ${escapeHtml(safeGet(nic, 'DnsSuffix'))}</li>
                    <li>WINS Servers: ${winsServers.length > 0 ? escapeHtml(winsServers.join(', ')) : 'N/A'}</li>
                    <li>DHCP Enabled: ${escapeHtml(safeGet(nic, 'DhcpEnabled'))} (Lease Obtained: ${formatNullableDateTime(safeGet(nic, 'DhcpLeaseObtained'))}, Expires: ${formatNullableDateTime(safeGet(nic, 'DhcpLeaseExpires'))})</li>
                    <li>Driver Date: ${formatNullableDateTime(safeGet(nic, 'DriverDate'), { year: 'numeric', month: 'short', day: 'numeric' })}</li>
                     <li>WMI Service Name: ${escapeHtml(safeGet(nic, 'WmiServiceName'))}</li>
                </ul>
            </div>`;
}

// --- UPDATED: renderStabilityInfo (Aligns with refined DataModels.cs) ---
function renderStabilityInfo(container, data) {
    if (!data) { container.innerHTML += '<p class="info-message"><i>System Stability data unavailable.</i></p>'; return; }
    let html = '';
    html += '<h3>Recent Crash Dumps</h3>';
    const dumps = safeGet(data, 'RecentCrashDumps', []); // Default to empty array
    if (dumps.length > 0) {
        const headers = ['Filename', 'Timestamp', 'Size'];
        const rows = dumps.map(dump => [
            safeGet(dump, 'FileName'),
            formatNullableDateTime(safeGet(dump, 'Timestamp')),
            formatBytes(safeGet(dump, 'FileSizeBytes', 0)) // Format size from raw bytes
        ]);
        html += createTable(headers, rows, 'crash-dumps-table');
    } else {
        html += '<p class="info-message"><i>No recent crash dump files found in standard locations.</i></p>';
    }
    container.innerHTML += html;
}


// ==================================================================================
// Other functions (renderSmartStatus, renderSoftwareInfo, filterAppTable,
// renderSecurityInfo, renderPingHtml, renderDnsResolutionHtml, renderNetworkInfo,
// renderEventLogInfo, setupTableInteractivity, renderAnalysisSummary) should remain
// largely the same as the last provided version, as they don't directly depend
// on the specific properties changed in HardwareInfo/StabilityInfo.
// Make sure renderAnalysisSummary uses the refined data model properties if needed.
// ==================================================================================

// Paste the rest of the functions here... (renderSmartStatus, renderSoftwareInfo, filterAppTable, etc.)
// Ensure they use safeGet, escapeHtml, and formatters appropriately.

// (Keep renderSmartStatus as is)
function renderSmartStatus(smartStatus) {
    if (!smartStatus) return '<span class="status-na">N/A</span>';
    let statusText = escapeHtml(safeGet(smartStatus, 'StatusText', 'Unknown'));
    let statusClass = 'status-unknown';
    const failurePredicted = safeGet(smartStatus, 'IsFailurePredicted', false);

    if (failurePredicted) {
        statusText = `<strong>FAILURE PREDICTED</strong>`;
        statusClass = 'status-fail';
    } else if (safeGet(smartStatus, 'StatusText', '').toUpperCase() === 'OK') {
        statusClass = 'status-pass';
    } else if (safeGet(smartStatus, 'StatusText', '').includes('Error') || safeGet(smartStatus, 'StatusText', '').includes('Query Error')) {
        statusClass = 'status-warning';
    } else if (safeGet(smartStatus, 'StatusText', '').includes('Not Supported') || safeGet(smartStatus, 'StatusText', '').includes('Requires Admin')) {
        statusClass = 'status-info';
    }

    let details = [];
    const reasonCode = safeGet(smartStatus, 'ReasonCode', null);
    if (failurePredicted && reasonCode) details.push(`Reason: ${escapeHtml(reasonCode)}`);
    const basicHwStatus = safeGet(smartStatus, 'BasicStatusFromDiskDrive', 'N/A');
    if (statusClass !== 'status-pass' && basicHwStatus !== 'OK' && basicHwStatus !== 'N/A') {
        details.push(`Basic HW Status: ${escapeHtml(basicHwStatus)}`);
    }
    const errorMsg = safeGet(smartStatus, 'Error', null);
    if (errorMsg) details.push(`Error: ${escapeHtml(errorMsg)}`);

    let fullStatus = `<span class="${statusClass}">${statusText}</span>`;
    if (details.length > 0) {
        fullStatus += ` (${details.join(', ')})`;
    }
    return fullStatus;
}

// (Keep renderSoftwareInfo as is, it uses standard properties)
function renderSoftwareInfo(container, data) {
    if (!data) { container.innerHTML += '<p class="info-message"><i>Software Info data unavailable.</i></p>'; return; }
    let html = '';

    html += '<h3>Installed Applications</h3>';
    const apps = safeGet(data, 'InstalledApplications', []);
    if (apps.length > 0) {
        html += `<p>Count: ${apps.length}</p>`;
        html += `<div class="filter-controls">
                    <label for="app-filter">Filter Apps:</label>
                    <input type="text" id="app-filter" placeholder="Type to filter by Name or Publisher...">
                    <button id="app-reset-button">Reset</button>
                 </div>`;
        const headers = ['Name', 'Version', 'Publisher', 'Install Date'];
        const rows = apps.map(app => [
             safeGet(app, 'Name'),
             safeGet(app, 'Version'),
             safeGet(app, 'Publisher'),
             formatNullableDateTime(safeGet(app, 'InstallDate'), { year: 'numeric', month: 'short', day: 'numeric' })
        ]);
        html += createTable(headers, rows, 'installed-apps-table', [0, 2]);
    } else { html += '<p class="info-message"><i>Installed Application data unavailable or none found.</i></p>'; }

    html += '<h3>Windows Updates (Hotfixes)</h3>';
    const updates = safeGet(data, 'WindowsUpdates', []);
    if (updates.length > 0) {
        html += '<div class="subsection"><ul>';
        updates.sort((a, b) => {
             const dateA = safeGet(a, 'InstalledOn', null); const dateB = safeGet(b, 'InstalledOn', null);
             if (dateA && dateB) return new Date(dateB) - new Date(dateA);
             if (dateA) return -1; if (dateB) return 1; return 0;
         }).forEach(upd => {
            html += `<li><strong>${escapeHtml(safeGet(upd, 'HotFixID'))}</strong> (${escapeHtml(safeGet(upd, 'Description'))}) - Installed: ${formatNullableDateTime(safeGet(upd, 'InstalledOn'))}</li>`;
        });
        html += '</ul></div>';
    } else { html += '<p class="info-message"><i>Windows Update data unavailable or none found.</i></p>'; }

    html += '<h3>Relevant Services</h3>';
    const services = safeGet(data, 'RelevantServices', []);
    if (services.length > 0) {
        const headers = ['Display Name', 'Name', 'State', 'Start Mode', 'Path'];
        const rows = services.map(svc => [
             safeGet(svc, 'DisplayName'), safeGet(svc, 'Name'), safeGet(svc, 'State'), safeGet(svc, 'StartMode'), safeGet(svc, 'PathName')
        ]);
        html += createTable(headers, rows, 'services-table', [0, 2]);
    } else { html += '<p class="info-message"><i>Relevant Service data unavailable or none found.</i></p>'; }

    html += '<h3>Startup Programs</h3>';
    const startup = safeGet(data, 'StartupPrograms', []);
    if (startup.length > 0) {
        html += '<div class="subsection"><ul>';
        startup.forEach(prog => {
            html += `<li>[${escapeHtml(safeGet(prog, 'Location'))}] <strong>${escapeHtml(safeGet(prog, 'Name'))}</strong> = ${escapeHtml(safeGet(prog, 'Command'))}</li>`;
        });
        html += '</ul></div>';
    } else { html += '<p class="info-message"><i>Startup Program data unavailable or none found.</i></p>'; }

    html += '<h3>Environment Variables</h3>';
    const renderEnvVars = (title, varsKey) => {
        let envHtml = '';
        const envVars = safeGet(data, varsKey, null);
        if (envVars && typeof envVars === 'object' && Object.keys(envVars).length > 0) {
            envHtml += `<h4>${title}:</h4><ul class="env-vars">`;
            Object.entries(envVars).slice(0, 30).forEach(([key, value]) => envHtml += `<li><strong>${escapeHtml(key)}</strong>=${escapeHtml(value)}</li>`);
            if (Object.keys(envVars).length > 30) envHtml += `<li>... (${Object.keys(envVars).length - 30} more variables exist)</li>`;
            envHtml += '</ul>';
        } else { envHtml += `<h4>${title}:</h4><p class="info-message"><i>Data unavailable or none found.</i></p>`; }
        return envHtml;
    };
    html += renderEnvVars('System', 'SystemEnvironmentVariables');
    html += renderEnvVars('User', 'UserEnvironmentVariables');

    container.innerHTML += html;
    setupTableInteractivity(container);

    const appFilterInput = container.querySelector('#app-filter');
    const appTable = container.querySelector('.installed-apps-table tbody');
    const appResetButton = container.querySelector('#app-reset-button');
    if (appFilterInput && appTable) {
        appFilterInput.addEventListener('input', () => filterAppTable(appFilterInput, appTable));
    }
    if(appResetButton && appFilterInput && appTable) {
        appResetButton.addEventListener('click', () => {
            appFilterInput.value = '';
            filterAppTable(appFilterInput, appTable);
        });
    }
}

// (Keep filterAppTable as is)
function filterAppTable(inputElement, tableBody) {
    const filter = inputElement.value.toLowerCase();
    const rows = tableBody.getElementsByTagName('tr');
    let visibleCount = 0;
    for (let i = 0; i < rows.length; i++) {
        const nameCell = rows[i].getElementsByTagName('td')[0];
        const publisherCell = rows[i].getElementsByTagName('td')[2];
        let showRow = false;
        if (nameCell) {
            const nameText = nameCell.textContent || nameCell.innerText;
            if (nameText.toLowerCase().indexOf(filter) > -1) {
                showRow = true;
            }
        }
        if (!showRow && publisherCell) {
            const publisherText = publisherCell.textContent || publisherCell.innerText;
            if (publisherText.toLowerCase().indexOf(filter) > -1) {
                showRow = true;
            }
        }
        rows[i].style.display = showRow ? "" : "none";
        if (showRow) visibleCount++;
    }
}

// (Keep renderSecurityInfo as is)
function renderSecurityInfo(container, data) {
    if (!data) { container.innerHTML += '<p class="info-message"><i>Security Info data unavailable.</i></p>'; return; }
    let html = '';
     html += `<h3>Overview</h3>
              <div class="subsection">
                  <p><strong>Running as Admin:</strong> ${escapeHtml(safeGet(data, 'IsAdmin', 'N/A'))}</p>
                  <p><strong>UAC Status:</strong> <span class="status-${safeGet(data, 'UacStatus','N/A').toLowerCase()}">${escapeHtml(safeGet(data, 'UacStatus'))}</span></p>
                  <p><strong>Antivirus:</strong> ${escapeHtml(safeGet(data, 'Antivirus.Name', 'N/A'))} (<span class="status-${safeGet(data, 'Antivirus.State','N/A').toLowerCase().includes('enabled')?'pass':'fail'}">${escapeHtml(safeGet(data, 'Antivirus.State', 'Requires Admin or Not Found'))}</span>)</p>
                  <p><strong>Firewall:</strong> ${escapeHtml(safeGet(data, 'Firewall.Name', 'N/A'))} (<span class="status-${safeGet(data, 'Firewall.State','N/A').toLowerCase().includes('enabled')?'pass':'fail'}">${escapeHtml(safeGet(data, 'Firewall.State', 'Requires Admin or Not Found'))}</span>)</p>
                  <p><strong>Secure Boot Enabled:</strong> <span class="status-${String(safeGet(data, 'IsSecureBootEnabled', 'unknown')).toLowerCase()}">${escapeHtml(safeGet(data, 'IsSecureBootEnabled', 'Unknown/Error'))}</span></p>
                  <p><strong>BIOS Mode (Inferred):</strong> ${escapeHtml(safeGet(data, 'BiosMode', 'Unknown/Error'))}</p>
              </div>`;
      html += '<h3>TPM (Trusted Platform Module)</h3>';
      const tpm = safeGet(data, 'Tpm', null);
     if (tpm) {
         const isPresent = safeGet(tpm,'IsPresent',false);
         const isEnabled = safeGet(tpm,'IsEnabled',false);
         const isActivated = safeGet(tpm,'IsActivated',false);
         const tpmReady = isPresent && isEnabled && isActivated;
         html += `<div class="subsection">
                     <p><strong>Present:</strong> <span class="status-${isPresent ? 'pass' : 'fail'}">${escapeHtml(isPresent)}</span></p>`;
         if(isPresent) {
             html += `<p><strong>Enabled:</strong> <span class="status-${isEnabled ? 'pass' : 'warning'}">${escapeHtml(isEnabled)}</span></p>
                      <p><strong>Activated:</strong> <span class="status-${isActivated ? 'pass' : 'warning'}">${escapeHtml(isActivated)}</span></p>
                      <p><strong>Spec Version:</strong> ${escapeHtml(safeGet(tpm, 'SpecVersion'))}</p>
                      <p><strong>Manufacturer:</strong> ${escapeHtml(safeGet(tpm, 'ManufacturerIdTxt'))} (Version: ${escapeHtml(safeGet(tpm, 'ManufacturerVersion'))})</p>
                      <p><strong>Status Summary:</strong> <span class="status-${tpmReady ? 'pass' : 'warning'}">${escapeHtml(safeGet(tpm, 'Status'))}</span></p>`;
         }
          const tpmError = safeGet(tpm, 'ErrorMessage', null);
         if(tpmError) html += `<p class="error-inline">Error: ${escapeHtml(tpmError)}</p>`;
         html += `</div>`;
     } else { html += '<p class="info-message"><i>TPM data unavailable.</i></p>'; }

     html += '<h3>Local Users</h3>';
     const localUsers = safeGet(data, 'LocalUsers', []);
     if(localUsers.length > 0) {
         html += '<div class="subsection"><ul>';
         localUsers.forEach(user => {
             const status = safeGet(user, 'IsDisabled', false) ? 'Disabled' : 'Enabled';
             html += `<li><strong>${escapeHtml(safeGet(user, 'Name'))}</strong> (Status: ${status}, PwdReq: ${escapeHtml(safeGet(user, 'PasswordRequired'))}) - SID: ${escapeHtml(safeGet(user, 'SID'))}</li>`;
         });
         html += '</ul></div>';
     } else { html += '<p class="info-message"><i>Local User data unavailable or none found.</i></p>'; }

      html += '<h3>Local Groups</h3>';
      const localGroups = safeGet(data, 'LocalGroups', []);
     if(localGroups.length > 0) {
         html += '<div class="subsection"><ul>';
         localGroups.slice(0, 20).forEach(grp => {
             html += `<li><strong>${escapeHtml(safeGet(grp, 'Name'))}</strong> - ${escapeHtml(safeGet(grp, 'Description'))}</li>`;
         });
         if (localGroups.length > 20) html += `<li>... (${localGroups.length - 20} more groups exist)</li>`;
         html += '</ul></div>';
     } else { html += '<p class="info-message"><i>Local Group data unavailable or none found.</i></p>'; }

     html += '<h3>Network Shares</h3>';
     const shares = safeGet(data, 'NetworkShares', []);
      if(shares.length > 0) {
          html += '<div class="subsection"><ul>';
          shares.forEach(share => {
              html += `<li><strong>${escapeHtml(safeGet(share, 'Name'))}</strong> -> ${escapeHtml(safeGet(share, 'Path'))} (${escapeHtml(safeGet(share, 'Description'))})</li>`;
          });
          html += '</ul></div>';
      } else { html += '<p class="info-message"><i>Network Share data unavailable or none found.</i></p>'; }

    container.innerHTML += html;
}

// (Keep renderPingHtml as is)
function renderPingHtml(pingResult, defaultName) {
    if (!pingResult) return `<li>Ping ${defaultName || 'Unknown Target'}: <span class="status-na">N/A</span></li>`;
    const target = escapeHtml(safeGet(pingResult, 'Target', defaultName || 'Unknown Target'));
    const resolvedIP = safeGet(pingResult, 'ResolvedIpAddress', null);
    const displayTarget = resolvedIP ? `${target} [${resolvedIP}]` : target;
    let statusClass = 'status-unknown';
    let statusText = escapeHtml(safeGet(pingResult, 'Status', 'N/A'));
    let lowerStatusText = statusText.toLowerCase();

    if (lowerStatusText === 'success') statusClass = 'status-pass';
    else if (lowerStatusText.includes('error') || lowerStatusText === 'timedout' || lowerStatusText.includes('unreachable') || lowerStatusText.includes('hardwareerror')) statusClass = 'status-fail';
    else if (lowerStatusText === 'ttlexpired') statusClass = 'status-info';

    let pingHtml = `<li>Ping <strong>${displayTarget}</strong>: Status <span class="${statusClass}">${statusText}</span>`;
    if (statusText === 'Success') pingHtml += ` (${escapeHtml(safeGet(pingResult, 'RoundtripTimeMs'))}ms)`;
    const errorMsg = safeGet(pingResult, 'Error', null);
    if (errorMsg) pingHtml += ` <span class="error-inline">[Error: ${escapeHtml(errorMsg)}]</span>`;
    pingHtml += '</li>';
    return pingHtml;
};

// (Keep renderDnsResolutionHtml as is)
function renderDnsResolutionHtml(dnsResult) {
     if (!dnsResult) return `<li>DNS Resolution Test: <span class="status-na">N/A</span></li>`;
     const target = escapeHtml(safeGet(dnsResult, 'Hostname', 'Default Host'));
     const success = safeGet(dnsResult, 'Success', false);
     let statusClass = success ? 'status-pass' : 'status-fail';
     let statusText = success ? 'Success' : 'Fail';

     let dnsHtml = `<li>DNS Resolution Test (<strong>${target}</strong>): Status <span class="${statusClass}">${statusText}</span>`;
     if (success) {
         dnsHtml += ` (${escapeHtml(safeGet(dnsResult, 'ResolutionTimeMs'))}ms)`;
         dnsHtml += ` -> IPs: ${escapeHtml(safeGet(dnsResult, 'ResolvedIpAddresses', []).join(', '))}`;
     }
      const errorMsg = safeGet(dnsResult, 'Error', null);
     if (errorMsg) dnsHtml += ` <span class="error-inline">[Error: ${escapeHtml(errorMsg)}]</span>`;
     dnsHtml += '</li>';
     return dnsHtml;
}

// (Keep renderNetworkInfo as is, uses renderNic which was updated)
function renderNetworkInfo(container, data) {
     if (!data) { container.innerHTML += '<p class="info-message"><i>Network Info data unavailable.</i></p>'; return; }
     let html = '';
     html += '<h3>Network Adapters</h3>';
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
    html += '<div id="nic-list">';
    const adapters = safeGet(data, 'Adapters', []);
    if (adapters.length > 0) {
        html += adapters.map(nic => renderNic(nic)).join(''); // renderNic was updated
    }
    else { html += '<p class="info-message"><i>Adapter data unavailable or none found.</i></p>'; }
    html += '</div>';

    const renderListenersTable = (title, listenersListKey) => {
        let tableHtml = `<h3>Active ${title} Listeners</h3>`;
        const listeners = safeGet(data, listenersListKey, []);
        if (listeners.length > 0) {
            const headers = ['Local Address:Port', 'PID', 'Process Name', 'Error'];
             const rows = listeners.map(l => [
                 `${safeGet(l, 'LocalAddress')}:${safeGet(l, 'LocalPort')}`,
                 safeGet(l, 'OwningPid', '-'),
                 safeGet(l, 'OwningProcessName', 'N/A'),
                 safeGet(l,'Error','')
                ]);
             tableHtml += createTable(headers, rows, 'listeners-table data-table');
        } else { tableHtml += '<p class="info-message"><i>Data unavailable or none found.</i></p>'; }
        return tableHtml;
    };
    html += renderListenersTable('TCP', 'ActiveTcpListeners');
    html += renderListenersTable('UDP', 'ActiveUdpListeners');

     html += '<h3>Active TCP Connections</h3>';
     const connections = safeGet(data, 'ActiveTcpConnections', []);
    if (connections.length > 0) {
        const headers = ['Local Addr:Port', 'Remote Addr:Port', 'State', 'PID', 'Process Name', 'Error'];
         const rows = connections.map(c => [
             `${safeGet(c, 'LocalAddress')}:${safeGet(c, 'LocalPort')}`,
             `${safeGet(c, 'RemoteAddress')}:${safeGet(c, 'RemotePort')}`,
             safeGet(c, 'State'),
             safeGet(c, 'OwningPid', '-'),
             safeGet(c, 'OwningProcessName', 'N/A'),
             safeGet(c,'Error','')
            ]);
         html += createTable(headers, rows, 'connections-table data-table');
    } else { html += '<p class="info-message"><i>TCP Connection data unavailable or none found.</i></p>'; }

    html += '<h3>Connectivity Tests</h3>';
    const tests = safeGet(data, 'ConnectivityTests', null);
    if(tests) {
        html += '<div class="subsection"><ul>';
        html += renderPingHtml(safeGet(tests, 'GatewayPing', null), 'Default Gateway');
        const dnsPings = safeGet(tests, 'DnsPings', []);
        if (dnsPings.length > 0) dnsPings.forEach(p => html += renderPingHtml(p));
        else html += `<li>DNS Server Ping Tests: <span class="status-na">N/A</span></li>`;
        html += renderDnsResolutionHtml(safeGet(tests, 'DnsResolution', null));
        html += '</ul>';

        const traceResults = safeGet(tests, 'TracerouteResults', []);
        if (traceResults.length > 0) {
            html += `<h4>Traceroute to ${escapeHtml(safeGet(tests, 'TracerouteTarget', 'Unknown Target'))}</h4>`;
            const headers = ['Hop', 'Time (ms)', 'Address', 'Status', 'Error'];
             const rows = traceResults.map(h => [
                 safeGet(h, 'Hop'), safeGet(h, 'RoundtripTimeMs', '*'),
                 safeGet(h, 'Address', '*'), safeGet(h, 'Status'), safeGet(h,'Error','')
                ]);
             html += createTable(headers, rows, 'traceroute-table');
        } else if (safeGet(tests, 'TracerouteTarget', null)) {
             html += `<h4>Traceroute to ${escapeHtml(safeGet(tests, 'TracerouteTarget'))}</h4><p class="info-message"><i>No results available.</i></p>`;
        }
         html += '</div>';
    } else { html += '<p class="info-message"><i>Connectivity Test data unavailable.</i></p>'; }

    container.innerHTML += html;

    const nicFilter = container.querySelector('#nic-status-filter');
    if (nicFilter) {
         nicFilter.addEventListener('change', (e) => {
             const selectedStatus = e.target.value;
             const nicListContainer = container.querySelector('#nic-list');
             // Use window.reportData as the source of truth for filtering
             const allAdapters = safeGet(window.reportData, 'Network.Adapters', []);

             if (nicListContainer && allAdapters.length >= 0) { // Check >= 0 to handle empty initial state
                 const filteredAdapters = selectedStatus
                     ? allAdapters.filter(nic => safeGet(nic, 'Status', 'Unknown').toString() === selectedStatus)
                     : allAdapters;

                 if (filteredAdapters.length > 0) {
                     nicListContainer.innerHTML = filteredAdapters.map(nic => renderNic(nic)).join('');
                 } else {
                     nicListContainer.innerHTML = '<p class="info-message"><i>No network adapters match the selected status.</i></p>';
                 }
             } else if (nicListContainer) {
                 nicListContainer.innerHTML = '<p class="info-message"><i>Adapter data unavailable.</i></p>';
             }
         });
     }
}

// (Keep renderEventLogInfo as is)
function renderEventLogInfo(container, data) {
    if (!data) { container.innerHTML += '<p class="info-message"><i>Event Log data unavailable.</i></p>'; return; }
   const renderLog = (title, entriesListKey) => {
       let logHtml = `<h3>${title} Log (Recent Errors/Warnings)</h3>`;
       const entries = safeGet(data, entriesListKey, []);
       if (entries.length > 0) {
            if (entries.length === 1 && safeGet(entries[0], 'Source', null) === null) {
                logHtml += `<p class="info-message"><i>${escapeHtml(safeGet(entries[0], 'Message'))}</i></p>`;
            } else {
                let actualEntries = entries.filter(e => safeGet(e, 'Source', null) !== null);
                if(actualEntries.length > 0) {
                    actualEntries.sort((a, b) => { /* Sort logic... */
                        const typeA = safeGet(a, 'EntryType', 'Unknown').toLowerCase();
                        const typeB = safeGet(b, 'EntryType', 'Unknown').toLowerCase();
                        const timeA = new Date(safeGet(a, 'TimeGenerated', 0)).getTime();
                        const timeB = new Date(safeGet(b, 'TimeGenerated', 0)).getTime();
                        if (typeA === 'error' && typeB !== 'error') return -1;
                        if (typeA !== 'error' && typeB === 'error') return 1;
                        return timeB - timeA;
                    });
                    logHtml += '<ul class="event-log-list">';
                    actualEntries.slice(0, 20).forEach(entry => {
                       const fullMsg = escapeHtml(safeGet(entry, 'Message', ''));
                       const entryType = safeGet(entry, 'EntryType', 'unknown').toLowerCase();
                       const entryTypeClass = `event-${entryType}`;
                       logHtml += `<li class="${entryTypeClass}">
                                       <span class="event-time">${formatNullableDateTime(safeGet(entry, 'TimeGenerated'))}:</span>
                                       <span class="event-type">[${escapeHtml(safeGet(entry, 'EntryType'))}]</span>
                                       <span class="event-source">${escapeHtml(safeGet(entry, 'Source'))}</span>
                                       <span class="event-id">(ID: ${escapeHtml(safeGet(entry, 'InstanceId'))})</span>
                                       <pre class="event-message-content">${fullMsg}</pre> </li>`;
                    });
                    if (actualEntries.length > 20) logHtml += '<li>... (more entries exist)</li>';
                    logHtml += '</ul>';
                } else if (entries.length > 0 && !actualEntries.length) {
                     logHtml += '<p class="info-message"><i>Could not retrieve events: ';
                     logHtml += entries.map(e => escapeHtml(safeGet(e, 'Message'))).join('; ');
                     logHtml += '</i></p>';
                } else {
                     logHtml += '<p class="info-message"><i>No recent Error/Warning entries found.</i></p>';
                }
            }
       } else {
           logHtml += '<p class="info-message"><i>Log data unavailable or none found.</i></p>';
       }
       return logHtml;
   };
   container.innerHTML += renderLog('System', 'SystemLogEntries');
   container.innerHTML += renderLog('Application', 'ApplicationLogEntries');
}

// (Keep renderAnalysisSummary as is)
function renderAnalysisSummary(container, data) {
     container.classList.add('analysis-section');
     let html = '';
     if (!data) { html += '<p class="info-message"><i>Analysis data unavailable.</i></p>'; container.innerHTML += html; return; }
      const analysisError = safeGet(data, 'SectionCollectionErrorMessage', null);
     if (analysisError) { html += `<div class="error-message critical-section-error">Analysis Engine Error: ${escapeHtml(analysisError)}</div>`; }
     const readiness = safeGet(data, 'Windows11Readiness', null);
     const readinessChecks = safeGet(readiness, 'Checks', []);
      if (readinessChecks.length > 0) { /* ... readiness table ... */
         html += '<h3>Windows 11 Readiness Check</h3>';
         const overall = safeGet(readiness, 'OverallResult', null);
         let overallStatus = 'INCOMPLETE/ERROR'; let overallClass = 'status-warning';
         if (overall === true) { overallStatus = 'PASS'; overallClass = 'status-pass'; }
         if (overall === false) { overallStatus = 'FAIL'; overallClass = 'status-fail'; }
         html += `<p><strong>Overall Status: <span class="${overallClass}">${overallStatus}</span></strong></p>`;
         const headers = ['Component', 'Requirement', 'Status', 'Details'];
         const rows = readinessChecks.map(c => [ safeGet(c,'ComponentChecked'), safeGet(c,'Requirement'), safeGet(c,'Status'), safeGet(c,'Details') ]);
         html += createTable(headers, rows, 'readiness-table');
      }
     const criticalEvents = safeGet(data, 'CriticalEventsFound', []);
     if (criticalEvents.length > 0) { /* ... critical events list ... */
         html += '<h3 class="critical-events">Critical Events Found</h3>';
         html += '<ul class="critical-events-list">';
         criticalEvents.forEach(event => { /* ... format event ... */
             html += `<li class="event-critical">
                         <span class="event-time">${formatNullableDateTime(safeGet(event, 'Timestamp'))}</span> -
                         <span class="event-source">[${escapeHtml(safeGet(event, 'LogName'))}] ${escapeHtml(safeGet(event, 'Source'))}</span>
                         <span class="event-id">(ID: ${escapeHtml(safeGet(event, 'EventID'))})</span><br>
                         <span class="event-message-excerpt">${escapeHtml(safeGet(event, 'MessageExcerpt'))}</span>
                      </li>`;
         });
         html += '</ul>';
      }
    const renderList = (title, itemsList, listClassPrefix) => { /* ... list rendering logic ... */
        const items = safeGet(data, itemsList, []);
        if (items.length > 0) {
            let listHtml = `<h3 class="${listClassPrefix}">${title}</h3><ul class="${listClassPrefix}-list">`;
            items.forEach(item => {
                let itemClass = ''; let itemText = escapeHtml(item);
                if (itemText.startsWith('[ACTION REQUIRED]')) { itemClass = 'issue-action'; itemText = itemText.substring('[ACTION REQUIRED]'.length).trim(); }
                else if (itemText.startsWith('[CRITICAL]')) { itemClass = 'issue-critical'; itemText = itemText.substring('[CRITICAL]'.length).trim(); }
                else if (itemText.startsWith('[INVESTIGATE]')) { itemClass = 'issue-investigate'; itemText = itemText.substring('[INVESTIGATE]'.length).trim(); }
                else if (itemText.startsWith('[RECOMMENDED]')) { itemClass = 'suggestion-recommended'; itemText = itemText.substring('[RECOMMENDED]'.length).trim(); }
                else if (itemText.startsWith('[INFO]')) { itemClass = 'info-note'; itemText = itemText.substring('[INFO]'.length).trim(); }
                listHtml += `<li class="${itemClass}">${itemText}</li>`;
            });
            listHtml += `</ul>`; return listHtml;
        } return '';
    };
    html += renderList('Potential Issues Found', 'PotentialIssues', 'issues');
    html += renderList('Suggestions', 'Suggestions', 'suggestions');
    html += renderList('Informational Notes', 'Info', 'info');
    const issues = safeGet(data, 'PotentialIssues', []);
    const suggestions = safeGet(data, 'Suggestions', []);
    const info = safeGet(data, 'Info', []);
    if (!issues.length && !suggestions.length && !info.length && !criticalEvents.length && !analysisError && !readinessChecks.length) {
         html += `<p class="info-message">No specific issues, suggestions, or notes generated by the analysis.</p>`;
    }
    const thresholds = safeGet(data, 'Configuration.AnalysisThresholds', null);
     if (thresholds) { /* ... thresholds list ... */
          html += '<h3>Analysis Thresholds Used</h3><ul class="thresholds-list">';
          html += `<li>Memory High/Elevated %: ${escapeHtml(safeGet(thresholds, 'HighMemoryUsagePercent'))}/${escapeHtml(safeGet(thresholds, 'ElevatedMemoryUsagePercent'))}</li>`;
          html += `<li>Disk Critical/Low Free %: ${escapeHtml(safeGet(thresholds, 'CriticalDiskSpacePercent'))}/${escapeHtml(safeGet(thresholds, 'LowDiskSpacePercent'))}</li>`;
          /* ... other thresholds ... */
           html += `<li>CPU High/Elevated %: ${escapeHtml(safeGet(thresholds, 'HighCpuUsagePercent'))}/${escapeHtml(safeGet(thresholds, 'ElevatedCpuUsagePercent'))}</li>`;
           html += `<li>High Disk Queue Length: ${escapeHtml(safeGet(thresholds, 'HighDiskQueueLength'))}</li>`;
           html += `<li>Max Sys Log Errors (Issue/Suggest): ${escapeHtml(safeGet(thresholds, 'MaxSystemLogErrorsIssue'))}/${escapeHtml(safeGet(thresholds, 'MaxSystemLogErrorsSuggestion'))}</li>`;
            html += `<li>Max App Log Errors (Issue/Suggest): ${escapeHtml(safeGet(thresholds, 'MaxAppLogErrorsIssue'))}/${escapeHtml(safeGet(thresholds, 'MaxAppLogErrorsSuggestion'))}</li>`;
           html += `<li>Driver Age Warning (Years): ${escapeHtml(safeGet(thresholds, 'DriverAgeWarningYears'))}</li>`;
           html += `<li>Ping Latency Warning (ms): ${escapeHtml(safeGet(thresholds, 'MaxPingLatencyWarningMs'))}</li>`;
           html += `<li>Traceroute Hop Latency Warning (ms): ${escapeHtml(safeGet(thresholds, 'MaxTracerouteHopLatencyWarningMs'))}</li>`;
           html += `<li>Max Uptime Suggestion (Days): ${escapeHtml(safeGet(thresholds, 'MaxUptimeDaysSuggestion'))}</li>`;
          html += '</ul>';
      }
     container.innerHTML += html;
}

// (Keep setupTableInteractivity as is)
function setupTableInteractivity(container) {
    container.querySelectorAll('th.sortable').forEach(header => {
        if (header.dataset.listenerAttached === 'true') return;
        header.dataset.listenerAttached = 'true';
        header.addEventListener('click', () => { /* ... sorting logic ... */
            const table = header.closest('table'); if (!table) return;
            const columnIndex = parseInt(header.dataset.columnIndex, 10);
            const tbody = table.querySelector('tbody'); if (!tbody) return;
            const rows = Array.from(tbody.querySelectorAll('tr'));
            const isAscending = header.classList.contains('sort-asc');
            const newDirection = isAscending ? 'desc' : 'asc';
            rows.sort((a, b) => { /* ... comparison logic ... */
                 const cellA = a.cells[columnIndex]?.textContent.trim().toLowerCase() ?? '';
                 const cellB = b.cells[columnIndex]?.textContent.trim().toLowerCase() ?? '';
                 let numA = parseFloat(cellA.replace(/[^0-9.-]+/g,""));
                 let numB = parseFloat(cellB.replace(/[^0-9.-]+/g,""));
                 if (table.classList.contains('installed-apps-table') && columnIndex === 3) {
                     numA = Date.parse(a.cells[columnIndex]?.textContent.trim() ?? '');
                     numB = Date.parse(b.cells[columnIndex]?.textContent.trim() ?? '');
                 }
                 if (!isNaN(numA) && !isNaN(numB)) {
                     return (numA - numB) * (newDirection === 'asc' ? 1 : -1);
                 }
                 return cellA.localeCompare(cellB) * (newDirection === 'asc' ? 1 : -1);
             });
            table.querySelectorAll('th.sortable').forEach(th => th.classList.remove('sort-asc', 'sort-desc'));
            header.classList.add(newDirection === 'asc' ? 'sort-asc' : 'sort-desc');
            rows.forEach(row => tbody.appendChild(row));
        });
    });
    container.querySelectorAll('.pagination-controls button').forEach(button => { /* ... pagination placeholder ... */
        if (button.dataset.listenerAttached === 'true') return;
        button.dataset.listenerAttached = 'true';
         button.addEventListener('click', () => {
             console.log("Pagination button clicked - full implementation needed.");
             alert("Pagination not fully implemented.");
         });
     });
}

}); // End DOMContentLoaded