# R10 — Statistics 開發報告

2026-10-10；**development verified／開發完成、待驗收**，供獨立 code review。Owner 已在本 chat 確認 [精簡實作計畫](R10_Implementation_Plan.md) `06853f3` review 通過，另行授權開發及必要 build／開發自測／同版回歸。本次未準備或執行正式驗收，未 accepted，未開始 R11／G1。

產品／測試提交：`a4f9402336a4eecdc07c72e92ef09234a4d5e976`；開發起點 `06853f32201a168989c25b5dbb03f8f4ec1c1e77`、master、Git 乾淨。報告／核准計畫狀態／總計畫狀態另採文件提交，SHA 於交付訊息提供。

## 1. 依據與前置核對

已讀 repo 最新 [AGENTS.md](../../AGENTS.md)、[development skill](../../.agents/skills/v1-round-development/SKILL.md)、正式八份規格及 [Backend Implementation Plan](V1_Backend_Implementation_Plan.md)。契約引用沿用核准計畫：00 §7；01 §3.3、FORM-07／09、SUB-06、STA-02～05；02 §5／7～11；03a §2／5～6／8～9；03b §3.5；**03c v1.1 §3.4～3.5／7～8**；04 §4.3／5／10；05 §5.3／10～15／20。四欄位修訂及語意已獲 Owner 核准；沒有發現須改變既定產品行為的契約衝突。

- R10 前 F-01～03、S-01 的 [獨立 review](R0_R9_Pre_R10_Local_Repair_Review_Report.md) `7e10d71` 已判定可結案、無阻擋問題；本次完整 suite 承接修正版，沒有冒用其歷史 377 PASS。
- RV-I-01（Publish barrier fresh GET 完整頂層 DTO assertion）、I-01（README 範圍）、I-02（List count/items 快照）仍是未完成的非阻擋建議；未順手擴充。
- R0～R9 正式整合驗收成對文件仍未同步修正版／未執行；R7／R9 Owner confirmation 仍 pending，沒有推定這些結果通過。上述事項未被既有 review 定為 R10 技術 blocker。
- AGENTS.md／skills 沒有過時或衝突指示，不需修改；正式八份規格本次亦未改動。

## 2. 實際修改檔案與行為

| 檔案 | 變更 |
| --- | --- |
| [StatisticsController.cs](../../src/CustomFormService.Api/Controllers/StatisticsController.cs)（新增） | 端點 18 GET `/api/v1/forms/{formId}/statistics`；string UUID、Form lookup、存在性保護／Conditional Auth／Visibility early return；短 RepeatableRead 交易與標準 Error Envelope |
| [StatisticsCredentialResolver.cs](../../src/CustomFormService.Api/Authentication/StatisticsCredentialResolver.cs)（新增） | 沿用已配置 JwtBearer TokenHandlers 與 TokenValidationParameters；Caller User 在同一 DbContext／快照查詢 |
| [StatisticsQuery.cs](../../src/CustomFormService.Api/Queries/StatisticsQuery.cs)（新增） | DB 先選每人最新整份 Response；只查選定 Answers；Definition、Creator、identified 的批次名稱投影 |
| [StatisticsAggregator.cs](../../src/CustomFormService.Api/Queries/StatisticsAggregator.cs)（新增） | 五題型聚合、三層 count、排序／零票、DefinitionNumber 精確分組比較、anonymous／identified Shape；無效持久 Answer 受控失敗 |
| [StatisticsResponse.cs](../../src/CustomFormService.Api/Contracts/Statistics/StatisticsResponse.cs)（新增） | 九欄位 Root、六欄位 Question、Choice／Value Items、id/displayName Respondent；僅 respondents 屬性忽略 null |
| [Program.cs](../../src/CustomFormService.Api/Program.cs) | 註冊兩個 scoped services；沒有改共用 JWT、Middleware／JSON null 政策 |
| [FormEndpointsTests.Statistics.cs](../../tests/CustomFormService.IntegrationTests/FormEndpointsTests.Statistics.cs)（新增） | 43 個 R10 DataRows／案例；沿用真 PostgreSQL、隨機精確 email fixture cleanup；SQL／實際 SELECT barrier 證據 |
| [R10_Implementation_Plan.md](R10_Implementation_Plan.md)、[V1_Backend_Implementation_Plan.md](V1_Backend_Implementation_Plan.md)、本報告 | 同步 Owner 核准及開發交付，保留規劃歷史與其他 Round pending |

Conditional Auth 只讀 `access_token` Cookie，使用既有 signature／issuer／audience／HS256／expiry／零 ClockSkew 設定；不採 unvalidated token 或 JWT Profile、不採 Bearer header。AllowAnonymous 只讓 middleware 略過提前 challenge，Controller 在 UUID／Form lookup 後才驗 Credential。Draft 只有 Active Owner 得到 409，其餘 404；Published 任一 Active User 得到 409，其餘 404，不因 Visibility／期限開放。Closed public 直接讀取，所有壞 Cookie 不阻擋；Private 依缺 Cookie／invalid／expired／disabled 回既有 Auth code，再判 Owner／曾成功提交資格，不要求最新有 Answer。

每個查詢以第一次實際 Form SELECT 建立快照，Caller status、授權政策／資格、名稱及統計都在同一交易完成。DTO 全部物化、JSON 值 Clone 後 commit；不取 Form lock／FOR UPDATE，不 SaveChanges。只依目前 Form responseIdentity 決定揭露；public／Owner 不例外。

Root 完整包含 `creatorDisplayName`（目前名稱，不排除 disabled Creator）、`responseMode`、`resultVisibility`（同授權快照）、`submissionDeadlineAt`（保存期限，UTC、null 必出現），沒有追加其他根欄位。Item 使用具體 Choice／Value DTO 放入 object 集合，actual JSON 已檢查沒有 type discriminator／內部欄位。Number 直接輸出保存 JsonElement，不經 decimal／double，不承諾原始 token spelling。

Entity、EF model／mapping、DB schema、Migration、DefinitionNumber 既有數值界限、R4 Auth／Logout、R8 writer、R9／Detail 查詢程式均未修改。檔案切分與核准計畫一致。

## 3. 開發自測及同版回歸

環境：Windows、.NET SDK 10.0.400／net10.0，真 PostgreSQL 17.11。RunDir：[artifacts/r10-development-20261010](../../artifacts/r10-development-20261010/)（本機 ignored 證據，未提交秘密或測試輸出）。

| 實際命令／檢查 | 結果及證據 |
| --- | --- |
| `dotnet restore CustomFormService.sln` | 一般 sandbox 初次 exit 1／無診斷輸出；本機授權重跑 exit 0，所有專案 up to date |
| `dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1` | 首次舊 API PID 4848 鎖住本 repo DLL（11 warnings／2 errors）；核對 executable／完整 DLL command line／5000 port 後停止。初版 Statistics resolver 額外加入的 legacy validator 分支也已移除，直接使用本 repo 已配置的 TokenHandlers，消除 obsolete warning；既有 Auth 程式未改。接著 `build-first.log` 新測試 params target-typed new 有 14 編譯錯誤；改為明確 NpgsqlParameter。second／third／fourth／**build-final.log** 均 0 warnings／0 errors；最終 exit 0 |
| `dotnet test CustomFormService.sln --no-build --configuration Debug --filter "FullyQualifiedName~R10_" --logger "trx;LogFileName=r10-first.trx" --results-directory artifacts/r10-development-20261010/focused-first` | 首輪 **24 PASS／15 FAIL**：14 是新 helper 送出空 Cookie header，在 client 端 FormatException；1 是 empty cookie 期望不符既有解析政策。保留原 log／TRX；修正 R10 helper 與參照政策，不修改舊 helper／產品 JWT 政策 |
| 相同 focused 命令，logger `r10-second.trx`／results `focused-second` | **39/39 PASS**。之後補強「同一合法 signing key 但 HS512」的算法測試、受控持久資料失敗及 scope 防護，重新 build／驗證 |
| 相同 focused 命令，logger `r10-final.trx`／results `focused-final` | **43/43 PASS，0 failed/skipped，12 秒**，四組 SQL 快照證據在 TRX stdout |
| `dotnet test CustomFormService.sln --no-build --configuration Debug --logger "trx;LogFileName=r10-full.trx" --results-directory artifacts/r10-development-20261010/full` | **420/420 PASS，0 failed/skipped，30 秒**；包含 R10 43，以及既有 R4 Auth／Logout、R8 Submit／原子性／並行、R9 Latest／Count、F-01～03／S-01 回歸 |
| PDB SHA-256 與 DLL 核對 | **78 個非 obj repo source checksum 全部一致**；API build output 與 tests/bin API DLL hash 相同；[delivery-pdb-audit.json](../../artifacts/r10-development-20261010/delivery-pdb-audit.json) |
| 隱藏 `Start-Process dotnet exec <本 repo DLL>`；`& ./artifacts/r10-development-20261010/native-probe.ps1` | 本次 Windows 原生 Kestrel **21 HTTP 全 PASS**，另 Privacy／null／exact JSON Number assertions PASS；不是正式驗收。health/readiness 200；本次 PID **36804**、localhost:5000、Development；[native-process-evidence.json](../../artifacts/r10-development-20261010/native-process-evidence.json)、[native-http.jsonl](../../artifacts/r10-development-20261010/native-http.jsonl)、[native-summary.json](../../artifacts/r10-development-20261010/native-summary.json) |
| 七表只讀快照、TRX 結果及 `git diff --check`／scope 核對 | suite 前後完整列 digest／Migration 相同，原生保留新增 fixture 而全部舊列相同；diff 無 whitespace 錯誤，只有本輪七個產品／測試檔與三個文件 |

原生 DLL SHA-256：`12AB1A7D125938F9A8F790DDDA41FBF15CF37C8B477D0E0878E43FEF07E12FAB`。native source 標記原為未提交候選版；其 source/PDB 及 suite 測試來源現已提交為 `a4f9402`，提交前後產品／測試沒有差異。API 保留運行，child-only 環境設定沒有覆寫工作 shell／持久設定。

43 個 R10 案例的驗證內容：

| 案例族／DataRows | 已檢查 |
| --- | --- |
| Lifecycle／Visibility，9 | 每列 Owner／Respondent／其他 Active／匿名／bad cookie；UUID 400 優先、missing／deleted 404 優先；Expired deadline 的 Published 不開放；錯誤只有 Error Envelope |
| JWT policy，13；Bearer，1 | missing、empty、malformed、issuer、audience、signature、algorithm、sub、no expiry、not-yet-valid、expired、absent User、disabled；和既有 protected `/users/me` code/status 比較；Draft／Published 404 及 public 200。literal `access_token=` 被 ASP.NET Request.Cookies 丟棄，兩路皆 AUTH_REQUIRED，沿用既有政策；簽名合法但 HS512 被兩路拒絕 |
| Zero-data Root／Privacy，6 | 全 Visibility × anonymous/identified；single/multiple、Creator 改名／public disabled Creator、九個 Root 欄位、description/deadline null、六個 Question 欄位、text/number []、Choice/rating 零票、identified []、anonymous property omission |
| 五題型分布／身分，2 | Root 6（含 Owner 最新全空），Question 5；text A/a/b/Unicode ordinal／count 排序；1/1.0/1e0、正負零合組；Choice 業務 value 與 ID 分開；multiple Item 加總 10 > Question 5；rating 0..10 全集合；目前改名、disabled 歷史、同名 id tie-break、count=名單長度；Owner／guest 相同政策結果及最小 SQL profile 查詢 |
| 超出 decimal/double 精度，1 | 81 位相近大整數、101 位小數、負數、零／等值 spelling；精確預期七組及排序，以 DB JSONB 保存文字核對 actual JSON number，非有限精度比較 |
| Effective 整份／UUID／scope，1 | 同人多次、較新僅 number 不補舊題；測試 fixture 的同 timestamp 已知 low/high UUID 在 PostgreSQL 選 high；另 Form 的 Owner Answer 不混入；Root 1、各題 0/1/0/0/0 |
| Deadline，2 | 保存微秒／+08:00 輸入對應 UTC output、提前 Close／Closed 過去及未來期限、表定期限保持、GET 無寫入；未到期／已到期不代換 Close 時間 |
| 無效持久 Answer，4 | text null、number string、未配置 Choice ID、非整數 rating → 受控 500 INTERNAL_SERVER_ERROR；無 SQL／內部例外細節，列不變 |
| 實際快照並行，4 | Visibility、Delete、Creator＋Respondent 改名、Caller disabled；已提交獨立變更後，當次仍完整 V0，下一次新政策／名稱／不存在／disabled；所有 workers 有界等待、finally Release／Cancel／drain，非預期錯誤不吞掉 |

## 4. SQL／快照證據

實際 interceptor SQL（完整見 [sql-snapshot-evidence.txt](../../artifacts/r10-development-20261010/sql-snapshot-evidence.txt) 與 final focused／full TRX），沒有載入歷史後逐題補答：

```sql
-- First SELECT inside RepeatableRead: establishes snapshot before credential/profile queries.
SELECT f.id, f.user_id, f.status, f.title, f.description,
       f.response_mode, f.response_identity, f.result_visibility, f.submission_deadline_at
FROM forms AS f WHERE f.id = @id AND f.deleted_at IS NULL LIMIT 2;

SELECT DISTINCT ON (user_id) id AS "Id", user_id AS "UserId"
FROM responses WHERE form_id = @p0
ORDER BY user_id, submitted_at DESC, id DESC;

SELECT a.response_id AS "ResponseId", a.question_id AS "QuestionId", a.value AS "Value"
FROM answers AS a WHERE a.response_id = ANY (@responseIds);
```

`@p0` 是 EF SqlQuery 參數，沒有 UUID 字串拼接。先選 Response 才讀 Answers，最新零 Answer 仍存在於 Root；回答集合以同一選取供全部 Questions 使用。匿名只有 Creator 的一個 Users projection，沒有 Respondent Profile；identified 再加一個 `WHERE u.id = ANY (@userIds)` 批次查詢，不依 User status 篩掉歷史。捕捉的 GET SQL 無 INSERT／UPDATE／FOR UPDATE。

四個 barrier 在 `ReaderExecutedAsync` 的實際目標 Form SELECT 暫停，獨立連線已完成 mutation 才 Release，不以 BeginTransaction／sleep 作快照證據。每案當次九個 SELECT 共用下列 ContextId 與 **RepeatableRead**，DTO 和 V0 全 JSON 相等；此證據也由同版 full 重新執行：

| mutation | final focused ContextId | fresh GET |
| --- | --- | --- |
| visibility | `be3b9fea-106b-44c6-9ed9-162068f9940b` | 403 STATISTICS_ACCESS_DENIED |
| delete | `3b395f06-2e97-4025-b9ef-438d24141dc4` | 404 FORM_NOT_FOUND |
| profiles | `87b175e1-2fae-4619-a76a-e5e4bcb2df76` | 200，Creator／Respondent 都是 V1 目前名稱 |
| caller-disabled | `457bb966-e42e-4fb8-a5cb-8f56f876ebf0` | 403 AUTH_USER_DISABLED |

UUID 平手 fixture：`10000000-0000-0000-0000-000000000001` 與 `f0000000-0000-0000-0000-000000000001` 同 timestamp，DB 及 Statistics 均選後者的 number=7，不從較舊整份取得其他題目。這是精確測試 fixture，不改 R8 正式提交流程。

## 5. 資料保留與實際 JSON

七表以 RepeatableRead READ ONLY、`row_to_json(t)::text` 按 ID 排序的完整列 MD5 核對（包括 timestamps／JSONB），不是只比 count。suite 本次精確 fixture cleanup 後與起始相同；native 結束排除**只有本次新增** Owner／Forms 關聯列後也相同。Migration 始終 `20260928122537_InitialV1Schema / 10.0.11`，沒有 apply／rebuild／schema／volume 操作。

| 舊表 | count | 前後相同的 digest |
| --- | --- | --- |
| users | 10 | `94607b0fb5b590305406e165f7a628d3` |
| user_identities | 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| forms | 28 | `feda43fca91bc18d8a34b8434707efad` |
| questions | 53 | `b61197a910d35a293df8a8f7a9caed9f` |
| options | 24 | `5bf7d7580b969694815d535fe5ca819d` |
| responses | 20 | `2637149d9e0b9b6fd704b08389eaaba9` |
| answers | 23 | `2738bacc148a57b31663bcec22baf71d` |

[db-before.txt](../../artifacts/r10-development-20261010/db-before.txt)／[db-after-suite.txt](../../artifacts/r10-development-20261010/db-after-suite.txt)／[native-db-after-old.txt](../../artifacts/r10-development-20261010/native-db-after-old.txt) 保存上述比較；[native-statistics-readonly.json](../../artifacts/r10-development-20261010/native-statistics-readonly.json) 另核對成功 GET 前後**包含新 fixture 的全部七表列**相等。

原生新 RunId `r10-native-a39bc77302ba4b718b7eae48b06d067d`；保留 1 User、2 Closed Forms、3 Questions、1 Response、1 Answer。Owner `de727243-c167-4eaa-8e2d-641d923e31c6`；Forms `bd31090c-813f-44dd-b19d-8b12cefb56da`（anonymous，最後 owner_only）及 `993eeba9-74a0-45e4-91e4-4788a20c1a7f`（identified public 零人）。交付總列數 Users=11、Identities=0、Forms=30、Questions=56、Options=24、Responses=21、Answers=24，見 [db-final.txt](../../artifacts/r10-development-20261010/db-final.txt)。憑證／JWT／Cookie 只在記憶體，沒有寫入 evidence；保留的 HTTP body 只含合成資料。

原生 response 以 System.Text.Json 直接解析及核對 raw token：Number `100000000000000000000000000000000000001` 為 JSON Number 且逐字精確；anonymous Items 不存在 respondents，identified 零票為 `respondents: []`；Root 恰九欄位且 `description: null`／`submissionDeadlineAt: null` 都存在。未以 PowerShell 的有限精度數值轉換判斷精確性。

## 6. 未完成事項與停止點

本輪已授權實作及必要開發驗證全部完成，沒有未解決契約衝突或本輪技術 blocker。獨立 code review 尚未執行；正式 R10 驗收計畫尚未建立或獲授權，正式／人工驗收未執行。開發自測不取代這些階段。

前置既有待辦仍按 §1 保留：R0～R9 正式整合驗收修正版、R7／R9 Owner confirmation、RV-I-01／I-01／I-02 非阻擋建議；本次沒有改其狀態。Linux 未執行，所有交付必要 build／自測／回歸及原生 HTTP 均在 Windows 成功完成。

交付 scope 自查已完成，Git 未包含 ignored RunDir／秘密／其他 Round。產品與測試先提交 `a4f9402`，文件另提交；最終 SHA／Git state 於交付訊息回報。**停止於「開發完成、待驗收」**，供獨立 code review；正式驗收準備與後續執行仍需另行授權及既有核准 gate。
