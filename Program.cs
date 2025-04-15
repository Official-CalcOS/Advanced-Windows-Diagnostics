using System;
using System.Collections.Generic;
using System.CommandLine; // Command line parsing
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Diagnostics; // For Stopwatch
using System.IO;
using System.Linq;
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
// using Microsoft.Extensions.Configuration; // Example: uncomment if using config file loading

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
        private const string LogFileExtension = ".log";

        // Configuration placeholder (Load in Main if using config files)
        // private static AppConfiguration? _appConfig = null;

        [SupportedOSPlatform("windows")]
        static async Task<int> Main(string[] args)
        {
            _isAdmin = AdminHelper.IsRunningAsAdmin();

            // --- Placeholder: Load Configuration (Example) ---
            /*
            try
            {
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                IConfigurationRoot configuration = configBuilder.Build();
                _appConfig = configuration.Get<AppConfiguration>() ?? new AppConfiguration(); // Bind or create default
                 if(_appConfig.AnalysisThresholds == null) _appConfig.AnalysisThresholds = new AnalysisThresholds(); // Ensure defaults
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.WriteLine($"[WARNING] Could not load configuration from appsettings.json: {ex.Message}. Using default settings.");
                Console.ResetColor();
                // Ensure defaults are created if config loading fails
                 _appConfig = new AppConfiguration { AnalysisThresholds = new AnalysisThresholds() };
            }
            */

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
            rootCommand.AddOption(timeoutOption);

            rootCommand.SetHandler(async (InvocationContext context) =>
            {
                var sections = context.ParseResult.GetValueForOption(sectionsOption) ?? new List<string>();
                var outputFile = context.ParseResult.GetValueForOption(outputOption);
                var outputJson = context.ParseResult.GetValueForOption(jsonOption);
                _quietMode = context.ParseResult.GetValueForOption(quietOption) || outputJson;
                var tracerouteTarget = context.ParseResult.GetValueForOption(tracerouteOption);
                var timeoutSeconds = context.ParseResult.GetValueForOption(timeoutOption);

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                var cancellationToken = cts.Token;

                var stopwatch = Stopwatch.StartNew();
                var report = await RunDiagnostics(sections, tracerouteTarget, cancellationToken);
                stopwatch.Stop();

                if (!_quietMode)
                {
                     Console.WriteLine($"{Separator}\nTotal execution time: {stopwatch.ElapsedMilliseconds / 1000.0:0.##} seconds.");
                     if (cancellationToken.IsCancellationRequested)
                     {
                          Console.ForegroundColor = ConsoleColor.Yellow;
                          Console.WriteLine("[WARNING] Operation cancelled due to timeout.");
                          Console.ResetColor();
                     }
                }

                await HandleOutput(report, outputFile, outputJson);

                // Determine exit code based on CRITICAL errors
                bool hasCriticalErrors = !string.IsNullOrEmpty(report.System?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Hardware?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Software?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Security?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Performance?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Network?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Events?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Analysis?.SectionCollectionErrorMessage);

                context.ExitCode = hasCriticalErrors ? 1 : 0; // Exit code 1 if critical errors occurred
                if (hasCriticalErrors && !_quietMode)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine("\n[CRITICAL ERRORS] One or more diagnostic sections encountered critical errors during collection or analysis.");
                    Console.ResetColor();
                }
                 else if (cancellationToken.IsCancellationRequested)
                 {
                      context.ExitCode = 2; // Indicate timeout exit code
                 }
            });

            return await rootCommand.InvokeAsync(args);
        }

        [SupportedOSPlatform("windows")]
        private static async Task<DiagnosticReport> RunDiagnostics(List<string> sections, string? tracerouteTarget, CancellationToken cancellationToken)
        {
            var report = new DiagnosticReport { RanAsAdmin = _isAdmin };
            // report.Configuration = _appConfig; // Store loaded config in report if needed

            var sectionsToRun = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool runAll = !sections.Any() || sections.Contains("all", StringComparer.OrdinalIgnoreCase);

            if (runAll) { sectionsToRun.UnionWith(new[] { "system", "hardware", "software", "security", "performance", "network", "events", "analysis" }); }
            else
            {
                sectionsToRun.UnionWith(sections);
                if (sectionsToRun.Contains("analysis") && sectionsToRun.Count == 1 && !(sections.Contains("all", StringComparer.OrdinalIgnoreCase)))
                {
                    if (!_quietMode) StatusUpdate("Analysis requires other sections. Running default data collection...");
                    sectionsToRun.UnionWith(new[] { "system", "hardware", "software", "security", "performance", "network", "events" });
                }
            }

            if (!_quietMode)
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.WriteLine("========================================");
                Console.WriteLine("   Advanced Windows Diagnostic Tool");
                Console.WriteLine("========================================");
                Console.WriteLine($"Report generated: {DateTime.Now} (Local Time) / {report.ReportTimestamp:u} (UTC)");
                Console.WriteLine($"Running as Administrator: {_isAdmin}");
                if (!_isAdmin) { StatusUpdate("[WARNING] Not running with Administrator privileges. Some data may be incomplete.", color: ConsoleColor.Yellow); }
                StatusUpdate($"Sections to run: {(runAll ? "All" : string.Join(", ", sectionsToRun))}");
                if (!string.IsNullOrEmpty(tracerouteTarget)) StatusUpdate($"Traceroute requested for: {tracerouteTarget}");
                Console.WriteLine(Separator); StatusUpdate("Gathering data...");
            }

            var tasks = new List<Task>();
            var sectionStopwatch = Stopwatch.StartNew();

            // --- Run Selected Collectors with progress updates ---
            Func<string, Func<Task<DiagnosticSection?>>, Task> runSection = async (name, collectorFunc) =>
            {
                 if (cancellationToken.IsCancellationRequested) return; // Check before starting
                 StatusUpdate($" Collecting {name}...", indent: 2);
                 var sw = Stopwatch.StartNew();
                 try
                 {
                     var result = await collectorFunc();
                     sw.Stop();
                     StatusUpdate($" -> {name} completed in {sw.ElapsedMilliseconds} ms.", indent: 4, color: ConsoleColor.DarkGray);
                     // Assign result based on type - could be more robust with reflection or a dictionary
                     switch (name)
                     {
                         case "System": report.System = (SystemInfo?)result; break;
                         case "Hardware": report.Hardware = (HardwareInfo?)result; break;
                         case "Software": report.Software = (SoftwareInfo?)result; break;
                         case "Security": report.Security = (SecurityInfo?)result; break;
                         case "Performance": report.Performance = (PerformanceInfo?)result; break;
                         case "Network": report.Network = (NetworkInfo?)result; break;
                         case "Events": report.Events = (EventLogInfo?)result; break;
                     }
                 }
                 catch (TaskCanceledException)
                 {
                      sw.Stop(); StatusUpdate($" -> {name} cancelled due to timeout.", indent: 4, color: ConsoleColor.Yellow);
                      // Ensure the report section reflects the cancellation error
                      var section = report.GetType().GetProperty(name)?.GetValue(report) as DiagnosticSection;
                      section?.AddSpecificError("Collection", "Task cancelled due to timeout.");
                 }
                 catch (Exception ex)
                 {
                      sw.Stop();
                      // ** CS1503 FIX: Corrected argument order for StatusUpdate **
                      StatusUpdate($" -> {name} FAILED after {sw.ElapsedMilliseconds} ms: {ex.Message}", 4, ConsoleColor.Red);
                      // Attempt to record the error in the specific section if possible
                      var section = report.GetType().GetProperty(name)?.GetValue(report) as DiagnosticSection;
                      section?.AddSpecificError("Collection", $"Critical failure during {name} collection: {ex.Message}");
                 }
            };

            // Wrap collector calls within a Task.Run to respect cancellation token if collector isn't inherently async-aware
            if (sectionsToRun.Contains("system")) tasks.Add(runSection("System", async () => await Task.Run(() => SystemInfoCollector.CollectAsync(_isAdmin), cancellationToken)));
            if (sectionsToRun.Contains("hardware")) tasks.Add(runSection("Hardware", async () => await Task.Run(() => HardwareInfoCollector.CollectAsync(_isAdmin), cancellationToken)));
            if (sectionsToRun.Contains("software")) tasks.Add(runSection("Software", async () => await Task.Run(() => SoftwareInfoCollector.CollectAsync(), cancellationToken)));
            if (sectionsToRun.Contains("security")) tasks.Add(runSection("Security", async () => await Task.Run(() => SecurityInfoCollector.CollectAsync(_isAdmin), cancellationToken)));
            if (sectionsToRun.Contains("performance")) tasks.Add(runSection("Performance", async () => await PerformanceInfoCollector.CollectAsync())); // Assumes Perf collector respects tokens internally or finishes quickly
            if (sectionsToRun.Contains("network")) tasks.Add(runSection("Network", async () => await NetworkInfoCollector.CollectAsync(tracerouteTarget))); // Assumes Network collector respects tokens internally or finishes quickly
            if (sectionsToRun.Contains("events")) tasks.Add(runSection("Events", async () => await Task.Run(() => EventLogCollector.CollectAsync(), cancellationToken)));


            await Task.WhenAll(tasks);
            sectionStopwatch.Stop();
            StatusUpdate($"\nData collection phase completed in {sectionStopwatch.ElapsedMilliseconds / 1000.0:0.##} seconds.");


            // --- Run Analysis ---
            if (sectionsToRun.Contains("analysis") && !cancellationToken.IsCancellationRequested)
            {
                 StatusUpdate("Running analysis...");
                 var analysisSw = Stopwatch.StartNew();
                 try
                 {
                     // Pass config if loaded: await AnalysisEngine.PerformAnalysisAsync(report, _isAdmin, _appConfig);
                      report.Analysis = await AnalysisEngine.PerformAnalysisAsync(report, _isAdmin);
                      analysisSw.Stop();
                      StatusUpdate($" -> Analysis completed in {analysisSw.ElapsedMilliseconds} ms.", indent: 4, color: ConsoleColor.DarkGray);

                      if (!string.IsNullOrEmpty(report.Analysis?.SectionCollectionErrorMessage))
                      {
                           StatusUpdate($"[ERROR] Analysis failed to complete: {report.Analysis.SectionCollectionErrorMessage}", color: ConsoleColor.Red);
                      }
                 }
                 catch(Exception ex) // Catch unexpected analysis errors
                 {
                      analysisSw.Stop();
                      StatusUpdate($" -> Analysis FAILED after {analysisSw.ElapsedMilliseconds} ms: {ex.Message}", indent: 4, color: ConsoleColor.Red);
                      report.Analysis = new AnalysisSummary { SectionCollectionErrorMessage = $"Analysis engine critical error: {ex.Message}" };
                 }
            }
            else if(cancellationToken.IsCancellationRequested)
            {
                 StatusUpdate("Skipping analysis due to timeout.", color: ConsoleColor.Yellow);
            }

            StatusUpdate("Diagnostic run finished.");
            return report;
        }


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
            header.AppendLine(Separator + "\n");
            reportHeaderText = header.ToString();

            // Prepare Content
            if (outputJson)
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    // Keep nulls so HTML viewer doesn't break if optional properties (like Analysis) are missing
                    DefaultIgnoreCondition = JsonIgnoreCondition.Never
                };
                outputContent = JsonSerializer.Serialize(report, options);
            }
            else
            {
                string textReport = TextReportGenerator.GenerateReport(report);
                outputContent = reportHeaderText + textReport;
            }

            // Write User-Specified Output File (-o)
            if (outputFile != null)
            {
                try
                {
                    outputFile.Directory?.Create();
                    await File.WriteAllTextAsync(outputFile.FullName, outputContent);
                    StatusUpdate($"{Separator}\nUser-specified report saved to: {outputFile.FullName}\n{Separator}");
                }
                catch (Exception ex)
                {
                    StatusUpdate($"{Separator}\n[ERROR] Could not save user-specified report to file '{outputFile.FullName}': {ex.Message}\n{Separator}", color: ConsoleColor.Red);
                }
            }

            // Automatic Logging (Always Text)
            string? autoLogFilePath = null;
            try
            {
                string baseDirectory = AppContext.BaseDirectory;
                string logDirectory = Path.Combine(baseDirectory, LogDirectoryName);
                Directory.CreateDirectory(logDirectory);
                string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string hostName = Environment.MachineName;
                string fileName = $"{LogFilePrefix}{hostName}_{timeStamp}{LogFileExtension}";
                autoLogFilePath = Path.Combine(logDirectory, fileName);

                // Generate text report specifically for the log file if needed (e.g., if main output was JSON)
                string logContent = outputJson ? reportHeaderText + TextReportGenerator.GenerateReport(report) : outputContent;

                await File.WriteAllTextAsync(autoLogFilePath, logContent);
                if (!_quietMode && (outputFile == null || outputJson || !string.Equals(outputFile.FullName, autoLogFilePath, StringComparison.OrdinalIgnoreCase)))
                {
                     StatusUpdate($"Automatic text log saved to: {autoLogFilePath}");
                }
            }
            catch (Exception ex)
            {
                StatusUpdate($"[WARNING] Could not write automatic log file to '{autoLogFilePath ?? LogDirectoryName}': {ex.Message}", color: ConsoleColor.Yellow);
            }

            // Console Output (Only if not quiet and not JSON)
            if (!_quietMode && !outputJson)
            {
                 Console.WriteLine("\n" + Separator);
                 Console.WriteLine(" --- Report Start ---");
                 Console.WriteLine(outputContent);
                 Console.WriteLine(" --- Report End ---");
                 Console.WriteLine(Separator);
            }

            // Keep console open if interactive (and no explicit output file specified)
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

             if(color.HasValue) Console.ForegroundColor = color.Value;
             Console.WriteLine($"{new string(' ', indent)}{message}");
             if(color.HasValue) Console.ResetColor();
        }
    }
}