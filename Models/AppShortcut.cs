using System.Text.Json.Serialization;
using System.Windows.Media;

namespace KageDock.Models
{
    public class AppShortcut
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public string WorkingDirectory { get; set; } = string.Empty;
        public string CustomWorkingDirectory { get; set; } = string.Empty;
        // Steam game information. Empty for non-Steam applications.
        public string SteamAppId { get; set; } = string.Empty;
        public string InstallDirectory { get; set; } = string.Empty;
        public int OrderIndex { get; set; }
        public int SlotIndex { get; set; } = -1;

        // Existing shortcuts default to Pinned when Category is absent.
        public string Category { get; set; } = "Pinned";

        [JsonIgnore]
        public ImageSource? CachedIcon { get; set; }
    }
}
