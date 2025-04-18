// Program.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations; // Required for ValidationContext/Validator
using System.CommandLine; // Command line parsing
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Diagnostics; // For Stopwatch
using System.IO;      // For Path, File
using System.Linq;    // For Any()
using System.Runtime.Versioning; // Required for [SupportedOSPlatform]
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization; // Required for JsonIgnoreCondition
using System.Text.RegularExpressions;
using System.Threading;          // For CancellationToken
using System.Threading.Tasks;
using DiagnosticToolAllInOne.Analysis;       // Namespace for AnalysisEngine
using DiagnosticToolAllInOne.Collectors;    // Namespace for Collectors
using DiagnosticToolAllInOne.Helpers;       // Namespace for Helpers (references the external Logger.cs)
using DiagnosticToolAllInOne.Reporting;     // Namespace for TextReportGenerator
using Microsoft.Extensions.Configuration; // Required for configuration


namespace DiagnosticToolAllInOne
{
    public class Program
    {
        private static bool _isAdmin = false;
        private static bool _quietMode = false;
        private const string Separator = "----------------------------------------";
        private const string ReportsDirectoryName = "Reports"; // Changed constant name
        private const string LogFilePrefix = "WinDiagReport_";
        //private const string LogFileExtension = ".log"; // Text log uses .txt now based on format
        private static AppConfiguration? _appConfig = null; // Holds loaded config
        private const string ConfigFileName = "web/appsettings.json";

        // --- Constants for Section Names (Match property names in DiagnosticReport) ---
        private const string SystemSection = "System";
        private const string HardwareSection = "Hardware";
        private const string SoftwareSection = "Software";
        private const string SecuritySection = "Security";
        private const string PerformanceSection = "Performance";
        private const string NetworkSection = "Network";
        private const string EventsSection = "Events";
        private const string StabilitySection = "Stability";
        private const string AnalysisSection = "Analysis";
        private const string AllSections = "all"; // Special keyword

        // Array of valid section names for validation and completions
        private static readonly string[] ValidSections = {
            SystemSection, HardwareSection, SoftwareSection, SecuritySection,
            PerformanceSection, NetworkSection, EventsSection, StabilitySection,
            AnalysisSection, AllSections
        };


        [SupportedOSPlatform("windows")]
        static async Task<int> Main(string[] args)
        {
            _isAdmin = AdminHelper.IsRunningAsAdmin();
            LoadConfiguration(); // Load and validate config early

            // --- System.CommandLine Setup ---
            var rootCommand = new RootCommand("Advanced Windows Diagnostic Tool (All-in-One) - Modular");

            // Options (Using constants for section names in description/completions)
            var sectionsOption = new Option<List<string>>(
                aliases: new[] { "--sections", "-s" },
                description: $"Specify sections (comma-separated or multiple options): {string.Join(", ", ValidSections)}. Default: {AllSections}.",
                parseArgument: result => result.Tokens.Select(t => t.Value).ToList()
            ) { AllowMultipleArgumentsPerToken = true, Arity = ArgumentArity.ZeroOrMore };
            sectionsOption.AddCompletions(ValidSections); // Use the array for completions

            var outputOption = new Option<FileInfo?>(
                aliases: new[] { "--output", "-o" },
                description: "Output report to specified file path (e.g., C:\\Reports\\MyReport.json). Directory will be created if it doesn't exist.");

            var formatOption = new Option<string>(
                name: "--format",
                description: "Output report format.",
                getDefaultValue: () => "json"
            );
            formatOption.AddCompletions("text", "json", "markdown"); // Added markdown completion

            var jsonOption = new Option<bool>( // Kept for backward compatibility/convenience
                name: "--json",
                description: "Output report in JSON format (equivalent to --format json, implies --quiet).");

            var quietOption = new Option<bool>(
                aliases: new[] { "--quiet", "-q" },
                description: "Suppress console status messages (useful mainly with --output). JSON output is always quiet.");

            var tracerouteOption = new Option<string?>(
                 name: "--traceroute",
                 description: "Perform a traceroute to the specified host or IP address.");

            // Use loaded config for default DNS test target description
            var dnsTestOption = new Option<string?>(
                 name: "--dns-test",
                 description: $"Perform a DNS resolution test for the specified hostname. Default: {_appConfig?.NetworkSettings?.DefaultDnsTestHostname ?? "www.google.com"}",
                 getDefaultValue: () => _appConfig?.NetworkSettings?.DefaultDnsTestHostname); // Get default from loaded config

            var timeoutOption = new Option<int>(
                 name: "--timeout",
                 description: "Global timeout in seconds for the entire collection process (10-600).",
                 getDefaultValue: () => 120); // Default timeout

            var debugLogOption = new Option<bool>(
                name: "--debug-log",
                description: "Enable detailed internal debug logging to WinDiagInternal.log in the application directory.");


            rootCommand.AddOption(sectionsOption);
            rootCommand.AddOption(outputOption);
            rootCommand.AddOption(formatOption);
            rootCommand.AddOption(jsonOption);
            rootCommand.AddOption(quietOption);
            rootCommand.AddOption(tracerouteOption);
            rootCommand.AddOption(dnsTestOption);
            rootCommand.AddOption(timeoutOption);
            rootCommand.AddOption(debugLogOption);

            rootCommand.SetHandler(async (InvocationContext context) =>
            {
                // *** ADDED: Top-level try-catch for handler setup/execution ***
                try
                {
                    var parseResult = context.ParseResult;
                    var sectionsRaw = parseResult.GetValueForOption(sectionsOption) ?? new List<string>();
                    var outputFile = parseResult.GetValueForOption(outputOption);
                    var outputFormat = parseResult.GetValueForOption(formatOption)?.ToLowerInvariant() ?? "json"; // Normalize format
                    var outputJsonExplicit = parseResult.GetValueForOption(jsonOption);
                    _quietMode = parseResult.GetValueForOption(quietOption);
                    var tracerouteTarget = parseResult.GetValueForOption(tracerouteOption);
                    // Default for dnsTestTarget is handled by the Option definition itself now
                    var dnsTestTarget = parseResult.GetValueForOption(dnsTestOption);
                    var timeoutSeconds = parseResult.GetValueForOption(timeoutOption);
                    var enableDebugLog = parseResult.GetValueForOption(debugLogOption);

                    // --- Setup Logger ---
                    Logger.IsDebugEnabled = enableDebugLog; // Set debug level
                    Logger.LogInfo("Application starting.");
                    // Log raw args for debugging potential parsing issues
                    Logger.LogDebug($"Raw Arguments: {string.Join(" ", Environment.GetCommandLineArgs())}");
                    Logger.LogDebug($"Admin: {_isAdmin}, Quiet: {_quietMode}, DebugLog: {enableDebugLog}, Timeout: {timeoutSeconds}s");
                    Logger.LogDebug($"Trace Target: {tracerouteTarget ?? "None"}, DNS Test Target: {dnsTestTarget ?? "None"}");
                    Logger.LogDebug($"Requested Sections Raw: {string.Join(", ", sectionsRaw)}");
                    Logger.LogDebug($"Requested Output File: {outputFile?.FullName ?? "Default"}");
                    Logger.LogDebug($"Requested Format: {outputFormat}");


                    // --- Determine Final Output Format & Quiet Mode ---
                    if (outputJsonExplicit) outputFormat = "json";
                    _quietMode = _quietMode || outputFormat == "json"; // JSON output is always quiet


                    // --- Validate Sections ---
                    var sectionsToRun = ValidateAndNormalizeSections(sectionsRaw);
                    if (sectionsToRun == null) // Null indicates invalid section provided
                    {
                        StatusUpdate($"[ERROR] Invalid section specified. Valid sections are: {string.Join(", ", ValidSections)}", color: ConsoleColor.Red);
                        Logger.LogError($"Invalid section specified by user. Valid: {string.Join(", ", ValidSections)}");
                        context.ExitCode = 98; // Invalid argument exit code
                        return;
                    }
                    Logger.LogDebug($"Normalized Sections to Run: {string.Join(", ", sectionsToRun)}");


                    // --- Default Output File Logic (Improved Error Handling) ---
                    if (outputFile == null)
                    {
                        try
                        {
                            outputFile = GenerateDefaultOutputPath(outputFormat);
                            if (!_quietMode && outputFile != null)
                            {
                                StatusUpdate($"No output file specified. Defaulting to {outputFormat.ToUpper()} output at: {outputFile.FullName}", color: ConsoleColor.Cyan);
                            }
                        }
                        catch (Exception ex)
                        {
                            StatusUpdate($"[WARNING] Could not generate default output path: {ex.Message}. Output to file disabled.", color: ConsoleColor.Yellow);
                            Logger.LogWarning($"Could not generate default output path: {ex.Message}", ex);
                            outputFile = null; // Ensure outputFile is null if path generation fails
                        }
                    }


                    // --- Validate Timeout ---
                    timeoutSeconds = Math.Clamp(timeoutSeconds, 10, 600); // Clamp timeout


                    // --- Set up cancellation token ---
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                    var cancellationToken = cts.Token;
                    // Register a callback to log when cancellation occurs
                    cancellationToken.Register(() => Logger.LogWarning($"Operation cancelled due to timeout ({timeoutSeconds}s)."));


                    // --- Run Diagnostics ---
                    var stopwatch = Stopwatch.StartNew();
                    var report = await RunDiagnostics(sectionsToRun, tracerouteTarget, dnsTestTarget, cancellationToken);
                    stopwatch.Stop();


                    if (!_quietMode)
                    {
                        Console.WriteLine($"{Separator}\nTotal execution time: {stopwatch.ElapsedMilliseconds / 1000.0:0.##} seconds.");
                        if (cancellationToken.IsCancellationRequested)
                        {
                            StatusUpdate($"[WARNING] Operation timed out after {timeoutSeconds} seconds. Results may be incomplete.", color: ConsoleColor.Yellow);
                        }
                    } else {
                        Logger.LogInfo($"Total execution time: {stopwatch.ElapsedMilliseconds} ms.");
                    }


                    // --- Handle Output ---
                    await HandleOutput(report, outputFile, outputFormat);


                    // --- Determine Exit Code ---
                    context.ExitCode = DetermineExitCode(report, cancellationToken.IsCancellationRequested);
                    Logger.LogInfo($"Application finished with Exit Code: {context.ExitCode}.");

                }
                // *** Catch for top-level handler setup/execution errors ***
                catch (Exception ex)
                {
                    StatusUpdate($"[CRITICAL SETUP ERROR] An unexpected error occurred: {ex.Message}", color: ConsoleColor.Red);
                    // Log the full error details
                    Logger.LogError("[CRITICAL] Unhandled exception in Main SetHandler", ex);
                    context.ExitCode = 99; // Indicate a setup/parse error
                }
            }); // End of SetHandler


            // --- Execute the command ---
            try
            {
                // Invoke the command parser and handler
                return await rootCommand.InvokeAsync(args);
            }
            catch (Exception ex) // Catch errors during command line parsing/invocation itself
            {
                 StatusUpdate($"[CRITICAL PARSE ERROR] Failed to parse command line arguments: {ex.Message}", color: ConsoleColor.Red);
                 Logger.LogError("[CRITICAL] Error invoking command", ex);
                 return 97; // Command line parse error exit code
            }

        } // End of Main


        // --- Enhanced Configuration Validation Method ---
        private static bool ValidateConfiguration(AppConfiguration config)
        {
            var results = new List<ValidationResult>();
            var context = new ValidationContext(config, serviceProvider: null, items: null);

            // Use recursive validation provided by DataAnnotations
            bool isValid = Validator.TryValidateObject(config, context, results, validateAllProperties: true);

            // Log validation results if invalid
            if (!isValid)
            {
                Logger.LogWarning($"Configuration validation failed. Issues: {results.Count}");
                StatusUpdate("[CONFIG WARN] Issues found in configuration:", color: ConsoleColor.Yellow);
                foreach (var validationResult in results)
                {
                    string memberNames = validationResult.MemberNames.Any() ? $" [{string.Join(", ", validationResult.MemberNames)}]" : "";
                    StatusUpdate($" -{memberNames} {validationResult.ErrorMessage}", indent: 2, color: ConsoleColor.Yellow);
                    Logger.LogWarning($"Configuration Validation Error:{memberNames} {validationResult.ErrorMessage}");
                }
            } else {
                Logger.LogInfo("Configuration validated successfully.");
            }
            return isValid;
        }


        // --- Configuration Loading Method ---
        private static void LoadConfiguration()
        {
            try
            {
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    // Optional: true means it won't throw if file is missing
                    .AddJsonFile(ConfigFileName, optional: true, reloadOnChange: false);

                IConfigurationRoot configuration = configBuilder.Build();

                // Bind the "AppConfiguration" section. Returns null if section doesn't exist.
                _appConfig = configuration.GetSection(nameof(AppConfiguration)).Get<AppConfiguration>();

                // Ensure defaults if config file is missing or section is empty/malformed
                if (_appConfig == null) {
                    Logger.LogInfo($"'{ConfigFileName}' not found or '{nameof(AppConfiguration)}' section missing/empty. Using default settings.");
                    _appConfig = new AppConfiguration(); // Create default parent
                }

                // Ensure child objects have defaults if they are null after binding
                _appConfig.AnalysisThresholds ??= new AnalysisThresholds();
                _appConfig.NetworkSettings ??= new NetworkSettings();


                // Validate the loaded or default configuration
                if (!ValidateConfiguration(_appConfig))
                {
                    StatusUpdate($"[WARNING] Configuration validation failed. Using default or potentially invalid values. Review '{ConfigFileName}' or application defaults.", color: ConsoleColor.Yellow);
                    // Keep the potentially invalid _appConfig, user was warned. Analysis Engine should handle nulls.
                } else {
                    Logger.LogInfo($"Configuration loaded successfully from '{ConfigFileName}' or defaults applied.");
                }

            }
            catch (Exception ex)
            {
                // Log critical errors during file reading/parsing
                Logger.LogError($"[CRITICAL] Error loading configuration from '{ConfigFileName}'", ex);
                StatusUpdate($"[CRITICAL ERROR] Could not load or parse '{ConfigFileName}'. Using default settings. Error: {ex.Message}", color: ConsoleColor.Red);
                // Use default settings on critical failure
                _appConfig = new AppConfiguration {
                    AnalysisThresholds = new AnalysisThresholds(),
                    NetworkSettings = new NetworkSettings()
                };
                ValidateConfiguration(_appConfig); // Validate the defaults
            }
        }


        // --- Section Validation and Normalization ---
        private static HashSet<string>? ValidateAndNormalizeSections(List<string> requestedSections)
        {
            var normalizedSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Use correct casing later
            bool runAll = !requestedSections.Any() || requestedSections.Contains(AllSections, StringComparer.OrdinalIgnoreCase);

            if (runAll)
            {
                 // Add all valid sections EXCEPT 'all' itself
                 normalizedSections.UnionWith(ValidSections.Where(s => !s.Equals(AllSections, StringComparison.OrdinalIgnoreCase)));
                 return normalizedSections;
            }

            // Validate user-provided sections
            foreach (var reqSection in requestedSections)
            {
                 // Find the correctly cased valid section name
                 string? matchedSection = ValidSections.FirstOrDefault(valid => valid.Equals(reqSection, StringComparison.OrdinalIgnoreCase));
                 if (matchedSection != null && matchedSection != AllSections)
                 {
                      normalizedSections.Add(matchedSection); // Add the correctly cased name
                 }
                 else if (matchedSection == AllSections) {
                     // If 'all' is included with others, ignore others and run all
                      Logger.LogInfo("Section 'all' specified with others; running all sections.");
                      normalizedSections.Clear();
                      normalizedSections.UnionWith(ValidSections.Where(s => !s.Equals(AllSections, StringComparison.OrdinalIgnoreCase)));
                      return normalizedSections; // Return immediately after setting all
                 }
                 else
                 {
                      Logger.LogError($"Invalid section specified: '{reqSection}'");
                      return null; // Indicate validation failure
                 }
            }


            // If analysis is specifically requested, ensure its dependencies are included
            if (normalizedSections.Contains(AnalysisSection))
            {
                var dependencies = new[] {
                    SystemSection, HardwareSection, SoftwareSection, SecuritySection,
                    PerformanceSection, NetworkSection, EventsSection, StabilitySection
                };
                int addedCount = 0;
                foreach(var dep in dependencies) {
                    if (normalizedSections.Add(dep)) { // Add returns true if item was added
                        addedCount++;
                    }
                }
                if (addedCount > 0 && !_quietMode)
                {
                    StatusUpdate($"[INFO] Analysis requires data from other sections. Automatically added {addedCount} required section(s).", color: ConsoleColor.Cyan);
                     Logger.LogInfo($"Added {addedCount} dependencies for Analysis section.");
                }
            }

            return normalizedSections;
        }


         // --- Generate Default Output Path ---
         private static FileInfo GenerateDefaultOutputPath(string format) {
             string baseDirectory = AppContext.BaseDirectory;
             // Use constant for directory name
             string outputDirectory = Path.Combine(baseDirectory, ReportsDirectoryName);
             Directory.CreateDirectory(outputDirectory); // Creates if not exists
             string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
             // Sanitize hostname: replace non-alphanumeric/dot/hyphen with underscore
             string hostName = Regex.Replace(Environment.MachineName, @"[^\w\.-]", "_");
             // Determine extension based on format
             string fileExtension = format.ToLowerInvariant() switch {
                 "json" => ".json",
                 "markdown" => ".md",
                 "text" => ".txt", // Keep text as .txt
                 _ => ".txt" // Default to text
             };
             string defaultFileName = $"DiagReport_{hostName}_{timeStamp}{fileExtension}";
             return new FileInfo(Path.Combine(outputDirectory, defaultFileName));
         }


        // --- Main Diagnostic Runner ---
        [SupportedOSPlatform("windows")]
        private static async Task<DiagnosticReport> RunDiagnostics(HashSet<string> sectionsToRun, string? tracerouteTarget, string? dnsTestTarget, CancellationToken cancellationToken)
        {
            var report = new DiagnosticReport { RanAsAdmin = _isAdmin, Configuration = _appConfig }; // Include config used
            var tasks = new List<Task>();
            var sectionStopwatch = Stopwatch.StartNew();


            // --- Console Output Setup ---
            if (!_quietMode)
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.WriteLine("========================================");
                Console.WriteLine("   Advanced Windows Diagnostic Tool");
                Console.WriteLine("========================================");
                Console.WriteLine($"Report generated: {DateTime.Now} (Local Time) / {report.ReportTimestamp:u} (UTC)");
                Console.WriteLine($"Running as Administrator: {_isAdmin}");
                if (!_isAdmin) { StatusUpdate("[WARNING] Not running with Administrator privileges. Some data may be incomplete.", color: ConsoleColor.Yellow); }
                StatusUpdate($"Sections to run: {string.Join(", ", sectionsToRun)}");
                if (!string.IsNullOrEmpty(tracerouteTarget)) StatusUpdate($"Traceroute Target: {tracerouteTarget}");
                if (!string.IsNullOrEmpty(dnsTestTarget)) StatusUpdate($"DNS Test Target: {dnsTestTarget}");
                Console.WriteLine(Separator);
                StatusUpdate("Gathering data...");
            }
            Logger.LogInfo($"Starting data collection for sections: {string.Join(", ", sectionsToRun)}");


            // --- Run Selected Collectors with progress updates and cancellation ---
            // Helper function to run and time each collector task safely
            Func<string, Func<Task<DiagnosticSection?>>, Task> runSection = async (name, collectorFunc) =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    StatusUpdate($" -> {name} skipped due to cancellation.", indent: 4, color: ConsoleColor.Yellow);
                    AssignErrorToReportSection(report, name, $"Collection skipped due to timeout/cancellation.");
                    Logger.LogWarning($"{name} collection skipped due to cancellation.");
                    return;
                }

                StatusUpdate($" Collecting {name}...", indent: 2);
                var sw = Stopwatch.StartNew();
                DiagnosticSection? result = null;
                try
                {
                    // Execute the collector function within Task.Run to ensure it's on the thread pool
                    // Use await directly on the Task returned by the collector
                    result = await Task.Run(collectorFunc, cancellationToken); // Pass CancellationToken here
                    sw.Stop();
                    StatusUpdate($" -> {name} completed in {sw.ElapsedMilliseconds} ms.", indent: 4, color: ConsoleColor.DarkGray);
                    AssignResultToReportSection(report, name, result);
                    Logger.LogDebug($"{name} collection completed in {sw.ElapsedMilliseconds} ms. Result is null: {result == null}");

                    // Log specific errors found within the section data
                    if (result?.SpecificCollectionErrors?.Any() ?? false)
                    {
                          Logger.LogWarning($"{name} completed with {result.SpecificCollectionErrors.Count} specific warnings/errors.");
                          foreach(var kvp in result.SpecificCollectionErrors)
                          {
                              Logger.LogWarning($"   - {name} Error [{kvp.Key}]: {kvp.Value}");
                          }
                    }
                    // Log section-level critical errors
                    if (!string.IsNullOrEmpty(result?.SectionCollectionErrorMessage))
                    {
                         Logger.LogError($"{name} collection failed with critical error: {result.SectionCollectionErrorMessage}");
                     }
                }
                catch (OperationCanceledException) // Catch cancellation specifically
                {
                    sw.Stop(); // Stop stopwatch on cancellation
                    StatusUpdate($" -> {name} cancelled or timed out after {sw.ElapsedMilliseconds} ms.", indent: 4, color: ConsoleColor.Yellow);
                    AssignErrorToReportSection(report, name, $"Task cancelled or timed out after {sw.ElapsedMilliseconds} ms.");
                    // Log warning already done by cancellation token registration
                }
                catch (Exception ex) // Catch any other unexpected exceptions during collection
                {
                    sw.Stop();
                    StatusUpdate($" -> {name} FAILED after {sw.ElapsedMilliseconds} ms: {ex.Message}", indent: 4, color: ConsoleColor.Red);
                    AssignErrorToReportSection(report, name, $"Critical failure during {name} collection: {ex.Message}");
                    Logger.LogError($"Collector Error - {name} failed after {sw.ElapsedMilliseconds} ms", ex);
                }
            };


            // --- Schedule collector tasks using the helper ---
            // Pass _isAdmin explicitly where needed
            // Use the constants for section names

            // Example for SystemSection:
            if (sectionsToRun.Contains(SystemSection))
                tasks.Add(runSection(SystemSection, async () => await SystemInfoCollector.CollectAsync(_isAdmin))); // Implicit cast to DiagnosticSection? works

            // Apply the same pattern to all other sections:
            if (sectionsToRun.Contains(HardwareSection))
                tasks.Add(runSection(HardwareSection, async () => await HardwareInfoCollector.CollectAsync(_isAdmin)));

            if (sectionsToRun.Contains(SoftwareSection))
                tasks.Add(runSection(SoftwareSection, async () => await SoftwareInfoCollector.CollectAsync()));

            if (sectionsToRun.Contains(SecuritySection))
                tasks.Add(runSection(SecuritySection, async () => await SecurityInfoCollector.CollectAsync(_isAdmin)));

            if (sectionsToRun.Contains(PerformanceSection))
                tasks.Add(runSection(PerformanceSection, async () => await PerformanceInfoCollector.CollectAsync()));

            if (sectionsToRun.Contains(NetworkSection))
                tasks.Add(runSection(NetworkSection, async () => await NetworkInfoCollector.CollectAsync(tracerouteTarget, dnsTestTarget)));

            if (sectionsToRun.Contains(EventsSection))
                tasks.Add(runSection(EventsSection, async () => await EventLogCollector.CollectAsync()));

            if (sectionsToRun.Contains(StabilitySection))
                tasks.Add(runSection(StabilitySection, async () => await StabilityInfoCollector.CollectAsync()));
                

            // --- Wait for collection tasks to complete ---
            try { await Task.WhenAll(tasks); }
            catch (Exception ex) // Should not happen if runSection handles errors, but catch just in case
            { Logger.LogError("Unexpected error during Task.WhenAll for collectors", ex); }

            sectionStopwatch.Stop();
            StatusUpdate($"\nData collection phase finished in {sectionStopwatch.ElapsedMilliseconds / 1000.0:0.##} seconds.");
            Logger.LogInfo($"Data collection phase finished in {sectionStopwatch.ElapsedMilliseconds} ms.");


            // --- Run Analysis ---
            if (sectionsToRun.Contains(AnalysisSection))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    StatusUpdate("Skipping analysis due to timeout/cancellation.", color: ConsoleColor.Yellow);
                    report.Analysis = new AnalysisSummary { SectionCollectionErrorMessage = "Analysis skipped due to timeout/cancellation." };
                    Logger.LogWarning("Analysis skipped due to timeout/cancellation.");
                }
                else if (ReportHasCriticalErrors(report)) // Check if core data needed for analysis is missing
                {
                    StatusUpdate("Skipping analysis due to critical errors in required data sections.", color: ConsoleColor.Yellow);
                    report.Analysis = new AnalysisSummary { SectionCollectionErrorMessage = "Analysis skipped due to critical errors in prerequisite data sections." };
                    Logger.LogWarning("Analysis skipped due to critical errors in required data sections.");
                }
                else
                {
                    StatusUpdate("Running analysis...");
                    Logger.LogInfo("Starting analysis phase.");
                    var analysisSw = Stopwatch.StartNew();
                    try
                    {
                         // Pass the already loaded config from the report object
                        report.Analysis = await AnalysisEngine.PerformAnalysisAsync(report, _isAdmin, report.Configuration);
                        analysisSw.Stop();
                        StatusUpdate($" -> Analysis completed in {analysisSw.ElapsedMilliseconds} ms.", indent: 4, color: ConsoleColor.DarkGray);
                        Logger.LogInfo($"Analysis completed in {analysisSw.ElapsedMilliseconds} ms.");

                         // Log analysis engine errors if any
                        if (!string.IsNullOrEmpty(report.Analysis?.SectionCollectionErrorMessage))
                        {
                            StatusUpdate($"[ERROR] Analysis encountered an error: {report.Analysis.SectionCollectionErrorMessage}", color: ConsoleColor.Red);
                            Logger.LogError($"Analysis engine error: {report.Analysis.SectionCollectionErrorMessage}");
                        }
                    }
                    catch (Exception ex) // Catch errors in the analysis engine execution
                    {
                        analysisSw.Stop();
                        StatusUpdate($" -> Analysis FAILED after {analysisSw.ElapsedMilliseconds} ms: {ex.Message}", indent: 4, color: ConsoleColor.Red);
                        report.Analysis = new AnalysisSummary { SectionCollectionErrorMessage = $"Analysis engine critical error: {ex.Message}" };
                        Logger.LogError($"Analysis engine critical error after {analysisSw.ElapsedMilliseconds} ms", ex);
                    }
                }
            }
            else {
                StatusUpdate("Analysis section not selected, skipping.");
                Logger.LogInfo("Analysis section skipped.");
            }

            StatusUpdate("Diagnostic run finished.");
            return report;
        }


        // --- Helper Methods for Assigning Results/Errors ---
        private static void AssignResultToReportSection(DiagnosticReport report, string sectionName, DiagnosticSection? result)
        {
            if (result == null) {
                 Logger.LogWarning($"AssignResultToReportSection received null result for section '{sectionName}'. Assigning error message.");
                 // Assign an error indicating the collector failed to produce a result
                 AssignErrorToReportSection(report, sectionName, "Collector returned null result.");
                 return;
            }

            // Find the property in DiagnosticReport matching the sectionName (case-insensitive)
            var propInfo = typeof(DiagnosticReport).GetProperty(sectionName, System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            // Check if property exists, is writable, and the result type is assignable to the property type
            if (propInfo != null && propInfo.CanWrite && propInfo.PropertyType.IsAssignableFrom(result.GetType()))
            {
                propInfo.SetValue(report, result); // Assign the collected data
            }
            else
            {
                // Log detailed error if assignment fails
                string expectedType = propInfo?.PropertyType.Name ?? "N/A";
                string actualType = result.GetType().Name;
                Logger.LogError($"[INTERNAL ERROR] Could not assign result to report section '{sectionName}'. Property not found, not writable, or type mismatch (Expected: {expectedType}, Got: {actualType}).");
                StatusUpdate($"[INTERNAL ERROR] Could not assign result to report section '{sectionName}'. Check internal logs.", color: ConsoleColor.Magenta);
            }
        }

        private static void AssignErrorToReportSection(DiagnosticReport report, string sectionName, string errorMessage)
        {
            // Finds the corresponding property in DiagnosticReport
            var propInfo = typeof(DiagnosticReport).GetProperty(sectionName, System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            if (propInfo != null && propInfo.CanWrite)
            {
                // Get the existing section object from the report, or create a new one if it's null
                var section = propInfo.GetValue(report) as DiagnosticSection;

                if (section == null)
                {
                    try
                    {
                        // Attempt to create a new instance of the correct section type
                        var sectionType = propInfo.PropertyType;
                        section = (DiagnosticSection?)Activator.CreateInstance(sectionType);

                        if (section != null)
                        {
                            propInfo.SetValue(report, section); // Assign the new section back
                        }
                        else
                        {
                            Logger.LogError($"[INTERNAL ERROR] Could not create instance for section '{sectionName}' (Activator returned null). Cannot store error message.");
                            StatusUpdate($"[INTERNAL ERROR] Could not create instance for section '{sectionName}'.", color: ConsoleColor.Magenta);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"[INTERNAL ERROR] Could not create instance for section '{sectionName}' to store error: {ex.Message}", ex);
                        StatusUpdate($"[INTERNAL ERROR] Could not create instance for section '{sectionName}'.", color: ConsoleColor.Magenta);
                        return;
                    }
                }

                // Assign the error message to the SectionCollectionErrorMessage
                if (section != null)
                {
                    // Append if an error already exists? For now, overwrite.
                    section.SectionCollectionErrorMessage = errorMessage;
                }
            }
            else
            {
                Logger.LogError($"[INTERNAL ERROR] Could not find report section property '{sectionName}' or it's not writable, cannot assign error.");
                StatusUpdate($"[INTERNAL ERROR] Could not find report section '{sectionName}'.", color: ConsoleColor.Magenta);
            }
        }

        // --- Helper to Check for Critical Errors Before Analysis ---
        private static bool ReportHasCriticalErrors(DiagnosticReport report) {
             // Check if any essential section has a critical collection error message
             return !string.IsNullOrEmpty(report.System?.SectionCollectionErrorMessage) ||
                    !string.IsNullOrEmpty(report.Hardware?.SectionCollectionErrorMessage) ||
                    !string.IsNullOrEmpty(report.Software?.SectionCollectionErrorMessage) ||
                    !string.IsNullOrEmpty(report.Security?.SectionCollectionErrorMessage) ||
                    !string.IsNullOrEmpty(report.Performance?.SectionCollectionErrorMessage) ||
                    !string.IsNullOrEmpty(report.Network?.SectionCollectionErrorMessage) ||
                    !string.IsNullOrEmpty(report.Events?.SectionCollectionErrorMessage) ||
                    !string.IsNullOrEmpty(report.Stability?.SectionCollectionErrorMessage);
        }

        // --- Determine Exit Code ---
        private static int DetermineExitCode(DiagnosticReport report, bool wasCancelled) {
            if (wasCancelled) return 2; // Timeout/Cancelled

            // Check for critical errors in any section
            bool hasCriticalErrors = ValidSections
                .Where(s => s != AllSections && s != AnalysisSection) // Exclude 'all' and 'Analysis' itself
                .Select(sectionName => typeof(DiagnosticReport).GetProperty(sectionName, System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.GetValue(report) as DiagnosticSection)
                .Any(section => section != null && !string.IsNullOrEmpty(section.SectionCollectionErrorMessage));

            // Check for critical error specifically in the Analysis section
            bool analysisFailedCritically = report.Analysis != null && !string.IsNullOrEmpty(report.Analysis.SectionCollectionErrorMessage);

            if (hasCriticalErrors || analysisFailedCritically)
            {
                 if (!_quietMode && hasCriticalErrors) StatusUpdate("[CRITICAL ERRORS] One or more diagnostic sections failed critically during collection.", color: ConsoleColor.Red);
                 if (!_quietMode && analysisFailedCritically) StatusUpdate("[CRITICAL ERRORS] The analysis engine encountered a critical error.", color: ConsoleColor.Red);
                 return 1; // Critical error
            }

            // Check for specific, non-critical errors
             bool hasSpecificErrors = ValidSections
                .Where(s => s != AllSections) // Check all sections including Analysis for specific errors
                .Select(sectionName => typeof(DiagnosticReport).GetProperty(sectionName, System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.GetValue(report) as DiagnosticSection)
                .Any(section => section?.SpecificCollectionErrors?.Any() ?? false);

            if (hasSpecificErrors) {
                if (!_quietMode) StatusUpdate("[WARNING] Some non-critical errors occurred during data collection or analysis. See report/log.", color: ConsoleColor.Yellow);
                return 3; // Non-critical errors occurred
            }

            return 0; // Success
        }


        // --- Output Handling Method (Refined) ---
        private static async Task HandleOutput(DiagnosticReport report, FileInfo? outputFile, string outputFormat)
        {
            string outputContent = "";
            string? reportFilePath = outputFile?.FullName;
            Logger.LogInfo($"Handling output. Format: {outputFormat}, File: {reportFilePath ?? "None Specified"}");

            // 1. Generate Report Content
            try
            {
                if (outputFormat == "json")
                {
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        Converters = { new JsonStringEnumConverter() },
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };
                    outputContent = JsonSerializer.Serialize(report, options);
                }
                else // Handle Text and Markdown (using TextGenerator for both currently)
                {
                     if (outputFormat == "markdown") {
                         // For now, generate text report and add basic Markdown header
                         outputContent = $"# Diagnostic Report ({DateTime.Now:yyyy-MM-dd HH:mm})\n\n```\n" + TextReportGenerator.GenerateReport(report) + "\n```";
                         Logger.LogInfo("Generating basic Markdown report (wrapping text output).");
                     } else { // Default to "text"
                         outputContent = TextReportGenerator.GenerateReport(report);
                     }
                }
                Logger.LogDebug($"Report content generated successfully (Format: {outputFormat}). Length: {outputContent.Length}");
            }
            catch (Exception ex)
            {
                StatusUpdate($"[ERROR] Failed to generate report content (Format: {outputFormat}): {ex.Message}", color: ConsoleColor.Red);
                Logger.LogError($"Failed to generate report content (Format: {outputFormat})", ex);
                return; // Stop if content generation fails
            }

            // 2. Write Output File (User-specified or Default)
            bool reportFileWritten = false;
            if (reportFilePath != null)
            {
                try
                {
                    // Basic path validation
                    if (reportFilePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0) {
                        throw new ArgumentException("Output file path contains invalid characters.");
                    }
                    outputFile?.Directory?.Create(); // Ensure directory exists
                    await File.WriteAllTextAsync(reportFilePath, outputContent, Encoding.UTF8);
                    reportFileWritten = true;
                    Logger.LogInfo($"Report saved to: {reportFilePath} (Format: {outputFormat})");
                    if (!_quietMode) { StatusUpdate($"Report saved to: {reportFilePath} (Format: {outputFormat.ToUpper()})"); }
                }
                catch (Exception ex)
                {
                    StatusUpdate($"[ERROR] Could not save report to file '{reportFilePath}': {ex.Message}", color: ConsoleColor.Red);
                    Logger.LogError($"Failed to save report to '{reportFilePath}'", ex);
                    reportFilePath = null; // Don't try to open a file that failed to save
                }
            }

            // 3. Console Output (if format is text and not written to file)
            if (!_quietMode && outputFormat == "text" && !reportFileWritten)
            {
                Console.WriteLine($"\n{Separator}\n --- Report Start ---\n");
                // Write in chunks to avoid potential console buffer issues with very large reports
                const int chunkSize = 4096;
                 for (int i = 0; i < outputContent.Length; i += chunkSize)
                 {
                     Console.Write(outputContent.Substring(i, Math.Min(chunkSize, outputContent.Length - i)));
                 }
                Console.WriteLine($"\n--- Report End ---\n{Separator}");
            }

            // 4. Attempt to open HTML viewer (Only if JSON was written successfully)
            if (outputFormat == "json" && reportFileWritten && reportFilePath != null && !_quietMode && Environment.UserInteractive)
            {
                 string htmlViewerPath = Path.Combine(AppContext.BaseDirectory, "Display.html");
                 if (File.Exists(htmlViewerPath))
                 {
                     await OpenHtmlViewer(htmlViewerPath, reportFilePath);
                 } else {
                    StatusUpdate($"[INFO] HTML Viewer '{htmlViewerPath}' not found. Cannot open automatically.", color: ConsoleColor.Cyan);
                    Logger.LogInfo($"HTML viewer '{htmlViewerPath}' not found.");
                 }
            }
        }


        // --- Helper to Open HTML Viewer ---
        private static async Task OpenHtmlViewer(string htmlViewerPath, string reportFilePath) {
            try
            {
                // Construct file URIs
                var htmlFileUri = new Uri(htmlViewerPath);
                var reportFileUri = new Uri(reportFilePath);

                // Create URI with query parameter for the report path
                var uriBuilder = new UriBuilder(htmlFileUri);
                // IMPORTANT: Ensure the path passed in the query is correctly encoded
                uriBuilder.Query = $"reportPath={Uri.EscapeDataString(reportFileUri.LocalPath)}";
                Uri finalUri = uriBuilder.Uri;

                StatusUpdate($"\nAttempting to open report viewer...");
                Logger.LogInfo($"Attempting Process.Start for URI: {finalUri.AbsoluteUri}"); // Log the exact URI

                // UseShellExecute = true allows opening the default browser
                Process.Start(new ProcessStartInfo(finalUri.AbsoluteUri) { UseShellExecute = true });
                await Task.Delay(500); // Brief delay to allow browser to launch
                Logger.LogInfo($"Successfully launched browser for report viewer.");

            }
            catch (Exception ex)
            {
                StatusUpdate($"[WARNING] Could not automatically open HTML viewer: {ex.Message}", color: ConsoleColor.Yellow);
                Logger.LogWarning($"Could not open HTML viewer '{htmlViewerPath}' with report path '{reportFilePath}'", ex);
                // Provide fallback info
                StatusUpdate($"You can manually open '{Path.GetFileName(htmlViewerPath)}' and select the report file:", color: ConsoleColor.Cyan);
                StatusUpdate(reportFilePath, indent: 2, color: ConsoleColor.Cyan);
            }
        }


        // --- Helper for status updates respecting quiet mode ---
        private static void StatusUpdate(string message, int indent = 0, ConsoleColor? color = null)
        {
            if (_quietMode) return;

            ConsoleColor originalColor = Console.ForegroundColor;
            try
            {
                if(color.HasValue) Console.ForegroundColor = color.Value;
                // Use Error stream for warnings/errors for better visibility/redirection
                var stream = (color == ConsoleColor.Red || color == ConsoleColor.Yellow) ? Console.Error : Console.Out;
                stream.WriteLine($"{new string(' ', indent)}{message}");
            }
            catch (IOException) { /* Ignore console writing errors */ }
            catch (InvalidOperationException) { /* Ignore if no console available */ }
            finally
            {
                if(color.HasValue) Console.ForegroundColor = originalColor;
            }
        }

    } // End of Program class
} // End of namespace