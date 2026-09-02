# Sonde du mode onglets : arrime une fenetre dans un cadre, puis la rend.
#
# Outil de developpement uniquement, jamais employe par l'application. Il
# repond a la seule question que le code ne peut pas trancher : une fenetre
# SDL, celle de scrcpy, survit-elle a devenir fenetre fille ?
#
# La fenetre est rendue a son etat d'origine a la fin, style et parent
# compris : une sonde qui laisse une fenetre orpheline ne vaut rien.
param(
    [Parameter(Mandatory = $true)][string]$Titre,
    [int]$Secondes = 20,
    [string]$Capture = ""
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Arrimage {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);
    public delegate bool EnumProc(IntPtr hWnd, IntPtr p);
    public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$GWL_STYLE = -16
$WS_CHILD = 0x40000000
$WS_POPUP = 0x80000000
$WS_CAPTION = 0x00C00000
$WS_THICKFRAME = 0x00040000
$SWP_FRAME = 0x0020

# --- Trouver la fenetre visee -------------------------------------------
$cible = [IntPtr]::Zero
$vu = @()
$rappel = [Arrimage+EnumProc]{
    param($h, $p)
    if ([Arrimage]::IsWindowVisible($h)) {
        $sb = New-Object System.Text.StringBuilder 512
        [void][Arrimage]::GetWindowTextW($h, $sb, 512)
        $t = $sb.ToString()
        if ($t -and $t.Contains($Titre)) { $script:cible = $h; $script:vu += $t; return $false }
    }
    return $true
}
[void][Arrimage]::EnumWindows($rappel, [IntPtr]::Zero)

if ($cible -eq [IntPtr]::Zero) { Write-Output "CIBLE INTROUVABLE: $Titre"; exit 1 }
Write-Output ("cible: " + $vu[0] + "  handle " + $cible)

# --- Retenir l'etat d'origine -------------------------------------------
$styleAvant = [Arrimage]::GetWindowLong($cible, $GWL_STYLE)
$parentAvant = [Arrimage]::GetParent($cible)
$rectAvant = New-Object Arrimage+RECT
[void][Arrimage]::GetWindowRect($cible, [ref]$rectAvant)
Write-Output ("avant: style 0x{0:X}  parent {1}  rect {2},{3} {4}x{5}" -f `
    [int64]$styleAvant, $parentAvant, $rectAvant.Left, $rectAvant.Top,
    ($rectAvant.Right - $rectAvant.Left), ($rectAvant.Bottom - $rectAvant.Top))

# --- Le cadre d'essai ----------------------------------------------------
$cadre = New-Object System.Windows.Forms.Form
$cadre.Text = "Sonde onglets"
$cadre.Size = New-Object System.Drawing.Size(1100, 760)
$cadre.StartPosition = "Manual"
$cadre.Location = New-Object System.Drawing.Point(60, 60)
$cadre.Show()
[System.Windows.Forms.Application]::DoEvents()
Start-Sleep -Milliseconds 400

# --- Arrimer -------------------------------------------------------------
$nouveau = ([int64]$styleAvant -band -bnot ($WS_POPUP -bor $WS_CAPTION -bor $WS_THICKFRAME)) -bor $WS_CHILD
[void][Arrimage]::SetWindowLong($cible, $GWL_STYLE, [IntPtr]$nouveau)
$ancienParent = [Arrimage]::SetParent($cible, $cadre.Handle)
[void][Arrimage]::SetWindowPos($cible, [IntPtr]::Zero, 0, 0, $cadre.ClientSize.Width, $cadre.ClientSize.Height, $SWP_FRAME)
[System.Windows.Forms.Application]::DoEvents()
Start-Sleep -Milliseconds 600

$parentApres = [Arrimage]::GetParent($cible)
Write-Output ("apres: parent {0}  attendu {1}  arrime: {2}" -f `
    $parentApres, $cadre.Handle, ($parentApres -eq $cadre.Handle))

# --- Laisser vivre, pour l'oeil et la capture ---------------------------
$fin = (Get-Date).AddSeconds($Secondes)
while ((Get-Date) -lt $fin -and -not $cadre.IsDisposed) {
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 100
}

if ($Capture) {
    $bmp = New-Object System.Drawing.Bitmap $cadre.Width, $cadre.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($cadre.Location, [System.Drawing.Point]::Empty, $cadre.Size)
    $bmp.Save($Capture); $g.Dispose(); $bmp.Dispose()
    Write-Output "capture -> $Capture"
}

# --- Rendre la fenetre telle qu'elle etait ------------------------------
[void][Arrimage]::SetParent($cible, $parentAvant)
[void][Arrimage]::SetWindowLong($cible, $GWL_STYLE, $styleAvant)
[void][Arrimage]::SetWindowPos($cible, [IntPtr]::Zero, $rectAvant.Left, $rectAvant.Top,
    ($rectAvant.Right - $rectAvant.Left), ($rectAvant.Bottom - $rectAvant.Top), $SWP_FRAME)
if (-not $cadre.IsDisposed) { $cadre.Close() }
Write-Output ("rendue: parent {0}  style 0x{1:X}" -f [Arrimage]::GetParent($cible), [int64][Arrimage]::GetWindowLong($cible, $GWL_STYLE))
