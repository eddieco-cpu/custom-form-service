# R7 AI 驗收執行報告

2026-10-04（Asia/Taipei）；**acceptance results pending owner confirmation**。Owner 在本 chat 核准計畫 `28cb8e2`、確認剩餘 review 事項不阻擋，授權三項局部修訂後直接執行。修訂提交 `858d6ae26717d8bcbaf66de46d48692dc9e66d52`；最新業務程式提交仍 `db2fe1b`，本次未修改業務程式／測試矩陣／正式規格。RunId：`r7-acceptance-20261004-01`。

成對紀錄：[R7_Acceptance_Test.md](../test-plan/R7_Acceptance_Test.md)、[R7_Manual_Tests.http](../test-plan/R7_Manual_Tests.http)。所有 AI HTTP 逐筆對應相同編號／順序／Body，人工全部「未執行」，沒有代填人工 PASS。R7 未 accepted，未開始 R8／G1。

## 結果

| 項目 | Windows AI 實際 | Linux | 人工 |
| --- | --- | --- | --- |
| 01 restore／build／原生啟動／baseline | PASS；0 warnings／errors；新 API PID 19340；health/readiness 200 | 未執行；不得代填 Windows | 未執行 |
| 02 全套首次執行 | **167/167 PASS**，0 failed／notExecuted，exit 0，13.9607 秒 | 未執行；Windows 成功，無需 fallback | 未執行 |
| 02 M2 targeted | **1/1 PASS**，exit 0，0.9031 秒；本次未重現、根因未明 | 未執行 | 未執行 |
| HTTP 01a～14c（含工程 health 與兩次 Login） | **42/42 PASS**；共 15 組，必要 DB 斷言均完成 | 未執行 | 全部未執行 |
| 14 真正並行 | PASS；兩個 HTTP DB waiter 確認後才 COMMIT；Publish 200／PATCH 409 FORM_NOT_EDITABLE／GET 200 | 未執行 | 未執行 |
| 15 復原／保留／秘密清理 | PASS；既有快照相等、新三張保留、raw／deadline 復原、barrier session 0、秘密掃描 0 hits | 未建立 SDK 容器 | 未執行 |

本次沒有產品 FAIL／BLOCKED。M2 的歷史首次失敗仍保留在 `artifacts/r7-development/r7-development-20261004-01/windows-full.*`，本次沒有改 Factory／TimeProvider／CORS 預期，也沒有用成功重跑覆蓋失敗。M2 結論只限「本次未重現、根因未明」，不推論為既有偶發或已根除。167 的 full 與 1 案 targeted 有重複，不合併宣稱 168 個獨立案例。

## 修訂與執行基準

1. repository 基準更新為 `28cb8e2`，最新 product code `db2fe1b`；修訂版 SHA `858d6ae` 另記。01 先核對／停止舊同 repo API PID 42776，再 restore／build，才啟動本次 Debug DLL、health/readiness；02 只用同一 build `--no-build`，沒有在 API 啟動後重建。
2. 14 barrier 由 controller 控制，取消固定 15 秒自動 COMMIT。等待兩個 HTTP DB waiter 的上限為 30 秒；確認後才能 COMMIT，若未確認則 ROLLBACK／回收 worker 並記 BLOCKED。實際沒有 timeout，也未重設 fixture 求另一勝方。
3. 02／12 覆蓋對照已列出現存 case 名稱，並在首次 full TRX 逐一核對。expired 是 `Missing_invalid_expired_and_unknown_user_tokens_are_distinguished` 的 `/users/me` 共用 Auth；Logout expired 只屬 Logout 例外，不宣稱三支 R7 expired 矩陣。R7 disabled／優先序對應 `R7_errors_follow_auth_parse_existence_owner_and_domain_order`。未新增測試或擴張矩陣。

## 實際命令／工具與證據

本機證據：`artifacts/r7-acceptance/r7-acceptance-20261004-01/`（gitignored，保留）；JSON UTF-8，.http 請求以 Python standard-library `http.client.HTTPConnection` 等價送至 Windows 原生 `localhost:5000`，逐筆保存 case ID、method/path、status、body、時刻與 Content-Type。03 密碼／Cookie 只在記憶體；不保存 request credential Body／完整 Set-Cookie。DB 透過 `docker compose exec -T postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -X -q -A -t -v ON_ERROR_STOP=1'`，SQL 走 stdin。執行 helper `execute.py`（preflight／health／http）僅在本次 ignored evidence 目錄，未修改產品／測試檔。

| 實際命令 | 證據／結果 |
| --- | --- |
| `git status --short`、`git log -6`／`git rev-parse HEAD` | 起始乾淨，原 `28cb8e2`；計畫修訂後 `858d6ae` |
| `dotnet --info`、`docker compose config --quiet`、`docker compose ps` | SDK 10.0.400、原 PostgreSQL 17.11 healthy；`windows-sdk.log`、`docker-status.log` |
| `dotnet restore CustomFormService.sln --disable-build-servers -m:1` | `windows-restore.log`，exit 0 |
| `dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1 -p:UseSharedCompilation=false` | `windows-build.log`，exit 0、0 warnings／errors、6.86 秒 |
| `dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --launch-profile http --no-build` | `windows-api-startup.log`，Development／localhost:5000；新 PID／完整 repo DLL 路徑 `windows-api-process.json`，DLL SHA256 `C44FD926474FE1DE2A3C2E9B379F874255E1EFDE45B5AFA2FE0B78D70C9EFEAC`，見 `windows-api-dll.json` |
| `dotnet test CustomFormService.sln --no-build --no-restore --configuration Debug -m:1 --logger "console;verbosity=normal" --logger "trx;LogFileName=windows-full-first.trx" --results-directory artifacts/r7-acceptance/r7-acceptance-20261004-01` | `windows-full-first.log/.trx/.exit`；167/167；09:23:31～09:23:45 +08:00，首次檔保留 |
| 同上加 `--filter FullyQualifiedName~Noncanonical_Cors_origin_fails_startup`、TRX 名 `windows-m2-targeted.trx` | `windows-m2-targeted.log/.trx/.exit`；1/1；09:23:46～09:23:47 +08:00 |
| Python `execute.py preflight`／`health`／`http` | `results.json`、42 份 `R7-XXx.json`、`http-execution-first.log`、各 DB JSON；所有 .http request 編號／預期不變 |
| 覆蓋／收尾唯讀 audit、`git diff --check`、秘密掃描 | `regression-coverage.json`、`fixture-final-audit.json`、`retained-final.json`、`security-scan.json`；實際覆蓋名稱與 source／TRX 一致 |

首次 helper／CLI 錯誤也保留：RunId 唯讀 SQL 初次因 shell 引號語法錯誤未執行 SQL，改用 stdin psql；見 `preflight-cli-first-error.log`。TRX 後處理首次讀 UTF-8 JSON 時用到 Windows 預設 cp950，修正顯式 UTF-8；見 `results-decoding-first-error.log`。這兩項沒有改資料／產品，也沒有重送已執行 HTTP 或重跑全套／M2；不能當成產品 FAIL，亦未隱藏原錯誤紀錄。

## HTTP／DB 主要證據

- 05 不完整五題型 Draft 保存合法；Publish 400 FORM_NOT_PUBLISHABLE，包含 Choice options／Rating min／max 定位；Close Draft 409，完整 DB 不變。
- 06／09／11 在 Draft／Published／Closed 變更 Visibility 200，只改 Form policy／updatedAt；相同值 200，完整 rows／timestamps 不變。08 非字串與未知欄位、缺少／錯誤 Content-Type、空 Body、損壞 JSON 皆 400 正式 Envelope／定位，DB 不變；12 非 Owner／Auth／UUID／不存在依正式次序；13 Owner 三端點對軟刪除 Form 全 404，子列保留。
- 07 `canonical-main.json` 保存補完整後基準；在本次 main 暫設 raw 文字／placeholder／Option label/value，再以實際 DB snapshot 作 Publish 前比較。`R7-07b.json`／`-db.json` 顯示 raw Definition 原樣、只有 status／updatedAt 改變，沒有 Candidate 回寫。finally 還原 raw 欄位；最終 Questions／Options 全欄位與 canonical 相同（含全部子 timestamps）。證據另有獨立 audit 依 canonical＋已核准 probe 欄位重建預期，沒有冒稱這是另一次 before 讀取。
- 10 `deadline-main-before.json` 保存原期限；10a DB snapshot 證明過去 deadline 仍 Published；10b 200 Closed、只改 status／updatedAt，不重新驗證截止時間，原期限 finally 還原。重複 Close／Closed Publish／Definition PATCH 回正式 409。
- 14 `R7-14-waiters.json`：barrier PID 55169；09:26:04.241922 +08:00，waiter 54349（transactionid，blocked by 55169）與 55227（tuple，blocked by 54349）。確認後 09:26:04.244928 才 COMMIT（`R7-14-release.json`）。結果 `14a=409 FORM_NOT_EDITABLE`／`14b=200`；`R7-14-before.json`／`-after.json` 只變 Form status／updatedAt；14c＝Publish response，Rating 0..5、Definition 全凍結。`barrier-session.json` exit 0，最終同名 session 數為 0。

細節與逐項結果以兩份成對文件為準；回歸 PASS 不代替上述實際 HTTP／DB 執行，也不代替人工結果。

## 保留／復原與限制

R4／R5／R6 的公開會員、全部既有 Form／Question／Option 快照 `retained-before.json`＝`retained-final.json`：2 Users、6 Forms、14 Questions、9 Options，沒有改 Profile／status、hard-delete、TRUNCATE 或清開發 volume。新三張全部保留，總量 2 Users、9 Forms、22 Questions、12 Options：

| 本次 fixture | Server ID | 實際保留狀態 |
| --- | --- | --- |
| main | `fed0ffda-9e30-4b09-83ae-d2a04dc23c68` | Closed／owner_only、5 Questions／2 Options、canonical 定義／未來 deadline 已復原 |
| deleted | `cc381383-82ff-4fc6-96f2-681118879761` | Draft、Soft Deleted、1 Question 保留 |
| concurrent | `c44ebb5b-8e27-409e-8c78-81e05de90bb1` | Published／owner_only、2 Questions／1 Option、Rating 0..5 |

沒有新增秘密暫存檔／Cookie jar；password／Token 只在 helper memory，結束時清空／process 退出；.http 的密碼與 JWT placeholders 保持原樣，只填非秘密 Server IDs。掃描本次文件／可讀 evidence 對照本機 signing key、DB password、合成登入密碼及 JWT 模式，0 hits；不掃掉／刪除舊既有資料。API PID 19340／原 PostgreSQL／volume 保留，沒有 Linux 暫存容器；`docker-status-final.log` healthy。

未驗證：人工全部未執行，Linux fallback 未執行，R7 專屬 expired token 矩陣沒有新增；M2 根因仍未明。Owner 確認前不標 accepted。本次文件／結果提交 SHA、最終 Git 狀態由交付訊息列出。

**驗收結果待 Owner 確認**
