# Quick Setup Script for Windows Laptop Server
# Run this in PowerShell as Administrator

# Color output
function Write-Header {
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host $args[0] -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
}

function Write-Success {
    Write-Host "✓ $($args[0])" -ForegroundColor Green
}

function Write-Error {
    Write-Host "✗ $($args[0])" -ForegroundColor Red
}

# Check if running as Administrator
if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Error "This script must be run as Administrator!"
    exit 1
}

Write-Header "YTdownloadBackend Deployment Setup"

# Step 1: Create necessary directories
Write-Host "`nStep 1: Creating directories..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path "C:\YTdownloadBackend\logs" -Force | Out-Null
New-Item -ItemType Directory -Path "C:\actions-runner" -Force | Out-Null
New-Item -ItemType Directory -Path "C:\cloudflare" -Force | Out-Null
Write-Success "Directories created"

# Step 2: Check .NET installation
Write-Host "`nStep 2: Checking .NET installation..." -ForegroundColor Yellow
try {
    $dotnetVersion = & dotnet --version
    Write-Success ".NET is installed: $dotnetVersion"
} catch {
    Write-Error ".NET is not installed. Please install .NET Runtime 10.0+"
}

# Step 3: Download GitHub Runner
Write-Host "`nStep 3: Downloading GitHub Actions Runner..." -ForegroundColor Yellow
$ProgressPreference = "SilentlyContinue"
$runnerUrl = "https://github.com/actions/runner/releases/download/v2.318.0/actions-runner-win-x64-2.318.0.zip"
$runnerZip = "C:\actions-runner\runner.zip"

if (Test-Path $runnerZip) {
    Write-Host "Runner already exists, skipping download..."
} else {
    Invoke-WebRequest -Uri $runnerUrl -OutFile $runnerZip
    Write-Success "Runner downloaded"
    
    Write-Host "Extracting runner..." -ForegroundColor Yellow
    Expand-Archive -Path $runnerZip -DestinationPath "C:\actions-runner" -Force
    Remove-Item $runnerZip
    Write-Success "Runner extracted"
}

# Step 4: Download Cloudflare
Write-Host "`nStep 4: Downloading Cloudflare Tunnel..." -ForegroundColor Yellow
$cfUrl = "https://github.com/cloudflare/cloudflared/releases/download/2024.1.0/cloudflared-windows-amd64.exe"
$cfPath = "C:\Program Files\cloudflared.exe"

if (Test-Path $cfPath) {
    Write-Host "Cloudflare already exists, skipping download..."
} else {
    $cfTemp = "C:\cloudflared.exe"
    Invoke-WebRequest -Uri $cfUrl -OutFile $cfTemp
    Move-Item -Path $cfTemp -Destination $cfPath -Force
    Write-Success "Cloudflare Tunnel downloaded"
}

# Step 5: Information for next steps
Write-Header "Next Steps"
Write-Host @"
1. CONFIGURE GITHUB RUNNER:
   cd C:\actions-runner
   .\config.cmd `
     --url "https://github.com/meVRDange/YTdownloadBackend" `
     --token "YOUR_GITHUB_PAT_HERE" `
     --name "Laptop-Server" `
     --work "_work"
   
   Then install service:
   .\svc.cmd install
   .\svc.cmd start

2. CREATE GITHUB PAT:
   Go to: https://github.com/settings/tokens
   Create new token with 'repo' and 'admin:repo_hook' scopes
   Copy the token and use it in step 1 above

3. SETUP CLOUDFLARE:
   & "C:\Program Files\cloudflared.exe" login

4. UPDATE APPSETTINGS.JSON:
   Add to your appsettings.json:
   {
     "Kestrel": {
       "Endpoints": {
         "Http": {
           "Url": "http://0.0.0.0:5000"
         }
       }
     }
   }

5. PUSH TO GITHUB:
   After configuring runner and appsettings:
   git add .
   git commit -m "Setup CI/CD pipeline"
   git push

6. MONITOR DEPLOYMENT:
   Check GitHub Actions: https://github.com/meVRDange/YTdownloadBackend/actions
"@

Write-Host "`n✓ Setup complete! Follow the steps above to finish configuration." -ForegroundColor Green
