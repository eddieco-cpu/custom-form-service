# R4 驗收測試：Email Auth 與 User

本文件是 R4 的手動／可由 Codex 執行的本機驗收清單。只覆蓋 Register、Password Login、Logout、`GET /users/me`、`PATCH /users/me`，以及支撐它們的本機 PostgreSQL、health／readiness、Cookie 與基本 CORS 行為。

不要在此流程重建資料庫、執行 `docker compose down -v`、套用新 migration，或開始 R5。測試使用既有本機開發資料庫；本次建立的兩個合成會員會保留，供 R5、R6 繼續使用。

## 共用變數

在 repository root 的 PowerShell 設定一次。`$DbName`、`$DbUser`、`$DbPort` 請由本機 `.env` 的非秘密設定抄入；不要在文件、終端紀錄或 Git 放入 `POSTGRES_PASSWORD`。`$TestPassword` 是本機合成測資，不可替換為真實帳號密碼。

```powershell
# 與 R4_Manual_Tests.http 使用相同固定測資；若要重新註冊，先在兩份文件換成同一新 RunId。
$RunId = 'r4-acceptance-20261001-01'
$ApiBase = 'http://localhost:5000'
$AllowedOrigin = 'http://localhost:3000'
$DbName = '<POSTGRES_DB from .env>'
$DbUser = '<POSTGRES_USER from .env>'
$DbPort = '<POSTGRES_PORT from .env, normally 5432>'
$TestPassword = "R4 local password $RunId!"
$MemberAEmail = "r4-member-a-$RunId@example.test"
$MemberAEmailUpper = $MemberAEmail.ToUpperInvariant()
$MemberBEmail = "r4-member-b-$RunId@example.test"
$MemberACookieJar = Join-Path $env:TEMP "custom-form-r4-a-$RunId.cookies"
$MemberBCookieJar = Join-Path $env:TEMP "custom-form-r4-b-$RunId.cookies"
$ShortApiBase = 'http://localhost:5001'
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
# 先 build，後續所有 --no-build（包含測試 8 暫時 API）均使用這次 build 的程式。
dotnet build CustomFormService.sln --configuration Debug
dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --launch-profile http --no-build

# 在本視窗確認工程端點。
curl.exe -i "$ApiBase/health"
curl.exe -i "$ApiBase/health/ready"

# 人工：DBeaver → New Database Connection → PostgreSQL → Test Connection。
# Host=localhost；Port=$DbPort；Database=$DbName；Username=$DbUser；
# Password=POSTGRES_PASSWORD from .env；SSL disabled（本機 loopback 預設）。
```

**預期結果：**Compose 的 `postgres` 為 healthy；`psql` 成功回傳資料庫與使用者。`GET /health` 回 `200` 和 `{"status":"healthy"}`；`GET /health/ready` 回 `200` 和 `Healthy`。人工 DBeaver Test Connection 成功。

**實際結果：**PASS（2026-10-01）：Compose PostgreSQL container 為 healthy（`127.0.0.1:5432`）。初始預設建置 exit code 1；確認 SDK／`global.json` 一致後，執行 `dotnet build-server shutdown`，以 `dotnet restore --disable-build-servers -m:1` 和 `dotnet build --no-restore --disable-build-servers -m:1` 均 exit code 0。標準 API 在 `http://localhost:5000`；`/health`、`/health/ready` 均 200。人工 DBeaver Test Connection 已由使用者確認成功。

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

**實際結果：**PASS（2026-10-01）：Member A、B Register 均 201、未設定登入 Cookie；DB 顯示兩筆 `active` 會員，`password_hash_present=true`。

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

**實際結果：**PASS（2026-10-01）：未登入 GET 401 `AUTH_REQUIRED`；Login 200 並設定 Cookie；GET、PATCH、再次 GET 均 200，displayName 更新為 `R4 Member A Updated`；Logout 204 無 body 並清除 Cookie；最後 GET 401 `AUTH_REQUIRED`。

---

## 4. 重複 Email

**目的：**確認 email 正規化後的唯一性與指定錯誤碼。

**前置條件與操作：**Member A 已由測試 2 建立。使用大寫及前後空白的相同 email，不建立第三位會員。

```powershell
curl.exe -i -X POST "$ApiBase/api/v1/auth/register" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"  {0}  ","password":"{1}","displayName":"Duplicate Attempt"}}' -f $MemberAEmailUpper, $TestPassword)
```

**預期結果：**`409 Conflict`；error envelope 的 `error.code` 為 `USER_EMAIL_ALREADY_EXISTS`。資料庫仍只有一筆 Member A email。

**實際結果：**PASS（2026-10-01）：帶大寫及前後空白的 Member A email Register 回 409 `USER_EMAIL_ALREADY_EXISTS`。

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

**實際結果：**PASS（2026-10-01）：錯誤 password Login 回 401 `AUTH_INVALID_CREDENTIALS`。

---

## 5a. 不存在 Email 登入

**目的：**確認不存在的 email 與錯誤 password 對外使用相同 Credential 錯誤，不洩漏帳號存在性。

**前置條件與操作：**API 正常運行。此 email 不用註冊，且不改動保留會員。

```powershell
$UnknownEmail = "r4-unknown-$RunId@example.test"
curl.exe -i -X POST "$ApiBase/api/v1/auth/login" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"{0}","password":"wrong local password"}}' -f $UnknownEmail)
```

**預期結果：**`401 Unauthorized`；error envelope 的 `error.code` 為 `AUTH_INVALID_CREDENTIALS`；不得設定有效登入 cookie 或建立 User。

**實際結果：**PASS（2026-10-01）：不存在 email Login 回 401 `AUTH_INVALID_CREDENTIALS`；DB 核對該 email User count 為 0。

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

**實際結果：**PASS（2026-10-01）：無效 Register、缺少 password Login 均回 400 `VALIDATION_ERROR`；未登入 readonly PATCH 回 401 `AUTH_REQUIRED`；帶有效 Cookie 的 readonly PATCH 回 400 `VALIDATION_ERROR` 且 details 含 `email`。

---

## 6a. 空白 displayName PATCH

**目的：**確認已認證使用者不能將 `displayName` 改為 trim 後的空字串。

**前置條件與操作：**Member A 為 active。登入取得新的 cookie jar，再送出只有空白的 `displayName`。

```powershell
curl.exe -sS -o NUL -c $MemberACookieJar -X POST "$ApiBase/api/v1/auth/login" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"{0}","password":"{1}"}}' -f $MemberAEmail, $TestPassword)

curl.exe -i -b $MemberACookieJar -X PATCH "$ApiBase/api/v1/users/me" `
  -H 'Content-Type: application/json' `
  --data '{"displayName":"   "}'
```

**預期結果：**PATCH 回 `400 Bad Request`，error envelope 的 `error.code` 為 `VALIDATION_ERROR`，且 `error.details` 含 path `displayName`。Member A 的既有 `displayName` 不變。

**實際結果：**PASS（2026-10-01）：帶有效 Cookie 的空白 displayName PATCH 回 400 `VALIDATION_ERROR`，details 含 `displayName`。

---

## 6b. 空 PATCH no-op

**目的：**確認 `{}` 是合法 no-op，回傳目前 User 且不更新 `updated_at`。

**前置條件與操作：**Member A 為 active。重新登入，先讀取 `updated_at`，再傳送空 JSON object；時間值只在記憶體比較，不寫入文件。

```powershell
curl.exe -sS -o NUL -c $MemberACookieJar -X POST "$ApiBase/api/v1/auth/login" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"{0}","password":"{1}"}}' -f $MemberAEmail, $TestPassword)

$UpdatedAtBefore = (docker compose exec -T postgres psql -X -q -t -A -U $DbUser -d $DbName -c "SELECT updated_at FROM users WHERE email = '$MemberAEmail';").Trim()
curl.exe -i -b $MemberACookieJar -X PATCH "$ApiBase/api/v1/users/me" `
  -H 'Content-Type: application/json' `
  --data '{}'
$UpdatedAtAfter = (docker compose exec -T postgres psql -X -q -t -A -U $DbUser -d $DbName -c "SELECT updated_at FROM users WHERE email = '$MemberAEmail';").Trim()
if ($UpdatedAtBefore -ne $UpdatedAtAfter) { throw 'Empty PATCH changed updated_at.' }
```

**預期結果：**PATCH 回 `200 OK` 與目前 Member A User；前後 `updated_at` 相同。不可新增或修改任何 profile 欄位。

**實際結果：**PASS（2026-10-01）：空 PATCH 回 200；DB 核對前後 `updated_at` 相同。

---

## 7. 無效 Token

**目的：**確認受保護端點將格式無效的 JWT 區分為 `AUTH_TOKEN_INVALID`。

**前置條件與操作：**API 正常運行。此測試不需要產生或保存真實 JWT。

```powershell
curl.exe -i "$ApiBase/api/v1/users/me" `
  -H 'Cookie: access_token=not-a-jwt'
```

**預期結果：**GET 回 `401 Unauthorized`；error envelope 的 `error.code` 為 `AUTH_TOKEN_INVALID`。

**實際結果：**PASS（2026-10-01）：`access_token=not-a-jwt` 的 GET 回 401 `AUTH_TOKEN_INVALID`。

---

## 7a. 無 Cookie Logout

**目的：**確認 Logout 沒有 `access_token` 仍成功並清除 client cookie scope。

**前置條件與操作：**API 正常運行。此 request 不得使用 `-b`、`-c` 或手動 Cookie header。

```powershell
curl.exe -i -X POST "$ApiBase/api/v1/auth/logout"
```

**預期結果：**無 body 的 `204 No Content`，並有清除 `access_token` 的 `Set-Cookie`；不回 Authentication error。

**實際結果：**PASS（2026-10-01）：無 Cookie Logout 回 204、無 body，並設定清除 Cookie。

---

## 7b. Logout 帶壞 Token

**目的：**確認 Logout 不會因無效 Token 被 Authentication middleware 攔截。

**前置條件與操作：**API 正常運行。此測試不需要產生或保存真實 JWT。

```powershell
curl.exe -i -X POST "$ApiBase/api/v1/auth/logout" `
  -H 'Cookie: access_token=not-a-jwt'
```

**預期結果：**無 body 的 `204 No Content`，並有清除 `access_token` 的 `Set-Cookie`；不回 `AUTH_TOKEN_INVALID`。

**實際結果：**PASS（2026-10-01）：帶壞 Cookie Logout 回 204、無 body，並設定清除 Cookie。

---

## 8. 過期 Token

**目的：**確認受保護端點將已過期的、原由系統簽發的 JWT 映射為 `AUTH_TOKEN_EXPIRED`。

**前置條件與操作：**Member A 為 active，且測試 1 的 build 已成功完成。暫時啟動第二個明確為 Development、監聽 `http://localhost:5001` 的 API，以 1 分鐘 lifetime 取得系統簽發的 token；原本 port 5000 API 不需停掉。登入後立刻從 cookie jar 讀入 token 到記憶體，刪除 jar；等待至少 65 秒後以明確 `Cookie` header 傳送該記憶體 token，避免 curl 因 cookie 已過期而不送出。完成後在第二個終端停止 API（Ctrl+C）；環境覆寫只屬該程序。

```powershell
# 在另一個 PowerShell 視窗啟動暫時 API；保持此程序運行。
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:5001'
$env:Jwt__LifetimeMinutes = '1'
dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --no-launch-profile --no-build

# 在本視窗確認實際監聽位置，再登入暫時 API；login header/body 均不輸出。
curl.exe -i "$ShortApiBase/health"
$ShortLoginStatus = curl.exe -sS -o NUL -w '%{http_code}' -c $ShortTokenCookieJar -X POST "$ShortApiBase/api/v1/auth/login" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"{0}","password":"{1}"}}' -f $MemberAEmail, $TestPassword)
if ($ShortLoginStatus -ne '200') { throw "Short-lifetime login returned HTTP $ShortLoginStatus." }
$ShortAccessToken = ((Select-String -Path $ShortTokenCookieJar -SimpleMatch "`taccess_token`t" | Select-Object -Last 1).Line -split "`t")[-1]
if ([string]::IsNullOrWhiteSpace($ShortAccessToken)) { throw 'Short-lifetime login did not write access_token to the cookie jar.' }
Remove-Item -LiteralPath $ShortTokenCookieJar

Start-Sleep -Seconds 65
curl.exe -i "$ShortApiBase/api/v1/users/me" `
  -H "Cookie: access_token=$ShortAccessToken"
$ShortAccessToken = $null

# 在第二個終端 Ctrl+C 停止暫時 API；原 port 5000 API 仍使用原設定。
```

**預期結果：**暫時 API console 顯示監聽 `http://localhost:5001`，`GET /health` 回 `200`；短 lifetime Login 回 `200` 並設定 cookie。等待後，明確帶 Cookie header 的 GET 回 `401 Unauthorized`，error envelope 的 `error.code` 為 `AUTH_TOKEN_EXPIRED`。不得輸出或把測試 JWT 複製到文件。

**實際結果：**PASS（2026-10-01）：暫時 Development API 在 `http://localhost:5001`，health 200、短效 Login 200。等待 65 秒後以明確 Cookie header GET 回 401 `AUTH_TOKEN_EXPIRED`；已停止 port 5001 暫時 API 並刪除暫存 token。

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

**實際結果：**PASS（2026-10-01）：Member B 暫設 disabled 後，既有 Cookie GET 與正確 password Login 均回 403 `AUTH_USER_DISABLED`；錯誤 password Login 回 401 `AUTH_INVALID_CREDENTIALS`。Member B 已於測試 13 恢復 active。

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

**實際結果：**PASS（2026-10-01）：Development Login Cookie 含 `HttpOnly`、`SameSite=Lax`、`Path=/`、`Expires`，且不含 `Domain`、`Max-Age`、`Secure`；Logout 回 204 無 body 並以相同 scope 清除 Cookie。未輸出 token。

---

## 11. 基本 CORS（標頭 smoke test）

**目的：**以預檢回應標頭快速確認 Development 的明列 credentialed origin 可預檢，且未設定 origin 不會收到允許標頭或 wildcard；這不是完整 CORS 矩陣。

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

**實際結果：**PASS（2026-10-01）：允許 origin preflight 回 204，含精確 `Access-Control-Allow-Origin: http://localhost:3000` 與 `Access-Control-Allow-Credentials: true`；未設定 origin 未含兩者，且未使用 wildcard。

---

## 12. 必要資料庫核對

**目的：**確認 R4 只寫入既有 `users` schema，兩個保留會員與修改結果符合 API 行為。

**前置條件與操作：**已完成測試 2、3、9；Member B 會在本測試後的恢復步驟重新啟用。此查詢不輸出 password hash 或 token。

```powershell
# 透過標準輸入傳送 SQL；不輸出 TestPassword 或 password_hash。
$QuotedTestPassword = $TestPassword.Replace("'", "''")
$HashCheckSql = @"
SELECT email, display_name, status,
       password_hash IS NOT NULL AS password_hash_present,
       password_hash IS DISTINCT FROM '$QuotedTestPassword' AS password_hash_is_not_plaintext,
       created_at, updated_at
FROM users
WHERE email IN ('$MemberAEmail', '$MemberBEmail')
ORDER BY email;
"@
$HashCheckSql | docker compose exec -T postgres psql -X -q -v ON_ERROR_STOP=1 -U $DbUser -d $DbName

docker compose exec postgres psql -U $DbUser -d $DbName -c "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'users' AND column_name IN ('password', 'password_hash') ORDER BY column_name;"
```

**預期結果：**第一個查詢正好有 Member A、Member B 兩筆；Member A 的 `display_name` 是 `R4 Member A Updated`，兩筆 `password_hash_present=true`，且 `password_hash_is_not_plaintext=true`。查詢只輸出布林判定，不輸出 password 或 hash 值。Member B 在恢復前可顯示 `disabled`。第二個查詢只顯示 `password_hash`，不顯示明文 `password` 欄位。

**實際結果：**PASS（2026-10-01）：兩會員 `password_hash_present=true` 且 `password_hash_is_not_plaintext=true`；只輸出布林結果，未輸出 hash 或 password。`users` 欄位核對只顯示 `password_hash`；Member A 為更新後名稱、Member B 在恢復前為 disabled。

---

## 13. 恢復測試狀態與最終服務確認

**目的：**恢復本流程暫時更改的 user status 與短 lifetime API，保留兩位正常合成會員，並確認標準服務仍正常。

**前置條件與操作：**已完成或中止任一前述測試也必須執行。若測試 8 啟動的 port 5001 API 尚在運行，先在其終端 Ctrl+C 停止；不要變更 appsettings 或 `.env`。

```powershell
# 只恢復本次測試改動的 Member B；兩個合成會員都保留。
docker compose exec postgres psql -U $DbUser -d $DbName -c "UPDATE users SET status = 'active'::user_status WHERE email = '$MemberBEmail';"

docker compose exec postgres psql -U $DbUser -d $DbName -c "SELECT email, display_name, status FROM users WHERE email IN ('$MemberAEmail', '$MemberBEmail') ORDER BY email;"

# 最後重新登入兩位保留會員，再以 Cookie 確認各自仍可查本人；login response 不輸出。
$MemberALoginStatus = curl.exe -sS -o NUL -w '%{http_code}' -c $MemberACookieJar -X POST "$ApiBase/api/v1/auth/login" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"{0}","password":"{1}"}}' -f $MemberAEmail, $TestPassword)
$MemberBLoginStatus = curl.exe -sS -o NUL -w '%{http_code}' -c $MemberBCookieJar -X POST "$ApiBase/api/v1/auth/login" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"{0}","password":"{1}"}}' -f $MemberBEmail, $TestPassword)
if ($MemberALoginStatus -ne '200' -or $MemberBLoginStatus -ne '200') { throw "Retained-member login failed: A=$MemberALoginStatus, B=$MemberBLoginStatus." }
curl.exe -i -b $MemberACookieJar "$ApiBase/api/v1/users/me"
curl.exe -i -b $MemberBCookieJar "$ApiBase/api/v1/users/me"

# 標準 API 仍使用 port 5000 和原本設定。
curl.exe -i "$ApiBase/health"
curl.exe -i "$ApiBase/health/ready"

# 不保留含 JWT 的暫存 cookie jar。
Remove-Item -LiteralPath $MemberACookieJar, $MemberBCookieJar, $ShortTokenCookieJar -ErrorAction SilentlyContinue
```

**預期結果：**Member A 與 Member B 都保留且 `status=active`，可供後續 R5、R6 使用；兩人最後 Login 都回 `200`，各自 `GET /users/me` 回 `200` 且對應正確 email。不留下 port 5001 的暫時 API 程序或 `Jwt__LifetimeMinutes=1` 設定；暫存 cookie jar 已移除。標準 `/health` 與 `/health/ready` 都回 `200`。任何尚未執行、失敗或中止的測試必須維持「待執行」或記錄實際失敗，不得標示 PASS。

**實際結果：**PASS（2026-10-01）：Member B 已恢復 `active`；兩位保留會員皆重新 Login 200、GET `/users/me` 200 且 email 正確。標準 `/health`、`/health/ready` 均 200；port 5001 暫時 API 與暫存 token／cookie jar 已清理。Compose PostgreSQL 保持 healthy。
