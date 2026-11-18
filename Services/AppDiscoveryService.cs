
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

public class DiscoveredApp
{
    public string Name { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string Source { get; set; } = "";
    public bool AlreadyAdded { get; set; }
}

public static class AppDiscoveryService
{
    private const string StorePrefix = "shell:AppsFolder\\";

    public static List<DiscoveredApp> DiscoverApps()
    {
        var apps = new List<DiscoveredApp>();

        // Regular desktop apps.
        ScanStartMenu(apps);

        // Registered desktop programs.
        ScanUninstallRegistry(RegistryHive.CurrentUser, RegistryView.Registry64, apps);
        ScanUninstallRegistry(RegistryHive.CurrentUser, RegistryView.Registry32, apps);
        ScanUninstallRegistry(RegistryHive.LocalMachine, RegistryView.Registry64, apps);
        ScanUninstallRegistry(RegistryHive.LocalMachine, RegistryView.Registry32, apps);

        // Microsoft Store / packaged apps.
        // ScanWindowsAppsFolder(apps);

        var candidates = apps
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .Where(a => !string.IsNullOrWhiteSpace(a.TargetPath))
            .Where(a => !IsUnwantedApp(a))
            .ToList();

        var startMenuApps = candidates
            .Where(a => string.Equals(
                a.Source, "Start Menu", StringComparison.OrdinalIgnoreCase))
            .GroupBy(
                a => $"{NormalizePath(a.TargetPath)}|{a.Name.Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var registryApps = candidates
            .Where(a => string.Equals(
                a.Source, "Installed Program", StringComparison.OrdinalIgnoreCase))
            .Where(registryApp => !startMenuApps.Any(startApp =>
                NormalizePath(startApp.TargetPath).Equals(
                    NormalizePath(registryApp.TargetPath),
                    StringComparison.OrdinalIgnoreCase) ||
                IsSameInstallation(startApp.TargetPath, registryApp.TargetPath)))
            .GroupBy(
                a => $"{NormalizePath(a.TargetPath)}|{a.Name.Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        return startMenuApps
            .Concat(registryApps)
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
    private static string NormalizeAppName(string name)
    {
        string normalized = name.Trim();

        // Remove a trailing dotted version, e.g. "1.5.6.5".
        return System.Text.RegularExpressions.Regex.Replace(
            normalized,
            @"\s+\d+(?:\.\d+){1,3}$",
            "").Trim();
    }

        private static bool IsSameInstallation(string firstPath, string secondPath)
    {
        if (firstPath.StartsWith(StorePrefix, StringComparison.OrdinalIgnoreCase) ||
            secondPath.StartsWith(StorePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            string firstDirectory = Path.GetDirectoryName(
                Path.GetFullPath(firstPath)) ?? "";

            string secondDirectory = Path.GetDirectoryName(
                Path.GetFullPath(secondPath)) ?? "";

            // Only treat paths as related when they are in the same directory
            // or one directory is a versioned subdirectory of the other.
            if (firstDirectory.Equals(
                    secondDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return firstDirectory.StartsWith(
                    secondDirectory.TrimEnd('\\') + "\\",
                    StringComparison.OrdinalIgnoreCase) ||
                secondDirectory.StartsWith(
                    firstDirectory.TrimEnd('\\') + "\\",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
        public static List<DiscoveredApp> DiscoverStoreApps()
    {
        var apps = new List<DiscoveredApp>();

        ScanWindowsAppsFolder(apps);

        return apps
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .Where(a => !string.IsNullOrWhiteSpace(a.TargetPath))
            .Where(a => !IsUnwantedApp(a))
            .GroupBy(
                a => NormalizePath(a.TargetPath),
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
    private static void ScanStartMenu(List<DiscoveredApp> apps)
    {
        string[] folders =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };

        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
            return;

        foreach (string folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                continue;

            foreach (string shortcut in SafeFiles(folder, "*.lnk"))
            {
                object? shell = null;
                object? link = null;

                try
                {
                    shell = Activator.CreateInstance(shellType);
                    if (shell == null)
                        continue;

                    link = shellType.InvokeMember(
                        "CreateShortcut",
                        System.Reflection.BindingFlags.InvokeMethod,
                        null,
                        shell,
                        new object[] { shortcut });

                    if (link == null)
                        continue;

                    Type linkType = link.GetType();

                    string target = ReadComString(linkType, link, "TargetPath");
                    string arguments = ReadComString(linkType, link, "Arguments");
                    string name = Path.GetFileNameWithoutExtension(shortcut);

                    if (string.IsNullOrWhiteSpace(target))
                        continue;

                    if (!File.Exists(target))
                        continue;

                    if (!string.Equals(Path.GetExtension(target), ".exe",
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    apps.Add(new DiscoveredApp
                    {
                        Name = name,
                        TargetPath = target,
                        Arguments = arguments,
                        Source = "Start Menu"
                    });
                }
                catch
                {
                    // Ignore shortcuts Windows cannot resolve.
                }
                finally
                {
                    ReleaseComObject(link);
                    ReleaseComObject(shell);
                }
            }
        }
    }


    private static void ScanWindowsAppsFolder(List<DiscoveredApp> apps)
    {
        Type? shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType == null)
            return;

        object? shell = null;
        object? folder = null;
        object? items = null;

        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell == null)
                return;

            folder = shellType.InvokeMember(
                "NameSpace",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { "shell:AppsFolder" });

            if (folder == null)
                return;

            Type folderType = folder.GetType();

            items = folderType.InvokeMember(
                "Items",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                folder,
                null);

            if (items == null)
                return;

            Type itemsType = items.GetType();

            int count = Convert.ToInt32(
                itemsType.InvokeMember(
                    "Count",
                    System.Reflection.BindingFlags.GetProperty,
                    null,
                    items,
                    null));

            for (int i = 0; i < count; i++)
            {
                object? item = null;

                try
                {
                    item = itemsType.InvokeMember(
                        "Item",
                        System.Reflection.BindingFlags.InvokeMethod,
                        null,
                        items,
                        new object[] { i });

                    if (item == null)
                        continue;

                    Type itemType = item.GetType();

                    string name = ReadComString(itemType, item, "Name");
                    string appId = ReadComString(itemType, item, "Path");

                    if (string.IsNullOrWhiteSpace(name) ||
                        string.IsNullOrWhiteSpace(appId))
                        continue;

                    // AppsFolder items expose their registered app identity.
                    // Do not add ordinary filesystem paths a second time.
                    if (!appId.Contains('!'))
                        continue;

                    apps.Add(new DiscoveredApp
                    {
                        Name = name,
                        TargetPath = StorePrefix + appId,
                        Arguments = "",
                        Source = "Microsoft Store"
                    });
                }
                catch
                {
                    // Some AppsFolder entries do not expose usable properties.
                }
                finally
                {
                    ReleaseComObject(item);
                }
            }
        }
        catch
        {
            // Store app discovery is optional; desktop discovery still works.
        }
        finally
        {
            ReleaseComObject(items);
            ReleaseComObject(folder);
            ReleaseComObject(shell);
        }
    }

        

    private static void ScanUninstallRegistry(
        RegistryHive hive,
        RegistryView view,
        List<DiscoveredApp> apps)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? uninstall = baseKey.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");

            if (uninstall == null)
                return;

            foreach (string subKeyName in uninstall.GetSubKeyNames())
            {
                try
                {
                    using RegistryKey? key = uninstall.OpenSubKey(subKeyName);
                    if (key == null)
                        continue;

                    string name = key.GetValue("DisplayName") as string ?? "";
                    string displayIcon = key.GetValue("DisplayIcon") as string ?? "";
                    string installLocation = key.GetValue("InstallLocation") as string ?? "";
                    string uninstallString = key.GetValue("UninstallString") as string ?? "";

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    // Never use an uninstaller as the app's launch target.
                    string target = ResolveExecutable(displayIcon, installLocation);

                    if (string.IsNullOrWhiteSpace(target))
                        continue;

                    apps.Add(new DiscoveredApp
                    {
                        Name = name.Trim(),
                        TargetPath = target,
                        Arguments = "",
                        Source = "Installed Program"
                    });
                }
                catch
                {
                    // Skip inaccessible or malformed registry entries.
                }
            }
        }
        catch
        {
            // Registry view may be unavailable on some systems.
        }
    }

    private static string ResolveExecutable(string displayIcon, string installLocation)
    {
        if (!string.IsNullOrWhiteSpace(displayIcon))
        {
            string iconPath = displayIcon.Trim().Trim('"');

            // DisplayIcon may have an icon index after the executable path.
            if (iconPath.StartsWith('"'))
            {
                int endQuote = iconPath.IndexOf('"', 1);
                if (endQuote > 1)
                    iconPath = iconPath.Substring(1, endQuote - 1);
            }
            else
            {
                int comma = iconPath.LastIndexOf(',');
                if (comma > 0)
                    iconPath = iconPath.Substring(0, comma).Trim();
            }

            if (File.Exists(iconPath) &&
                string.Equals(Path.GetExtension(iconPath), ".exe",
                    StringComparison.OrdinalIgnoreCase))
            {
                return iconPath;
            }
        }

        if (!string.IsNullOrWhiteSpace(installLocation) &&
            Directory.Exists(installLocation))
        {
            try
            {
                string[] executables = Directory.GetFiles(
                    installLocation, "*.exe", SearchOption.TopDirectoryOnly);

                if (executables.Length == 1)
                    return executables[0];
            }
            catch
            {
                // Ignore inaccessible install folders.
            }
        }

        return "";
    }

    
        private static bool IsUnwantedApp(DiscoveredApp app)
        {
            string name = app.Name.Trim();
            string target = app.TargetPath;
            string fileName = Path.GetFileNameWithoutExtension(target);

            // Hide uninstallers and installer/cache executables.
            string normalizedTarget = target.Replace('/', '\\');

            if (fileName.Equals("uninst", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("uninstall", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("unins000", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("setup", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("installer", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (normalizedTarget.Contains(
                    @"\Package Cache\", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Hide obvious installers, runtimes, drivers, and support components.
            string[] blockedNames =
            {
                "Microsoft Visual C++",
                "Microsoft Windows Desktop Runtime",
                "Microsoft .NET Runtime",
                "Microsoft ASP.NET Core",
                "Visual Studio Installer",
                "Redistributable",
                "OpenOffice Calc",
                "OpenOffice Math",
                "OpenOffice Impress",
                "Realtek Audio Driver",
                "GIGABYTE Storage Library",
                "Tesseract OCR"
            };

            if (blockedNames.Any(x =>
                    name.Contains(x, StringComparison.OrdinalIgnoreCase)) ||
                name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Setup", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Installer", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Driver", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Update Service", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Runtime", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Hide Windows administration tools.
            string[] blockedSystemTools =
            {
                "dfrgui",
                "Disk Defragmenter",
                "Windows Memory Diagnostic",
                "Registry Editor",
                "Task Manager",
                "Command Prompt",
                "Windows PowerShell"
            };

            if (blockedSystemTools.Any(x =>
                    string.Equals(name, x, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fileName, x, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // Store apps use shell identities rather than filesystem paths.
            if (target.StartsWith(StorePrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            // Hide executables located directly inside the Windows directory.
            string windowsDir = Environment.GetFolderPath(
                Environment.SpecialFolder.Windows);

            try
            {
                string fullTarget = Path.GetFullPath(target);
                string fullWindows = Path.GetFullPath(windowsDir)
                    .TrimEnd(Path.DirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;

                if (fullTarget.StartsWith(
                        fullWindows, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
                return true;
            }

            return false;
        }


    private static IEnumerable<string> SafeFiles(string folder, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(
                folder, pattern, SearchOption.AllDirectories).ToArray();
        }
        catch
        {
            try
            {
                return Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }

    private static string ReadComString(Type type, object instance, string property)
    {
        try
        {
            return type.InvokeMember(
                property,
                System.Reflection.BindingFlags.GetProperty,
                null,
                instance,
                null) as string ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string NormalizePath(string path)
    {
        if (path.StartsWith(StorePrefix, StringComparison.OrdinalIgnoreCase))
            return path.Trim().ToLowerInvariant();

        try
        {
            return Path.GetFullPath(path).TrimEnd('\\').ToLowerInvariant();
        }
        catch
        {
            return path.Trim().ToLowerInvariant();
        }
    }

    private static void ReleaseComObject(object? obj)
    {
        if (obj != null && Marshal.IsComObject(obj))
        {
            try
            {
                Marshal.FinalReleaseComObject(obj);
            }
            catch
            {
                // Ignore COM cleanup errors.
            }
        }
    }
}
