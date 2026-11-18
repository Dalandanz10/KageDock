using System;
using System.Collections.Generic;

namespace KageDock.Models
{
    public enum DockEdge
    {
        Right,
        Left
    }


    public class DockConfig
    {
        // Kage CreatorSecret persistence
        public DateTime CreatorSecretNextEligibleUtc { get; set; } = DateTime.MinValue;
        public List<string> CreatorSecretRemainingMessages { get; set; } = new();
        // Kage visibility settings
        public bool HideKageInFullscreen { get; set; } = true;
        // Kage behavior settings
        public bool EnableKageDialogue { get; set; } = true;
        public bool EnableEnvironmentalEffects { get; set; } = true;
        public DockEdge Edge { get; set; } = DockEdge.Right;
        public int TargetMonitor { get; set; } = 0;
        public double TriggerWidth { get; set; } = 4.0;
        public double ExpandedWidth { get; set; } = 430.0;
        public double IconSize { get; set; } = 34.0;
        public double SmokeIntensity { get; set; } = 0.0;
        public int OpenDelayMs { get; set; } = 60;
        public int CloseDelayMs { get; set; } = 280;

        // Branding is optional; blank title falls back to "My Apps".
        public string LauncherTitle { get; set; } = "My Apps";
        public string FooterText { get; set; } = "";
        public bool ShowFooterText { get; set; } = false;
    }
}
