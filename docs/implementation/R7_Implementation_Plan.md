# R7 — Lifecycle 與 Result Policy 精簡實作計畫

2026-10-04；**development verified；test plan pending owner review**（Owner 核准計畫提交 `0f1f70a` 並明確授權開發及後續有限確認／Prepare）；實作提交 `0496041`、review 修正 `db2fe1b`，詳見 [開發報告](R7_Development_Report.md)。R6 accepted 依據：AI 證據提交 `81b64f3` 與 Owner review；文件收尾 `def2d2b`，人工未執行。程式基準 `af1a5a3`。本次只規劃 R7 端點 12～14。

外部 review 有限確認已完成，詳見 [確認報告](R7_Review_Confirmation_Report.md)：M2 本次未重現、根因未明；Visibility 局部補上 JSON Content-Type 驗證，Windows R7 49/49、全套 167/167 PASS，Linux 本次未重跑。M1 的 Delete／Submit／Close 共用 lock／交易要求已登記 R8 前置，未提前實作；不做 L2 重構。Owner 已授權接續 Prepare，仍需成對驗收計畫 review 後才能執行。

成對 [R7_Acceptance_Test.md](../test-plan/R7_Acceptance_Test.md)／[R7_Manual_Tests.http](../test-plan/R7_Manual_Tests.http) 已準備，15 組／42 個 HTTP 子項，所有 AI／人工結果未執行；未跑計畫中的 HTTP／DB 操作，停止待 Owner 審核。以下內容保留核准開發計畫及原開發交付紀錄，不能當作正式驗收結果。

依 [AGENTS.md](../../AGENTS.md)、[development skill](../../.agents/skills/v1-round-development/SKILL.md)、[acceptance skill](../../.agents/skills/v1-acceptance-testing/SKILL.md)、[Implementation Plan](V1_Backend_Implementation_Plan.md) R7；規格對照：[01](../specs/01-v1-domain-specification.md) FORM-01／03／04／07／09、CON-01／03；[02](../specs/02-v1-database-specification.md) §7～9；[03a](../specs/03a-v1-api-contract-common.md) §5～6；[03b](../specs/03b-v1-api-contract-auth-user-form.md) §8.2～8.4／9；[04](../specs/04-v1-error-catalog.md) §6～8；[05](../specs/05-v1-system-constraints.md) §8～10。未發現本範圍的規格衝突。

## Publish 驗證

鎖後以 `FormDefinitionCandidate.From(form)` 建立獨立 Candidate，於 `FormDefinitionValidator` 新增 Publish 入口，共用 `TryNormalizeQuestions` 的 Config／文字／數值／排序規則，補齊題型、Form title 與歸屬檢查。Draft Create／PATCH 仍允許無題、Choice 無 Option、Rating 缺 min／max；Publish 必須至少一題、所有定義完整有效、Choice 至少一個有效 Option、Rating 整數 `0 ≤ min < max ≤ 100`，非 Choice 無 Option。

在鎖後取一次 `TimeProvider.GetUtcNow()`，實際 DB deadline 非 null 必須嚴格晚於此 Publish Server Time；等於即拒絕。驗證只操作獨立 Candidate，不補缺少設定或回寫 Definition；`required=false` 也須符合完整性。失敗回 `400 FORM_NOT_PUBLISHABLE`，沿用正式 details（缺題 `REQUIRED_FIELD`、Choice `INSUFFICIENT_OPTIONS`、Rating `INVALID_QUESTION_CONFIG`、deadline `INVALID_VALUE`）；同階段可收集多筆，依 sortOrder／id 穩定建立 `questions[i]...` 定位。

## 狀態、權限與錯誤次序

三端點均為 Active Owner。Publish／Close：Authentication → UUID 解析 → 未刪除資源 → Owner → Lifecycle → Publish 完整性（僅 Publish）→ 原子寫入；不需要 Body、不要求 JSON，也不綁定／解析 Body。Result Visibility 才採 Authentication → UUID／JSON Body 可解析性 → 未刪除資源 → Owner → Body 欄位驗證 → 原子寫入。各階段 early return。依序回既有 Auth Error、`400 VALIDATION_ERROR`、`404 FORM_NOT_FOUND`、`403 FORM_ACCESS_DENIED`；非 Owner（含他人 Draft）優先於狀態／內容錯誤。

| 端點（Base：`/api/v1/forms/{formId}`） | draft | published | closed |
| --- | --- | --- | --- |
| POST `/publish`（不需 Body） | 完整性通過 → published | 409 `FORM_ALREADY_PUBLISHED` | 409 `FORM_INVALID_STATE_TRANSITION` |
| POST `/close`（不需 Body） | 409 `FORM_INVALID_STATE_TRANSITION` | → closed | 409 `FORM_ALREADY_CLOSED` |
| PATCH `/result-visibility` | 允許 | 允許 | 允許 |

成功均回 `200 FormDetail`。Close 不重驗凍結 Definition，不 Reopen／退回 Draft；Deadline 不自動 Close。既有 Definition PATCH（含空 PATCH）在 Published／Closed 回 `409 FORM_NOT_EDITABLE`，非 Owner 仍先回 403。

## Form lock、交易與獨立 Visibility

沿用 PATCH 的 `ReadCommitted` 與 `FormAggregateLock.TryAcquireActiveFormAsync`（`deleted_at IS NULL ... FOR UPDATE`）。R7 各寫入請求在自己的交易中，先取同一 Form row lock，再載入目前 Aggregate、判權限／狀態／驗證，持鎖到 commit／rollback。PATCH 先成功，Publish 必須讀新定義重新驗證；Publish 先成功，等待的 PATCH 讀到 Published 後拒絕。失敗完整回滾；同交易內 AsNoTracking 重讀保存後回應，commit 成功後才回 200。

Publish 正規化只操作獨立 Candidate，成功後只更新 Form status／updatedAt；完整 Definition、Questions／Options 與子列 timestamps 必須保持原樣，以 DB 全欄位快照及實際 SQL 檢查確認。Close 同樣只更新 status／updatedAt。Visibility 共用鎖但不套 Draft 限制、Publish Validator 或 R6 Writer；Owner 通過後驗證 Body 只含必填、非 null、非重複的 `resultVisibility`，僅接受 `owner_only`／`respondents`／`public`，錯誤回 `400 VALIDATION_ERROR`。相同值 no-op，所有 timestamps 不動；改值只寫 resultVisibility／updatedAt。Definition／子列不變，不改 responseIdentity、不影響 Form Detail 權限，也不提前開放 Statistics。Close 鎖供 R8 Submit 共用；本次不建立 Submit。

## 檔案、必要測試與停止點

- 預計修改 `src/CustomFormService.Api/` 下的 `Controllers/FormsController.cs`（三端點及局部解析／交易 helper）、`Validation/FormDefinitionValidator.cs`（共用檢查／Publish 入口）；`Infrastructure/Persistence/FormAggregateLock.cs` 至多更新用途說明。新增 `tests/CustomFormService.IntegrationTests/FormEndpointsTests.Lifecycle.cs`，沿用 TestFactory／PostgreSQL／TimeProvider。只做必要局部整理，無架構重構、Migration 或 R6 Writer 重寫。
- 開發測試：完整流程、Draft 可存但不可發布與 details、deadline 過去／等於／未來／鎖等待後時間、錯誤優先序、Freeze、三狀態 Visibility 更新／非法 Body／no-op、DB／timestamps 與 Save／commit 故障回滾。用可觀察的 lock barrier／interceptor 測兩種 PATCH／Publish 先後（含 PATCH 使定義不可發布）、重複 Publish／Close 一次成功另一次正式 409、Visibility 與狀態競爭無欄位遺失；不用固定 sleep 冒稱並行。只清測試自身資料，保留 R4／R5／R6 fixtures。
- Owner 已明確核准依本計畫開發並補充 Body／Candidate 保持原樣規則，不需重送計畫；Windows restore／build、focused tests 及必要全套回歸後更新報告、提交並交付「開發完成、待驗收」，停止。驗收計畫另待授權準備並先送審，核准且授權執行後才跑 AI；人工獨立記錄，最後 Owner 確認 accepted。Submit／Close／Delete 競爭留 R8，Latest／Count／Statistics、G1 留各自 Round。計畫初次交付未改業務程式、未跑測試；本次已完成已授權 R7 開發，Windows／Linux focused 37/37、全套 155/155 PASS，實際命令及初次失敗見開發報告；不把開發自測當正式驗收，交付後停止。
