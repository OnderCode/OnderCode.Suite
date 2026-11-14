using System.Runtime.InteropServices;

namespace Donanim.Windows;

/// <summary>Anlık işlemci ve bellek kullanımı (GetSystemTimes, GlobalMemoryStatusEx). Ek paket gerektirmez.</summary>
public sealed class LiveUsage
{
    long _idle, _kernel, _user;

    public LiveUsage() => Sample(out _idle, out _kernel, out _user);

    /// <summary>Son çağrıdan bu yana işlemci kullanımı (0..1).</summary>
    public double Cpu()
    {
        Sample(out var idle, out var kernel, out var user);
        var di = idle - _idle;
        var total = kernel - _kernel + (user - _user);
        (_idle, _kernel, _user) = (idle, kernel, user);
        return total <= 0 ? 0 : Math.Clamp(1 - (double)di / total, 0, 1);
    }

    /// <summary>Kullanılan bellek oranı (0..1) ve kullanılan GB.</summary>
    public static (double Ratio, double UsedGb, double TotalGb) Memory()
    {
        var s = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref s)) return (0, 0, 0);
        var total = s.TotalPhys / 1024.0 / 1024 / 1024;
        var used = (s.TotalPhys - s.AvailPhys) / 1024.0 / 1024 / 1024;
        return (total > 0 ? used / total : 0, used, total);
    }

    static void Sample(out long idle, out long kernel, out long user)
    {
        if (!GetSystemTimes(out idle, out kernel, out user)) idle = kernel = user = 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
