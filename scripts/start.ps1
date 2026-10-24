param([switch]$DatabaseOnly)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

if (-not (Test-Path -LiteralPath '.env')) {
    $randomBytes = New-Object byte[] 24
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($randomBytes) } finally { $rng.Dispose() }
    $localPassword = 'Ss1!' + [BitConverter]::ToString($randomBytes).Replace('-', '')
    Set-Content -LiteralPath '.env' -Value "MSSQL_SA_PASSWORD=$localPassword" -Encoding utf8
}
$passwordLine = Get-Content -LiteralPath '.env' | Where-Object { $_ -match '^MSSQL_SA_PASSWORD=' } | Select-Object -First 1
if (-not $passwordLine) { throw 'Set MSSQL_SA_PASSWORD in .env.' }
$env:MSSQL_SA_PASSWORD = $passwordLine.Substring('MSSQL_SA_PASSWORD='.Length).Trim()

docker compose up -d --wait --wait-timeout 180
if ($LASTEXITCODE -ne 0) { throw 'SQL Server did not start. Check that Docker Desktop is running.' }
if ($DatabaseOnly) { return }

# The password is passed to the child process through its environment, never as a CLI argument.
if (-not $env:ConnectionStrings__SmartShop) {
    $env:ConnectionStrings__SmartShop = 'Server=tcp:127.0.0.1,14333;Database=SMARTSHOP;User Id=sa;Password="' +
        $env:MSSQL_SA_PASSWORD.Replace('"', '""') + '";Encrypt=True;TrustServerCertificate=True'
}
dotnet run --project src/SmartShop.Api --launch-profile SmartShop
if ($LASTEXITCODE -ne 0) { throw 'Smart Shop stopped with an error.' }
