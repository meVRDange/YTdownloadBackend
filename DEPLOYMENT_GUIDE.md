# CI/CD Pipeline Setup Guide for YTdownloadBackend

## Overview
This guide will walk you through setting up an automated deployment pipeline that:
1. Builds your .NET API when you push to GitHub
2. Deploys it to your Windows laptop server automatically
3. Makes it accessible via your domain with Cloudflare Tunnel

---

## PHASE 1: GitHub Actions Runner Setup (On Your Laptop Server)

### Step 1.1: Create GitHub Personal Access Token (PAT)

1. Go to GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic)
2. Click "Generate new token (classic)"
3. Name: `YTdownloadBackend-Runner`
4. Select scopes:
   - ✅ `repo` (Full control of private repositories)
   - ✅ `admin:repo_hook` (Access to hooks)
   - ✅ `admin:org_hook`
5. Copy and **SAVE the token somewhere safe** (you'll need it once)

### Step 1.2: Download GitHub Actions Runner

On your laptop server (Windows), open PowerShell as Administrator:

```powershell
# Create a runner directory
New-Item -ItemType Directory -Path "C:\actions-runner" -Force
cd C:\actions-runner

# Download the latest runner
$ProgressPreference = "SilentlyContinue"
Invoke-WebRequest -Uri "https://github.com/actions/runner/releases/download/v2.318.0/actions-runner-win-x64-2.318.0.zip" -OutFile "runner.zip"

# Extract it
Expand-Archive -Path "runner.zip" -DestinationPath "." -Force
Remove-Item "runner.zip"ls

```

### Step 1.3: Configure the Runner

Still in PowerShell (as Administrator), in `C:\actions-runner`:

```powershell
# Run the configuration script
.\config.cmd `
  --url "https://github.com/meVRDange/YTdownloadBackend" `
  --token "YOUR_PAT_TOKEN_HERE" `
  --name "Laptop-Server" `
  --work "_work" `
  --runnergroup "Default"

# When prompted:
# - Runner name: "Laptop-Server"
# - Work folder: "_work" (press Enter for default)
# - Unattended: "Y"
```

### Step 1.4: Install Runner as Windows Service

Still in `C:\actions-runner`:

```powershell
# Install as service
.\svc.cmd install

# Start the service
.\svc.cmd start

# Verify it's running
Get-Service -Name "GitHub.Runner*"
```

### Step 1.5: Verify Runner is Connected

1. Go to your GitHub repo: https://github.com/meVRDange/YTdownloadBackend
2. Navigate to Settings → Actions → Runners
3. You should see "Laptop-Server" listed as **Online** (green dot)

---

## PHASE 2: Configure Your .NET API for Deployment

### Step 2.1: Ensure API Runs on Startup

Your `Program.cs` should have proper port binding. Add/verify this in your `Program.cs`:

```csharp
// After var app = builder.Build();
app.Run("http://0.0.0.0:5000");
// or specify in appsettings.json
```

Add this to your `appsettings.json`:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:5000"
      }
    }
  }
}
```

### Step 2.2: Add Health Check Endpoint (Optional but Recommended)

Add this to your Program.cs:

```csharp
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health")
    .WithOpenApi()
    .AllowAnonymous();
```

---

## PHASE 3: Setup Your Laptop as a Service

### Step 3.1: Create Logs Directory

On your laptop, in PowerShell (as Administrator):

```powershell
New-Item -ItemType Directory -Path "C:\YTdownloadBackend\logs" -Force
```

### Step 3.2: Install .NET Runtime (If Not Already Installed)

```powershell
# Check if .NET is installed
dotnet --version

# If not installed, download and install
$ProgressPreference = "SilentlyContinue"
Invoke-WebRequest -Uri "https://dotnet.microsoft.com/download/dotnet/latest/runtime" -OutFile "dotnet-installer.exe"
```

---

## PHASE 4: Setup Cloudflare Tunnel

### Step 4.1: Install Cloudflare Tunnel on Laptop

On your laptop, in PowerShell (as Administrator):

```powershell
# Download Cloudflare Tunnel
$ProgressPreference = "SilentlyContinue"
Invoke-WebRequest -Uri "https://github.com/cloudflare/cloudflared/releases/download/2024.1.0/cloudflared-windows-amd64.exe" -OutFile "C:\cloudflared.exe"

# Move to Program Files
Move-Item -Path "C:\cloudflared.exe" -Destination "C:\Program Files\cloudflared.exe" -Force
```

### Step 4.2: Authenticate Cloudflare

```powershell
& "C:\Program Files\cloudflared.exe" login
```

This will open your browser. Choose your Cloudflare domain and authorize it.

### Step 4.3: Create Cloudflare Tunnel Configuration

Create `C:\cloudflare\config.yml`:

```yaml
tunnel: <tunnel-name>
credentials-file: C:\Users\<YourUsername>\.cloudflared\<tunnel-id>.json

ingress:
  - hostname: api.vdange.site
    service: http://localhost:5000
  - service: http_status:404
```

### Step 4.4: Run Cloudflare Tunnel as Service

```powershell
# Install as Windows Service
& "C:\Program Files\cloudflared.exe" service install `
  --config "C:\cloudflare\config.yml" `
  --logfile "C:\cloudflare\cloudflared.log"

# Start the service
Start-Service cloudflared

# Verify
Get-Service cloudflared
```

---

## PHASE 5: Configure DNS at NameCheap

### Step 5.1: Update Nameservers (If Using Cloudflare DNS)

1. Log into NameCheap: https://www.namecheap.com/
2. Go to Dashboard → Your Domains → vdange.site
3. Click "Manage"
4. Go to Nameserver section
5. Change from "NameCheap BasicDNS" to "Custom DNS"
6. Add Cloudflare nameservers:
   - ns1.cloudflare.com
   - ns2.cloudflare.com
   - ns3.cloudflare.com
   - ns4.cloudflare.com

### Step 5.2: Add DNS Record in Cloudflare

1. Log into Cloudflare: https://dash.cloudflare.com
2. Select your zone: vdange.site
3. Go to DNS → Records
4. Click "Add record"
   - Type: CNAME
   - Name: api
   - Target: [your-tunnel-name].cfargotunnel.com
   - Proxy status: Proxied
   - TTL: Auto

---

## PHASE 6: Test Everything

### Step 6.1: Test the Pipeline

1. Make a small change to your code
2. Commit and push to GitHub:
   ```bash
   git add .
   git commit -m "Test CI/CD pipeline"
   git push
   ```

3. Go to GitHub → Actions tab
4. You should see the workflow running
5. Wait for it to complete (should take 2-5 minutes)

### Step 6.2: Test the Deployed API

Once the workflow completes:

1. **Local test:**
   ```bash
   curl http://localhost:5000/health
   ```

2. **Via Cloudflare Tunnel:**
   ```bash
   curl https://api.vdange.site/health
   ```

3. **Check service logs:**
   ```powershell
   Get-Content "C:\YTdownloadBackend\logs\stdout.log" -Tail 50
   Get-Content "C:\YTdownloadBackend\logs\stderr.log" -Tail 50
   ```

---

## PHASE 7: Troubleshooting

### Runner Not Connecting?
```powershell
# Check runner service status
Get-Service -Name "GitHub.Runner*"

# View runner logs
Get-Content "C:\actions-runner\_diag\Runner_*" -Tail 100
```

### Service Won't Start?
```powershell
# Check if port 5000 is in use
netstat -ano | findstr :5000

# Check recent errors
Get-EventLog -LogName Application -Source "YTdownloadBackend" -Newest 10
```

### Cloudflare Tunnel Issues?
```powershell
# Check tunnel status
Get-Service cloudflared

# View tunnel logs
Get-Content "C:\cloudflare\cloudflared.log" -Tail 100
```

### DNS Not Resolving?
1. Wait 10-15 minutes for DNS propagation
2. Test with: `nslookup api.vdange.site`
3. Verify CNAME in Cloudflare dashboard

---

## PHASE 8: Maintenance & Monitoring

### Check Deployment Status
```powershell
# Get service status
Get-Service -Name "YTdownloadBackend"

# View recent activity
Get-EventLog -LogName Application -Source YTdownloadBackend -Newest 20
```

### Manual Deployment
If you need to deploy without pushing:
1. Go to GitHub Actions tab
2. Select the workflow
3. Click "Run workflow" button

### Updates
When you push code:
1. Workflow runs automatically
2. Service stops → deploys → starts
3. New version is live within minutes

---

## Quick Reference Checklist

✅ GitHub PAT created  
✅ Runner installed on laptop  
✅ Runner service running  
✅ .NET API configured  
✅ Logs directory created  
✅ Cloudflare Tunnel installed  
✅ Cloudflare authenticated  
✅ Nameservers updated at NameCheap  
✅ DNS CNAME record created  
✅ Test deployment completed  

---

## Commands Summary

```powershell
# Check everything is running on laptop:
Get-Service -Name "YTdownloadBackend"
Get-Service -Name "GitHub.Runner*"
Get-Service -Name "cloudflared"

# All should show "Running" status
```

---

For help or issues, check the logs in:
- GitHub Actions: https://github.com/meVRDange/YTdownloadBackend/actions
- Service logs: `C:\YTdownloadBackend\logs\`
- Cloudflare logs: `C:\cloudflare\cloudflared.log`
