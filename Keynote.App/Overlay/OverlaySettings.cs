using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace Keynote.App.Overlay
{
    public class OverlaySettings
    {
        public double Left { get; set; } = 200;
        public double Top { get; set; } = 100;
        public double ExpandedWidth { get; set; } = 920;
        public double ExpandedHeight { get; set; } = 440;
        public bool IsCompact { get; set; } = false;
        public bool IsPinned { get; set; } = true;
        public double GlassOpacity { get; set; } = 0.97;

        private static string SettingsFilePath
        {
            get
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KEYNOTE");
                Directory.CreateDirectory(folder);
                return Path.Combine(folder, "overlay-settings.json");
            }
        }

        public static OverlaySettings Load()
        {
            try
            {
                string path = SettingsFilePath;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var settings = JsonSerializer.Deserialize<OverlaySettings>(json);
                    if (settings != null)
                    {
                        settings.ValidateAndAdjustBounds();
                        return settings;
                    }
                }
            }
            catch
            {
                // Fall back to defaults on corruption or read failure
            }

            var fallback = new OverlaySettings();
            fallback.CenterOnPrimaryScreen();
            return fallback;
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
                // Silently ignore save errors during shutdown
            }
        }

        public void CenterOnPrimaryScreen()
        {
            double screenW = SystemParameters.PrimaryScreenWidth;
            double screenH = SystemParameters.PrimaryScreenHeight;
            ExpandedWidth = 920;
            ExpandedHeight = 440;
            Left = Math.Max(20, (screenW - ExpandedWidth) / 2);
            Top = 100;
        }

        public void ValidateAndAdjustBounds()
        {
            double virtualLeft = SystemParameters.VirtualScreenLeft;
            double virtualTop = SystemParameters.VirtualScreenTop;
            double virtualWidth = SystemParameters.VirtualScreenWidth;
            double virtualHeight = SystemParameters.VirtualScreenHeight;

            ExpandedWidth = Math.Clamp(ExpandedWidth > 0 ? ExpandedWidth : 920, 640, 1400);
            ExpandedHeight = Math.Clamp(ExpandedHeight > 0 ? ExpandedHeight : 440, 360, 800);
            GlassOpacity = Math.Clamp(GlassOpacity > 0 ? GlassOpacity : 0.97, 0.85, 0.99);

            double w = IsCompact ? 760 : ExpandedWidth;
            double h = IsCompact ? 96 : ExpandedHeight;

            bool isVisible = (Left + w > virtualLeft + 50) &&
                             (Left < virtualLeft + virtualWidth - 50) &&
                             (Top + h > virtualTop + 50) &&
                             (Top < virtualTop + virtualHeight - 50);

            if (!isVisible)
            {
                CenterOnPrimaryScreen();
            }
        }
    }
}
