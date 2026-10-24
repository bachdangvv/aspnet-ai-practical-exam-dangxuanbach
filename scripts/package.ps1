$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot
$outputDirectory = Join-Path $repoRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$archive = Join-Path $outputDirectory 'SmartShop-SET01-DangXuanBach.zip'
$includedRoots = @('src', 'database', 'postman')
$files = @('README.md', 'SmartShop.sln', 'docker-compose.yml', '.gitignore', '.env.example',
    'scripts/start.ps1', 'scripts/package.ps1')
foreach ($directory in $includedRoots) {
    $files += Get-ChildItem -LiteralPath (Join-Path $repoRoot $directory) -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|TestResults)[\\/]' -and
            $_.Name -notlike '.env*' -and $_.Name -ne 'appsettings.Local.json' -and
            $_.Extension -notin @('.user', '.suo') } |
        ForEach-Object { $_.FullName.Substring($repoRoot.Length + 1) }
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream = [System.IO.File]::Open($archive, [System.IO.FileMode]::Create)
try {
    $zip = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, (Join-Path $repoRoot $file), $file.Replace('\', '/')) | Out-Null
        }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }
Write-Output "Submission archive: $archive"
