$appData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$settingsPath = Join-Path $appData "LarpianoOverlayPrototype\settings.json"

$compactSettings = @{
    Left = 300
    Top = 150
    ExpandedWidth = 920
    ExpandedHeight = 440
    IsCompact = $true
    IsPinned = $true
    Opacity = 0.94
    PlaybackSpeed = 1.25
    Volume = 0.85
} | ConvertTo-Json

Set-Content -Path $settingsPath -Value $compactSettings
Write-Host "Saved compact settings."

$exePath = "D:\projects\larpiano\larpiano-overlay-prototype\publish\Larpiano.OverlayPrototype.exe"
$proc = Start-Process $exePath -PassThru
Start-Sleep -Seconds 2

Add-Type @"
using System;
using System.Runtime.InteropServices;

public class WinBoundsCompact
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
"@

$rect = New-Object WinBoundsCompact+RECT
[WinBoundsCompact]::GetWindowRect($proc.MainWindowHandle, [ref]$rect)
$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top
Write-Host "Compact Window Dimensions: Width=$w, Height=$h"

$isCompactSize = ($w -ge 700 -and $w -le 800 -and $h -ge 70 -and $h -le 130)
Write-Host "Compact Mode Dimension Verified: $isCompactSize"

Stop-Process -Id $proc.Id -Force
