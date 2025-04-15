using System;
using System.Collections.Generic;
using System.CommandLine; // Command line parsing
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Runtime.Versioning; // Required for [SupportedOSPlatform]
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization; // Required for JsonIgnoreCondition
using System.Threading.Tasks;
using DiagnosticToolAllInOne.Analysis;       // Namespace for AnalysisEngine
using DiagnosticToolAllInOne.Collectors;    // Namespace for Collectors
using DiagnosticToolAllInOne.Helpers;       // Namespace for Helpers
using DiagnosticToolAllInOne.Reporting;     // Namespace for TextReportGenerator

namespace DiagnosticToolAllInOne
{
    public class Program
    {
        private static bool _isAdmin = false;
        private static bool _quietMode = false;
        private const string Separator = "----------------------------------------";

        // --- Log File Constants ---
        private const string LogDirectoryName = "WinDiagLogs";
        private const string LogFilePrefix = "WinDiagReport_";
        private const string LogFileExtension = ".log";

        // Add attribute to Main to address CA1416 warnings originating here
        [SupportedOSPlatform("windows")]
        static async Task<int> Main(string[] args)
        {
            // Use helper to check admin status
            _isAdmin = AdminHelper.IsRunningAsAdmin(); // CA1416 warning suppressed by attributing Main

            // --- System.CommandLine Setup ---
            var rootCommand = new RootCommand("Advanced Windows Diagnostic Tool (All-in-One) - Modular");

            // Options
            var sectionsOption = new Option<List<string>>(
                aliases: new[] { "--sections", "-s" },
                description: "Specify which sections to run (comma-separated or multiple options). Default is all.",
                parseArgument: result => result.Tokens.Select(t => t.Value).ToList()
                ) { AllowMultipleArgumentsPerToken = true, Arity = ArgumentArity.ZeroOrMore };
            sectionsOption.AddCompletions(new[] { "system", "hardware", "software", "security", "performance", "network", "events", "analysis", "all" });

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

            var helpOption = new Option<bool>(
                aliases: new[] { "--help", "-h", "-?" },
                description: "Show help information.");

            rootCommand.AddOption(sectionsOption);
            rootCommand.AddOption(outputOption);
            rootCommand.AddOption(jsonOption);
            rootCommand.AddOption(quietOption);
            rootCommand.AddOption(tracerouteOption);
            rootCommand.AddOption(helpOption);

            rootCommand.SetHandler(async (InvocationContext context) =>
            {
                var sections = context.ParseResult.GetValueForOption(sectionsOption) ?? new List<string>();
                var outputFile = context.ParseResult.GetValueForOption(outputOption);
                var outputJson = context.ParseResult.GetValueForOption(jsonOption);
                _quietMode = context.ParseResult.GetValueForOption(quietOption) || outputJson;
                var tracerouteTarget = context.ParseResult.GetValueForOption(tracerouteOption);

                var report = await RunDiagnostics(sections, tracerouteTarget); // CA1416 warning suppressed by attributing Main

                // Handle output (console, -o file, automatic log)
                await HandleOutput(report, outputFile, outputJson);

                // Determine exit code based on CRITICAL errors recorded in the report sections
                // Check SectionCollectionErrorMessage for critical failures in collection setup
                bool hasCriticalErrors = !string.IsNullOrEmpty(report.System?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Hardware?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Software?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Security?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Performance?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Network?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Events?.SectionCollectionErrorMessage) ||
                                         !string.IsNullOrEmpty(report.Analysis?.SectionCollectionErrorMessage); // Check analysis errors too

                 // Optionally, also consider specific errors as critical if needed
                 // bool hasSpecificCriticalErrors = report.Hardware?.SpecificCollectionErrors?.ContainsKey("SomeCriticalPart") ?? false;

                context.ExitCode = hasCriticalErrors ? 1 : 0; // Exit code 1 if critical errors occurred
                if(hasCriticalErrors && !_quietMode)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine("\n[CRITICAL ERRORS] One or more diagnostic sections encountered critical errors during collection.");
                    Console.ResetColor();
                }
            });

            return await rootCommand.InvokeAsync(args);
        }

        [SupportedOSPlatform("windows")]
        private static async Task<DiagnosticReport> RunDiagnostics(List<string> sections, string? tracerouteTarget)
        {
            var report = new DiagnosticReport { RanAsAdmin = _isAdmin };

            var sectionsToRun = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool runAll = !sections.Any() || sections.Contains("all", StringComparer.OrdinalIgnoreCase);

            if (runAll) { sectionsToRun.UnionWith(new[] { "system", "hardware", "software", "security", "performance", "network", "events", "analysis" }); }
            else
            {
                sectionsToRun.UnionWith(sections);
                // If only analysis is requested, ensure other sections are run too
                if (sectionsToRun.Contains("analysis") && sectionsToRun.Count == 1 && !(sections.Contains("all", StringComparer.OrdinalIgnoreCase)))
                {
                    if (!_quietMode) Console.WriteLine("Analysis requires other sections. Running default data collection...");
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
                if (!_isAdmin) { Console.WriteLine("[WARNING] Not running with Administrator privileges. Some data may be incomplete or inaccessible."); }
                Console.WriteLine($"Sections to run: {(runAll ? "All" : string.Join(", ", sectionsToRun))}");
                if (!string.IsNullOrEmpty(tracerouteTarget)) Console.WriteLine($"Traceroute requested for: {tracerouteTarget}");
                Console.WriteLine(Separator); Console.WriteLine("Gathering data...");
            }

            var tasks = new List<Task>();

            // --- Run Selected Collectors ---
            // Assign results directly within the Task.Run
            if (sectionsToRun.Contains("system")) tasks.Add(Task.Run(async () => report.System = await SystemInfoCollector.CollectAsync(_isAdmin)));
            if (sectionsToRun.Contains("hardware")) tasks.Add(Task.Run(async () => report.Hardware = await HardwareInfoCollector.CollectAsync(_isAdmin)));
            if (sectionsToRun.Contains("software")) tasks.Add(Task.Run(async () => report.Software = await SoftwareInfoCollector.CollectAsync()));
            if (sectionsToRun.Contains("security")) tasks.Add(Task.Run(async () => report.Security = await SecurityInfoCollector.CollectAsync(_isAdmin)));
            if (sectionsToRun.Contains("performance")) tasks.Add(Task.Run(async () => report.Performance = await PerformanceInfoCollector.CollectAsync()));
            if (sectionsToRun.Contains("network")) tasks.Add(Task.Run(async () => report.Network = await NetworkInfoCollector.CollectAsync(tracerouteTarget)));
            if (sectionsToRun.Contains("events")) tasks.Add(Task.Run(async () => report.Events = await EventLogCollector.CollectAsync()));

            await Task.WhenAll(tasks);

            // --- Run Analysis ---
            // Analysis depends on other sections, so run it after others complete
            if (sectionsToRun.Contains("analysis"))
            {
                 if (!_quietMode) Console.WriteLine("Running analysis...");
                 // Analysis engine itself doesn't use SafeRunner, it catches internally
                 report.Analysis = await AnalysisEngine.PerformAnalysisAsync(report, _isAdmin);
                 if (!string.IsNullOrEmpty(report.Analysis?.SectionCollectionErrorMessage) && !_quietMode)
                 {
                    Console.Error.WriteLine($"[ERROR] Analysis failed to complete: {report.Analysis.SectionCollectionErrorMessage}");
                 }
            }

             if (!_quietMode) Console.WriteLine("Data collection and analysis complete.");
            return report;
        }


        private static async Task HandleOutput(DiagnosticReport report, FileInfo? outputFile, bool outputJson)
        {
            string outputContent;
            string reportHeaderText;

            // --- Prepare Header ---
            var header = new StringBuilder();
            header.AppendLine("========================================");
            header.AppendLine("   Advanced Windows Diagnostic Tool Report");
            header.AppendLine("========================================");
            header.AppendLine($"Generated: {DateTime.Now} (Local Time) / {report.ReportTimestamp:u} (UTC)");
            header.AppendLine($"Ran as Administrator: {report.RanAsAdmin}");
            header.AppendLine(Separator + "\n");
            reportHeaderText = header.ToString();

            // --- Prepare Content ---
            if (outputJson)
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    // Include null values so HTML viewer doesn't break if properties are missing
                    // DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    DefaultIgnoreCondition = JsonIgnoreCondition.Never // Or keep nulls
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
                    outputFile.Directory?.Create(); // Ensure directory exists
                    await File.WriteAllTextAsync(outputFile.FullName, outputContent);
                    if (!_quietMode) { Console.WriteLine($"{Separator}\nUser-specified report saved to: {outputFile.FullName}\n{Separator}"); }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"{Separator}\n[ERROR] Could not save user-specified report to file '{outputFile.FullName}': {ex.Message}\n{Separator}");
                    Console.ResetColor();
                }
            }

            // --- Automatic Logging (Always Text) ---
            string? autoLogFilePath = null;
            try
            {
                string baseDirectory = AppContext.BaseDirectory;
                string logDirectory = Path.Combine(baseDirectory, LogDirectoryName);
                Directory.CreateDirectory(logDirectory);
                string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string hostName = Environment.MachineName; // Add hostname for easier identification
                string fileName = $"{LogFilePrefix}{hostName}_{timeStamp}{LogFileExtension}";
                autoLogFilePath = Path.Combine(logDirectory, fileName);

                // Automatic log is always text format for easy viewing
                string textReportForLog = TextReportGenerator.GenerateReport(report);
                string logContent = reportHeaderText + textReportForLog;

                await File.WriteAllTextAsync(autoLogFilePath, logContent);
                // Only notify if not quiet and not writing the same text file via -o
                if (!_quietMode && (outputFile == null || outputJson || outputFile.FullName != autoLogFilePath))
                {
                     Console.WriteLine($"Automatic text log saved to: {autoLogFilePath}");
                }
            }
            catch (Exception ex)
            {
                if (!_quietMode)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Error.WriteLine($"[WARNING] Could not write automatic log file to '{autoLogFilePath ?? LogDirectoryName}': {ex.Message}");
                    Console.ResetColor();
                }
            }

            // --- Console Output (Only if not quiet and not JSON) ---
            if (!_quietMode && !outputJson)
            {
                 // Display the text report content (which already includes the header)
                 Console.WriteLine("\n" + Separator);
                 Console.WriteLine(" --- Report Start ---");
                 Console.WriteLine(outputContent); // Display full text report in console
                 Console.WriteLine(" --- Report End ---");
                 Console.WriteLine(Separator);
            }


            // --- Keep console open if interactive ---
             // Keep open only if: not quiet, not json output, AND no specific output file was requested
            if (!_quietMode && !outputJson && outputFile == null && !Console.IsInputRedirected && Environment.UserInteractive)
            {
                Console.WriteLine("\nPress Enter to exit...");
                Console.ReadLine();
            }
        }
    }
}