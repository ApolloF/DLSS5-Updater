using System.Runtime.InteropServices;

namespace DLSSUpdater.Core;

/// <summary>The refresh rate of the primary display, for the frame-limit hint. 0 when Windows doesn't say.</summary>
public static class DisplayRefresh
{
    private const int CurrentSettings = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int PositionX, PositionY;
        public uint DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public uint IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode mode);

    public static int Hz()
    {
        try
        {
            var mode = new DevMode { Size = (ushort)Marshal.SizeOf<DevMode>() };
            // 0 and 1 mean "hardware default": not a real rate.
            return EnumDisplaySettings(null, CurrentSettings, ref mode) && mode.DisplayFrequency > 1 ? (int)mode.DisplayFrequency : 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException) { return 0; }
    }
}
