# R4 驗收測試：Email Auth 與 User

本文件是 R4 的手動／可由 Codex 執行的本機驗收清單。只覆蓋 Register、Password Login、Logout、`GET /users/me`、`PATCH /users/me`，以及支撐它們的本機 PostgreSQL、health／readiness、Cookie 與基本 CORS 行為。

不要在此流程重建資料庫、執行 `docker compose down -v`、套用新 migration，或開始 R5。測試使用既有本機開發資料庫；本次建立的兩個合成會員會保留，供 R5、R6 繼續使用。

## 共用變數

在 repository root 的 PowerShell 設定一次。`$DbName`、`$DbUser`、`$DbPort` 請由本機 `.env` 的非秘密設定抄入；不要在文件、終端紀錄或 Git 放入 `POSTGRES_PASSWORD`。`$TestPassword` 是本機合成測資，不可替換為真實帳號密碼。

```powershell
$RunId = Get-Date -Format 'yyyyMMddHHmmss'
$ApiBase = 'http://localhost:5000'
$AllowedOrigin = 'http://localhost:3000'
$DbName = '<POSTGRES_DB from .env>'
$DbUser = '<POSTGRES_USER from .env>'
$DbPort = '<POSTGRES_PORT from .env, normally 5432>'
$TestPassword = "R4 local password $RunId!"
$MemberAEmail = "r4-member-a-$RunId@example.test"
$MemberBEmail = "r4-member-b-$RunId@example.test"
$MemberACookieJar = Join-Path $env:TEMP "custom-form-r4-a-$RunId.cookies"
$MemberBCookieJar = Join-Path $env:TEMP "custom-form-r4-b-$RunId.cookies"
$ShortTokenCookieJar = Join-Path $env:TEMP "custom-form-r4-expired-$RunId.cookies"
```

所有 API 呼叫使用 `curl.exe`，避免 PowerShell 對 `curl` 的別名行為。動態 JWT 只由 cookie jar 保存；不要把 token 貼入文件或提交。

---

## 1. 服務重啟、PostgreSQL、health／readiness 與 DBeaver

**目的：**確認保留資料的本機 PostgreSQL 已可連線，且 API 可重新啟動並對資料庫完成 readiness 檢查。

**前置條件與操作：**在 repository root 執行。API 啟動命令須保持在獨立終端執行，直到本測試及後續 API 測試完成。DBeaver 的連線測試是唯一需要人工操作的步驟。

```powershell
# 保留 named volume，不使用 down -v。
docker compose config --quiet
docker compose up -d
docker compose ps

# PostgreSQL 容器內實際連線；不需要在命令列提供密碼。
docker compose exec postgres psql -U $DbUser -d $DbName -c 'SELECT current_database(), current_user, now();'

# 在另一個 PowerShell 視窗啟動或重啟 API；停止舊 API 程序後才啟動此命令。
dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --launch-profile http --no-build

# 在本視窗確認工程端點。
curl.exe -i "$ApiBase/health"
curl.exe -i "$ApiBase/health/ready"

# 人工：DBeaver → New Database Connection → PostgreSQL → Test Connection。
# Host=localhost；Port=$DbPort；Database=$DbName；Username=$DbUser；
# Password=POSTGRES_PASSWORD from .env；SSL disabled（本機 loopback 預設）。
```

**預期結果：**Compose 的 `postgres` 為 healthy；`psql` 成功回傳資料庫與使用者。`GET /health` 回 `200` 和 `{"status":"healthy"}`；`GET /health/ready` 回 `200` 和 `Healthy`。人工 DBeaver Test Connection 成功。

**實際結果：**待執行。

---

## 2. 建立並保留兩個合成會員

**目的：**建立兩個可登入、active 的 R4 合成會員；Member A 用於正常流程，Member B 用於 Disabled User 測試後恢復。

**前置條件與操作：**已完成測試 1，並已設定共用變數。兩個帳號應保留，不要在測試末刪除。

```powershell
curl.exe -i -X POST "$ApiBase/api/v1/auth/register" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"{0}","password":"{1}","displayName":"R4 Member A"}}' -f $MemberAEmail, $TestPassword)

curl.exe -i -X POST "$ApiBase/api/v1/auth/register" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"{0}","password":"{1}","displayName":"R4 Member B"}}' -f $MemberBEmail, $TestPassword)

docker compose exec postgres psql -U $DbUser -d $DbName -c "SELECT email, display_name, status, password_hash IS NOT NULL AS password_hash_present FROM users WHERE email IN ('$MemberAEmail', '$MemberBEmail') ORDER BY email;"
```

**預期結果：**兩次 Register 均回 `201 Created`，各自 body 僅包含 `id`、`email`、`displayName`、`avatarUrl`，且沒有 `Set-Cookie: access_token`。SQL 顯示兩筆會員、`status=active`、`password_hash_present=true`；不得顯示或保存明文 password 欄位。

**實際結果：**待執行。

---

## 3. 正常流程：註冊後未登入 → 登入 → 查本人 → 修改 → 登出 → 再查本人

**目的：**驗證 R4 的主要使用路徑、Cookie-only 認證，以及登出只清除目前 client cookie。

**前置條件與操作：**已完成測試 2。先以 Member A 確認沒有 cookie；登入後只用 `$MemberACookieJar` 傳送 cookie。`curl.exe -b` 會自 jar 取得動態 token。

```powershell
# 註冊後尚未登入：沒有 cookie 時受保護端點必須拒絕。
curl.exe -i "$ApiBase/api/v1/users/me"

# Member A 登入並保存動態 access_token cookie。
curl.exe -i -c $MemberACookieJar -X POST "$ApiBase/api/v1/auth/login" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"{0}","password":"{1}"}}' -f $MemberAEmail, $TestPassword)

curl.exe -i -b $MemberACookieJar "$ApiBase/api/v1/users/me"

curl.exe -i -b $MemberACookieJar -X PATCH "$ApiBase/api/v1/users/me" `
  -H 'Content-Type: application/json' `
  --data '{"displayName":"R4 Member A Updated"}'

curl.exe -i -b $MemberACookieJar "$ApiBase/api/v1/users/me"

curl.exe -i -b $MemberACookieJar -c $MemberACookieJar -X POST "$ApiBase/api/v1/auth/logout"

# 使用已被 logout response 更新的 jar 再查本人。
curl.exe -i -b $MemberACookieJar "$ApiBase/api/v1/users/me"
```

**預期結果：**第一次 `GET /users/me` 為 `401 AUTH_REQUIRED`。登入回 `200 OK`、User body，並設定 `access_token` cookie，不在 JSON body 回 token。登入後 GET 回 `200` 且 email 為 Member A；PATCH 回 `200` 且 `displayName` 為 `R4 Member A Updated`；第二次 GET 保持同一值。Logout 回無 body 的 `204 No Content` 和清除 cookie 的 `Set-Cookie`。最後 GET 為 `401 AUTH_REQUIRED`。

**實際結果：**待執行。

---

## 4. 重複 Email

**目的：**確認 email 正規化後的唯一性與指定錯誤碼。

**前置條件與操作：**Member A 已由測試 2 建立。使用大寫及前後空白的相同 email，不建立第三位會員。

```powershell
curl.exe -i -X POST "$ApiBase/api/v1/auth/register" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"  {0}  ","password":"{1}","displayName":"Duplicate Attempt"}}' -f $MemberAEmail.ToUpperInvariant(), $TestPassword)
```

**預期結果：**`409 Conflict`；error envelope 的 `error.code` 為 `USER_EMAIL_ALREADY_EXISTS`。資料庫仍只有一筆 Member A email。

**實際結果：**待執行。

---

## 5. 錯誤密碼

**目的：**確認 Login 不洩漏帳號存在性，並以統一 Credential 錯誤回應。

**前置條件與操作：**Member A 為 active。使用錯誤 password；不要變更 Member A 資料。

```powershell
curl.exe -i -X POST "$ApiBase/api/v1/auth/login" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"{0}","password":"wrong local password"}}' -f $MemberAEmail)
```

**預期結果：**`401 Unauthorized`；error envelope 的 `error.code` 為 `AUTH_INVALID_CREDENTIALS`；不得設定有效登入 cookie。

**實際結果：**待執行。

---

## 6. 基本非法輸入

**目的：**確認 R4 公開端點與 User PATCH 拒絕基本不合法 request shape／值，並維持正式 error envelope。

**前置條件與操作：**Member A 已存在。先重新登入取得 jar；未登入 PATCH 用來確認 Authentication 先於 body validation。

```powershell
# 註冊：無效 email、過短 password、空 displayName。
curl.exe -i -X POST "$ApiBase/api/v1/auth/register" `
  -H 'Content-Type: application/json' `
  --data '{"email":"not-an-email","password":"short","displayName":"   "}'

# Login：缺少 password。
curl.exe -i -X POST "$ApiBase/api/v1/auth/login" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"{0}"}}' -f $MemberAEmail)

# 未登入 protected endpoint：認證錯誤必須優先於 readonly body 欄位驗證。
curl.exe -i -X PATCH "$ApiBase/api/v1/users/me" `
  -H 'Content-Type: application/json' `
  --data '{"email":"not-allowed@example.test"}'

# 取得有效 cookie 後，PATCH 拒絕 readonly email 欄位。
curl.exe -sS -c $MemberACookieJar -X POST "$ApiBase/api/v1/auth/login" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"{0}","password":"{1}"}}' -f $MemberAEmail, $TestPassword) | Out-Null
curl.exe -i -b $MemberACookieJar -X PATCH "$ApiBase/api/v1/users/me" `
  -H 'Content-Type: application/json' `
  --data '{"email":"not-allowed@example.test"}'
```

**預期結果：**前三個公開／未登入請求依序為 `400 VALIDATION_ERROR`、`400 VALIDATION_ERROR`、`401 AUTH_REQUIRED`。帶有效 cookie 的 readonly PATCH 為 `400 VALIDATION_ERROR`，`error.details` 含 path `email`。不產生新 User，Member A email 不改變。

**實際結果：**待執行。

---

## 7. 無效 Token 與 Logout 帶壞 Token

**目的：**確認受保護端點區分無效 JWT，且 Logout 不要求有效登入。

**前置條件與操作：**API 正常運行。此測試不需要產生或保存真實 JWT。

```powershell
curl.exe -i "$ApiBase/api/v1/users/me" `
  -H 'Cookie: access_token=not-a-jwt'

curl.exe -i -X POST "$ApiBase/api/v1/auth/logout" `
  -H 'Cookie: access_token=not-a-jwt'
```

**預期結果：**無效 Token 的 GET 回 `401 AUTH_TOKEN_INVALID`。帶壞 Token 的 Logout 回無 body 的 `204 No Content`，並回 `Set-Cookie` 清除 `access_token`。

**實際結果：**待執行。

---

## 8. 過期 Token

**目的：**確認受保護端點將已過期的、原由系統簽發的 JWT 映射為 `AUTH_TOKEN_EXPIRED`。

**前置條件與操作：**Member A 為 active。此測試暫時啟動第二個 Development API（port 5001），以 1 分鐘 lifetime 取得系統簽發的 token；原本 port 5000 API 不需停掉。等待至少 65 秒，避免 ClockSkew=0 下的邊界誤判。完成後在該第二個終端停止 API（Ctrl+C）；環境覆寫只屬該程序。

```powershell
# 在另一個 PowerShell 視窗啟動暫時 API；保持此程序運行。
$env:ASPNETCORE_URLS = 'http://localhost:5001'
$env:Jwt__LifetimeMinutes = '1'
dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --no-build

# 在本視窗登入暫時 API，將動態 token 放入獨立 jar。
$ShortApiBase = 'http://localhost:5001'
curl.exe -i -c $ShortTokenCookieJar -X POST "$ShortApiBase/api/v1/auth/login" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"{0}","password":"{1}"}}' -f $MemberAEmail, $TestPassword)

Start-Sleep -Seconds 65
curl.exe -i -b $ShortTokenCookieJar "$ShortApiBase/api/v1/users/me"

# 在第二個終端 Ctrl+C 停止暫時 API；原 port 5000 API 仍使用原設定。
```

**預期結果：**短 lifetime Login 回 `200` 並設定 cookie；等待後 GET 回 `401 Unauthorized`，error envelope 的 `error.code` 為 `AUTH_TOKEN_EXPIRED`。不得把測試 JWT 複製到文件。

**實際結果：**待執行。

---

## 9. Disabled User

**目的：**確認既有有效 cookie 與正確 password 都會在 User status 變成 disabled 後被拒絕，且錯誤 password 不洩漏 disabled 狀態。

**前置條件與操作：**Member B 必須保持本機合成帳號。先登入取得 cookie，再直接修改這一筆測資的 status；測試 13 會恢復它為 active。

```powershell
curl.exe -sS -c $MemberBCookieJar -X POST "$ApiBase/api/v1/auth/login" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"{0}","password":"{1}"}}' -f $MemberBEmail, $TestPassword) | Out-Null

docker compose exec postgres psql -U $DbUser -d $DbName -c "UPDATE users SET status = 'disabled'::user_status WHERE email = '$MemberBEmail';"

curl.exe -i -b $MemberBCookieJar "$ApiBase/api/v1/users/me"

curl.exe -i -X POST "$ApiBase/api/v1/auth/login" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"{0}","password":"{1}"}}' -f $MemberBEmail, $TestPassword)

curl.exe -i -X POST "$ApiBase/api/v1/auth/login" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"{0}","password":"wrong local password"}}' -f $MemberBEmail)
```

**預期結果：**SQL 更新一筆。既有 cookie 的 GET 與正確 password Login 都回 `403 AUTH_USER_DISABLED`；錯誤 password Login 回 `401 AUTH_INVALID_CREDENTIALS`。本測試結束時 Member B 暫時仍為 disabled，必須執行測試 13 恢復。

**實際結果：**待執行。

---

## 10. Login／Logout Cookie flags

**目的：**直接檢查 Development HTTP 下的登入與登出 Cookie scope／flag；不檢查或暴露 token 值。

**前置條件與操作：**Member A 為 active。檢視 response header 時遮蔽 token value，再保存驗收紀錄。

```powershell
curl.exe -i -c $MemberACookieJar -X POST "$ApiBase/api/v1/auth/login" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"{0}","password":"{1}"}}' -f $MemberAEmail, $TestPassword)

curl.exe -i -b $MemberACookieJar -c $MemberACookieJar -X POST "$ApiBase/api/v1/auth/logout"
```

**預期結果：**Login `Set-Cookie` 使用 `access_token`，含 `HttpOnly`、`SameSite=Lax`、`Path=/`、絕對 `Expires`；不含 `Domain`、`Max-Age`，且 Development HTTP 下不含 `Secure`。Logout 回 `204`，並以相同 Path／SameSite／HttpOnly／Secure scope 發出已過期的清除 cookie；不含 body。

**實際結果：**待執行。

---

## 11. 基本 CORS

**目的：**確認 Development 的明列 credentialed origin 可預檢，且未設定 origin 不會收到允許標頭或 wildcard。

**前置條件與操作：**API 以 Development 設定運行，`Cors:AllowedOrigins` 保持目前 `http://localhost:3000`；不修改設定檔。

```powershell
# 已設定 origin 的 preflight。
curl.exe -i -X OPTIONS "$ApiBase/api/v1/auth/login" `
  -H "Origin: $AllowedOrigin" `
  -H 'Access-Control-Request-Method: POST'

# 未設定 origin 的 preflight。
curl.exe -i -X OPTIONS "$ApiBase/api/v1/auth/login" `
  -H 'Origin: http://localhost:3001' `
  -H 'Access-Control-Request-Method: POST'
```

**預期結果：**第一個 request 回 `204 No Content`，含 `Access-Control-Allow-Origin: http://localhost:3000` 與 `Access-Control-Allow-Credentials: true`。第二個 request 不得含這兩個 allow header，且任何回應均不得使用 `Access-Control-Allow-Origin: *`。

**實際結果：**待執行。

---

## 12. 必要資料庫核對

**目的：**確認 R4 只寫入既有 `users` schema，兩個保留會員與修改結果符合 API 行為。

**前置條件與操作：**已完成測試 2、3、9；Member B 會在本測試後的恢復步驟重新啟用。此查詢不輸出 password hash 或 token。

```powershell
docker compose exec postgres psql -U $DbUser -d $DbName -c "SELECT email, display_name, status, password_hash IS NOT NULL AS password_hash_present, created_at, updated_at FROM users WHERE email IN ('$MemberAEmail', '$MemberBEmail') ORDER BY email;"

docker compose exec postgres psql -U $DbUser -d $DbName -c "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'users' AND column_name IN ('password', 'password_hash') ORDER BY column_name;"
```

**預期結果：**第一個查詢正好有 Member A、Member B 兩筆；Member A 的 `display_name` 是 `R4 Member A Updated`，兩筆 `password_hash_present=true`。Member B 在恢復前可顯示 `disabled`。第二個查詢只顯示 `password_hash`，不顯示明文 `password` 欄位。

**實際結果：**待執行。

---

## 13. 恢復測試狀態與最終服務確認

**目的：**恢復本流程暫時更改的 user status 與短 lifetime API，保留兩位正常合成會員，並確認標準服務仍正常。

**前置條件與操作：**已完成或中止任一前述測試也必須執行。若測試 8 啟動的 port 5001 API 尚在運行，先在其終端 Ctrl+C 停止；不要變更 appsettings 或 `.env`。

```powershell
# 只恢復本次測試改動的 Member B；兩個合成會員都保留。
docker compose exec postgres psql -U $DbUser -d $DbName -c "UPDATE users SET status = 'active'::user_status WHERE email = '$MemberBEmail';"

docker compose exec postgres psql -U $DbUser -d $DbName -c "SELECT email, display_name, status FROM users WHERE email IN ('$MemberAEmail', '$MemberBEmail') ORDER BY email;"

# 標準 API 仍使用 port 5000 和原本設定。
curl.exe -i "$ApiBase/health"
curl.exe -i "$ApiBase/health/ready"
```

**預期結果：**Member A 與 Member B 都保留且 `status=active`，可供後續 R5、R6 使用；不留下 port 5001 的暫時 API 程序或 `Jwt__LifetimeMinutes=1` 設定。標準 `/health` 與 `/health/ready` 都回 `200`。任何尚未執行、失敗或中止的測試必須維持「待執行」或記錄實際失敗，不得標示 PASS。

**實際結果：**待執行。
