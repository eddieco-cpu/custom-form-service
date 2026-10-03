# R7 開發報告 — Lifecycle 與 Result Visibility

2026-10-04（Asia/Taipei）；**development verified，開發完成、待驗收**。Owner 在本 chat 核准計畫提交 `0f1f70a` 並授權開發，補充 Publish／Close 不要求 Body／JSON，以及 Publish 正規化只操作獨立 Candidate、成功後 Definition／子列保持原樣。本次只完成端點 12～14，未標 R7 accepted、未進入 R8／G1，未準備或執行正式驗收。

程式提交：`0496041c762062f310bf61662276d44c8ea83e09`（`feat(forms): implement R7 lifecycle and result visibility`）；起始 HEAD `0f1f70a`，起始 Git 乾淨。報告／進度文件另作單一用途提交，最終 SHA 與 Git 狀態由交付訊息提供。

## 完成內容與契約

| 項目 | 實作與可觀察結果 |
| --- | --- |
| Publish | `POST /api/v1/forms/{formId}/publish`；無 Body 綁定，不要求 JSON。Active Owner 的 Draft 通過完整性檢查才轉 Published，回 200 FormDetail；不完整回 `400 FORM_NOT_PUBLISHABLE` 與正式 details。Published 重試 `409 FORM_ALREADY_PUBLISHED`；Closed 回 `409 FORM_INVALID_STATE_TRANSITION`。 |
| Close | `POST /api/v1/forms/{formId}/close`；同樣不要求 Body／JSON。Published → Closed，回 200 FormDetail；Draft 回 `409 FORM_INVALID_STATE_TRANSITION`，Closed 重試 `409 FORM_ALREADY_CLOSED`。不重驗凍結 Definition、不 Reopen，Deadline 到期不自動 Close。 |
| Result Visibility | `PATCH /api/v1/forms/{formId}/result-visibility`；明確解析 JSON Body，缺少／無法解析回 `400 VALIDATION_ERROR`；資源及 Owner 通過後才驗證唯一必填、非 null／非重複、合法 Enum 欄位。Draft／Published／Closed 均可改 `owner_only`／`respondents`／`public`。同值 no-op 不更新任何 timestamps；改值只更新 Form resultVisibility／updatedAt。 |
| 次序與 Freeze | Authentication → UUID（Visibility 另解析 JSON）→ 未刪除資源 → Owner → Lifecycle／內容驗證 → 原子寫入。Auth Codes、400、404、403、409 依正式順序；非 Owner 優先於狀態／Definition 錯誤。Published／Closed 的既有 Definition PATCH（含空 PATCH）保持 `409 FORM_NOT_EDITABLE`。 |

Publish 從鎖後、限定該 Form 的完整 Aggregate 建立 `FormDefinitionCandidate.From(form)`；共用 `TryNormalizeQuestions` 的文字／Config／精確數值／排序規則，再檢查 Form title／Enum、至少一題、合法題型、Choice 至少一個有效 Option、Rating 完整整數 min／max，以及 Deadline。Question／Option 歸屬由 scoped Include、既有 Candidate 構造與 DB FK 保證，Publish 不接受外部子 ID。Details 依 sortOrder／id 建立穩定 `questions[i]...` 定位；不把缺少必要設定補成成功，`required=false` 仍須達發布完整度。

共用正規化只修改獨立 Candidate 的欄位／Config／Source；沒有將其複製回 EF Aggregate，也不調用 R6 ChangeSet／Writer。成功只賦值 Form status／updatedAt。DB 測試刻意在測試自建 Aggregate 放入可被 trim 的 Form／Question／Option 文字及 Config placeholder，並保留五題型與 `1e30`／`1e31` Number Config；完整全欄位快照與實際 UPDATE SQL 證明 Definition、Questions／Options、ID、createdAt／子列 updatedAt 均原樣保留。

所有 R7 寫入各有 `ReadCommitted` transaction，與 PATCH 共用 `FormAggregateLock.TryAcquireActiveFormAsync` 的 `FOR UPDATE`，鎖後才讀 Aggregate／目前狀態，鎖持有至 commit／rollback。PATCH 先完成，Publish 讀新定義重驗；Publish 先完成，舊 PATCH 等待後讀到 Published 並拒絕。Publish Server Time 在鎖後取得一次，依 DB 實際 deadline 判定嚴格晚於該時間；不沿用等鎖前的時間。保存後同交易內 AsNoTracking 重讀 FormDetail，commit 成功才回 200。

Visibility 不套 Draft 限制或 Publish Validator，不改 Definition／responseIdentity，也不提前開放 Statistics。Close 的同一鎖機制供 R8 Submit 沿用，本次沒有實作 Submit 或其競爭流程。

## 修改檔案與重構界線

- [FormsController.cs](../../src/CustomFormService.Api/Controllers/FormsController.cs)：三端點、局部共用 Lifecycle／commit 回應 helper、Visibility JSON 解析與既有欄位解析器；原 Create／PATCH／Delete 行為不改。
- [FormDefinitionValidator.cs](../../src/CustomFormService.Api/Validation/FormDefinitionValidator.cs)：獨立 Publish 入口，沿用 Draft 正規化；原 Draft 入口不收緊。
- [FormAggregateLock.cs](../../src/CustomFormService.Api/Infrastructure/Persistence/FormAggregateLock.cs)：僅更新用途註解，SQL／鎖機制不改。
- [FormEndpointsTests.Lifecycle.cs](../../tests/CustomFormService.IntegrationTests/FormEndpointsTests.Lifecycle.cs)：37 個必要開發案例，含快照、SQL、錯誤次序、時間、rollback 與並行。
- [EngineeringEndpointsTests.cs](../../tests/CustomFormService.IntegrationTests/EngineeringEndpointsTests.cs)：TestFactory 增加可選 TimeProvider 注入，以確定性測 deadline 等於／前後及鎖等待；預設仍為原服務。
- [R7 實作計畫](R7_Implementation_Plan.md)／[Implementation Plan](V1_Backend_Implementation_Plan.md)：納入授權補充與開發完成狀態。本報告記錄實際結果。

只有必要局部 helper，未架構重構、未改 Candidate／R6 Writer、未改 Schema／Migration／正式規格，未提前實作 R8～R11／G1。

## 實跑命令與平台結果

證據目錄：[artifacts/r7-development/r7-development-20261004-01/](../../artifacts/r7-development/r7-development-20261004-01/)（gitignored，本機保留）。兩平台同套 source／case，不合併計數，不用舊 R6 結果代填。

| 平台／檢查 | 實際結果 |
| --- | --- |
| SDK／DB | Windows .NET SDK 10.0.400；既有 `docker compose ps` 顯示 PostgreSQL 17.11 healthy，原 named volume 保留。Linux 使用既有官方 `mcr.microsoft.com/dotnet/sdk:10.0.400`。 |
| Windows restore | `dotnet restore CustomFormService.sln --disable-build-servers -m:1`，exit 0。 |
| Windows build | `dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1 -p:UseSharedCompilation=false`；最終 exit 0，0 warnings／0 errors，10.51 秒，見 `windows-build-final.log`。 |
| Windows R7 | `dotnet test CustomFormService.sln --no-build --no-restore --configuration Debug -m:1 --filter "FullyQualifiedName~R7_" --logger "console;verbosity=normal" --logger "trx;LogFileName=windows-r7-focused-final.trx" --results-directory artifacts/r7-development/r7-development-20261004-01`；37/37 PASS，0 failed／notExecuted，exit 0，18.5354 秒。 |
| Windows full | 同上移除 `--filter`，最終 `LogFileName=windows-full-recheck.trx`；155/155 PASS，0 failed／notExecuted，exit 0，15.5366 秒，2026-10-04 01:47:38～01:47:54。首次失敗及重查見下方。 |
| Windows native API | 為重建停止已核對完整 repo DLL 路徑的 R6 舊 API PID 12064；最終以 `dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --launch-profile http --no-build` 重新啟動成功，Development／localhost:5000，保留執行中。未以此補填正式驗收。 |
| Linux restore／build | `docker exec r7-development-sdk-20261004-01 bash -lc <prepare/verify script>`；script 在 `/work/r7` 執行相同 restore／build flags，最終 exit 0、0 warnings／0 errors，見 `linux-verification.log`。 |
| Linux R7／full | 相同 `dotnet test` flags，focused filter `FullyQualifiedName~R7_`／full 無 filter；results-directory `/work/evidence`，TRX `linux-r7-focused.trx`／`linux-full.trx`。最終 37/37 與 155/155 PASS，0 failed／notExecuted，整段 exit 0；full 8.1539 秒。TRX 已匯出證據目錄。 |
| 文件／安全 | `git diff --check`、提交差異範圍檢查通過；最終 34 個變更／證據檔掃描未命中本機 signing key、DB password 或 JWT。未新增可用秘密暫存設定。 |

37 案涵蓋主要成功／不完整 Draft、五題型完整性與代表性 details、權限／刪除／狀態次序、Visibility 三狀態／非法欄位／no-op、缺少 Body／不要求 JSON 的端點區分、Publish deadline 前／等於／後及等待鎖後時間、三寫入各自 Save／commit 故障回滾。七個並行案例以 Save interceptor 持有第一筆交易的 Form lock，確認 PostgreSQL `pg_blocking_pids` 有第二筆 waiter 後才釋放；涵蓋 PATCH 先使 Rating 不完整、Publish 先凍結、重複 Publish／Close、Visibility 與生命週期互相競爭。沒有用固定 sleep 或連續請求取代並行。

## 初次失敗、處理及保留證據

- 第一次 Windows build 因 R6 留存 API 占用 DLL，10 warnings／2 errors；核對完整 DLL 路徑後只停止 PID 12064，後續 build 通過，最後恢復本專案 API 啟動。
- 第一次 Windows focused 因應用程式控制原則 `0x800711C7` 封鎖新版 API DLL，未執行案例；未更改／停用安全設定，先用官方 Linux SDK 完成測試。最終修正後的 Windows DLL 已實際跑完 37 與 155 案，不把 Linux 結果冒稱 Windows PASS。
- 初次 Linux R7 為 29 PASS／8 FAIL。三案是 helper 把缺少 Cookie 寫成空 header，四案是 SQL helper 未辨識 UPDATE 與 WHERE 間換行，均修正 helper、未放寬斷言；另一步缺少 Visibility Body 被 MVC formatter 回 415，改由此端點明確解析 JSON，空／無效 JSON 穩定回正式 `400 VALIDATION_ERROR`。原 log／TRX 保留為 `linux-r7-focused-first.*`，修正後 focused／full 全通過。
- 首次 Windows full 154/155，既有 `Noncanonical_Cors_origin_fails_startup` 收到 TestFactory 的 `ObjectDisposedException (IServiceProvider)`，未取得預期 CORS 文字；原 `windows-full.trx`／log 保留。該案例原碼、預期與產品設定均未改：單獨重查 1/1 PASS（`windows-cors-recheck.trx`），再相同全套 155/155 PASS（`windows-full-recheck.trx`）。此偶發 TestFactory 啟動例外的根因未在 R7 修正；不因一次重查成功宣稱框架問題已根除。

## 資料、安全與後續停止點

僅測試自身隨機合成 User／Form 由既有 Cleanup 清理。R4／R5／R6 保留快照 `retained-before.json`／`retained-final.json` 完全相同：2 Users、6 Forms（含 R5 Soft Deleted 與三張 R6）、14 Questions、9 Options；含所有子列 timestamps。沒有 hard-delete 保留資料、TRUNCATE、rebuild 或開發 volume cleanup。

Linux SDK 的 source、User Secrets、NuGet 來源唯讀，建置／清理僅在容器 `/work/r7` 暫存副本；證據匯出後已移除本次唯一 `r7-development-sdk-20261004-01`，PostgreSQL healthy、volume 未動。未寫入新秘密、未變更 User Secrets／安全設定，API 最終以新版程式保留供後續使用。

R7 必要開發驗證已完成；正式成對驗收計畫尚未準備／執行，人工驗收未執行，未標 accepted。後續另依 acceptance skill 提出計畫供 Owner review，核准且明確授權後才執行。Submit／Close／Delete 真正競爭留 R8，Latest／Count／Statistics 與 G1 留各自 Round；本次交付後停止。

**開發完成、待驗收**
