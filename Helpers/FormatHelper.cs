using System;

namespace DiagnosticToolAllInOne.Helpers
{
    public static class FormatHelper
    {
        // --- Byte Formatter ---
        public static string FormatBytes(ulong bytes)
        {
            string[] suffix = { "B", "KB", "MB", "GB", "TB", "PB" };
            int i = 0;
            double dblBytes = bytes;
            if (bytes == 0) return "0 B";
            while (dblBytes >= 1024 && i < suffix.Length - 1) { dblBytes /= 1024.0; i++; }
            return $"{dblBytes:0.##} {suffix[i]}";
        }

        // --- RAM Type / Form Factor Helpers ---
        public static string GetMemoryTypeDescription(string? typeId) => typeId switch
        {
            "0" => "Unknown", "1" => "Other", "2" => "DRAM", "3" => "Synchronous DRAM", "4" => "Cache DRAM",
            "5" => "EDO", "6" => "EDRAM", "7" => "VRAM", "8" => "SRAM", "9" => "RAM", "10" => "ROM",
            "11" => "Flash", "12" => "EEPROM", "13" => "FEPROM", "14" => "EPROM", "15" => "CDRAM",
            "16" => "3DRAM", "17" => "SDRAM", "18" => "SGRAM", "19" => "RDRAM", "20" => "DDR",
            "21" => "DDR2", "22" => "DDR2 FB-DIMM", "24" => "DDR3", "25" => "FBD2", "26" => "DDR4",
            "27" => "LPDDR", "28" => "LPDDR2", "29" => "LPDDR3", "30" => "LPDDR4", "31" => "Logical NV-DIMM",
            "32" => "HBM", "33" => "HBM2", "34" => "DDR5", "35" => "LPDDR5",
            _ => $"Unknown ({typeId ?? "null"})"
        };

        public static string GetFormFactorDescription(string? formFactorId) => formFactorId switch
        {
            "0" => "Unknown", "1" => "Other", "2" => "SIP", "3" => "DIP", "4" => "ZIP", "5" => "SOJ",
            "6" => "Proprietary", "7" => "SIMM", "8" => "DIMM", "9" => "TSOP", "10" => "PGA", "11" => "RIMM",
            "12" => "SODIMM", "13" => "SRIMM", "14" => "SMD", "15" => "SSMP", "16" => "QFP", "17" => "TQFP",
            "18" => "SOIC", "19" => "LCC", "20" => "PLCC", "21" => "BGA", "22" => "FPBGA", "23" => "LGA",
            "24" => "FB-DIMM",
            _ => $"Unknown ({formFactorId ?? "null"})"
        };

        // --- Security Center State Decoder ---
        public static string DecodeProductState(string? productState)
        {
             if (!uint.TryParse(productState, System.Globalization.NumberStyles.HexNumber, null, out uint state))
             {
                  if (!uint.TryParse(productState, out state)) { return $"Unknown ({productState ?? "null"})"; }
             }
             bool isEnabled = (state & 0b_0001_0000_0000_0000) != 0;
             bool isUpToDate = (state & 0b_0000_0000_0001_0000) != 0;
             return $"{(isEnabled ? "Enabled" : "Disabled/Snoozed")}, {(isUpToDate ? "Up-to-date" : "Not up-to-date")} (State: {state:X})";
        }
    }
}