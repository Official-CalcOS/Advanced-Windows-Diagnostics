using System;
using System.Collections.Generic;
using System.CommandLine; // Command line parsing
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Diagnostics; // For Stopwatch
using System.IO;      // <--- ADDED for Path, File
using System.Linq;    // <--- ADDED for Any()
using System.Runtime.Versioning; // Required for [SupportedOSPlatform]
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization; // Required for JsonIgnoreCondition
using System.Threading;          // For CancellationToken
using System.Threading.Tasks;
using DiagnosticToolAllInOne.Analysis;       // Namespace for AnalysisEngine
using DiagnosticToolAllInOne.Collectors;    // Namespace for Collectors
using DiagnosticToolAllInOne.Helpers;       // Namespace for Helpers
using DiagnosticToolAllInOne.Reporting;     // Namespace for TextReportGenerator
// --- Added for Configuration ---
using Microsoft.Extensions.Configuration;
// using Microsoft.Extensions.Configuration.Binder; // Might need if using .Get<T>() extensively

namespace DiagnosticToolAllInOne
{
    public class Program
    {
        private static bool _isAdmin = false;
        private static bool _quietMode = false;
        private const string Separator = "----------------------------------------";

        // Log File Constants
        private const string LogDirectoryName = "WinDiagLogs";
        private const string LogFilePrefix = "WinDiagReport_";
        private const string LogFileExtension = ".log"; // Changed to .log for clarity

        // --- Configuration object ---
        private static AppConfiguration? _appConfig = null;
        private const string ConfigFileName = "appsettings.json";


        [SupportedOSPlatform("windows")]
        static async Task<int> Main(string[] args)
        {
            _isAdmin = AdminHelper.IsRunningAsAdmin();

            // --- Load Configuration ---
            LoadConfiguration(); // Load settings from appsettings.json

            // --- System.CommandLine Setup ---
            var rootCommand = new RootCommand("Advanced Windows Diagnostic Tool (All-in-One) - Modular");

            // Options
            var sectionsOption = new Option<List<string>>(
                aliases: new[] { "--sections", "-s" },
                description: "Specify sections (comma-separated or multiple options): system, hardware, software, security, performance, network, events, analysis, all. Default: all.",
                parseArgument: result => result.Tokens.Select(t => t.Value).ToList()
            ) { AllowMultipleArgumentsPerToken = true, Arity = ArgumentArity.ZeroOrMore };
            sectionsOption.AddCompletions("system", "hardware", "software", "security", "performance", "network", "events", "analysis", "all");

            var outputOption = new Option<FileInfo?>(
                aliases: new[] { "--output", "-o" },
                description: "Output report to specified file path.");

            var jsonOption = new Option<bool>(
                name: "--json",
                description: "Output report in JSON format (implies --quiet).");

            var quietOption = new Option<bool>(
                aliases: new[] { "--quiet", "-q" },
                description: "Suppress console output (useful mainly with --output).");

            var tracerouteOption = new Option<string?>(
                 name: "--traceroute",
                 description: "Perform a traceroute to the specified host or IP address.");

             // Option to specify DNS test target, default from config
            var dnsTestOption = new Option<string?>(
                 name: "--dns-test",
                 description: $"Perform a DNS resolution test for the specified hostname. Default: {_appConfig?.NetworkSettings?.DefaultDnsTestHostname ?? "www.google.com"}",
                 getDefaultValue: () => _appConfig?.NetworkSettings?.DefaultDnsTestHostname); // Get default from config

            var timeoutOption = new Option<int>(
                 name: "--timeout",
                 description: "Global timeout in seconds for collection tasks.",
                 getDefaultValue: () => 120); // Default timeout 120 seconds

            // Removed help option as System.CommandLine adds it automatically

            rootCommand.AddOption(sectionsOption);
            rootCommand.AddOption(outputOption);
            rootCommand.AddOption(jsonOption);
            rootCommand.AddOption(quietOption);
            rootCommand.AddOption(tracerouteOption);
            rootCommand.AddOption(dnsTestOption); // Add new option
            rootCommand.AddOption(timeoutOption);

            rootCommand.SetHandler(async (InvocationContext context) =>
            {
                var sections = context.ParseResult.GetValueForOption(sectionsOption) ?? new List<string>();
                var outputFile = context.ParseResult.GetValueForOption(outputOption);
                var outputJson = context.ParseResult.GetValueForOption(jsonOption); // Initial value from flag
                _quietMode = context.ParseResult.GetValueForOption(quietOption) || outputJson;
                var tracerouteTarget = context.ParseResult.GetValueForOption(tracerouteOption);
                var dnsTestTarget = context.ParseResult.GetValueForOption(dnsTestOption);
                var timeoutSeconds = context.ParseResult.GetValueForOption(timeoutOption);

                // --- >> ADD THIS LOGIC << ---
                bool explicitOutputSet = context.ParseResult.HasOption(outputOption);
                bool explicitJsonSet = context.ParseResult.HasOption(jsonOption);

                // If no explicit output file or JSON flag is given, default to JSON output
                if (!explicitOutputSet && !explicitJsonSet)
                {
                    outputJson = true; // Force JSON output
                    _quietMode = true; // Usually implies quiet mode as well

                    // Generate a default filename similar to the automatic log
                    try
                    {
                        string baseDirectory = AppContext.BaseDirectory;
                        // Put default JSON in the main app directory or a subdirectory
                        string outputDirectory = Path.Combine(baseDirectory, "JSONReports"); // Example subdirectory
                        Directory.CreateDirectory(outputDirectory); // Ensure directory exists
                        string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        string hostName = Environment.MachineName.Replace(@"\", "-").Replace("/", "-").Replace(":", "-");
                        // Use a different prefix/extension for the default JSON output
                        string defaultFileName = $"DefaultReport_{hostName}_{timeStamp}.json";
                        outputFile = new FileInfo(Path.Combine(outputDirectory, defaultFileName));
                        if (!_quietMode) // Only print status if not forced quiet
                        {
                            StatusUpdate($"No output flags specified. Defaulting to JSON output at: {outputFile.FullName}", color: ConsoleColor.Cyan);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Handle potential error creating default path/filename
                        StatusUpdate($"[WARNING] Could not generate default JSON output path: {ex.Message}. Output may fail.", color: ConsoleColor.Yellow);
                        outputFile = null; // Prevent trying to write if path failed
                    }
                }
                // --- >> END OF ADDED LOGIC << ---


                // Set up cancellation token (clamp timeout etc. - keep existing logic)
                timeoutSeconds = Math.Clamp(timeoutSeconds, 10, 600);
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                var cancellationToken = cts.Token;

                var stopwatch = Stopwatch.StartNew();
                // Pass DNS test target to RunDiagnostics
                var report = await RunDiagnostics(sections, tracerouteTarget, dnsTestTarget, cancellationToken);
                stopwatch.Stop();

                if (!_quietMode) // Keep existing status updates
                {
                    Console.WriteLine($"{Separator}\nTotal execution time: {stopwatch.ElapsedMilliseconds / 1000.0:0.##} seconds.");
                    if (cancellationToken.IsCancellationRequested) { /* ... existing timeout message ... */ }
                }

                // Pass the potentially modified outputFile and outputJson variables
                await HandleOutput(report, outputFile, outputJson);

                // Determine exit code based on CRITICAL errors or timeout
                 bool hasCriticalErrors = !string.IsNullOrEmpty(report.System?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Hardware?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Software?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Security?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Performance?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Network?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Events?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Analysis?.SectionCollectionErrorMessage);

                 if (cancellationToken.IsCancellationRequested)
                 {
                     context.ExitCode = 2; // Timeout/Cancelled exit code
                 }
                 else if (hasCriticalErrors)
                 {
                     context.ExitCode = 1; // Critical error exit code
                     if (!_quietMode)
                     {
                         Console.ForegroundColor = ConsoleColor.Red;
                         Console.Error.WriteLine("\n[CRITICAL ERRORS] One or more diagnostic sections encountered critical errors during collection or analysis.");
                         Console.ResetColor();
                     }
                 }
                 else
                 {
                     context.ExitCode = 0; // Success
                 }
            });

            return await rootCommand.InvokeAsync(args);
        }

        // --- Configuration Loading Method ---
        private static void LoadConfiguration()
        {
             try
             {
                  var configBuilder = new ConfigurationBuilder()
                       .SetBasePath(AppContext.BaseDirectory) // Look in the application's base directory
                       .AddJsonFile(ConfigFileName, optional: true, reloadOnChange: false); // Make file optional

                  IConfigurationRoot configuration = configBuilder.Build();

                  // Bind the "AppConfiguration" section of the JSON to our AppConfiguration object
                  // Requires Microsoft.Extensions.Configuration.Binder NuGet package
                  _appConfig = configuration.GetSection("AppConfiguration").Get<AppConfiguration>();

                  // If config file is missing or section is empty, create a default object
                  _appConfig ??= new AppConfiguration();

                  // Ensure nested properties are also initialized with defaults if they are null after binding
                  _appConfig.AnalysisThresholds ??= new AnalysisThresholds();
                  _appConfig.NetworkSettings ??= new NetworkSettings();

                  // Optional: Log successful config load
                  // StatusUpdate($"Configuration loaded from '{ConfigFileName}'.");
             }
             catch (Exception ex)
             {
                  // Log warning but continue with defaults
                  Console.ForegroundColor = ConsoleColor.Yellow;
                  Console.Error.WriteLine($"[WARNING] Could not load configuration from '{ConfigFileName}': {ex.Message}. Using default settings.");
                  Console.ResetColor();
                  // Ensure defaults are created if config loading fails entirely
                  _appConfig = new AppConfiguration {
                      AnalysisThresholds = new AnalysisThresholds(),
                      NetworkSettings = new NetworkSettings()
                  };
             }
        }


        [SupportedOSPlatform("windows")]
        // Added dnsTestTarget parameter
        private static async Task<DiagnosticReport> RunDiagnostics(List<string> sections, string? tracerouteTarget, string? dnsTestTarget, CancellationToken cancellationToken)
        {
            var report = new DiagnosticReport { RanAsAdmin = _isAdmin };
            report.Configuration = _appConfig; // Store loaded config in report

            var sectionsToRun = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool runAll = !sections.Any() || sections.Any(s => s.Equals("all", StringComparison.OrdinalIgnoreCase)); // Corrected 'Contains'

            if (runAll) { sectionsToRun.UnionWith(new[] { "system", "hardware", "software", "security", "performance", "network", "events", "analysis" }); }
            else
            {
                sectionsToRun.UnionWith(sections);
                // If analysis is explicitly requested, ensure dependent sections are run
                // Corrected 'Contains' to 'Any' with case-insensitive check
                if (sectionsToRun.Contains("analysis") && !sections.Any(s => s.Equals("all", StringComparison.OrdinalIgnoreCase)))
                {
                    bool needsDependencies = !sectionsToRun.Contains("system") || !sectionsToRun.Contains("hardware") ||
                                            !sectionsToRun.Contains("software") || !sectionsToRun.Contains("security") ||
                                            !sectionsToRun.Contains("performance") || !sectionsToRun.Contains("network") ||
                                            !sectionsToRun.Contains("events");

                    if (needsDependencies)
                    {
                        if (!_quietMode) StatusUpdate("[INFO] Analysis requires data from other sections. Adding default data collection sections...", color: ConsoleColor.Cyan);
                        sectionsToRun.UnionWith(new[] { "system", "hardware", "software", "security", "performance", "network", "events" });
                    }
                }
            }

             var timeoutSeconds = 0; // Get the actual timeout value used
            if (cancellationToken.CanBeCanceled)
            {
                // This is a simplified way to get the original timeout back, assuming it was passed correctly.
                // Retrieving remaining time accurately is complex.
                // Let's assume it was correctly passed via the timeoutOption default or user input.
                 // We need to retrieve the value passed to the CancellationTokenSource
                 // For now, let's just use the value from the command line options parsing (stored in the handler)
                 // A better way would be to pass timeoutSeconds into RunDiagnostics. Let's adjust:
                 // TODO: Refactor to pass timeoutSeconds into RunDiagnostics if precise display is needed.
                 // For now, display a general message or retrieve it from the parsed options if accessible here.
                 // Simplified display:
                 // StatusUpdate($"Timeout configured for collection tasks."); // Generic message
                 // OR retrieve from _appConfig? No, it's a command line arg.
                 // Let's use the value obtained in the handler (assuming it's accessible or passed down).
                 // Since it's not passed down, we'll use a placeholder or the default.
                 // We know the initial value from the handler's scope was timeoutSeconds
                 // For simplicity, let's just log the configured timeout value
                 // This line caused the previous error, correcting it:
                 var timeoutOptionValue = 120; // Default if cannot retrieve easily
                 try {
                      // This is context-dependent and might not work easily here.
                      // Best approach is to pass timeoutSeconds into RunDiagnostics
                      // var invocationContext = Command.GetInvocationContext(args); // Pseudo-code
                      // timeoutSeconds = invocationContext.ParseResult.GetValueForOption(timeoutOption);
                 } catch {} // Ignore if retrieval fails
                 // StatusUpdate($"Timeout set to: {timeoutSeconds} seconds"); // Simplified timeout display
            }


            if (!_quietMode)
            {
                Console.OutputEncoding = Encoding.UTF8; // Ensure proper character display
                Console.WriteLine("========================================");
                Console.WriteLine("   Advanced Windows Diagnostic Tool");
                Console.WriteLine("========================================");
                Console.WriteLine($"Report generated: {DateTime.Now} (Local Time) / {report.ReportTimestamp:u} (UTC)");
                Console.WriteLine($"Running as Administrator: {_isAdmin}");
                if (!_isAdmin) { StatusUpdate("[WARNING] Not running with Administrator privileges. Some data may be incomplete.", color: ConsoleColor.Yellow); }
                StatusUpdate($"Sections to run: {(runAll ? "All" : string.Join(", ", sectionsToRun))}");
                if (!string.IsNullOrEmpty(tracerouteTarget)) StatusUpdate($"Traceroute requested for: {tracerouteTarget}");
                if (!string.IsNullOrEmpty(dnsTestTarget)) StatusUpdate($"DNS resolution test requested for: {dnsTestTarget}");
                // Removed the complex/error-prone timeout display line here, moved simplified version above.
                Console.WriteLine(Separator); StatusUpdate("Gathering data...");
            }

            var tasks = new List<Task>();
            var sectionStopwatch = Stopwatch.StartNew();

            // --- Run Selected Collectors with progress updates and cancellation ---
            // Helper lambda to run a collector task safely with cancellation and status updates
            Func<string, Func<Task<DiagnosticSection?>>, Task> runSection = async (name, collectorFunc) =>
            {
                 if (cancellationToken.IsCancellationRequested)
                 {
                     StatusUpdate($" -> {name} skipped due to cancellation.", indent: 4, color: ConsoleColor.Yellow);
                     // Optionally assign a default object with an error message to the report section
                     AssignErrorToReportSection(report, name, $"Collection skipped due to timeout/cancellation.");
                     return;
                 }

                 StatusUpdate($" Collecting {name}...", indent: 2);
                 var sw = Stopwatch.StartNew();
                 DiagnosticSection? result = null;
                 try
                 {
                     // Execute the collector function within the cancellation token's scope
                     // Use Task.Run to ensure even synchronous collectors can be cancelled if they take too long.
                     // Note: The collector itself MUST check the token periodically for cooperative cancellation.
                     // If it doesn't, Task.Run might only cancel after the sync work is done.
                     result = await Task.Run(collectorFunc, cancellationToken);
                     sw.Stop();
                     StatusUpdate($" -> {name} completed in {sw.ElapsedMilliseconds} ms.", indent: 4, color: ConsoleColor.DarkGray);
                     AssignResultToReportSection(report, name, result);
                 }
                 catch (OperationCanceledException) // Catches TaskCanceledException as well
                 {
                      sw.Stop();
                      StatusUpdate($" -> {name} cancelled or timed out after {sw.ElapsedMilliseconds} ms.", indent: 4, color: ConsoleColor.Yellow);
                      AssignErrorToReportSection(report, name, $"Task cancelled or timed out after {sw.ElapsedMilliseconds} ms.");
                 }
                 catch (Exception ex)
                 {
                      sw.Stop();
                      StatusUpdate($" -> {name} FAILED after {sw.ElapsedMilliseconds} ms: {ex.Message}", indent: 4, color: ConsoleColor.Red);
                      AssignErrorToReportSection(report, name, $"Critical failure during {name} collection: {ex.Message}");
                      Console.Error.WriteLine($"[Collector Error - {name}]: {ex}"); // Log full exception for debugging
                 }
            };

            // --- Schedule collector tasks ---
            // Wrap collector calls within the runSection helper
            if (sectionsToRun.Contains("system")) tasks.Add(runSection("System", () => SystemInfoCollector.CollectAsync(_isAdmin).ContinueWith(t => (DiagnosticSection?)t.Result, TaskContinuationOptions.OnlyOnRanToCompletion)));
            if (sectionsToRun.Contains("hardware")) tasks.Add(runSection("Hardware", () => HardwareInfoCollector.CollectAsync(_isAdmin).ContinueWith(t => (DiagnosticSection?)t.Result, TaskContinuationOptions.OnlyOnRanToCompletion)));
            if (sectionsToRun.Contains("software")) tasks.Add(runSection("Software", () => SoftwareInfoCollector.CollectAsync().ContinueWith(t => (DiagnosticSection?)t.Result, TaskContinuationOptions.OnlyOnRanToCompletion)));
            if (sectionsToRun.Contains("security")) tasks.Add(runSection("Security", () => SecurityInfoCollector.CollectAsync(_isAdmin).ContinueWith(t => (DiagnosticSection?)t.Result, TaskContinuationOptions.OnlyOnRanToCompletion)));
            if (sectionsToRun.Contains("performance")) tasks.Add(runSection("Performance", () => PerformanceInfoCollector.CollectAsync().ContinueWith(t => (DiagnosticSection?)t.Result, TaskContinuationOptions.OnlyOnRanToCompletion))); // Assumes Perf collector respects tokens internally or finishes quickly
            // Pass DNS test target to Network Collector
            if (sectionsToRun.Contains("network")) tasks.Add(runSection("Network", () => NetworkInfoCollector.CollectAsync(tracerouteTarget, dnsTestTarget).ContinueWith(t => (DiagnosticSection?)t.Result, TaskContinuationOptions.OnlyOnRanToCompletion))); // Assumes Network collector respects tokens internally or finishes quickly
            if (sectionsToRun.Contains("events")) tasks.Add(runSection("Events", () => EventLogCollector.CollectAsync().ContinueWith(t => (DiagnosticSection?)t.Result, TaskContinuationOptions.OnlyOnRanToCompletion)));

            // --- Wait for collection tasks to complete ---
            await Task.WhenAll(tasks);
            sectionStopwatch.Stop();
            StatusUpdate($"\nData collection phase finished in {sectionStopwatch.ElapsedMilliseconds / 1000.0:0.##} seconds.");


            // --- Run Analysis ---
            if (sectionsToRun.Contains("analysis"))
            {
                 if (cancellationToken.IsCancellationRequested)
                 {
                      StatusUpdate("Skipping analysis due to timeout/cancellation.", color: ConsoleColor.Yellow);
                      report.Analysis = new AnalysisSummary { SectionCollectionErrorMessage = "Analysis skipped due to timeout/cancellation." };
                 }
                 else
                 {
                      StatusUpdate("Running analysis...");
                      var analysisSw = Stopwatch.StartNew();
                      try
                      {
                           // Pass loaded configuration to analysis engine
                           report.Analysis = await AnalysisEngine.PerformAnalysisAsync(report, _isAdmin, _appConfig);
                           analysisSw.Stop();
                           StatusUpdate($" -> Analysis completed in {analysisSw.ElapsedMilliseconds} ms.", indent: 4, color: ConsoleColor.DarkGray);

                           if (!string.IsNullOrEmpty(report.Analysis?.SectionCollectionErrorMessage))
                           {
                                StatusUpdate($"[ERROR] Analysis encountered an error: {report.Analysis.SectionCollectionErrorMessage}", color: ConsoleColor.Red);
                           }
                      }
                      catch (Exception ex) // Catch unexpected analysis errors
                      {
                           analysisSw.Stop();
                           StatusUpdate($" -> Analysis FAILED after {analysisSw.ElapsedMilliseconds} ms: {ex.Message}", indent: 4, color: ConsoleColor.Red);
                           report.Analysis = new AnalysisSummary { SectionCollectionErrorMessage = $"Analysis engine critical error: {ex.Message}" };
                           Console.Error.WriteLine($"[Analysis Engine Error]: {ex}"); // Log full exception
                      }
                 }
            }
            else
            {
                StatusUpdate("Analysis section not selected, skipping.");
            }

            StatusUpdate("Diagnostic run finished.");
            return report;
        }

        // --- Helper Methods for Assigning Results/Errors ---
        private static void AssignResultToReportSection(DiagnosticReport report, string sectionName, DiagnosticSection? result)
        {
            var propInfo = typeof(DiagnosticReport).GetProperty(sectionName);
            if (propInfo != null && propInfo.CanWrite)
            {
                propInfo.SetValue(report, result);
            }
            else
            {
                 StatusUpdate($"[INTERNAL ERROR] Could not assign result to report section '{sectionName}'.", color: ConsoleColor.Magenta);
            }
        }

        private static void AssignErrorToReportSection(DiagnosticReport report, string sectionName, string errorMessage)
        {
             var propInfo = typeof(DiagnosticReport).GetProperty(sectionName);
             if (propInfo != null && propInfo.CanWrite)
             {
                  // Try to get existing section or create a new one to store the error
                  var section = propInfo.GetValue(report) as DiagnosticSection;
                  if (section == null)
                  {
                       // Need to instantiate the correct derived type (SystemInfo, HardwareInfo, etc.)
                       // This requires reflection or a factory pattern. Simple approach for now:
                       try
                       {
                           var sectionType = propInfo.PropertyType;
                           section = (DiagnosticSection?)Activator.CreateInstance(sectionType);
                           if (section != null)
                           {
                               propInfo.SetValue(report, section);
                           }
                       }
                       catch (Exception ex)
                       {
                            StatusUpdate($"[INTERNAL ERROR] Could not create instance for section '{sectionName}' to store error: {ex.Message}", color: ConsoleColor.Magenta);
                            return; // Cannot store the error if instance creation fails
                       }
                  }

                  if (section != null)
                  {
                       section.SectionCollectionErrorMessage = errorMessage;
                       // Optionally add to SpecificCollectionErrors as well
                       // section.AddSpecificError("CollectionPhase", errorMessage);
                  }
             }
             else
             {
                  StatusUpdate($"[INTERNAL ERROR] Could not find report section '{sectionName}' to assign error.", color: ConsoleColor.Magenta);
             }
        }


        // --- Output Handling Method ---
        private static async Task HandleOutput(DiagnosticReport report, FileInfo? outputFile, bool outputJson)
        {
            string outputContent;
            string reportHeaderText;

            // Prepare Header
            var header = new StringBuilder();
            header.AppendLine("========================================");
            header.AppendLine("   Advanced Windows Diagnostic Tool Report");
            header.AppendLine("========================================");
            header.AppendLine($"Generated: {DateTime.Now} (Local Time) / {report.ReportTimestamp:u} (UTC)");
            header.AppendLine($"Ran as Administrator: {report.RanAsAdmin}");
            // Indicate config source? Maybe too verbose.
             header.AppendLine($"Configuration Source: {(File.Exists(Path.Combine(AppContext.BaseDirectory, ConfigFileName)) ? ConfigFileName : "Defaults")}");
            header.AppendLine(Separator + "\n");
            reportHeaderText = header.ToString();

            // Prepare Content
            if (outputJson)
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    // Keep nulls so HTML viewer doesn't break if optional properties (like Analysis) are missing
                    // Also helps distinguish between a property not collected vs. collected as null.
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, // Changed back to WhenWritingNull to avoid clutter
                    // Use converters for specific types if needed (e.g., TimeSpan)
                    Converters = { new JsonStringEnumConverter() } // Example: Output enums as strings
                };
                outputContent = JsonSerializer.Serialize(report, options);
            }
            else
            {
                string textReport = TextReportGenerator.GenerateReport(report);
                outputContent = reportHeaderText + textReport;
            }

            // --- Write User-Specified Output File (-o) ---
            if (outputFile != null)
            {
                try
                {
                    // Ensure directory exists
                    outputFile.Directory?.Create();
                    await File.WriteAllTextAsync(outputFile.FullName, outputContent, Encoding.UTF8); // Specify UTF8 encoding
                    StatusUpdate($"{Separator}\nUser-specified report saved to: {outputFile.FullName}\n{Separator}");
                }
                catch (Exception ex)
                {
                    StatusUpdate($"{Separator}\n[ERROR] Could not save user-specified report to file '{outputFile.FullName}': {ex.Message}\n{Separator}", color: ConsoleColor.Red);
                    Console.Error.WriteLine($"[File Save Error]: {ex}"); // Log full exception
                }
            }

            // --- Automatic Logging (Always Text Format) ---
            string? autoLogFilePath = null;
            try
            {
                string baseDirectory = AppContext.BaseDirectory;
                string logDirectory = Path.Combine(baseDirectory, LogDirectoryName);
                Directory.CreateDirectory(logDirectory); // Ensure log directory exists
                string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                // Sanitize hostname for filename
                string hostName = Environment.MachineName.Replace(@"\", "-").Replace("/", "-").Replace(":", "-");
                string fileName = $"{LogFilePrefix}{hostName}_{timeStamp}{LogFileExtension}";
                autoLogFilePath = Path.Combine(logDirectory, fileName);

                // Generate text report specifically for the log file if main output was JSON
                string logContent = outputJson ? reportHeaderText + TextReportGenerator.GenerateReport(report) : outputContent;

                await File.WriteAllTextAsync(autoLogFilePath, logContent, Encoding.UTF8); // Specify UTF8 encoding

                // Inform user about the automatic log unless quiet mode or it's the same as the output file
                if (!_quietMode && (outputFile == null || outputJson || !string.Equals(outputFile.FullName, autoLogFilePath, StringComparison.OrdinalIgnoreCase)))
                {
                     StatusUpdate($"Automatic text log saved to: {autoLogFilePath}");
                }
            }
            catch (Exception ex)
            {
                // Log a warning if automatic logging fails, but don't treat as critical error for exit code
                StatusUpdate($"[WARNING] Could not write automatic log file to '{autoLogFilePath ?? LogDirectoryName}': {ex.Message}", color: ConsoleColor.Yellow);
                 Console.Error.WriteLine($"[AutoLog Save Error]: {ex}"); // Log full exception
            }

            // --- Console Output (Only if not quiet and not JSON) ---
            if (!_quietMode && !outputJson)
            {
                 Console.WriteLine("\n" + Separator);
                 Console.WriteLine(" --- Report Start ---");
                 // Ensure console can display UTF8 if necessary
                 // Console.OutputEncoding = Encoding.UTF8; // Already set earlier
                 Console.WriteLine(outputContent);
                 Console.WriteLine(" --- Report End ---");
                 Console.WriteLine(Separator);
            }

            // --- Attempt to open the HTML viewer ---
            string htmlFilePath = Path.Combine(AppContext.BaseDirectory, "Display.html"); // Ensure this path is correct
            if (File.Exists(htmlFilePath) && !_quietMode && Environment.UserInteractive) // <-- Modified condition
            {
                StatusUpdate($"\nAttempting to open report viewer: {htmlFilePath}");
                try
                {
                    // Use Process.Start with UseShellExecute = true to open the default browser
                    Process.Start(new ProcessStartInfo(htmlFilePath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    StatusUpdate($"[WARNING] Could not automatically open '{htmlFilePath}': {ex.Message}", color: ConsoleColor.Yellow);
                    // Don't treat this as a critical failure
                }
            }

            // --- Keep console open logic (Keep as is) ---
            if (!_quietMode && !outputJson && outputFile == null && !Console.IsInputRedirected && Environment.UserInteractive)
            {
                Console.WriteLine("\nPress Enter to exit...");
                Console.ReadLine();
            }

            // --- Keep console open if interactive (and no explicit output file specified) ---
             if (!_quietMode && !outputJson && outputFile == null && !Console.IsInputRedirected && Environment.UserInteractive)
            {
                Console.WriteLine("\nPress Enter to exit...");
                Console.ReadLine();
            }
        }

        // Helper for status updates respecting quiet mode
        private static void StatusUpdate(string message, int indent = 0, ConsoleColor? color = null)
        {
             if (_quietMode) return;

             ConsoleColor originalColor = Console.ForegroundColor;
             if(color.HasValue) Console.ForegroundColor = color.Value;

             try
             {
                 // Use WriteLine for thread safety compared to multiple Console.Write calls
                 Console.WriteLine($"{new string(' ', indent)}{message}");
             }
             catch (IOException) { /* Ignore console writing errors */ }
             catch (InvalidOperationException) { /* Ignore console errors if redirected and closed */ }
             finally
             {
                if(color.HasValue) Console.ForegroundColor = originalColor; // Reset color
             }
        }
    }
}