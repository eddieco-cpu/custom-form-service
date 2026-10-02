# R5 驗收測試：Form 基礎讀寫

本計畫只驗證 R5 的四支 API：`POST /api/v1/forms`、`GET /api/v1/forms`、`GET /api/v1/forms/{formId}`、`DELETE /api/v1/forms/{formId}`。驗收使用既有本機開發 PostgreSQL 與 R4 保留的兩位合成會員；不重建資料庫、不執行 `docker compose down -v`，也不開始 R6。

本輪驗收會保留建立的 R5 合成 Form（含一筆 Soft Delete Form）以供後續 Round 使用。只刪除暫存 cookie jar 與 HTTP response 檔案。

## 共用變數與前置條件

在 repository root 的 PowerShell 設定一次。R4 使用的兩位會員和其合成密碼已存在於 `R4_Acceptance_Test.md`；不要將 JWT、資料庫密碼或任何真實帳密寫入文件或 Git。

```powershell
$R4RunId = 'r4-acceptance-20261001-01'
$RunId = 'r5-acceptance-20261002-01'
$ApiBase = 'http://localhost:5000'
$DbName = '<POSTGRES_DB from .env>'
$DbUser = '<POSTGRES_USER from .env>'
$TestPassword = "R4 local password $R4RunId!"
$MemberAEmail = "r4-member-a-$R4RunId@example.test"
$MemberBEmail = "r4-member-b-$R4RunId@example.test"
$MemberACookieJar = Join-Path $env:TEMP "custom-form-r5-a-$RunId.cookies"
$MemberBCookieJar = Join-Path $env:TEMP "custom-form-r5-b-$RunId.cookies"
$CreateResponsePath = Join-Path $env:TEMP "custom-form-r5-create-$RunId.json"
$DeleteResponsePath = Join-Path $env:TEMP "custom-form-r5-delete-$RunId.json"
$NestedTitle = "R5 nested draft $RunId"
$AtomicFailureTitle = "R5 atomic failure $RunId"
$DeleteTitle = "R5 soft delete $RunId"
```

### 1. 確認本機服務與保留會員

**目的：**確認 API、既有開發 DB，以及 R4 的兩位 active 合成會員仍可使用；不變更其 Profile 或 status。

**操作：**先在另一個終端啟動標準 Development API，並在本視窗執行：

```powershell
docker compose config --quiet
docker compose up -d
docker compose ps

# 另一個 PowerShell 視窗：
# dotnet build src/CustomFormService.Api/CustomFormService.Api.csproj --configuration Debug
# dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --launch-profile http --no-build

curl.exe -i "$ApiBase/health"
curl.exe -i "$ApiBase/health/ready"

$MemberALoginStatus = curl.exe -sS -o NUL -w '%{http_code}' -c $MemberACookieJar -X POST "$ApiBase/api/v1/auth/login" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"{0}","password":"{1}"}}' -f $MemberAEmail, $TestPassword)
$MemberBLoginStatus = curl.exe -sS -o NUL -w '%{http_code}' -c $MemberBCookieJar -X POST "$ApiBase/api/v1/auth/login" `
  -H 'Content-Type: application/json' `
  --data ('{{"email":"{0}","password":"{1}"}}' -f $MemberBEmail, $TestPassword)
if ($MemberALoginStatus -ne '200' -or $MemberBLoginStatus -ne '200') {
  throw "Retained-member login failed: A=$MemberALoginStatus; B=$MemberBLoginStatus."
}
```

**預期結果：**Compose PostgreSQL healthy；`/health`、`/health/ready` 都為 `200`。兩位會員 Login 都為 `200`，並各自只在暫存 cookie jar 保存 `access_token`。

**實際結果：**未執行（等待 Owner review）。

### 2. 巢狀 Draft 建立、預設值、排序與資料庫寫入

**目的：**確認 Owner 可在一次 `POST` 原子建立 Form、Question、Option；Draft 可以含不完整 Rating config，並正規化文字、套用 Form default、依 sortOrder 回傳巢狀集合。

**操作：**

```powershell
$NestedBody = @'
{
  "title": "  R5 nested draft r5-acceptance-20261002-01  ",
  "description": " \u2003 ",
  "questions": [
    {
      "type": "single_choice",
      "title": "  Select one  ",
      "description": " \u2003 ",
      "required": true,
      "sortOrder": 4,
      "config": { "variant": "radio" },
      "options": [
        { "label": " Second ", "value": "second", "sortOrder": 1 },
        { "label": " First ", "value": "first", "sortOrder": 0 }
      ]
    },
    {
      "type": "rating",
      "title": "Rate this draft",
      "sortOrder": 2,
      "config": { "min": 1 }
    }
  ]
}
'@

$CreateStatus = curl.exe -sS -o $CreateResponsePath -w '%{http_code}' -b $MemberACookieJar -X POST "$ApiBase/api/v1/forms" `
  -H 'Content-Type: application/json' --data $NestedBody
if ($CreateStatus -ne '201') { throw "Nested create returned HTTP $CreateStatus." }
$NestedForm = Get-Content -Raw $CreateResponsePath | ConvertFrom-Json
$NestedFormId = $NestedForm.id
$NestedForm

# 僅讀取 R5 建立資料，不輸出密碼、hash 或 token。
docker compose exec postgres psql -X -q -U $DbUser -d $DbName -c "
SELECT id, title, description, status, response_mode, response_identity, result_visibility,
       deleted_at IS NULL AS not_deleted
FROM forms WHERE id = '$NestedFormId'::uuid;
SELECT COUNT(*) AS question_count FROM questions WHERE form_id = '$NestedFormId'::uuid;
SELECT COUNT(*) AS option_count FROM options
WHERE question_id IN (SELECT id FROM questions WHERE form_id = '$NestedFormId'::uuid);"
```

**預期結果：**回 `201 Created` 和 FormDetail。Form `title` 為 `$NestedTitle`；`description` 為 `null`；`status=draft`、`responseMode=single`、`responseIdentity=anonymous`、`resultVisibility=owner_only`、`submissionDeadlineAt=null`。Questions 先回 Rating (`sortOrder=2`) 再回 Single Choice (`sortOrder=4`)；Choice Options 先回 `First` (`sortOrder=0`) 再回 `Second`。資料庫有 1 Form、2 Questions、2 Options，且 `deleted_at` 為 `NULL`。

**實際結果：**未執行（等待 Owner review）。

### 3. 巢狀定義失敗不留下部分資料

**目的：**確認同一 Question 內重複 Option value 被拒絕，且 Create 的 Form／Question／Option 寫入完全原子。

**操作：**

```powershell
$InvalidNestedBody = @'
{
  "title": "R5 atomic failure r5-acceptance-20261002-01",
  "questions": [
    {
      "type": "single_choice",
      "title": "Duplicate values",
      "sortOrder": 0,
      "options": [
        { "label": "One", "value": "duplicate", "sortOrder": 0 },
        { "label": "Two", "value": "duplicate", "sortOrder": 1 }
      ]
    }
  ]
}
'@

curl.exe -i -b $MemberACookieJar -X POST "$ApiBase/api/v1/forms" `
  -H 'Content-Type: application/json' --data $InvalidNestedBody

docker compose exec postgres psql -X -q -U $DbUser -d $DbName -c "
SELECT COUNT(*) AS invalid_form_count
FROM forms WHERE title = '$AtomicFailureTitle';"
```

**預期結果：**API 回 `400 Bad Request`，error envelope 的 `error.code` 為 `FORM_DEFINITION_INVALID`，details 指向第二個重複 Option value。DB `invalid_form_count=0`；不得留下對應 Question 或 Option。

**實際結果：**未執行（等待 Owner review）。

### 4. Owner List、分頁、篩選與查詢錯誤

**目的：**確認 List 只回 Member A 的未刪除 Form、使用正式分頁與排序，並拒絕非法 query。

**操作：**先建立一張較新的 Draft 作為分頁排序樣本，再依序列出：

```powershell
$DeleteBody = @'
{
  "title": "R5 soft delete r5-acceptance-20261002-01",
  "questions": [
    {
      "type": "single_choice",
      "title": "Keep definition after delete",
      "sortOrder": 0,
      "options": [
        { "label": "Only option", "value": "only", "sortOrder": 0 }
      ]
    }
  ]
}
'@

Start-Sleep -Milliseconds 20
$DeleteCreateStatus = curl.exe -sS -o $DeleteResponsePath -w '%{http_code}' -b $MemberACookieJar -X POST "$ApiBase/api/v1/forms" `
  -H 'Content-Type: application/json' --data $DeleteBody
if ($DeleteCreateStatus -ne '201') { throw "Delete-sample create returned HTTP $DeleteCreateStatus." }
$DeleteForm = Get-Content -Raw $DeleteResponsePath | ConvertFrom-Json
$DeleteFormId = $DeleteForm.id

curl.exe -i -b $MemberACookieJar "$ApiBase/api/v1/forms?page=1&pageSize=1"
curl.exe -i -b $MemberACookieJar "$ApiBase/api/v1/forms?page=3&pageSize=1"
curl.exe -i -b $MemberACookieJar "$ApiBase/api/v1/forms?status=draft"
curl.exe -i -b $MemberBCookieJar "$ApiBase/api/v1/forms"
curl.exe -i -b $MemberACookieJar "$ApiBase/api/v1/forms?page=0"
```

**預期結果：**Member A 的第一頁回 `200`，`items` 只有 `$DeleteTitle`、其 `count=0`；pagination 是 `page=1`、`pageSize=1`、`totalItems=2`、`totalPages=2`。第 3 頁回 `200`、`items=[]` 並保留 `page=3`。`status=draft` 回兩張 R5 Draft。Member B 的 List 不得包含 `$NestedFormId` 或 `$DeleteFormId`。`page=0` 回 `400 VALIDATION_ERROR`。

**實際結果：**未執行（等待 Owner review）。

### 5. Draft Detail 存在性隱藏與 Owner 管理授權

**目的：**確認 UUID URL 不授予讀取或管理權；Draft Detail 只讓 Owner 讀取，管理操作對非 Owner 回 403，且 Protected Endpoint 的 Authentication 先於 path 驗證。

**操作：**

```powershell
# Owner 可以讀 Draft。
curl.exe -i -b $MemberACookieJar "$ApiBase/api/v1/forms/$NestedFormId"

# 另一位已登入會員不可探查 Draft 存在性，但同一會員的管理操作為 403。
curl.exe -i -b $MemberBCookieJar "$ApiBase/api/v1/forms/$NestedFormId"
curl.exe -i -b $MemberBCookieJar -X DELETE "$ApiBase/api/v1/forms/$NestedFormId"

# 無登入時 Authentication Error 優先於 invalid formId。
curl.exe -i "$ApiBase/api/v1/forms/not-a-uuid"

# 已登入才驗證 path UUID。
curl.exe -i -b $MemberACookieJar "$ApiBase/api/v1/forms/not-a-uuid"
```

**預期結果：**Owner Detail 回 `200` FormDetail。Member B Detail 回 `404 FORM_NOT_FOUND`，Member B Delete 回 `403 FORM_ACCESS_DENIED`。無 Cookie 的 invalid ID 回 `401 AUTH_REQUIRED`；Member A 帶有效 Cookie 的 invalid ID 回 `400 VALIDATION_ERROR`。兩張 Form 與定義資料均不變。

**實際結果：**未執行（等待 Owner review）。

### 6. Owner Soft Delete 與資料保留邊界

**目的：**確認 Owner Delete 僅設定 Soft Delete，刪除後所有一般 Form 讀取均視為不存在，且 Question／Option 資料仍在 DB。

**操作：**

```powershell
curl.exe -i -b $MemberACookieJar -X DELETE "$ApiBase/api/v1/forms/$DeleteFormId"

# 刪除重試與一般 Form 查詢邊界。
curl.exe -i -b $MemberACookieJar -X DELETE "$ApiBase/api/v1/forms/$DeleteFormId"
curl.exe -i -b $MemberACookieJar "$ApiBase/api/v1/forms/$DeleteFormId"
curl.exe -i -b $MemberBCookieJar "$ApiBase/api/v1/forms/$DeleteFormId"
curl.exe -i -b $MemberACookieJar "$ApiBase/api/v1/forms?status=draft"

# 僅核對 Soft Delete 與定義 rows；不做任何 DELETE 或 rebuild。
docker compose exec postgres psql -X -q -U $DbUser -d $DbName -c "
SELECT deleted_at IS NOT NULL AS soft_deleted, status
FROM forms WHERE id = '$DeleteFormId'::uuid;
SELECT COUNT(*) AS retained_questions FROM questions WHERE form_id = '$DeleteFormId'::uuid;
SELECT COUNT(*) AS retained_options FROM options
WHERE question_id IN (SELECT id FROM questions WHERE form_id = '$DeleteFormId'::uuid);"
```

**預期結果：**第一次 Delete 回無 body 的 `204 No Content`。重複 Delete、Member A Detail、Member B Detail 都回 `404 FORM_NOT_FOUND`。Member A draft List 僅保留 `$NestedTitle`。DB 顯示 `soft_deleted=true`、status 仍為 `draft`、`retained_questions=1`、`retained_options=1`。

**實際結果：**未執行（等待 Owner review）。

### 7. 清理暫存狀態並保留合成資料

**目的：**移除驗收暫存 token／response 檔案，確認 R4 會員與 R5 合成 Form 都保留供後續 Round 使用。

**操作：**

```powershell
Remove-Item -LiteralPath $MemberACookieJar, $MemberBCookieJar, $CreateResponsePath, $DeleteResponsePath -ErrorAction SilentlyContinue

docker compose exec postgres psql -X -q -U $DbUser -d $DbName -c "
SELECT email, status FROM users
WHERE email IN ('$MemberAEmail', '$MemberBEmail') ORDER BY email;
SELECT title, status, deleted_at IS NOT NULL AS soft_deleted
FROM forms WHERE title IN ('$NestedTitle', '$DeleteTitle') ORDER BY title;"
```

**預期結果：**兩位 R4 會員仍為 `active`。`$NestedTitle` 保持未刪除 Draft；`$DeleteTitle` 保留為 Soft Deleted Draft。暫存 cookie 與 response 檔案已移除，沒有停止 PostgreSQL、刪除 volume 或改動設定。

**實際結果：**未執行（等待 Owner review）。

## 本次刻意不擴充的邊界

本計畫聚焦 R5 主行為、Owner／Draft 隱藏、DB 寫入與代表性錯誤。所有 Question config 變體、Unicode code-point 極限、PATCH Delta、Publish／Close、Submission 與 Statistics 仍各自依正式規格在 R6～R10 驗收；這些項目未因本計畫精簡而放寬。
