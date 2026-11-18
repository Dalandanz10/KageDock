
using System;
using System.Linq;
using System.Windows.Forms;

namespace KageDock.Services
{
    public static class ScreenService
    {
        public static Screen[] GetScreens()
        {
            // Always put the primary display at index 0.
            return Screen.AllScreens
                .OrderByDescending(screen => screen.Primary)
                .ToArray();
        }

        public static int GetScreenCount()
        {
            return GetScreens().Length;
        }

        public static Screen GetScreen(int monitorIndex)
        {
            var screens = GetScreens();

            if (screens.Length == 0)
            {
                return Screen.PrimaryScreen
                    ?? throw new InvalidOperationException(
                        "No available display was detected.");
            }

            if (monitorIndex >= 0 && monitorIndex < screens.Length)
                return screens[monitorIndex];

            // Invalid saved index: safely fall back to the primary display.
            return screens[0];
        }
    }
}
