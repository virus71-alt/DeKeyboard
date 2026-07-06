using System.ComponentModel;
using System.Runtime.InteropServices;
using static Dekeyboard.Interop.NativeMethods;

namespace Dekeyboard.Interop;

/// <summary>A single present device in a setup class.</summary>
public sealed record DeviceInfo(
    string InstanceId,
    string Description,
    string Enumerator,
    string[] HardwareIds)
{
    /// <summary>The bus the device sits on: ACPI, HID, USB, BTHENUM, ...</summary>
    public bool IsUsb  => Enumerator.Equals("USB", StringComparison.OrdinalIgnoreCase);
    public bool IsBluetooth =>
        Enumerator.StartsWith("BTH", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{Description} [{InstanceId}]";
}

/// <summary>
/// Thin managed wrapper over SetupAPI that can enumerate a device class and flip a
/// specific device instance on/off. Everything here needs administrator rights.
/// </summary>
public static class DeviceControl
{
    /// <summary>Return every present device in the given setup class.</summary>
    public static List<DeviceInfo> Enumerate(Guid classGuid)
    {
        var result = new List<DeviceInfo>();
        IntPtr set = SetupDiGetClassDevs(ref classGuid, null, IntPtr.Zero, DIGCF_PRESENT);
        if (set == INVALID_HANDLE_VALUE)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetClassDevs failed");

        try
        {
            var did = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref did); i++)
            {
                string instanceId = GetInstanceId(set, ref did);
                string desc = GetStringProperty(set, ref did, SPDRP_FRIENDLYNAME)
                              ?? GetStringProperty(set, ref did, SPDRP_DEVICEDESC)
                              ?? "(unknown device)";
                string enumr = GetStringProperty(set, ref did, SPDRP_ENUMERATOR)
                               ?? instanceId.Split('\\').FirstOrDefault() ?? "";
                string[] hwids = GetMultiStringProperty(set, ref did, SPDRP_HARDWAREID);

                result.Add(new DeviceInfo(instanceId, desc, enumr, hwids));
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        return result;
    }

    /// <summary>Enable or disable the device whose instance id matches (case-insensitive).</summary>
    public static void SetEnabled(Guid classGuid, string instanceId, bool enable)
    {
        IntPtr set = SetupDiGetClassDevs(ref classGuid, null, IntPtr.Zero, DIGCF_PRESENT);
        if (set == INVALID_HANDLE_VALUE)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetClassDevs failed");

        try
        {
            var did = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref did); i++)
            {
                string current = GetInstanceId(set, ref did);
                if (!current.Equals(instanceId, StringComparison.OrdinalIgnoreCase))
                    continue;

                ChangeState(set, ref did, enable);
                return;
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        throw new InvalidOperationException(
            $"Device instance '{instanceId}' was not found in class {classGuid}.");
    }

    /// <summary>
    /// Walk the device tree upward from <paramref name="instanceId"/> and report whether
    /// any ancestor sits on the USB or Bluetooth bus. This distinguishes an internal
    /// HID keyboard (parent chain is ACPI / I2C) from an external USB-HID keyboard.
    /// Returns false on any failure (fail-open: treat as not-external).
    /// </summary>
    public static bool IsBehindUsbOrBluetooth(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId)) return false;
        if (CM_Locate_DevNodeW(out uint node, instanceId, CM_LOCATE_DEVNODE_NORMAL) != CR_SUCCESS)
            return false;

        for (int guard = 0; guard < 32; guard++)
        {
            if (CM_Get_Parent(out uint parent, node, 0) != CR_SUCCESS) break;

            string id = GetDeviceId(parent);
            if (id.Length == 0) break;

            string enumr = id.Split('\\')[0];
            if (enumr.Equals("USB", StringComparison.OrdinalIgnoreCase)) return true;
            if (enumr.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)) return true;
            if (enumr.Equals("HTREE", StringComparison.OrdinalIgnoreCase)) break; // reached the root

            node = parent;
        }
        return false;
    }

    /// <summary>
    /// Whether the device node reports the DN_DISABLEABLE capability. If it does not,
    /// SetupDiCallClassInstaller(DICS_DISABLE) will always fail and we must fall back
    /// to input blocking. Returns true when the status can't be read (let the caller try).
    /// </summary>
    public static bool IsDisableable(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId)) return true;
        if (CM_Locate_DevNodeW(out uint node, instanceId, CM_LOCATE_DEVNODE_NORMAL) != CR_SUCCESS)
            return true;
        if (CM_Get_DevNode_Status(out uint status, out _, node, 0) != CR_SUCCESS)
            return true;
        return (status & DN_DISABLEABLE) != 0;
    }

    /// <summary>True when the device node is currently in the "disabled" problem state.</summary>
    public static bool IsCurrentlyDisabled(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId)) return false;
        if (CM_Locate_DevNodeW(out uint node, instanceId, CM_LOCATE_DEVNODE_NORMAL) != CR_SUCCESS)
            return false;
        if (CM_Get_DevNode_Status(out uint status, out uint problem, node, 0) != CR_SUCCESS)
            return false;
        return (status & DN_HAS_PROBLEM) != 0 && problem == CM_PROB_DISABLED;
    }

    private static string GetDeviceId(uint node)
    {
        if (CM_Get_Device_ID_Size(out int len, node, 0) != CR_SUCCESS || len <= 0) return string.Empty;
        var buffer = new char[len + 1];
        if (CM_Get_Device_IDW(node, buffer, buffer.Length, 0) != CR_SUCCESS) return string.Empty;
        return new string(buffer).TrimEnd('\0');
    }

    private static void ChangeState(IntPtr set, ref SP_DEVINFO_DATA did, bool enable)
    {
        var pcp = new SP_PROPCHANGE_PARAMS
        {
            ClassInstallHeader = new SP_CLASSINSTALL_HEADER
            {
                cbSize = Marshal.SizeOf<SP_CLASSINSTALL_HEADER>(),
                InstallFunction = DIF_PROPERTYCHANGE
            },
            StateChange = enable ? DICS_ENABLE : DICS_DISABLE,
            Scope = DICS_FLAG_GLOBAL,
            HwProfile = 0
        };

        if (!SetupDiSetClassInstallParams(set, ref did, ref pcp, Marshal.SizeOf<SP_PROPCHANGE_PARAMS>()))
        {
            int e = Marshal.GetLastWin32Error();
            throw new Win32Exception(e, $"SetupDiSetClassInstallParams failed (Win32 {e})");
        }

        if (!SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, set, ref did))
        {
            int e = Marshal.GetLastWin32Error();
            throw new Win32Exception(e, $"SetupDiCallClassInstaller failed (Win32 {e})");
        }
    }

    private static string GetInstanceId(IntPtr set, ref SP_DEVINFO_DATA did)
    {
        SetupDiGetDeviceInstanceId(set, ref did, null, 0, out int required);
        if (required <= 0) return string.Empty;

        var buffer = new char[required];
        if (!SetupDiGetDeviceInstanceId(set, ref did, buffer, buffer.Length, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetDeviceInstanceId failed");

        return new string(buffer).TrimEnd('\0');
    }

    private static string? GetStringProperty(IntPtr set, ref SP_DEVINFO_DATA did, int property)
    {
        byte[]? raw = GetRawProperty(set, ref did, property);
        if (raw is null || raw.Length == 0) return null;
        return System.Text.Encoding.Unicode.GetString(raw).TrimEnd('\0');
    }

    private static string[] GetMultiStringProperty(IntPtr set, ref SP_DEVINFO_DATA did, int property)
    {
        byte[]? raw = GetRawProperty(set, ref did, property);
        if (raw is null || raw.Length == 0) return Array.Empty<string>();
        return System.Text.Encoding.Unicode.GetString(raw)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static byte[]? GetRawProperty(IntPtr set, ref SP_DEVINFO_DATA did, int property)
    {
        SetupDiGetDeviceRegistryProperty(set, ref did, property, out _, null, 0, out int required);
        if (required <= 0) return null;

        var buffer = new byte[required];
        if (!SetupDiGetDeviceRegistryProperty(set, ref did, property, out _, buffer, buffer.Length, out _))
            return null; // property simply not present on this device

        return buffer;
    }
}
