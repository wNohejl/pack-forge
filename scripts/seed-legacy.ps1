# Seeds the fake legacy content for the Phase 2 migration demo:
#   .legacy-share/fileshare   — plain files (file-share stand-in)
#   .legacy-share/sharepoint  — includes *corrupt* files the throttled Graph-style
#                               source truncates on read; they must FAIL verification.
# Target: >= 500 files, >= 1 GB total.
param(
    [string]$Root = (Join-Path $PSScriptRoot ".." ".legacy-share")
)

$rng = [System.Random]::new(20260706)

function New-RandomFile([string]$Path, [long]$Size) {
    $dir = Split-Path $Path -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
    $buffer = [byte[]]::new(1MB)
    $fs = [System.IO.File]::Create($Path)
    try {
        $remaining = $Size
        while ($remaining -gt 0) {
            $rng.NextBytes($buffer)
            $chunk = [Math]::Min($remaining, $buffer.Length)
            $fs.Write($buffer, 0, $chunk)
            $remaining -= $chunk
        }
    } finally { $fs.Dispose() }
}

if (Test-Path $Root) { Remove-Item -Recurse -Force $Root }

$total = 0L
# fileshare: 300 files, 64KB..4MB, in nested folders + 4 big 50MB files
for ($i = 0; $i -lt 300; $i++) {
    $size = $rng.Next(64KB, 4MB)
    $folder = "dept-{0}/project-{1}" -f ($i % 6), ($i % 20)
    New-RandomFile (Join-Path $Root "fileshare" $folder "doc-$i.bin") $size
    $total += $size
}
for ($i = 0; $i -lt 4; $i++) {
    New-RandomFile (Join-Path $Root "fileshare" "archives" "archive-$i.bin") 50MB
    $total += 50MB
}

# sharepoint: 250 files, 128KB..3MB, 15 of them corrupt + 2 big 60MB files
for ($i = 0; $i -lt 250; $i++) {
    $size = $rng.Next(128KB, 3MB)
    $name = if ($i % 17 -eq 0 -and $i -lt 250) { "site-{0}/lib/report-$i-corrupt.bin" -f ($i % 5) } else { "site-{0}/lib/report-$i.bin" -f ($i % 5) }
    New-RandomFile (Join-Path $Root "sharepoint" $name) $size
    $total += $size
}
for ($i = 0; $i -lt 2; $i++) {
    New-RandomFile (Join-Path $Root "sharepoint" "media" "video-$i.bin") 60MB
    $total += 60MB
}

$count = (Get-ChildItem -Recurse -File $Root).Count
"Seeded $count files, {0:N2} GB total at $Root" -f ($total / 1GB)
