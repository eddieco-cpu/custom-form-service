# R8 連線修復後重驗報告

2026-10-05；**本次 AI 驗收全部 PASS，驗收結果待 Owner review**。未標記 R8 accepted，未開始 R9／R10／G1；未修改產品、正式規格、測試語意、核准請求或預期。

## 版本、授權與結果

Owner 表示連線修復並明確要求再次驗收。沿用核准成對計畫 **`8680cccd17e98646a6f257757dfa65654c039153`**；測試 HEAD **`d4d93e75ce35947015280f77794527ff8c9b5631`**、RunId **`r8-acceptance-20261005-02`**。初始 Git 乾淨；與 `d3f75b3` 比對 source/tests/specs 無差異，54 個原始 HTTP Method／URL／headers／Body 與核准版相同。前次獨立 review 無具體產品缺陷的結論維持，本次沒有產品修正或計畫實質修改。

| 檢查 | 本次結果 |
| --- | --- |
| Windows restore／build | 首次均 exit 0；build 0 warnings／0 errors、20.31 秒 |
| 本次 native API | Development／localhost:5000；launcher PID 6316，API PID 34336，build 後才重新啟動 |
| 01a／01b | 首次 200 healthy／200 Healthy；API 與 PostgreSQL readiness PASS |
| 01 baseline／碰撞 | PASS；完整非秘密 baseline 保存，本次 RunId 與 missingFormId 無碰撞 |
| 02 同版 full | **280／280 PASS**，0 failed／notExecuted；exit 0，27.2333 秒；實際 TRX／名稱／12 waiter／4 fault 核對通過 |
| 54 HTTP requests | **54 PASS／0 FAIL／0 BLOCKED**，逐案實際執行，各請求一次 |
| 01／02／13／14 非 HTTP | **4 PASS**，不以歷史結果代填 |
| 人工 | 全部未執行，未由 AI PASS 代填 |

DLL SHA256 **`41B433D9371F16E659872DC779E97B3549BC0E2E7C2706F6A77A5C149D91483B`**；啟動 UTC `2026-10-05T10:18:50.9449130Z`。實際 command line 指向此 repo 的 Debug/net10.0 DLL。Windows SDK 10.0.400；PostgreSQL 17.11 容器 healthy，實際 mapping `127.0.0.1:5432 → 5432/tcp`。未使用 Linux fallback、安全政策調整、替代 DB／volume 或 TestServer 代填 native startup。

## 實際 HTTP／DB 與並行證據

HTTP 直接讀核准 `.http`，只展開本次 RunId／fresh server IDs／fresh Cookie，不重新序列化 JSON numeric token。05e 明確 application/json＋0-byte body → 正式 400；06a 非 Owner Draft Submit → 409、06b Detail → 404；06c 使用本次 foreign Form 的真實 Question ID → QUESTION_NOT_FOUND，兩張 Form 均零新增。05～07 每案保存整張 Form／全表 snapshots，錯誤與 details/path 正確、舊列零變更。

08 五題型反序提交、Unicode trim／精確 Number／Choice Array／Rating／不同 User single，Receipt 皆與實際 DB ID/user_id/value/timestamp/Question 排序比對；Definition／timestamps 不變。09 multiple 新完整回覆 omission 不補舊答案，歷史保留。10 僅對本次 fresh deadline fixture 暫時設定過期，deadline 優先於 single／answers，不自動 Close，finally 復原完整原 snapshot。11／12 Close／Delete／非 Owner／重試／已刪除 Submit 均符合核准 HTTP 與資料保留。

02 的 Clock／精確 deadline／EF JSONB／Receipt／原子性／四方向 lifecycle／cleanup 是 **本次 TestServer＋真 PostgreSQL** 證據：12 個實際 blocker／waiter 與四項真正部分 INSERT／完整 Save 後 precommit、Delete UPDATE／precommit 故障回滾；不是 native HTTP 故障注入。COMMIT 已送出但結果未知的情境未驗證，不從 precommit 故障推論零新增。

13 四組 native HTTP 各先持自己的 Form row barrier、在同組共用 monotonic 10 秒內確認兩個真正 Form FOR UPDATE waiters 與 blocking chain，才釋放自己的 barrier：

| Pair | 本次 HTTP 結果／先後 | Responses/Answers | 確認兩 waiter 耗時 |
| --- | --- | --- | --- |
| 13ab | 409／201；single winner 13b | 0/0 → 1/1 | 2.485 秒 |
| 13cd | 201／201；multiple both committed; omission preserved | 2/2 → 4/3 | 0.828 秒 |
| 13ef | 409／200；close first | 1/0 → 1/0 | 0.703 秒 |
| 13gh | 404／204；delete first | 4/3 → 4/3 | 0.750 秒 |

四組 finally 均 `allWorkersDone=true`、`errors=[]`、`barrierSessionAbsent=true`，沒有以固定 sleep 或 Task pending 當重疊證據。Native 本次只代表觀察到的合法先後，另方向由本次 02 deterministic tests 覆蓋。產品 timeout 未改，沒有重設 fixture 重試求 PASS。

## 資料保留與清理

原有 IDs 的完整非秘密 baseline、full 後及最後 snapshots 均相同：**14 forms／4 users／10 answers／15 options／32 questions／9 responses**。新增列只屬本次 Owner A＋RunId＋server IDs；全套暫存 fixtures 已清除，未改舊會員／舊 Form／歷史或 timestamps，未刪 regular volume。

| 本次新 fixture | 最終狀態 | Responses／Answers |
| --- | --- | --- |
| mainFormId | closed／已軟刪除 | 2／6 |
| foreignFormId | draft／未刪除 | 0／0 |
| multipleFormId | published／已軟刪除 | 4／3 |
| deadlineFormId | closed／未刪除 | 1／0 |
| singleRaceFormId | published／未刪除 | 1／1 |

新增五張 Forms／10 Questions／3 Options／8 Responses／10 Answers 全部留存；最終全表 **19 forms／4 users／20 answers／18 options／42 questions／17 responses**。Fixtures／actors／201 response IDs／submittedAt manifest 已保存。Deadline 原值復原、全部自己的 barrier／workers 回收、Cookie/password 只在執行 process 記憶體取得並清除，未產生可用 Cookie jar／JWT 或秘密 request 暫存；成對 credentials/token/server IDs 仍為 placeholders。API／DB 留存。

## 命令與證據位置

PowerShell repo root 實際執行（stdout/stderr/exit metadata 均保存）：

```powershell
git status --short
git diff d3f75b3 HEAD -- src tests docs/specs
docker compose config --quiet
docker compose ps
docker inspect custom-form-service-postgres-1 --format '{{json .NetworkSettings.Ports}}'
dotnet restore CustomFormService.sln --disable-parallel --verbosity minimal
dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --launch-profile http --no-build
dotnet test CustomFormService.sln --no-build --no-restore --configuration Debug -m:1 --logger "console;verbosity=normal" --logger "trx;LogFileName=r8-acceptance-full-first.trx" --results-directory artifacts/r8-acceptance/r8-acceptance-20261005-02
```

啟動前只停止已確認 command line 指向此 repo 的旧 API；新 API 用 `Start-Process -WindowStyle Hidden`。Python standard library `http.client`／subprocess psql 執行 HTTP、SQL snapshots 與 barriers：`execute.py health`、`run-acceptance.py full-check`、`run-acceptance.py http`，皆用 bundled Python `C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe`。psql 走原 compose postgres，`-X -q -A -t -v ON_ERROR_STOP=1`、SQL stdin，不把 credentials 放 arguments/logs。`preflight.py`／`verify-final.py` 核對版本與文件，`git diff --check` 檢查 diff。

非秘密本機證據：[本次目錄](../../artifacts/r8-acceptance/r8-acceptance-20261005-02/)。

- [results.json](../../artifacts/r8-acceptance/r8-acceptance-20261005-02/results.json)：54 HTTP＋4 非 HTTP 實際狀態／UTC／證據；每案 `<case>.json` 回應與 before/after snapshots 同目錄。
- windows-restore-first.log／windows-build-first.log／api-build-identity.json／windows-api-process.json／01a.json／01b.json：本次 build、DLL／process、首次 health/readiness。
- [本次 TRX](../../artifacts/r8-acceptance/r8-acceptance-20261005-02/r8-acceptance-full-first.trx)、[full-verification.json](../../artifacts/r8-acceptance/r8-acceptance-20261005-02/full-verification.json)：280 counters／required names／12 waiters／4 faults／baseline。
- 13ab／13cd／13ef／13gh 的 waiters／release／outcome／cleanup JSON：本次兩 waiter／共用 10 秒／合法狀態與清理。
- [retention-cleanup-summary.json](../../artifacts/r8-acceptance/r8-acceptance-20261005-02/retention-cleanup-summary.json)、fixture-ids.json、deadline-restoration.json、retained-final.json：保留 IDs／歷史／完整舊列不變與復原。
- documents-secret-verification.json／evidence-index.json：成對 58 結果一致、54 request／預期／人工欄未變、known-secret／JWT／private-key 掃描及 SHA256。

成對 [Markdown](../test-plan/R8_Acceptance_Test.md)／[.http](../test-plan/R8_Manual_Tests.http) 已同步本次 AI 實際结果，人工欄保持未執行。`r8-acceptance-20261004-01`／`r8-acceptance-20261005-01` 首次失敗與 `r8-acceptance-20261004-02` PASS、全部 fixtures／原始證據保持歷史；本次改寫前成對全文另存 approved-before.md／approved-before.http，不覆寫歷史失敗。

交付前文件核對：54 原始請求／預期／人工欄未改，58 個 AI 欄與本次 results.json 一致；秘密掃描 **0 hits／0 read errors**，`git diff --check` 通過。原始證據不加入 Git。

## 剩餘待辦與停止點

本次核准 AI 計畫全部完成，無 FAIL／BLOCKED 或需修改產品／計畫的問題。人工未執行；仍需 Owner review 本次證據並明確確認 R8 是否 accepted。Linux fallback、native Clock／故障注入、COMMIT 結果未知、R9／R10／G1／部署均未執行且維持核准範圍。

只提交本次報告、成對結果與兩份 Implementation Plan；原始證據留 ignored artifacts。Commit SHA／剩餘 Git 狀態見交付訊息。停止於 **「驗收結果待 Owner review」**（驗收結果待 Owner 確認），未 accepted、未開始 R9。
