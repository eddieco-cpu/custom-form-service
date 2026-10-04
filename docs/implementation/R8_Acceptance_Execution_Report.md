# R8 AI 驗收執行報告

2026-10-04；**R8 驗收結果待 Owner review**。Owner 已核准成對計畫 `8680cccd17e98646a6f257757dfa65654c039153` 並明確授權 Execute。本次 RunId `r8-acceptance-20261004-01`，Windows 原生 .NET SDK 10.0.400／runtime 10.0.11，PostgreSQL 17.11。未改產品、正式規格、預期結果或 timeout；未 accepted，未開始 R9／R10／G1。

成對實際紀錄：[R8_Acceptance_Test.md](../test-plan/R8_Acceptance_Test.md)／[R8_Manual_Tests.http](../test-plan/R8_Manual_Tests.http)。人工全部未執行；Owner 核准 AI 執行不代替人工結果。

## 結果與阻礙

| 項目 | 實際結果 |
| --- | --- |
| Restore／build | PASS；兩命令 exit 0，build 0 warnings／0 errors，16.50 秒 |
| 本次 Windows native API | 已啟動；Development／localhost:5000；API PID 28640、launcher 35112 |
| 01a health | **PASS**；首次 `200`、`{"status":"healthy"}` |
| 01b readiness | **FAIL**；首次 `503`、`Unhealthy`，原預期仍為 200／Healthy；沒有重送覆盖首次失敗 |
| HTTP 整體 54 requests | **1 PASS／1 FAIL／52 BLOCKED**，實際只送 01a／01b；03a～13h 未送，含 05b、05e、06a/b/c |
| 非 HTTP 01／02／13／14 | 01 FAIL；02／13／14 BLOCKED。01 的 restore/build/startup 已成功；readiness gate 未通過；14 的唯讀資料保留與秘密清理核對另記下方，完整五 fixture 結果仍無法驗證 |
| 完整回歸／Clock／故障注入／EF Receipt | **BLOCKED，未執行**。不得以過去 277 或收尾 34 代填當前 280；沒有本次 full TRX |
| 原生並行 | **BLOCKED，未執行**；沒有 barrier／workers／waiter 證據，不宣稱並行 PASS |

01b 於 `2026-10-04T12:09:37.551893Z` 開始、`12:09:42.332371Z` 結束（臺北 20:09:37～20:09:42）；HTTP elapsed 4.782 秒。API log 同時記錄 postgresql health check Unhealthy。Docker 容器內部 healthy，Compose 宣告 `127.0.0.1:5432 → 5432`，但實際 NetworkSettings.Ports 是 `{"5432/tcp":[]}`；主機 localhost:5432 沒有 listener。唯讀確認 API 本機設定／User Secrets／環境覆寫後的 Host=localhost、Port=5432；沒有輸出 secret。這是本次 readiness 前置的環境連線阻礙，尚無 R8 Submit 產品缺陷的驗證證據。

依核准計畫「前置失敗使依賴案例 BLOCKED」與「health/readiness 後才開始 02」，本次沒有跳過 gate、啟動 Linux fallback、重跑回歸、建立替代 fixtures 或修改預期取得 PASS。Windows DLL 已可正常載入，因此未使用只適用載入封鎖的 fallback。

首次證據：[01b 原始結果](../../artifacts/r8-acceptance/r8-acceptance-20261004-01/01b.json)、[health 首次輸出／AssertionError](../../artifacts/r8-acceptance/r8-acceptance-20261004-01/health-first.log)、[API startup log](../../artifacts/r8-acceptance/r8-acceptance-20261004-01/windows-api-startup.log)、[actual Docker binding](../../artifacts/r8-acceptance/r8-acceptance-20261004-01/docker-actual-bindings.json)、[非秘密 endpoint 診斷](../../artifacts/r8-acceptance/r8-acceptance-20261004-01/nonsecret-endpoint-diagnosis.json)。不覆寫為成功，不把 helper 對 503 的斷言當成產品崩潰。

## 實跑命令、工具與版本識別

依序執行（repo cwd；stdout／stderr 以 Tee-Object 或 Start-Process redirect 保存於本次 ignored evidence）：

```powershell
git status --short
git rev-parse HEAD
dotnet --info
docker compose config --quiet
docker compose ps
dotnet restore CustomFormService.sln --disable-parallel --verbosity minimal
dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --launch-profile http --no-build
& 'C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' artifacts/r8-acceptance/r8-acceptance-20261004-01/execute.py health
docker inspect custom-form-service-postgres-1 --format '{{json .HostConfig.PortBindings}}'
docker inspect custom-form-service-postgres-1 --format '{{json .NetworkSettings.Ports}}'
```

API 啟動命令使用 `Start-Process -WindowStyle Hidden`，在本次 restore/build 完成後啟動，非舊 process。API 的實際 command line 為 `dotnet exec <此 repo>/src/CustomFormService.Api/bin/Debug/net10.0/CustomFormService.Api.dll`。DLL SHA256：`ECD9C0C990526DA8486CB2CCBBBDF28E9480E03C17D03773F05D995316CFE80B`。證據：[build](../../artifacts/r8-acceptance/r8-acceptance-20261004-01/windows-build-first.log)、[build/PID/time](../../artifacts/r8-acceptance/r8-acceptance-20261004-01/api-build-identity.json)、[真實 process](../../artifacts/r8-acceptance/r8-acceptance-20261004-01/windows-api-process.json)。

Python standard-library helper 直接讀取核准 `.http`，保留原 Method／URL／headers／Body token，不重新序列化 request；只送 01a／01b。原始 response body、status、Content-Type、UTC、耗時及 case ID 已記錄；未產生 JWT／Cookie／password 暫存。Python 使用 bundled runtime；WindowsApps 的 python alias 沒有可用 version 輸出，不作為執行 runtime。首輪普通 sandbox 的 process 查詢拒絕存取，已改以授權的唯讀存取取得 PID／路徑；沒有修改安全政策。

DB 唯讀透過 helper `all_rows()` 載入 `artifacts/r8-a-review/fixture-snapshot.sql`，Python `-c` 調用兩次，實際子命令為：

```text
docker compose exec -T postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -X -q -A -t -v ON_ERROR_STOP=1'
```

SQL 走 stdin；兩次 snapshot 及 `SELECT COUNT(*) FROM forms WHERE title LIKE '%r8-acceptance-20261004-01%';` 都是唯讀，不屬 DB probe。UUID／credentials placeholders 未替換為可用秘密。沒有執行計畫 02 的 dotnet test 命令；沒有任何產品 DB UPDATE／INSERT／DELETE、Publish／Close／Soft Delete。

## 資料保留、清理與未完成工作

readiness 失敗後的唯讀 snapshot 與最終 snapshot 完整相等：**4 users／9 forms／22 questions／12 options／0 responses／0 answers**。Users 不含 password hash；比較所有原列、IDs／Definition／timestamps／歷史，不只是 counts。這兩次 snapshot 的比較範圍從失敗後唯讀診斷開始，不能宣稱完成原計畫 01 的成功路徑 baseline 或全部 14 驗證。證據：[首次唯讀 snapshot](../../artifacts/r8-acceptance/r8-acceptance-20261004-01/retained-after-readiness-failure.json)、[最終 snapshot](../../artifacts/r8-acceptance/r8-acceptance-20261004-01/retained-final.json)、[清理／保留摘要](../../artifacts/r8-acceptance/r8-acceptance-20261004-01/retention-cleanup-summary.json)。

本次 RunId Forms=0；五張規劃 fixture 尚未建立，沒有可保留的新 submission。Deadline probe／barrier／workers 都未開始，不需復原或取消；没有可用 credential／JWT／Cookie jar／秘密 request 暫存。原始 paired 文件仍保留 placeholders；非秘密 logs／JSON／helper 保留於 `artifacts/r8-acceptance/r8-acceptance-20261004-01/`。既有 API／DB／volume 保留；没有 restart／recreate 容器、down -v、Schema／migration／硬刪除資料。

文件／evidence 的 known-secret 值、JWT／private-key pattern 掃描完成，0 hits，沒有輸出 secret。首輪 ReadAllText 因仍在寫入的 API log 發生兩項 sharing conflict，該次不能宣稱完整通過，保留 `secret-scan-first-incomplete.json`；改以 FileShare.ReadWrite 唯讀取得日志後全部觀察成功、0 read failures，見 [secret scan](../../artifacts/r8-acceptance/r8-acceptance-20261004-01/secret-scan.json)。文件自查確認 54 requests／預期／人工欄未變、58 個 AI 欄與 manifest 一致，見 [配對核對](../../artifacts/r8-acceptance/r8-acceptance-20261004-01/documents-verification.json)；不是額外產品測試。

未完成：01b readiness、02 同版 full／Clock／故障與 EF 讀回，以及 03～13 所有業務 HTTP／DB／並行驗證、14 五 fixture 最終結果。需要先恢復既有 PostgreSQL localhost:5432 host binding，保持同一 regular development volume 與既有資料；本次只提出環境恢復方向，沒有執行容器修復或產品修正。環境恢復後的補驗需保留這次首次 FAIL／BLOCKED 記錄，依 Owner 下一步授權記另次結果；不要重設舊 fixture 或把本次結果改填 PASS。

## 文件與提交

實際 AI 結果已逐案回填兩份核准文件（預期／requests 不變），Implementation Plan 同步為「R8 驗收結果待 Owner review」。人工欄仍全部未執行；開發 PASS 保留為歷史。執行結果以單一目的 commit 記錄；SHA、完整 diff 與剩餘 Git 狀態見交付訊息。沒有產品／測試／正式規格變更，沒有自行 accepted 或開始 R9。

停止於 **「R8 驗收結果待 Owner review」**。
