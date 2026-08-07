using System.Runtime.InteropServices;

namespace Dock.Interop.Shell;

/// <summary>
/// The name of the wireless network currently joined, or null on a machine that is wired, has no
/// radio, or is not associated with anything.
///
/// Reads the <em>profile name</em> rather than picking the SSID out of the nested association
/// attributes. The two are the same string for any network joined the ordinary way, and the profile
/// name sits at a fixed offset near the front of the structure where the SSID is buried behind
/// several variable-alignment members. Given that a wrong offset here does not fail, it corrupts
/// memory and takes the process down somewhere unrelated, the shallower read is worth the rare case
/// where a renamed profile disagrees with its SSID.
/// </summary>
internal static class WifiInfo
{
    private const uint ClientVersion = 2;

    /// <summary>wlan_intf_opcode_current_connection.</summary>
    private const uint OpcodeCurrentConnection = 7;

    /// <summary>
    /// Offset of <c>strProfileName</c> in WLAN_CONNECTION_ATTRIBUTES: two 4-byte enums ahead of it.
    /// </summary>
    private const int ProfileNameOffset = 8;

    /// <summary>WCHARs in <c>strProfileName</c>, fixed by the API.</summary>
    private const int ProfileNameLength = 256;

    /// <summary>
    /// Offset of the first <c>WLAN_INTERFACE_INFO</c> in the list: two 4-byte counts ahead of it.
    /// The GUID is the first member of that entry.
    /// </summary>
    private const int InterfaceListOffset = 8;

    public static string? CurrentNetwork()
    {
        var handle = IntPtr.Zero;
        var interfaces = IntPtr.Zero;

        try
        {
            if (WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out handle) != 0)
                return null;

            if (WlanEnumInterfaces(handle, IntPtr.Zero, out interfaces) != 0 || interfaces == IntPtr.Zero)
                return null;

            if (Marshal.ReadInt32(interfaces) < 1)
                return null;

            var guid = Marshal.PtrToStructure<Guid>(interfaces + InterfaceListOffset);
            return QueryProfileName(handle, guid);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // No wireless stack on this machine at all.
            return null;
        }
        finally
        {
            if (interfaces != IntPtr.Zero)
                WlanFreeMemory(interfaces);

            if (handle != IntPtr.Zero)
                WlanCloseHandle(handle, IntPtr.Zero);
        }
    }

    private static string? QueryProfileName(IntPtr handle, Guid interfaceId)
    {
        var data = IntPtr.Zero;

        try
        {
            if (WlanQueryInterface(handle, ref interfaceId, OpcodeCurrentConnection,
                    IntPtr.Zero, out var size, out data, IntPtr.Zero) != 0)
            {
                return null;
            }

            // The guard that matters. Windows reports how much it wrote, and reading the profile
            // name out of anything shorter than that would be reading past the buffer.
            if (data == IntPtr.Zero || size < ProfileNameOffset + ProfileNameLength * sizeof(char))
                return null;

            var name = Marshal.PtrToStringUni(data + ProfileNameOffset, ProfileNameLength);
            return string.IsNullOrWhiteSpace(name) ? null : name.TrimEnd('\0').Trim();
        }
        finally
        {
            if (data != IntPtr.Zero)
                WlanFreeMemory(data);
        }
    }

    [DllImport("wlanapi.dll")]
    private static extern int WlanOpenHandle(
        uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr handle);

    [DllImport("wlanapi.dll")]
    private static extern int WlanCloseHandle(IntPtr handle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern int WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);

    [DllImport("wlanapi.dll")]
    private static extern int WlanQueryInterface(
        IntPtr handle, ref Guid interfaceId, uint opCode, IntPtr reserved,
        out uint dataSize, out IntPtr data, IntPtr valueType);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);
}
