
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using KageDock.Models;

namespace KageDock.Services
{
    public static class ConfigService
    {
        private static readonly string ConfigFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KageDock",
            "Config");

        private static readonly string ConfigPath =
            Path.Combine(ConfigFolder, "config.json");

        private static readonly string ShortcutsPath =
            Path.Combine(ConfigFolder, "shortcuts.json");

        private static readonly string LegacyConfigFolder =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        static ConfigService()
        {
            Directory.CreateDirectory(ConfigFolder);

            // Migrate existing files without overwriting anything
            // already present in the new location.
            MigrateLegacyFile("config.json");
            MigrateLegacyFile("shortcuts.json");
        }

        private static void MigrateLegacyFile(string fileName)
        {
            string oldPath = Path.Combine(LegacyConfigFolder, fileName);
            string newPath = Path.Combine(ConfigFolder, fileName);

            try
            {
                if (!File.Exists(newPath) && File.Exists(oldPath))
                {
                    File.Copy(oldPath, newPath, overwrite: false);
                }
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"Failed to migrate {fileName}: {ex}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"Access denied while migrating {fileName}: {ex}");
            }
        }

        public static DockConfig LoadConfig()
        {
            if (!File.Exists(ConfigPath))
            {
                var defaults = new DockConfig();
                SaveConfig(defaults);
                return defaults;
            }

            try
            {
                string json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<DockConfig>(json, JsonOptions)
                    ?? throw new InvalidDataException(
                        "The configuration file contains no valid data.");
            }
            catch (Exception primaryError) when (
                    primaryError is JsonException ||
                    primaryError is InvalidDataException ||
                    primaryError is IOException ||
                    primaryError is UnauthorizedAccessException)
            {
                DockConfig? backup = TryLoadBackup<DockConfig>(ConfigPath);

                if (backup != null)
                    return backup;

                throw new InvalidDataException(
                    $"Could not load '{ConfigPath}'. The original file was preserved.",
                    primaryError);
            }
        }

        public static void SaveConfig(DockConfig config)
        {
            SaveJson(ConfigPath, config);
        }

        
        public static List<AppShortcut> LoadShortcuts()
        {
            if (!File.Exists(ShortcutsPath))
            {
                // If the main file is missing, try the backup before creating defaults.
                List<AppShortcut>? backup =
                    TryLoadBackup<List<AppShortcut>>(ShortcutsPath);

                if (backup != null)
                {
                    SaveShortcuts(backup);
                    return backup;
                }

                var defaults = new List<AppShortcut>
                {
                    new AppShortcut
                    {
                        TargetPath = Path.Combine(
                            Environment.GetFolderPath(
                                Environment.SpecialFolder.System),
                            "notepad.exe"),
                        OrderIndex = 0
                    },
                    new AppShortcut
                    {
                        TargetPath = Path.Combine(
                            Environment.GetFolderPath(
                                Environment.SpecialFolder.System),
                            "cmd.exe"),
                        OrderIndex = 1
                    },
                    new AppShortcut
                    {
                        TargetPath = Path.Combine(
                            Environment.GetFolderPath(
                                Environment.SpecialFolder.System),
                            "taskmgr.exe"),
                        OrderIndex = 2
                    }
                };

                SaveShortcuts(defaults);
                return defaults;
            }

            try
            {
                string json = File.ReadAllText(ShortcutsPath);

                return JsonSerializer.Deserialize<List<AppShortcut>>(
                    json, JsonOptions)
                    ?? throw new InvalidDataException(
                        "The shortcuts file contains no valid data.");
            }
            catch (Exception primaryError) when (
                primaryError is JsonException ||
                primaryError is InvalidDataException ||
                primaryError is IOException ||
                primaryError is UnauthorizedAccessException)
            {
                List<AppShortcut>? backup =
                    TryLoadBackup<List<AppShortcut>>(ShortcutsPath);

                if (backup != null)
                    return backup;

                throw new InvalidDataException(
                    $"Could not load '{ShortcutsPath}'. " +
                    "The original file was preserved and no defaults were written.",
                    primaryError);
            }
        }


        public static void SaveShortcuts(List<AppShortcut> shortcuts)
        {
            SaveJson(ShortcutsPath, shortcuts);
        }

        private static T? TryLoadBackup<T>(string path) where T : class
        {
            string backupPath = path + ".bak";

            if (!File.Exists(backupPath))
                return null;

            try
            {
                string json = File.ReadAllText(backupPath);
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is JsonException)
            {
                return null;
            }
        }

        private static void SaveJson<T>(string path, T data)
        {
            Directory.CreateDirectory(ConfigFolder);

            string tempPath = path + ".tmp";
            string backupPath = path + ".bak";

            string json = JsonSerializer.Serialize(data, JsonOptions);

            // Write the complete new contents before touching the old file.
            File.WriteAllText(tempPath, json);

            try
            {
                if (File.Exists(path))
                {
                    // Preserve the last saved version before replacing it.
                    File.Copy(path, backupPath, overwrite: true);
                }

                // The temporary file is in the same folder, so replacement
                // stays on the same drive.
                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch (IOException ex)
                {
                    Debug.WriteLine($"Failed to clean up temporary config file: {ex}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    Debug.WriteLine($"Access denied while cleaning up temporary config file: {ex}");
                }
            }
        }
    }
}
