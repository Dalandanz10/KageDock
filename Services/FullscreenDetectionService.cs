using System;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KageDock.Services
{
    public sealed class FullscreenDetectionService : IDisposable
    {
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;

        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private RECT? _targetMonitorBounds;
        private const int OBJID_WINDOW = 0;

        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        private readonly WinEventDelegate _eventCallback;

        private IntPtr _foregroundHook;
        private IntPtr _locationHook;
        private IntPtr _ignoredWindow;

        private bool _isFullscreen;
        private bool _started;
        private bool _disposed;

        public bool IsFullscreen => _isFullscreen;

        public string? ForegroundApplicationPath { get; private set; }

        public event Action<bool>? FullscreenStateChanged;

        public event Action<string?>? ForegroundApplicationChanged;

        public FullscreenDetectionService()
        {
            // Keep the delegate alive for the lifetime of the hooks.
            _eventCallback = OnWinEvent;
        }

        public void SetTargetMonitorBounds(Rectangle bounds)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _targetMonitorBounds = new RECT
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Right = bounds.Right,
                Bottom = bounds.Bottom
            };

            if (_started)
                Refresh();
        }

        public void Start(IntPtr ignoredWindow = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_started)
                return;

            _ignoredWindow = ignoredWindow;

            _foregroundHook = SetWinEventHook(
                EVENT_SYSTEM_FOREGROUND,
                EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _eventCallback,
                0,
                0,
                WINEVENT_OUTOFCONTEXT);

            _locationHook = SetWinEventHook(
                EVENT_OBJECT_LOCATIONCHANGE,
                EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero,
                _eventCallback,
                0,
                0,
                WINEVENT_OUTOFCONTEXT);

            if (_foregroundHook == IntPtr.Zero ||
                _locationHook == IntPtr.Zero)
            {
                DisposeHooks();

                throw new InvalidOperationException(
                    "Unable to initialize fullscreen detection hooks.");
            }

            _started = true;

            Refresh();
        }

        public void Refresh()
        {
            if (_disposed)
                return;

            IntPtr hwnd = GetForegroundWindow();

            string? applicationPath = GetForegroundApplicationPath(hwnd);

            if (!string.Equals(
                ForegroundApplicationPath,
                applicationPath,
                StringComparison.OrdinalIgnoreCase))
            {
                ForegroundApplicationPath = applicationPath;
                ForegroundApplicationChanged?.Invoke(applicationPath);
            }

            bool fullscreen = IsForegroundWindowFullscreen();

            if (_isFullscreen == fullscreen)
                return;

            _isFullscreen = fullscreen;
            FullscreenStateChanged?.Invoke(fullscreen);
        }

        private string? GetForegroundApplicationPath(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || hwnd == _ignoredWindow)
                return null;

            try
            {
                GetWindowThreadProcessId(hwnd, out uint processId);

                if (processId == 0)
                    return null;

                using Process process = Process.GetProcessById((int)processId);

                return process.MainModule?.FileName;
            }
            catch
            {
                // Some protected or elevated processes don't expose their executable path.
                return null;
            }
        }

        
        private void OnWinEvent(
            IntPtr hook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint eventThread,
            uint eventTime)
        {
            if (_disposed)
                return;

            try
            {
                if (eventType == EVENT_OBJECT_LOCATIONCHANGE)
                {
                    if (idObject != OBJID_WINDOW || idChild != 0)
                        return;

                    // Only location changes to the foreground window matter.
                    if (hwnd != GetForegroundWindow())
                        return;
                }

                Refresh();
            }
            catch (Exception ex)
            {
                // Never allow managed exceptions to escape into the Win32 callback.
                Debug.WriteLine(
                    $"Fullscreen detection callback failed: {ex}");
            }
        }


        private bool IsForegroundWindowFullscreen()
        {
            IntPtr hwnd = GetForegroundWindow();

            if (hwnd == IntPtr.Zero ||
                hwnd == _ignoredWindow ||
                !IsWindowVisible(hwnd) ||
                IsIconic(hwnd))
            {
                return false;
            }

            if (!GetWindowRect(hwnd, out RECT windowRect))
                return false;

            IntPtr monitor = MonitorFromWindow(
                hwnd,
                MONITOR_DEFAULTTONEAREST);

            if (monitor == IntPtr.Zero)
                return false;

            MONITORINFO monitorInfo = new()
            {
                cbSize = Marshal.SizeOf<MONITORINFO>()
            };

            if (!GetMonitorInfo(monitor, ref monitorInfo))
                return false;

            RECT monitorRect = monitorInfo.rcMonitor;

            // Only detect fullscreen applications on KageDock's monitor.
            if (_targetMonitorBounds is not RECT targetRect ||
                monitorRect.Left != targetRect.Left ||
                monitorRect.Top != targetRect.Top ||
                monitorRect.Right != targetRect.Right ||
                monitorRect.Bottom != targetRect.Bottom)
            {
                return false;
            }

            const int tolerance = 2;

            return Math.Abs(windowRect.Left - monitorRect.Left) <= tolerance &&
                Math.Abs(windowRect.Top - monitorRect.Top) <= tolerance &&
                Math.Abs(windowRect.Right - monitorRect.Right) <= tolerance &&
                Math.Abs(windowRect.Bottom - monitorRect.Bottom) <= tolerance;
        }

        private void DisposeHooks()
        {
            if (_foregroundHook != IntPtr.Zero)
            {
                UnhookWinEvent(_foregroundHook);
                _foregroundHook = IntPtr.Zero;
            }

            if (_locationHook != IntPtr.Zero)
            {
                UnhookWinEvent(_locationHook);
                _locationHook = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _started = false;

            DisposeHooks();

            FullscreenStateChanged = null;
            ForegroundApplicationChanged = null;
        }

        private delegate void WinEventDelegate(
            IntPtr hook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint eventThread,
            uint eventTime);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventDelegate callback,
            uint processId,
            uint threadId,
            uint flags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            IntPtr hwnd,
            out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(
            IntPtr hwnd,
            out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(
            IntPtr hwnd,
            uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(
            IntPtr monitor,
            ref MONITORINFO monitorInfo);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hwnd);
    }
}
