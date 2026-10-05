# R8 驗收測試：Submit／Response／Answer

2026-10-05；**本次 AI 驗收 BLOCKED，驗收結果待 Owner review**。RunId `r8-acceptance-20261005-01`，HEAD `0bb78428226104890b8c9ec73e7bb6ae2fbf68f1`；獨立 review 未發現具體 R8 產品／計畫缺陷，source/tests 與 `d3f75b3` 相同、54 請求與核准 `8680ccc` 相同。本次 build 0 warnings/errors、native API 01a PASS；01b 首次 503 Unhealthy FAIL，PostgreSQL 實際 host binding 空。54 HTTP 為 **1 PASS／1 FAIL／52 BLOCKED**，非 HTTP 01 FAIL、02／13／14 BLOCKED；本次全套與後續驗收未執行。原資料完整相同、無新增 fixture；人工全部未執行，未 accepted、未進 R9。詳見 [獨立 review／本次執行報告](../implementation/R8_Independent_Review_Acceptance_Report.md)及[本次結果](../../artifacts/r8-acceptance/r8-acceptance-20261005-01/results.json)。

**以下 2026-10-04 開頭段落保留為第二次歷史結果；表格 AI 欄為 2026-10-05 本次結果。**

2026-10-04；**Execute 補驗：正式補驗完成，待 Owner review**。Owner 持續授權核准計畫 `8680cccd17e98646a6f257757dfa65654c039153`，本次新 RunId `r8-acceptance-20261004-02`：Windows restore/build 0 warnings/errors、同版 API health/readiness PASS、當前全套實際 280/280 PASS；54 HTTP requests 與 01／02／13／14 非 HTTP 檢查全部 PASS。首次 run `r8-acceptance-20261004-01` 的 1 PASS／1 FAIL／52 BLOCKED 與原始證據保留，沒有覆寫。詳見 [正式補驗報告](../implementation/R8_Acceptance_Reexecution_Report.md)及[本次逐案證據](../../artifacts/r8-acceptance/r8-acceptance-20261004-02/results.json)。本輪採 AI 執行驗收＋Owner 審核證據；人工全部未執行，依 Owner 指示不作為本輪 accepted 必要條件。仍須 Owner 明確確認 R8 accepted，才可進入 R9。

Companion：[R8_Manual_Tests.http](R8_Manual_Tests.http)。業務版本 `14aac3036c25f5fefa756f2e4133009680369f25`；開發候選／cleanup 修正基準 `d3f75b37623f5f774d8dabe5817c4488a96eb368`。Owner 已確認 A／B code review 通過，並授權 cleanup 局部收尾及 Prepare；本次開發自測 build 0 warnings/errors、R8 affected 34/34 是背景，不能代填驗收。本版若再修業務／測試語意，受影響計畫需重新 review。

範圍只有端點 15 `POST /api/v1/forms/{formId}/responses` 與已實作依賴／Delete 局部鎖。正式依據：01 §3.3／SUB-01～05／CON-01、02、04；02 §10～11／15；03a §2／5～6；03b §6.1／7.1／8；03c §3.1～3.2／4.1～4.7；04 §4／6／9／11；05 §8～12／13。不測未實作的 R9 Latest／Count、R10 Statistics、G1 或部署。

## 變數、順序與保留資料

本次 AI 使用兩檔相同新 RunId `r8-acceptance-20261005-01`；readiness 失敗，沒有建立本次 fixtures。2026-10-04 五張 fixtures 與所有歷史結果保留，不能重放其 single／已刪除資料。獨立 AI／人工 run 必須另取唯一 RunId。使用既有兩位 Active 合成會員：A 是新 Form Owner，B 是另一會員；從本機取得其 email／合成 password，不能照抄舊執行 JWT、Cookie、已建立的 Form／Question IDs 或舊結果。不要註冊新會員／改舊會員 Profile／status。

```powershell
$RunId = 'r8-acceptance-20261005-01'
$ApiBase = 'http://localhost:5000'
$FutureDeadline = '2036-10-04T00:00:00.123456Z'
$MemberAEmail = '<ENTER_RETAINED_ACTIVE_OWNER_EMAIL_LOCALLY>'
$MemberBEmail = '<ENTER_DIFFERENT_RETAINED_ACTIVE_MEMBER_EMAIL_LOCALLY>'
$TestPassword = '<ENTER_LOCAL_SYNTHETIC_PASSWORD>'
$DbName = '<POSTGRES_DB from .env>'
$DbUser = '<POSTGRES_USER from .env>'
$EvidenceRoot = Join-Path $PWD "artifacts/r8-acceptance/$RunId"
$MemberACookieJar = Join-Path $env:TEMP "custom-form-r8-a-$RunId.cookies"
$MemberBCookieJar = Join-Path $env:TEMP "custom-form-r8-b-$RunId.cookies"
```

HTTP 主體、身份與案例編號以 companion 為唯一請求樣本；可用等價 curl.exe／HTTP harness，但要記 case ID、平台與實際請求，保留原始非秘密回應。REST Client 先 Clear All Cookies，每一 request 使用 `@no-cookie-jar`；僅複製 access_token value 到本機 token 變數，不複製整個 Set-Cookie。執行後把 credential／token／server IDs 還原為 placeholders，不將本機秘密或實際執行結果混入未執行文件。

依 01→02→03→04→05…→14。任何前置失败使依賴 case BLOCKED；保留首次 log／TRX／response，不重設舊 fixture 或重跑覆蓋失敗。13 中每對 worker 必須同時透過 harness，不能把 .http 的檔案順序當並行證據。

| 新 fixture（皆 Owner A） | 模式／Definition | 04 後狀態與用途 |
| --- | --- | --- |
| mainFormId | single、anonymous；Required Text minLength=2／maxLength=100，Number min=0／max=1e30，Single 1 Option，Multiple 2 Options，Rating 1～5 | Published；所有代表錯誤先驗零新增，再 A 五題型／B omission，最後 Close／Delete |
| foreignFormId | single、1 Optional Text | **保持 Draft**；真實 qForeign 跨 Form ID、B 的 Submit 409 與 Detail 404 |
| multipleFormId | multiple、identified；Optional Text／Number | Published；完整歷史與 omission、13 multiple race，再 Submit／Delete race |
| deadlineFormId | single、1 Optional Text；未來 deadline | Published；10 A 先提交、expired probe／finally 復原，再 13 B Submit／A Close race |
| singleRaceFormId | single、1 Optional Text | Published；13 兩個 A worker 競爭一次性 |

所有 id 取本次 server 回應，以 Question type／sortOrder、Option sortOrder 配對變數，不用 Request Array index 猜 ID。qForeign 必須是 04c **已保存於另一張 Form 的真實 ID**，不是任意 UUID。每張保留完整 Definition／Question／Option snapshot，04 後總計 **5 Forms／10 Questions／3 Options／0 Responses／0 Answers**。

## 01 — 環境、本次 build／Windows 原生啟動與既有資料 snapshot

目的：保留既有 localhost PostgreSQL／volume，驗證本次版本真正啟動。先記 HEAD／Git 狀態／SDK；兩份既有未追蹤 ZIP 可保留，不提交／刪除。確認追蹤程式與核准候選相符。Compose config／ps、非秘密 DB 連線資訊可記，不能輸出 password、User hash、JWT signing key 或完整有密碼的 connection string。

```powershell
git status --short
dotnet --info
docker compose config --quiet
docker compose ps
dotnet restore CustomFormService.sln --disable-parallel --verbosity minimal
dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1 -p:UseSharedCompilation=false
# 在適當終端以本次 build 重啟此 repo 的 API；只處理已確認屬此 repo 的 API process。
dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --launch-profile http --no-build
```

build 必須在 startup 前；API 必須是此 repo／此 build、Development、localhost:5000，記 process／路徑／UTC 啟動時間與 DLL SHA256。完成 01a／01b 後才跑 02；不能用 TestServer 代填原生啟動。Windows 載入被政策封鎖時留原堆疊、01／依賴 HTTP 為 BLOCKED；02 可沿 repo 既有唯讀 Linux SDK fallback 跑同候選測試，但必須記實際命令／平台，不能改稱 Windows 原生 PASS，不改安全政策／Schema／volume。

用現有 DB 的 psql（`-X -v ON_ERROR_STOP=1`；或 DBeaver 同步人工唯讀）先保存下列全表非秘密 baseline 到本次 evidence，記所有原有 IDs／counts。03 登入後确认 A/B 不同且 active；04 前確認本 RunId title 尚不存在、missingFormId 不存在；有碰撞便整次換 RunId，不覆寫舊 Form。準備階段不執行這些 SQL。

```sql
SELECT jsonb_build_object(
 'users', COALESCE((SELECT jsonb_agg(jsonb_build_object(
   'id',id,'email',email,'display_name',display_name,'avatar_url',avatar_url,
   'status',status,'created_at',created_at,'updated_at',updated_at) ORDER BY id)
   FROM users),'[]'::jsonb),
 'forms', COALESCE((SELECT jsonb_agg(to_jsonb(f) ORDER BY id) FROM forms f),'[]'::jsonb),
 'questions', COALESCE((SELECT jsonb_agg(to_jsonb(q) ORDER BY id) FROM questions q),'[]'::jsonb),
 'options', COALESCE((SELECT jsonb_agg(to_jsonb(o) ORDER BY id) FROM options o),'[]'::jsonb),
 'responses', COALESCE((SELECT jsonb_agg(to_jsonb(r) ORDER BY id) FROM responses r),'[]'::jsonb),
 'answers', COALESCE((SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM answers a),'[]'::jsonb));
-- psql runId / memberAId / missingFormId 先驗格式，memberAId 從 03a 取得。
SELECT COUNT(*) FROM forms
 WHERE user_id=:'memberAId'::uuid AND title LIKE '%' || :'runId' || '%';
SELECT COUNT(*) FROM forms WHERE id=:'missingFormId'::uuid;
-- 兩結果都須為 0，才可建立本次 fixtures。
```

| 非 HTTP 檢查 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| 01 build／原生啟動／baseline | restore/build exit 0，0 errors；此版本 Windows native API；DB healthy；保留原有 IDs／完整列 | FAIL：本次 restore/build 0 errors、native startup 成功；01b readiness 503；唯讀舊資料相同 | 未執行（等待 Owner review） |


### 01 HTTP readiness

| Case／目的 | Companion request | 身分 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 01a — 本次原生 API health | `GET /health` | 無 Cookie | 200；status=healthy | PASS：本次首次 HTTP 200；status=healthy | 未執行（等待 Owner review） |
| 01b — 本次原生 API readiness | `GET /health/ready` | 無 Cookie | 200；Healthy | FAIL：本次首次 HTTP 503 Unhealthy；PostgreSQL host binding 空；原預期 200 Healthy | 未執行（等待 Owner review） |


## 02 — 同版完整回歸、Clock／原子性／cleanup 證據（非 HTTP）

目的：重新執行當前版本，不能沿用 B 的 277 或收尾 34 開發自測代填。本次新增 3 cleanup cases，預計 full **280**，以實際探索／TRX 核對；0 tests／未執行不算 PASS。首次全套有失敗即記 FAIL 與堆疊，不以「既有偶發」直接略過。

```powershell
dotnet test CustomFormService.sln --no-build --no-restore --configuration Debug -m:1 --logger "console;verbosity=normal" --logger "trx;LogFileName=r8-acceptance-full-first.trx" --results-directory "artifacts/r8-acceptance/$RunId"
```

核對 TRX 的以下實際名稱、DataRows／StdOut；共用 Auth expired 是 users/me 的共用 middleware 回歸，不宣稱 Submit 專屬 expired HTTP 已测。故障／可控制 Clock 是本次 **TestServer＋實際 PostgreSQL** 的自動驗證，不是原生 HTTP 故障注入，不新增 production Clock／重試 hook。

| 保證 | 當前 source 案例／證據 |
| --- | --- |
| cleanup 继续觀察／最後回報 | R8_request_cleanup_observes_remaining_requests_and_reports_all_failures（false／true）；R8_request_cleanup_only_ignores_actual_task_cancellation |
| 鎖後唯一取時、deadline 前／等於／後、判定後跨 deadline | R8_deadline_boundary_uses_one_sample_and_does_not_close_form；R8_clock_samples_after_observed_lock_wait_crossing_deadline；R8_clock_does_not_resample_when_save_and_commit_cross_deadline；all-types 取樣／微秒讀回 |
| EF 保存實際 JSONB／Receipt | R8_submit_all_types_returns_persisted_sorted_receipt_without_changing_definition；R8_number_round_trip_uses_actual_EF_JSONB_value_without_precision_loss；R8_receipt_breaks_equal_question_sort_order_by_question_id |
| single／multiple、四方向 lifecycle | R8_concurrent_submissions_observe_waiter_and_enforce_mode；R8_submit_close_delete_share_lock_and_read_state_after_wait；R8_multiple_preserves_history_and_omits_optional_answers_from_new_response |
| 真實部分 INSERT／precommit SQL 故障 | R8_submit_precommit_fault_rolls_back_actual_writes_and_single_waiter_can_succeed：Response=1／Answers=1 或 5，失敗 ID 零殘留，single waiter 201 |
| Delete UPDATE／precommit 故障 | R8_delete_precommit_fault_rolls_back_update_and_waiting_submit_can_succeed：交易內 deleted_at 非 null，回滾原狀，Submit waiter 201 |
| A 的 Required／null／Integer／Text／精確 Number／生命週期 | SubmissionAnswerValidatorTests，含 Nonempty_text_shorter_than_minLength_returns_invalid_value、Rating 5／5.0／5e0／超大值、NUL／孤立 surrogate／合法 pair、JsonDocument 釋放後 owned values |
| 既有回歸 | 原 167（含 Auth／R5～R7）；Delete 三狀態／Owner／重試、R7 writers／Delete 競爭 |

故障只在已確認 COMMIT 發送前；未驗證 COMMIT 已送達但結果未知時零新增。A JSONB SELECT probes 只證相容性／數值等值，不能替代 EF／Receipt round-trip。02 後把原有 baseline 再比對，開發測試暫存 fixtures 應清完；若發現舊列改變，先記 FAIL／停止依賴寫入。

| 非 HTTP 檢查 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| 02 full／名稱核對／當次故障與 Clock／資料比較 | 當前全套（預計 280）全部 PASS、0 skipped；每項對应 TRX；原有資料不變 | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |

## DB 與 Receipt 核對方法（04～14）

只操作本次已確認 Owner A＋RunId＋server ID 的新 fixtures；psql UUID 必須先驗，所有 UPDATE 恰一列。以下單 Form 查詢可用於每案前後完整 snapshot；counts／存在判定另列，不能只看 HTTP status。

```sql
SELECT jsonb_build_object(
 'form',(SELECT to_jsonb(f) FROM forms f WHERE id=:'formId'::uuid),
 'questions',COALESCE((SELECT jsonb_agg(to_jsonb(q) ORDER BY id)
   FROM questions q WHERE form_id=:'formId'::uuid),'[]'::jsonb),
 'options',COALESCE((SELECT jsonb_agg(to_jsonb(o) ORDER BY o.id)
   FROM options o JOIN questions q ON q.id=o.question_id
   WHERE q.form_id=:'formId'::uuid),'[]'::jsonb),
 'responses',COALESCE((SELECT jsonb_agg(to_jsonb(r) ORDER BY id)
   FROM responses r WHERE form_id=:'formId'::uuid),'[]'::jsonb),
 'answers',COALESCE((SELECT jsonb_agg(to_jsonb(a) ORDER BY a.id)
   FROM answers a JOIN responses r ON r.id=a.response_id
   WHERE r.form_id=:'formId'::uuid),'[]'::jsonb));
SELECT
 (SELECT COUNT(*) FROM responses WHERE form_id=:'formId'::uuid) AS responses,
 (SELECT COUNT(*) FROM answers a JOIN responses r ON r.id=a.response_id
   WHERE r.form_id=:'formId'::uuid) AS answers;
-- 06c：兩張 Form 存在，真實 Question 屬 foreign；不是不存在 UUID。
SELECT id,form_id FROM questions WHERE id=:'qForeign'::uuid;
-- Receipt 對照：responseId 取本次 201 body，ID／userId／timestamp 等於已保存值。
SELECT id,form_id,user_id,submitted_at FROM responses WHERE id=:'responseId'::uuid;
SELECT a.question_id,a.value,q.sort_order
 FROM answers a JOIN questions q ON q.id=a.question_id
 WHERE a.response_id=:'responseId'::uuid ORDER BY q.sort_order,a.question_id;
-- 08a Number 精確等值，不轉 double/decimal：
SELECT value = '1.00000000000000000000000000001'::jsonb AS exact_equal
 FROM answers WHERE response_id=:'mainResponseAId'::uuid AND question_id=:'qMainNumber'::uuid;
-- 09a token 等值；Receipt 可按實際 JSONB 顯示 1，不保證原始 1e0：
SELECT value = '1e0'::jsonb AS equal_numeric
 FROM answers WHERE response_id=:'multipleResponse1Id'::uuid AND question_id=:'qMultipleNumber'::uuid;
```

Receipt root 僅 id／formId／submittedAt／answers；每 Answer 僅 questionId／value。只回實際 Answers，排序 Question sortOrder ASC、id ASC；文字比較 decoded Code Point sequence、Option IDs／Array 次序不變、Number 比保存後實際 JSONB 數值（不承諾 Request 原 token）、submittedAt 比 DB UTC timestamp，允許既有微秒保存格式，不比較 datetime 字串表面差異。DB user_id 必須是真正當次 A/B，Receipt 不回身分／Answer ID。08／09 的 Definition（包括 Form／Question／Option timestamps）完整不變。

每個 05～07 失敗後保存非秘密 error body／details／整張 Form snapshot，與案前相比 **Response／Answers 零新增、全部原列不變**。06c 當時 main／foreign 都是 0／0，必須查兩張，不拿後來成功資料混算。08b、10b、11b、12 及 13 的 rejected Submit 是相對案前零新增；已有歷史不應刪除。

## 03 — 登入既有 Active 會員

目的：確認實際 A/B 身分，拿本次 Cookie；不改舊會員任何欄位。01／02 成功後執行，A/B 必須不同。

前置：01／02 與此前依賴案例成功；實際請求 Body／Content-Type 逐案見 companion 03，不重新序列化精確 numeric token。

| Case／目的 | Companion request | 身分 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 03a — 登入現有 Active Owner A | `POST /api/v1/auth/login` | 無 Cookie | 200 User＋Cookie | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 03b — 登入另一位 Active User B | `POST /api/v1/auth/login` | 無 Cookie | 200 User＋Cookie | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |

- **03a**：將 id 記為 memberAId，只抄 access_token 的 value 到 memberAToken；不記錄可用 JWT。
- **03b**：將 id 記為 memberBId（須不同於 A），只抄 token value 到 memberBToken。

## 04 — 建立本次五張 fixtures／Publish

目的：只用本次新 server IDs；不得 Patch 已有 R4～R7 Form。每次 201 配對 IDs 並查 DB，Publish 無 Body／Content-Type。04i 後查表確認新總數與全部零 Responses／Answers。

前置：01／02 與此前依賴案例成功；實際請求 Body／Content-Type 逐案見 companion 04，不重新序列化精確 numeric token。

| Case／目的 | Companion request | 身分 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 04a — 建立 main single／五題型 | `POST /api/v1/forms` | A | 201 Draft FormDetail；5 Questions／3 Options | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 04b — Publish main | `POST /api/v1/forms/{{mainFormId}}/publish` | A | 200；Published | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 04c — 建立另一張 foreign Draft（保持 Draft） | `POST /api/v1/forms` | A | 201 Draft；1 真實 Question | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 04d — 建立 multiple／identified | `POST /api/v1/forms` | A | 201 Draft；2 Optional Questions | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 04e — Publish multiple | `POST /api/v1/forms/{{multipleFormId}}/publish` | A | 200；Published | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 04f — 建立 deadline single | `POST /api/v1/forms` | A | 201 Draft；futureDeadline 為未來 UTC Z | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 04g — Publish deadline | `POST /api/v1/forms/{{deadlineFormId}}/publish` | A | 200；Published | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 04h — 建立 single race | `POST /api/v1/forms` | A | 201 Draft；1 Optional Question | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 04i — Publish single race | `POST /api/v1/forms/{{singleRaceFormId}}/publish` | A | 200；Published | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |

- **04a**：保存 mainFormId；以 type／sortOrder 配對 qMainText／Number／Single／Multiple／Rating、oMainSingle、oMainMultipleOne／Two；記完整 Definition snapshot。
- **04b**：無 Body／Content-Type；取新的 main-before-submit snapshot（包含 Publish 的 timestamps）。
- **04c**：保存 foreignFormId／qForeign；DB 確認 qForeign.form_id=foreignFormId 且不等於 mainFormId；不得改用隨機不存在 UUID。
- **04d**：保存 multipleFormId／qMultipleText／qMultipleNumber。
- **04f**：保存 deadlineFormId；03、04 的 fixtures 為本次 RunId，不操作舊 Form。
- **04h**：保存 singleRaceFormId／qSingleRaceText。
- **04i**：完成後新 fixtures 共 5 Forms／10 Questions／3 Options；全部 Response／Answer 為 0。

## 05 — Auth／parse／Content-Type／不存在資源

目的：驗證正式 error envelope 與 priority。05e 必須是 application/json＋0-byte Body，與 {} 或錯誤 Content-Type 分開；全部拒絕後 main 的 Definition／0 Responses／0 Answers 不變。

前置：01／02 與此前依賴案例成功；實際請求 Body／Content-Type 逐案見 companion 05，不重新序列化精確 numeric token。

| Case／目的 | Companion request | 身分 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 05a — 未登入＋壞 JSON | `POST /api/v1/forms/{{mainFormId}}/responses` | 無 Cookie | 401 AUTH_REQUIRED；Auth 優先 | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 05b — 無效 formId | `POST /api/v1/forms/not-a-uuid/responses` | B | 400 VALIDATION_ERROR；formId INVALID_VALUE | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 05c — 不存在 Form＋未知／唯讀欄位 | `POST /api/v1/forms/{{missingFormId}}/responses` | B | 400 VALIDATION_ERROR；userId INVALID_VALUE | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 05d — 不存在 Form＋合法 Envelope | `POST /api/v1/forms/{{missingFormId}}/responses` | B | 404 FORM_NOT_FOUND | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 05e — application/json＋空 Body | `POST /api/v1/forms/{{mainFormId}}/responses` | B | 400 VALIDATION_ERROR；body INVALID_VALUE | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 05f — 錯誤 Content-Type | `POST /api/v1/forms/{{mainFormId}}/responses` | B | 400 VALIDATION_ERROR；body INVALID_VALUE | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |

- **05a**：main 與所有新 Forms 的 Response／Answers 零新增。
- **05c**：先確認 missingFormId 在 DB 不存在；parse／Envelope 優先於 404。
- **05e**：Body 必須 0 bytes；不可代成 {}、null 或只輸入缺少 Content-Type。

## 06 — Draft 規則與另一張 Form 的真實 Question

Owner 指定的三個重點之一：Active 非 Owner B Submit 他人 Draft 是 409 FORM_NOT_SUBMITTABLE；同 B Detail 是 404 FORM_NOT_FOUND。另一重點使用 qForeign：先以 DB 確認真實 Question 存在且屬 foreignFormId，再提交到 Published main，必須 400 VALIDATION_ERROR／QUESTION_NOT_FOUND；main／foreign 都零新增，不能用隨機不存在 UUID 代替。

前置：01／02 與此前依賴案例成功；實際請求 Body／Content-Type 逐案見 companion 06，不重新序列化精確 numeric token。

| Case／目的 | Companion request | 身分 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 06a — 非 Owner Submit 他人 Draft＋缺 answers | `POST /api/v1/forms/{{foreignFormId}}/responses` | B | 409 FORM_NOT_SUBMITTABLE | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 06b — 相同 B 讀他人 Draft Detail | `GET /api/v1/forms/{{foreignFormId}}` | B | 404 FORM_NOT_FOUND | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 06c — Main 使用另一張 Form 的真實 Question ID | `POST /api/v1/forms/{{mainFormId}}/responses` | A | 400 VALIDATION_ERROR；answers[0].questionId QUESTION_NOT_FOUND | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |

- **06a**：必須 Active B≠Owner A；foreign 與 main Response／Answers 仍全 0。
- **06b**：與 06a 區分：Detail 有 Draft 隱藏，Submit 正式 Published gate 回 409。
- **06c**：使用 04c 真實 qForeign；Question 歸屬優先於 main 的 Required 缺題。DB main／foreign Response／Answers 均 0。

## 07 — 代表性 Answer 錯誤與原子零新增

目的：Required 缺題／合法空答案／明確 null、非空 minLength、Number JSON type、Integer predicate、Option 歸屬／重複，以及 NUL 正式 400。每案 snapshot 不變，最后仍為 main 0 Responses／0 Answers；不複製完整 UUID／Unicode／range 矩陣。

前置：01／02 與此前依賴案例成功；實際請求 Body／Content-Type 逐案見 companion 07，不重新序列化精確 numeric token。

| Case／目的 | Companion request | 身分 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 07a — Published 缺 answers | `POST /api/v1/forms/{{mainFormId}}/responses` | A | 400 VALIDATION_ERROR；answers REQUIRED_FIELD | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 07b — Required 完全缺題 | `POST /api/v1/forms/{{mainFormId}}/responses` | A | 400 VALIDATION_ERROR；questions.&lt;qMainText&gt; REQUIRED_FIELD | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 07c — Required 合法空文字 | `POST /api/v1/forms/{{mainFormId}}/responses` | A | 400 VALIDATION_ERROR；answers[0].value REQUIRED_FIELD | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 07d — 明確 value:null | `POST /api/v1/forms/{{mainFormId}}/responses` | A | 400 VALIDATION_ERROR；answers[0].value INVALID_VALUE | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 07e — 非空 Text 短於 minLength | `POST /api/v1/forms/{{mainFormId}}/responses` | A | 400 VALIDATION_ERROR；answers[0].value INVALID_VALUE | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 07f — Number String 拒絕 | `POST /api/v1/forms/{{mainFormId}}/responses` | A | 400 VALIDATION_ERROR；answers[1].value INVALID_VALUE | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 07g — Rating 5.0 沿用 Integer 判定 | `POST /api/v1/forms/{{mainFormId}}/responses` | A | 400 VALIDATION_ERROR；answers[1].value INVALID_VALUE | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 07h — Option 屬於另一個 Question | `POST /api/v1/forms/{{mainFormId}}/responses` | A | 400 VALIDATION_ERROR；answers[1].value OPTION_NOT_FOUND | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 07i — Multiple Choice 重複 ID | `POST /api/v1/forms/{{mainFormId}}/responses` | A | 400 VALIDATION_ERROR；answers[1].value[1] INVALID_VALUE | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 07j — NUL 受控拒絕 | `POST /api/v1/forms/{{mainFormId}}/responses` | A | 400 VALIDATION_ERROR；answers[0].value INVALID_VALUE | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |

- **07e**：trim 後 1 Code Point、不是 omission。
- **07g**：保留 raw 5.0 token；08a 的 5 正常接受，5e0／超大值由 02 同版自動測試涵蓋。
- **07j**：不接受 500、不刪除或替換字元；孤立 surrogate／合法 pair 完整細部由 02 同版 tests，08a 合法 pair 實際保存。

## 08 — single 全題型 Receipt／不同 User／omission

目的：Owner A 正常回答全題型、single 重送順序、B 可以各自一次。核對真 DB 保存值與 Receipt／排序／timestamp；anonymous 仍保存 A/B user_id。08c 完成 main 共 2 Responses／6 Answers。

前置：01／02 與此前依賴案例成功；實際請求 Body／Content-Type 逐案見 companion 08，不重新序列化精確 numeric token。

| Case／目的 | Companion request | 身分 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 08a — Owner 全題型反序 Submit | `POST /api/v1/forms/{{mainFormId}}/responses` | A | 201 ResponseReceipt；排序 Text／Number／Single／Multiple／Rating | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 08b — single 重送＋缺 answers | `POST /api/v1/forms/{{mainFormId}}/responses` | A | 409 RESPONSE_ALREADY_SUBMITTED | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 08c — 非 Owner B 同一 single Form 可一次；Optional omission | `POST /api/v1/forms/{{mainFormId}}/responses` | B | 201；Receipt 只有 qMainText="B answer" | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |

- **08a**：保存 mainResponseAId。Root 僅 id／formId／submittedAt／answers；Answer 僅 questionId／value。DB 1 Response／5 Answers；user_id=A；Definition／timestamps 不變；比對保存數值及 timestamp。
- **08b**：Response 限制先於 answers；DB 仍 1／5。
- **08c**：保存 mainResponseBId；不補 null。DB main 2 Responses／6 Answers，內部 user_id=B（anonymous 仍登入）。

08a decoded Text 預期為 `Hello 😀`；Number 精確等於 `1.00000000000000000000000000001`；Single=oMainSingle；Multiple 按送入 `[oMainMultipleTwo,oMainMultipleOne]` 次序；Rating=5。Receipt 的 answers 次序依 Question，不影響 Multiple value 的 Array 次序。

## 09 — multiple 歷史與最新整份 omission

目的：identified 與 anonymous 一樣只回 Receipt 欄位，不公開 userId；第二份全部 optional 未作答，Receipt 空 Array，旧 Answer 不補回。09b 後 multiple 2 Responses／2 Answers；用 DB 按各 Response ID 核對，不調用未實作的 R9 Latest。

前置：01／02 與此前依賴案例成功；實際請求 Body／Content-Type 逐案見 companion 09，不重新序列化精確 numeric token。

| Case／目的 | Companion request | 身分 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 09a — multiple 第一次 identified Submit | `POST /api/v1/forms/{{multipleFormId}}/responses` | B | 201；2 Answers 按 Question 排序 | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 09b — multiple 新完整 Response 略過全部 Optional | `POST /api/v1/forms/{{multipleFormId}}/responses` | B | 201；新的 ID、answers=[] | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |

- **09a**：保存 multipleResponse1Id；Receipt 不含 userId／Answer ID，即使 identified；數值等值與 DB 實際表示為準，不承諾 1e0 原 token。
- **09b**：保存 multipleResponse2Id；DB 2 Responses／2 Answers；舊 09a 的 2 Answers 保留，新 Response 0 Answers，不從舊 Response 補答案；不用 R9 Latest。

## 10 — deadline／single／answers priority 與復原

目的：同 native API 先成功，再只對新的 deadline fixture 暫改過期；既有 Response 保留、deadline 優先於 single／answers、不自動 Close。相等／鎖後一次取時／判定後跨 deadline 的可控制 Clock 證據屬 02 當次自動測試，不能把此 native 過期 probe 當精確等值測試。

前置：01／02 與此前依賴案例成功；實際請求 Body／Content-Type 逐案見 companion 10，不重新序列化精確 numeric token。

| Case／目的 | Companion request | 身分 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 10a — deadline 前 Owner Submit | `POST /api/v1/forms/{{deadlineFormId}}/responses` | A | 201；answers=[] | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 10b — 過期 deadline 優先於 single／缺 answers | `POST /api/v1/forms/{{deadlineFormId}}/responses` | A | 409 SUBMISSION_DEADLINE_EXCEEDED | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 10c — deadline 不自動 Close | `GET /api/v1/forms/{{deadlineFormId}}` | B | 200 FormDetail；status=published | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |

- **10a**：保存 deadlineResponseAId；DB deadline 1 Response／0 Answers；先完成此案才進行 10 SQL probe。
- **10b**：必須先依 Markdown 10 將這張新 Form deadline 暫改為過去；零新增，舊 Response 保留。
- **10c**：取證後 finally 復原 deadline，完整 Definition 回 probe 前；13e／13f 要用復原後未截止 Published Form。

### 10 的 DB probe／finally 復原（只改本次 deadlineFormId）

10a 成功後，保存该 Form 的完整 snapshot 為 deadline-before-probe（含原 submission_deadline_at），驗證 Owner A／RunId／ID／Published。用短交易只改 deadline，提交此 probe 交易後才送 10b／10c，不能在 HTTP 期間持這個 transaction。

```sql
BEGIN;
UPDATE forms SET submission_deadline_at=clock_timestamp()-interval '1 hour'
 WHERE id=:'deadlineFormId'::uuid AND user_id=:'memberAId'::uuid
   AND title='R8 deadline ' || :'runId' AND deleted_at IS NULL AND status='published'
 RETURNING id,submission_deadline_at;
COMMIT;
-- 必須恰 1 row；保存 update 後值與 10b/c 证据。
-- finally：originalDeadline 來自實際 snapshot，不猜測原值，不還原 API 成功的其他欄位。
UPDATE forms SET submission_deadline_at=:'originalDeadline'::timestamptz
 WHERE id=:'deadlineFormId'::uuid AND user_id=:'memberAId'::uuid
   AND title='R8 deadline ' || :'runId';
```

finally 復原後完整 snapshot 須等於 10a 後／probe 前：1 Response／0 Answers、status=published、所有 timestamps／子列不變；只復原本次欄位，不修改歷史資料。失敗必記 FAIL／保留證據，13e/f BLOCKED；不默默更換 fixture 求 PASS。此 probe 為驗收專用 SQL，不代表 Publish 後可透過正式 API 修改 Definition。

## 11 — Close 先完成後 Submit 拒絕

目的：main 先 Close，歷史完整保留；Closed＋B已提交＋缺 answers 仍先回 FORM_NOT_SUBMITTABLE。主 Form 只允許 status／updated_at 改動，其餘列不變。

前置：01／02 與此前依賴案例成功；實際請求 Body／Content-Type 逐案見 companion 11，不重新序列化精確 numeric token。

| Case／目的 | Companion request | 身分 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 11a — Close 已提交 main | `POST /api/v1/forms/{{mainFormId}}/close` | A | 200；Closed；兩個 Responses／6 Answers 保留 | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 11b — Closed＋缺 answers | `POST /api/v1/forms/{{mainFormId}}/responses` | B | 409 FORM_NOT_SUBMITTABLE | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |

- **11a**：只有 status／updated_at 可變，Definition 子列不變。
- **11b**：即使 B 已成功 single，Published gate 仍先於 single／answers；零新增。

## 12 — Delete 局部鎖／Owner／重試／歷史保留

目的：先 B 非 Owner 拒絕，再 A 軟刪除 Closed main，重試／Submit 皆 404。Delete 只允許 deleted_at／updated_at 變更，status 保持 closed，main 2 Responses／6 Answers 及全部 Definition rows 原樣保留。Draft／Published／Closed Delete 的完整自動矩陣在 02。

前置：01／02 與此前依賴案例成功；實際請求 Body／Content-Type 逐案見 companion 12，不重新序列化精確 numeric token。

| Case／目的 | Companion request | 身分 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 12a — 非 Owner Delete main | `DELETE /api/v1/forms/{{mainFormId}}` | B | 403 FORM_ACCESS_DENIED | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 12b — Owner Soft Delete main | `DELETE /api/v1/forms/{{mainFormId}}` | A | 204；Form／Questions／Options／Responses／Answers 保留 | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 12c — Owner Delete 重試 | `DELETE /api/v1/forms/{{mainFormId}}` | A | 404 FORM_NOT_FOUND | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 12d — 已刪除 Submit | `POST /api/v1/forms/{{mainFormId}}/responses` | B | 404 FORM_NOT_FOUND | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |

- **12a**：完整資料不變。
- **12b**：只變 deleted_at／updated_at，status 仍 closed，Response=2／Answers=6。
- **12c**：零變更。
- **12d**：保留 2 Responses／6 Answers，無新增。

## 13 — 四組 native HTTP 並行／實際 waiter／合法配對

必須遵守下方 harness；每對案前 snapshot 記 counts／Definition。若任一前置未通過，不盲目继续 race。singleRace 前無 Response；multiple race 前為 2／2、成功後 4／3；deadline race 前 futureDeadline 已復原且只有 A Response；Delete race 在 multiple race 成功後，baseline 為 4／3。

前置：01／02 與此前依賴案例成功；實際請求 Body／Content-Type 逐案見 companion 13，不重新序列化精確 numeric token。

| Case／目的 | Companion request | 身分 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- | --- | --- |
| 13a — single race worker 1 | `POST /api/v1/forms/{{singleRaceFormId}}/responses` | A | 與 13b 合併：恰一個 201／一個 409 RESPONSE_ALREADY_SUBMITTED | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 13b — single race worker 2 | `POST /api/v1/forms/{{singleRaceFormId}}/responses` | A | 與 13a 合併：總 1 Response／1 Answer | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 13c — multiple race worker 1 | `POST /api/v1/forms/{{multipleFormId}}/responses` | B | 與 13d：兩個 201；delta 2 Responses／1 Answer | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 13d — multiple race worker 2 | `POST /api/v1/forms/{{multipleFormId}}/responses` | B | 201；answers=[]；13c/d 後總 4 Responses／3 Answers | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 13e — Submit／Close race 的 Submit | `POST /api/v1/forms/{{deadlineFormId}}/responses` | B | 201 或 409 FORM_NOT_SUBMITTABLE（與 13f 配對） | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 13f — Submit／Close race 的 Close | `POST /api/v1/forms/{{deadlineFormId}}/close` | A | 200；Closed；paired DB delta=1 或 0 Response／0 Answers | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 13g — Submit／Delete race 的 Submit | `POST /api/v1/forms/{{multipleFormId}}/responses` | B | 201 或 404 FORM_NOT_FOUND（與 13h 配對） | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |
| 13h — Submit／Delete race 的 Delete | `DELETE /api/v1/forms/{{multipleFormId}}` | A | 204；paired DB delta=1 或 0 Response／0 Answers | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |

- **13a**：只可依 Markdown 13 barrier harness 與 13b 同時執行；確認兩個實際 DB waiters 後釋放，不能按 REST Client 順序單送求並行 PASS。
- **13b**：兩個 worker 相同 A；成功 Receipt value 必須對應 winner 的 Request，Definition 不变。
- **13c**：前置 09 已成功且保留 2 Responses／2 Answers；仍用 Published multiple，兩 waiter 已確認後才釋放 barrier。
- **13d**：未回答項目不補回 09a 或 13c 值。
- **13e**：前置 deadline 已復原、B 尚無此 Form Response；先前 10a 是 A。Submit 先 commit 時保留新增 B Response；Close 先 commit 時不新增。
- **13f**：與 13e 同時且兩 waiter 證據；一定保留 10a 的 A Response，只有 status／updated_at 變更。
- **13g**：前置 13c/d 通過，總 4 Responses／3 Answers；multiple 不受已提交次數限制。
- **13h**：與 13g 同時且兩 waiter 證據；保留原 4 Responses／3 Answers，只有 deleted_at／updated_at 變更，status 仍 published。

### 13 的 barrier harness（原生 HTTP，不能以 02 或開發 PASS 代填）

依四組 13a/b → 13c/d → 13e/f → 13g/h 執行；其他驗收寫入／全套測試先暫停。同一組兩 worker 各送一次 exact companion request、身份固定，記開始／結束 UTC、status/body 和新 response IDs；Cookie 只用本機 jar／記憶體，不能放可用 JWT 到 command arguments、報告或 Git。

1. 開唯一 application_name=`r8-barrier-<RunId>-<pair>` 的 DB session，`BEGIN; SELECT 1 FROM forms WHERE id=<本組新formId> AND deleted_at IS NULL FOR UPDATE;`，保留同 transaction，記 blocker PID。先查該 PID／transaction 確認持鎖，才開兩個 HTTP workers。native API 使用本次 build／同 DB；worker transport 總 timeout 維持 90 秒，另遵守下列 waiter 10 秒與釋放後 45 秒界限。維持現有產品 DB command timeout 與其他 timeout 設定，不為 harness 調大產品 timeout。
2. controller 持 barrier、兩 worker 同時發，查 pg_stat_activity／pg_blocking_pids 的實際兩個 HTTP DB waiters；不以 Task pending、送出順序或固定 sleep 判重疊。直接／間接 tuple-lock blocking chain 都需追到這個 barrier，排除監控／其他連線，記兩個 waiter PIDs／query／blocking chain。
3. 每組 waiter 觀察／輪詢共用同一個 **10 秒**上限：在送出本組第一個 worker 前開始單一 monotonic 計時，包含兩個 waiter 的觀察、所有輪詢 SQL／blocking-chain 查詢與輪詢間隔；不能每個 waiter、每次查詢或每次輪詢重新取得 10 秒。監控查詢與 delay 使用剩餘預算／取消訊號，整體不越過此期限，替既有產品 DB command timeout 留餘裕。兩個真實 waiter 必須在此期限內都確認成功後才 `COMMIT` 釋放 barrier；不設定固定自動 COMMIT。任一 worker 提前完成／transport timeout、期限內查不到兩個真 waiters 或前置失敗，該並行組記 BLOCKED（觀察到錯誤狀態／資料時記 FAIL），finally ROLLBACK 自己的 barrier並回收全部 workers，保留原回應，不重設 fixture 重試求 PASS。
4. 釋放後每個 HTTP worker 完成上限 45 秒。finally 一定釋放／Dispose 自己的 barrier、取消尚未完成 workers；逐一有限等待回收（各 10 秒），即使某個 timeout／fault 也繼續觀察其餘，最後彙整失敗。清理失敗不得當 PASS；不強制終止陌生 DB backend／API／DB service。
5. 與每組案前 counts／snapshot 比，並核對每個成功 Receipt／DB ID/user_id/value/time、所有失敗零新增。13 的 native run 可能只出現其中一種合法先後，不宣稱兩方向都由本次 HTTP 重現；02 的 deterministic tests 分別覆蓋兩方向。

```sql
-- 在監控 connection 唯讀輪詢，查直接／間接 chain，僅列必要非秘密資訊。
SELECT pid,application_name,wait_event_type,wait_event,pg_blocking_pids(pid),query
 FROM pg_stat_activity WHERE wait_event_type='Lock' AND pid<>pg_backend_pid();
```

| Pair | 只接受的 HTTP／DB 結果 |
| --- | --- |
| 13a/b single 同 A | 一個 201、一個 409 RESPONSE_ALREADY_SUBMITTED；總 1 Response／1 Answer，value 對應 winner |
| 13c/d multiple 同 B | 201／201；delta 2 Responses／1 Answer；本組後總 4／3，empty Receipt 無舊值 |
| 13e/f Submit／Close | Close=200；Submit=201 則 delta 1 Response／0 Answers（Submit 先 commit），或 Submit=409 FORM_NOT_SUBMITTABLE 且零新增（Close 先 commit）；原 A Response 保留，最終 closed |
| 13g/h Submit／Delete | Delete=204；Submit=201 則 delta 1 Response／0 Answers（Submit 先 commit），或 Submit=404 FORM_NOT_FOUND 且零新增（Delete 先 commit）；原 4 Responses／3 Answers 保留，最終 soft-deleted、status 仍 published |

| 非 HTTP 檢查 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| 13 每組 barrier／兩 waiter／finally／資料核對 | 四組均有本次實際重疊證據、合法配對與零殘留 cleanup；原有資料不變 | BLOCKED：01b readiness gate 失敗；本次未執行；歷史 PASS 不代填 | 未執行（等待 Owner review） |

## 14 — 保留資料、復原、秘密清理與交付（非 HTTP）

目的：保留舊資料與本次 5 張新合成 Forms／合法 Responses／Answers，不刪 regular development volume、不做 down -v／reset／migration／hard delete。保留 native Submit 的成功歷史與已軟刪除列供後續 Round；不是重新建立 fixture 取得 PASS。

1. 記本次平台、HEAD／核准 paired-plan 版本、RunId、5 Forms／Question／Option IDs、所有 201 response IDs／actors／最後 status／deleted_at；保留非秘密 evidence manifest。foreign 仍 Draft、main closed＋deleted、singleRace Published、deadline Closed、multiple Published＋deleted（成功前置成立時）。
2. 若所有前置通過：main 固定 2 Responses／6 Answers；foreign 0／0；singleRace 1／1；deadline 1 或 2 Responses／0 Answers；multiple 4 或 5 Responses／3 Answers。不是把不同 race 結果硬填同一總數；任一 FAIL／BLOCKED 依實際記錄。
3. 確認 10 deadline 完整復原，13 barrier transactions／workers 全部回收；任何清理失敗要記 FAIL／BLOCKED，不吞掉求 PASS。保留原 API／DB／volume，僅移除本次可用 Cookie jar／JWT／秘密 request 暫存；非秘密回應／TRX／waiter／snapshot 保留在 ignored evidence 目錄。
4. 將最終 snapshot 依原有 IDs 排除本次新增 Form／Question／Option／Response／Answer IDs後，與 01 全表 baseline 精確比較；所有旧會員公開欄位與旧 Form／子列／歷史／timestamps 不變。不能只比較 counts，不能刪舊資料。回歸暫存資料應全部清完。
5. 最終核對 Git；復原 .http 的 credential/token/server-ID placeholders，保留實際非秘密結果／ID manifest，AI／人工各自填實際欄與證據，不從 AI PASS 推人工 PASS。若要独立人工 replay，以新 RunId／fresh IDs 另跑，不盲目重放 single／已刪除 fixtures。

| 非 HTTP 檢查 | 預期 | AI 實際 | 人工實際 |
| --- | --- | --- | --- |
| 14 舊資料比對／probe 復原／worker 清理／秘密／保留資料／Git | 全部實際完成並有證據；本次合法 fixtures 保留、舊列不變 | BLOCKED：完整成功路徑未執行；另完成唯讀資料保留比對、無新增 fixture／secret／probe／worker | 未執行（等待 Owner review） |

## 覆蓋限制與 Prepare 歷史

本次局部修訂以 `63bd1a9` 原始文件為比對基準；只改 13 的 waiter 共用觀察預算及配對註解／review 狀態，不改 54 個 requests 的 Method、URL、headers、Body、身份、預期或 fixture。產品 timeout 設定不變；正式 HTTP／DB／並行語意仍依核准契約。

實跑文件靜態命令：`& ./artifacts/r8-closeout/validate-plan.ps1`、`& ./artifacts/r8-plan-revision/verify-raw-plan.ps1`、`git diff --check`。結果：兩檔 54 唯一 ID／順序一致，含 05b；每個 request 內容與原版逐段相同（僅正規化 CRLF／LF 比較），分隔符、URL、headers、variables、05e 空 JSON Body 已核對。38 個非空 Body 語法正確；05a 刻意壞 JSON 保留。07j／08a 的 JSON 跳脫、5.0／精確長數值／1e0 token 保留，沒有 code fence／Markdown 貼上標記。證據：[原始文件檢查](../../artifacts/r8-plan-revision/raw-static-check.json)，script 同目錄保留；此為靜態檢查，不是驗收 PASS。

本計畫包含 54 個 HTTP requests（其中環境／登入／建 fixture 是前置），另外 02 同版全套及 13／14 DB、並行／復原檢查。04～14 覆蓋代表性五題型、驗證次序、不同 User single、multiple 歷史、Deadline、Receipt／實際保存、Close／Delete 先後。Owner 指定的 real foreign Question 在 06c、空 JSON Body 在 05e、非 Owner Draft Submit／Detail 分別在 06a／06b。

UUID／Unicode／Number／Rating 細部矩陣由 02 既有 tests 限定證據，未新增業務矩陣／Definition Validation；共用 Auth expired/disabled 是回歸與 source 共用管線證據，不當成 Submit 專屬 native HTTP 全矩陣。精確等值 Clock 與 precommit faults 只由本次 02 受控測試驗證；不做 production Clock 注入／新 retry 框架。COMMIT 結果未知時零新增未驗證，不聲稱保證；multiple 不確定重送仍可能新增另一筆歷史。Number 依 01 §3.3 現有 JSONB 安全範圍，Receipt 不承諾原 token；R10 按精確數值等值分組的依賴保留，不執行 R10。

Prepare 歷史：`8680ccc` 交付時只做原始文件靜態核對，曾停止於「R8 修訂驗收計畫待 review」，所有正式操作當時未執行。Owner 隨後核准並明確授權本次 Execute；歷史靜態檢查不代填本次驗收。

## 首次 AI 執行歷史（r8-acceptance-20261004-01）

01b 首次回傳 503／Unhealthy；Docker 容器內部 healthy，實際 `5432/tcp` binding 為空，API localhost:5432 無 listener。依核准 gate：02（含預計 280 full）與 03～13 未執行；原生並行無 waiter 證據，14 完整五 fixture 結果 BLOCKED。這是環境前置阻礙，尚無 Submit 產品缺陷結論。只有 01a／01b 實際送出；全部 AI 欄據此回填，人工欄未代填。

失敗後的唯讀資料保留核對：4 users／9 forms／22 questions／12 options／0 responses／0 answers 兩次完整 snapshot 相同；此比較不是成功路徑的 01 baseline／完整 14 PASS。本次新 fixture=0；deadline probe／barrier／workers／可用 secrets 均=0；API／DB／volume 保留。首次 log／原始回應／snapshot 保留於本次 evidence，詳見[執行報告](../implementation/R8_Acceptance_Execution_Report.md)。恢復既有 PostgreSQL host binding 後的補驗需保留本次結果，依 Owner 下一步指示記另次執行，不改填本次 PASS。停止於 **「R8 驗收結果待 Owner review」**，未 accepted，未開始 R9。

## 本次正式補驗結果（r8-acceptance-20261004-02）

本輪採 AI 執行驗收＋Owner 審核證據；人工全部未執行，依 Owner 指示不作為本輪 accepted 必要條件。仍須 Owner 明確確認 R8 accepted，才可進入 R9。

本次 54 HTTP／4 非 HTTP 全部 PASS；Windows 全套實際 discovery／TRX 280/280，0 failed／notExecuted。四組 native HTTP 都在各組共用 10 秒內確認兩個真 DB waiter 後才釋放 barrier；Submit→Close 201／200、Delete→Submit 404／204，其他方向由本次 02 deterministic tests 覆蓋。deadline 已完整復原，全部 barrier／workers 回收；旧資料完整列不變。本次五張新 Forms／10 Questions／3 Options／9 Responses／10 Answers 保留，人工未執行。測試細節、fixture IDs、fault／Clock／EF 證據與限制見 [正式補驗報告](../implementation/R8_Acceptance_Reexecution_Report.md)。停止於 **「正式補驗完成，待 Owner review」**，未 accepted、未進 R9。
