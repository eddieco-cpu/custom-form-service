# R9 驗收計畫 — My Latest／Respondent Count

2026-10-05；**Owner 已核准計畫並授權本次 AI Execute（2026-10-05）**。核准計畫 `17b1c2f`；Owner 指定三項局部補強後直接執行，不需再次詢問。三項補強完成於 `f8a8026`。Owner 隨後明確指定動態註冊並登入 A/B 新會員，03 改為四個 requests；總數 40，R9 業務預期不變。候選程式／測試／正式規格基準 `32bb41c2aa02600d3e1f6d10466676e3a145160a`；目前沒有另存於 repo 的 R9 review 報告，不補造審查證據。

Companion：[R9_Manual_Tests.http](R9_Manual_Tests.http)。依 [AGENTS.md](../../AGENTS.md)、[acceptance skill](../../.agents/skills/v1-acceptance-testing/SKILL.md)、[Implementation Plan](../implementation/V1_Backend_Implementation_Plan.md) R9、[核准實作計畫](../implementation/R9_Implementation_Plan.md) 與 [開發報告](../implementation/R9_Development_Report.md)。正式對照：01 SUB-06～07／STA-01；02 §10～11；03a §5～6；03b §3.6／6.1／8；03c §3.1／3.3／5～6；04 §4／5／7／9；05 §13～15／20。

只驗 Latest／Count 及已實作依賴；40 個順序 HTTP requests，4 組非 HTTP 工作（01、02、10、13）。不呼叫 Statistics、不改業務程式／schema、不重建 DB、不開始 R10。所有 AI／人工結果先填 **未執行（等待 Owner review）**；歷史開發 PASS 不代填本次驗收。

## 共用變數、測資與執行順序

兩檔使用同一新 RunId `r9-acceptance-20261005-01`（規劃值，尚未執行）；每次獨立 AI／人工 run 另取未使用的 RunId 並同步兩檔。使用本次動態註冊的兩位 Active 合成會員：A 擁有新 Form，B 是不同會員。03a 註冊 A、03b 登入 A、03c 註冊 B、03d 登入 B；依 Owner 本 chat 明確指示追加這兩個註冊請求，並非新功能驗收。Email 為 `r9-a-r9-acceptance-20261005-01@example.com`／`r9-b-r9-acceptance-20261005-01@example.com`，碰撞時換新 RunId 並同步兩檔；密碼使用本次隨機 10～128 字元值，僅記憶體／本機臨時操作，不寫進文件、Git、log 或證據。不讀舊密碼／JWT，不改舊會員 Profile／status；新會員與其合法 fixtures 保留。

```powershell
$RunId = 'r9-acceptance-20261005-01'
$ApiBase = 'http://localhost:5000'
$FutureDeadline = '2036-10-05T00:00:00.123456Z'
$RepoRoot = (Get-Location).Path
$EvidenceRoot = Join-Path $RepoRoot "artifacts/r9-acceptance/$RunId"
$DbName = '<POSTGRES_DB from local .env>'
$DbUser = '<POSTGRES_USER from local .env>'
# Member A／B email 隨本次 RunId；隨機 password、Cookie jar 或 token 只在本機設定。
```

以 companion 為唯一 HTTP Request Body 樣本；AI 可用 `curl.exe` 或既有 Python standard-library HTTP／psql harness，必須逐一對應 case ID，不新增正式 HTTP cases。不重新序列化精確 Number token；保存原始非秘密 Response／UTC／status／Content-Type，數值比較採 JSONB 或精確表示，避免 ConvertFrom-Json／double／decimal 捨入。人工 REST Client 先 Clear All Cookies；每 request `@no-cookie-jar`，只使用明確 Cookie。註冊／登入的請求密碼與登入回應 Set-Cookie 不存入共享證據，token 僅留記憶體／本機暫存 jar；執行後全部還原 placeholders。

依 01→02→03…→13；03～12 必須 01a／01b、02 PASS。失敗即保留首次證據，依賴列 BLOCKED，不重放 POST 或重設 fixture 求 PASS；multiple transport 結果不明時先查 DB，不能盲目重試新增歷史。清理 13 不因前置失敗跳過。

| 新 fixture（皆 Owner A） | 定義／用途 | 正常完成後保留 |
| --- | --- | --- |
| mainFormId | multiple／anonymous／owner_only；optional Text sortOrder=20、Number=10；未來 Deadline。反序 answers、B 兩份歷史、A 零 Answer，deadline probe 後復原 | Closed＋Soft Delete；2 Questions／3 Responses／3 Answers |
| emptyFormId | single／identified／public；1 optional Text。先 Draft 權限，再 Published／Closed 零資料 | Closed 未刪除；1 Question／0 Responses／0 Answers |

04 只建立 **2 Forms／3 Questions／0 Options**。取本次 server UUID，Question 依 type／sortOrder 配對變數，不以 Request array index 猜 ID。03 取得本次 A／B ID、確認不同且 active，新增兩位 User 不影響舊列；03a 前先查兩個 email 不存在，04 前 DB 查 RunId title 與 missingFormId 未存在，RunId title 碰撞時換新 RunId；missingFormId（初值 `00000000-0000-4000-8000-000000000009`）碰撞時以 `[guid]::NewGuid()` 產生新 UUID，再查 DB 直到不存在，同步本 Markdown 的實際值與 .http `@missingFormId` 並記錄查證結果，不能只換 RunId。不修改舊 fixtures。最終新增 2 Users／2 Forms／3 Questions／0 Options／3 Responses／3 Answers；其他舊資料完全保留。

## 01 — 環境、本次 build／原生 API 身分與 baseline（非 HTTP）

目的：確認本次候選版本、既有 PostgreSQL host binding／連線、保留資料及真正的原生 API。以下腳本依本次 Owner 授權執行；各階段失敗保留首次 log／exit code，停止該平台的依賴操作。

```powershell
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null
function Invoke-NativeStage([string]$Stage, [string]$Exe, [string[]]$StageArgs) {
    & $Exe @StageArgs 2>&1 | Tee-Object -FilePath (Join-Path $EvidenceRoot "$Stage.log")
    $stageExit = $LASTEXITCODE
    Set-Content -LiteralPath (Join-Path $EvidenceRoot "$Stage.exit.txt") -Value $stageExit
    if ($stageExit -ne 0) { throw "$Stage failed (exit $stageExit); stop dependent stages" }
}
git status --short
if ($LASTEXITCODE -ne 0) { throw 'Prerequisite failed; stop dependent stages and retain output' }
git rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Prerequisite failed; stop dependent stages and retain output' }
git diff 32bb41c -- src tests docs/specs
if ($LASTEXITCODE -ne 0) { throw 'Prerequisite failed; stop dependent stages and retain output' }
dotnet --info
if ($LASTEXITCODE -ne 0) { throw 'Prerequisite failed; stop dependent stages and retain output' }
docker compose config --quiet
if ($LASTEXITCODE -ne 0) { throw 'Prerequisite failed; stop dependent stages and retain output' }
docker compose ps
if ($LASTEXITCODE -ne 0) { throw 'Prerequisite failed; stop dependent stages and retain output' }
docker inspect custom-form-service-postgres-1 --format '{{json .NetworkSettings.Ports}}'
if ($LASTEXITCODE -ne 0) { throw 'Prerequisite failed; stop dependent stages and retain output' }
# DB 只讀連線，不提供密碼 argument，不輸出 secrets／完整 connection string。
docker compose exec -T postgres psql -U $DbUser -d $DbName -X -v ON_ERROR_STOP=1 -c 'SELECT current_database(), current_user, now();'
if ($LASTEXITCODE -ne 0) { throw 'Prerequisite failed; stop dependent stages and retain output' }
Invoke-NativeStage 'windows-restore-first' 'dotnet' @('restore','CustomFormService.sln','--disable-parallel','--verbosity','minimal')
Invoke-NativeStage 'windows-build-first' 'dotnet' @('build','CustomFormService.sln','--no-restore','--configuration','Debug','--disable-build-servers','-m:1','-p:UseSharedCompilation=false')
# restore 失敗不 build；build 失敗不 test／啟動。先核對所有輸入，不複製秘密。
# API 是長駐進程，Start-Process 成功不等於啟動成功；記錄早退的 ExitCode 與 stderr。
try {
    $ApiLauncher = Start-Process -FilePath 'dotnet' -ArgumentList @('run','--project','src/CustomFormService.Api/CustomFormService.Api.csproj','--launch-profile','http','--no-build','--no-restore') -WorkingDirectory $RepoRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $EvidenceRoot 'api-stdout-first.log') -RedirectStandardError (Join-Path $EvidenceRoot 'api-stderr-first.log')
} catch {
    $_ | Out-String | Set-Content (Join-Path $EvidenceRoot 'api-launch-first-error.log')
    throw
}
# 有限等待 launcher／listener；若已退出，Refresh() 後記 ExitCode，停止 HTTP gate。
# 活著仍須下述 listener／DLL 身分及 01a／01b，不能只用 HasExited=false 判 PASS。
Get-FileHash src/CustomFormService.Api/bin/Debug/net10.0/CustomFormService.Api.dll -Algorithm SHA256
```

執行前創建 EvidenceRoot；記錄工作目錄／HEAD／UTC／restore-build log 與 exit code／DLL hash／launcher PID。核對 localhost:5000 listener 的實際 API PID、父子關係、程序參數中的本 repo apphost／DLL 路徑、啟動日誌的 Content root／Development 與 launcher 的 http launch profile；用相同輸出目錄的 DLL hash 對應本次 build。不能只看 launcher PID 或 health 200，不能連接其他 repo／舊進程冒充本次 build。若舊 API 鎖 DLL，只暫停已驗證屬此 repo 的進程並保留恢復資料，不能停陌生程序。端口不明佔用／source diff 非預期須先停受影響執行。

**已知風險**：R9 開發時 Windows application control `0x800711C7` 阻擋新 API／test DLL，localhost:5000 未能恢復；本次必須重新證明原生啟動與 01a／01b，不能沿用舊 health、TestServer 或 Linux PASS。若加載／啟動／readiness 失敗，01 記錄實際 FAIL／BLOCKED，01a／01b 按實際請求或未發送區分，03～12 全部 BLOCKED；不改安全政策、不另啟 Linux API 代填。本計畫只允許 02 的既有 Linux SDK **測試** fallback。

在 02 前用現有 DB／psql `-X -q -A -t -v ON_ERROR_STOP=1`（或人工 DBeaver 等價只讀）保存六表非秘密 baseline、所有舊 IDs／counts；不含 password_hash／JWT／外部 Identity credential。Query 見下節。確認 PostgreSQL volume 持續保留，不能執行 migration／down -v／force recreate。

| 非 HTTP 工作 | 預期與證據 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| 01 | 候選版本一致，restore/build exit 0；同 repo 本次 DLL 的原生 API；DB host binding／連接與 baseline；manifest/log | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

## 02 — 同版本完整回歸與承接案例（非 HTTP）

目的：承接已存在的開發測試，**重新執行當前版本**並核對案例與證據，不能用歷史 26／306 自動填 PASS。原生 API health/readiness 成功後首選 Windows full；若 Windows test loader 被政策阻擋，保留失敗 log／TRX，可依既有只讀 Linux SDK fallback 跑同 source/tests。若 01 原生 API 已被阻擋但 DB／baseline 可用，02 可獨立跑 fallback，仍不解鎖 03～12。

```powershell
Invoke-NativeStage 'windows-full-first' 'dotnet' @('test','CustomFormService.sln','--no-build','--no-restore','--configuration','Debug','-m:1','--logger','console;verbosity=normal','--logger','trx;LogFileName=r9-acceptance-full-first.trx','--results-directory',$EvidenceRoot)
```

預期當前 full **306**、其中 R9 **26**，以實際 discovery／TRX 為準；0 failed／notExecuted，新增／少案須解釋，不把 0 tests 當 PASS。完整 full 已包含下表，無需為湊證據重複 focused。取 02-after baseline，與 01 舊列逐字／結構精確比較；測試自有臨時資料必須清完，舊 fixture 不變。

歷史證據（只作設計依據）：[開發報告](../implementation/R9_Development_Report.md)；[Linux focused 26/26 TRX](../../artifacts/r9-development/linux-r9-focused-first.trx)、[Linux full 306/306 TRX](../../artifacts/r9-development/linux-full-first.trx)、[開發資料比較](../../artifacts/r9-development/verification-summary.json)。Windows loader 失敗與原生 API 未恢復亦保留。以下方法均位於 [ResponseQueries 測試](../../tests/CustomFormService.IntegrationTests/FormEndpointsTests.ResponseQueries.cs)；本次應保存新 TRX／stdout／同候選 source hash。人工本輪結果獨立，不把 AI 代填成人工。

| 驗收對應／保證 | 當前方法（省略共同 R9_ 前綴）／DataRows | 本次證據要求 |
| --- | --- | --- |
| 08／09 Receipt、single／multiple、無寫入 | latest_returns_persisted_receipt_and_GET_has_no_writes：4 rows | 保存值／完整欄位／排序、DB snapshot；實際 PASS 4 |
| 08 omission／整份選擇 | latest_uses_whole_response_without_backfilling_optional_answers：2 rows | partial／全部 optional 缺答，不補舊 Answer |
| 同時間／穩定排序（非 HTTP） | latest_timestamp_precedes_UUID_and_same_timestamp_uses_UUID_descending：1 | 已知高／低 UUID，同 submitted_at 高 ID 勝；低 ID 時間加一微秒後勝；不是插入順序 |
| 08／09 Count | count_deduplicates_successful_users_including_owner_empty_answers_and_disabled_history：1 | 4 Responses／3 Users；Owner／零 Answer／disabled 歷史；Form List 一致 |
| 05／07／11 狀態空資料 | state_owner_and_empty_data_matrix：5 rows | Draft／Published／Closed，Owner／非 Owner，空／有回覆 |
| 10／11 政策不影響存取 | deadline_and_result_policies_do_not_change_latest_or_count_access：6 rows | Identity 兩值 × Visibility 三值，截止後與 Closed，Count 非 Owner 403 |
| 09 身分邊界／跨 Form | latest_is_scoped_to_current_user_and_form：1 | A/B 各自完整 Response、另一 Form 不串；不接受指定他人身分 |
| 06／12 精確錯誤優先序 | authentication_UUID_and_resource_errors_keep_priority：2 rows | 兩端點各自缺／壞／過期／不存在 User／disabled、UUID／Deleted；不修改保留會員 |
| RepeatableRead 快照（非 HTTP） | completed_Form_SELECT_establishes_snapshot_before_concurrent_write：4 rows | ReaderExecutedAsync 確認實際 Form SELECT 完成且交易 RR 後才暫停；另一 Submit／Delete 已 commit，舊 GET 穩定、下次 GET 見新值／404；每組 StdOut |

上表合計 26。快照案是 **TestServer＋真實 PostgreSQL**，不是原生 HTTP race；承接該測試即可，不新增生產 hook／原生 barrier。不能以 BeginTransaction 當快照證據，finally 必須釋放 gate／回收 worker；TRX StdOut 須有四組具體事件。

### 02 Linux SDK fallback 操作約定

沿 R9 開發途徑使用既有 `mcr.microsoft.com/dotnet/sdk:10.0.400`；每次唯一 `r9-acceptance-sdk-<RunId>` 容器，同既有 PostgreSQL network。只讀掛載本 repo→/source、既有 NuGet cache→/nuget、該項目既有 User Secrets→對應 Linux usersecrets 路徑；EvidenceRoot→/evidence 可寫。只取非秘密 DB host/port/database/user 配置，`PostgreSql__Host=postgres`，不得在 arguments/log 輸出密碼／signing key；不改 host 配置。記錄 mount 方向、source revision 與 SDK，不能取舊 /work 二進制。

先確認 Docker network 確為此 repo 的既有 `custom-form-service_default`、image/cache/User Secrets 路徑存在；若配置不同只套用已核對的等價非秘密路徑並記錄，不複製秘密。範例建立命令（核准後執行，容器名限本 RunId）：

```powershell
$SdkName = "r9-acceptance-sdk-$RunId"
$NuGetCache = Join-Path $env:USERPROFILE '.nuget/packages'
$ProjectSecrets = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Microsoft/UserSecrets/07f62f1e-ff60-47a2-bf24-866b69909532'
docker run -d --name $SdkName --network custom-form-service_default --mount "type=bind,source=$RepoRoot,target=/source,readonly" --mount "type=bind,source=$EvidenceRoot,target=/evidence" --mount "type=bind,source=$NuGetCache,target=/nuget,readonly" --mount "type=bind,source=$ProjectSecrets,target=/root/.microsoft/usersecrets/07f62f1e-ff60-47a2-bf24-866b69909532,readonly" --env PostgreSql__Host=postgres --env NUGET_PACKAGES=/nuget --env DOTNET_CLI_TELEMETRY_OPTOUT=1 --env DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true mcr.microsoft.com/dotnet/sdk:10.0.400 sleep infinity
if ($LASTEXITCODE -ne 0) { throw 'SDK container creation failed; do not copy/restore/build/test' }
# 下方 bash 內容由本次 /evidence 的非秘密腳本透過 docker exec $SdkName bash /evidence/linux-prepare.sh 執行；
# full 命令執行在 /work/r9；保存 source copy／restore／build／test 原始 log／exit code。
```

**完整輸入核對**：先列根目錄／src／tests 與上層自動匯入檔、csproj Import／ProjectReference／Content，保存 `build-inputs.json` 路徑與 SHA256。必備 `src tests global.json CustomFormService.sln`；根目錄存在的 `Directory.Build.*`、`Directory.Packages.props`、大小寫等價 NuGet.Config 一併帶入。額外 Import／建置資產須保持相對路徑與雜湊；無法確認或含秘密的 NuGet.Config 不輸出內容／不照抄，停止 fallback 並回報。2026-10-05 核對目前這些 root／ancestor 檔皆不存在，兩 csproj 無額外 Import；各專案設定與 runtime content 已在 src/tests。既有 Linux NuGet cache 配置另記非秘密來源／hash，不宣稱是 repo 檔案。

複製至新的 /work/r9，排除 bin／obj；必備與存在的 root 輸入一起 tar。`set -Eeuo pipefail`，tar pipeline 任一側失敗即停止；保留 `PIPESTATUS` 兩側 exit、stderr 與 stage exit。restore 失敗不 build，build 失敗不 test；每個 stage 都保存 exit code，不能只採最後一條命令。範例：
```bash
set -Eeuo pipefail
stage=setup
trap 'rc=$?; printf "%s %s\n" "$stage" "$rc" >> /evidence/linux-stages.exit.txt; exit "$rc"' ERR
mkdir -p /work/r9
inputs=(src tests global.json CustomFormService.sln)
shopt -s nullglob nocaseglob
for file in /source/Directory.Build.* /source/Directory.Packages.props /source/NuGet.Config; do
  [[ -f "$file" ]] && inputs+=("${file#/source/}")
done
stage=copy
if tar -C /source --exclude='*/bin' --exclude='*/obj' -cf - "${inputs[@]}" 2>/evidence/linux-copy-create.log | tar -C /work/r9 -xf - 2>/evidence/linux-copy-extract.log; then
  copy_status=("${PIPESTATUS[@]}")
else
  copy_status=("${PIPESTATUS[@]}")
fi
printf 'copy-create %s\ncopy-extract %s\n' "${copy_status[0]}" "${copy_status[1]}" >> /evidence/linux-stages.exit.txt
if ((copy_status[0] != 0 || copy_status[1] != 0)); then exit 1; fi
# Windows 清單另輸出 build-inputs.sha256，路徑相對 repo 並使用 /；涵蓋同一組輸入。
cd /work/r9
run_stage() {
  stage="$1"; shift
  if "$@" >"/evidence/linux-$stage-first.log" 2>&1; then rc=0; else rc=$?; fi
  printf '%s %s\n' "$stage" "$rc" >> /evidence/linux-stages.exit.txt
  if ((rc != 0)); then exit "$rc"; fi
}
run_stage hash sha256sum -c /evidence/build-inputs.sha256
run_stage restore dotnet restore CustomFormService.sln --disable-parallel --ignore-failed-sources
run_stage build dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1 -p:UseSharedCompilation=false
run_stage full dotnet test CustomFormService.sln --no-build --no-restore --configuration Debug -m:1 --logger "console;verbosity=normal" --logger "trx;LogFileName=r9-acceptance-linux-full-first.trx" --results-directory /evidence
```
外層 `docker exec $SdkName bash /evidence/linux-prepare.sh` 同樣透過 Invoke-NativeStage 保存 exit；非零不得執行後續依賴 HTTP。setup／copy／hash／restore／build／full 的首次失敗不得覆寫。
所有日誌／exit code 留存，明確平台為 Linux；03～12 仍須 Windows 原生 API gate。測試完只 stop／rm 此次 SDK，不 rm volume；保留初次失敗，後續若需重跑另存結果，不覆寫。

| 非 HTTP 工作 | 預期與證據 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| 02 | full 當前 306／R9 26 全 PASS；上述名稱／rows／四組實際 SELECT StdOut 齊全；舊列不變，平台明確 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

## DB 與 Receipt 證據方法（01～13）

baseline 只讀 Query；在 01、02 後、13 最終保存。13 以 baseline 的舊 IDs 過濾新結果後比較，合法本次新列單獨核對，不能把新增合成 fixtures 當舊列變化：
```sql
SELECT jsonb_build_object(
 'users', COALESCE((SELECT jsonb_agg(jsonb_build_object(
  'id',id,'email',email,'display_name',display_name,'avatar_url',avatar_url,
  'status',status,'created_at',created_at,'updated_at',updated_at) ORDER BY id)
  FROM users),'[]'::jsonb),
 'forms',COALESCE((SELECT jsonb_agg(to_jsonb(f) ORDER BY id) FROM forms f),'[]'::jsonb),
 'questions',COALESCE((SELECT jsonb_agg(to_jsonb(q) ORDER BY id) FROM questions q),'[]'::jsonb),
 'options',COALESCE((SELECT jsonb_agg(to_jsonb(o) ORDER BY id) FROM options o),'[]'::jsonb),
 'responses',COALESCE((SELECT jsonb_agg(to_jsonb(r) ORDER BY id) FROM responses r),'[]'::jsonb),
 'answers',COALESCE((SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM answers a),'[]'::jsonb));
-- 03 後／04 前，psql 參數先在本機驗證 UUID／RunId：
SELECT COUNT(*) FROM forms WHERE title IN ('R9 main '||:'runId','R9 empty '||:'runId');
SELECT COUNT(*) FROM forms WHERE id=:'missingFormId'::uuid;
-- 兩項必須都是 0，才建立本次 fixtures；03a 前另外查本次 A/B email 均不存在。
SELECT id,status FROM users WHERE id IN (:'memberAId'::uuid,:'memberBId'::uuid) ORDER BY id;
-- 必須正好兩位本次新增、不同的 active 合成會員，不能修改舊會員狀態。
```

每個 GET／錯誤請求前後取目標 Form 完整 snapshot（Form＋Questions／Options＋Responses／Answers），必須全列／timestamps 不變；這裡 formId 每次在 psql 綁定為本案 main 或 empty：
```sql
SELECT jsonb_build_object(
 'form',(SELECT to_jsonb(f) FROM forms f WHERE id=:'formId'::uuid),
 'questions',COALESCE((SELECT jsonb_agg(to_jsonb(q) ORDER BY id) FROM questions q WHERE form_id=:'formId'::uuid),'[]'::jsonb),
 'options',COALESCE((SELECT jsonb_agg(to_jsonb(o) ORDER BY o.id) FROM options o JOIN questions q ON q.id=o.question_id WHERE q.form_id=:'formId'::uuid),'[]'::jsonb),
 'responses',COALESCE((SELECT jsonb_agg(to_jsonb(r) ORDER BY id) FROM responses r WHERE form_id=:'formId'::uuid),'[]'::jsonb),
 'answers',COALESCE((SELECT jsonb_agg(to_jsonb(a) ORDER BY a.id) FROM answers a JOIN responses r ON r.id=a.response_id WHERE r.form_id=:'formId'::uuid),'[]'::jsonb));
SELECT COUNT(*) AS response_rows, COUNT(DISTINCT user_id) AS respondent_count
 FROM responses WHERE form_id=:'formId'::uuid;
SELECT id,form_id,user_id,submitted_at FROM responses
 WHERE form_id=:'formId'::uuid AND user_id=:'currentUserId'::uuid
 ORDER BY submitted_at DESC,id DESC LIMIT 1;
SELECT a.question_id,a.value,q.sort_order
 FROM answers a JOIN questions q ON q.id=a.question_id
 WHERE a.response_id=:'responseId'::uuid ORDER BY q.sort_order,q.id;
```

Receipt root 僅 id／formId／submittedAt／answers，每項僅 questionId／value；不回 userId／Answer ID。核對真正當次 User、DB UTC 時間／微秒、已保存 JSONB 值／完整集合／Question 排序，不只看 200 或 response ID。08a／08b 的長小數須與 DB JSONB 數值精確等值；08c 可從 1e0 保存為 1，不承諾原始 token。08c 若時間未嚴格晚於 08a，本案 FAIL 並留證，不臨時修改 submitted_at；同時間 tie-break 另由 02 已知 UUID 測試證明。

POST Submit 後 Definition／子列／timestamps 不變，Response＋有效 Answers 一起新增；08a 累計 1／2，08c 累計 2／3，09a 累計 3／3。Close 只允許 Form status／updated_at 變化；Delete 只允許 deleted_at／updated_at，status 仍 closed，全部子列／歷史保持相等。錯誤 Envelope 核對 HTTP／top-level code，06c 另核 details path=formId／INVALID_VALUE，不得洩漏 SQL／異常。

## 依序 HTTP 案例（Request Body 依 companion）

每行目的／前置／請求／預期與 .http 同編號。AI／人工分別填寫實際結果、執行 UTC、平台與證據路徑；例 `artifacts/r9-acceptance/<RunId>/08d.json`，只保存非秘密響應及 snapshot，登入略去 Set-Cookie。03a～d 保存新會員身分確認，註冊／登入請求密碼全部略去；每案失敗保留實際 response，不改預期。

### 01 — health/readiness

| Case／目的 | 請求（companion ID）／身分 | 前置 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 01a — 本次原生 API health | `GET /health`；無 Cookie | 01 build／原生啟動與 process／DLL 身分確認 | 200；{"status":"healthy"} | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 01b — 本次原生 API readiness | `GET /health/ready`；無 Cookie | 01a 成功 | 200；Healthy | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 03 — 本次新會員註冊與登入

| Case／目的 | 請求（companion ID）／身分 | 前置 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 03a — 註冊本次 Owner A | `POST /api/v1/auth/register`；無 Cookie | 01a／01b／02 通過；本次 email 未存在、隨機密碼僅記憶體 | 201 User；active；不設定 access_token Cookie；只新增 A | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 03b — 登入本次 Active Owner A | `POST /api/v1/auth/login`；無 Cookie | 03a 成功；使用同一隨機密碼 | 200 User＋access_token Cookie；id 等於 03a；token 僅記憶體 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 03c — 註冊本次 User B | `POST /api/v1/auth/register`；無 Cookie | 03b；B email 未存在，與 A 不同 | 201 User；active；不設定 access_token Cookie；只新增 B | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 03d — 登入本次 Active User B | `POST /api/v1/auth/login`；無 Cookie | 03c 成功；B 使用同一隨機密碼 | 200 User＋Cookie；id 等於 03c，且與 A 不同；token 僅記憶體 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 04 — 建立本次測資

| Case／目的 | 請求（companion ID）／身分 | 前置 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 04a — 建立 multiple 主 Form | `POST /api/v1/forms`；A | 03 完成、RunId／missing ID 碰撞檢查為零 | 201 Draft；2 optional Questions，qMainNumber sortOrder=10／qMainText=20 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 04b — 建立空資料／Draft Form | `POST /api/v1/forms`；A | 04a；記 emptyFormId | 201 Draft；1 optional Text，無 Response | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 05 — Draft 權限／空資料

| Case／目的 | 請求（companion ID）／身分 | 前置 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 05a — Owner 查 Draft Latest | `GET /api/v1/forms/{{emptyFormId}}/responses/me/latest`；A | 04b；empty 保持 Draft | 404 RESPONSE_NOT_FOUND | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 05b — 非 Owner 查 Draft Latest | `GET /api/v1/forms/{{emptyFormId}}/responses/me/latest`；B | 05a；相同 Draft | 404 FORM_NOT_FOUND | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 05c — Owner 查 Draft Count | `GET /api/v1/forms/{{emptyFormId}}/respondent-count`；A | 05b；相同 Draft | 200；count=0 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 05d — 非 Owner 查 Draft Count | `GET /api/v1/forms/{{emptyFormId}}/respondent-count`；B | 05c；相同 Draft | 403 FORM_ACCESS_DENIED | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 06 — 代表性錯誤優先序

| Case／目的 | 請求（companion ID）／身分 | 前置 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 06a — 未登入＋壞 UUID | `GET /api/v1/forms/not-a-uuid/responses/me/latest`；無 Cookie | 05；Clear All Cookies／不附 Cookie | 401 AUTH_REQUIRED，先於 UUID 驗證 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06b — 壞 Cookie＋壞 UUID | `GET /api/v1/forms/not-a-uuid/respondent-count`；刻意無效 Cookie | 06a；只附刻意無效 Cookie | 401 AUTH_TOKEN_INVALID，先於 UUID 驗證 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06c — Active User＋壞 UUID | `GET /api/v1/forms/not-a-uuid/responses/me/latest`；A | 06b；有效 A Cookie | 400 VALIDATION_ERROR；details path=formId／code=INVALID_VALUE | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06d — 非 Owner＋不存在 Form | `GET /api/v1/forms/{{missingFormId}}/respondent-count`；B | 06c；missingFormId 已查證不存在 | 404 FORM_NOT_FOUND，先於 Owner 判定 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 07 — Publish／Published 空資料

| Case／目的 | 請求（companion ID）／身分 | 前置 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 07a — Publish multiple 主 Form | `POST /api/v1/forms/{{mainFormId}}/publish`；A | 04a Draft；futureDeadline 尚晚於 Server UTC | 200 FormDetail；status=published | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 07b — Publish 空資料 Form | `POST /api/v1/forms/{{emptyFormId}}/publish`；A | 05 完成；1 optional Text 足以 Publish | 200 FormDetail；status=published | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 07c — Published 無本人 Response | `GET /api/v1/forms/{{emptyFormId}}/responses/me/latest`；B | 07b；此 Form 無任何提交 | 404 RESPONSE_NOT_FOUND | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 07d — Published 零 Respondent | `GET /api/v1/forms/{{emptyFormId}}/respondent-count`；A | 07c；此 Form 無任何提交 | 200；count=0 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 08 — Latest 整份選擇／排序／Count 去重

| Case／目的 | 請求（companion ID）／身分 | 前置 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 08a — B 第一份完整回覆 | `POST /api/v1/forms/{{mainFormId}}/responses`；B | 07a；反序送 answers；main 初始 0 Response | 201 Receipt；記 bResponse1Id；Answers 順序 Number→Text，Text=Old 😀，Number 精確保存 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 08b — B 查最新完整 Receipt 與排序 | `GET /api/v1/forms/{{mainFormId}}/responses/me/latest`；B | 08a 成功；取 main 完整 DB snapshot | 200；id=bResponse1Id；2 Answers，內容／時間／排序等於實際保存的 08a Receipt | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 08c — B 較新回覆略過 optional Text | `POST /api/v1/forms/{{mainFormId}}/responses`；B | 08b；multiple；保存兩次實際 UTC timestamp | 201 Receipt；記 bResponse2Id；只有 Number，submittedAt 嚴格晚於第一份 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 08d — Latest 不補舊 Text | `GET /api/v1/forms/{{mainFormId}}/responses/me/latest`；B | 08c；DB 按 submitted_at DESC／id DESC 核對 | 200；id=bResponse2Id；answers 僅 Number=1，無 Text／null | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 08e — 同人兩份 Count 去重 | `GET /api/v1/forms/{{mainFormId}}/respondent-count`；A | 08d；A 尚未提交 | 200；count=1；DB 2 Responses／3 Answers／1 distinct User | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 09 — Owner 自己／零 Answer／Count 權限

| Case／目的 | 請求（companion ID）／身分 | 前置 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 09a — Owner 自己提交整份零 Answer | `POST /api/v1/forms/{{mainFormId}}/responses`；A | 08e；所有 Questions optional | 201 Receipt；記 aResponseId；answers=[]；DB 3 Responses／3 Answers | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 09b — Owner Latest 僅自己的零 Answer 回覆 | `GET /api/v1/forms/{{mainFormId}}/responses/me/latest`；A | 09a；A 與 B 身分不同 | 200；id=aResponseId；answers=[]，不取得 B 的 Answers | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 09c — Count 包含 Owner 與零 Answer | `GET /api/v1/forms/{{mainFormId}}/respondent-count`；A | 09b；DB 3 Responses／3 Answers／2 distinct Users | 200；count=2，不是歷史 Response 數 3 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 09d — Respondent 仍無 Count 權限 | `GET /api/v1/forms/{{mainFormId}}/respondent-count`；B | 09c；B 曾成功提交但非 Owner | 403 FORM_ACCESS_DENIED；不回 count／Answers | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 10 — 截止後讀取（先執行下方 probe）

| Case／目的 | 請求（companion ID）／身分 | 前置 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 10a — 截止後 Published 仍可讀 Latest | `GET /api/v1/forms/{{mainFormId}}/responses/me/latest`；B | Markdown 10 probe：備份並只暫設本次 main deadline 為過去 | 200；仍為 bResponse2Id，僅 Number=1；Form 仍 published | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 10b — 截止後 Published 仍可讀 Count | `GET /api/v1/forms/{{mainFormId}}/respondent-count`；A | 10a；仍為過期 deadline；完成後必做 Markdown 10 finally 復原 | 200；count=2；GET 無寫入 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 11 — Closed 與零資料

| Case／目的 | 請求（companion ID）／身分 | 前置 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 11a — Close 主 Form | `POST /api/v1/forms/{{mainFormId}}/close`；A | 10 deadline 已成功復原、全列快照相等 | 200 FormDetail；status=closed；保留全部 Responses／Answers | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 11b — Close 零資料 Form | `POST /api/v1/forms/{{emptyFormId}}/close`；A | 11a；empty 從未提交 | 200 FormDetail；status=closed；0 Responses／Answers | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 11c — Closed Latest 可讀本人歷史 | `GET /api/v1/forms/{{mainFormId}}/responses/me/latest`；B | 11a；不受 owner_only／anonymous 影響 | 200；仍為 bResponse2Id，只含 Number=1 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 11d — Closed Owner Count | `GET /api/v1/forms/{{mainFormId}}/respondent-count`；A | 11c；3 歷史 Responses 仍保留 | 200；count=2 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 11e — Closed 無 Response | `GET /api/v1/forms/{{emptyFormId}}/responses/me/latest`；A | 11b；empty 保持零資料 | 404 RESPONSE_NOT_FOUND | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 11f — Closed Count 空資料 | `GET /api/v1/forms/{{emptyFormId}}/respondent-count`；A | 11e；public／identified 不改 Owner Count | 200；count=0 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 12 — Soft Delete／存在性優先

| Case／目的 | 請求（companion ID）／身分 | 前置 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 12a — Soft Delete 主 Form | `DELETE /api/v1/forms/{{mainFormId}}`；A | 11 全部完成；保存 main-before-delete 全列 snapshot | 204；deleted_at 非 null、status 仍 closed；子列與回覆全部保留 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 12b — Deleted Latest 不繞過保留回覆 | `GET /api/v1/forms/{{mainFormId}}/responses/me/latest`；B | 12a；DB 仍有 B 的歷史 Responses | 404 FORM_NOT_FOUND，不是 RESPONSE_NOT_FOUND | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 12c — Deleted Count 存在性先於 Owner | `GET /api/v1/forms/{{mainFormId}}/respondent-count`；B | 12b；原先 B 是 403；子列保留 | 404 FORM_NOT_FOUND，不是 FORM_ACCESS_DENIED | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

## 10 — deadline probe 與 finally（非 HTTP）

在 09 全部完成後、10a 之前操作；只針對本次 A＋RunId＋mainFormId 的新 Form。完整備份 main snapshot／original deadline 到本次 evidence，驗證 id／Owner／title／status=published／deleted_at IS NULL，UUID 與 manifest 一致。原值必須等於本輪 futureDeadline；UPDATE 必須恰一列，異常不得發後續 HTTP。每條操作以 autocommit 或短 transaction 完成後才發 HTTP，不留鎖等待請求。

```sql
UPDATE forms SET submission_deadline_at=clock_timestamp()-interval '1 hour'
 WHERE id=:'mainFormId'::uuid AND user_id=:'memberAId'::uuid
 AND title='R9 main '||:'runId' AND status='published' AND deleted_at IS NULL
 RETURNING id,submission_deadline_at,status;
-- 確認新 deadline < Server UTC、status 仍 published；提交後才發 10a／10b。
-- finally：originalDeadline 從已驗證的 probe-before snapshot 原樣取得，不靠“現在＋若干天”猜。
UPDATE forms SET submission_deadline_at=:'originalDeadline'::timestamptz
 WHERE id=:'mainFormId'::uuid AND user_id=:'memberAId'::uuid
 AND title='R9 main '||:'runId' AND deleted_at IS NULL
 RETURNING id,submission_deadline_at;
```

無論 10a/b PASS／FAIL／transport error 都執行 finally，確認還原恰一列，恢復後完整 snapshot 與 probe-before 相等（這期間只有 GET）。不改 updated_at／status；復原失敗記 FAIL，11～12 BLOCKED，13 仍處理安全復原並保留未完成證據，不重跑或刪除合法 Response。本 probe 不測試 Submission Deadline 拒絕邏輯／R10。

| 非 HTTP 工作 | 預期與證據 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| 10 probe／10z finally | 僅本次 main deadline 暫變、截止後 GET 正確；原值／全列精確復原，舊 fixtures 不動 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

## 13 — 最終保留資料、證據與清理（非 HTTP）

AI／人工均執行各自 run 的清理；失敗 run 仍記錄能確認的實際情況，不用正常成功路徑數字代填。

1. 確認 10 probe 恢復；所有本輪普通 GET／錯誤查詢前後 snapshot 完整相同。重新取六表非秘密 snapshot：baseline 舊 IDs 的列完全不變，包括既有 soft-deleted Forms／歷史 Responses。不得清除舊會員、舊 fixtures 或 volume。
2. 正常流程本次增量：2 Users／2 Forms／3 Questions／0 Options／3 Responses／3 Answers，新 Users 的 email 對應本次 RunId；Forms 皆 Owner A，Response 的 User 是 A/B。main 仍 closed、deleted_at 非 null，2 B 歷史＋1 A 空答案原樣保留；empty closed、未刪除、零 Response。部分失敗按真實新增列記錄並保留，不刪合法部分成果。
3. SDK fallback 若使用，結束測試後 stop／rm **僅本次唯一 SDK**，回收自己 workers／臨時 sessions；不動 PostgreSQL、其他 API、未知進程。正常通過啟動的本次開發 API 保留；失敗啟動記錄真實退出／未恢復，不能宣稱服務可用。恢復臨時 environment／啟動設置，僅清除本人創建的 credential jar／usable token／秘密臨時文件，文件恢復 placeholders；非秘密 response／snapshot／日誌／TRX 保留。
4. 比較本次 source/tests/specs 未變與 git diff；按批准 plan 逐案彙總 PASS／FAIL／BLOCKED／未執行、平台／命令／首次失敗／清理狀態。AI 與人工分開；交付“驗收結果待 Owner 確認”，不標 accepted／不進 R10。

| 非 HTTP 工作 | 預期與證據 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| 13 | 舊列不變；新合法 fixtures／歷史保留；probe／worker／SDK／credentials 清理完成或明確未完成；manifest／比對摘要 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

## 執行證據與範圍邊界

執行時每行結果綁定 case ID／UTC／實際平台／HTTP 原始請求 method/path 與非秘密 body／status／response（註冊／登入不留 password 或 Set-Cookie）／before-after DB snapshot。非 HTTP 01 保存 build/startup identity 與 baseline；02 新 full TRX、九個方法／26 rows／四組 SELECT 事件；10 保存 probe 原值／變化／finally；13 保存保留清理彙總。將執行報告與結果回填成對文件，同一語意變更須重新 review，不修改預期求 PASS。

HTTP 矩陣保持兩張 Form 的主流程；Auth 過期／未知 User／disabled、Identity×Visibility 六組合、跨 Form、所有五題型、完整 single/multiple 與快照競爭明確由 02 同版自動回歸承擔。Latest 同 Question sortOrder 的人工異常數據不額外構造（正式 Definition sortOrder 唯一）；01～13 不增加原生故障／大容量／生產部署／Statistics 測試。細粒度原生 HTTP 重複組合暫緩，不能據此放寬正式契約。

**Prepare 静態檢查**：只核對兩檔編號／順序／40 requests／變量引用／JSON 與精確 Number token／無 GET Body／Cookie 身分／雙結果欄／SQL scope 與連結、`git diff --check`。本輪未 restore/build/start API，未發送 HTTP、未跑回歸／DB probe／清理操作；所有本次驗收結果仍 未執行（等待 Owner review）。以上為 `17b1c2f` Prepare 的歷史記錄；本次 Owner 已核准三項修訂後直接 AI Execute，另記實際結果，不改歷史 PASS。
