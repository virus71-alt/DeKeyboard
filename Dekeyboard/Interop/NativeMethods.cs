using System.Runtime.InteropServices;

namespace Dekeyboard.Interop;

/// <summary>
/// Raw P/Invoke declarations for SetupAPI (device enumeration + state change) and
/// the low-level keyboard hook used by the global-hotkey engine.
///
/// The enable/disable path is exactly what Device Manager itself uses:
///   SetupDiSetClassInstallParams(DIF_PROPERTYCHANGE, DICS_ENABLE|DICS_DISABLE)
///   + SetupDiCallClassInstaller(DIF_PROPERTYCHANGE)
/// This is fully reversible and does NOT uninstall the driver or require a reboot.
/// </summary>
internal static class NativeMethods
{
    // ---- Device setup class GUIDs (from devguid.h) --------------------------------
    // NOTE: the suffix is BFC1, not BFC8. These classic classes share the tail
    // 11CE-BFC1-08002BE10318; using BFC8 makes SetupDiGetClassDevs return nothing.
    public static readonly Guid GUID_DEVCLASS_KEYBOARD = new("4D36E96B-E325-11CE-BFC1-08002BE10318");
    public static readonly Guid GUID_DEVCLASS_MOUSE    = new("4D36E96F-E325-11CE-BFC1-08002BE10318");

    // ---- SetupDiGetClassDevs flags ------------------------------------------------
    public const int DIGCF_PRESENT = 0x00000002;

    // ---- Device registry property ids ---------------------------------------------
    public const int SPDRP_DEVICEDESC   = 0x00000000;
    public const int SPDRP_HARDWAREID   = 0x00000001;
    public const int SPDRP_FRIENDLYNAME = 0x0000000C;
    public const int SPDRP_ENUMERATOR   = 0x00000016; // SPDRP_ENUMERATOR_NAME

    // ---- Class-install / state-change constants -----------------------------------
    public const int DIF_PROPERTYCHANGE = 0x00000012;
    public const int DICS_ENABLE        = 0x00000001;
    public const int DICS_DISABLE       = 0x00000002;
    public const int DICS_FLAG_GLOBAL   = 0x00000001;

    public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_CLASSINSTALL_HEADER
    {
        public int cbSize;
        public int InstallFunction;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_PROPCHANGE_PARAMS
    {
        public SP_CLASSINSTALL_HEADER ClassInstallHeader;
        public int StateChange;
        public int Scope;
        public int HwProfile;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid, string? enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        char[]? deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        int property, out int propertyRegDataType,
        byte[]? propertyBuffer, int propertyBufferSize, out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiSetClassInstallParams(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        ref SP_PROPCHANGE_PARAMS classInstallParams, int classInstallParamsSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiCallClassInstaller(
        int installFunction, IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData);

    // ---- Low-level keyboard hook (global hotkeys) ---------------------------------
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN     = 0x0100;
    public const int WM_KEYUP       = 0x0101;
    public const int WM_SYSKEYDOWN  = 0x0104;
    public const int WM_SYSKEYUP    = 0x0105;

    // KBDLLHOOKSTRUCT.flags bit: event was injected by SendInput (on-screen / touch
    // keyboard, remote desktop, automation). Real hardware keys do NOT set this.
    public const uint LLKHF_INJECTED = 0x00000010;

    public const int VK_SHIFT   = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU    = 0x12; // ALT
    public const int VK_LWIN    = 0x5B;
    public const int VK_RWIN    = 0x5C;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr SetWindowsHookEx(
        int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    // ---- Convertible / tablet posture ---------------------------------------------
    // SM_CONVERTIBLESLATEMODE: 0 = slate/tablet (folded) OR device doesn't support it,
    // non-zero = laptop mode. Because 0 is ambiguous, callers must act on TRANSITIONS
    // only, never on the startup value.
    public const int SM_CONVERTIBLESLATEMODE = 0x2003;

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    // Display orientation fallback: when the convertible-slate signal isn't reported,
    // we detect the fold via the screen auto-rotating (EnumDisplaySettings ->
    // dmDisplayOrientation). 0 = DMDO_DEFAULT (landscape), 1/2/3 = rotated.
    public const int ENUM_CURRENT_SETTINGS = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    // ---- CfgMgr32: walk the device tree to find a device's real bus ---------------
    // Used to tell an INTERNAL HID keyboard (parent = ACPI/I2C) apart from an
    // EXTERNAL USB HID keyboard (an ancestor enumerator is USB / Bluetooth).
    public const int CR_SUCCESS = 0;
    public const int CM_LOCATE_DEVNODE_NORMAL = 0;

    // Device-node status flag: the device can be disabled at all. When this is clear,
    // SetupDiCallClassInstaller(DICS_DISABLE) will always fail (Device Manager greys
    // out "Disable device"), so we must fall back to input blocking.
    public const uint DN_DISABLEABLE = 0x00002000;
    public const uint DN_HAS_PROBLEM = 0x00000400;
    public const uint CM_PROB_DISABLED = 22; // device is disabled

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Get_DevNode_Status(
        out uint pulStatus, out uint pulProblemNumber, uint dnDevInst, int ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, int ulFlags);

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, int ulFlags);

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Get_Device_ID_Size(out int pulLen, uint dnDevInst, int ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_Device_IDW(uint dnDevInst, char[] buffer, int bufferLen, int ulFlags);
}
