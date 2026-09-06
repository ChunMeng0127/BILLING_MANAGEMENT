$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$configPath = Join-Path $root '.local/settings.json'
if (!(Test-Path -LiteralPath $configPath)) { throw 'Local credentials are missing. Follow README development setup.' }
$config = Get-Content -LiteralPath $configPath | ConvertFrom-Json
$pg = Join-Path $root '.local/pgsql/bin/pg_ctl.exe'
if (Test-Path -LiteralPath $pg) {
    & (Join-Path $root '.local/pgsql/bin/pg_isready.exe') -h 127.0.0.1 -p 55432 -U billing *> $null
    if ($LASTEXITCODE -ne 0) { & $pg -D '.local/pgdata' -l '.local/postgres.log' -o '-h 127.0.0.1 -p 55432' start }
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL failed to start. Check .local/postgres.log.' }
}
$env:ConnectionStrings__Default = 'Host=127.0.0.1;Port=55432;Database=billing_dev;Username=billing;Password=' + $config.DatabasePassword
$env:DataProtection__Path = Join-Path $root '.local/keys'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/BillingControl --no-build --no-launch-profile --urls http://127.0.0.1:5188
