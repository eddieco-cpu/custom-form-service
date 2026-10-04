# R8 — Submit／Response／Answer 精簡實作計畫

2026-10-04；**A 開發完成、待 review；B 尚未授權／未開始**。Owner 已 review 通過並授權六項局部補充及 A 開發；本次 A 證據見 [開發報告](R8_A_Development_Report.md)，不增加其他 review 停止點。

依最新 [AGENTS.md](../../AGENTS.md)、[development skill](../../.agents/skills/v1-round-development/SKILL.md)、[Implementation Plan](V1_Backend_Implementation_Plan.md) §3／R8。正式規格對照：01 §3、QST、SUB-01～05、CON-01／02／04；02 §10～11／15；03a §2／5～6；03b §8.1／8.3；03c §3.1～3.2／4；04 §4／6.1／9；05 §7～12／13.1／14～15／20。

## 現況與範圍

基準 HEAD `8108bda`，盤點時 working tree 乾淨。Response／Answer 實體、JSONB mapping、FK、`(response_id, question_id)` unique 與必要 indexes 已存在；沒有 Submit 端點、Answer validator 或 ResponseReceipt。PATCH／Publish／Close／Visibility 已採 `ReadCommitted` + `FormAggregateLock`，先鎖未刪除 Form、再讀 Aggregate，持鎖到 commit／rollback。Delete 尚無共用鎖與明確交易；Implementation Plan R8 已登記 R7 review M1 的局部調整。

沿用既有 `TimeProvider` DI、可替換 Clock／EF interceptors 的 TestFactory、`TextRules`、`RequestJson`、精確數值比較 `DefinitionNumber` 及 PostgreSQL lock waiter／故障測試模式。R8 只完成端點 15：`POST /api/v1/forms/{formId}/responses` 與必要依賴；Active User（含 Owner）可填答，anonymous 仍需登入並保存內部 User ID。不做 R9 Latest／Count、R10 Statistics、G1、Migration、容量政策或全面重構。

Repo 的 R7 仍記為「acceptance results pending owner confirmation」。本次 R8 計畫授權不代替 R7 accepted 確認，不改填歷史結果。

## A. Answer 驗證與正規化

新增無 DbContext／Clock 依賴、不修改 EF Aggregate 的 validator；輸入 Request JSON 與只讀 Questions／Options，輸出正式 details 或獨立正規化 Answers，保留原始 Array index。分開提供傳輸欄位檢查與答案驗證入口，讓 B 按正式階段呼叫。返回的 Request value 使用 Clone、重建值使用 SerializeToElement，保證來源 JsonDocument 釋放後仍可使用，不借用其生命週期。

### 驗證次序

依 03c §4.3，B 固定採以下次序：

1. 完整 Authentication → formId UUID／JSON／Content-Type 可解析性，拒絕未知／唯讀欄位。頂層只允許 `answers`；可安全巡覽的 Answer object 只允許 `questionId`／`value`，此時不先做題型驗證。
2. 交易內鎖未刪除 Form、重讀 Aggregate。不存在／已刪除 → `404 FORM_NOT_FOUND`；非 Published → `409 FORM_NOT_SUBMITTABLE`。Submit 不套 Owner 管理權限或 Detail 的他人 Draft 隱藏規則。
3. 取唯一 submissionTime → deadline → responseMode；依序回 `409 SUBMISSION_DEADLINE_EXCEEDED`、`409 RESPONSE_ALREADY_SUBMITTED`。
4. 驗證 answers 存在且為 Array、Item object／必要欄位／questionId UUID／重複 questionId；再驗證所有 Question 屬於目標 Form。必填／Array 檢查不移到步驟 1；「非 Published + 缺少 answers」必須先回 409 FORM_NOT_SUBMITTABLE。A 驗證兩入口的延後檢查行為；B 再驗證真正 Submit 的 HTTP 409。
5. Required 未作答 → Answer JSON Type／Config／範圍 → Choice Option 歸屬／multiple 重複 → 移除 Optional 未作答、形成正規化結果。
6. 寫入前重確認提交條件 → 原子保存 → 排序 Receipt。

跨階段 early return；第 5 階段可收集不同題目中可安全判定的多筆 details，不對未知 Question／錯型 value 強行判 Range 或 Option。Answer 錯誤統一 `400 VALIDATION_ERROR`：Required 完全缺題用 `questions.<questionId>` + `REQUIRED_FIELD`，已出現但未作答用 `answers[i].value`；錯型／非法值用 `INVALID_VALUE`；跨 Form／不存在 Question 用 `QUESTION_NOT_FOUND`，跨 Question／不存在 Option 用 `OPTION_NOT_FOUND`，均不回父資源 404。重複 ID 用 `INVALID_VALUE`，指向第二次出現的 `answers[i].questionId`／`answers[i].value[j]`。

### 各題型與 omission

| 題型 | 驗證與正規化 |
| --- | --- |
| text | 僅 String；沿用 TextRules 的 Unicode White_Space 頭尾 trim，保留中間空白、大小寫與原碼點序列，不做 normalization。非空值以 Code Point 數檢查 minLength／maxLength，含邊界；trim 後空字串視為未作答。NUL（`\u0000`）及孤立 surrogate 受控拒絕，合法 surrogate pair 接受且不刪除／替換字元；依實際 JSON parse／string decode／保存相容性階段回正式 400，能定位答案時用 answers[i].value + INVALID_VALUE。先實測既有處理，不宣稱 R6 已保護。 |
| number | 依 01 §3.3 的安全處理範圍，沿用 DefinitionNumber 精確比較 min／max，不經 decimal／double；超出既有 JSONB numeric 安全保存範圍回 INVALID_VALUE，不捨入，不直接列規格衝突。範圍依據為 PostgreSQL 17 numeric／JSONB（整數位 131072、小數位 16383、raw exponent guard int.MaxValue / 2，含零）；不新增欄位精度規則。保留精確數值不保證 Receipt 逐字保留 token；R10 必須按數值等值分組（1／1.0 同組）。 |
| rating | 沿用既有 ValueKind.Number + TryGetInt32 的 Integer 判定，min ≤ value ≤ max；補測 5、5.0、5e0 與超大值，不轉型／捨入。保存 Integer。 |
| single_choice | Option UUID String，須屬於該 Question；不用 label／Option value。保存一致的 UUID String。 |
| multiple_choice | String Array；每個 UUID 屬於該 Question，以解析後 UUID 判重，大小寫差異不能繞過。一題一筆 Answer、保存完整 Array；正規化 UUID 表示，保留 Array 順序。空 Array 視為未作答，不新增選取數限制。 |

Optional 完全省略、text trim 後空字串、multiple 空 Array 均不建立 Answer，不因 minLength 而變成必答；Required 缺題及合法空答案用 REQUIRED_FIELD。`0`／rating 下界是有效答案。明確 value: null 在 Required／Optional 一律 INVALID_VALUE、path 為 answers[i].value；缺失 Item value 是必要欄位缺少，於 Item Shape 階段回 REQUIRED_FIELD。錯型或非法 Option 不是 omission。全題 Optional 時 answers: [] 可通過 A；建立 Response 留 B。

**代表測試**：五題型成功／錯型；Unicode trim、補充平面字元長度、保留大小寫／碼點；text／number／rating 邊界（含非空 text 短於 minLength）；超過 decimal 精度的 number 比較及參數化 SELECT 的 JSONB 相容性／數值等值、安全範圍拒絕；Required 缺題／明示空值、Optional omission／null 拒絕、零答案；重複 UUID／Question 與 Option 歸屬、原始 index paths／early return，以及輸入定義與 JSON 不被修改。A 不宣稱 EF 保存或 Receipt round-trip，兩者留 B。

**修改與停止點**：新增 `Validation/SubmissionAnswerValidator.cs`（含必要小型輸入／結果型別）及答案測試檔；RequestJson 至多補 path 支援，DefinitionNumber 保留名稱與算法。Receipt／Controller／保存／Delete 留 B。build、focused tests、受影響回歸、diff／文件與單一目的提交後交付 A 並停止。A PASS 只代表 validator 開發驗證，不代表 Submit／R8 完成。

## B. Submit、原子保存與並行控制

新增 `ResponsesController`，沿用 Controller + DbContext 模式，接上 A 分階段入口。Body 手動解析參照 Visibility，讓 Content-Type／空 Body／壞 JSON 回正式 400，維持 Authentication 先行，避免 MVC 415 或 DTO validation 提前改變次序。

### Deadline 與 Clock

在交易取得 Form lock、等待結束後重讀並確認 Published，**此時只呼叫一次 TimeProvider.GetUtcNow()**。同一 submissionTime 用於嚴格 `< deadline`、寫入前重驗與明確設定 Response.SubmittedAt，不由 DB now() 再取樣。寫入前確認狀態／deletedAt／deadline／single，deadline 仍比較同一值，其他條件由持鎖與交易內查詢保護。

鎖等待跨過 deadline → 拒絕；取樣通過後驗證／commit 跨過 deadline → 仍可成功（01 CON-04）。Deadline 不自動 Close。測試 Clock 可推進並計數，只計 Submit 業務判定取時，不混入測資／JWT 取時。Receipt 使用 DB 實際 timestamp／JSONB 表示，沿用現有 PostgreSQL 微秒保存行為，不增加時間精度契約。

### 共用交易／鎖與保存

三操作各自有交易、鎖同一 Form row：解析 → Begin ReadCommitted → `deleted_at IS NULL ... FOR UPDATE` → 重讀 Aggregate → 各端點權限／狀態／規則 → 保存 → commit。持鎖到交易結束，錯誤／取消／例外 rollback，不使用等待前舊 snapshot。

- **Submit**：single 持鎖查 `(formId, currentUserId)` 是否已有成功 Response；等待者在前者 commit 後重查，最多一個 201，其他 409。不同 User 各可一次。multiple 每次新增完整 Response，不覆寫、無 Max N／Idempotency-Key，不新增 `(form_id, user_id)` unique。採同 Form 序列化以保持局部實作；暫不加每 User 鎖或新 Schema。
- **Close**：沿用現有共用鎖／交易、Owner／Lifecycle 規則，不重新驗 Publish Definition。
- **Delete 必須局部調整**：UUID 解析後 Begin ReadCommitted、共用鎖、鎖後讀 Form／判 Owner，再設定 deletedAt／updatedAt、Save、commit 後回 204。保持任一狀態可軟刪除，不改 status／子列；非 Owner 403、已刪除／重試 404、無 Body 契約不變。FormAggregateLock 只需更新用途說明。

| 先成功 commit | 等待後必要結果 |
| --- | --- |
| Submit | Close／Delete 可成功，Response／Answers 保留。 |
| Close | Submit 重讀 Closed → 409 FORM_NOT_SUBMITTABLE，零新增。 |
| Delete | 鎖查詢等待後不再找到未刪除列 → Submit 404 FORM_NOT_FOUND，零新增。 |
| 首個 single Submit rollback | 零殘留；等待者重判後仍可成功。 |

順序由取得鎖及成功 commit 決定，不由 HTTP 抵達時間決定。已知競爭以鎖後現況回正式錯誤；不把任意 DB 例外當 RESPONSE_ALREADY_SUBMITTED。提交前可確認故障完整回滾；COMMIT 已送達但結果未知時可能已成功，不能推論零新增，不新增重試框架。

全驗證通過才建立 server ID 的 Response + 正規化 Answer graph、同交易保存；正確設定外鍵／entity state，避免誤插入 User 或修改凍結 Definition。交易內 AsNoTracking 讀實際保存結果、建立 Receipt，commit 成功後才回 201。Receipt 僅 id／formId／submittedAt／實際 answers（questionId／value），按 Question sortOrder ASC、id ASC 排序，不回 Answer ID／userId、不補 null。Submit 不更新 Form／Question／Option timestamps。

**代表測試**：

- API：Owner／其他 Active User、anonymous 需登入、disabled／壞 Cookie；五題型 Receipt／排序／JSONB／零答案；Definition／timestamps 不變。錯誤次序覆蓋未登入 + 壞 Body、非 Published + 壞答案、deadline + single、single + 壞 answers、傳輸錯誤 + 不存在資源。
- Clock：無 deadline、之前／相等／之後；鎖等待中推進到 deadline；判定後延遲 commit 並推進時鐘仍成功、提交時間不變，無二次業務取樣。
- 真 DB 並行：single 同 User 一個 201／一個 409、不同 User 各成功；multiple 同 User 兩個 201、兩份歷史（較新可略過 Optional）；Submit→Close、Close→Submit、Submit→Delete、Delete→Submit。gate／waiter／請求完成均用有限等待；finally 釋放 gate、取消／收尾請求與交易。未觀察到真 DB waiter 必須 FAIL／BLOCKED，不得宣稱並行 PASS。
- 原子性注入點：(a) 同交易已 INSERT Response 及至少一筆 Answer、其餘 Answer 尚未完成時注入 SQL 故障；(b) 全部 Save 成功後，在 TransactionCommittingAsync／送出 COMMIT 前注入 SQL 故障；另一 connection 確認全部回滾，single 等待者仍可成功。Delete 在 UPDATE 後／送出 COMMIT 前用同類注入，確認原狀保留。這些證據僅涵蓋已確認提交前失敗，不涵蓋 COMMIT 已送達的結果未知情境。重跑 Delete 權限／三狀態／重試與 R7 lifecycle／競爭回歸。

**修改與停止點**：新增 `Controllers/ResponsesController.cs`、`Contracts/Responses/ResponseReceipt.cs`、Submit／Clock／Concurrency 測試；局部修改 FormsController.Delete、FormAggregateLock 用途註解及必要測試工具。不改 Schema／Migration 或提前加 R9／R10 路由。執行 `dotnet restore CustomFormService.sln`、`dotnet build CustomFormService.sln --no-restore`、測試專案 focused tests（`dotnet test ... --no-build --filter ...`）與完整回歸；報告實際命令／filter／TRX／平台結果。只清本次合成測資，保留既有 fixtures，不刪開發 volume。

完成必要驗證／diff，更新 R8 開發報告與 Implementation Plan 開發狀態，以單一目的 commit 交付後停止於 **「R8 開發完成、待驗收」**。B 可拆 Delete／Submit 單一目的提交，兩者之間不新增 review gate。A review 不自動授權 B；B 交付不自動準備／執行正式驗收，成對計畫另待授權，執行仍需 Owner 核准與明確授權。

## 衝突與本次交付

未發現 R8 正式規格衝突。03c §4.3 的寫入前 deadline 重確認與 01 CON-04／05 §10 單一時間相容：重驗使用同一 submissionTime；02 §10.3 的 now() 是 Schema default，API 明確寫入該取樣值。Delete 缺鎖／交易是已登記的實作待補事項，非 Frozen Contract 衝突。

初次計畫提交 1a0cf46 未改業務程式／執行測試。Owner 已核准本計畫及六項局部補充，直接授權 A；本次 A 實跑結果另記 R8_A_Development_Report.md，A 交付後停止待 review，不開始 B。
