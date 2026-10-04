# R8 正式補驗執行報告

2026-10-04；**正式補驗完成，待 Owner review**。Owner 已核准成對計畫 `8680cccd17e98646a6f257757dfa65654c039153`，本次確認 Execute 授權持續有效；新 RunId **`r8-acceptance-20261004-02`**，候選 HEAD `90686447e473c65625123cc8e3b61b21ee8e82ce`。追蹤 source／tests 與 cleanup 候選 `d3f75b3` 相同；未修改產品、測試語意、正式規格、預期或 timeout，未進 R9／R10／G1。

本輪依 Owner 指示採 **AI 執行驗收＋Owner 審核證據**；人工全部記為未執行，不是本輪 accepted 必要條件。**尚未標記 R8 accepted**，需 Owner 根據本報告明確確認後才可進 R9。

成對實際結果：[Markdown](../test-plan/R8_Acceptance_Test.md)／[原始 .http](../test-plan/R8_Manual_Tests.http)。首次 run `r8-acceptance-20261004-01` 的 1 PASS／1 FAIL／52 BLOCKED、503 原始回應與 logs 保留於 [首次報告](R8_Acceptance_Execution_Report.md)及原 evidence 目錄；首次成對逐案結果另存 [Markdown snapshot](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/approved-before.md)／[HTTP snapshot](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/approved-before.http)。本次另記結果，不把首次失敗改填 PASS。環境修復亦保留為獨立歷史。

## 結果與實跑順序

| 項目 | 本次實際結果 |
| --- | --- |
| Restore／build | PASS；首次兩命令 exit 0；build 0 warnings／0 errors，17.35 秒 |
| 新 Windows native API | PASS；build 後才啟動，Development／localhost:5000，launcher PID 9804、實際 API PID 33732 |
| 01a／01b | PASS；首次 200 healthy／200 Healthy，readiness 2.860 秒 |
| 01 baseline／碰撞 | PASS；完整非秘密全表 baseline 保存；新 RunId 與 missingFormId 均無碰撞 |
| 02 當前全套 | **280/280 PASS**，實際 discovery／TRX 為 280 executed／passed、0 failed／notExecuted；23.9280 秒；exit 0 |
| HTTP 54 requests | **54 PASS／0 FAIL／0 BLOCKED**，全部實際各送一次，含 05b、05e、06a/b/c |
| 非 HTTP 01／02／13／14 | **4 PASS**；不以開發 277／34 代填本次結果 |
| 人工 | 全部未執行；本輪不要求人工為 accepted 必要條件，不推論人工 PASS |

實際 API command line 指向此 repo 的 `src/CustomFormService.Api/bin/Debug/net10.0/CustomFormService.Api.dll`。啟動 UTC `2026-10-04T13:16:56.5667751Z`；本次 DLL SHA256 **`A6A42B0CFF64A45A7CC6B3C56D158BB262CACD77210AB17DABC8DDFA715DD6F0`**。Windows SDK 10.0.400／runtime 10.0.11，PostgreSQL 17.11，原 `127.0.0.1:5432 -> 5432/tcp` 與 named volume 保留。沒有 Linux fallback、安全政策調整、UAC 提權 terminal 或產品 Clock hook。

命令均在 repo root；restore／build／health／full／HTTP 的首次 stdout／stderr 以 Tee-Object 或 Start-Process redirect 保存於 `artifacts/r8-acceptance/r8-acceptance-20261004-02/`，前置工具輸出亦保留於本次 chat：

```powershell
git status --short
git diff d3f75b3 -- src tests
docker compose config --quiet
docker compose ps
dotnet --info
dotnet restore CustomFormService.sln --disable-parallel --verbosity minimal
dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --launch-profile http --no-build
& 'C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' artifacts/r8-acceptance/r8-acceptance-20261004-02/execute.py health
dotnet test CustomFormService.sln --no-build --no-restore --configuration Debug -m:1 --logger "console;verbosity=normal" --logger "trx;LogFileName=r8-acceptance-full-first.trx" --results-directory artifacts/r8-acceptance/r8-acceptance-20261004-02
& 'C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' artifacts/r8-acceptance/r8-acceptance-20261004-02/run-acceptance.py full-check
& 'C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' artifacts/r8-acceptance/r8-acceptance-20261004-02/run-acceptance.py http
& 'C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' artifacts/r8-acceptance/r8-acceptance-20261004-02/fill-supplement.py
& ./artifacts/r8-acceptance/r8-acceptance-20261004-02/scan-secrets.ps1
& 'C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' artifacts/r8-acceptance/r8-acceptance-20261004-02/verify-supplement.py
git diff --check
```

啟動 API 用 `Start-Process -WindowStyle Hidden`，先確認舊 5000 listener 的 command line 屬此 repo 才停止該 API／launcher。Restore／build 完成後啟動新 process，沒有沿用環境修復時的 API 作本次 startup 證據。HTTP 工具為 Python standard library `http.client`，直接讀核准 `.http` 的 Method／URL／headers／原始 Body，僅展開本次動態變數，不重新序列化 number／跳脫 token。05e 以 `Content-Length: 0` 明確送零位元組 Body；helper 在執行前完成此傳輸表示處理，未修改核准 request。DB 走 `docker compose exec -T postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -X -q -A -t -v ON_ERROR_STOP=1'`，SQL stdin、UUID 先驗格式，credentials 不放 arguments／logs。

## HTTP／DB／Receipt 代表結果

- **05e**：application/json＋0-byte Body → 400 VALIDATION_ERROR，body／INVALID_VALUE；既有資料與五張 fixtures 完整 snapshot 不變。
- **06a/b**：同一 Active 非 Owner B，Submit 他人 Draft＋缺 answers → 409 FORM_NOT_SUBMITTABLE；Detail → 404 FORM_NOT_FOUND，兩者規則區分成立。
- **06c**：本次 foreign Form 的真實已保存 Question ID 經 SQL 確認 form_id，Submit 到 main → 400 VALIDATION_ERROR，answers[0].questionId／QUESTION_NOT_FOUND；main／foreign 均 0 Response／0 Answer，全部原列不變。
- **07**：Required 缺题／合法空文字、明確 null、非空 minLength、Number String、raw Rating 5.0、Option 歸屬／重複與 NUL 均為核准正式 400／正確 path＋code。05～07 每案保存全表／目標 Form snapshot，與案前完整比較。
- **08**：Owner 五題型反序 Request → Receipt 按 Question 排序；Text 為原 `Hello 😀`，Number 精確 `1.00000000000000000000000000001`，Choice IDs／Multiple Array 次序、Rating 5 均符實際 DB。single 重送＋缺 answers → 409；B 可各自一次，Optional omission 不補 null，main 共 2 Responses／6 Answers。Form／Question／Option 與 timestamps 不變。
- **09**：identified multiple 舊兩個 Answers 保留，新完整 Response 略過所有 Optional、Receipt 空 Array；總 2 Responses／2 Answers。Request `1e0` 的實際 JSONB token 為 `1`，數值等值成立，未宣稱原 token 逐字保留。
- **10**：A 先提交成功，再僅改本次 deadline fixture 為過去；deadline 優先於 single／缺 answers，回 409 SUBMISSION_DEADLINE_EXCEEDED，不自動 Close。finally 依真實原值復原，完整 snapshot 等於 probe 前，包括 Response／全部 timestamps。
- **11／12**：Close／Delete 前後只改契約允許欄位；非 Owner Delete 403，Owner Soft Delete 204、重試／已刪除 Submit 404，main 2 Responses／6 Answers 全部保留。

每個成功 Receipt 都與 SQL／實際已保存 Response、Answers 比對 ID／內部當次 user_id／UTC timestamp／精確 value／Question 順序；Receipt root 不含 userId／Answer ID，未作答不補 null。詳細 request／response 原文、Content-Type、Body bytes、UTC、耗時與 identity 均按 case ID 保存，不保存 Cookie value。

## 本次 02：Clock／EF／並行與故障

同次 Windows TRX 已逐名核對核准代表測試及 DataRows：Text／Required／null／Integer／Number／owned JsonElement、唯一 Clock 取樣、deadline 前／等於／後、等待跨 deadline、取樣後 Save／commit 跨 deadline、實際 EF JSONB／timestamp 讀回與 Receipt、single／multiple、四方向 Submit／Close／Delete 及 cleanup 聚合。完整原有回歸亦在此次 280 案內。

本次 TRX 中 **12 個 R8 真 PostgreSQL waiter 案例**，每個有 blocker／waiter PID、Lock／Form FOR UPDATE StdOut；四個故障案例有注入前實際值與新 connection 回滾證據：

| 注入位置 | 注入前實際資料與回滾結果 |
| --- | --- |
| Submit 第一筆 Answer INSERT 後、其餘保存前 | 同交易 Response=1／Answer=1；失敗 response／answers 殘留均 0；single waiter 成功 201 |
| Submit 全部 Save／讀回後、COMMIT 送出前 | 同交易 Response=1／Answer=5；失敗資料殘留均 0；single waiter 成功 201 |
| Delete UPDATE 已執行後 | 同交易 deleted_at 非 null；新 connection 完整 Definition 原狀；Submit waiter 201 |
| Delete COMMIT 送出前 | 同上；完整回滾，等待 Submit 可成功 |

這是 **TestServer＋實際 PostgreSQL** 的受控自動證據；沒有冒稱 native HTTP 故障注入。COMMIT 已送達但結果未知的情境未驗證，不能由此推論零新增。A 的參數化 SELECT 探針仍只是 JSONB 相容性／數值等值，不取代此輪 EF 保存／Receipt 測試。

## 13：四組 native HTTP 真實重疊

每組先用唯一 `r8-barrier-<RunId>-<pair>` session 鎖本次 Form row，確認 transaction／PID；兩個 exact companion workers 同時開始。monitor 與輪詢／SQL subprocess timeout／delay 共用從第一個 worker 前開始的 monotonic **10 秒**，沒有每次重新計時。直接／間接 blocking chain 都追到自己的 barrier，兩個真 Form FOR UPDATE waiters 確認後才 COMMIT；產品 DB timeout 未改。

| Pair | 本次 HTTP pair／先後 | 觀察兩 waiter 耗時 | Response／Answer counts |
| --- | --- | --- | --- |
| 13a/b | 201／409 RESPONSE_ALREADY_SUBMITTED；13a 成功 | 2.734 秒 | 0/0 → 1/1，value 對應 winner |
| 13c/d | 201／201；multiple 兩份完整歷史 | 0.468 秒 | 2/2 → 4/3，empty Receipt 不補舊答案 |
| 13e/f | 201／200；Submit 先 commit，Close 後成功 | 0.453 秒 | 1/0 → 2/0，舊 A Response 保留、最終 closed |
| 13g/h | 404 FORM_NOT_FOUND／204；Delete 先 commit | 0.500 秒 | 4/3 → 4/3，零新增、舊資料保留、仍 published＋soft-deleted |

上述每組 waiter PID／query／blocking chain／釋放 UTC、worker responses、案前案後完整 snapshot 另存。釋放後 workers 使用有限完成等待，finally 先收回自己的 barrier，再逐一取消／有限觀察全部 workers，最後聚合清理錯誤；**四組均 allWorkersDone=true、cleanup errors=[]、barrierSessionAbsent=true**。没有終止陌生 DB backend／服務或重設 fixture 重試。原生 race 只代表本次觀察到的合法先後；另方向由此次 02 deterministic tests 覆蓋。

## 資料保留、清理、範圍及未執行事項

01 baseline、02 全套後及14最終依原 IDs 比較完整非秘密原列，全部相同：**4 Users／9 Forms／22 Questions／12 Options／0 Responses／0 Answers**。不只比較 counts。回歸測資清完；新增列只屬本次已確認 Owner A＋RunId＋server IDs，沒有硬刪除任何舊資料或重建 volume。

| 本次保留 fixture | 最終 status／deleted | Responses／Answers |
| --- | --- | --- |
| main | closed／已軟刪除 | 2／6 |
| foreign | draft／未刪除 | 0／0 |
| multiple | published／已軟刪除 | 4／3 |
| deadline | closed／未刪除、deadline 原值已復原 | 2／0 |
| singleRace | published／未刪除 | 1／1 |

本次新增 **5 Forms／10 Questions／3 Options／9 Responses／10 Answers**；DB 最終全表為 4 Users／14 Forms／32 Questions／15 Options／9 Responses／10 Answers。合法新 fixtures／歷史／軟刪除列保留。ID 與 actor／submittedAt manifest 見下方索引。

fresh Cookie／合成 password 只在執行 process 記憶體取得，finally 清除，沒有可用 Cookie jar／JWT／秘密 request 暫存；paired files credentials／tokens／server IDs 仍為 placeholders，只有非秘密 RunId 更新。deadline 復原、全部 workers／barriers 回收及完整列比對完成。交付前執行 known-secret／JWT／private-key 掃描為 **0 hits／0 read failures**（API log 以 FileShare.ReadWrite 唯讀），兩檔自查為 **54 requests／預期／人工欄與核准版相同、58 AI 欄與 manifest 一致**；工具 exit 0，`git diff --check` 通過。實際結果另存 evidence。

未執行：人工、Linux fallback、native HTTP 可控制 Clock／故障注入、COMMIT 結果未知情境、R9／R10／G1、部署。這些界線維持核准範圍；本輪 AI 核准計畫沒有未完成或 BLOCKED 案例，也未發現產品缺陷。R10 按精確數值等值分組／排序的既有登記維持，未提前實作。R7 accepted 歷史未改填。

## 證據索引與交付

全部非秘密原始證據保留於 ignored `artifacts/r8-acceptance/r8-acceptance-20261004-02/`，未把 credentials 或全資料 fixtures 加入 Git：

| 索引 | 內容 |
| --- | --- |
| [results.json](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/results.json) | 54 HTTP＋4 非 HTTP逐案狀態／UTC／證據 |
| [restore](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/windows-restore-first.log)／[build](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/windows-build-first.log)／[API build](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/api-build-identity.json)／[process](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/windows-api-process.json) | 首次命令輸出／同版 startup／SHA／PID |
| [01a](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/01a.json)／[01b](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/01b.json) | 本次首次正式 health／readiness 原回應 |
| [full log](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/windows-full-first.log)／[TRX](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/r8-acceptance-full-first.trx)／[full verification](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/full-verification.json) | 實際 280 counters／名稱／12 waiters／4 faults／baseline |
| [HTTP／DB log](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/http-db-concurrency-first.log)／[fixture IDs](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/fixture-ids.json) | request case mapping、fresh IDs；每案 `<case>.json` 及 before／after snapshots 同目錄 |
| [06c ownership](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/06c-real-question-ownership.json)／[08a Number](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/08a-exact-numeric.json)／[09a JSONB](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/09a-exact-numeric.json) | 真實跨 Form ID／精確數值／实际 token |
| [deadline 復原](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/deadline-restoration.json) | 完整 snapshot 等於 probe 前 |
| [single waiters](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/13ab-waiters.json)／[multiple waiters](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/13cd-waiters.json)／[Close waiters](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/13ef-waiters.json)／[Delete waiters](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/13gh-waiters.json) | 四組真實 PID／query／blocking chain；對應 release／cleanup／outcome files 同目錄 |
| [保留與清理](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/retention-cleanup-summary.json)／[原列最終](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/retained-final.json) | 五張最終狀態／Response IDs／actor／counts／完整舊列 |
| [文件核對](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/documents-verification.json)／[秘密掃描](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/secret-scan.json)／[證據 manifest](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/evidence-index.json) | 原始核准請求／預期／人工欄／58 AI欄／evidence SHA256 |

只提交成對實際結果、兩份 Implementation Plan、首次報告的最新狀態連結與本正式補驗報告；產品／tests／Frozen Contract 不變。單一目的 commit SHA、完整 diff 與剩餘 Git 狀態見交付訊息。停止於 **「正式補驗完成，待 Owner review」**，等待 Owner 確認 R8 accepted。
