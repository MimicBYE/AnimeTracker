$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$frames=@()
foreach($size in @(16,24,32,48,64,128,256,512,1024)) {
 $bitmap=New-Object System.Drawing.Bitmap($size,$size)
 $g=[Drawing.Graphics]::FromImage($bitmap)
 $g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
 $g.ScaleTransform($size/64.0,$size/64.0)
 $paper=New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(249,231,165))
 $ink=New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(71,54,29))
 $gold=New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(185,134,44))
 $g.FillRectangle($ink,6,3,52,58)
 $g.FillRectangle($paper,9,6,46,52)
 $g.FillRectangle($gold,9,6,46,8)
 $g.FillPolygon($ink,[Drawing.PointF[]]@([Drawing.PointF]::new(24,22),[Drawing.PointF]::new(24,43),[Drawing.PointF]::new(42,32.5)))
 $g.FillRectangle($gold,17,49,30,3)
 $stream=New-Object IO.MemoryStream
 $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
 $frames+=,@{Size=$size;Bytes=$stream.ToArray()}
 $stream.Dispose(); $g.Dispose(); $bitmap.Dispose(); $paper.Dispose(); $ink.Dispose(); $gold.Dispose()
}
$allFrames=$frames
$frames=@($frames | Where-Object {$_.Size -le 256})
$file=[IO.File]::Create((Join-Path (Split-Path $PSScriptRoot -Parent) 'src/AnimeTracker.Windows/Assets/app.ico'))
$writer=New-Object IO.BinaryWriter($file)
try {
 $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
 $offset=6+16*$frames.Count
 foreach($frame in $frames) {
  $dimension=if($frame.Size -eq 256){0}else{$frame.Size}
  $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
  $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
  $offset+=$frame.Bytes.Length
 }
 foreach($frame in $frames){$writer.Write([byte[]]$frame.Bytes)}
} finally {$writer.Dispose();$file.Dispose()}
$macAssets=Join-Path (Split-Path $PSScriptRoot -Parent) 'src/AnimeTracker.Desktop/Assets'
New-Item -ItemType Directory -Path $macAssets -Force|Out-Null
$iconFrames=@($allFrames|Where-Object {$_.Size -ge 128})
$file=[IO.File]::Create((Join-Path $macAssets 'app.icns'))
$writer=[IO.BinaryWriter]::new($file)
function Write-BigEndian([int]$value){$bytes=[BitConverter]::GetBytes($value);[Array]::Reverse($bytes);$writer.Write($bytes)}
try{
 $writer.Write([Text.Encoding]::ASCII.GetBytes('icns'));Write-BigEndian (8+($iconFrames|ForEach-Object {$_.Bytes.Length+8}|Measure-Object -Sum).Sum)
 foreach($frame in $iconFrames){$type=switch($frame.Size){128{'ic07'}256{'ic08'}512{'ic09'}1024{'ic10'}};$writer.Write([Text.Encoding]::ASCII.GetBytes($type));Write-BigEndian ($frame.Bytes.Length+8);$writer.Write([byte[]]$frame.Bytes)}
}finally{$writer.Dispose();$file.Dispose()}
