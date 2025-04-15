using System;
using System.Threading.Tasks;

namespace DiagnosticToolAllInOne.Helpers
{
    public static class SafeRunner
    {
        // Runs an async Func<Task> and returns true on success, false on failure.
        // Errors should be logged by the calling method or AssignErrorToReport
        public static async Task<bool> RunProtectedAsync(Func<Task> action)
        {
            try
            {
                await action();
                return true; // Indicate success
            }
            catch (Exception ex)
            {
                // Optionally log the exception here if needed for debugging the runner itself
                 Console.Error.WriteLine($"[SafeRunner Error]: {ex.Message}");
                return false; // Indicate failure
            }
        }

         // Example overload if you refactor collectors to return data:
        /*
        public static async Task<T?> RunProtectedAsync<T>(Func<Task<T>> action) where T : class
        {
            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SafeRunner Error]: {ex.Message}");
                return null; // Indicate failure by returning null
            }
        }
        */
    }
}