using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Zfl.App;

static class SystemInfo
{
    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx m);

    public static long TotalRam()
    {
        var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref m) ? (long)m.TotalPhys : 8L << 30;
    }

    /// En büyük ekran kartı belleği (MiB); bulunamazsa 0.
    public static long VramMiB()
    {
        using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        if (cls is null) return 0;
        long best = 0;
        foreach (var name in cls.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
        {
            try
            {
                using var k = cls.OpenSubKey(name);
                long bytes = k?.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long q => q,
                    byte[] b when b.Length >= 8 => BitConverter.ToInt64(b),
                    _ => k?.GetValue("HardwareInformation.MemorySize") switch
                    {
                        int d => (uint)d,
                        byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b),
                        _ => 0,
                    },
                };
                best = Math.Max(best, bytes >> 20);
            }
            catch (System.Security.SecurityException) { }
        }
        return best;
    }

    /// Ekran kartları: Windows'un görüntü bağdaştırıcısı sınıfındaki ad + sürücü sürümü.
    public static IEnumerable<string> Gpus()
    {
        using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        if (cls is null) yield break;
        foreach (var name in cls.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
        {
            using var k = cls.OpenSubKey(name);
            if (k?.GetValue("DriverDesc") is string desc)
                yield return $"{desc}, sürücü {k.GetValue("DriverVersion")}";
        }
    }
}
