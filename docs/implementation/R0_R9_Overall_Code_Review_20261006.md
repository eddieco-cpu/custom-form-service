# R0～R9 整體 code review（2026-10-06 開始；2026-10-07 交付）

狀態：**整體 review 完成；整合驗收計畫待 Owner review，尚未執行**。
本次只審查 R0～R9 與準備整合驗收文件，未修改產品、正式規格、既有驗收狀態或自動測試。R10 Statistics、G1 Google Auth、R11、前端與部署未納入，未實作不列缺陷。

## 1. 審查基準與 Round 現況

- 審查 commit：`6f61a160863bfacb2fd85ef3d97abca271af5e22`（`docs: record R9 AI acceptance results`）。
- 起始 `git status --short` 無輸出，工作樹乾淨。此次 source/tests/specs 均維持該 commit；文件交付提交另見交付訊息。
- 先讀最新 `AGENTS.md`、八份 `docs/specs/00～05`（含 03a/b/c）及 Implementation Plan。計畫準備使用 `.agents/skills/v1-acceptance-testing/SKILL.md` Prepare；開發／repair skill 的觸發條件不成立，本次無產品修正。
- 直接閱讀目前實作與測試，不以歷史 review／PASS 代替核對。未發現本次受審範圍中必須停工的正式規格衝突。

| Round | 目前實際交付切面 | Implementation Plan 記錄，保留原狀 |
| --- | --- | --- |
| R0 | Alignment 報告及完整 V1 範圍／依賴 | 歷史報告，未另記 accepted |
| R1 | net10 API、工程 health、Development OpenAPI／Swagger | repo 未存完整驗收紀錄 |
| R2 | PostgreSQL Compose、named volume、API readiness／設定 | repo 未存完整驗收紀錄 |
| R3 | 七張 Domain Table、enum、InitialV1Schema／EF mapping | repo 未存完整驗收紀錄 |
| R4 | Register／Login／Logout／GET、PATCH me | accepted |
| R5 | Create／Owner List／Detail／Soft Delete | accepted，AI case 紀錄有歷史缺口 |
| R6 | Draft Candidate／Delta／Question、Option、Config／持久化 | accepted；人工未執行 |
| R7 | Publish／Close／Visibility／Freeze／寫入競爭 | acceptance results pending owner confirmation |
| R8 | 五題型 Submission／Receipt／single、multiple／原子性 | accepted；人工未執行 |
| R9 | Latest 整份 Response／Respondent Count／快照 | acceptance results pending owner confirmation |
| R10／G1 | Statistics／Google Auth | not started，本次範圍外 |

「大致完成」符合已有切面實作，但不能等同 R0～R9 全部已 accepted。本次沒有替 Owner 確認 R7／R9，也不修改原進度表。

## 2. 結論與優先序

發現 **3 項已確認 P2 缺陷**；沒有確認的 P0／P1、跨會員資料洩漏、single 雙重成功或交易殘留。另有 1 項待驗證的讀取一致性疑點與 2 項非阻擋改善建議。

三項 P2 會阻止受影響的整合案例取得全 PASS，建議在整合驗收簽核前修正。尚無證據認定它們阻擋 R10 開發；R7／R9 的 Owner 確認仍是進度上的待處理事項。是否先修正再進 R10、或先執行未受影響案例，由 Owner 排序，本次不自行推進。

### F-01 — P2，已確認：合法大 page 的 offset 乘法溢位

- 位置：[FormsController.cs:179](../../src/CustomFormService.Api/Controllers/FormsController.cs#L179)；同檔 568、596～611 接受正 int page，未保護乘法。
- 觸發：Active User 呼叫 `GET /api/v1/forms?page=2147483647&pageSize=100`；或 `page=42949674&pageSize=100`。
- 實際：`(page - 1) * pageSize` 使用 unchecked int。前者變成 **-200**，後者變成 **4**；負 OFFSET 會送至 PostgreSQL，正向溢位則跳到錯誤的前面頁面。有至少五張未刪 Form 時後者可能回第五張以後，而非空頁。
- 預期：03b §6.2／05 §13.2：合法 page 超過最後一頁回 200、`items=[]`，保留指定 page 與正確 pagination。
- 影響：合法輸入可能變成 500，或無聲回錯頁；未突破 Owner scope。
- 證據：直接核對整條查詢與 range validation；同次 .NET 10 primitive probe 重現 offset **-200／4**。**未以帶登入 HTTP 重現此項**，不宣稱已觀察資料庫 500 或錯頁 body。
- 修正方向：以 long 安全計算 offset，對 totalItems 先判斷超界，避免把超出 int 的 offset 傳入 LINQ Skip；不可任意新增 page 上限或回 400 改掉凍結契約。
- 測試缺口：現有 List 測試只含小 page 超界、page=0、pageSize=101，未覆蓋乘法溢位。
- 計畫影響：12a／12b。
- 計畫12b改用同一缺陷的零offset變體：`page=1073741825&pageSize=4`。追加純記憶體unchecked算式核對結果為0，兩張本次fixture即可揭露錯頁，無須額外堆疊五張表單。

### F-02 — P2，已確認：Auth／共用文字解碼未處理不完整 Unicode

- 位置：[RequestJson.cs:58](../../src/CustomFormService.Api/Validation/RequestJson.cs#L58)、[AuthController.cs:50](../../src/CustomFormService.Api/Controllers/AuthController.cs#L50)；相同未受控 GetString 另見 FormsController 761、FormDefinitionValidator 480、DraftDefinitionDelta 351。
- 觸發：JSON 可被 JsonDocument 解析，但字串使用未配對 surrogate，例如 Register 的 `"displayName":"\uD800"`。
- 實際：GetString 丟 InvalidOperationException，經 ApiExceptionMiddleware 15～26 映射為 **500 INTERNAL_SERVER_ERROR**。本次原生 API 實際觀察此 response；stack 指向 RequestJson:58 → AuthController:50，無 User 寫入。
- 預期：03b §4.1、04 §6／§6.1：無效 Request／文字輸入是 400 VALIDATION_ERROR，detail 應定位 displayName、INVALID_VALUE。不能把 Client 解碼失敗當未預期 Server Failure。
- 影響：Public Register 可由特製 body 觸發 500／錯誤 log；其他共用解碼路徑有同類風險。**Auth 已 HTTP 確認；列出的 User／Form 路徑僅 source 核對，未逐端點重現**。
- 跨 Round 差異：R8 SubmissionAnswerValidator 的 TryReadString／TryReadObject 已控制相同錯誤，R4～R6 未共用這層處理。
- 修正方向：共用安全字串與 property-name 解碼，保留各 endpoint 的 early-return／top-level／detail path，不在全域把所有 InvalidOperationException 改為 400。
- 測試缺口：R8 有 lone-surrogate 覆蓋，Auth／User／Definition 沒有對等解碼案例。
- 計畫影響：12c。

### F-03 — P2，已確認：MVC Content-Type 失敗漏出 415 ProblemDetails

- 位置：[Program.cs:16](../../src/CustomFormService.Api/Program.cs#L16)（16～24），[AuthController.cs:37](../../src/CustomFormService.Api/Controllers/AuthController.cs#L37)。設定只取代 InvalidModelStateResponseFactory，未涵蓋 MVC unsupported-media-type／client error result。
- 觸發：`POST /api/v1/auth/register`，`Content-Type: text/plain`，body `{}`。
- 實際：原生 API 回 **415**，body 為 `type/title/status/traceId` 的框架 ProblemDetails，沒有正式 `error.code`。ApiExceptionMiddleware 只捕捉 exception，無法攔截這個 framework result。
- 預期：03a §2.1／§4.2、03b §4.1、04 §2／§3／§6：Request Body／傳輸驗證依本 API 的 400 VALIDATION_ERROR 與正式 Envelope，不得回 ProblemDetails。
- 影響：Client 無法統一處理 error.code；Auth／User／Form 的 JsonElement MVC binding 與 R7 Visibility／R8 Submit 的手動 JSON 解析不一致。**Register 已 HTTP 確認；其他 MVC binding endpoint 尚未逐一 probe**。
- 修正方向：在適當 MVC filter／input binding 邊界映射正式錯誤；保護 endpoint 的 Authentication 必須仍先於 body 驗證，勿以全域 status 重寫改壞其他 route。
- 測試缺口：R7／R8 測了 transport failure，R4 測 malformed JSON，但未測 unsupported Content-Type。
- 計畫影響：12d。

### S-01 — 待驗證疑點：Detail 的 split query 可讀到不同 Definition 版本

- 位置：[FormsController.cs:253](../../src/CustomFormService.Api/Controllers/FormsController.cs#L253)、532～541：GetDetail 不開交易，LoadAvailableForm 使用 AsSplitQuery。
- 可能時序：GET 讀到 Draft 的舊 Form title；Owner 的 Patch 在 Questions／Options SELECT 前提交新 title 與新題目；GET 可能合成舊 title + 新 Questions。Publish／Delete 亦可能在拆分 SELECT 間生效。
- 影響／預期：完整 FormDetail 是否應來自一個一致版本需核對具體 SQL 時序與可觀察回應；不能僅因沒有鎖就判定越權、已刪除後讀取違約或 Definition freeze 失效。已開始的讀取先於 Delete 可以合理成功。
- 現況：**未做同步 barrier 重現，未列為已確認缺陷或進 R10 blocker**。R9 有 Latest／Count 快照測試，但不涵蓋 Detail。
- 後續方向：Owner 若授权確認，可做同步讀取 probe；若證實不存在一致版本，再考慮 RepeatableRead 或單一查詢。新測試／修正不在本次範圍；新計畫不加入未經核准的併行矩陣。

### I-01／I-02 — 非阻擋改善建議

- **I-01 文件**：README 開頭／Current boundaries 仍以 R5 交付描述，與現有 R6～R9 endpoints 不一致。容易讓新執行者低估範圍；後續更新 onboarding／scope 索引，保留歷史 Round 文件，不改 accepted 紀錄。本次未修改 README。
- **I-02 查詢**：List 的 totalItems 與 items 分開查詢（FormsController 173～199），併行 Create／Delete 下可能不同快照；效能與一致性可以後續評估。規格未明訂 List 快照／效能 SLA，不列確認缺陷，勿以此臆造新契約。

## 3. 審查覆蓋摘要

| 面向 | 本次直接核對 | 結果與界線 |
| --- | --- | --- |
| API／Errors | 16 支 R0～R9 業務 endpoint、resource shapes、Status／detail paths／early return；ApiErrors／middleware | F-01～03；不審查 Statistics／Google 行為 |
| Auth／授權 | Cookie-only JWT、signature／issuer／audience／expiration、sub、Active DB 查詢、Logout、Owner scope／Draft hiding | 未見具體越權；Profiles 用目前 DB 資料，密碼 hash／dummy verification／unique email conflict 對應 |
| 設定 | Program／appsettings／launch profiles／Jwt、Cookie、CORS options／PostgreSqlOptions／Compose／configuration guide | 秘密分離、startup validation、Secure／Host-only／Lax、canonical origins；未驗正式部署 HTTPS |
| Schema | 七個 Entity、七個 Configuration、enum mapping、Initial migration／snapshot 與既有 schema tests | 保留 user_identities；FK 只有 Question→Options Cascade；沒有無條件 response single unique；此次未從零重建 |
| Definition | FormDefinitionCandidate／Validator／Delta／ChangeSet／Writer | full candidate、未知欄位、nullable、config replacement、type change、ID-only delete、排序交換／option alternate-key 兩階段 save；F-02 解碼缺口 |
| Lifecycle／Delete | Publish／Close／Visibility／Patch／Delete 的鎖與驗證流程 | 共用 FormAggregateLock，鎖持至 commit／rollback，Freeze 與 soft delete 排除一致；Deadline 不 auto-close |
| Submission | envelope／Answer validation 五題型／exact number／optional omission／receipt | 一次取樣 time、寫入前重新確認、history immutable、同 transaction 保存 Response＋Answers |
| Latest／Count | RepeatableRead 第一個 Form SELECT、DB latest ORDER BY／LIMIT、distinct User、receipt sorting | 不 backfill；Deadline／identity／visibility 不影響本人或 Owner Count；未過濾 disabled 的合法歷史 |
| Concurrency／rollback | R6/R7/R8 writers／save、precommit faults／waiter；R9 四個 SELECT 快照案例及 finally cleanup | 閱讀實際控制點與 DB 殘留 assertions，並重跑現有 suite；未只靠 Task.WhenAll 或歷史 PASS |
| Tests | tests 專案全部測試檔案的 case／DataRows／fixture／清理範圍；核心交易案例逐段讀取 | 306/306 PASS 仍漏 F-01～03；沒有新增測試或宣稱 coverage 百分比 |

現有自動測試承接範例：
- AuthUserEndpointsTests、ConfigurationTests、EngineeringEndpointsTests：Password／Unicode code points、Cookie／CORS、disabled／expired／不存在 User、no-op／logout。
- DatabaseSchemaTests：實際 DB columns／enums／keys／checks／indexes／delete behavior，含 choice case sensitivity。
- FormEndpointsTests 及 CandidateContract／CandidateSafety／OptionPersistence／DeadlinePrecision：Delta、完整 Candidate、精確 JSONB／timestamp、第一／第二 Save 與 commit 回滾。
- Lifecycle／LifecycleReview：Publish／Close／Visibility／Delete 競爭與狀態。
- SubmissionAnswerValidatorTests、Submission／SubmissionConcurrency／SubmissionAtomicity：五題型、數字精度、single／multiple、time、等待鎖、真正 partial write／precommit fault。
- ResponseQueries：整份 Latest、UUID tie-break、distinct count、四組 Latest／Count × Submit／Delete（ReaderExecutedAsync Form SELECT 控制點）。

## 4. 實際執行與資料影響

| 實際命令／操作 | 實際結果 |
| --- | --- |
| `git status --short`、`git rev-parse HEAD`、`git log -5 --oneline` | 起始乾淨、基準 commit 如 §1 |
| `dotnet --info` | SDK 10.0.400、runtime／ASP.NET Core 10.0.11，Windows x64 |
| `docker compose config --quiet` | exit 0，未輸出秘密 interpolation |
| `docker compose ps` | sandbox 首次 named-pipe Access denied；核准本機唯讀重試 exit 0，postgres:17.11-alpine healthy |
| `docker compose port postgres 5432` | 127.0.0.1:5432 |
| `dotnet build CustomFormService.sln --no-restore --configuration Debug` | sandbox exit 1；編譯器取消、MSB5021，未稱成功 |
| `dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1` | 本機重試 exit 0，0 warnings／0 errors |
| `dotnet test CustomFormService.sln --no-build --configuration Debug --logger "trx;LogFileName=review.trx" --results-directory artifacts/r0-r9-review-20261006` | exit 0，306 PASS／0 FAIL／0 skipped；29 秒 |
| `docker compose exec -T postgres sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At'`，stdin 為唯讀 baseline.sql | 測試前、測試後、probe 後七表逐表 rows digest 相同；migration history 為 20260928122537_InitialV1Schema／10.0.11 |
| `pwsh -NoProfile -File artifacts/r0-r9-review-20261006/primitive-probe.ps1` | .NET 10.0.11；GetString surrogate 解碼 exception，unchecked offset -200／4；純記憶體，非新增 suite |
| `dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --launch-profile http --no-build -- --urls http://localhost:5010` | 同次 build 原生 API，Development；只為本次 review probe |
| curl.exe health／ready、無效 surrogate Register、text/plain Register | 200／200／500 正式 Server Envelope／415 ProblemDetails。無成功 Register、無驗收 fixtures |
| Ctrl+C 停止 5010 review process；查 5010 listener | 暫時 process 已結束，沒有 listener；停止只作用本次 session |

本次實際未執行：restore（使用現有已還原 packages）、dotnet format、clean-room migration rebuild、Linux fallback、正式 HTTPS／瀏覽器 CORS／DBeaver、新整合驗收、任何產品修正／新自動測試。
本機編譯／Docker 權限首次失敗已留實際結果，不算產品缺陷。

DB 前／後／final：users=8、user_identities=0、forms=22、questions=47、options=18、responses=20、answers=23；七表完整列 MD5 各自一致。原有 fixtures／驗收證據完整保留，沒有重建 DB／移除 volume。
逐表digest：users `a7f312cb0a1d527e93b46ac312cc46d4`；user_identities `d41d8cd98f00b204e9800998ecf8427e`；forms `8f68b9854f4b05102655f05d1b5a9546`；questions `6ca0954fa06c5f29fdf11fc607e4cc07`；options `304bc35f6bbe633248acad863af29c0a`；responses `2637149d9e0b9b6fd704b08389eaaba9`；answers `2738bacc148a57b31663bcec22baf71d`。這些是ordered整列集合雜湊，不是秘密原文。
現有測試僅建立隨機 email 的暫存 users／forms，自行按已核對的 exact email 清理；full suite cleanup 完成。probe 沒有 DB write。
非秘密原始 test.log／review.trx／baseline.sql／digest 證據在忽略的 `artifacts/r0-r9-review-20261006/`，不提交可用秘密。這些本機檔案不是跨機器可用的永久證據，摘要與命令已記入本文件。

## 5. 交付與停止點

整合計畫：[Markdown](../test-plan/R0_R9_Integration_Acceptance_Test.md)／[HTTP companion](../test-plan/R0_R9_Integration_Manual_Tests.http)。
新計畫所有 AI／人工結果均未執行；既有驗收紀錄原封保留。
只提交此次 review 文件及成對計畫，分成 review／plan preparation 單一目的 commits。SHA 與剩餘 Git 狀態見交付訊息，不把文件提交當作修正或驗收通過。

**整體 review 完成；整合驗收計畫待 Owner review，尚未執行。**
