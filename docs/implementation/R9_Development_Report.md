# R9 — My Latest／Respondent Count 開發報告

2026-10-05；**development verified（開發完成、待驗收）**。Owner 核准精簡計畫 `14442b2` 並明確授權開發；並行測試以實際 SELECT 建立快照為控制點。開發自測不代表正式驗收通過；未準備／執行 R9 驗收，未開始 R10／G1。

## 完成範圍與主要檔案

- `src/CustomFormService.Api/Controllers/ResponsesController.cs`：新增 GET `/api/v1/forms/{formId}/responses/me/latest`。依 Current User 選取 `submitted_at DESC, id DESC` 的整份 Response，DB 排序／LIMIT 1；只讀該份 Answers，依 Question sortOrder／ID 排序，沿用既有 Receipt，不補舊答／null，不重新轉換保存的 JSONB 數值。
- `src/CustomFormService.Api/Controllers/FormsController.cs`、新增 `Contracts/Responses/RespondentCountResponse.cs`：新增 Owner-only GET `/api/v1/forms/{formId}/respondent-count`。在 PostgreSQL 對成功 Response 的 User ID 去重；multiple 歷史、Owner 自己、零 Answer 與後來 disabled 的填答者符合相同 Count 語意。
- 兩端點沿用 `[Authorize]`／CurrentUserContext／Error Envelope／直接 DbContext 架構。Authentication → UUID → 未刪除 Form → 端點分支 → 查詢；Draft Latest Owner／非 Owner 的兩種 404、Count 非 Owner 403、Deleted 優先及空資料正確。Deadline／Visibility／Identity 不影響讀取。所有業務資料讀取 NoTracking，同一短暫 `RepeatableRead` 交易，不取 Form row lock、不寫入；首個 Form SELECT 建立快照。
- 新增 `tests/CustomFormService.IntegrationTests/FormEndpointsTests.ResponseQueries.cs`：26 個 R9 案例，重用既有 TestFactory／Clock／PostgreSQL／資料清理 helper。涵蓋全題型 Receipt 保存值／排序與欄位、single／multiple、partial／全 optional 缺答、timestamp／固定 UUID tie-break、User／Form 隔離、Count 去重／Form List 一致、狀態／空資料／政策、Auth／UUID／Deleted 錯誤優先序、GET 無寫入及四組快照競爭。
- `R9_Implementation_Plan.md`／本報告／`V1_Backend_Implementation_Plan.md`：記錄核准、開發驗證與停止點。R8 accepted 同步單獨提交 `0f44286`，只更新 Owner 確認；既有正式驗收檔案、報告與 FAIL／BLOCKED／PASS 證據未改。

規格對照：01 SUB-06～07／STA-01；02 §10～11；03a §5～6；03b §3.6／6.2；03c §3.1／3.3／5～6；04 §4／5／7／9；05 §13～15。無已知契約偏離，Entity／Mapping／Migration／Schema 未變，沒有 R10 統計框架或額外 API。

## 實際開發驗證

| 驗證 | 實際結果 |
| --- | --- |
| Windows `dotnet restore CustomFormService.sln`（預設 sandbox） | exit 1、無 stdout；沒有據此宣稱還原成功。 |
| Windows 授權 SDK／cache 存取後 restore | exit 0、全部 up-to-date。 |
| Windows 首次 build | exit 1；既有 repo API PID 34336 鎖住輸出 DLL，MSB3026 重試 10 次／MSB3027、MSB3021。不是產品編譯錯誤。 |
| Windows 確認 command line 指向本 repo DLL、暫停該 API 後 build | exit 0，0 warnings／0 errors，8.66 秒；[build log](../../artifacts/r9-development/windows-build.log)。 |
| Windows focused tests | exit 1；MSTest 無法載入新 DLL，application control `0x800711C7`。未探索／執行案例，不計為測試 PASS。保留 [log](../../artifacts/r9-development/windows-focused-first.log)／[TRX](../../artifacts/r9-development/r9-focused-first.trx)。 |
| Linux 同份 source restore／build | exit 0、0 warnings／0 errors；build 19.68 秒。[log](../../artifacts/r9-development/linux-build-first.log)。 |
| Linux R9 focused 首次 | **26/26 PASS**，0 failed／skipped，5.7280 秒；[log](../../artifacts/r9-development/linux-focused-first.log)／[TRX](../../artifacts/r9-development/linux-r9-focused-first.trx)。 |
| Linux 同版 full 首次 | **306/306 PASS**，0 failed／skipped，22.1587 秒；含原 280 與 R9 26。[log](../../artifacts/r9-development/linux-full-first.log)／[TRX](../../artifacts/r9-development/linux-full-first.trx)。 |
| 保留資料 | focused／full 後六表非秘密完整快照與測前逐字相同：4 users／19 forms／42 questions／18 options／17 responses／20 answers；[比較摘要](../../artifacts/r9-development/verification-summary.json)。 |
| Diff 自查 | `git diff --check`／提交前 staged diff 檢查通過；只含 R9 程式、測試、開發紀錄與授權的 R8 進度同步。 |

實跑主要命令：

```powershell
dotnet restore CustomFormService.sln
dotnet restore CustomFormService.sln --disable-parallel --verbosity minimal
dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test CustomFormService.sln --no-build --no-restore --configuration Debug -m:1 --filter 'FullyQualifiedName~R9_' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=r9-focused-first.trx' --results-directory artifacts/r9-development
docker exec r9-development-sdk bash /evidence/linux-prepare.sh
```

Linux prepare 將唯讀 source 的 `src`／`tests`／solution／global.json（排除 bin／obj）複製到 `/work/r9`；使用既有 SDK `10.0.400` image，NuGet cache 與既有 User Secrets 唯讀，連到原 PostgreSQL network。未改秘密設定或 regular volume。Linux 原樣执行：

```text
dotnet restore CustomFormService.sln --disable-parallel --ignore-failed-sources
dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test CustomFormService.sln --no-build --no-restore --configuration Debug -m:1 --filter "FullyQualifiedName~R9_" --logger "console;verbosity=normal" --logger "trx;LogFileName=linux-r9-focused-first.trx" --results-directory /evidence
dotnet test CustomFormService.sln --no-build --no-restore --configuration Debug -m:1 --logger "console;verbosity=normal" --logger "trx;LogFileName=linux-full-first.trx" --results-directory /evidence
```

兩個 Linux test 命令使用 `docker exec r9-development-sdk bash -lc 'cd /work/r9 && ...'` 執行；stdout／stderr 經 Tee-Object 留存，實際 exit 0。沒有把 Linux PASS 改填為 Windows PASS，Windows 首次失败也未覆寫。

## 快照控制與實測資料

`R9SnapshotGate.ReaderExecutedAsync` 只攔截目標 Form 的實際 SELECT，且確認命令交易為 `RepeatableRead`。Npgsql 已执行 SELECT 並返回 Reader，才設定 Reached／暫停讀取；沒有用 TransactionStarted／BeginTransaction 作為快照證據，也不用固定 sleep。

四組 Latest／Count × Submit／Delete：先提交一份零 Answer Response；暫停在已完成的 Form SELECT 後，另一請求的 Submit／Delete 必須成功返回（已 commit），此時 GET 仍暫停。釋放後 Latest 仍為原 Response、Count 仍為 1；下一個 GET 看見新增完整回覆／Count=2，或 Deleted 404。四組在 focused 與 full 均 PASS，TRX StdOut 保留每組證據。finally 釋放 gate、取消並有限等待讀取 worker，測試清理只刪自身隨機會員與資料。

Effective Response 實測：完整五題舊 Response → 較新僅一題／零題，各回一份新 Receipt、不補舊 Answer；同 submitted_at 的較早高 UUID `ffffffff-0000-0000-0000-000000000009` 勝過較晚低 UUID，低 UUID 的 submitted_at 增加一微秒後改由低 UUID 勝出。固定 ID 只用在本次零 Answer 測資，已清理。Count 實測：4 份零 Answer Response 屬 3 個 User（含 Owner、重複提交及後來 disabled），Count 與 Form List 均 3。

## 環境限制、提交與停止點

Windows 原生 API 恢復啟動兩次均受同一 application-control `0x800711C7` 封鎖，程序已退出，**原本 localhost:5000 開發 API 目前未能恢復**。首次／重試 stderr 保留於 [首次啟動](../../artifacts/r9-development/api-stderr.log)／[恢復重試](../../artifacts/r9-development/api-restore-retry-stderr.log)。沒有更改或繞過 Windows 政策；本次原生 API health/readiness 未成功驗證，Windows focused 只完成探索嘗試、未執行案例；Windows full 未執行。Linux 整合套件已驗證同份程式與真實 DB；原生環境恢復是後續驗收前需處理的環境事項。

PostgreSQL、既有 volume／fixtures 保留；只清測試自身資料，未新增保留的正式驗收 fixtures。暫存 Linux SDK 容器在交付前 stop／rm，無背景測試 worker；啟動時暫設的 ASPNETCORE environment／URL 已在 finally 還原，沒有寫入秘密或改動 User Secrets。`artifacts/r9-development` 為忽略的本機證據，不含 credentials／password hashes，未加入 Git。

R8 進度同步提交 `0f44286`；R9 提交為 `feat(responses): add latest receipt and respondent count`（SHA 隨最終交付回報，避免文件自指）。交付後 Git 乾淨。正式 R9 驗收計畫尚未授權準備，AI／人工驗收均未執行；後續需另行指定驗收準備並遵守 Owner approval gate。停止於「開發完成、待驗收」，R10 未開始。
