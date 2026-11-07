using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GlassBongoCat
{
    /// <summary>Read-only session gate. Never unlocks, activates or switches a desktop.</summary>
    internal sealed class DesktopSessionState : IDisposable
    {
        internal struct Probe { internal bool Active; internal string Reason; }
        private readonly IntPtr hwnd;
        private readonly Func<Probe> reader;
        private readonly Func<double> clock;
        private bool registered, disposed, active;
        private double refreshed;
        internal string Reason { get; private set; }
        internal bool IsActive
        {
            get { if (!disposed && clock() - refreshed >= 1000) Refresh(); return !disposed && active; }
        }

        internal DesktopSessionState(IntPtr window) : this(window, ReadSession, Now) { }
        // Injected reader and clock let lock/unlock transitions be tested without
        // changing the user's actual session or sending system messages.
        internal DesktopSessionState(IntPtr window, Func<Probe> query, Func<double> time)
        {
            hwnd = window; reader = query; clock = time;
            if (hwnd != IntPtr.Zero) registered = WTSRegisterSessionNotification(hwnd, 0);
            Refresh();
        }
        internal bool ProcessMessage(int message, IntPtr wParam)
        {
            if (message != 0x02B1 || disposed) return false;
            int kind = wParam.ToInt32();
            if (kind == 7 || kind == 4 || kind == 2 || kind == 6)
            {
                active = false; Reason = kind == 7 ? "Windows 会话已锁定，桌面采样与绘制已暂停。" : "Windows 会话未处于活动连接，桌面采样与绘制已暂停。";
                refreshed = clock();
            }
            else Refresh();
            return true;
        }
        private void Refresh() { Probe p = reader(); active = p.Active; Reason = p.Reason; refreshed = clock(); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (registered) WTSUnRegisterSessionNotification(hwnd);
            registered = false;
        }

        internal static bool QueryActive(out string reason)
        { Probe result = ReadSession(); reason = result.Reason; return result.Active; }
        private static Probe ReadSession()
        {
            IntPtr buffer = IntPtr.Zero; int bytes;
            try
            {
                if (!WTSQuerySessionInformation(IntPtr.Zero, -1, 25, out buffer, out bytes))
                    return new Probe { Active = false, Reason = "无法确认 Windows 会话状态，桌面采样已暂停。" };
                return Decode(buffer, bytes);
            }
            finally { if (buffer != IntPtr.Zero) WTSFreeMemory(buffer); }
        }
        internal static Probe Decode(IntPtr buffer, int bytes)
        {
            // WTSINFOEXW is DWORD Level, 4-byte alignment, then its level-1
            // union (8-byte aligned by LARGE_INTEGER members). Read only the
            // three status fields; user/domain/station strings stay unread.
            if (buffer == IntPtr.Zero || bytes < 20 || Marshal.ReadInt32(buffer, 0) != 1)
                return new Probe { Active = false, Reason = "Windows 会话状态格式不可用，桌面采样已暂停。" };
            int state = Marshal.ReadInt32(buffer, 12), flags = Marshal.ReadInt32(buffer, 16);
            if (flags == 0) return new Probe { Active = false, Reason = "Windows 会话已锁定，桌面采样与绘制已暂停。" };
            if (state != 0) return new Probe { Active = false, Reason = "Windows 会话未处于活动连接，桌面采样与绘制已暂停。" };
            if (flags != 1) return new Probe { Active = false, Reason = "Windows 会话状态未知，桌面采样已暂停。" };
            return new Probe { Active = true, Reason = "" };
        }
        private static double Now() { return Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency); }
        [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)] private static extern bool WTSQuerySessionInformation(IntPtr server, int session, int info, out IntPtr buffer, out int bytes);
        [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr buffer);
        [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);
        [DllImport("wtsapi32.dll")] private static extern bool WTSUnRegisterSessionNotification(IntPtr window);
    }
}
