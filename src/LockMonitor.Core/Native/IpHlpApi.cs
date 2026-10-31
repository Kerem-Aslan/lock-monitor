using System.Runtime.InteropServices;

namespace LockMonitor.Native;

/// <summary>GetIfTable2: per-interface 64-bit traffic counters in one call, no admin needed.</summary>
internal static unsafe partial class IpHlpApi
{
    public const int IF_TYPE_SOFTWARE_LOOPBACK = 24;
    public const int IF_TYPE_TUNNEL = 131;
    public const int IfOperStatusUp = 1;

    // InterfaceAndOperStatusFlags bits
    public const byte HardwareInterfaceFlag = 0x01;
    public const byte FilterInterfaceFlag = 0x02;

    /// <summary>MIB_IF_ROW2 from netioapi.h. Sequential layout reproduces the C layout (1352 bytes on x64).</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MIB_IF_ROW2
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public Guid InterfaceGuid;
        public fixed char Alias[257];
        public fixed char Description[257];
        public uint PhysicalAddressLength;
        public fixed byte PhysicalAddress[32];
        public fixed byte PermanentPhysicalAddress[32];
        public uint Mtu;
        public int Type;
        public int TunnelType;
        public int MediaType;
        public int PhysicalMediumType;
        public int AccessType;
        public int DirectionType;
        public byte InterfaceAndOperStatusFlags;
        public int OperStatus;
        public int AdminStatus;
        public int MediaConnectState;
        public Guid NetworkGuid;
        public int ConnectionType;
        public ulong TransmitLinkSpeed;
        public ulong ReceiveLinkSpeed;
        public ulong InOctets;
        public ulong InUcastPkts;
        public ulong InNUcastPkts;
        public ulong InDiscards;
        public ulong InErrors;
        public ulong InUnknownProtos;
        public ulong InUcastOctets;
        public ulong InMulticastOctets;
        public ulong InBroadcastOctets;
        public ulong OutOctets;
        public ulong OutUcastPkts;
        public ulong OutNUcastPkts;
        public ulong OutDiscards;
        public ulong OutErrors;
        public ulong OutUcastOctets;
        public ulong OutMulticastOctets;
        public ulong OutBroadcastOctets;
        public ulong OutQLen;
    }

    public const int ExpectedRowSize = 1352;

    [LibraryImport("iphlpapi.dll")]
    public static partial int GetIfTable2(out nint table);

    [LibraryImport("iphlpapi.dll")]
    public static partial void FreeMibTable(nint memory);

    /// <summary>
    /// Physical, connected interfaces only: hardware adapters that are up, not NDIS filter
    /// instances (WFP/QoS layers would double count), not loopback or tunnels (VPN traffic is
    /// already counted on the physical adapter). Hyper-V vEthernet is not a hardware interface.
    /// </summary>
    public static bool IsCountable(in MIB_IF_ROW2 row) =>
        (row.InterfaceAndOperStatusFlags & HardwareInterfaceFlag) != 0 &&
        (row.InterfaceAndOperStatusFlags & FilterInterfaceFlag) == 0 &&
        row.OperStatus == IfOperStatusUp &&
        row.Type != IF_TYPE_SOFTWARE_LOOPBACK &&
        row.Type != IF_TYPE_TUNNEL;

    /// <summary>Total received/sent bytes over all countable interfaces.</summary>
    public static (ulong Received, ulong Sent, int Interfaces) ReadTotals()
    {
        int error = GetIfTable2(out nint table);
        if (error != 0)
        {
            throw new System.ComponentModel.Win32Exception(error);
        }
        try
        {
            uint count = *(uint*)table;
            // MIB_IF_TABLE2 { ULONG NumEntries; MIB_IF_ROW2 Table[]; } - rows are 8-byte aligned.
            var rows = (MIB_IF_ROW2*)(table + 8);
            ulong received = 0, sent = 0;
            int interfaces = 0;
            for (uint i = 0; i < count; i++)
            {
                if (IsCountable(rows[i]))
                {
                    received += rows[i].InOctets;
                    sent += rows[i].OutOctets;
                    interfaces++;
                }
            }
            return (received, sent, interfaces);
        }
        finally
        {
            FreeMibTable(table);
        }
    }
}
