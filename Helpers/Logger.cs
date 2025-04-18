// In Helpers/Logger.cs
using System;
using System.IO; // Required for Path
using System.Text; // Required for Encoding

namespace DiagnosticToolAllInOne.Helpers
{
    public static class Logger
    {
        // Simple file logging implementation
        private static readonly string LogFilePath = Path.Combine(AppContext.BaseDirectory, "WinDiagInternal.log");
        private static readonly object _lockObj = new object(); // For thread safety
        public static bool IsDebugEnabled { get; set; } = false; // Control debug logging

        // Static constructor to clear the log file on application start (optional)
        static Logger()
        {
            try
            {
                 // Clear or initialize log file
                 File.WriteAllText(LogFilePath, $"--- Log started at {DateTime.Now} ---\n", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // Fallback to console if file logging fails initially
                Console.Error.WriteLine($"[CRITICAL LOGGER ERROR] Failed to initialize log file '{LogFilePath}': {ex.Message}");
            }
        }

        private static void WriteLog(string level, string message, Exception? ex = null)
        {
             // Thread-safe writing to the log file
             lock (_lockObj)
             {
                 try
                 {
                     using (var streamWriter = new StreamWriter(LogFilePath, true, Encoding.UTF8)) // Append mode
                     {
                         streamWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level.PadRight(5)}] {message}");
                         if (ex != null)
                         {
                             streamWriter.WriteLine($"    Exception: {ex.GetType().Name} - {ex.Message}");
                             streamWriter.WriteLine($"    Stack Trace: {ex.StackTrace}");
                         }
                     }
                 }
                 catch (Exception fileEx)
                 {
                      // Fallback if writing to file fails
                      Console.Error.WriteLine($"[LOGGER FILE ERROR] Could not write to log file: {fileEx.Message}");
                      Console.Error.WriteLine($"    Original Log: [{level}] {message} {(ex != null ? $"- Exception: {ex.Message}" : "")}");
                 }
             }
        }

        public static void LogInfo(string message)
        {
            WriteLog("INFO", message);
            // Optionally write INFO to console as well?
            // Console.WriteLine($"[INFO] {message}");
        }

        public static void LogDebug(string message)
        {
            if (IsDebugEnabled)
            {
                WriteLog("DEBUG", message);
            }
        }

        public static void LogWarning(string message, Exception? ex = null)
        {
             WriteLog("WARN", message, ex);
             // Also write warnings to console error stream
             Console.Error.WriteLine($"[WARN] {message}{(ex != null ? $" - Exception: {ex.Message}" : "")}");
        }

        public static void LogError(string message, Exception? ex = null)
        {
             WriteLog("ERROR", message, ex);
             // Also write errors to console error stream
             Console.Error.WriteLine($"[ERROR] {message}{(ex != null ? $" - Exception: {ex.ToString()}" : "")}"); // Log full exception details for errors
        }
    }
}