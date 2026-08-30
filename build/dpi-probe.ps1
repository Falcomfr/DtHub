# Mesure la hauteur reellement dessinee par le jeu selon la densite demandee.
#
# La zone client est calee exactement sur le rapport de l'afficheur : la mise
# en boite de scrcpy est alors nulle, et toute bande observee vient du jeu.
# Outil de developpement uniquement.
param(
    [string]$Serial,
    [int]$Dpi = 240,
    [int]$Width = 2760,
    [int]$Height = 2000,
    [int]$ClientHeight = 1000,
    [int]$ClientWidth = 0,
    [int]$Settle = 25,
    [switch]$Flex)

$root   = "$env:LOCALAPPDATA\DtHub\tools\scrcpy-4.1.0\scrcpy-win64-v4.1"
# Jamais de numero de serie en dur : le premier appareil joignable fait foi.
if (-not $Serial) {
    $Serial = (& "$root\adb.exe" devices | Select-String '\tdevice$' | Select-Object -First 1).ToString().Split("`t")[0]
}
if (-not $Serial) { 'ECHEC : aucun appareil joignable'; exit 1 }
$serial = $Serial
$tag    = "$Width-$Height-$Dpi-$ClientHeight"
$log    = "C:\Dev\DTHub\build\dpi-$tag.log"
$png    = "C:\Dev\DTHub\build\dpi-$tag.png"

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Runtime.InteropServices;using System.Text;
public class Q {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int m);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static IntPtr Find(string title) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h,l) => { if (!IsWindowVisible(h)) return true;
      var sb = new StringBuilder(512); GetWindowTextW(h, sb, sb.Capacity);
      if (sb.ToString().Contains(title)) { found = h; return false; } return true; }, IntPtr.Zero);
    return found; }
}
"@
[void][Q]::SetProcessDPIAware()

Remove-Item $log, "$log.err" -EA SilentlyContinue
$args = @("--serial=$serial", "--window-title=EssaiDpi",
          "--new-display=${Width}x${Height}/$Dpi", "--no-vd-system-decorations",
          "--keyboard=sdk", "--no-audio", "--max-fps=45", "--video-bit-rate=8000K")
if ($Flex) { $args += "--flex-display" }

$proc = Start-Process -FilePath "$root\scrcpy.exe" -WorkingDirectory $root -PassThru `
  -RedirectStandardOutput $log -RedirectStandardError "$log.err" -ArgumentList $args

$id = $null
for ($i = 0; $i -lt 60; $i++) {
  Start-Sleep -Milliseconds 500
  foreach ($f in @($log, "$log.err")) {
    if (Test-Path $f) {
      $m = Select-String -Path $f -Pattern 'New display: .*\(id=(\d+)\)' -EA SilentlyContinue | Select-Object -First 1
      if ($m) { $id = $m.Matches[0].Groups[1].Value; break }
    }
  }
  if ($id) { break }
}
if (-not $id) {
  "ECHEC a $Dpi ppp : aucun afficheur"
  Get-Content "$log.err" -EA SilentlyContinue | Select-Object -Last 6
  $proc | Stop-Process -Force -EA SilentlyContinue
  exit 1
}

& "$root\adb.exe" -s $serial shell am start --display $id -n com.ankama.dofustouch/com.ankama.dofustouch.MainActivity | Out-Null
Start-Sleep -Seconds $Settle

$h = [Q]::Find('EssaiDpi')
if ($h -eq [IntPtr]::Zero) { "ECHEC : fenetre introuvable"; $proc | Stop-Process -Force; exit 1 }

$targetW = if ($ClientWidth -gt 0) { $ClientWidth } else { [int][Math]::Round($ClientHeight * $Width / $Height) }
$wr = New-Object Q+RECT; $cr = New-Object Q+RECT
[void][Q]::GetWindowRect($h, [ref]$wr); [void][Q]::GetClientRect($h, [ref]$cr)
$chromeW = ($wr.R - $wr.L) - $cr.R
$chromeH = ($wr.B - $wr.T) - $cr.B
[void][Q]::SetWindowPos($h, [IntPtr]::Zero, 60, 30, $targetW + $chromeW, $ClientHeight + $chromeH, 0x0014)
Start-Sleep -Seconds 5

[void][Q]::GetWindowRect($h, [ref]$wr); [void][Q]::GetClientRect($h, [ref]$cr)
$ww = $wr.R - $wr.L; $wh = $wr.B - $wr.T
$bmp = New-Object System.Drawing.Bitmap $ww, $wh
$g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
[void][Q]::PrintWindow($h, $dc, 2); $g.ReleaseHdc($dc)

# On ne garde que la zone client : le cadre fausserait le compte des lignes.
$border = [int](($ww - $cr.R) / 2)
$titleH = $wh - $cr.B - $border
$crop = New-Object System.Drawing.Rectangle $border, $titleH, $cr.R, $cr.B
$client = $bmp.Clone($crop, $bmp.PixelFormat)
$client.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
$client.Dispose(); $g.Dispose(); $bmp.Dispose()

"dpi=$Dpi afficheur=${Width}x${Height} client=$($cr.R)x$($cr.B) (vise ${targetW}x$ClientHeight) -> $png"
$proc | Stop-Process -Force -EA SilentlyContinue
Start-Sleep -Seconds 2
