# R8 B — Submit／Receipt／原子保存與並行開發報告

2026-10-04；**R8 開發完成、待驗收**。Owner 已確認 A code review 通過，明確授權依 [核准計畫](R8_Implementation_Plan.md) 完成 B。基準 HEAD `62f278422f14bdb6ce2b45d4ad53d2ebd3428775`，開始時 working tree 乾淨。A 初次開發與補件的歷史結果保留；本報告是 B 開發自測，不代表正式驗收或 Round accepted。

**Git 操作紀錄**：Git staging 的自動權限審查曾兩次逾時（不是安全判定），普通 sandbox 另回 `.git/index.lock: Permission denied`。先備妥全部工作區 diff／報告後，Owner 明確回覆「授權再次請求 Git 寫入並提交」；其後 staging 成功，依此新指示完成必要 Git 提交。阻碍時的工作區 diff 保留於 `artifacts/r8-b/B-before-git-authorization.diff`，最終 commit diff／SHA 與 Git 狀態另見下方及交付訊息。

## 主要變更與正式規格對照

| 範圍 | 實作／可觀察結果 | 正式依據 |
| --- | --- | --- |
| Submit | 新增 `POST /api/v1/forms/{formId}/responses`，完整 Authentication 在解析之前，Owner／其他 Active User 可提交；anonymous 仍需登入且保存內部 userId。 | 01 SUB-01～05；03a §2；03c §4 |
| 驗證次序 | UUID／Content-Type／JSON／Envelope 未知或唯讀欄位 → 未刪除 Form → Published → 鎖後唯一時間 → deadline → single → A 的 answers shape、Question、Required／type／config／Choice／omission → 寫入前重確認。`非 Published + 缺 answers` 實際 HTTP 為 409 FORM_NOT_SUBMITTABLE；deadline 優先於 single，single 優先於 answers。 | 03c §4.3～4.6；04 §4／9；05 §12 |
| Clock | 沿用 TimeProvider DI。取得 Form lock 並重讀 Published 後只取樣一次；嚴格 `< deadline`，stage 14 重確認及 Response.SubmittedAt 均使用同值。等待跨 deadline 拒絕；判定後 Save／commit 跨 deadline 仍成功；不自動 Close。 | 01 CON-04；03c §4.3；05 §10 |
| 原子保存 | 每次自己的 ReadCommitted transaction、共用 Form row lock；全答案通過才建立 Response＋Answer graph，一次 EF Save。FK 使用 server ID／current user ID，不附加 User／Question graph。持鎖至 commit／rollback，commit 成功才回 201。 | 01 CON-01／02；02 §10～11／15；03c §4.7；05 §8／9.2 |
| single／multiple | single 在鎖內查同 Form＋User 的成功 Response，寫入前再次查；等待者依前者 commit／rollback 後現況判定。multiple 每次新增完整歷史，不補舊答案、不新增 form/user unique 或重試框架。 | 01 SUB-02／03；03c §4.3／4.7；05 §9.2 |
| Receipt | 保存後 AsNoTracking 重新讀取實際 Response／Answers；排序為 Question sortOrder ASC、Question ID ASC。只回 id／formId／submittedAt／answers(questionId,value)，不回 userId／Answer ID、不補 null。JSON value Clone 保持獨立生命週期。 | 01 SUB-07；03c §3.1～3.2／4.5；05 §13 |
| Delete 局部修正 | UUID 後 Begin ReadCommitted → 共用 active Form lock → 重讀／Owner 判定 → 只更新 deletedAt／updatedAt → Save／commit → 204。保持 Draft／Published／Closed 均可刪除、非 Owner 403、已刪除／重試 404；Definition／子列／status 保留。Close 沿用現有實作。 | 03b §8.1／8.3；03c §4.7；05 §8／9.2 |

未修改 A validator、RequestJson、DefinitionNumber、TextRules、Definition Validation、Schema／Migration 或 Frozen Contract。Number 安全保存範圍仍依 01 §3.3 沿用既有 DefinitionNumber／PostgreSQL 17 上限（整數部分 131072 位、小數部分 16383 位，及既有 raw exponent 防護），不是新規格衝突。R10 仍須按精確數值等值分組／排序，`1`／`1.0`／`1e0` 同組，不以原 token 或 decimal／double 分組；本次只保留登記。

## EF 保存後讀回與 Receipt 證據

新 B 測試透過真 API／EF INSERT 保存，Receipt 由 server 的 AsNoTracking 查詢產生；測試再用**另一個 DI scope／DbContext**查已 commit 的 Response／Answers，確認 Receipt 的每個值、排序與 submittedAt。Number 額外要求 Receipt token 與 DB 實際讀回 token 一致，並用新 SQL connection 驗數值等值：

- 五題型反序 Request → Question 順序 Receipt；trim 後 `Hello 😀` 保留合法 pair，Choice 存為標準 UUID，Multiple Choice 保留選项 Array 次序，Rating 為 Integer。Form／Question／Option 完整列與 timestamps 比較前後相等。
- `1e0` 保存後 JSONB 實際為 `1`，Receipt 為 `1`；不承諾原 Request token 逐字 round-trip。其他案例為 `0.123456789012345678901234567890123456789`、`1e30`、`1e-30`，不經有限精度轉型。
- Clock 取樣刻意含次微秒；Receipt submittedAt 等於 EF 讀回的實際 PostgreSQL timestamp，沿用既有微秒保存行為。
- multiple 新提交可以沒有 Answer，舊 Response 的五個 Answers 保留，新 Receipt 為空 Array；沒有從舊 Response 補 Optional。
- equal sortOrder 的 SQL 專用 fixture 僅測 Receipt 的 Question ID tie-break；没有改變正式 Definition Validation 或宣稱該 fixture 是合法 Publish Definition。

A 的 JSONB SELECT 探針仍僅是相容性／數值等值驗證；本段新增的 API→EF→已保存資料→Receipt 才是 B round-trip 證據。文字 escaping／JSON Array 排版差異按 JSON value 比較；Number 的實際讀回 token 與 Receipt 另作精確字串檢查。

## 並行與故障的實際證據

[Focused TRX](../../artifacts/r8-b/windows-b-final.trx) 及其 [StdOut 擷取](../../artifacts/r8-b/concurrency-fault-evidence.json) 有 **12 個通過案例的實際 PostgreSQL waiter**：以 `pg_blocking_pids`、`wait_event_type=Lock`、Form `FOR UPDATE` 查詢確認，再釋放 gate。僅 Task 未完成或延遲不視為證據。gate 等待 35 秒、Reached／請求完成各 15 秒、waiter 10 秒、整組 cancellation 45 秒、finally drain 10 秒；finally 釋放 gate、取消並觀察全部請求，外部 blocker 交易亦 finally Dispose。未觀察到 waiter 時 Assert.Fail，沒有並行 PASS。

| 先取得鎖的操作 | 真實等待後結果 | 資料證據 |
| --- | --- | --- |
| single 同 User Submit | 第一個 201、第二個 409 RESPONSE_ALREADY_SUBMITTED | 1 Response／5 Answers |
| single 不同 User Submit | 各 201 | 2 Responses，第二份無 Answers |
| multiple 同 User Submit | 各 201 | 2 完整歷史，第二份略過 Optional |
| Submit → Close | 201／200 | Response／5 Answers 保留，僅 status／updatedAt 改變 |
| Close → Submit | 200／409 FORM_NOT_SUBMITTABLE | 零新增 |
| Submit → Delete | 201／204 | Response／5 Answers 保留，僅 deletedAt／updatedAt 改變 |
| Delete → Submit | 204／404 FORM_NOT_FOUND | 零新增 |
| 外部 Form lock 等待跨 deadline | 409 SUBMISSION_DEADLINE_EXCEEDED | 等待中 Clock calls=0，釋放後=1，零新增 |

故障均為**已確認 COMMIT 發送前**的 SQL `SELECT 1 / 0`，由既有 middleware 回正式 500 INTERNAL_SERVER_ERROR。不是模擬 client 網路未知結果，也沒有把任意 DB error 映射成重複提交。

| 注入位置 | 注入前同交易實際查詢 | 回滾後新 connection／等待者 |
| --- | --- | --- |
| Submit 第一個 EF Answer INSERT 已執行、其餘四個尚未執行；DbCommandInterceptor.ReaderExecutedAsync，先完成該 statement 的 reader | Response=1、Answer=1 | 失敗 response ID 及其 Answers 均 0；single waiter=201，僅其 Response 保留 |
| Submit 全部 Save 及 Receipt 讀回後，TransactionCommittingAsync、送出 COMMIT 前 | Response=1、Answer=5 | 失敗 response ID 及其 Answers 均 0；single waiter=201 |
| Delete UPDATE 已執行後、ReaderExecutedAsync | deleted_at 在該交易中非 null | 完整 Form／Question／Option 列與更新前相等；Submit waiter=201 |
| Delete TransactionCommittingAsync、送出 COMMIT 前 | deleted_at 在該交易中非 null | 原狀保留；Submit waiter=201 |

部分 Answer 故障使用 Factory 的**測試專用** MaxBatchSize=1／MinBatchSize=1；其餘案例及 production 保持原 batch 設定。查詢的是 EF 真實 INSERT／UPDATE，不由 injector 代寫候選資料。上述零殘留證據只涵蓋這些提交前注入點；**COMMIT 已送達但結果未知時可能已成功，未驗證也不宣稱零新增**。multiple 的不確定結果重送可能新增歷史，依正式契約保留。

## 實跑命令與結果

Windows、.NET SDK 10.0.400、既有 PostgreSQL 17.11 Compose。MSTest Workers=16／MethodLevel。所有 logs／TRX 保存在 `artifacts/r8-b/`，首次失敗未覆寫。B 本次 Windows DLL 能正常載入，不沿用 A 的測試載入封鎖結論；未執行 Linux fallback、原生 API startup smoke 或正式驗收。

```powershell
dotnet restore CustomFormService.sln --disable-parallel --verbosity minimal
dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test CustomFormService.sln --no-build --no-restore --configuration Debug -m:1 --filter "FullyQualifiedName~R8_" --logger "console;verbosity=normal" --logger "trx;LogFileName=windows-b-final.trx" --results-directory artifacts/r8-b
dotnet test CustomFormService.sln --no-build --no-restore --configuration Debug -m:1 --logger "console;verbosity=normal" --logger "trx;LogFileName=windows-full-first.trx" --results-directory artifacts/r8-b
```

各命令以 `2>&1 | Tee-Object -FilePath artifacts/r8-b/<對應 log>` 保留輸出，再 `exit $LASTEXITCODE`。實際結果：

| 檢查 | 結果／證據 |
| --- | --- |
| Restore | 預設 sandbox 首次 exit 1、沒有 stdout；使用已授權 cache／SDK 存取後 exit 0、全部 up-to-date，`windows-restore-authorized.log`。 |
| Build | 第一次局部編譯成功但有 1 個 MSTEST0037（count assertion）warning，改用 HasCount；正式 first／second／diagnostic／final builds 均 exit 0、0 warnings／0 errors。最終 [build log](../../artifacts/r8-b/windows-build-final.log)。 |
| B focused 首輪 | **18 PASS／13 FAIL／31**，`windows-b-first.log/.trx`。失敗集中於測試 deadline fixture 輸出 +00:00 而輸入契約要求 Z、Receipt assertion 把 escaping／空白當成值差異，以及單筆 batch 工具重複 enum mapping；沒有修改業務規則來消除失敗。 |
| B focused 第二輪 | **30 PASS／1 FAIL／31**，`windows-b-second.log/.trx`；只剩 enum mapping 重複。 |
| Diagnostic | `--filter "FullyQualifiedName~R8_submit_precommit_fault&DisplayName~partial-answer"` exit 0 但 **0 cases**，不當 PASS；改用 `--filter "FullyQualifiedName~R8_submit_precommit_fault"`，1 PASS／1 FAIL／2，堆疊定位 Npgsql FindEnumMapping 的重複項。Factory 保留 Program mapping，只追加 batch 設定。對應 `windows-partial-diagnostic`／`windows-fault-diagnostic` logs／TRX。 |
| B focused 最終 | **31/31 PASS、0 skipped**，19.4737 秒、exit 0；[log](../../artifacts/r8-b/windows-b-final.log)／[TRX](../../artifacts/r8-b/windows-b-final.trx)。 |
| 完整回歸首次 | **277/277 PASS、0 skipped**，17.6111 秒、exit 0；[log](../../artifacts/r8-b/windows-full-first.log)／[TRX](../../artifacts/r8-b/windows-full-first.trx)。包含既有 167（含 R4～R7）、A 79、B 31；Delete 三狀態／Owner／重試及既有 R7 writers／Delete 競爭均通過。 |

代表性測試位於三個 `FormEndpointsTests.Submission*.cs`：all-types／number round-trip、non-published missing answers、auth／transport／gate priority、deadline boundary／wait／delayed commit、single／multiple concurrent submissions、四方向 Submit／Close／Delete、两種 Submit／Delete precommit faults。A 的 `Nonempty_text_shorter_than_minLength_returns_invalid_value` 隨本次全套通過；未重做 Definition Validation 或擴大 UUID／Unicode 矩陣。

## 資料安全、diff 與交付狀態

以既有 `artifacts/r8-a-review/fixture-snapshot.sql` 在測試前後透過 `docker compose exec -T postgres ... psql ...` 讀取非秘密完整列 snapshot。users 不輸出 password hash。`fixtures-before.json` 與 `fixtures-after.json` **逐字相等**：[比較結果](../../artifacts/r8-b/fixture-comparison.json)，4 users／9 forms／22 questions／12 options／0 responses／0 answers。TestCleanup 只依本次隨機 emails 清理其 Responses／Answers 再清自有 Form／User；沒有刪既有 fixtures、重建 DB 或移除 volume。

修改共 14 檔：production 4（ResponsesController、ResponseReceipt、Delete、FormAggregateLock 註解）、tests 5（三份 B tests、Factory batch 選項、fixture cleanup）、documents 5（本報告、兩份計畫、A 報告與 review 索引的目前狀態）。`git diff --check`、逐檔 diff／秘密與 scope 自查完成；沒有規格衝突、沒有修改正式規格或 migration。

以單一目的 B 開發 commit 交付；完整 [B commit diff](../../artifacts/r8-b/B-full.diff) 含全部程式、測試、報告與兩份計畫，SHA 在 diff header／交付訊息，提交後 Git 狀態見交付訊息。A code review 通過不等於完整 B 已 review；本次是依核准計畫的開發交付。

未做 R9 Latest／Count、R10 Statistics、G1、部署或正式驗收準備／執行；未驗證 commit 結果未知的故障情形。既有正式驗收文件／歷史結果不改填，後續正式驗收須使用本次 R8 版本；尚無 R8 成對驗收計畫。停止於 **「R8 開發完成、待驗收」**，等待 Owner 下一階段授權，不自動準備驗收或宣告 accepted。
