# Testing Windows Authentication with Different User

## 🎯 Problem
Incognito mode still uses your current Windows session (ukhan2). To test as CMUserUK, you need to run the browser AS that user.

---

## ✅ Method 1: Run Browser as Different User (Windows)

### **Step 1: Open Command Prompt**
Press `Win + R`, type `cmd`, press Enter

### **Step 2: Run Browser as CMUserUK**
```cmd
runas /user:.\CMUserUK "C:\Program Files\Google\Chrome\Application\chrome.exe" --new-window --incognito https://localhost:7170
```

Or for Edge:
```cmd
runas /user:.\CMUserUK "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" --inprivate https://localhost:7170
```

**You'll be prompted for the password** for CMUserUK (9019566144@Uks)

### **Step 3: Access the Application**
The browser will open as CMUserUK and Windows Authentication will use those credentials.

---

## ✅ Method 2: Test via API with Credentials

### **Using curl (Command Line)**
```bash
curl -X GET "https://localhost:7170/api/AuthTest/whoami" ^
  --negotiate ^
  --user CMUserUK:9019566144@Uks ^
  --insecure
```

### **Using PowerShell**
```powershell
$username = "CMUserUK"
$password = ConvertTo-SecureString "9019566144@Uks" -AsPlainText -Force
$credential = New-Object System.Management.Automation.PSCredential ($username, $password)

Invoke-WebRequest -Uri "https://localhost:7170/api/AuthTest/whoami" `
  -Credential $credential `
  -AllowUnencryptedAuthentication `
  -SkipCertificateCheck
```

---

## ✅ Method 3: Remote Desktop / Switch User

1. **Sign out** from Windows (Don't just lock)
2. Click "Other User"
3. Login as:
   - Username: `CMUserUK`
   - Password: `9019566144@Uks`
4. Open browser normally
5. Navigate to `https://localhost:7170`

---

## 🐛 Debug Endpoints to Test

### **1. Check Auth Status**
```
GET https://localhost:7170/api/AuthTest/status
```
Expected: Shows if authentication is working

### **2. Who Am I?**
```
GET https://localhost:7170/api/AuthTest/whoami
```
Expected: Shows username, display name, email, groups

### **3. Debug Auth**
```
GET https://localhost:7170/api/AuthTest/debug
```
Expected: Shows authentication headers and identity details

### **4. Check Groups**
```
GET https://localhost:7170/api/AuthTest/groups
```
Expected: Shows all Windows groups the user belongs to

---

## 📋 Verify User Setup in Content Manager

Before testing, ensure CMUserUK exists in Content Manager:

1. **Check in Content Manager Admin**
   - Open Content Manager
   - Go to Administration → Locations
   - Search for "CMUserUK"
   - Verify the user exists

2. **Check Database (SQL)**
   ```sql
   SELECT uri, lcName, lcType, lcPrimaryEmail
   FROM TSLOCATION
   WHERE lcName LIKE '%CMUserUK%'
   ```

3. **Check User Mapping**
   - Windows username: `CMUserUK` or `.\CMUserUK` (local machine)
   - Should map to Content Manager location with same name

---

## 🔍 Expected Log Output

When CMUserUK logs in successfully, you should see:

```
🔐 [TRIM] Connecting to Content Manager as user: CMUserUK
✅ [TRIM] Connected successfully. Current TRIM user: CMUserUK
✅ [AUTH SUCCESS] User authenticated: CMUserUK using Negotiate
🍪 [COOKIE CREATED] Authentication cookie created for CMUserUK
```

---

## ⚠️ Common Issues

### **Issue 1: "User doesn't exist" in Content Manager**
**Error:** `Failed to connect to TRIM Content Manager`

**Fix:**
1. Open Content Manager Admin
2. Create a Location for user "CMUserUK"
3. Set location type to "Internal User"

### **Issue 2: "Access Denied"**
**Error:** `401 Unauthorized`

**Possible causes:**
- User exists in Windows but not in Content Manager
- User has no permissions in Content Manager
- Password incorrect

**Fix:**
```sql
-- Check if user exists in Content Manager
SELECT * FROM TSLOCATION WHERE lcName = 'CMUserUK'

-- Grant basic permissions (if needed)
-- This depends on your Content Manager setup
```

### **Issue 3: Incognito Still Shows ukhan2**
**Problem:** Browser still authenticating as ukhan2

**Reason:** Windows Authentication uses current Windows session, not browser session

**Fix:** Use Method 1 (runas) or Method 3 (Switch User)

---

## 📊 Test Checklist

- [ ] User CMUserUK created in Windows (Computer Management)
- [ ] Password set correctly: `9019566144@Uks`
- [ ] User exists in Content Manager (TSLOCATION table)
- [ ] Application is running (dotnet run)
- [ ] Windows Authentication enabled in appsettings.json
- [ ] Browser opened as CMUserUK (using runas)
- [ ] Can access https://localhost:7170/api/AuthTest/whoami
- [ ] Logs show CMUserUK as authenticated user
- [ ] Search results filtered by CMUserUK's ACL permissions

---

## 🎯 Quick Test Script

Save this as `test-cmuser.ps1`:

```powershell
# Test CMUserUK authentication
$apiBase = "https://localhost:7170"

Write-Host "Testing authentication for CMUserUK..." -ForegroundColor Cyan

# Test 1: Public endpoint (no auth)
Write-Host "`n1. Testing public endpoint..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$apiBase/api/AuthTest/public" -SkipCertificateCheck
    Write-Host "✅ Public endpoint works: $($response.Message)" -ForegroundColor Green
} catch {
    Write-Host "❌ Public endpoint failed: $_" -ForegroundColor Red
}

# Test 2: Auth required endpoint
Write-Host "`n2. Testing authenticated endpoint..." -ForegroundColor Yellow

$username = "CMUserUK"
$password = ConvertTo-SecureString "9019566144@Uks" -AsPlainText -Force
$credential = New-Object System.Management.Automation.PSCredential ($username, $password)

try {
    $response = Invoke-RestMethod -Uri "$apiBase/api/AuthTest/whoami" `
        -Credential $credential `
        -Authentication Negotiate `
        -SkipCertificateCheck

    Write-Host "✅ Authentication successful!" -ForegroundColor Green
    Write-Host "   Username: $($response.User.Username)" -ForegroundColor White
    Write-Host "   Display Name: $($response.User.DisplayName)" -ForegroundColor White
    Write-Host "   Authenticated: $($response.User.IsAuthenticated)" -ForegroundColor White
} catch {
    Write-Host "❌ Authentication failed: $_" -ForegroundColor Red
    Write-Host "   Possible causes:" -ForegroundColor Yellow
    Write-Host "   - User doesn't exist in Content Manager" -ForegroundColor Yellow
    Write-Host "   - Password incorrect" -ForegroundColor Yellow
    Write-Host "   - User has no permissions" -ForegroundColor Yellow
}
```

Run it:
```powershell
powershell -ExecutionPolicy Bypass -File test-cmuser.ps1
```

---

## 📞 Need Help?

Check the application logs at:
```
DocumentProcessingAPI.API/logs/documentprocessing-<date>.txt
```

Look for entries with:
- `[TRIM]` - Content Manager connection
- `[AUTH]` - Authentication events
- `CMUserUK` - User-specific logs
