# R7 外部 review 有限確認

2026-10-04；RunId `r7-review-20261004-01`；起始 HEAD `e6b081f`、Git 乾淨。Owner 授權 M2／L3 有限確認、必要局部修正及無阻擋後準備驗收計畫。本報告是開發確認，不是正式驗收結果。未做 L2 重構、未新增 Capacity Policy、未開始 R8／G1。

## 結論

| 項目 | 確認與界線 |
| --- | --- |
| M2 | **未重現、根因未明**。本次失敗案例單獨重跑前後各 1/1 PASS；Windows 全套 167/167 PASS。未定位原因，撤回「既有偶發」推論，不宣稱問題根除或已排除 R7 因果。未修改 Factory、TimeProvider、CORS 測試或產品設定。 |
| L3 | 修正前缺少 Content-Type／`text/plain` 配合法 JSON，均 200 並更新 Visibility；偏離 03a §2.1。有效 Header 的空 Body／損壞 JSON 原已正確 400。只於 Visibility 加入媒體型別檢查：四種情境均 `400 VALIDATION_ERROR`，`details` 包含 `body / INVALID_VALUE`，正式 `error.code/message/details`，非 ProblemDetails／415，完整 DB 快照不變。 |
| M1 | 本次三個受控 R7→Delete 競爭及 Owner 刪除後存取通過，未發現 R7 狀態／Visibility 覆寫、復活或子列變更；不等於 Submit 安全。Delete 的鎖前讀取缺口確實存在，已記入 Implementation Plan R8 前置事項，沒有提前改 Delete。 |
| 驗收準備 | 無已重現的 R7 阻擋事項；M2 未定位及本次 Linux 未重跑明列為限制。依授權進入 Prepare，新增計畫優先含非字串 Visibility、Owner 已刪除 Form；仍需 Owner review／執行授權。 |

## M2 堆疊與變更核對

首次原始證據：`artifacts/r7-development/r7-development-20261004-01/windows-full.log`／`.trx`；155 案中 154 PASS、1 FAIL。失敗為 `ConfigurationTests.Noncanonical_Cors_origin_fails_startup`，預期 CORS 啟動驗證文字，實際收到 `ObjectDisposedException: IServiceProvider`。堆疊為 DI `GetRequiredService` → `DeferredHost.StartAsync` → `WebApplicationFactory.CreateHost / StartServer / CreateClient` → `ConfigurationTests.AssertStartupFails` line 163；沒有 R7 Controller／Validator／clock 呼叫。原失敗證據保留，不以後續成功覆蓋。

`git diff 0f1f70a 0496041 -- tests/CustomFormService.IntegrationTests/EngineeringEndpointsTests.cs` 顯示唯一 Factory 行為增量是可選 `TimeProvider?` constructor argument、instance 欄位與非 null 才 `AddSingleton`。ConfigurationTests 仍用兩參數 constructor，clock 為 null，新增 DI 分支不執行。Program 的 `AddSingleton(TimeProvider.System)` 未改；唯一注入呼叫是 R7 deadline helper，每個 test 用自己的 factory／clock，沒有 static clock 或全域時間更動。測試組使用 MethodLevel 平行、Windows 16 workers。

以上只縮小可見關聯，不能證明啟動／釋放時序無關。針對性與本次全套皆未重現，不作既有偶發或框架根因判定；未修改測試預期、不做 L2 重構。

## L3 與 M1 實測

L3 依 03a §2.1／4、03b §8.4、04 §2～4／Form Error 集合，無效傳輸屬 `400 VALIDATION_ERROR`，不是新增 415 contract。Header 接受大小寫不敏感的 `application/json`，允許 charset；缺少、損壞或其他 media type 於 UUID 後、資源查找前拒絕。Publish／Close 不讀 Body、不要求 JSON，原規則保持。局部修改 `FormsController.ChangeResultVisibility`，沒有跨端點 formatter／filter 改造。

修正前 `R7_review_visibility_transport_errors_use_contract_envelope_without_writes` 四案：2 PASS（空 Body、損壞 JSON）／2 FAIL（缺少／錯誤 Content-Type 實際 200）。修正後四案全 PASS；HTTP 由原生 Windows .NET TestServer 發送，使用測試自身隨機合成 User／Form，正式 response shape／status／detail 與全欄位 DB 不變均有斷言。另補數字 `1`、Boolean `false` 兩案及 Owner 對軟刪除 Form 的三端點 404／無寫入。

M1 的原 Delete 沒有顯式交易／共用 lock，但 EF 只標記 `deleted_at / updated_at`；資料庫 UPDATE 仍須取列鎖。R7 有交易，先鎖 `deleted_at IS NULL` 的列再讀 Aggregate，status／Visibility 各只寫自身欄位。三案由 interceptor 在 R7 Save 前持有 lock，發出 Delete，透過 PostgreSQL `pg_blocking_pids` 確認其實際等鎖後釋放：R7 200、Delete 204，完整快照只有預期 status 或 Visibility、updatedAt、deletedAt 改動；其後 Owner R7 操作 404，子列均不變。Delete 先完成亦以三個 Owner 已刪除存取案例確認 404。此證據支持本次 R7 無阻擋，不保證尚未實作的 Response 寫入安全，也不替代 R8 雙順序／rollback 測試。

## 本次命令與證據

證據目錄：`artifacts/r7-review/r7-review-20261004-01/`（gitignored、本機保留）。以下每次 `dotnet test` 均實際使用 `CustomFormService.sln --no-build --no-restore --configuration Debug -m:1`、`--logger "console;verbosity=normal"`、`--logger "trx;LogFileName=<下表檔名>.trx"`、`--results-directory artifacts/r7-review/r7-review-20261004-01`；同名 `.log` 保留完整輸出。

| 平台／命令或 filter | 檔名／實際結果 |
| --- | --- |
| Windows `--filter FullyQualifiedName~Noncanonical_Cors_origin_fails_startup`（改動前） | `windows-m2-focused`，1/1 PASS，1.0692 秒 |
| Windows build（新增測試、尚未修正） | `windows-build-before-fix.log`，0 warnings／errors |
| Windows `--filter FullyQualifiedName~R7_review_visibility_transport`（修正前） | `windows-l3-before-fix`，2/4 PASS、2 FAIL；原 200 response 保留 |
| Windows build（修正後） | `dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1 -p:UseSharedCompilation=false`，`windows-build.log`，0 warnings／errors、5.60 秒 |
| Windows `--filter FullyQualifiedName~R7_` | `windows-r7-focused`，49/49 PASS、13.5930 秒 |
| Windows 無 filter（全套） | `windows-full`，167/167 PASS、12.8561 秒，0 failed／notExecuted，exit 0；含原 CORS 案例 |
| Windows 原 CORS filter（修正後） | `windows-m2-after-fix`，1/1 PASS、0.8925 秒 |
| Windows API 恢復 | 核對完整 repo DLL command line，僅停止原 PID 36928；`dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --launch-profile http --no-build`，`windows-api-startup.log` 顯示 Development／localhost:5000；`curl.exe ... /health/ready` 實得 200 Healthy |
| Linux | 本次未重跑，前次 37/37／155/155 為歷史結果，不作本次修正的 PASS |

本次共新增 12 案，總案例 155 → 167；R7 37 → 49。不把同一案例的 focused／full 重複計成獨立驗證覆蓋，也不代填人工結果。

## 資料與交付

`docker compose ps` 原 PostgreSQL 17.11 healthy，未動 volume。只清理 integration tests 自己建立的隨機資料。`retained-before.json / retained-final.json` 與前次 R7 `retained-final.json` 完全相等：2 Users、6 Forms、14 Questions、9 Options，包含全欄位／子列 timestamps；使用者快照不含 hash／秘密。R4／R5／R6 retained fixtures 原樣保留。

未建立新秘密設定、未動 User Secrets／安全政策；Windows API 已恢復，本次沒有 Linux 暫存容器。提交前執行 `git diff --check`、檢查變更範圍與秘密掃描；提交 SHA／剩餘 Git 狀態由本次交付訊息記錄。成對驗收文件另作 Prepare 提交，各欄位維持未執行，待 Owner 審核。
