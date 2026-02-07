# Clean old config
Remove-Item ".\.credentials" -Force -ErrorAction SilentlyContinue
Remove-Item ".\.runner" -Force -ErrorAction SilentlyContinue

# Try with the fine-grained token
.\config.cmd `
  --url "https://github.com/meVRDange/YTdownloadBackend" `
  --token "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" `
  --name "Laptop-Server" `
  --work "_work"


# YTdownloadBackend - CI/CD Pipeline Setup

## 🎯 Project Overview

Your .NET API will be automatically deployed to your Windows laptop server whenever you push code to GitHub, and it will be accessible via your domain through Cloudflare Tunnel.

```
GitHub (Your Code)
    ↓ (Push)
GitHub Actions (Builds & Tests)
    ↓ (Success)
Laptop Server (Deploys & Runs)
    ↓ (Tunnel)
Cloudflare Tunnel
    ↓ (CNAME)
api.vdange.site (Your Domain)
```

---

## ✅ Completed Steps

- ✅ **Step 1**: GitHub runner installed and ONLINE on laptop

---

## 📋 Next Steps

### Step 2️⃣: Create GitHub Actions Workflow File (5 minutes)

This file tells GitHub how to build and deploy your API.

1. In your repository, create this folder structure:
   ```
   .github/workflows/
   ```

2. Create file: `.github/workflows/deploy.yml`

3. Paste this content:

```yaml
# filepath: .github/workflows/deploy.yml
name: Build and Deploy .NET API

on:
  push:
    branches: [ main ]
  workflow_dispatch:

jobs:
  build-and-deploy:
    runs-on: self-hosted
    
    steps:
    - uses: actions/checkout@v4
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '10.0.x'
    
    - name: Restore dependencies
      run: dotnet restore
    
    - name: Build
      run: dotnet build --configuration Release --no-restore
    
    - name: Test
      run: dotnet test --configuration Release --no-build --verbosity normal
    
    - name: Publish
      run: dotnet publish --configuration Release --output ./publish
    
    - name: Stop existing service
      run: |
        Stop-Service -Name "YTdownloadBackend" -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 3
      shell: powershell
    
    - name: Deploy to service folder
      run: |
        $deployPath = "C:\YTdownloadBackend\app"
        if (Test-Path $deployPath) { Remove-Item -Path $deployPath -Recurse -Force }
        Copy-Item -Path "./publish" -Destination $deployPath -Recurse
      shell: powershell
    
    - name: Start service
      run: Start-Service -Name "YTdownloadBackend"
      shell: powershell
    
    - name: Verify deployment
      run: |
        Start-Sleep -Seconds 5
        $response = Invoke-WebRequest -Uri "http://localhost:5000/health" -ErrorAction SilentlyContinue
        if ($response.StatusCode -eq 200) {
          Write-Host "✅ Deployment successful!"
        } else {
          Write-Host "❌ Health check failed"
          exit 1
        }
      shell: powershell
```

4. **Commit and push this file:**
```bash
git add .github/workflows/deploy.yml
git commit -m "Add GitHub Actions deployment workflow"
git push
```

---

### Step 3️⃣: Configure Your .NET API (5 minutes)

#### Update appsettings.json

Add Kestrel configuration to your `appsettings.json`:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:5000"
      }
    }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

#### Update Program.cs

Add the health check endpoint before `app.Run()`:

```csharp
// ...existing code...

var app = builder.Build();

// Add this health endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("Health")
    .WithOpenApi()
    .AllowAnonymous();

// ...existing code...

app.Run();
```

---

### Step 4️⃣: Create Windows Service for Auto-Start (10 minutes)

Create a PowerShell script to install your API as a Windows Service: `setup-service.ps1`

```powershell
# filepath: setup-service.ps1
# Run this ONCE on your laptop as Administrator

$serviceName = "YTdownloadBackend"
$displayName = "YT Download Backend API"
$appPath = "C:\YTdownloadBackend\app"
$exePath = "$appPath\YTdownloadBackend.exe"
$logPath = "C:\YTdownloadBackend\logs"

# Create app directory
New-Item -ItemType Directory -Path $appPath -Force | Out-Null
New-Item -ItemType Directory -Path $logPath -Force | Out-Null

# Check if service exists
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($service) {
    Write-Host "Service already exists. Stopping..."
    Stop-Service -Name $serviceName -Force
    Start-Sleep -Seconds 2
    Remove-Service -Name $serviceName -Force
}

# Create service
Write-Host "Creating Windows Service..."
New-Service -Name $serviceName `
    -DisplayName $displayName `
    -BinaryPathName $exePath `
    -StartupType Automatic | Out-Null

Write-Host "✅ Service created successfully!"
Write-Host "Start the service with: Start-Service -Name 'YTdownloadBackend'"
```

**Run this once:**
```powershell
# On your laptop as Administrator
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
.\setup-service.ps1
```

---

### Step 5️⃣: Test Locally (5 minutes)

```powershell
# Build locally to test
dotnet build

# Run and test
dotnet run --configuration Release

# In another PowerShell window, test the health endpoint
curl http://localhost:5000/health
```

You should see:
```json
{"status":"healthy","timestamp":"2025-02-07T10:30:45.1234567Z"}
```

---

### Step 6️⃣: Push to GitHub and Watch Deployment

```bash
git add .
git commit -m "Setup CI/CD with health endpoint and service configuration"
git push
```

**Go to:** https://github.com/meVRDange/YTdownloadBackend/actions

You'll see the workflow running! ⏳ (Takes 2-5 minutes)

---

## 🔧 Quick Checklist Before Push

- [ ] `.github/workflows/deploy.yml` created
- [ ] `appsettings.json` has Kestrel config
- [ ] `/health` endpoint added to `Program.cs`
- [ ] `setup-service.ps1` run on your laptop
- [ ] Service starts without errors
- [ ] Local health endpoint works

---

## Next: Cloudflare Tunnel Setup

Once the workflow completes successfully, we'll setup Cloudflare Tunnel to make your API accessible via `api.vdange.site`.

Tell me:
1. Have you created the workflow file?
2. Have you updated appsettings.json and Program.cs?
3. Are you ready to push and test?
