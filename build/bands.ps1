# Compte les lignes uniformes en haut et en bas d'une image.
# Outil de developpement uniquement : sert a mesurer la bande laissee par le jeu.
param([string]$Path)

Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile($Path)
$w = $bmp.Width; $h = $bmp.Height
$rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
$data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$stride = $data.Stride
$bytes = New-Object byte[] ($stride * $h)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
$bmp.UnlockBits($data); $bmp.Dispose()

# Une ligne est dite uniforme si ses pixels ne varient pas de plus de 4 niveaux.
$step = [Math]::Max(1, [int]($w / 200))
function Test-Uniform([int]$y) {
    $base = $y * $stride
    $minB = 255; $maxB = 0; $minG = 255; $maxG = 0; $minR = 255; $maxR = 0
    for ($x = 0; $x -lt $w; $x += $step) {
        $i = $base + $x * 4
        $b = $bytes[$i]; $g = $bytes[$i + 1]; $r = $bytes[$i + 2]
        if ($b -lt $minB) { $minB = $b }; if ($b -gt $maxB) { $maxB = $b }
        if ($g -lt $minG) { $minG = $g }; if ($g -gt $maxG) { $maxG = $g }
        if ($r -lt $minR) { $minR = $r }; if ($r -gt $maxR) { $maxR = $r }
    }
    return (($maxB - $minB) -le 4) -and (($maxG - $minG) -le 4) -and (($maxR - $minR) -le 4)
}

$top = 0
while ($top -lt $h -and (Test-Uniform $top)) { $top++ }
$bottom = 0
while (($h - 1 - $bottom) -gt $top -and (Test-Uniform ($h - 1 - $bottom))) { $bottom++ }

"{0}  image {1}x{2}  bande haut={3} bas={4}  dessine={5}" -f (Split-Path $Path -Leaf), $w, $h, $top, $bottom, ($h - $top - $bottom)
