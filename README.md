# Advanced Windows Diagnostics 🩺💻

A comprehensive command-line tool and Standalone Executable designed to gather detailed diagnostic information from Windows systems, analyze potential issues, and generate smart reports to assist technicians and end-users in troubleshooting.

---

## ✨ Features

* **Comprehensive Data Collection:** Gathers information across multiple categories:
    * 🖥️ **System:** OS details, computer model, BIOS, motherboard, uptime, pending reboots.
    * ⚙️ **Hardware:** CPU, RAM (total, available, modules), Physical & Logical Disks (including SMART status), Volumes (including BitLocker status), GPU, Monitors, Audio Devices.
    * 💾 **Software:** Installed applications, Windows Updates (Hotfixes), relevant services, startup programs, environment variables.
    * 🛡️ **Security:** Admin status, UAC, Antivirus & Firewall status (requires Admin), local users & groups, network shares, TPM details, Secure Boot status.
    * ⏱️ **Performance:** Snapshot of CPU usage, available memory, disk queue length, top processes by CPU & memory usage.
    * 🌐 **Network:** Detailed adapter configuration (IPs, DNS, DHCP, MAC, Driver Date), active TCP/UDP listeners & connections (with PIDs if run as Admin), optional connectivity tests (Ping, DNS resolution, Traceroute).
    * 📊 **Event Logs:** Recent Error and Warning entries from System and Application logs.
    * 💥 **Stability:** Checks for recent system crash dump files (`.dmp`).
    * 🔍 **System Integrity:** Parses SFC (System File Checker) and DISM logs to report on OS file health - To be implemented.
* **Automated Analysis:** (Optional) Interprets collected data against configurable thresholds to identify:
    * ❗ Potential Issues (e.g., low disk space, high resource usage, critical service failures).
    * 💡 Suggestions for investigation or resolution.
    * ℹ️ Informational notes.
    * 🔥 Critical Event Log entries.
* **Flexible Reporting:** Generates reports in multiple formats:
    * 📄 **Text:** Human-readable summary.
    * 📝 **JSON:** Structured data, suitable for parsing or viewing in the HTML viewer.
    * Ⓜ️ **Markdown:** Basic Markdown output.
* **Interactive HTML Viewer:** (`Display.html`) Provides a user-friendly interface to load and explore the generated JSON reports with collapsible sections and search functionality.
* **Configurable:** Customize analysis thresholds and network test targets via `web/appsettings.json`.
* **Command-Line Interface:** Offers various options to control data collection, output, and network tests.

## 🎯 Goal & Use Cases

The primary goal is to **accelerate the diagnosis of common Windows issues** by providing a consolidated view of relevant system information and potential problems.

**Use Cases:**

* **Technicians:** Quickly gather baseline system information and identify common problem areas (disk space, resource bottlenecks, critical errors, security misconfigurations) without manually checking numerous locations.
* **Remote Support:** Users can run the tool and provide the report file (JSON or text) to support personnel for analysis.
* **System Baselining:** Capture a snapshot of system configuration and health at a specific point in time.
* **Troubleshooting:** Help identify potential causes for performance issues, instability (crashes), network problems, or software conflicts.

## 🛠️ Prerequisites for Building

* **.NET SDK:** You'll need a compatible .NET SDK installed. Based on the project structure, **.NET 6.0 or later** is recommended (verify based on the actual `.csproj` file if available, otherwise start with the latest LTS version). You can download it from the official [.NET website](https://dotnet.microsoft.com/download).
* **Windows Operating System:** The tool relies heavily on Windows-specific APIs (WMI, Registry, P/Invoke) and is designed to run on Windows.

## 🏗️ Building the Project

1.  **Clone/Download:** Get the source code onto your machine.
2.  **Open Terminal:** Navigate to the root directory of the project (where the `.sln` or `.csproj` file is located) in your terminal (Command Prompt, PowerShell, Windows Terminal).
3.  **Restore Dependencies:** Run `dotnet restore` (often done automatically by the build command).
4.  **Build:** Execute the build command:
    ```bash
    dotnet build -c Release
    ```
    * `-c Release`: Specifies the Release configuration for an optimized build.
    * This will compile the code and place the output (including the `.exe` and necessary `.dll` files) typically in the `bin/Release/netX.Y` directory (where `netX.Y` is your target framework, e.g., `net6.0-windows`).

## 🚀 Publishing as a Standalone Executable

To create a single `.exe` file that includes the .NET runtime and all dependencies (allowing it to run on machines without the .NET SDK/Runtime installed), use the `publish` command.

1.  **Open Terminal:** Navigate to the root directory of the project.
2.  **Publish Command:** Execute the following command:

    ```bash
    dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
    ```

    * `-c Release`: Use the Release configuration.
    * `-r win-x64`: Specify the target runtime identifier (e.g., `win-x64` for 64-bit Windows, `win-x86` for 32-bit). Choose the one appropriate for your target machines.
    * `--self-contained true`: Include the .NET runtime within the published output.
    * `/p:PublishSingleFile=true`: Package the application and its dependencies into a single executable file. *(Note: This might extract files to a temporary location on first run)*.
    * `/p:IncludeNativeLibrariesForSelfExtract=true`: Ensures native dependencies (like those needed for P/Invoke) are included correctly within the single file bundle.

3.  **Find Executable:** The standalone executable will be located in the publish directory, typically `bin/Release/netX.Y/win-x64/publish/`.

## ▶️ Usage

Run the compiled executable (`Advanced-Windows-Diagnostics.exe`) from the command line, or like a normal application (i.e. Double click or Right click and select "Run as Administrator").

**Basic Usage:**

```bash
# Run all diagnostics and output to default JSON file in ./Reports/
Advanced-Windows-Diagnostics.exe

# Run all diagnostics and output to a specific JSON file
Advanced-Windows-Diagnostics.exe -o C:\Temp\MyReport.json

# Run only System and Hardware sections, output as text to console
Advanced-Windows-Diagnostics.exe -s System Hardware --format text --quiet

# Run Network section with traceroute and custom DNS test, output JSON
Advanced-Windows-Diagnostics.exe -s Network --traceroute 8.8.8.8 --dns-test myinternalserver -o network_report.json

Key Command-Line Options:

--sections, -s: Specify sections to run (comma-separated or multiple flags: System, Hardware, Software, Security, Performance, Network, Events, Stability, Analysis, all). Default: all.

--output, -o: Specify the output file path (e.g., MyReport.json). Default: ./Reports/DiagReport_<hostname>_<timestamp>.<ext>.

--format: Output format (json, text, markdown). Default: json.

--json: Shortcut for --format json. Implies --quiet.

--quiet, -q: Suppress console status messages (useful for scripting or when only file output is needed). JSON output is always quiet.

--traceroute <host_or_ip>: Perform a traceroute to the specified target.

--dns-test <hostname>: Perform a DNS resolution test for the specified hostname (defaults to value in appsettings.json or www.google.com).

--timeout <seconds>: Set a global timeout for the entire collection process (default: 120s, min: 10s, max: 600s).

--debug-log: Enable detailed internal logging to WinDiagInternal.log in the application directory.

❗ **Important: For complete data collection (Security details, SMART, PIDs, etc.), run the tool as Administrator.**

⚙️ Configuration (web/appsettings.json)
The web/appsettings.json file allows customization of:

Analysis Thresholds: Define limits for CPU/Memory usage, disk space, event log counts, driver age, etc., used by the Analysis engine.

Network Settings: Set the default hostname for the --dns-test option.

If the file is missing or invalid, the application uses internal default values.

📊 HTML Viewer (Display.html)
The generated JSON report (.json file) can be viewed interactively using the included Display.html file:

Generate a report using the default JSON format (or specify --format json).

Open Display.html in your web browser.

Click the "Select Report JSON File" button and choose the .json report file you generated.

The report data will be loaded and displayed in collapsible sections.

Use the search bar to filter the report content.

(Note: If the tool is run interactively and generates a JSON file, it will attempt to automatically open the viewer in your default browser.)

🤝 Contributers:
Jack Warren - "Official-CalcOS"