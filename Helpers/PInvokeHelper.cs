// Helpers/PInvokeHelper.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Net.NetworkInformation; // Required for TcpState
using DiagnosticToolAllInOne.Helpers;

namespace DiagnosticToolAllInOne.Helpers
{
    [SupportedOSPlatform("windows")]
    public static class PInvokeHelper
    {
        // Constants for Address Families
        private const int AF_INET = 2; // IPv4
        private const int AF_INET6 = 23; // IPv6 (Add structures/calls if needed)

        // --- P/Invoke Signatures ---

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, TCP_TABLE_CLASS TableClass, uint Reserved);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint GetExtendedUdpTable(IntPtr pUdpTable, ref int pdwSize, bool bOrder, int ulAf, UDP_TABLE_CLASS TableClass, uint Reserved);


        // --- Structures for TCP (IPv4) ---

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] localPort;
            public uint remoteAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] remotePort;
            public uint owningPid;

            // Helper properties to convert raw data
            public ushort LocalPort => BitConverter.ToUInt16(new byte[2] { localPort[1], localPort[0] }, 0);
            public ushort RemotePort => BitConverter.ToUInt16(new byte[2] { remotePort[1], remotePort[0] }, 0);
            public IPAddress LocalAddress => new IPAddress(localAddr);
            public IPAddress RemoteAddress => new IPAddress(remoteAddr);
            public TcpState State => ConvertToTcpState(state); // Use helper method below
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_TCPTABLE_OWNER_PID
        {
            public uint dwNumEntries;
            // Followed by variable number of MIB_TCPROW_OWNER_PID structs
        }

        // --- Structures for UDP (IPv4) ---

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_UDPROW_OWNER_PID
        {
            public uint localAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] localPort;
            public uint owningPid;

            public ushort LocalPort => BitConverter.ToUInt16(new byte[2] { localPort[1], localPort[0] }, 0);
            public IPAddress LocalAddress => new IPAddress(localAddr);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_UDPTABLE_OWNER_PID
        {
            public uint dwNumEntries;
            // Followed by variable number of MIB_UDPROW_OWNER_PID structs
        }

        // --- Enums for Table Classes ---

        public enum TCP_TABLE_CLASS
        {
            TCP_TABLE_BASIC_LISTENER,
            TCP_TABLE_BASIC_CONNECTIONS,
            TCP_TABLE_BASIC_ALL,
            TCP_TABLE_OWNER_PID_LISTENER,
            TCP_TABLE_OWNER_PID_CONNECTIONS,
            TCP_TABLE_OWNER_PID_ALL, // Use this one for all TCP entries with PIDs
            TCP_TABLE_OWNER_MODULE_LISTENER,
            TCP_TABLE_OWNER_MODULE_CONNECTIONS,
            TCP_TABLE_OWNER_MODULE_ALL
        }

        public enum UDP_TABLE_CLASS
        {
            UDP_TABLE_BASIC,
            UDP_TABLE_OWNER_PID, // Use this one for UDP listeners with PIDs
            UDP_TABLE_OWNER_MODULE
        }

        // --- Helper Methods ---

        /// <summary>
        /// Gets a lookup dictionary mapping TCP connection endpoints to owning PIDs.
        /// Key format: "LocalIP:LocalPort-RemoteIP:RemotePort" for connections, "LocalIP:LocalPort" for listeners.
        /// </summary>
        /// <returns>Dictionary mapping connection/listener string to PID.</returns>
        /// <exception cref="System.ComponentModel.Win32Exception">Thrown if the API call fails.</exception>
        public static Dictionary<string, uint> GetTcpConnectionPids()
        {
            Dictionary<string, uint> lookup = new Dictionary<string, uint>();
            int bufferSize = 0;
            IntPtr tcpTablePtr = IntPtr.Zero;
            // Get all TCP entries (listeners and connections) with PIDs
            uint result = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);

            if (result != 0 && result != 122) // 122 = ERROR_INSUFFICIENT_BUFFER
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), $"GetExtendedTcpTable initial call failed with {result}");
            }

            try
            {
                tcpTablePtr = Marshal.AllocHGlobal(bufferSize);
                result = GetExtendedTcpTable(tcpTablePtr, ref bufferSize, true, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);

                if (result != 0)
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), $"GetExtendedTcpTable data call failed with {result}");
                }

                if (tcpTablePtr == IntPtr.Zero) throw new InvalidOperationException("TCP table pointer is zero before structure conversion."); // Should not happen
                MIB_TCPTABLE_OWNER_PID table = Marshal.PtrToStructure<MIB_TCPTABLE_OWNER_PID>(tcpTablePtr); // Use generic version

                IntPtr rowPtr = (IntPtr)((long)tcpTablePtr + Marshal.SizeOf(table.dwNumEntries));
                int rowSize = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));
                for (int i = 0; i < table.dwNumEntries; i++)
                {
                    if (rowPtr == IntPtr.Zero) throw new InvalidOperationException("TCP row pointer is zero before structure conversion."); // Should not happen
                    MIB_TCPROW_OWNER_PID row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr); // Use generic version
                    TcpState state = ConvertToTcpState(row.state);

                    // Generate key based on state (listener vs connection)
                    string key;
                    if (state == TcpState.Listen)
                    {
                        // Key for listeners: LocalIP:LocalPort
                        key = $"{row.LocalAddress}:{row.LocalPort}";
                    }
                    else
                    {
                        // Key for connections: LocalIP:LocalPort-RemoteIP:RemotePort
                        key = $"{row.LocalAddress}:{row.LocalPort}-{row.RemoteAddress}:{row.RemotePort}";
                    }

                    lookup[key] = row.owningPid; // Add to lookup table
                    rowPtr = (IntPtr)((long)rowPtr + rowSize); // Move pointer to next row
                }
            }
            finally
            {
                if (tcpTablePtr != IntPtr.Zero) Marshal.FreeHGlobal(tcpTablePtr);
            }
            return lookup;
        }

        /// <summary>
        /// Gets a lookup dictionary mapping UDP listener endpoints to owning PIDs.
        /// Key format: "LocalIP:LocalPort"
        /// </summary>
        /// <returns>Dictionary mapping listener string to PID.</returns>
        /// <exception cref="System.ComponentModel.Win32Exception">Thrown if the API call fails.</exception>
        public static Dictionary<string, uint> GetUdpListenerPids()
        {
            Dictionary<string, uint> lookup = new Dictionary<string, uint>();
            int bufferSize = 0;
            IntPtr udpTablePtr = IntPtr.Zero;
            uint result = GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);

            if (result != 0 && result != 122) // 122 = ERROR_INSUFFICIENT_BUFFER
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), $"GetExtendedUdpTable initial call failed with {result}");
            }

            try
            {
                udpTablePtr = Marshal.AllocHGlobal(bufferSize);
                result = GetExtendedUdpTable(udpTablePtr, ref bufferSize, true, AF_INET, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);

                if (result != 0)
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), $"GetExtendedUdpTable data call failed with {result}");
                }

                if (udpTablePtr == IntPtr.Zero) throw new InvalidOperationException("UDP table pointer is zero before structure conversion."); // Should not happen
                MIB_UDPTABLE_OWNER_PID table = Marshal.PtrToStructure<MIB_UDPTABLE_OWNER_PID>(udpTablePtr); // Use generic version

                IntPtr rowPtr = (IntPtr)((long)udpTablePtr + Marshal.SizeOf(table.dwNumEntries));
                int rowSize = Marshal.SizeOf(typeof(MIB_UDPROW_OWNER_PID));
                for (int i = 0; i < table.dwNumEntries; i++)
                {
                    if (rowPtr == IntPtr.Zero) throw new InvalidOperationException("UDP row pointer is zero before structure conversion."); // Should not happen
                    MIB_UDPROW_OWNER_PID row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr); // Use generic version
                    // Key format: LocalIP:LocalPort
                    string key = $"{row.LocalAddress}:{row.LocalPort}";
                    lookup[key] = row.owningPid;
                    rowPtr = (IntPtr)((long)rowPtr + rowSize); // Move pointer to next row
                }
            }
            finally
            {
                if (udpTablePtr != IntPtr.Zero) Marshal.FreeHGlobal(udpTablePtr);
            }
            return lookup;
        }


        /// <summary>
        /// Gets the process name for a given PID, handling errors.
        /// Returns specific strings for common error conditions.
        /// </summary>
        public static string? GetProcessName(int pid)
        {
            // Handle special PIDs
            if (pid == 0) return "System Idle Process";
            if (pid == 4) return "System";
            if (pid < 0) return "Invalid PID"; // PIDs should not be negative

            try
            {
                using (var process = Process.GetProcessById(pid))
                {
                    if (process == null || process.HasExited) return "Process Exited";
                    return process.ProcessName;
                }
            }
            catch (ArgumentException) { return "Process Exited"; }
            catch (InvalidOperationException) { return "Process Exited"; }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                Logger.LogWarning($"[PInvokeHelper] Access Denied getting process name for PID {pid}.");
                return "Access Denied";
            }
            catch (Exception ex)
            {
                Logger.LogError($"[PInvokeHelper] Error getting process name for PID {pid}", ex);
                return "Lookup Error";
            }
        }


        // Helper to convert MIB state to .NET TcpState enum
        private static TcpState ConvertToTcpState(uint mibState)
        {
            // Map MIB TCP state constants to System.Net.NetworkInformation.TcpState enum values
            return mibState switch
            {
                1 => TcpState.Closed,       // MIB_TCP_STATE_CLOSED
                2 => TcpState.Listen,       // MIB_TCP_STATE_LISTEN
                3 => TcpState.SynSent,      // MIB_TCP_STATE_SYN_SENT
                4 => TcpState.SynReceived,  // MIB_TCP_STATE_SYN_RCVD
                5 => TcpState.Established,  // MIB_TCP_STATE_ESTAB
                6 => TcpState.FinWait1,     // MIB_TCP_STATE_FIN_WAIT1
                7 => TcpState.FinWait2,     // MIB_TCP_STATE_FIN_WAIT2
                8 => TcpState.CloseWait,    // MIB_TCP_STATE_CLOSE_WAIT
                9 => TcpState.Closing,      // MIB_TCP_STATE_CLOSING
                10 => TcpState.LastAck,     // MIB_TCP_STATE_LAST_ACK
                11 => TcpState.TimeWait,    // MIB_TCP_STATE_TIME_WAIT
                12 => TcpState.DeleteTcb,   // MIB_TCP_STATE_DELETE_TCB
                _ => TcpState.Unknown,      // Default for unrecognized states
            };
        }
    }
}
