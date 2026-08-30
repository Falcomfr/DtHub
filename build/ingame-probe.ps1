# Mesure la hauteur dessinee par le jeu une fois connecte, apres agrandissement.
# Outil de developpement uniquement.
param(
    [string]$Serial,
    [int]$Dpi = 240,
    [int]$BornWidth = 2760, [int]$BornHeight = 1416,
    [int]$FirstClientW = 1380, [int]$FirstClientH = 708,
    [int]$ThenClientW = 2820, [int]$ThenClientH = 1844,
    [switch]$NoTap, [int]$Between = 15, [int]$Settle = 25)

$root   = "$env:LOCALAPPDATA\DtHub\tools\scrcpy-4.1.0\scrcpy-win64-v4.1"
# Jamais de numero de serie en dur : le premier appareil joignable fait foi.
if (-not $Serial) {
    $Serial = (& "$root\adb.exe" devices | Select-String '\tdevice$' | Select-Object -First 1).ToString().Split("`t")[0]
}
if (-not $Serial) { 'ECHEC : aucun appareil joignable'; exit 1 }
$serial = $Serial
$log    = "C:\Dev\DTHub\build\ingame.log"
$png    = "C:\Dev\DTHub\build\ingame-$FirstClientH-$ThenClientH-$Dpi.png"

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Runtime.InteropServices;using System.Text;
public class R {
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
  public static IntPtr Find(string t) {
    IntPtr f = IntPtr.Zero;
    EnumWindows((h,l) => { if (!IsWindowVisible(h)) return true;
      var sb = new StringBuilder(512); GetWindowTextW(h, sb, sb.Capacity);
      if (sb.ToString().Contains(t)) { f = h; return false; } return true; }, IntPtr.Zero);
    return f; }
}
"@
[void][R]::SetProcessDPIAware()

function Set-Client([IntPtr]$h, [int]$cw, [int]$ch) {
  $wr = New-Object R+RECT; $cr = New-Object R+RECT
  [void][R]::GetWindowRect($h, [ref]$wr); [void][R]::GetClientRect($h, [ref]$cr)
  $bw = ($wr.R - $wr.L) - $cr.R; $bh = ($wr.B - $wr.T) - $cr.B
  [void][R]::SetWindowPos($h, [IntPtr]::Zero, 60, 30, $cw + $bw, $ch + $bh, 0x0014)
}

function Save-Client([IntPtr]$h, [string]$out) {
  $wr = New-Object R+RECT; $cr = New-Object R+RECT
  [void][R]::GetWindowRect($h, [ref]$wr); [void][R]::GetClientRect($h, [ref]$cr)
  $ww = $wr.R - $wr.L; $wh = $wr.B - $wr.T
  $bmp = New-Object System.Drawing.Bitmap $ww, $wh
  $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
  [void][R]::PrintWindow($h, $dc, 2); $g.ReleaseHdc($dc)
  $border = [int](($ww - $cr.R) / 2); $title = $wh - $cr.B - $border
  $crop = New-Object System.Drawing.Rectangle $border, $title, $cr.R, $cr.B
  $c = $bmp.Clone($crop, $bmp.PixelFormat); $c.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
  $c.Dispose(); $g.Dispose(); $bmp.Dispose()
  return "$($cr.R)x$($cr.B)"
}

Remove-Item $log, "$log.err" -EA SilentlyContinue
$proc = Start-Process -FilePath "$root\scrcpy.exe" -WorkingDirectory $root -PassThru `
  -RedirectStandardOutput $log -RedirectStandardError "$log.err" -ArgumentList @(
    "--serial=$serial", "--window-title=EssaiJeu",
    "--new-display=${BornWidth}x${BornHeight}/$Dpi", "--no-vd-system-decorations",
    "--flex-display", "--keyboard=sdk", "--no-audio", "--max-fps=45", "--video-bit-rate=8000K")

$id = $null
for ($i = 0; $i -lt 60; $i++) {
  Start-Sleep -Milliseconds 500
  foreach ($f in @($log, "$log.err")) {
    if (Test-Path $f) {
      $m = Select-String -Path $f -Pattern 'New display: .*\(id=(\d+)\)' -EA SilentlyContinue | Select-Object -First 1
      if ($m) { $id = $m.Matches[0].Groups[1].Value; break } } }
  if ($id) { break } }
if (-not $id) { "ECHEC: aucun afficheur"; $proc | Stop-Process -Force -EA SilentlyContinue; exit 1 }
"afficheur id=$id, ne en ${BornWidth}x${BornHeight} a $Dpi ppp"

& "$root\adb.exe" -s $serial shell am start --display $id -n com.ankama.dofustouch/com.ankama.dofustouch.MainActivity | Out-Null
Start-Sleep -Seconds $Settle

$h = [R]::Find('EssaiJeu')
if ($h -eq [IntPtr]::Zero) { "ECHEC: fenetre introuvable"; $proc | Stop-Process -Force; exit 1 }
Set-Client $h $FirstClientW $FirstClientH
Start-Sleep -Seconds 5

# Le bouton « Continuer en tant que » se trouve aux deux tiers de la largeur,
# a mi-hauteur. Coordonnees exprimees dans l'afficheur, non dans la fenetre.
if (-not $NoTap) {
  $tapX = [int]($FirstClientW * 0.655); $tapY = [int]($FirstClientH * 0.547)
  & "$root\adb.exe" -s $serial shell input -d $id tap $tapX $tapY | Out-Null
  "clic connexion en $tapX,$tapY"
  Start-Sleep -Seconds 50
} else {
  Start-Sleep -Seconds $Between
}

Set-Client $h $ThenClientW $ThenClientH
Start-Sleep -Seconds 8
$client = Save-Client $h $png
"apres agrandissement : client $client -> $png"
$proc | Stop-Process -Force -EA SilentlyContinue
