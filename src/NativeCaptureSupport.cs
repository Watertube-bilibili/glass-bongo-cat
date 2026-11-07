using System;
using System.Runtime.InteropServices;

namespace GlassBongoCat
{
    /// <summary>
    /// OS capability gate for the native sampler. Windows before 10 build 19041
    /// interpret 0x11 as WDA_MONITOR and produce a black window, so exact affinity
    /// readback alone is insufficient. Runtime DWM/affinity checks still happen
    /// for every capture; this caches only the process-invariant OS version.
    /// </summary>
    internal static class NativeCaptureSupport
    {
        private static readonly bool supported;
        private static readonly string reason;
        internal static bool IsSupported { get { return supported; } }
        internal static string FailureReason { get { return reason; } }

        static NativeCaptureSupport()
        {
            try
            {
                // RTL_OSVERSIONINFOW: 5 DWORDs followed by WCHAR[128]. Read the
                // actual kernel version, unaffected by compatibility manifests.
                VersionData version = new VersionData();
                version.Size = (uint)Marshal.SizeOf(typeof(VersionData));
                if (RtlGetVersion(ref version) != 0)
                { reason = "无法确认 Windows 版本，已保留透明玻璃。"; return; }
                supported = version.Major > 10 || version.Major == 10 && version.Build >= 19041;
                reason = supported ? "" : "实时桌面折射需要 Windows 10 2004 或更新版本。";
            }
            catch (DllNotFoundException) { reason = "当前系统缺少 Windows 原生背景捕获接口。"; }
            catch (EntryPointNotFoundException) { reason = "当前系统无法确认背景捕获能力。"; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct VersionData
        {
            internal uint Size, Major, Minor, Build, Platform;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string ServicePack;
        }
        [DllImport("ntdll.dll", CharSet = CharSet.Unicode)] private static extern int RtlGetVersion(ref VersionData version);
    }
}
