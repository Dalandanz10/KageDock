
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace KageDock.Services
{
    public sealed class SteamGame
    {
        public string AppId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string InstallDirectory { get; init; } = string.Empty;
        public string ManifestPath { get; init; } = string.Empty;
        public string LibraryPath { get; init; } = string.Empty;
    }

    public static class SteamDiscoveryService
    {
        public static List<SteamGame> DiscoverInstalledGames()
        {
            var games = new Dictionary<string, SteamGame>(
                StringComparer.OrdinalIgnoreCase);

            foreach (string libraryPath in GetSteamLibraries())
            {
                string steamAppsPath = Path.Combine(
                    libraryPath, "steamapps");

                if (!Directory.Exists(steamAppsPath))
                    continue;

                IEnumerable<string> manifests;

                try
                {
                    manifests = Directory.EnumerateFiles(
                        steamAppsPath,
                        "appmanifest_*.acf",
                        SearchOption.TopDirectoryOnly).ToList();
                }
                catch
                {
                    continue;
                }

                foreach (string manifestPath in manifests)
                {
                    try
                    {
                        string content = File.ReadAllText(manifestPath);

                        string appId = GetVdfValue(content, "appid");
                        string name = GetVdfValue(content, "name");
                        string installDir = GetVdfValue(content, "installdir");

                        if (string.IsNullOrWhiteSpace(appId) ||
                            string.IsNullOrWhiteSpace(name) ||
                            string.IsNullOrWhiteSpace(installDir))
                            continue;

                        // Skip Steam's shared runtime component.
                        if (appId == "228980")
                            continue;

                        string installPath = Path.GetFullPath(
                            Path.Combine(
                                steamAppsPath,
                                "common",
                                installDir));

                        if (!Directory.Exists(installPath))
                            continue;

                        if (games.ContainsKey(appId))
                            continue;

                        games.Add(appId, new SteamGame
                        {
                            AppId = appId,
                            Name = name,
                            InstallDirectory = installPath,
                            ManifestPath = manifestPath,
                            LibraryPath = libraryPath
                        });
                    }
                    catch
                    {
                        // Ignore invalid or inaccessible manifests.
                    }
                }
            }

            return games.Values
                .OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static IEnumerable<string> GetSteamLibraryCacheDirectories()
        {
            foreach (string steamPath in GetSteamInstallPaths())
            {
                string cachePath = Path.Combine(
                    steamPath,
                    "appcache",
                    "librarycache");

                if (Directory.Exists(cachePath))
                    yield return cachePath;
            }
        }

        private static HashSet<string> GetSteamLibraries()
        {
            var libraries = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (string steamPath in GetSteamInstallPaths())
            {
                if (!Directory.Exists(steamPath))
                    continue;

                // The main Steam installation is also a library.
                libraries.Add(steamPath);

                string libraryFile = Path.Combine(
                    steamPath,
                    "steamapps",
                    "libraryfolders.vdf");

                if (!File.Exists(libraryFile))
                    continue;

                try
                {
                    string content = File.ReadAllText(libraryFile);

                    var matches = Regex.Matches(
                        content,
                        "\"path\"\\s*\"((?:\\\\.|[^\"\\\\])*)\"",
                        RegexOptions.IgnoreCase);

                    foreach (Match match in matches)
                    {
                        if (!match.Success)
                            continue;

                        string path = UnescapeVdfString(
                            match.Groups[1].Value);

                        if (string.IsNullOrWhiteSpace(path))
                            continue;

                        path = Environment.ExpandEnvironmentVariables(path);

                        if (Directory.Exists(path))
                            libraries.Add(Path.GetFullPath(path));
                    }
                }
                catch
                {
                    // Keep the libraries already discovered.
                }
            }

            return libraries;
        }

        private static IEnumerable<string> GetSteamInstallPaths()
        {
            var paths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            // Steam registry locations.
            string[] registryKeys =
            {
                @"Software\Valve\Steam",
                @"SOFTWARE\WOW6432Node\Valve\Steam",
                @"SOFTWARE\Valve\Steam"
            };

            foreach (string keyPath in registryKeys)
            {
                try
                {
                    using var key =
                        Registry.CurrentUser.OpenSubKey(keyPath);

                    AddRegistryPath(paths, key, "SteamPath");
                    AddRegistryPath(paths, key, "InstallPath");
                }
                catch
                {
                    // Continue checking other locations.
                }

                try
                {
                    using var key =
                        Registry.LocalMachine.OpenSubKey(keyPath);

                    AddRegistryPath(paths, key, "SteamPath");
                    AddRegistryPath(paths, key, "InstallPath");
                }
                catch
                {
                    // Continue checking other locations.
                }
            }

            // Common installation locations as fallback.
            string[] fallbackPaths =
            {
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.ProgramFilesX86),
                    "Steam"),

                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.ProgramFiles),
                    "Steam")
            };

            foreach (string path in fallbackPaths)
            {
                if (Directory.Exists(path))
                    paths.Add(Path.GetFullPath(path));
            }

            return paths;
        }

        private static void AddRegistryPath(
            HashSet<string> paths,
            RegistryKey? key,
            string valueName)
        {
            if (key == null)
                return;

            string? value = key.GetValue(valueName) as string;

            if (!string.IsNullOrWhiteSpace(value) &&
                Directory.Exists(value))
            {
                paths.Add(Path.GetFullPath(value));
            }
        }

        private static string GetVdfValue(
            string content,
            string key)
        {
            var match = Regex.Match(
                content,
                "\"" + Regex.Escape(key) +
                "\"\\s*\"((?:\\\\.|[^\"\\\\])*)\"",
                RegexOptions.IgnoreCase);

            return match.Success
                ? UnescapeVdfString(match.Groups[1].Value)
                : string.Empty;
        }

        private static string UnescapeVdfString(string value)
        {
            var result = new System.Text.StringBuilder(value.Length);

            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length)
                {
                    i++;
                    result.Append(value[i]);
                }
                else
                {
                    result.Append(value[i]);
                }
            }

            return result.ToString();
        }
    }
}
