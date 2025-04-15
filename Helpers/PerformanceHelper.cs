using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace DiagnosticToolAllInOne.Helpers
{
    [SupportedOSPlatform("windows")]
    public static class PerformanceHelper
    {
        // Gets a counter value by taking two samples with a delay
        public static async Task<string> GetSampledCounterValueAsync(string category, string counter, string? instance = null, int sampleDelayMs = 500)
        {
            // Validate delay
            if (sampleDelayMs <= 0) sampleDelayMs = 100; // Ensure minimum delay

            return await Task.Run(async () => // Use async lambda for await Task.Delay
            {
                PerformanceCounter? perfCounter = null;
                try
                {
                    // --- Instance and Category Validation ---
                    if (!PerformanceCounterCategory.Exists(category)) return $"Category '{category}' not found";

                    string? effectiveInstance = instance;
                    bool categoryIsMultiInstance = false;
                    try
                    {
                        // Check if the category type is MultiInstance
                        categoryIsMultiInstance = new PerformanceCounterCategory(category).CategoryType == PerformanceCounterCategoryType.MultiInstance;
                    }
                    catch (Exception ex) { return $"Error checking category type for '{category}': {ex.Message.Split('.')[0]}"; }

                    // Determine effective instance name based on category type
                    if (categoryIsMultiInstance && string.IsNullOrEmpty(instance))
                    {
                        effectiveInstance = "_Total"; // Default to _Total for multi-instance if no instance specified
                    }
                    else if (!categoryIsMultiInstance)
                    {
                        effectiveInstance = string.Empty; // Use empty string for single-instance categories
                    }
                    // If multi-instance and instance is specified, use it (validation below)
                    // If single-instance and instance is specified, ignore instance (handled by using empty string)

                    // Validate instance existence if applicable
                    if (categoryIsMultiInstance && !string.IsNullOrEmpty(effectiveInstance))
                    {
                        try
                        {
                             if (!(new PerformanceCounterCategory(category)).InstanceExists(effectiveInstance))
                             { return $"Instance '{effectiveInstance}' not found in '{category}'."; }
                        }
                        catch(Exception ex) { return $"Error checking instance '{effectiveInstance}' in '{category}': {ex.Message.Split('.')[0]}"; }
                    }

                    // Validate counter existence
                     try
                     {
                        if (!(new PerformanceCounterCategory(category)).CounterExists(counter)) return $"Counter '{counter}' not found in '{category}'";
                     }
                    catch(Exception ex) { return $"Error checking counter '{counter}' in '{category}': {ex.Message.Split('.')[0]}"; }

                    // --- Create and Sample Counter ---
                    perfCounter = string.IsNullOrEmpty(effectiveInstance)
                        ? new PerformanceCounter(category, counter, true) // ReadOnly = true
                        : new PerformanceCounter(category, counter, effectiveInstance, true);

                    // First sample (often returns 0)
                    perfCounter.NextValue();
                    // Wait for the specified interval
                    await Task.Delay(sampleDelayMs);
                    // Second sample (should be more accurate)
                    float value = perfCounter.NextValue();

                    // Format based on common counter types
                    if (counter.Contains("%") || counter.Contains("Percent"))
                       return $"{value:0.##}"; // Percentage format
                    else if (counter.Contains("Bytes") || counter.Contains("Memory"))
                       return $"{value:0}"; // Integer format for memory/bytes typically
                    else
                       return $"{value:0.##}"; // Default format for others (like queue length)

                }
                catch (InvalidOperationException ioex) { return $"PerfCounter Error: {ioex.Message.Split('.')[0]}"; }
                catch (UnauthorizedAccessException) { return "PerfCounter Access Denied"; }
                catch (System.ComponentModel.Win32Exception winex) { return $"PerfCounter Win32 Error ({winex.NativeErrorCode})"; }
                catch (Exception ex) { return $"PerfCounter Error: {ex.GetType().Name}"; }
                finally { perfCounter?.Dispose(); }
            });
        }
    }
}