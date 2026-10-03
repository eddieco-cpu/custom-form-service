# R5 驗收測試：Form 基礎讀寫

本計畫只驗證 R5 的四支 API：`POST /api/v1/forms`、`GET /api/v1/forms`、`GET /api/v1/forms/{formId}`、`DELETE /api/v1/forms/{formId}`。驗收使用既有本機開發 PostgreSQL 與 R4 保留的兩位合成會員；不重建資料庫、不執行 `docker compose down -v`，也不開始 R6。HTTP 請求的逐步手動 companion 是 [`R5_Manual_Tests.http`](R5_Manual_Tests.http)；DB 與其他非 HTTP 操作仍以本文件為準。

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
$ListResponsePath = Join-Path $env:TEMP "custom-form-r5-list-$RunId.json"
$ManualHttpPath = 'docs/test-plan/R5_Manual_Tests.http'
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

function Get-FormPage {
  param(
    [string]$CookieJar,
    [int]$Page = 1,
    [int]$PageSize = 100,
    [string]$Status
  )

  $Uri = "$ApiBase/api/v1/forms?page=$Page&pageSize=$PageSize"
  if (-not [string]::IsNullOrWhiteSpace($Status)) { $Uri += "&status=$Status" }
  $HttpStatus = curl.exe -sS -o $ListResponsePath -w '%{http_code}' -b $CookieJar $Uri
  if ($HttpStatus -ne '200') { throw "Form list returned HTTP $HttpStatus for $Uri." }
  return (Get-Content -Raw $ListResponsePath | ConvertFrom-Json)
}

function Get-AllForms {
  param([string]$CookieJar, [string]$Status)

  $FirstPage = Get-FormPage -CookieJar $CookieJar -Status $Status
  $Items = @($FirstPage.items)
  for ($Page = 2; $Page -le $FirstPage.pagination.totalPages; $Page++) {
    $NextPage = Get-FormPage -CookieJar $CookieJar -Page $Page -Status $Status
    $Items += @($NextPage.items)
  }
  return [PSCustomObject]@{
    TotalItems = $FirstPage.pagination.totalItems
    TotalPages = $FirstPage.pagination.totalPages
    Items = @($Items)
  }
}

function Assert-FormOrder {
  param([object[]]$Items)

  $ActualIds = @($Items | ForEach-Object id)
  $ExpectedIds = @($Items |
    Sort-Object @{ Expression = { [DateTimeOffset]$_.updatedAt }; Descending = $true },
                @{ Expression = { $_.id }; Descending = $true } |
    ForEach-Object id)
  if (Compare-Object $ActualIds $ExpectedIds -SyncWindow 0) {
    throw 'Form List is not ordered by updatedAt DESC, id DESC.'
  }
}

# Baseline is read-only and lets this plan be rerun without assuming Member A has no prior Forms.
$MemberABaseline = Get-AllForms -CookieJar $MemberACookieJar
$MemberADraftBaseline = Get-AllForms -CookieJar $MemberACookieJar -Status 'draft'
$MemberABaselineTotalItems = $MemberABaseline.TotalItems
$MemberADraftBaselineTotalItems = $MemberADraftBaseline.TotalItems
$MemberABaselineIds = @($MemberABaseline.Items | ForEach-Object id)
[PSCustomObject]@{
  totalItems = $MemberABaselineTotalItems
  draftTotalItems = $MemberADraftBaselineTotalItems
  ids = $MemberABaselineIds
}
```

**預期結果：**Compose PostgreSQL healthy；`/health`、`/health/ready` 都為 `200`。兩位會員 Login 都為 `200`，並各自只在暫存 cookie jar 保存 `access_token`。Member A 的全部 Form 與 Draft Form baseline total／ID 均被記錄，但不改動既有資料。

**實際結果：**未執行（等待 Owner review）。

### 2. 巢狀 Draft 建立、預設值、排序與資料庫寫入

**目的：**確認 Owner 可在一次 `POST` 原子建立 Form、Question、Option；Draft 可以含不完整 Rating config，並正規化文字、套用 Form default、依 sortOrder 回傳巢狀集合。

**操作：**

```powershell
$NestedBody = @"
{
  "title": "  $NestedTitle  ",
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
"@

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
$InvalidNestedBody = @"
{
  "title": "$AtomicFailureTitle",
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
"@

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

**操作：**先建立一張較新的 Draft 作為分頁排序樣本，再依 baseline 驗證清單、分頁與排序：

```powershell
$DeleteBody = @"
{
  "title": "$DeleteTitle",
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
"@

Start-Sleep -Milliseconds 20
$DeleteCreateStatus = curl.exe -sS -o $DeleteResponsePath -w '%{http_code}' -b $MemberACookieJar -X POST "$ApiBase/api/v1/forms" `
  -H 'Content-Type: application/json' --data $DeleteBody
if ($DeleteCreateStatus -ne '201') { throw "Delete-sample create returned HTTP $DeleteCreateStatus." }
$DeleteForm = Get-Content -Raw $DeleteResponsePath | ConvertFrom-Json
$DeleteFormId = $DeleteForm.id

$MemberAAfterCreate = Get-AllForms -CookieJar $MemberACookieJar
$ExpectedTotalAfterCreate = $MemberABaselineTotalItems + 2
if ($MemberAAfterCreate.TotalItems -ne $ExpectedTotalAfterCreate) {
  throw "Expected $ExpectedTotalAfterCreate Member A Forms after creates; got $($MemberAAfterCreate.TotalItems)."
}
$MemberAAfterCreateIds = @($MemberAAfterCreate.Items | ForEach-Object id)
foreach ($FormId in @($NestedFormId, $DeleteFormId)) {
  if ($MemberAAfterCreateIds -notcontains $FormId) { throw "Current-run Form $FormId is absent from Member A List." }
}
Assert-FormOrder -Items $MemberAAfterCreate.Items

$MemberAFirstPage = Get-FormPage -CookieJar $MemberACookieJar -Page 1 -PageSize 1
if ($MemberAFirstPage.pagination.totalItems -ne $ExpectedTotalAfterCreate -or
    $MemberAFirstPage.pagination.totalPages -ne $ExpectedTotalAfterCreate -or
    $MemberAFirstPage.items[0].id -ne $MemberAAfterCreate.Items[0].id) {
  throw 'First-page pagination or sorted first item does not match the complete List.'
}
$PageAfterCreates = $ExpectedTotalAfterCreate + 1
$MemberABeyondPage = Get-FormPage -CookieJar $MemberACookieJar -Page $PageAfterCreates -PageSize 1
if ($MemberABeyondPage.items.Count -ne 0 -or $MemberABeyondPage.pagination.page -ne $PageAfterCreates) {
  throw 'A page beyond the last Form List page was not empty or did not retain its page number.'
}

$MemberADraftAfterCreate = Get-AllForms -CookieJar $MemberACookieJar -Status 'draft'
if ($MemberADraftAfterCreate.TotalItems -ne ($MemberADraftBaselineTotalItems + 2)) {
  throw 'Draft List total did not increase by two current-run Draft Forms.'
}
foreach ($FormId in @($NestedFormId, $DeleteFormId)) {
  if (@($MemberADraftAfterCreate.Items | ForEach-Object id) -notcontains $FormId) {
    throw "Current-run Draft $FormId is absent from the Draft List."
  }
}
Assert-FormOrder -Items $MemberADraftAfterCreate.Items

$MemberBAllForms = Get-AllForms -CookieJar $MemberBCookieJar
$MemberBIds = @($MemberBAllForms.Items | ForEach-Object id)
foreach ($FormId in @($NestedFormId, $DeleteFormId)) {
  if ($MemberBIds -contains $FormId) { throw "Member B List exposed Member A Form $FormId." }
}

curl.exe -i -b $MemberACookieJar "$ApiBase/api/v1/forms?page=0"
```

**預期結果：**兩次 Create 都回 `201`。Member A 的完整 List total 為 baseline `+2`，且包含 `$NestedFormId`、`$DeleteFormId`；所有 items 依 `updatedAt DESC, id DESC` 排列。第一頁 `page=1&pageSize=1` 的 item 是完整排序清單第一筆，`totalItems` 與 `totalPages` 都等於 baseline `+2`；`page=$PageAfterCreates` 回 `items=[]` 並保留指定 page。Draft List total 為 Draft baseline `+2`，也包含這兩個 ID。Member B 的完整 List 不得包含兩個本次 ID。`page=0` 回 `400 VALIDATION_ERROR`。既有 Form 不刪除、不要求固定 title、status 或數量。

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

$MemberAAfterDelete = Get-AllForms -CookieJar $MemberACookieJar
if ($MemberAAfterDelete.TotalItems -ne ($MemberABaselineTotalItems + 1)) {
  throw 'Member A List total did not decrease by one after current-run Soft Delete.'
}
$MemberAAfterDeleteIds = @($MemberAAfterDelete.Items | ForEach-Object id)
if ($MemberAAfterDeleteIds -notcontains $NestedFormId -or $MemberAAfterDeleteIds -contains $DeleteFormId) {
  throw 'Soft Delete List boundary did not retain the nested Form and hide the deleted Form.'
}
Assert-FormOrder -Items $MemberAAfterDelete.Items

$MemberADraftAfterDelete = Get-AllForms -CookieJar $MemberACookieJar -Status 'draft'
if ($MemberADraftAfterDelete.TotalItems -ne ($MemberADraftBaselineTotalItems + 1)) {
  throw 'Draft List total did not decrease by one after current-run Soft Delete.'
}
$MemberADraftAfterDeleteIds = @($MemberADraftAfterDelete.Items | ForEach-Object id)
if ($MemberADraftAfterDeleteIds -notcontains $NestedFormId -or $MemberADraftAfterDeleteIds -contains $DeleteFormId) {
  throw 'Soft Delete Draft List boundary is incorrect.'
}
Assert-FormOrder -Items $MemberADraftAfterDelete.Items

# 僅核對 Soft Delete 與定義 rows；不做任何 DELETE 或 rebuild。
docker compose exec postgres psql -X -q -U $DbUser -d $DbName -c "
SELECT deleted_at IS NOT NULL AS soft_deleted, status
FROM forms WHERE id = '$DeleteFormId'::uuid;
SELECT COUNT(*) AS retained_questions FROM questions WHERE form_id = '$DeleteFormId'::uuid;
SELECT COUNT(*) AS retained_options FROM options
WHERE question_id IN (SELECT id FROM questions WHERE form_id = '$DeleteFormId'::uuid);"
```

**預期結果：**第一次 Delete 回無 body 的 `204 No Content`。重複 Delete、Member A Detail、Member B Detail 都回 `404 FORM_NOT_FOUND`。Member A 的全部與 Draft List total 都從各自 baseline 變為 `+1`，保留 `$NestedFormId`、排除 `$DeleteFormId`，且維持正式排序；不要求其他既有 Form 的 title、status 或數量。DB 顯示 `$DeleteFormId` 的 `soft_deleted=true`、status 仍為 `draft`、`retained_questions=1`、`retained_options=1`。

**實際結果：**未執行（等待 Owner review）。

### 7. 清理暫存狀態並保留合成資料

**目的：**移除驗收暫存 token／response 檔案，確認 R4 會員與 R5 合成 Form 都保留供後續 Round 使用。

**操作：**

```powershell
Remove-Item -LiteralPath $MemberACookieJar, $MemberBCookieJar, $CreateResponsePath, $DeleteResponsePath, $ListResponsePath -ErrorAction SilentlyContinue

# REST Client 中曾暫填的 JWT 不得留在工作區或 commit。執行此段會把兩個 token
# 變數恢復為 placeholder，且不輸出 token 值。
$ManualHttp = Get-Content -Raw $ManualHttpPath
$ManualHttp = [regex]::Replace($ManualHttp, '(?m)^@memberAToken = .+$', '@memberAToken = PASTE_ACCESS_TOKEN_VALUE_ONLY')
$ManualHttp = [regex]::Replace($ManualHttp, '(?m)^@memberBToken = .+$', '@memberBToken = PASTE_ACCESS_TOKEN_VALUE_ONLY')
[System.IO.File]::WriteAllText($ManualHttpPath, $ManualHttp, [System.Text.UTF8Encoding]::new($false))
if ((Select-String -Path $ManualHttpPath -Pattern '^@member[AB]Token = PASTE_ACCESS_TOKEN_VALUE_ONLY$').Count -ne 2) {
  throw 'R5 Manual Tests token placeholders were not restored.'
}

docker compose exec postgres psql -X -q -U $DbUser -d $DbName -c "
SELECT email, status FROM users
WHERE email IN ('$MemberAEmail', '$MemberBEmail') ORDER BY email;
SELECT id, title, status, deleted_at IS NOT NULL AS soft_deleted
FROM forms WHERE id IN ('$NestedFormId'::uuid, '$DeleteFormId'::uuid) ORDER BY id;"
```

**預期結果：**兩位 R4 會員仍為 `active`。`$NestedFormId` 保持未刪除 Draft；`$DeleteFormId` 保留為 Soft Deleted Draft。暫存 cookie／response 檔案已移除；`R5_Manual_Tests.http` 的 `memberAToken`、`memberBToken` 都是 `PASTE_ACCESS_TOKEN_VALUE_ONLY` placeholder，未保存或提交有效 JWT。沒有停止 PostgreSQL、刪除 volume 或改動既有 Form 資料。

**實際結果：**未執行（等待 Owner review）。

## 本次刻意不擴充的邊界

本計畫聚焦 R5 主行為、Owner／Draft 隱藏、DB 寫入與代表性錯誤。所有 Question config 變體、Unicode code-point 極限、PATCH Delta、Publish／Close、Submission 與 Statistics 仍各自依正式規格在 R6～R10 驗收；這些項目未因本計畫精簡而放寬。

## 驗收結果與 Round 狀態

**AI 執行結果（核對日期：2026-10-03）：**本文件第 1～7 項的既有實際結果均為「未執行（等待 Owner review）」，repository 內沒有可對應本計畫案例的 HTTP、DB 或自動化執行輸出；因此全部維持 `未執行`，沒有補記為 PASS。`R5_Manual_Tests.http` 的兩個 token 變數均為 placeholder，未發現已保存的 JWT。`FormEndpointsTests.cs` 是 R5 開發期整合測試原始碼，但不是本計畫案例的已執行證據，不能替代上述 AI 結果。

**人工驗收結果：**PASS（2026-10-03；Owner 在本次指示中明確確認 R5 已人工驗收通過）。此確認僅記錄人工驗收，不回填或推論 AI 執行結果。

**Round 狀態：**accepted（2026-10-03；以 Owner 人工驗收確認為依據）。後續若需補齊 AI 驗收紀錄，應先取得執行授權，再依本成對計畫實跑並新增獨立、可追溯的結果；不可將此人工確認倒推為 AI PASS。
