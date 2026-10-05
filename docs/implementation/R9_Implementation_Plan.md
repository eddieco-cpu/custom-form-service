# R9 — My Latest／Respondent Count 精簡實作計畫

2026-10-05；狀態：**acceptance results pending owner confirmation（R9 驗收結果待 Owner review；人工未執行，未 accepted）**。Owner 已確認計畫 `14442b2` review 通過並授權開發，維持 `RepeatableRead`，補充並行測試必須以實際查詢建立快照為控制點，不能以 BeginTransaction 當證據。R8 Owner accepted 已依授權同步於 Implementation Plan（`0f44286`），原驗收結果與證據保留。規劃基準 `a925bbd`，規劃前 Git 乾淨；本次實作與實際驗證見 [開發報告](R9_Development_Report.md)。下方為核准實作計畫。

依 repo 最新 [AGENTS.md](../../AGENTS.md)、[development skill](../../.agents/skills/v1-round-development/SKILL.md) 與 [Implementation Plan](V1_Backend_Implementation_Plan.md) R9；契約對照：01 SUB-06～07／STA-01、02 §10～11、03a §5～6、03b §3.6／6.2、03c §3.1／3.3／5～6、04 §4／5／7／9、05 §13～15。未發現 R9 契約衝突。

## 實作範圍與查詢

沿用 Controller 直接使用 `ApplicationDbContext`、`[Authorize]`、`CurrentUserContext`、既有 Error Envelope 與 DTO 模式，只完成端點 16～17。

| GET（Base：`/api/v1/forms/{formId}`） | 實作與查詢策略 |
| --- | --- |
| `/responses/me/latest` | 在 `ResponsesController` 新增 GET；先讀未刪除 Form 的必要欄位，再以 `(formId, CurrentUserId)` 在 PostgreSQL 執行 `submitted_at DESC, id DESC LIMIT 1`。選定 Response ID 後，只查該份 Answers，依 Question `sortOrder ASC, id ASC` 排序，沿用 `ResponseReceipt` 回 200。 |
| `/respondent-count` | 在 `FormsController` 新增 GET；未刪除 Form 存在且 Owner 通過後，在 PostgreSQL 對目標 Form 的 Response `user_id` 做 `Distinct().CountAsync()`，回 200 `{ "count": n }`；與現有 Form List count 語意一致。 |

全部使用 `AsNoTracking` 與必要欄位投影，沿用現有索引，不載入所有歷史、完整 Form Definition 或 Users／Options。多段讀取放在短生命週期的 `RepeatableRead` 交易內，讓 Form 狀態／Soft Delete 與查詢資料使用同一快照；不取 `FOR UPDATE`，不呼叫 `SaveChanges`。Delete 已在快照建立前完成時回 404；讀取先建立快照時可回該時點一致結果，不新增 GET／寫入線性化契約。

Latest 的排序在 DB 完成，同時間明確以 UUID `id DESC` 決定；不以插入順序或 Client 時間代替。只讀最新整份 Response，optional 缺答不補舊答案、不補 null；即使最新整份沒有 Answers，也回 200 與 `answers: []`。保留 DB 正規化 JSONB 值及 timestamp，不重新驗證、正規化或轉成有限精度數值。

Count 依成功 Response 的不同 User 去重；multiple 歷史只算一人、Owner 自己提交也算、零 Answer 的成功 Response 仍算。呼叫者必須 Active，但既有填答者後來 disabled 不會抹除其成功提交或扣除人數；不以 Answer join、User status 或每人最新答案內容篩掉填答者。

## 權限、空資料與錯誤順序

兩支 GET 均不要求 Body／JSON Content-Type。沿用完整 Authentication → UUID 解析 → 未刪除 Form 查找 → 各端點權限／狀態分支 → 資料查詢，逐階段 early return。Auth 失敗先於壞 UUID／資源判定，沿用既有 401／403 Auth Codes；有效身分的壞 UUID 回 `400 VALIDATION_ERROR`，不存在／Soft Delete 一律 `404 FORM_NOT_FOUND`，不從保留的子列繞過 Form 邊界。

| 情境 | My Latest | Respondent Count |
| --- | --- | --- |
| Draft Owner | `404 RESPONSE_NOT_FOUND`，不查歷史 | 200，正常空資料 `count=0` |
| Draft 非 Owner | `404 FORM_NOT_FOUND` | `403 FORM_ACCESS_DENIED` |
| Published／Closed | 只查本人；無本人 Response → `404 RESPONSE_NOT_FOUND` | Owner 200；非 Owner → `403 FORM_ACCESS_DENIED` |

Deadline、resultVisibility、responseIdentity 不改變兩端點權限或既有資料讀取；Count 在三狀態空資料皆回 0。Receipt 不含 userId／Answer ID，Count 不揭露 Answers 或填答者清單；Owner 也只可讀自己的 Latest。

## 必要開發測試與主要檔案

- 沿用 MSTest／TestFactory／真實 PostgreSQL／既有 Clock 與合成資料 helper：single／multiple、較新整份略過 optional／全部缺答、同時間已知 UUID tie-break、跨 User／Form 隔離、Receipt 全題型保存值與 Answers 排序；Count 多人／重複提交／Owner／零 Answer／disabled 歷史，以及與 Form List count 一致。
- 覆蓋上述狀態／身分／空資料矩陣，缺 Cookie、無效／過期 Token、Token 對應 User 不存在／disabled、Auth＋壞 UUID、已刪除＋非 Owner 等代表性錯誤優先序；截止後仍可讀、Visibility／Identity 不影響權限。以既有 interceptor 控制讀取與 Delete 先後，驗證快照一致及已刪除不可存取；DB 快照確認 GET 不改業務列／timestamps。只清理測試自身資料，保留既有驗收 fixtures。
- 預計修改 `src/CustomFormService.Api/Controllers/ResponsesController.cs`、`Controllers/FormsController.cs`；新增 `Contracts/Responses/RespondentCountResponse.cs`、`tests/CustomFormService.IntegrationTests/FormEndpointsTests.ResponseQueries.cs`。重用 `ResponseReceipt.cs`，僅在必要處做局部整理；不改 Entity、Mapping、Migration 或 Schema。
- 開發獲授權後執行 `dotnet restore CustomFormService.sln`、`dotnet build CustomFormService.sln --no-restore`、R9 focused tests 及同版全套 `dotnet test CustomFormService.sln --no-build`；實際測試數與結果寫入 `R9_Development_Report.md`，更新 Implementation Plan 開發進度、檢查 diff 並提交單一目的變更，交付「開發完成、待驗收」後停止。

**初次計畫交付紀錄（歷史）**：當時只有計畫文件，未開發、未執行 build／tests，未準備或執行驗收；待 codex-reviewer 審查及 Owner 確認後才開始 R9。

**本次開發交付**：核准範圍已完成；Windows build 0 warnings／errors，Windows 測試探索／原生 API 啟動受 application control 封鎖。Linux 同份 source／PostgreSQL focused 26/26、full 306/306 首次 PASS，四組實際 SELECT 快照競爭 PASS；既有資料未變。詳見開發報告的原生環境限制。不擴充 API／DB schema，不拆 A／B，未準備或執行正式驗收，未開始 R10；交付後停止於「開發完成、待驗收」。

**驗收準備（2026-10-05）**：Owner 已確認 `32bb41c` code review 通過並授權 Prepare；成對 [Markdown](../test-plan/R9_Acceptance_Test.md)／[.http](../test-plan/R9_Manual_Tests.http) 已備妥且只做文件靜態核對，所有 AI／人工結果仍未執行。Windows 原生 API 已知 policy 限制與本次 build／startup／health/readiness gate、同版 26 個 R9 案例對應與 full 新證據、資料保留與 probe finally 復原均明列；停止於「驗收計畫待 review」，未執行驗收／未開始 R10。

**AI 驗收交付（2026-10-05）**：Owner 核准 `17b1c2f` 並授權三項補強 `f8a8026` 後 Execute，另明確指示新會員註冊／登入 `f93d7fc`。首輪工具失敗及合法 fixtures 保留於 `2644eb3`；補驗 -02 Windows 本次原生 API／health/readiness PASS、同版 full 306/306（R9 26/26、四組實際 SELECT 快照）與 40 HTTP／4 非 HTTP 全 PASS。舊資料與首輪合法資料完整相同，本次 fixtures 保留、deadline 精確復原；未改業務程式／預期／schema，Linux 未執行、人工全部未執行。詳 [執行報告](R9_Acceptance_Execution_Report.md)及成對結果；停止於「R9 驗收結果待 Owner review」，未自行 accepted／未開始 R10。
