
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace KageDock.Services
{
    public static class IconExtractorService
    {
        private const string AppsFolderPrefix = @"shell:AppsFolder\";

        private const uint SHGFI_ICON = 0x00000100;
        private const uint SHGFI_LARGEICON = 0x00000000;

        private const uint SIIGBF_ICONONLY = 0x00000004;
        private const uint SIIGBF_BIGGERSIZEOK = 0x00000001;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;

            public SIZE(int width, int height)
            {
                cx = width;
                cy = height;
            }
        }

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            void GetImage(
                SIZE size,
                uint flags,
                out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(
            string pszPath,
            IntPtr pbc,
            ref Guid riid,
            out IShellItemImageFactory ppv);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            out SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint ExtractIconEx(
            string lpszFile,
            int nIconIndex,
            out IntPtr phiconLarge,
            out IntPtr phiconSmall,
            uint nIcons);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
        private static BitmapSource? GetPackagedAppLogo(string appIdentity)
        {
            try
            {
                // Example identity:
                // shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App
                string identity = appIdentity.StartsWith(
                    AppsFolderPrefix,
                    StringComparison.OrdinalIgnoreCase)
                        ? appIdentity.Substring(AppsFolderPrefix.Length)
                        : appIdentity;

                int separator = identity.IndexOf('!');
                if (separator < 0)
                    return null;

                string familyName = identity.Substring(0, separator);
                
                string? installPath = GetPackageInstallPath(familyName);

                if (string.IsNullOrWhiteSpace(installPath))
                    return null;

                // Netflix: prefer its official red N logo.
                if (familyName.StartsWith(
                        "4DF9E0F8.Netflix_",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        string netflixLogo = Path.Combine(
                            installPath,
                            "Images",
                            "Square44x44Logo.scale-200.png");

                        if (File.Exists(netflixLogo))
                        {
                            var netflixBitmap = new BitmapImage();

                            netflixBitmap.BeginInit();
                            netflixBitmap.UriSource = new Uri(netflixLogo);
                            netflixBitmap.DecodePixelWidth = 96;
                            netflixBitmap.DecodePixelHeight = 96;
                            netflixBitmap.CacheOption = BitmapCacheOption.OnLoad;
                            netflixBitmap.EndInit();

                            var cropped = new CroppedBitmap(
                            netflixBitmap,
                            new Int32Rect(
                                (int)(netflixBitmap.PixelWidth * 0.15),
                                (int)(netflixBitmap.PixelHeight * 0.15),
                                (int)(netflixBitmap.PixelWidth * 0.70),
                                (int)(netflixBitmap.PixelHeight * 0.70)
                            )
                        );

                        if (cropped.CanFreeze)
                            cropped.Freeze();

                        return cropped;
                        }
                    }

                var candidates = Directory
                    .EnumerateFiles(installPath, "*.png", SearchOption.AllDirectories)
                    .Where(path =>
                    {
                        string name = Path.GetFileName(path);

                        return name.Contains(
                                "AppList",
                                StringComparison.OrdinalIgnoreCase)
                            && name.Contains(
                                "unplated",
                                StringComparison.OrdinalIgnoreCase)
                            && !name.Contains(
                                "light",
                                StringComparison.OrdinalIgnoreCase);
                    })
                    .OrderByDescending(path =>
                        Path.GetFileName(path).Contains(
                            "targetsize-48",
                            StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(path =>
                        Path.GetFileName(path).Contains(
                            "targetsize-44",
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (string path in candidates)
                {
                    try
                    {
                        var bitmap = new BitmapImage();

                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(path);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();

                        if (bitmap.CanFreeze)
                            bitmap.Freeze();

                        return bitmap;
                    }
                    catch
                    {
                        // Try the next matching logo.
                    }
                }
            }
            catch
            {
                // Use the existing Windows Shell fallback.
            }

            return null;
        }
        public static BitmapSource? GetIcon(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            // Microsoft Store / packaged apps.
            if (filePath.StartsWith(
                    AppsFolderPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return GetPackagedAppLogo(filePath)
                ?? GetAppsFolderIcon(filePath);
            }

            // Normal desktop apps and shortcuts.
            if (!File.Exists(filePath))
            {
                return null;
            }

            SHFILEINFO info;

            IntPtr result = SHGetFileInfo(
                filePath,
                0,
                out info,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_LARGEICON);

            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            {
                IntPtr largeIcon = IntPtr.Zero;
                IntPtr smallIcon = IntPtr.Zero;

                try
                {
                    uint extracted = ExtractIconEx(
                        filePath,
                        0,
                        out largeIcon,
                        out smallIcon,
                        1);

                    IntPtr iconHandle = largeIcon != IntPtr.Zero
                        ? largeIcon
                        : smallIcon;

                    if (extracted == 0 || iconHandle == IntPtr.Zero)
                    {

                        return null;
                    }

                    BitmapSource fallbackBitmap =
                        Imaging.CreateBitmapSourceFromHIcon(
                            iconHandle,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromWidthAndHeight(64, 64));

                    if (fallbackBitmap.CanFreeze)
                        fallbackBitmap.Freeze();

                    return fallbackBitmap;
                }
                finally
                {
                    if (largeIcon != IntPtr.Zero)
                        DestroyIcon(largeIcon);

                    if (smallIcon != IntPtr.Zero)
                        DestroyIcon(smallIcon);
                }
            }

            try
            {
                BitmapSource bitmap = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(64, 64));

                if (bitmap.CanFreeze)
                    bitmap.Freeze();

                return bitmap;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        private static readonly Dictionary<string, string?> PackagePathCache =
                    new(StringComparer.OrdinalIgnoreCase);

        private static readonly object PackagePathLock = new();
        
        private static string? GetPackageInstallPath(string familyName)
        {
            lock (PackagePathLock)
            {
                if (PackagePathCache.TryGetValue(familyName, out string? cachedPath))
                    return cachedPath;
            }

            string escapedFamily = familyName.Replace("'", "''");
            string? installPath = null;

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments =
                            "-NoProfile -NonInteractive -Command " +
                            "\"$p = Get-AppxPackage | Where-Object " +
                            "{ $_.PackageFamilyName -eq '" + escapedFamily +
                            "' } | Select-Object -First 1 -ExpandProperty InstallLocation; " +
                            "if ($p) { [Console]::WriteLine($p) }\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                installPath = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                if (process.ExitCode != 0 ||
                    string.IsNullOrWhiteSpace(installPath) ||
                    !Directory.Exists(installPath))
                {
                    installPath = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[KageDock] Package lookup failed: {ex.Message}");
            }

            lock (PackagePathLock)
            {
                PackagePathCache[familyName] = installPath;
            }

            return installPath;
        }

        private static BitmapSource? GetAppsFolderIcon(string appIdentity)
        {
            IShellItemImageFactory? factory = null;
            IntPtr hBitmap = IntPtr.Zero;

            try
            {
                // Convert shell:AppsFolder\... to a parsing name Windows Shell understands.
                Guid iid = typeof(IShellItemImageFactory).GUID;

                int hr = SHCreateItemFromParsingName(
                    appIdentity,
                    IntPtr.Zero,
                    ref iid,
                    out factory);

                if (hr < 0 || factory == null)
                    return null;

                factory.GetImage(
                    new SIZE(256, 256),
                    SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK,
                    out hBitmap);

                if (hBitmap == IntPtr.Zero)
                    return null;

                BitmapSource bitmap = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                if (bitmap.CanFreeze)
                    bitmap.Freeze();

                return bitmap;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (hBitmap != IntPtr.Zero)
                    DeleteObject(hBitmap);

                if (factory != null && Marshal.IsComObject(factory))
                    Marshal.FinalReleaseComObject(factory);
            }
        }
    }
}
