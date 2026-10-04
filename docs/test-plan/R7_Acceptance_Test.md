# R7 成對驗收計畫 — Lifecycle 與 Result Visibility

2026-10-04；**Owner-approved revised plan／acceptance executing**。Owner 在本 chat 核准 `28cb8e2` 計畫，明確授權本次三項局部修訂後直接執行、不需再停下詢問。本文件與 [R7_Manual_Tests.http](R7_Manual_Tests.http) 成對；開始時 AI／人工皆未執行。只驗收 R7 端點 12～14 及其 PATCH Freeze 依賴，不做 R8／Statistics／G1，也不做 L2 重構或 Capacity Policy。

依 [AGENTS.md](../../AGENTS.md)、[驗收 skill](../../.agents/skills/v1-acceptance-testing/SKILL.md)、[Implementation Plan](../implementation/V1_Backend_Implementation_Plan.md) R7、[核准實作計畫](../implementation/R7_Implementation_Plan.md)、[開發報告](../implementation/R7_Development_Report.md) 與 [有限確認報告](../implementation/R7_Review_Confirmation_Report.md)。最新 repository 提交基準 `28cb8e2`；其中最新業務程式提交 `db2fe1b`（包含 review 局部修正）。本次計畫修訂僅文件，業務程式不變，正式規格：[03a](../specs/03a-v1-api-contract-common.md) §2／4～6、[03b](../specs/03b-v1-api-contract-auth-user-form.md) §8～9、[04](../specs/04-v1-error-catalog.md) §2～4／Form Errors、[01](../specs/01-v1-domain-specification.md) FORM／CON、[02](../specs/02-v1-database-specification.md) §7～9、[05](../specs/05-v1-system-constraints.md) §8～10。M2 仍「未重現、根因未明」，本次計畫明列追蹤，不稱既有偶發。

## 執行規則與共用測資

- 本次預定唯一 RunId：`r7-acceptance-20261004-01`；確認無碰撞後採用。獨立重跑或人工完整流程換新 RunId，兩文件及工具同步；人工只讀檢視可沿用 AI run，註明來源。
- API：`http://localhost:5000`；沿用 R4 active A／B：`r4-member-a-r4-acceptance-20261001-01@example.test`／`r4-member-b-r4-acceptance-20261001-01@example.test`。密碼／Cookie／DB 連線值本機取得，不抄舊計畫的可用秘密。
- 保留既有 R4／R5／R6 全資料。本次 A 新建 `R7 main <RunId>`、`R7 deleted <RunId>`、`R7 concurrent <RunId>` 三張；不預填 Server IDs。`missingFormId=00000000-0000-0000-0000-000000000007` 須先證實不存在。
- 逐筆 request body 以 .http 為唯一範例，Markdown 管 DB／命令／斷言；可用等價 curl／HttpClient，每筆記對應編號、status/code/details、非秘密 response、DB 快照。Cookie 只能從本次 Login 取得；每筆 @no-cookie-jar，A／B／無登入嚴格區分。
- 順序 01→15；14a／14b 只能並行 harness 發送。所有 Error 使用正式 `error` Envelope；message 只驗存在與型別，不鎖文案；detail 不依陣列順序。
- Windows／Linux 獨立記錄；AI 與人工分開，不以本次有限確認結果預填。任一前置失敗，保留原證據並將依賴案 BLOCKED／未執行。需要改請求／先後／預期／資料效果時先成對修訂送 Owner，不能改預期求 PASS。

## 環境與完整回歸命令（01／02）

01 先在 repo root 核對／僅停止舊的同 repo API，完成本次 restore／build（逐一 exit 0）後，以該 repo、本次 Debug build 的 API DLL 重新啟動並驗證 01a health／01b readiness。記錄 DLL 完整路徑、SHA256、build 命令／時間與 API startup log。不得沿用舊程序冒稱本次 build。02 接著跑同一 build 的 full／M2 targeted（--no-build），不在 API 啟動後重新 build。若必須重建，仍須先核對／停止這個 API，再 build、重啟並重驗 health/readiness，保存第一次紀錄。不要停止其他 dotnet 或 PostgreSQL。

```powershell
$RunId = 'r7-acceptance-20261004-01'
$EvidenceDir = "artifacts/r7-acceptance/$RunId"
New-Item -ItemType Directory -Force $EvidenceDir | Out-Null
dotnet --info
docker compose config --quiet
docker compose ps
dotnet restore CustomFormService.sln --disable-build-servers -m:1
dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1 -p:UseSharedCompilation=false
# 01：restore/build 全部成功後，在另一個 PowerShell／同 repo 啟動：
dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --launch-profile http --no-build
# 原視窗依 .http 送 01a／01b，均成功後才執行 02：
dotnet test CustomFormService.sln --no-build --no-restore --configuration Debug -m:1 --logger 'console;verbosity=normal' --logger 'trx;LogFileName=windows-full-first.trx' --results-directory $EvidenceDir
dotnet test CustomFormService.sln --no-build --no-restore --configuration Debug -m:1 --filter 'FullyQualifiedName~Noncanonical_Cors_origin_fails_startup' --logger 'trx;LogFileName=windows-m2-targeted.trx' --results-directory $EvidenceDir
```

逐個命令記 exit／log，不能讓最後一個成功掩蓋前面失敗。Linux fallback 使用相同 flags，平台檔名改 linux；不得以 Linux PASS 填 Windows PASS。本計畫 Windows／Linux 實際：均 **未執行（等待 Owner review）**。

| 非 HTTP 檢查／編號 | AI 實際 | 人工實際 |
| --- | --- | --- |
| 01 · Windows restore／build／本次 DLL 原生啟動／既有資料 baseline | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 02 · Windows 同一 build 的 full／M2 targeted | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 02 · Linux fallback（僅 Windows 被阻擋時） | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

HTTP 案例的 AI 實際欄必須註明所用平台；不以 Linux 原生啟動或 TestServer 代填 Windows 啟動。

### 02／12 回歸覆蓋對照（已核對 source，執行後另核對 TRX）

| 宣稱的覆蓋 | 實際案例名稱／範圍 |
| --- | --- |
| M2 | `ConfigurationTests.Noncanonical_Cors_origin_fails_startup` |
| Publish deadline 微秒／鎖後取時 | `R7_publish_deadline_uses_locked_server_time_and_actual_storage_value`（-1／0／+1）；`R7_publish_samples_time_after_waiting_for_the_form_lock` |
| Deadline 保存精度 no-op | `Patch_deadline_uses_actual_storage_precision_for_no_op` |
| Candidate 隔離、完整快照／SQL | `R7_publish_without_body_normalizes_only_candidate_and_preserves_entire_definition` |
| Draft／完整性與多 details | `R7_incomplete_draft_can_be_saved_but_publish_fails_atomically`；`R7_publish_reuses_structural_rules_and_collects_precise_details_without_writes` |
| 精確 Number Config | `Number_config_compares_and_persists_exact_json_numbers` |
| 三端點 Save／commit rollback | `R7_save_and_commit_faults_rollback_all_rows`（三 action × Save／commit） |
| 雙順序 Freeze、重複 Publish／Close、Visibility／Lifecycle 競爭 | `R7_writers_share_lock_and_validate_after_the_first_commit`（七組 DataRow） |
| 三狀態 Visibility no-op／欄位 | `R7_visibility_is_independent_atomic_and_no_op_in_every_state`；`R7_visibility_rejects_invalid_fields_without_any_write` |
| 非字串／四種傳輸錯誤 | `R7_review_visibility_non_string_values_are_rejected`；`R7_review_visibility_transport_errors_use_contract_envelope_without_writes` |
| R7 Auth／parse／existence／Owner／domain、disabled | `R7_errors_follow_auth_parse_existence_owner_and_domain_order`（三 action；含 missing／invalid Cookie／disabled，不含 expired） |
| expired token（共用 Auth，非 R7 專屬） | `AuthUserEndpointsTests.Missing_invalid_expired_and_unknown_user_tokens_are_distinguished`：GET /users/me 的 AUTH_TOKEN_EXPIRED；`Logout_succeeds_with_missing_invalid_and_expired_tokens` 只覆蓋 Logout 例外 |
| M1 有限確認／Owner 已刪除 | `R7_review_delete_waiting_on_r7_write_preserves_transition_and_children`；`R7_review_owner_cannot_access_deleted_form`；不涵蓋未實作的 Submit |

## DB 操作與比較（01／04～15）

DB 操作 AI 可執行；人工可在 DBeaver 同步執行。使用現有 PostgreSQL 的本機連線／`docker compose exec -T postgres`，psql 以 `-v ON_ERROR_STOP=1`，UUID／RunId 先驗格式。每張新 Form 以以下查詢生成全欄位快照（psql `formId` 變數為該 Form UUID），保存 JSON 至本次 evidence；不含 User hash。

```sql
SELECT jsonb_build_object(
 'form', (SELECT to_jsonb(f) FROM forms f WHERE f.id = :'formId'::uuid),
 'questions', COALESCE((SELECT jsonb_agg(to_jsonb(q) ORDER BY q.id)
     FROM questions q WHERE q.form_id = :'formId'::uuid), '[]'::jsonb),
 'options', COALESCE((SELECT jsonb_agg(to_jsonb(o) ORDER BY o.id)
     FROM options o JOIN questions q ON q.id = o.question_id
     WHERE q.form_id = :'formId'::uuid), '[]'::jsonb));
```

比較須按完整 JSON 結構／UUID 排序，刪除「預期可變 Form 欄位」後其餘欄位／rows 必須完全相等；改值另斷言該欄確實改變。Publish／Close 只允許 status／updated_at；Visibility 只允許 result_visibility／updated_at；no-op／失敗不允許任何變化。DB 微秒值為準，不以 API 字串格式差異當資料變更。01 的既有快照範圍為 A／B 的所有舊 Form＋子列及會員公開欄位，排除本次新 IDs，15 再比對。

07：先保存 07a 後 canonical JSON（psql `canonicalSnapshot` 為此非秘密 JSON），確認所有目標 id 屬本次 main，再做以下 probe。三個 UPDATE 可包同一短交易；不得在送 HTTP 時仍持此交易。取 publish-before；07b 回應／DB 核對完即 finally 復原。

```sql
UPDATE forms SET title = '  R7 main ' || :'runId' || '  '
 WHERE id = :'mainFormId'::uuid;
UPDATE questions SET title = '  Text  ', config = '{"placeholder":"  raw placeholder  "}'::jsonb
 WHERE id = :'qMainText'::uuid AND form_id = :'mainFormId'::uuid;
UPDATE options SET label = '  One  ', value = '  one  '
 WHERE id = :'oMainSingle'::uuid AND question_id = :'qMainSingle'::uuid;
-- finally：只還原上述欄位，不恢復已成功提交的 status／policy／timestamps。
UPDATE forms SET title = :'canonicalSnapshot'::jsonb->'form'->>'title'
 WHERE id = :'mainFormId'::uuid;
UPDATE questions q SET title = s->>'title', config = s->'config'
 FROM jsonb_array_elements(:'canonicalSnapshot'::jsonb->'questions') s
 WHERE q.id = :'qMainText'::uuid AND q.form_id = :'mainFormId'::uuid AND q.id = (s->>'id')::uuid;
UPDATE options o SET label = s->>'label', value = s->>'value'
 FROM jsonb_array_elements(:'canonicalSnapshot'::jsonb->'options') s
 WHERE o.id = :'oMainSingle'::uuid AND o.question_id = :'qMainSingle'::uuid AND o.id = (s->>'id')::uuid;
```

10：備份目前 main snapshot 至 `deadlineSnapshot`，只更新新 main 的 deadline，不改任何 timestamps。取 close-before，送 10a／10b，取證後 finally 復原。若前置未完成或復原失敗，後續依賴 BLOCKED，保留證據。

```sql
UPDATE forms SET submission_deadline_at = clock_timestamp() - interval '1 hour'
 WHERE id = :'mainFormId'::uuid;
-- finally：deadlineSnapshot 是變更前的完整快照。
UPDATE forms SET submission_deadline_at =
 (:'deadlineSnapshot'::jsonb->'form'->>'submission_deadline_at')::timestamptz
 WHERE id = :'mainFormId'::uuid;
```

復原後須再比對僅上述 probe 欄位已回原值；保留合法 API status／policy／updatedAt。每個 UPDATE row count 必須恰一列，JSON snapshot 中的 UUID 必須符合本次變數；無法確認時不盲目 UPDATE。

## 並行 harness（14）

AI／人工都可用 PowerShell jobs／Python subprocess 或等價 HTTP workers；不得把開發 integration test PASS 當成本案實際 HTTP 執行。只在其他驗收寫入／regression 停止時執行，Cookie jar 本機唯讀、不將 Token 放 process arguments，response 另存非秘密證據。

1. 以唯一 application_name=`r7-barrier-<RunId>` 開 PostgreSQL session；`BEGIN; SELECT 1 FROM forms WHERE id=<concurrentFormId> AND deleted_at IS NULL FOR UPDATE;`，同 session 保留 transaction。記錄 barrier PID，查 `pg_stat_activity` 確認 transaction 及持鎖，才繼續。
2. 兩個 HTTP workers 發 14a／14b，每個一次，記 start／end UTC、status/body。14b 無 Body／Content-Type；14a body 精確同 .http。barrier 由 controller 保持，不設定固定 15 秒或其他自動 COMMIT；只能在第 3 步確認兩個 HTTP DB waiters 後發 COMMIT。
3. 在 barrier 釋放前持續唯讀輪詢 `SELECT pid,wait_event_type,wait_event,pg_blocking_pids(pid),query FROM pg_stat_activity WHERE wait_event_type='Lock' AND pid<>pg_backend_pid();`。追蹤直接或經 tuple waiter 間接被 barrier 阻擋的兩個 HTTP DB session；保存兩個 waiter 的 PID／blocking chain，排除其他連線。等待上限 30 秒；未確認兩個 HTTP DB waiters，或任一 worker 在確認前結束／transport timeout，統一記本並行案 BLOCKED，finally ROLLBACK 釋放自己的 barrier 並回收 workers，保留首次實際回應／waiter 證據，不宣稱確有重疊、不重設 fixture 重試求 PASS。
4. 只有第 3 步確認成功才 COMMIT 釋放 barrier，再等 jobs 完成（各有 45 秒 timeout），依 14 合法配對、details、14c 與完整 DB 判定。finally 釋放自己的 barrier、回收自己的 jobs／非秘密 JSON 暫存，不停止 API／DB，不刪 fixture。不得強制終止陌生 backend。若平台需語法調整，可用等價 harness 並記錄；改測試語意須先重審。

## 依序案例與實際結果

### R7-01 — 環境、Windows 原生啟動與保留資料

AI 可執行；人工可獨立確認。核對 HEAD／Git、.NET SDK、`docker compose config --quiet`／`docker compose ps`，使用既有 localhost:5432 DB，不重建 volume。先完成本次 restore／build，再以原生 PowerShell 的 `dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --launch-profile http --no-build` 重新啟動同 repo、本次 build 的 API，記錄完整路徑、Development、localhost:5000；不能以 TestServer 或 Linux 代填 Windows 啟動。

先唯讀備份 A／B 公開會員欄位（不含 hash）、其全部既有 Forms／Questions／Options 的完整 JSON 與 timestamps，特別確認 R5 軟刪除 Form 及三張 R6 fixtures 仍在。不得變更其資料。核對三個新 title 尚不存在；RunId 碰撞就整次換唯一 RunId，不重設舊 Form。

| 編號／請求 | 預期結果 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| R7-01a · `GET /health` | 200，工程回應 status=healthy | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-01b · `GET /health/ready` | 200 Healthy，原生 API 能連既有 DB | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### R7-02 — 本次全套回歸與 M2 追查

AI 可執行；人工可自行重跑，不能以 AI PASS 代填人工。在 01 本次 restore／build、重新啟動及 health/readiness 都成功後，依前述命令執行同一 build 的整個 solution 無 filter，另針對 CORS 案例重跑一次。預計 167 案，以實際探索數核對；0 tests／未執行不算 PASS。保留首次失敗 log／TRX，不用成功重試覆蓋。

M2 開發確認為「未重現、根因未明」。若再次出現 ObjectDisposedException，記錄原堆疊、平台、filter／平行執行狀態及受影響案例，該檢查 FAIL，不能直接歸因為既有偶發。全套承接精度（deadline 微秒前／等於／後、鎖後取時）、五題型／Config、獨立 Candidate 全欄位／SQL、三寫入 Save／commit rollback、雙順序 Freeze、重複 Lifecycle、Visibility／Close 競爭與 M1 有限確認；不逐案複製成 HTTP 大矩陣。

Windows 與 Linux 結果分開。預設跑 Windows；若 Windows 執行受安全政策阻擋，Windows 標 BLOCKED，才用官方相同 SDK 的隔離 Linux 暫存副本及既有 DB 跑同套，明列 Linux 結果。不得更改安全設定、清掉開發 volume；只回收本次唯一 SDK 容器。不以歷史 155/155 或本次開發 167/167 代填本案。

**AI 實際：**未執行（等待 Owner review）。

**人工實際：**未執行（等待 Owner review）。

### R7-03 — 登入保留 Owner／非 Owner

AI 可執行；人工另跑須先清 REST Client Cookies。沿用既有 active R4 A／B，密碼從本機取得，不寫入提交證據；保存 access_token 的值至本次 Cookie jar／記憶體或 .http 本機 placeholder。Login 不在 JSON 回 Token，不變更 Profile／status。

| 編號／請求 | 預期結果 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| R7-03a · `POST /api/v1/auth/login` | 200；Set-Cookie access_token，Body 無 Token | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-03b · `POST /api/v1/auth/login` | 200；B Cookie 與 A 分開保存 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### R7-04 — 建立本次三張合成 Form

AI／人工皆可。三張都由 A 擁有，Server IDs 由 response 記錄至變數；依 sortOrder 辨識 `qMainText`（0）、`qMainSingle`（2）、`qMainRating`（4）、`qConcurrentRating`（0）。main 尚未達發布完整度但保存合法；記錄 DB 全欄位快照。初始共 8 Questions／2 Options。`futureDeadline` 必須晚於實際 Server UTC，預設示例為 2036-10-04T00:00:00.123456Z，若已不在未來則於執行前同時設定兩文件／本機變數的新未來值。

| 編號／請求 | 預期結果 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| R7-04a · `POST /api/v1/forms` | 201 Draft；5 Questions／1 Option，Choice 空／Rating {} 可存；記 mainFormId／子 IDs | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-04b · `POST /api/v1/forms` | 201 Draft；1 Question；記 deletedFormId | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-04c · `POST /api/v1/forms` | 201 Draft；2 Questions／1 Option，完整可發布；記 concurrentFormId／qConcurrentRating | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### R7-05 — Draft 保存與發布要求分開

AI／人工皆可。只操作 main。兩次失敗均完整 DB 快照不變；錯誤不使用框架 ProblemDetails。`required=false` 的 Rating 仍須完整。Publish／Close 請求完全無 Body、無 Content-Type。

| 編號／請求 | 預期結果 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| R7-05a · `POST /api/v1/forms/{{mainFormId}}/publish` | 400 FORM_NOT_PUBLISHABLE；details 包含 questions[2].options / INSUFFICIENT_OPTIONS 與 questions[4].config.min / INVALID_QUESTION_CONFIG 與 questions[4].config.max / INVALID_QUESTION_CONFIG | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-05b · `POST /api/v1/forms/{{mainFormId}}/close` | 409 FORM_INVALID_STATE_TRANSITION；不可從 Draft Close | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### R7-06 — Draft Visibility 獨立更新與 no-op

AI／人工皆可。main 不完整不妨礙 policy 更新；改值只能變 Form result_visibility／updated_at，Definition／responseIdentity／子列 timestamps 原樣；同值再送的完整快照與回應 updatedAt 不變。

| 編號／請求 | 預期結果 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| R7-06a · `PATCH /api/v1/forms/{{mainFormId}}/result-visibility` | 200 Draft、respondents；只更新 policy／updatedAt | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-06b · `PATCH /api/v1/forms/{{mainFormId}}/result-visibility` | 200 no-op；全部 DB columns／timestamps 不變 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### R7-07 — 補完整、Publish Candidate 隔離與 Definition Freeze

AI／人工皆可。07a 只補一個 Single Option 與 Rating config，未提及內容保留；記 `oMainSingle`、canonical snapshot。依 §DB 操作只在本次 main 放入可 trim 的 raw title／Text config／Option label/value，建立 publish-before snapshot，才送 07b。此為觀察共用正規化是否污染 EF 資料，不是 API 允許修改已發布定義。

07b 必須 200，Publish 不得正規化回寫：raw Definition、Questions、Options、IDs、所有 createdAt／子列 updatedAt 全部與 publish-before 一樣，只變 Form status／updatedAt；FormDetail 對應保存後 DB（raw 字串亦保留）。取證後立即 finally 還原本次 raw 欄位到 canonical 值，不動 status／policy／任何 timestamp。即使失敗亦復原，記錄實際結果；07c／07d 均無資料變更。

| 編號／請求 | 預期結果 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| R7-07a · `PATCH /api/v1/forms/{{mainFormId}}` | 200 Draft；5 Questions／2 Options；Rating 0..5，原 ID／createdAt 保留 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-07b · `POST /api/v1/forms/{{mainFormId}}/publish` | 200 Published；只更新 Form status／updatedAt，raw Definition／子列不變 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-07c · `POST /api/v1/forms/{{mainFormId}}/publish` | 409 FORM_ALREADY_PUBLISHED；DB 不變 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-07d · `PATCH /api/v1/forms/{{mainFormId}}` | 409 FORM_NOT_EDITABLE；空 PATCH 也不能改 Published | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### R7-08 — Visibility 代表性欄位與傳輸錯誤（L3 優先）

AI／人工皆可。所有請求帶 A Cookie，資源有效。每筆均 `400 VALIDATION_ERROR`／正式 Envelope，error.message 與 details 每筆 path/code/message 為字串，無 ProblemDetails／415／HTML／stack trace；完整 DB snapshot 不變。數字非字串值優先；Boolean／Array 等細矩陣留 02。

08c 必須真正省略 Content-Type，不能讓工具自動補 text/plain／form-urlencoded；用 `.http` 或 curl `-H 'Content-Type:' --data-binary @<本次 JSON 檔>`。08e 有 application/json Header 但空 Body；08f 使用原始 `{`，不可由 JSON serializer 修復。

| 編號／請求 | 預期結果 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| R7-08a · `PATCH /api/v1/forms/{{mainFormId}}/result-visibility` | 400 VALIDATION_ERROR；resultVisibility / INVALID_VALUE | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-08b · `PATCH /api/v1/forms/{{mainFormId}}/result-visibility` | 400 VALIDATION_ERROR；status / INVALID_VALUE | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-08c · `PATCH /api/v1/forms/{{mainFormId}}/result-visibility` | 400 VALIDATION_ERROR；body / INVALID_VALUE，缺少 Content-Type | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-08d · `PATCH /api/v1/forms/{{mainFormId}}/result-visibility` | 400 VALIDATION_ERROR；body / INVALID_VALUE，錯誤 Content-Type | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-08e · `PATCH /api/v1/forms/{{mainFormId}}/result-visibility` | 400 VALIDATION_ERROR；body / INVALID_VALUE，空 Body | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-08f · `PATCH /api/v1/forms/{{mainFormId}}/result-visibility` | 400 VALIDATION_ERROR；body / INVALID_VALUE，損壞 JSON | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### R7-09 — Published Visibility 更新與 no-op

AI／人工皆可。不經 Publish Validator，不套 Draft 限制；核對 DB 只更新 policy／updatedAt，同值不變，status 維持 Published、Deadline 與 Definition 不變。不測未實作 Statistics。

| 編號／請求 | 預期結果 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| R7-09a · `PATCH /api/v1/forms/{{mainFormId}}/result-visibility` | 200 Published、public；只更新 policy／updatedAt | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-09b · `PATCH /api/v1/forms/{{mainFormId}}/result-visibility` | 200 no-op；完整快照不變 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### R7-10 — Deadline 不自動 Close、Close 不重驗及終止狀態

AI／人工皆可。先備份本次 main 的實際 DB deadline；依 §DB 操作暫設為過去，不改 updatedAt，快照作 close-before。10a 證明過期仍 Published；10b 不帶 Body／Content-Type，必須成功且只變 status／updatedAt，過期 deadline、Definition／子列不變。取證後 finally 僅還原原 deadline（不改任何 timestamps）；不得對保留 R5／R6 或其他 Form 做此操作。餘下三筆不改資料；Closed 不可 Reopen。

| 編號／請求 | 預期結果 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| R7-10a · `GET /api/v1/forms/{{mainFormId}}` | 200；deadline 已過，但 status=published | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-10b · `POST /api/v1/forms/{{mainFormId}}/close` | 200 Closed；只改 status／updatedAt，不重跑 Publish deadline 驗證 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-10c · `POST /api/v1/forms/{{mainFormId}}/close` | 409 FORM_ALREADY_CLOSED | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-10d · `POST /api/v1/forms/{{mainFormId}}/publish` | 409 FORM_INVALID_STATE_TRANSITION | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-10e · `PATCH /api/v1/forms/{{mainFormId}}` | 409 FORM_NOT_EDITABLE；Closed Freeze | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### R7-11 — Closed Visibility 更新與 no-op

AI／人工皆可。核對只有 result_visibility／updated_at 改動，responseIdentity／status／Definition／子列不變；重送同值完全不變。三狀態與三個合法值至此均走過主流程，不展開 3×3 矩陣。

| 編號／請求 | 預期結果 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| R7-11a · `PATCH /api/v1/forms/{{mainFormId}}/result-visibility` | 200 Closed、owner_only；只改 policy／updatedAt | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-11b · `PATCH /api/v1/forms/{{mainFormId}}/result-visibility` | 200 no-op；完整快照不變 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### R7-12 — 權限、存在性與錯誤優先序

AI／人工皆可。先以唯讀 DB 確認 missingFormId 不存在。所有失敗不寫資料。12a 非 Owner 優先於 Closed 狀態；12b Body 已可解析，但欄位型別驗證在 Owner 之後；12c Authentication 優先 UUID；12d UUID 可解析性先於資源；12e 合法 Body 才查不存在資源。disabled／R7 錯誤優先序由 02 的 `R7_errors_follow_auth_parse_existence_owner_and_domain_order` 承接；expired token 只有共用 Authentication 的 `AuthUserEndpointsTests.Missing_invalid_expired_and_unknown_user_tokens_are_distinguished`（GET /users/me），不宣稱是三支 R7 expired 矩陣。無須暫改保留會員 status。

| 編號／請求 | 預期結果 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| R7-12a · `POST /api/v1/forms/{{mainFormId}}/publish` | 403 FORM_ACCESS_DENIED，不能洩漏／先回 Closed 狀態錯誤 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-12b · `PATCH /api/v1/forms/{{mainFormId}}/result-visibility` | 403 FORM_ACCESS_DENIED，先於非字串欄位 400 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-12c · `POST /api/v1/forms/bad-id/close` | 401 AUTH_REQUIRED，先於 UUID 400 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-12d · `POST /api/v1/forms/bad-id/close` | 400 VALIDATION_ERROR；formId / INVALID_VALUE | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-12e · `PATCH /api/v1/forms/{{missingFormId}}/result-visibility` | 404 FORM_NOT_FOUND | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### R7-13 — Owner 存取已刪除 Form（優先）

AI／人工皆可。只軟刪除 04b 本次新建 Form，不碰 R5 保留 deleted fixture。Owner 三端點皆 404，且存在性優先於 Draft／Close Lifecycle；Visibility 傳合法 Header／Body，避免先被 parsing 階段拒絕。Delete 後子列／ID／createdAt 保留，後續失敗完全無寫入。

| 編號／請求 | 預期結果 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| R7-13a · `DELETE /api/v1/forms/{{deletedFormId}}` | 204、無 Body；只設 deletedAt／updatedAt，status Draft／子列保留 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-13b · `POST /api/v1/forms/{{deletedFormId}}/publish` | 404 FORM_NOT_FOUND，即使 Owner | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-13c · `POST /api/v1/forms/{{deletedFormId}}/close` | 404 FORM_NOT_FOUND，先於 Draft 狀態錯誤 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-13d · `PATCH /api/v1/forms/{{deletedFormId}}/result-visibility` | 404 FORM_NOT_FOUND，即使 Owner | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### R7-14 — 真正 PATCH／Publish 並行與 Freeze

AI 可依 §並行 harness 執行；人工須用同樣 barrier 獨立操作，不能順序 Send。只用尚未修改的 04c 新 Form。14a 將 Rating 設為 Draft 合法但不完整的 `{}`；14b 不帶 Body 的 Publish 同時發送。外部 DB row-lock barrier 持鎖，觀察至少兩個 lock waiters 才釋放，不用固定 sleep 判定順序。

允許且只允許一個可解釋結果：PATCH 先取得鎖 → 14a=200、14b=400 FORM_NOT_PUBLISHABLE，details questions[0].config.min／max / INVALID_QUESTION_CONFIG，最終 Draft／Rating {}；Publish 先取得鎖 → 14b=200、14a=409 FORM_NOT_EDITABLE，最終 Published／Rating 0..5。成功 writer 的完整保存狀態即 14c／DB；PATCH 先時只允許 Form updated_at 與該 Rating config／updated_at 改變，Publish 先時只允許 Form status／updated_at 改變，其餘欄位／子列 timestamps 原樣。若未觀察兩個 waiter，並行案 BLOCKED，不能因 HTTP 看似合理就 PASS。另一種先後及 Visibility／Lifecycle 故障／競爭由 02 確定性回歸覆蓋，不要求本案強制指定勝方。

兩個 template 每次各送一次，只在 harness 內發送，不先手動測過。保留實際最終狀態，不重設 fixture 求另一種結果。

| 編號／請求 | 預期結果 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| R7-14a · `PATCH /api/v1/forms/{{concurrentFormId}}` | PARALLEL：200（PATCH 先）或 409 FORM_NOT_EDITABLE（Publish 先） | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-14b · `POST /api/v1/forms/{{concurrentFormId}}/publish` | PARALLEL：400 FORM_NOT_PUBLISHABLE（PATCH 先）或 200（Publish 先） | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| R7-14c · `GET /api/v1/forms/{{concurrentFormId}}` | 200；等於成功 writer 的 response，符合上方唯一合法先後 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### R7-15 — 人工檢視、暫存復原與秘密清理

AI 可執行復原／清理／唯讀比對；人工以 REST Client／DBeaver 獨立檢視 07／08／10／13／14 的 Error Envelope、Freeze 與 DB timestamps，僅記人工觀察結果，不以 AI 或開發 PASS 代填。

即使中途失敗，也完成 07 raw 欄位及 10 deadline 的 finally 復原，記錄成功／失敗；不復原合法 Lifecycle／Visibility／競爭寫入。既有會員／R4／R5／R6 snapshots 必須等於 01。新三張保留，main 正常為 Closed／owner_only／5 Questions／2 Options；deleted fixture Draft、已軟刪除／1 Question；concurrent 按 14 真實結果保留 Draft 或 Published／2 Questions／1 Option。不得 hard-delete／TRUNCATE／清 volume；若流程未完成，記錄現況，不冒稱上述最終狀態。

刪除僅本次明確列冊的 TEMP Cookie jar／登入密碼 JSON／秘密 response；每一絕對路徑先驗證在 TEMP 且檔名含本次 RunId，再用 Remove-Item -LiteralPath，不遞迴清理。清 REST Client Cookie／登入 response tab、記憶體 Token；兩份文件密碼／JWT 還原 placeholders，非秘密證據與 fixtures 保留。若用 Linux fallback，只移除核對名稱／ID 的本次 SDK 容器。最後 git diff --check、秘密掃描、Git 狀態與提交；API／PostgreSQL 保留。

**AI 實際：**未執行（等待 Owner review）。

**人工實際：**未執行（等待 Owner review）。

## 交付與停止點

Owner 已核准 `28cb8e2` 並授權本次三項修訂後直接執行，開始時所有 AI／人工未執行；不使用開發證據代填。若執行發現產品缺陷，先回報具體案例／影響，不修改業務程式或改預期求 PASS。未來執行以 PASS／FAIL／BLOCKED／未執行逐項記錄，Windows／Linux 與人工分開，交付「驗收結果待 Owner 確認」後停止；本次執行交付**驗收結果待 Owner 確認**。R7 尚未 accepted，R8／G1 未開始。
