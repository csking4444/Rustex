# Rustex local dev bootstrap (Windows / PowerShell)
# Usage: pwsh ./scripts/dev-setup.ps1

$ErrorActionPreference = "Stop"

Write-Host "==> Checking prerequisites" -ForegroundColor Cyan
foreach ($cmd in @("dotnet", "node", "npm", "docker")) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        Write-Warning "$cmd not found on PATH. Install it before continuing."
    }
}

Write-Host "==> Creating .env files from templates (if missing)" -ForegroundColor Cyan
$serverEnv = "server/src/Rustex.Api/.env"
$clientEnv = "client/.env"
if (-not (Test-Path $serverEnv)) { Copy-Item "server/src/Rustex.Api/.env.example" $serverEnv }
if (-not (Test-Path $clientEnv)) { Copy-Item "client/.env.example" $clientEnv }

Write-Host "==> Starting Postgres + Redis" -ForegroundColor Cyan
docker compose up -d postgres redis

Write-Host "==> Restoring .NET packages" -ForegroundColor Cyan
Push-Location server
dotnet restore
Pop-Location

Write-Host "==> Installing frontend packages" -ForegroundColor Cyan
Push-Location client
npm install
Pop-Location

Write-Host "==> Done. Next steps:" -ForegroundColor Green
Write-Host "  1. Fill in Discord OAuth + JWT secrets in the .env files created above."
Write-Host "  2. dotnet ef migrations add InitialCreate -p server/src/Rustex.Infrastructure -s server/src/Rustex.Api"
Write-Host "  3. dotnet ef database update -p server/src/Rustex.Infrastructure -s server/src/Rustex.Api"
Write-Host "  4. cd server/src/Rustex.Api; dotnet run"
Write-Host "  5. cd client; npm run dev"
