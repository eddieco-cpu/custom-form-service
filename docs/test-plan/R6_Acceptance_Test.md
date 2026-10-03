# R6 驗收計畫：Draft Definition 與 Delta

計畫日期：2026-10-03；AI 執行／記錄日期：2026-10-03～04（Asia/Taipei）；Owner 確認日期：2026-10-04。階段：**accepted**。Owner 已核准 `034a0be8b4347f14c7609a8ec2460dfa2eccdd40` 的本文件與 [R6_Manual_Tests.http](R6_Manual_Tests.http)，授權 AI 執行，並在本 chat review 執行紀錄提交 `81b64f3c87b2e9efb4f1955cf0397acb99125114` 後明確確認 R6 accepted。人工全部未執行，Owner 確認依據是 AI 提交證據與 review，沒有人工 PASS。以下保留原請求／前置／預期與 AI 證據；本次僅文件收尾，未擴充或重跑測試。

## 依據、範圍及證據界線

- 正式契約：[01 Domain](../specs/01-v1-domain-specification.md) §3／FORM-01、04～06／QST；[02 Database](../specs/02-v1-database-specification.md) §7～9；[03a Common](../specs/03a-v1-api-contract-common.md) Authentication／驗證次序／§9；[03b Form](../specs/03b-v1-api-contract-auth-user-form.md) §3.3～3.5／§7.2／§9；[04 Errors](../specs/04-v1-error-catalog.md) §2／4／6～7；[05 Constraints](../specs/05-v1-system-constraints.md) §8～12／15。
- Round／階段依 [AGENTS.md](../../AGENTS.md)、[acceptance skill](../../.agents/skills/v1-acceptance-testing/SKILL.md) 與 [Implementation Plan](../implementation/V1_Backend_Implementation_Plan.md) §3／R6／§6。00 導覽與 03c 的後續功能不增加本次範圍。
- 實作基準：`af1a5a32fdf0ef5f7594d3866111e60989a7c760`；FormsController → Delta／Candidate／Validator → ChangeSet → Writer，既有 Form lock／交易與保存後 DB 回應重讀。執行時另記文件 commit／實際 HEAD，若業務語意改變須重審計畫。
- [最新收尾報告](../implementation/R6_Closeout_Development_Report.md)：精度 12/12、Form 73/73 是歷史 Linux 開發證據；其後本次 AI 驗收已確認 Windows 原生新版 API 並完成全套 118/118 PASS（見 01／02 與執行紀錄）。[Candidate 報告](../implementation/R6_Candidate_Refactor_Development_Report.md) 的全套 106/106 也是歷史證據。[Option 報告](../implementation/R6_Option_Persistence_Review_Report.md) 說明 replacement、混合操作與故障回滾。
- 沿用 [R4](R4_Acceptance_Test.md)／[R5](R5_Acceptance_Test.md) 的 HTTP Development 拓撲、兩位合成會員及保留資料。R5 人工 accepted 不補造其 AI 結果，也不直接作為 R6 PASS。

主流程覆蓋 Question／Option 新增、更新、id-only 刪除、未提及保留、空集合 no-op、最終排序交換、value replacement／刪除後重用、五題型及 Config replacement／Type reset／Options 連動、代表性錯誤 path、權限／生命週期優先序、updatedAt、deadline 保存精度、原子性及 PATCH 並行。細部矩陣由一次完整回歸承接，不把大量開發案例逐一重抄為手動請求。

## 執行方式與共用測資

AI 可執行下列 HTTP、唯讀 DB、限定的新 fixture 狀態設定、既有測試與清理；人工項目是 Windows 原生終端確認、REST Client／DBeaver 獨立檢視及 Owner 手動結果。AI 不代填人工 PASS。每一主項與 HTTP 子項都有分開的 AI／人工結果；本次 AI 結果見逐項欄位，人工全部維持未執行。執行後填 PASS／FAIL／BLOCKED／未執行及日期、SHA、工具、status/code/path、DB 差異或 TRX；不要記錄 token／完整 Cookie／密碼。前置失敗則依賴項 BLOCKED，不能當作已通過。

以 `.http` 為唯一逐筆 request body 範例；Markdown 管非 HTTP 操作與斷言。AI 可用等價 curl／HttpClient，必須逐筆映射 R6-XXx。一般請求依序；12a／12b 僅供並行 harness 同時發送，不逐筆順序 Send。人工再次執行完整流程要換新 RunId，不能將 AI 已編輯過的 fixture 當作初始資料。只獨立讀取檢視時可用同 run，註明觀察來源。

`runId=r6-acceptance-20261003-01`；重跑在兩份文件同步改成唯一 suffix，保留舊 fixtures／結果。每 run 建立三張 Form：`R6 editor <runId>`、`R6 scope <runId>`、`R6 concurrent <runId>`。若 title 已存在，不覆寫、不刪除，先換 runId。R4 身分固定：
`r4-member-a-r4-acceptance-20261001-01@example.test`、`r4-member-b-r4-acceptance-20261001-01@example.test`。密碼從本機保留測試設定取得／由 Owner 私下提供；兩份新文件只留 placeholder，不複製舊密碼或 JWT。

PowerShell 共用變數（repository root）：

```powershell
$RunId = 'r6-acceptance-20261003-01'
$ApiBase = 'http://localhost:5000'
$RepoRoot = (Get-Location).Path
$DbName = '<POSTGRES_DB from existing .env>'
$DbUser = '<POSTGRES_USER from existing .env>'
$DbPort = '<POSTGRES_PORT from existing .env, normally 5432>'
$MemberAEmail = 'r4-member-a-r4-acceptance-20261001-01@example.test'
$MemberBEmail = 'r4-member-b-r4-acceptance-20261001-01@example.test'
$MemberACookieJar = Join-Path $env:TEMP "custom-form-r6-a-$RunId.cookies"
$MemberBCookieJar = Join-Path $env:TEMP "custom-form-r6-b-$RunId.cookies"
$TempPaths = @()  # 登錄本次所有暫存檔；13 只清理這些明確路徑
$EvidenceDir = Join-Path $RepoRoot "artifacts/r6-acceptance-evidence/$RunId"
$ManualHttpPath = Join-Path $RepoRoot 'docs/test-plan/R6_Manual_Tests.http'
# UUID 變數在 03 的 201 後填入，不能沿用前次 run。
```

REST Client：先 Clear All Cookies，每個請求都有 `@no-cookie-jar`；只用明確 A／B Cookie header，無登入項不得含 Cookie。登入後把 `access_token=` 至第一個分號之間的值暫填對應 token，不複製屬性。若用 curl，01d／01e 分別加 `-c $MemberACookieJar`／`-c $MemberBCookieJar` 並不列印 response headers，之後用 `-b`。密碼只於執行時輸入；body 與回應暫存檔限定本次 TEMP 名稱，登入 headers 不存到證據。12 使用 A 的 cookie jar；若先用 REST Client，另以相同 01d 登入取得 jar，記錄為同 case 工具轉換。

ID 以 title／value 對應，不能把回應 Array index 當固定 ID：

| 來源 | 變數 |
| --- | --- |
| 03a Form | editorFormId／PowerShell $EditorFormId |
| Text／Number／Choice／Multiple／Incomplete rating／Remove question | qText／qNumber／qChoice／qMultiple／qRating／qRemove |
| Choice alpha／beta／gamma／Delta | oAlpha／oBeta／oDelete／oKeep |
| Multiple other；Remove question cascade | oOther；oCascade（DB 證據） |
| 03b Form／Foreign question／Foreign option | scopeFormId／$ScopeFormId；qForeign；oForeign |
| 03c Form／Concurrent first／Concurrent second | concurrentFormId／$ConcurrentFormId；qConcurrentA／$QConcurrentA；qConcurrentB／$QConcurrentB |
| 05a 新增 Added choice／New child／Reused gamma | qNew／oNew／oReused（DB 證據） |
| 07c Branch | oBranch（DB 證據） |

Question／Option 的 timestamps 不在 API Resource Shape；所有子列 updatedAt／createdAt 斷言都由 DB 驗證。FormDetail 不含 email、deletedAt、Responses、Statistics，完整集合依 sortOrder ASC，PATCH／GET 須一致；JSONB key 順序／number 表示可不同，但數值須精確等值。

## 環境與回歸命令（R6-01／02；核准後才執行）

沿用現有 `.env` 與 Development User Secrets；不得覆寫／列印密碼、signing key，或照 README 的新環境範例重建設定／migration。先記錄 git status、HEAD、SDK、既有 API 程序與 port5000。若需停止舊 API，只停止可辨識的本專案程序，人工可在其終端 Ctrl+C；不殺其他 dotnet 程序。

```powershell
git status --short
git rev-parse HEAD
dotnet --version
New-Item -ItemType Directory -Path $EvidenceDir -Force | Out-Null
docker compose config --quiet
docker compose up -d
docker compose ps
docker compose exec -T postgres psql -X -U $DbUser -d $DbName -c 'SELECT current_database(), current_user;'

dotnet restore CustomFormService.sln --disable-build-servers -m:1
dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1 -p:UseSharedCompilation=false
# 每個命令確認 exit code=0；build 必須使用目前 source，不能偷用歷史 DLL。
# 獨立 Windows PowerShell 視窗持續執行，記錄 Development／localhost:5000 與啟動成功：
dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --launch-profile http --no-build
# 在操作視窗執行 HTTP 01a～01f；確認回應來自上述新版 Windows 原生程序。
```

唯讀查詢 R4／R5 保留資料及缺失 UUID：

```powershell
docker compose exec -T postgres psql -X -U $DbUser -d $DbName -c "
SELECT id, email, display_name, status FROM users
WHERE email IN ('$MemberAEmail', '$MemberBEmail') ORDER BY email;
SELECT f.id, f.title, f.status, f.deleted_at, f.created_at, f.updated_at
FROM forms f JOIN users u ON u.id=f.user_id
WHERE u.email IN ('$MemberAEmail', '$MemberBEmail') ORDER BY f.id;
SELECT COUNT(*) AS missing_id_count FROM forms
WHERE id='00000000-0000-0000-0000-000000000006'::uuid;"
```

兩位 User 須 active；從查詢中選出 Owner A 的 R5 nested 未刪除 Draft 與 R5 soft delete fixture，記錄實際 ID，填 `retainedDeletedFormId`。若找不到，不建立假歷史資料，相關項 BLOCKED 交 Owner 處理。missing_id_count 須 0，否則在兩份文件同步改成確定不存在的新 UUID（不刪除該列）。在 R6 建立前保存所有既有 Form IDs（含 Soft Delete）與完整 snapshot，加上兩位會員上述公開欄位基準；02／13 前後比對，確認未變更。

02 在同一次目前 build 上跑完整 suite：

```powershell
dotnet test CustomFormService.sln --no-build --no-restore --configuration Debug -m:1 --logger "console;verbosity=normal" --logger "trx;LogFileName=windows-full.trx" --results-directory $EvidenceDir
# 記錄本命令 exit code，核對 TRX Counters 與每個 TestResult，尤其：
# Patch_deadline_uses_actual_storage_precision_for_no_op（12 個資料案例）
# Concurrent_patches_are_serialized_before_candidate_validation
# Patch_rolls_back_first_save_when_second_save_has_database_failure
# Patch_does_not_return_success_and_rolls_back_when_commit_fails
```

Windows 原生 **API 啟動**與 **testhost 探索／執行**是分開的驗證。若仍出現 0x800711C7／Smart App Control／0 tests，記錄該子項 BLOCKED，保留錯誤與非秘密 Code Integrity 證據，不停用安全設定、不把 Linux 結果稱作 Windows PASS。原生 API 啟動失敗則 01 及依賴的 HTTP 驗收 BLOCKED。

以下為保留的條件式備案，本次 Windows testhost 已成功，未啟用此備案。若另次核准執行時 Windows testhost 受封鎖，可沿用已獲授權的官方 Linux SDK 方式，完成「本次全套」子項；Windows 原生缺口仍單獨待 Owner 判定。使用新暫存容器、同一目前 source、既有 DB；不以歷史容器 DLL／TRX 替代。以下 setup 不列印 secrets、不掛載 Docker socket、不刪任何開發資源：

```powershell
$SdkContainer = "r6-acceptance-sdk-$RunId"
$PostgresContainerId = docker compose ps -q postgres
$PostgresInfo = (docker inspect $PostgresContainerId | ConvertFrom-Json)[0]
$Network = @($PostgresInfo.NetworkSettings.Networks.PSObject.Properties.Name)[0]
$SecretId = '07f62f1e-ff60-47a2-bf24-866b69909532'
$SecretDir = Join-Path $env:APPDATA "Microsoft/UserSecrets/$SecretId"
$NugetDir = Join-Path $env:USERPROFILE '.nuget/packages'
# 確認這三個來源目錄與 Docker network 存在；同名容器已存在時停止，勿 rm 他人容器。
docker run --detach --name $SdkContainer --network $Network --mount "type=bind,source=$RepoRoot,target=/source,readonly" --mount "type=bind,source=$SecretDir,target=/root/.microsoft/usersecrets/$SecretId,readonly" --mount "type=bind,source=$NugetDir,target=/seed-packages,readonly" -e PostgreSql__Host=postgres -e PostgreSql__Port=5432 mcr.microsoft.com/dotnet/sdk:10.0.400 sleep infinity
# 若官方映像／restore 無法取得則記 BLOCKED，不調降版本或安全原則。
$PrepareScript = @'
set -e
mkdir -p /work/r6 /root/.nuget
cp /source/CustomFormService.sln /source/global.json /work/r6/
cp -a /source/src /source/tests /work/r6/
find /work/r6 -type d \( -name bin -o -name obj \) -prune -exec rm -rf '{}' +
cp -a /seed-packages /root/.nuget/packages
cd /work/r6
dotnet --version
dotnet restore CustomFormService.sln
dotnet build CustomFormService.sln --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test CustomFormService.sln --no-build --no-restore -m:1 --logger "console;verbosity=normal" --logger "trx;LogFileName=linux-full.trx" --results-directory /work/evidence
'@
docker exec $SdkContainer bash -lc $PrepareScript
# 記錄 exit code 與實際 Counters；/work/r6 是已核實的容器內暫存副本。
docker cp "${SdkContainer}:/work/evidence/." $EvidenceDir
```

只清掉容器內新副本的 bin/obj，來源 workspace 唯讀，DB named volume 不掛載到 SDK 容器。suite 只清自身隨機合成會員／Form；DatabaseSchemaTests 查 schema，沒有 rebuild／TRUNCATE。既有 User Secrets 不改動。本項自動回歸不能替代 03～12 的 HTTP／DB 案例或人工結果。

## DB snapshot 與比較方式（各寫入／錯誤／no-op 共用）

核准後在 PowerShell 定義。UUID 驗證後才組入 SQL；psql 失敗不得視為空 snapshot。完整列含 timestamps，JSONB canonical text 比較不經浮點解析。

```powershell
function Get-R6Snapshot {
  param([string]$FormId)
  $SafeId = ([guid]$FormId).ToString()
  $Sql = @"
SELECT jsonb_build_object(
 'form', (SELECT to_jsonb(f) FROM forms f WHERE f.id='$SafeId'::uuid),
 'questions', COALESCE((SELECT jsonb_agg(to_jsonb(q) ORDER BY q.id)
   FROM questions q WHERE q.form_id='$SafeId'::uuid), '[]'::jsonb),
 'options', COALESCE((SELECT jsonb_agg(to_jsonb(o) ORDER BY o.id)
   FROM options o JOIN questions q ON q.id=o.question_id
   WHERE q.form_id='$SafeId'::uuid), '[]'::jsonb)
)::text;
"@
  $Rows = docker compose exec -T postgres psql -X -q -A -t -v ON_ERROR_STOP=1 -U $DbUser -d $DbName -c $Sql
  if ($LASTEXITCODE -ne 0) { throw 'Snapshot query failed.' }
  return ($Rows -join "`n").Trim()
}
# 無變更項（08 每筆、09 各 no-op、10 每筆、11a）：
$Before = Get-R6Snapshot $EditorFormId
# 執行對應且僅該筆 HTTP，記錄 status/code/path
$After = Get-R6Snapshot $EditorFormId
if ($Before -cne $After) { throw 'Unexpected aggregate change.' }
```

實際修改項保存 before/after 至非秘密證據，逐列核對變動欄位／未提及列。03／05 檢查 rows／ID／cascade；07b 檢查 Options 實體刪除；06b 可用
`SELECT (config->>'min')::numeric = '1e30'::numeric, (config->>'max')::numeric = '1e31'::numeric FROM questions WHERE id='<qNumber>'::uuid;`
（兩個 true）做精確 oracle。09f～09i 以 DB 的 `submission_deadline_at` 與 Form `updated_at` 比較，不用截字串推測 no-op。任何 PATCH／GET 相等比較保持精確 JSON 數值語意，子 timestamps 只用 DB。

各成功案例中：只有實際變動的 Form／Question／Option 更新 updatedAt；新增列 createdAt=updatedAt；更新／同 ID replacement 的 createdAt 不變；集合改動使其 Form／受影響 Question updatedAt 更新，未變 Option 不動。以 DB 微秒精度比較，不要求執行前採樣時間與 Server timestamp 完全相等。

## 依序案例

下列表格的「請求」指同編號 `.http` 的完整內容；每組都需完成 prose 指定的 DB／非 HTTP 操作。本次已按實際 HTTP／DB 證據填入 AI 結果，人工結果未推論。

### R6-01 — Windows 原生啟動與保留資料

AI 可執行服務／HTTP／唯讀 DB 檢查；人工在 Windows 終端確認原生 API 程序與啟動訊息。先完成下方啟動操作，再依序執行 01a～01f。01d／01e 使用既有 R4 合成會員，不重新註冊、不改 Profile 或 status。完整分頁讀取 A 的 Forms，記錄 R5 nested／soft-delete fixture 的 ID；不要假設 DB 空白。

| 編號／請求 | 前置與預期結果 | AI 實際結果 | 人工實際結果 |
| --- | --- | --- | --- |
| R6-01a · `GET /health` | 200；body 為 {"status":"healthy"}。 | PASS：200；工程回應／OpenAPI PATCH 已核對 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-01b · `GET /health/ready` | 200；Healthy，確認原生 API 可連既有 DB。 | PASS：200；工程回應／OpenAPI PATCH 已核對 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-01c · `GET /openapi/v1.json` | 200；Development 文件包含 PATCH /api/v1/forms/{formId}。 | PASS：200；工程回應／OpenAPI PATCH 已核對 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-01d · `POST /api/v1/auth/login` | 200 User + access_token Cookie；不在 body 回 JWT。只在本機暫填 memberAToken。 | PASS：200 User＋Cookie；無 body JWT；01d helper 診斷見執行紀錄 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-01e · `POST /api/v1/auth/login` | 200 User + Cookie；只在本機暫填 memberBToken。 | PASS：200 User＋Cookie；無 body JWT；01d helper 診斷見執行紀錄 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-01f · `GET /api/v1/forms?page={{listPage}}&pageSize=100` | 200；A 的保留清單。逐頁記錄所有 ID／totalItems；之後恢復 listPage=1。 | PASS：200；A totalItems=1，完整 IDs 與既有 DB 一致 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |

### R6-02 — 本次完整回歸（非 HTTP）

AI 可執行／人工可獨立重跑。依 §環境與回歸命令跑目前 HEAD 的整個 solution，無 filter，記錄平台、SHA、exit code、實際 total／passed／failed／skipped 與 TRX。現有程式預計 118 案（前次 106 + 精度 12）；以實際探索數量核對，不以 exit 0 或 0 tests 視為 PASS。Windows DLL 封鎖和 Linux 結果分列。完整 suite 應包含精度、精確 Number、Config、SQL 欄位、三組混合操作、並行 PATCH、Save #2 與 commit 故障回滾；兩個故障測試回受控 500 並逐列驗證全部回滾。故障僅由現有 TestFactory／interceptor 對測試自建資料注入，禁止對常駐 API 或保留 fixture 注入故障。歷史 73/73、106/106 均不填入本次結果。

**AI 實際結果：**PASS（2026-10-04）：Windows 原生 118/118，0 failed／0 notExecuted，exit 0；12 個精度案例與兩個保存故障回滾均 Passed。Linux：未執行（Windows 已通過，fallback 條件未成立）。

**人工實際結果：**未執行（人工；Owner 依 AI 提交證據與 review 確認驗收）。

### R6-03 — 建立本次 Draft fixtures 與 ID 基準

AI／人工皆可。03a 建立完整五題型及一題待刪除 Choice，03b 建立跨 Form／生命週期 fixture，03c 建立並行 fixture。201 後依共用測資的 ID 對照記錄 Server ID，執行 DB snapshot。三張 Form 都由 A 擁有。R4／R5 資料只讀。

| 編號／請求 | 前置與預期結果 | AI 實際結果 | 人工實際結果 |
| --- | --- | --- | --- |
| R6-03a · `POST /api/v1/forms` | 201 Draft；6 Questions／6 Options；placeholder trim；Rating 缺 max 仍合法。按 sortOrder 排序，defaults、ID 與 createdAt 由 Server 決定。 | PASS：201，6Q／6O；Server ID／defaults／排序／完整 DB 核對 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-03b · `POST /api/v1/forms` | 201；記錄 scopeFormId／qForeign／oForeign；後續狀態只限此 fixture。 | PASS：201，1Q／1O；Server ID／defaults／排序／完整 DB 核對 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-03c · `POST /api/v1/forms` | 201；記錄 concurrentFormId／qConcurrentA／qConcurrentB。 | PASS：201，2Q／0O；Server ID／defaults／排序／完整 DB 核對 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |

### R6-04 — Form 欄位、nullable replacement 與未提及保留

AI／人工皆可。先保存 editor 的完整 DB snapshot，再送 04a，之後 GET／DB 比較。Form 發生實際修改，但所有子列保持 ID、createdAt、updatedAt 與內容；resultVisibility 仍 owner_only。

| 編號／請求 | 前置與預期結果 | AI 實際結果 | 人工實際結果 |
| --- | --- | --- | --- |
| R6-04a · `PATCH /api/v1/forms/{{editorFormId}}` | 200；title trim 為 R6 edited {{runId}}；description=null；模式更新；deadline 仍 null，Questions／Options 未變；只有 Form updatedAt 改變。 | PASS：200；Form 欄位更新、子列不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-04b · `GET /api/v1/forms/{{editorFormId}}` | 200；完整 FormDetail 與 04a 一致（JSON Object key 順序無關）。 | PASS：200；GET＝前次成功 PATCH；DB 無副作用 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |

### R6-05 — Question／Option 混合 Delta、排序交換與 value 重用

AI／人工皆可。05a 前保存 snapshot。一次完成 Question 更新／id-only 刪除／新增，Option 更新／value 與 sortOrder 交換／id-only 刪除後重用 value／新增。未提及 qNumber、qMultiple、qRating 與 oKeep 必須不變。新 ID 不可沿用已刪除列；現有 Option value replacement 保留原 ID／createdAt。

| 編號／請求 | 前置與預期結果 | AI 實際結果 | 人工實際結果 |
| --- | --- | --- | --- |
| R6-05a · `PATCH /api/v1/forms/{{editorFormId}}` | 200；仍 6 Questions／6 Options。qRemove 與 cascade 子 Option 實體刪除；qText description=null／required=true；oAlpha=beta/order1、oBeta=alpha/order0，ID／createdAt 保留；新 gamma trim，ID 與 oDelete 不同。新增 qNew／oNew（記錄 ID）；oKeep 的大小寫 Delta 保留。最終排序唯一，不因中途 key／排序衝突回 500。 | PASS：200；6Q／6O；交換／重用／Cascade／ID／createdAt／未提及列核對 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-05b · `GET /api/v1/forms/{{editorFormId}}` | 200；與已保存的 05a FormDetail 一致；DB 無刪除列、無多餘列或 orphan。 | PASS：200；GET＝前次成功 PATCH；DB 無副作用 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |

### R6-06 — Config 完整取代及精確 Number

AI／人工皆可。06a 是 replacement，不 deep merge。06b 直接傳 raw JSON number；比對 Number 時不要用 PowerShell ConvertFrom-Json 的浮點結果當 oracle，使用 JSON 原文字與 DB JSONB numeric 等值查詢。全部細部型別／範圍矩陣由 02 回歸補足。

| 編號／請求 | 前置與預期結果 | AI 實際結果 | 人工實際結果 |
| --- | --- | --- | --- |
| R6-06a · `PATCH /api/v1/forms/{{editorFormId}}` | 200；Text config 只剩 placeholder=Replacement/minLength=1/maxLength=3，舊 textarea 消失；Rating 變完整有效設定，required 不影響 Draft 可保存性。 | PASS：200；完整 Config replacement／trim | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-06b · `PATCH /api/v1/forms/{{editorFormId}}` | 200；Config 只剩 min/max，舊 placeholder 消失；DB min = 1e30::numeric、max = 1e31::numeric，無 decimal/double 捨入。JSONB 可改數字表示，數值必須精確相同。 | PASS：200；SQL numeric 1e30／1e31 精確相等 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |

### R6-07 — Type 切換及 Options／Config 連動

AI／人工皆可。逐步執行，不共用舊 Options ID 作新增。每步比對完整 PATCH 回應、DB 與前一步 snapshot，確認 q ID／createdAt 保留且未提及資料保持原樣；後續 09m 再對照 GET，無額外未編號 HTTP 步驟。

| 編號／請求 | 前置與預期結果 | AI 實際結果 | 人工實際結果 |
| --- | --- | --- | --- |
| R6-07a · `PATCH /api/v1/forms/{{editorFormId}}` | 200；Choice 互轉保留四個既有 Options 的 ID／createdAt／updatedAt，套用目標 config。 | PASS：200；Choice 互轉保留四個 Options 完整列 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-07b · `PATCH /api/v1/forms/{{editorFormId}}` | 200；未提供 config，重設 {}；四個 Options 自動實體刪除，Question 保留。 | PASS：200；Config={}／四個 Options 實體清除 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-07c · `PATCH /api/v1/forms/{{editorFormId}}` | 200；Text→Choice 重設 config={} 並新建 oBranch（記錄）；Number→Rating 使用指定 config；Rating→Text 未指定 config 為 {} 且無 Options。qMultiple、qNew 保留。 | PASS：200；三個 Type 連動、目標 Config、新 Option | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |

### R6-08 — 代表性錯誤、ID 歸屬與 Request path

AI／人工皆可。每筆前後都做完整 snapshot 相等檢查，不只查總筆數。所有 Definition 錯誤為 HTTP 400 FORM_DEFINITION_INVALID，Not Found detail 不升成 HTTP 404；未知／唯讀頂層欄位為 VALIDATION_ERROR。Details 可一筆或多筆、順序與 message 非固定契約，核對下表指定的 code/path 存在。錯誤不揭露外部資源 Owner／Form ID。

| 編號／請求 | 前置與預期結果 | AI 實際結果 | 人工實際結果 |
| --- | --- | --- | --- |
| R6-08a · `PATCH /api/v1/forms/{{editorFormId}}` | 400 VALIDATION_ERROR；path=resultVisibility，資料完全不變。 | PASS：400 VALIDATION_ERROR；INVALID_VALUE＠resultVisibility；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-08b · `PATCH /api/v1/forms/{{editorFormId}}` | 400 FORM_DEFINITION_INVALID；INVALID_VALUE at questions[1].id（第二次出現）；更新與刪除不部分成功。 | PASS：400 FORM_DEFINITION_INVALID；INVALID_VALUE＠questions[1].id；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-08c · `PATCH /api/v1/forms/{{editorFormId}}` | 400 FORM_DEFINITION_INVALID；INVALID_VALUE at questions[0].options[1].id。 | PASS：400 FORM_DEFINITION_INVALID；INVALID_VALUE＠questions[0].options[1].id；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-08d · `PATCH /api/v1/forms/{{editorFormId}}` | 400 FORM_DEFINITION_INVALID；QUESTION_NOT_FOUND at questions[0].id；不 upsert／搬移。 | PASS：400 FORM_DEFINITION_INVALID；QUESTION_NOT_FOUND＠questions[0].id；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-08e · `PATCH /api/v1/forms/{{editorFormId}}` | 400 FORM_DEFINITION_INVALID；OPTION_NOT_FOUND at questions[0].options[0].id（同 Form 的另一 Question）。 | PASS：400 FORM_DEFINITION_INVALID；OPTION_NOT_FOUND＠questions[0].options[0].id；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-08f · `PATCH /api/v1/forms/{{editorFormId}}` | 400 FORM_DEFINITION_INVALID；DUPLICATE_SORT_ORDER at questions[1].sortOrder，定位真正改排序欄位，非僅改 title 的項目。 | PASS：400 FORM_DEFINITION_INVALID；DUPLICATE_SORT_ORDER＠questions[1].sortOrder；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-08g · `PATCH /api/v1/forms/{{editorFormId}}` | 400 FORM_DEFINITION_INVALID；INVALID_OPTION at questions[0].options[0].value；trim 後重複 Branch，Form title 亦不得改。 | PASS：400 FORM_DEFINITION_INVALID；INVALID_OPTION＠questions[0].options[0].value；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-08h · `PATCH /api/v1/forms/{{editorFormId}}` | 400 FORM_DEFINITION_INVALID；QUESTION_TYPE_CONFLICT at questions[0].options；不可一邊自動清除一邊帶非空 Delta。 | PASS：400 FORM_DEFINITION_INVALID；QUESTION_TYPE_CONFLICT＠questions[0].options；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-08i · `PATCH /api/v1/forms/{{editorFormId}}` | 400 FORM_DEFINITION_INVALID；INVALID_QUESTION_CONFIG at questions[0].config.variant（非合法 multiple_choice variant）。 | PASS：400 FORM_DEFINITION_INVALID；INVALID_QUESTION_CONFIG＠questions[0].config.variant；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-08j · `PATCH /api/v1/forms/{{editorFormId}}` | 400 FORM_DEFINITION_INVALID；INVALID_QUESTION_CONFIG at questions[0].config；精確 min > max，不可經浮點捨入為相等。 | PASS：400 FORM_DEFINITION_INVALID；INVALID_QUESTION_CONFIG＠questions[0].config；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-08k · `PATCH /api/v1/forms/{{editorFormId}}` | 400 VALIDATION_ERROR；INVALID_VALUE at title；非 nullable 欄位不能用 null 清除。 | PASS：400 VALIDATION_ERROR；INVALID_VALUE＠title；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |

### R6-09 — no-op／updatedAt 與截止時間保存精度

AI／人工皆可。09a 先建立實際 Number config 基準。09b～09e 各自與該基準的完整 snapshot 比較；等值文字、Config key 順序／1 與 1.0、空 Question／Option Delta 都不得更新任何列或 updatedAt。09f～09l 再按 deadline 分段建立基準；需比較保存值與 timestamps，不能只比較請求字串。保存精度是現用 Npgsql／PostgreSQL 的可觀察行為，沒有新增日期格式限制；2000 年前、8 位小數、Min/MaxValue／infinity 與 SQL 無 UPDATE 證據由 02 的 12 案確認。

| 編號／請求 | 前置與預期結果 | AI 實際結果 | 人工實際結果 |
| --- | --- | --- | --- |
| R6-09a · `PATCH /api/v1/forms/{{editorFormId}}` | 200；實際 config 變更，Form/qChoice updatedAt 改變；其他列不變。保存此基準。 | PASS：200；實際 Config 更新；未變列保留 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-09b · `PATCH /api/v1/forms/{{editorFormId}}` | 200 no-op；完整 DB snapshot／timestamps 不變。 | PASS：200 no-op；完整 DB 快照與所有 timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-09c · `PATCH /api/v1/forms/{{editorFormId}}` | 200 no-op；不清空 Questions。 | PASS：200 no-op；完整 DB 快照與所有 timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-09d · `PATCH /api/v1/forms/{{editorFormId}}` | 200 no-op；不清空 Options，任何 updatedAt 不變。 | PASS：200 no-op；完整 DB 快照與所有 timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-09e · `PATCH /api/v1/forms/{{editorFormId}}` | 200 no-op；文字 trim、Config 屬性順序和 1.0／1 等值；無任何列或 timestamps 變更。 | PASS：200 no-op；完整 DB 快照與所有 timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-09f · `PATCH /api/v1/forms/{{editorFormId}}` | 200；保存／回應 deadline=.123456Z。只有 Form updatedAt 改變，子列不變。保存 deadline 基準。 | PASS：200；保存 .123456Z；只有 Form deadline／updatedAt 改變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-09g · `PATCH /api/v1/forms/{{editorFormId}}` | 200 no-op；重送原始七位字串，與 09f 保存基準相等，updatedAt 不變。 | PASS：200 no-op；完整 DB 快照與所有 timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-09h · `PATCH /api/v1/forms/{{editorFormId}}` | 200 no-op；只差次微秒，實際保存相同，任何 timestamps 不變。 | PASS：200 no-op；完整 DB 快照與所有 timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-09i · `PATCH /api/v1/forms/{{editorFormId}}` | 200 no-op；重送 GET canonical 值亦不更新。 | PASS：200 no-op；完整 DB 快照與所有 timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-09j · `PATCH /api/v1/forms/{{editorFormId}}` | 200；真正差一微秒，deadline 與 Form updatedAt 改變；子列不變。 | PASS：200；保存 .123457Z；真正一微秒變更，子列不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-09k · `PATCH /api/v1/forms/{{editorFormId}}` | 200；deadline 清除，只有 Form updatedAt 改變。保存 null 基準。 | PASS：200；deadline=null；只有 Form deadline／updatedAt 改變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-09l · `PATCH /api/v1/forms/{{editorFormId}}` | 200 no-op；與 09k 全部保存資料／timestamps 一致。 | PASS：200 no-op；完整 DB 快照與所有 timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-09m · `GET /api/v1/forms/{{editorFormId}}` | 200；與 09l 完整 FormDetail 一致；deadline=null，status=draft。 | PASS：200；GET＝前次成功 PATCH；DB 無副作用 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |

### R6-10 — Authentication、權限、存在性與生命週期優先序

AI 可執行 HTTP／DB fixture 設定；人工可逐筆檢查錯誤優先序。只依本節明確步驟變更本次 scopeFormId.status；不呼叫 Publish／Close／Visibility API。editor、R5 fixture 不改狀態。每筆失敗後保存資料與 timestamps 不變；10g／10i 前的 test-only DB 操作見下方。

| 編號／請求 | 前置與預期結果 | AI 實際結果 | 人工實際結果 |
| --- | --- | --- | --- |
| R6-10a · `PATCH /api/v1/forms/not-a-uuid` | 401 AUTH_REQUIRED；沒有 Cookie，即使 path／欄位無效仍先判 Authentication。 | PASS：401 AUTH_REQUIRED；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-10b · `PATCH /api/v1/forms/not-a-uuid` | 400 VALIDATION_ERROR；有效 A 才檢查 UUID。 | PASS：400 VALIDATION_ERROR；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-10c · `PATCH /api/v1/forms/{{missingFormId}}` | 404 FORM_NOT_FOUND；不存在優先於欄位驗證。先用 DB 證明 missingFormId 未使用。 | PASS：404 FORM_NOT_FOUND；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-10d · `PATCH /api/v1/forms/{{editorFormId}}` | 403 FORM_ACCESS_DENIED；非 Owner 優先於 Definition validation。 | PASS：403 FORM_ACCESS_DENIED；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-10e · `GET /api/v1/forms/{{editorFormId}}` | 404 FORM_NOT_FOUND；他人 Draft Detail 隱藏存在性。 | PASS：404 FORM_NOT_FOUND；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-10f · `PATCH /api/v1/forms/{{retainedDeletedFormId}}` | 404 FORM_NOT_FOUND；使用 DB 確認的 R5 Soft Delete ID，歷史資料仍原樣。 | PASS：404 FORM_NOT_FOUND；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-10g · `PATCH /api/v1/forms/{{scopeFormId}}` | 409 FORM_NOT_EDITABLE；前置：僅將本次 scope fixture 設為 published。Lifecycle 優先於欄位驗證。 | PASS：409 FORM_NOT_EDITABLE；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-10h · `PATCH /api/v1/forms/{{scopeFormId}}` | 403 FORM_ACCESS_DENIED；Published 非 Owner，Authorization 優先 Lifecycle；空 PATCH 也不跳過。 | PASS：403 FORM_ACCESS_DENIED；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-10i · `PATCH /api/v1/forms/{{scopeFormId}}` | 409 FORM_NOT_EDITABLE；前置：僅將 scope fixture 設為 closed。空 PATCH 仍需 Draft。 | PASS：409 FORM_NOT_EDITABLE；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-10j · `PATCH /api/v1/forms/{{scopeFormId}}` | 403 FORM_ACCESS_DENIED；Closed 非 Owner。完成後立即恢復 scope fixture 的 draft 原狀。 | PASS：403 FORM_ACCESS_DENIED；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |

### R6-10 的 DB fixture 狀態操作

此處只是 R6 Lifecycle guard 的測試前置，不代表 R7 狀態轉移驗收。03b 建立後保存 $ScopeBaseline；10f 結束後、10g 前設定 published，10h 後、10i 前設定 closed；10j 完成立即還原 draft。每段 UPDATE 只改 status，RETURNING 必須恰一列且符合本次 title／Owner。SQL 失敗或 fixture 身分不符就停止相關項；不得把 R5／editor 或其他 Form 帶入。

```powershell
if ($RunId -notmatch '^r6-acceptance-[0-9]{8}-[0-9]+$') { throw 'Invalid runId.' }
$SafeScopeId = ([guid]$ScopeFormId).ToString()
$ScopeBaseline = Get-R6Snapshot $SafeScopeId  # 須在變成 published 前保存
# 10g 前：
docker compose exec -T postgres psql -X -v ON_ERROR_STOP=1 -U $DbUser -d $DbName -c "
UPDATE forms SET status='published'
WHERE id='$SafeScopeId'::uuid AND title='R6 scope $RunId' AND status='draft'
AND user_id=(SELECT id FROM users WHERE email='$MemberAEmail')
RETURNING id, status;"
# 執行 10g／10h；兩筆各以前後 scope snapshot 相等作斷言。

# 10i 前：
docker compose exec -T postgres psql -X -v ON_ERROR_STOP=1 -U $DbUser -d $DbName -c "
UPDATE forms SET status='closed'
WHERE id='$SafeScopeId'::uuid AND title='R6 scope $RunId' AND status='published'
AND user_id=(SELECT id FROM users WHERE email='$MemberAEmail')
RETURNING id, status;"
# 執行 10i／10j；同樣前後完整 snapshot 相等。

# 10j 後，或失敗後的 finally／13：只限本次測試 fixture 恢復。
docker compose exec -T postgres psql -X -v ON_ERROR_STOP=1 -U $DbUser -d $DbName -c "
UPDATE forms SET status='draft'
WHERE id='$SafeScopeId'::uuid AND title='R6 scope $RunId'
AND status IN ('published','closed')
AND user_id=(SELECT id FROM users WHERE email='$MemberAEmail')
RETURNING id, status;"
if ($ScopeBaseline -cne (Get-R6Snapshot $SafeScopeId)) {
  throw 'Scope fixture was not restored to its complete baseline.'
}
```

若完成的 10j 已還原，13 不再要求 RETURNING 一列；唯讀確認 draft 與 $ScopeBaseline 相同即可。Restore 記錄與驗收結果分列，不用此 test-only 復原宣稱產品支援 Reopen。

### R6-11 — 完整 Candidate 原子性及保存故障證據

AI／人工皆可。11a 同時包含合法 Form 修改、合法 Choice→Text 清理、以及另一題非法 Config；即使已形成部分 Candidate 也不能寫入。前後完整 snapshot 相同；qNew 仍為 single_choice、oNew 存在、title 不變。保存階段 Save #2／commit 故障則引用本次 02 全套的指定測試結果與 TRX，不另改正式 API 或用手動斷 DB 方式注入。

| 編號／請求 | 前置與預期結果 | AI 實際結果 | 人工實際結果 |
| --- | --- | --- | --- |
| R6-11a · `PATCH /api/v1/forms/{{editorFormId}}` | 400 FORM_DEFINITION_INVALID；INVALID_QUESTION_CONFIG at questions[1].config；整份 Form／Questions／Options 含 ID、createdAt、updatedAt 完全不變。 | PASS：400 FORM_DEFINITION_INVALID；INVALID_QUESTION_CONFIG＠questions[1].config；完整 DB／timestamps 不變 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |

### R6-12 — 兩個並行 PATCH 的最終 Candidate 唯一性

AI 可依下方 PowerShell harness 執行；人工依相同步驟獨立操作，不以 REST Client 逐筆 Send 當成並行。12a／12b 是同時送出的兩個請求模板，單次各送一次，不先順序執行再啟動 harness。只用 03c 新增且尚未修改的 concurrent fixture。以 DB row lock barrier 與 lock waiter 證據確認重疊；一方成功後另一方必須基於最新已保存資料驗證，不可丟失更新或保存重複排序。真正 Patch／Publish、Patch／Delete 競爭留到 R7／後續，02 已含可控制 lock 的 deterministic PATCH 回歸。

| 編號／請求 | 前置與預期結果 | AI 實際結果 | 人工實際結果 |
| --- | --- | --- | --- |
| R6-12a · `PATCH /api/v1/forms/{{concurrentFormId}}` | 與 12b 同時執行；二者恰一個 200，另一個 400 FORM_DEFINITION_INVALID／DUPLICATE_SORT_ORDER at questions[0].sortOrder。不指定哪方贏。 | PASS：200；2 lock waiters；12a 勝出，12b 無部分寫入 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-12b · `PATCH /api/v1/forms/{{concurrentFormId}}` | 與 12a 同時執行；敗者無部分寫入或 500；DB 恰一題 order2，另一題仍原 order0或1，ID／createdAt 保留。 | PASS：400；2 lock waiters；12a 勝出，12b 無部分寫入 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6-12c · `GET /api/v1/forms/{{concurrentFormId}}` | 200；Questions 依最終 sortOrder 排列，無重複；內容等於成功 PATCH 的保存狀態。 | PASS：200；GET＝前次成功 PATCH；DB 無副作用 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |

### R6-12 的並行 harness（不得先手動送 12a／12b）

執行前 concurrent fixture 必須仍是 order0／1；若已跑過，換新 RunId 的整組 fixtures，或額外新建只供重跑的 concurrent Form 並記錄新 ID，不回寫排序來掩蓋前次結果。02 的 deterministic test 是自動化證據；本項另對 Windows 常駐原生 API 驗證，不互相替代。

以下使用本次 A cookie jar 與 raw JSON 暫存檔；記錄 barrier、兩個 HTTP job 起迄、至少兩個 lock waiters 及 status/body。Cookie jar 只讀，不把 Cookie 值傳入 process arguments。先確保沒有其他驗收寫入或 regression 同時跑，以免 waiter 證據混入其他請求。

```powershell
$SafeConcurrentId = ([guid]$ConcurrentFormId).ToString()
$SafeConcurrentA = ([guid]$QConcurrentA).ToString()
$SafeConcurrentB = ([guid]$QConcurrentB).ToString()
$ConcurrentBaseline = Get-R6Snapshot $SafeConcurrentId
$BodyAPath = Join-Path $env:TEMP "custom-form-r6-$RunId-12a-body.json"
$BodyBPath = Join-Path $env:TEMP "custom-form-r6-$RunId-12b-body.json"
$ResultAPath = Join-Path $env:TEMP "custom-form-r6-$RunId-12a-result.json"
$ResultBPath = Join-Path $env:TEMP "custom-form-r6-$RunId-12b-result.json"
$TempPaths += @($BodyAPath,$BodyBPath,$ResultAPath,$ResultBPath)
$Utf8 = [System.Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($BodyAPath, '{"questions":[{"id":"' + $SafeConcurrentA + '","sortOrder":2}]}', $Utf8)
[IO.File]::WriteAllText($BodyBPath, '{"questions":[{"id":"' + $SafeConcurrentB + '","sortOrder":2}]}', $Utf8)
$BarrierName = "r6-barrier-$RunId"
$Barrier = Start-Job -ArgumentList $RepoRoot,$DbUser,$DbName,$SafeConcurrentId,$BarrierName -ScriptBlock {
  param($Root,$User,$Database,$FormId,$Name)
  Set-Location -LiteralPath $Root
  docker compose exec -T -e "PGAPPNAME=$Name" postgres psql -X -v ON_ERROR_STOP=1 -U $User -d $Database -c "BEGIN; SELECT id FROM forms WHERE id='$FormId'::uuid FOR UPDATE; SELECT pg_sleep(15); COMMIT;"
  if ($LASTEXITCODE -ne 0) { throw 'Barrier failed.' }
}
# 等到 row lock 已取得且正在 PgSleep；至多等 5 秒，否則記 BLOCKED。
$BarrierHeld = $false
for ($Probe=0; $Probe -lt 20; $Probe++) {
  $Count = docker compose exec -T postgres psql -X -q -A -t -U $DbUser -d $DbName -c "SELECT count(*) FROM pg_stat_activity WHERE application_name='$BarrierName' AND wait_event='PgSleep';"
  if ($LASTEXITCODE -eq 0 -and [int]($Count -join '') -eq 1) { $BarrierHeld=$true; break }
  Start-Sleep -Milliseconds 250
}
if (-not $BarrierHeld) { throw 'No confirmed row-lock barrier; do not claim concurrent PASS.' }
$PatchJob = {
  param($Uri,$Jar,$BodyPath,$ResultPath)
  $Started = [DateTimeOffset]::UtcNow
  $Status = curl.exe -sS --max-time 30 -o $ResultPath -w '%{http_code}' -b $Jar -X PATCH $Uri -H 'Content-Type: application/json' --data-binary "@$BodyPath"
  $Code = $LASTEXITCODE
  [PSCustomObject]@{ status=$Status; curlExit=$Code; start=$Started; end=[DateTimeOffset]::UtcNow; resultPath=$ResultPath }
}
$Uri = "$ApiBase/api/v1/forms/$SafeConcurrentId"
$JobA = Start-Job -ScriptBlock $PatchJob -ArgumentList $Uri,$MemberACookieJar,$BodyAPath,$ResultAPath
$JobB = Start-Job -ScriptBlock $PatchJob -ArgumentList $Uri,$MemberACookieJar,$BodyBPath,$ResultBPath
$ConfirmedWaiters = $false
for ($Probe=0; $Probe -lt 20; $Probe++) {
  $Count = docker compose exec -T postgres psql -X -q -A -t -U $DbUser -d $DbName -c "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type='Lock' AND query ILIKE '%FOR UPDATE%' AND query ILIKE '%forms%';"
  if ($LASTEXITCODE -eq 0 -and [int]($Count -join '') -ge 2) {
    $ConfirmedWaiters=$true
    $WaiterEvidence = [PSCustomObject]@{ count=[int]($Count -join ''); observedAt=[DateTimeOffset]::UtcNow; formId=$SafeConcurrentId }
    break
  }
  Start-Sleep -Milliseconds 250
}
# pg_sleep 結束自然 COMMIT 釋放；不強制終止 DB 或 API。
@($Barrier,$JobA,$JobB) | Wait-Job -Timeout 45 | Out-Null
$BarrierEvidence = Receive-Job $Barrier
$Results = @(Receive-Job $JobA; Receive-Job $JobB)
$Results  # 不含 Cookie／secret
$WaiterEvidence
if (-not $ConfirmedWaiters) { throw 'Overlap unconfirmed: retain results, mark concurrency BLOCKED.' }
if ($Results.Count -ne 2 -or @($Results | Where-Object curlExit -ne 0).Count -ne 0) { throw 'Concurrent HTTP transport failed.' }
$Statuses = @($Results.status | Sort-Object)
if (($Statuses -join ',') -ne '200,400') { throw 'Expected exactly one 200 and one 400.' }
# 讀取兩份 body：400 的 code/path 必須符合 12；200 為完整 FormDetail。
# 執行 12c、Get-R6Snapshot，逐列驗證贏家與敗者時間／內容。
# 保存非秘密 HTTP bodies、Results、barrier/waiter 證據至 $EvidenceDir。
@($Barrier,$JobA,$JobB) | Remove-Job
```

若 timeout／throw，待 barrier 自然釋放後只回收本段 job；先保存實際結果再記 FAIL／BLOCKED，未完成的 12c 不補 PASS。不刪 concurrent fixture，不停止 postgres。若在執行平台中需改 harness 語法而不變測試語意，記錄修正；若改請求、先後或數據效果，成對計畫重審。

### R6-13 — 人工檢視、復原暫存狀態與保留資料（非 HTTP）

AI 可執行暫存檔／placeholder 清理及唯讀核對；人工使用 REST Client／DBeaver 獨立核對 05／07／09／10／11／12 的主要流程、error paths、timestamps 與 DB 資料，結果只填人工欄。檢查不依賴 Nuxt。依下方收尾操作恢復 scope fixture，保留所有 R4／R5 與本次三張 R6 fixture、非秘密測試證據；不做 hard-delete／TRUNCATE／rebuild／volume cleanup。Owner 確認前不標 accepted。

**AI 實際結果：**PASS（2026-10-04）：三張既有 Form 與 R4 公開會員欄位逐一相等；新三張 Form 保留；scope 完整復原；兩個 TEMP Cookie jar 移除；221 個證據／文件掃描未命中實際密碼、signing key 或 JWT。原生 API／開發 PostgreSQL 保留供人工驗收。

**人工實際結果：**未執行（人工；Owner 依 AI 提交證據與 review 確認驗收）。

### R6-13 收尾及資料核對

即使中途失敗，也恢復本次 scope fixture（10 的 finally）並記錄成功／失敗，不回寫 editor／concurrent 的合法驗收變更。核對所有初始既有 Form snapshot 與 R4 會員公開欄位不變；既有 R5 nested Draft、Soft Deleted Form 與子資料仍在。完整成功流程後，新 editor 保留 6 Questions／3 Options、deadline=null；scope 恢復 draft，concurrent 保留實際贏家狀態。若流程或並行未完成，記錄實際保存狀態，不假設已達最終數量或已變動。

```powershell
# $TempPaths 自始收集本次實際建立的明確 TEMP 檔；包括任何 login body（內含密碼）。
# 不遞迴掃描或移除 TEMP 目錄。Cookie jars 必須刪除；非秘密證據另保存。
Remove-Item -LiteralPath $MemberACookieJar,$MemberBCookieJar -ErrorAction SilentlyContinue
foreach ($Path in $TempPaths) {
  $Resolved = [IO.Path]::GetFullPath($Path)
  $TempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
  if (-not $Resolved.StartsWith($TempRoot,[StringComparison]::OrdinalIgnoreCase) -or
      -not ([IO.Path]::GetFileName($Resolved)).StartsWith("custom-form-r6-")) {
    throw 'Unexpected temporary path; preserve and inspect it.'
  }
  Remove-Item -LiteralPath $Resolved -ErrorAction SilentlyContinue
}
$Http = Get-Content -Raw $ManualHttpPath
$Http = [regex]::Replace($Http,'(?m)^@member[AB]Token = .+$', { param($m) ($m.Value -split '=')[0].TrimEnd() + ' = PASTE_ACCESS_TOKEN_VALUE_ONLY' })
$Http = [regex]::Replace($Http,'(?m)^@testPassword = .+$','@testPassword = ENTER_RETAINED_R4_PASSWORD_LOCALLY')
[IO.File]::WriteAllText($ManualHttpPath,$Http,[Text.UTF8Encoding]::new($false))
# Clear REST Client cookies／本次登入回應 tab；移除 memory 內密碼／Token 變數。
# 若 02 實際建立了專用 Linux SDK 容器，只在確認其名稱／ID 為本次保存者後：
# docker stop $SdkContainer
# docker rm $SdkContainer
# 保留正式 postgres container／custom-form-service-postgres-data／User Secrets／原始 source。
git diff --check
git status --short
```

保留 $EvidenceDir 的去識別／無秘密 TRX、HTTP body、snapshot 與 case mapping；文件只填精簡證據與位置，人工結果必須註明 Owner 提供或實際觀察來源。token/password placeholders 恢復後再提交執行紀錄；不得把驗收 fixtures 清掉以換取乾淨 DB。

## 尚待確認與後續邊界

本次已實跑 Windows 原生新版 API 與完整 testhost regression，見 01／02；沒有使用歷史開發結果代填。本次 Linux 未執行，Windows 已成功而未啟用條件式 fallback。人工結果未執行。真正 PATCH／Publish、Submit／Close／Delete、Deadline Submit 臨界、Statistics 數值排序與 Google 整合分屬 R7～R10／G1，未執行、不提前建立其 API。UUID 接受格式／lock 取得時機等既有延後事項不在本次改契約。

人工 HTTP 不展開每個 Config variant／Unicode 極限／JSONB 極端指數／日期邊界／全部巢狀 Shape；02 完整 suite 覆蓋已有開發回歸，新增手動案例只驗主流程及不同錯誤類型。未由本次 plan 證明的細部邊界保持未驗證，不降低正式規格。若發現規格衝突，記文件／章節／影響並停止受影響項；若發現產品缺陷，交回 R6 修正階段，不能在本階段修改程式或更改預期結果求 PASS。

## 交付與結果記錄

| 項目 | AI 實際結果 | 人工實際結果／來源 |
| --- | --- | --- |
| Windows 原生 API 啟動（01） | PASS：PID 12064、Development／5000；01a～01f 通過 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| Windows 原生全套 testhost（02） | PASS：118/118，exit 0，0 failed／0 notExecuted | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| 本次全套回歸／必要時 Linux（02；平台分列） | Windows PASS；Linux 未執行（fallback 條件未成立） | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| R6 HTTP／DB 主要案例（03～12） | PASS：50 個 HTTP 子項；完整 DB／原子性／並行已核對 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |
| 暫存狀態復原與保留資料（13） | PASS：scope 完整復原；資料保留；Cookie／秘密清理完成 | 未執行（人工；Owner 依 AI 提交證據與 review 確認驗收） |

執行紀錄另記 RunId、Owner 核准版本／執行授權、實際 SHA、平台／工具、實跑命令、子項結果、保留 ID、清理紀錄、FAIL／BLOCKED／未驗證事項及剩餘 Git state。AI 執行報告當時以「驗收結果待 Owner 確認」交付；Owner 其後明確確認通過，現記 accepted。

**目前狀態：R6 accepted（2026-10-04，Owner 依 AI 提交證據與 review 明確確認）；人工全部未執行。** 本次 R6 僅文件收尾；R7 另只準備實作計畫，尚未開發。

## 本次 AI 執行紀錄（2026-10-03～04）

Owner 核准與執行授權來源：本 chat 明確核准兩份目前文件並授權 AI 驗收。核准文件／執行 source HEAD 均為 `034a0be8b4347f14c7609a8ec2460dfa2eccdd40`（程式基準 `af1a5a3`）；起始 Git 乾淨。唯一 RunId：`r6-acceptance-20261003-01`，先查 DB 確認未使用；跨午夜續跑保留同 RunId，沒有重建或覆寫測資。

**AI 結果：**56/56 HTTP 子項 PASS；02 完整 Windows 自動回歸 PASS；13 復原／清理 PASS。沒有產品案例 FAIL／BLOCKED，沒有修改業務程式、schema、migration、請求或預期。**人工結果：**全部未執行；Owner 依 AI 提交證據與 review 確認驗收，沒有手動操作結果可記錄。

| 平台／操作 | 實跑命令／結果 |
| --- | --- |
| Windows SDK／DB | `dotnet --version`＝10.0.400；`docker compose config --quiet` exit 0；`docker compose up -d`／`docker compose ps` 確認 PostgreSQL 17.11 healthy，原 volume 保留 |
| Windows restore | `dotnet restore CustomFormService.sln --disable-build-servers -m:1` exit 0 |
| Windows build | `dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1 -p:UseSharedCompilation=false` exit 0，0 warnings／0 errors，20.04 秒 |
| Windows native API | `dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --launch-profile http --no-build` 啟動成功，Development、localhost:5000，PID 12064／dotnet exec 本專案 DLL；01 工程端點通過 |
| Windows full regression | `dotnet test CustomFormService.sln --no-build --no-restore --configuration Debug -m:1 --logger "console;verbosity=normal" --logger "trx;LogFileName=windows-full.trx" --results-directory artifacts/r6-acceptance-evidence/r6-acceptance-20261003-01` exit 0；2026-10-04 00:21:42～00:21:58，118 executed／118 passed／0 failed／0 notExecuted，15.7945 秒 |
| Linux | 未執行；Windows testhost 本次正常，核准計畫的條件式 fallback 未啟用，未建立 SDK 暫存容器 |
| HTTP／DB | bundled Python `.../dependencies/python/python.exe` 執行 ignored helper `execute.py` 的 `baseline`、`01`、`retained`、`03-05`、`06-09`、`10-11`、`12`、`13`；urllib 逐筆讀取 .http 原始 JSON，代入本次 ID／保留身分；psql 經 `docker compose exec -T postgres psql -X -q -A -t -v ON_ERROR_STOP=1` 執行計畫 SQL 與完整 snapshots |
| 文件檢查 | 56 個同編號／請求／預期／AI 結果一致，AI 執行交付時人工欄全未執行；可執行 HTTP 內容與核准版本相同；`git diff --check` PASS；文件更新後最終秘密掃描 226 個檔案、0 命中，TEMP Cookie jar 剩餘 0 |

本次 02 TRX 明確包含 `Patch_deadline_uses_actual_storage_precision_for_no_op` 的 12 案、`Concurrent_patches_are_serialized_before_candidate_validation`、`Patch_rolls_back_first_save_when_second_save_has_database_failure`、`Patch_does_not_return_success_and_rolls_back_when_commit_fails`，全部 Passed。故障只發生於 TestFactory 自建資料／交易；常駐 API 未被注入故障。

12 的 PowerShell Start-Job 範例以等價 Python subprocess＋兩個 HTTP workers 執行，未改 barrier／請求／資料效果：`pg_sleep(15)` 自然 COMMIT，00:30:03 觀察到兩個 Lock waiters（transactionid／tuple）；12a＝200，12b＝400，detail＝`DUPLICATE_SORT_ORDER`／`questions[0].sortOrder`。只更新 qConcurrentA order0→2，qConcurrentB 仍 order1；ID／createdAt 與敗者 updatedAt 保留，12c GET 等於成功回應。這是相同測試語意的工具轉換，未以順序送出替代並行。

證據目錄：[artifacts/r6-acceptance-evidence/r6-acceptance-20261003-01/](../../artifacts/r6-acceptance-evidence/r6-acceptance-20261003-01/)（gitignored，本機保留）。逐筆映射見 `results.json`、`R6-XXx.request.json`（登入 request 不保存）、`R6-XXx.response.json` 與 before／after DB snapshots；全套 `windows-full.trx`／`windows-full.log`／`windows-full.exit.txt`；並行 `R6-12-overlap.json`／`R6-12-barrier.log`；保留基準 `retained-baseline.json`；清理 `R6-13-cleanup.json`。證據不含 Cookie／Set-Cookie／密碼或 User passwordHash。

| 保留資源 | 實際 ID／狀態 |
| --- | --- |
| R4 兩位會員／既有三張 Form | User 公開欄位及三個 Aggregate 全列快照與執行前相等；兩位 active，沒有刪除 |
| R5 nested Draft | `f8eb26cc-2c74-4ff5-b64c-65bda613916b`，保留原狀 |
| R5 Soft Deleted Form | `fce3cdc1-a9ec-426c-a278-7a689b3a6b46`，含子資料保留原狀 |
| 本次 editor | `5975fea4-c75e-4a16-a5e7-6e53f976594d`，Draft／6Q／3O、deadline=null |
| 本次 scope | `78b14621-30d2-40d8-aabc-1057200f3b7d`，恢復 Draft，整份快照等於 03b 後基準 |
| 本次 concurrent | `2427ed41-2dc1-4f07-ba10-10147e82fda9`，Draft，保留 order1／2 的合法贏家結果 |

清理實際完成：移除本次兩個 TEMP Cookie jar；文件的 HTTP/password placeholders 始終未寫入有效憑證；Python 過程內秘密隨 process 結束消失。掃描當時 221 個證據／文件，對本機實際兩份 JWT、保留合成密碼、User Secrets signing key／DB password 與 JWT 格式均無命中。未新增秘密設定或需還原的 User Secrets；本次無其他秘密暫存 request／header 檔，未建立 Linux SDK container。PostgreSQL container／volume、原生 API 保留供人工驗收。

**工具／環境診斷（保留，不視為產品缺陷）：**起初 sandbox 無法連 Docker pipe，通過自動審核後正常；先前額度耗盡時 DB 基準查詢未執行，Owner 指示重置後才續跑。續跑時誤嘗試啟動第二個 API，該程序因 address in use 終止；已核對本次原先啟動的 PID 12064 仍正常，未停止其他程序。Helper 最初以 cp950 讀 UTF-8 R4 文件失敗，沒有送登入；修正 UTF-8 後，01d 曾因未展開 R4 本機密碼模板收到 401（保留 `R6-01d-helper-template-attempt.response.json`），再正確解析 R4 自身 RunId、使用同一保留密碼得到 200。未重設密碼、修改會員或改請求契約。最終 56 個核准子項的 PASS 均有實跑證據；此額外診斷 401 不冒稱成功。

AI 執行紀錄提交為 `81b64f3c87b2e9efb4f1955cf0397acb99125114`；本次 Owner 確認後同步 Implementation Plan 與成對驗收狀態，無測試語意修訂、無新增執行。人工與 Linux 未執行項分開保留；Owner 依提交證據確認 R6 accepted，並非補填人工結果。文件收尾提交 SHA 與最終 Git 狀態由交付訊息提供。

**R6 accepted；人工未執行，Owner 依 AI 提交證據與 review 確認驗收。**
