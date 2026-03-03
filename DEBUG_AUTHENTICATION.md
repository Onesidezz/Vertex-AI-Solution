# Debug Windows Authentication for CMUserUK

## Quick Debugging Checklist

### ✅ Step 1: Verify User Exists

**Check Windows User:**
```cmd
net user CMUserUK
```
Expected: Shows user details

**Check Content Manager User:**
```sql
SELECT uri, lcName, lcType, lcPrimaryEmail
FROM TSLOCATION
WHERE lcName = 'CMUserUK'
```
Expected: Returns 1 row

---

### ✅ Step 2: Start Application with Logging

1. Open CMD/PowerShell
2. Navigate to project:
   ```cmd
   cd "C:\Users\ukhan2\Desktop\DocProcessing embeddings\DocumentProcessingAPI\DocumentProcessingAPI.API"
   ```
3. Start application:
   ```cmd
   dotnet run
   ```
4. **Keep this window open** to see real-time logs

---

### ✅ Step 3: Switch to CMUserUK

1. Press `Ctrl + Alt + Del`
2. Click "Switch User"
3. Login:
   - Username: `CMUserUK`
   - Password: `9019566144@Uks`

---

### ✅ Step 4: Test in Browser

**Open Browser and navigate to:**
```
https://localhost:7170/api/AuthTest/whoami
```

**Expected Success Response:**
```json
{
  "message": "Windows Authentication is working!",
  "user": {
    "username": "CMUserUK",
    "displayName": "CMUserUK",
    "isAuthenticated": true,
    "authenticationType": "Negotiate"
  }
}
```

**If you see this → Authentication is working! ✅**

---

### ✅ Step 5: Check Console Logs

Look at the console where `dotnet run` is running.

**Expected Log Output:**
```
🔐 [TRIM] Connecting to Content Manager as user: CMUserUK
✅ [TRIM] Connected successfully. Current TRIM user: CMUserUK
✅ [AUTH SUCCESS] User authenticated: CMUserUK using Negotiate
🍪 [COOKIE CREATED] Authentication cookie created for CMUserUK
```

---

### ✅ Step 6: Test Search Page

Navigate to:
```
https://localhost:7170/Search
```

**Expected Behavior:**
- Page loads immediately (no login prompt)
- You can perform searches
- Results are filtered by CMUserUK's permissions

**Test Search:**
1. Enter query: `test`
2. Click "Search"
3. Check console logs for ACL filtering:
   ```
   🔒 STEP 5: Applying ACL Filtering
   🔐 Checking ACL permissions for user: CMUserUK
   ```

---

## 🔍 Detailed Debugging Methods

### Method 1: Check Authentication Cookie

**Chrome/Edge:**
1. Press `F12` (Developer Tools)
2. Go to **Application** tab
3. Expand **Cookies** → `https://localhost:7170`
4. Look for: `DocumentProcessing.Auth`

**Should see:**
- Name: `DocumentProcessing.Auth`
- Value: (long encrypted string)
- HttpOnly: ✓
- Secure: ✓
- SameSite: Lax

**If cookie is missing:**
- Authentication failed
- Check logs for errors

---

### Method 2: Network Tab Analysis

1. Open Developer Tools (`F12`)
2. Go to **Network** tab
3. Clear network log
4. Navigate to `https://localhost:7170/Search`
5. Look at requests:

**Scenario A: First Visit (Not Authenticated)**
```
1. GET /Search → 302 Redirect to /Login
2. GET /Login → 401 Challenge (Negotiate)
3. GET /Login (with auth) → 302 Redirect to /Search
4. GET /Search → 200 OK (with Set-Cookie header)
```

**Scenario B: Already Authenticated**
```
1. GET /Search → 200 OK (with Cookie header)
```

---

### Method 3: Check HTTP Headers

**Request Headers (should include):**
```
Cookie: DocumentProcessing.Auth=<encrypted value>
```

**Response Headers (first time):**
```
Set-Cookie: DocumentProcessing.Auth=<value>; path=/; secure; httponly; samesite=lax
WWW-Authenticate: Negotiate
```

---

### Method 4: Test All Auth Endpoints

**1. Public Endpoint (No Auth):**
```
GET https://localhost:7170/api/AuthTest/public
```
Expected: 200 OK (always works)

**2. Auth Status:**
```
GET https://localhost:7170/api/AuthTest/status
```
Expected: Shows authentication state

**3. Current User:**
```
GET https://localhost:7170/api/AuthTest/whoami
```
Expected: Shows CMUserUK details

**4. Debug Info:**
```
GET https://localhost:7170/api/AuthTest/debug
```
Expected: Shows authentication headers and identity

**5. User Groups:**
```
GET https://localhost:7170/api/AuthTest/groups
```
Expected: Shows CMUserUK's Windows groups

---

## 🐛 Common Issues & Solutions

### Issue 1: "401 Unauthorized" on all requests

**Symptoms:**
- Every page returns 401
- No authentication happens

**Debug Steps:**
1. Check if Windows Auth is enabled:
   ```json
   // In appsettings.json
   "Authentication": {
     "EnableWindowsAuthentication": true  // Must be true
   }
   ```

2. Check browser console for errors

3. Check application logs for:
   ```
   Windows Authentication enabled
   ```

**Solution:**
- Ensure `EnableWindowsAuthentication: true` in appsettings.json
- Restart application

---

### Issue 2: Shows wrong user (ukhan2 instead of CMUserUK)

**Symptoms:**
- `/api/AuthTest/whoami` shows: `"username": "ukhan2"`
- Logs show: `Current TRIM user: ukhan2`

**Root Cause:**
- Browser is running under ukhan2's Windows session

**Solution:**
- **Switch Windows user** (don't just use incognito)
- Logout from Windows and login as CMUserUK
- Or use Remote Desktop to localhost as CMUserUK

---

### Issue 3: "Failed to connect to TRIM Content Manager"

**Symptoms:**
- Authentication works
- But application crashes when accessing search

**Debug Steps:**
1. Check if CMUserUK exists in Content Manager:
   ```sql
   SELECT * FROM TSLOCATION WHERE lcName = 'CMUserUK'
   ```

2. Check logs for:
   ```
   ❌ [TRIM] Database connection failed
   ```

**Solution:**
- Create user in Content Manager Admin Console
- Ensure username matches exactly: `CMUserUK`

---

### Issue 4: No logs appearing

**Symptoms:**
- Console shows minimal output
- No authentication logs

**Solution:**
1. Check appsettings.json has authentication logging enabled:
   ```json
   "Microsoft.AspNetCore.Authentication": "Debug",
   "Microsoft.AspNetCore.Authorization": "Debug"
   ```

2. Restart application

---

### Issue 5: Authentication loop (keeps redirecting)

**Symptoms:**
- Browser keeps redirecting between /Login and /Search
- Never actually logs in

**Debug Steps:**
1. Check browser network tab for redirect loop
2. Check console logs for authentication errors

**Solution:**
- Clear browser cookies
- Check if TRIM connection is failing
- Verify user exists in Content Manager

---

## 📊 Log Analysis Guide

### Successful Authentication Logs:

```
[INF] 🔐 [TRIM] Connecting to Content Manager as user: CMUserUK
[INF] ✅ [TRIM] Connected successfully. Current TRIM user: CMUserUK
[INF] ✅ [AUTH SUCCESS] User authenticated: CMUserUK using Negotiate
[INF] 🍪 [COOKIE CREATED] Authentication cookie created for CMUserUK
[INF] Login requested. ReturnUrl: /Search
```

### Failed Authentication Logs:

```
[WRN] ❌ [AUTH FAILED] Authentication failed for /Login: User not found
[WRN] ⚠️ [AUTH SERVICE] No Windows identity found in current HTTP context
[ERR] ❌ [TRIM] Database connection failed
```

### Search with ACL Filtering:

```
[INF] 📋 [TRIM] Fetching records for user: CMUserUK (TRIM: CMUserUK) with search: test
[INF] 🔒 STEP 5: Applying ACL Filtering
[INF] 🔐 Checking ACL permissions for user: CMUserUK
[INF] ✅ ACL filtering complete: 18 accessible out of 20 results
```

---

## 🎯 Quick Test Script

Save this as `test-auth-flow.ps1`:

```powershell
# Test Authentication Flow for CMUserUK
$apiBase = "https://localhost:7170"

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Testing Windows Authentication" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Test 1: Public endpoint
Write-Host "[1] Testing public endpoint (no auth required)..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$apiBase/api/AuthTest/public" -SkipCertificateCheck
    Write-Host "✅ PASSED" -ForegroundColor Green
} catch {
    Write-Host "❌ FAILED: $_" -ForegroundColor Red
}

# Test 2: Auth required endpoint
Write-Host "`n[2] Testing authenticated endpoint..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$apiBase/api/AuthTest/whoami" `
        -UseDefaultCredentials `
        -SkipCertificateCheck

    Write-Host "✅ PASSED: Authenticated as $($response.User.Username)" -ForegroundColor Green
    Write-Host "   Display Name: $($response.User.DisplayName)" -ForegroundColor White
    Write-Host "   Is Authenticated: $($response.User.IsAuthenticated)" -ForegroundColor White
    Write-Host "   Auth Type: $($response.User.AuthenticationType)" -ForegroundColor White
} catch {
    Write-Host "❌ FAILED: $_" -ForegroundColor Red
    Write-Host "`n   This is expected if running as different user than CMUserUK" -ForegroundColor Yellow
}

# Test 3: Search page
Write-Host "`n[3] Testing search page access..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "$apiBase/Search" `
        -UseDefaultCredentials `
        -SkipCertificateCheck

    if ($response.StatusCode -eq 200) {
        Write-Host "✅ PASSED: Search page accessible" -ForegroundColor Green
    }
} catch {
    Write-Host "❌ FAILED: $_" -ForegroundColor Red
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Test Complete" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan
```

**Run it:**
```powershell
powershell -ExecutionPolicy Bypass -File test-auth-flow.ps1
```

---

## 📁 Where to Find Logs

**Console Output:**
- Real-time logs in terminal where `dotnet run` is running

**Log Files:**
```
C:\Users\ukhan2\Desktop\DocProcessing embeddings\DocumentProcessingAPI\DocumentProcessingAPI.API\logs\documentprocessing-<date>.txt
```

**Filter logs by user:**
```powershell
# In PowerShell
Select-String -Path "logs\documentprocessing-*.txt" -Pattern "CMUserUK"
```

---

## ✅ Success Criteria

Authentication is working correctly when:

1. ✅ `/api/AuthTest/whoami` returns user details for CMUserUK
2. ✅ Console shows: `Current TRIM user: CMUserUK`
3. ✅ Browser has `DocumentProcessing.Auth` cookie
4. ✅ `/Search` page loads without login prompt
5. ✅ Search results are ACL-filtered for CMUserUK
6. ✅ Logs show successful TRIM connection

---

## 🆘 Still Having Issues?

1. **Check application is running:** `dotnet run` should show "Now listening on https://localhost:7170"
2. **Check port is not blocked:** Firewall or antivirus
3. **Check HTTPS certificate:** Trust the dev certificate with `dotnet dev-certs https --trust`
4. **Check Content Manager is accessible:** TRIM database connection working
5. **Check Windows user:** `net user CMUserUK` shows user exists
6. **Review full logs:** Check `logs\documentprocessing-*.txt` for errors

---

## 📝 Quick Command Reference

**Start application:**
```cmd
cd DocumentProcessingAPI.API
dotnet run
```

**Check logs:**
```powershell
Get-Content "logs\documentprocessing-$(Get-Date -Format 'yyyyMMdd').txt" -Tail 50
```

**Check user in database:**
```sql
SELECT * FROM TSLOCATION WHERE lcName = 'CMUserUK'
```

**Test authentication:**
```cmd
curl https://localhost:7170/api/AuthTest/whoami --negotiate -u : --insecure
```

---

Good luck with debugging! 🚀
