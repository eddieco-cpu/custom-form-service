# R6 — Option Persistence 修正與 Candidate 重構評估

日期：2026-10-03。階段：開發修正完成、待驗收；不是驗收計畫或驗收結果。

## 1. 範圍與契約

本次只修 R6 PATCH 的新增 Option 正規化／追蹤方式，補 Owner 指定的混合 Delta 與兩段保存回滾測試。未實作 R7／G1，未自動啟動 Candidate 全面重構，未修改正式規格、Mapping、DB Constraint 或 Migration。

依據：03a §2.2／§9 一般文字 trim、原子性及 no-op；01 FORM-05／06、QST；02 §3.3／§8～9 時間欄位與 `(question_id, value)` 唯一性；03b §7.2 完整 Candidate 正規化、Type Change 及 Delta 驗證順序；04 §2.4／§6 details 定位及錯誤代碼；05 §8～10 交易、並行與受控錯誤。

本次使用 `v1-round-development`。依 AGENTS.md 的明確階段界線及 Owner 指令，停止於開發交付；Implementation Plan §3／§6 的舊版「功能完成後產出驗收文件」文字不視為本次接受準備／執行授權。沒有 R6 成對驗收文件或既有正式結果需要改寫；先前 6/6、10/10 僅為歷史開發證據，不涵蓋本次新增案例。

## 2. 確認的問題與修正

修正前先加入兩個正規化測試，對原程式實跑，結果 **2/2 FAIL**：既有 Question 新增 Option、新增 Question 帶 Options 都把含首尾 Unicode 空白的 Request 原始 value 回傳／保存，與驗證後的 Candidate 不一致。這不是僅由靜態 review 推定的 bug。

實際修正：

- `DraftDefinitionDelta.TryReadOptionValues` 在建立 Option 或比較既有 value 前，使用既有 `TextRules.Trim` 正規化；保留大小寫與內部空白。
- 所有新增 Option 與既有 value 修改的 replacement 均先保持未追蹤；寫回 Candidate 正規化結果後，交由原有 transition writer 保存。顯式設定 Question navigation，混合操作時不依賴 EF 提前追蹤／修復關聯。
- 延遲清單改名為 `deferredOptions`／`deferredOptionWrites`，反映它同時包含新增項與 replacement，而非只有 replacement。
- Writer 仍在 Form aggregate 的同一交易內先保存舊列刪除與其他變更，再追蹤／保存新 Options。value 修改的既有 Option 保留原 ID、createdAt；updatedAt 依實際差異更新。trim 後相等的 value 不建立 replacement，維持 no-op。
- 保留 Form row lock、驗證優先序、Path 映射、Snapshot 時間更新與 AutoDetectChanges 保護。本次沒有藉重構移除它們。

變更檔案：`DraftDefinitionDelta.cs`、`OptionValueTransitionWriter.cs`、`FormsController.cs`（清單命名）、`FormEndpointsTests.cs`（partial、補強重複 ID 第二次 Request 位置斷言）、新增 `FormEndpointsTests.OptionPersistence.cs`，以及本報告與 Implementation Plan。

## 3. 實際測試證據

新增測試沿用既有 TestFactory、真 PostgreSQL、唯一合成會員與原有 cleanup；只清除本次自建 fixtures，沒有重建 DB、移除 volume 或刪除保留的人工驗收資料。

| 案例／測試名稱 | 實際結果與斷言 |
| --- | --- |
| `Patch_normalizes_new_option_values_in_existing_question_before_tracking` | 200；一般空白、U+2003、U+00A0 正規化；PATCH／GET／DB 一致；新增 ID、createdAt=updatedAt；舊 Option 全列不變；再送 trim 後相等 value，完整 DB 快照不變。 |
| `Patch_normalizes_option_values_in_new_question_and_rejects_normalized_duplicates` | 200；新增 Question／Options 的 value 正規化；`Alpha` 與 `alpha` 可共存；ID／時間欄位及 GET／DB 一致；再新增 trim 後重複 value 回 400 `FORM_DEFINITION_INVALID` 且零變更。 |
| `Patch_persists_mixed_option_and_question_operations_atomically(1)` | 同一 PATCH 新增 Choice Question／空白 value Options，並修改另一題既有 Option value；200，無 NRE／500；新增值正規化，更新保留 ID／createdAt。 |
| 同上 `(2)` | 同一 PATCH 刪除 Question A，並修改 B 的 Option value；200；A 與全部 Options 消失，B 正確保存，第三題完整 DB 列不變。 |
| 同上 `(3)` | 同一 PATCH 在 A 新增、更新 value、刪除不同 ID 的 Options；B Choice→text 省略 config／options；200；B Config=`{}`、Options 清空，A 正規化／更新／刪除正確。 |
| `Patch_rolls_back_first_save_when_second_save_has_database_failure` | 測試 interceptor 記錄第一次 SaveChanges 已完成，並用同一交易連線查到 Form／Question 更新、Option 更新／刪除、Type 清理及新 Question 寫入。第二次 SaveChanges 的 SavingChanges 階段執行 `SELECT 1 / 0` 注入真 PostgreSQL 錯誤；回 500 `INTERNAL_SERVER_ERROR`、不洩漏 SQL／exception。重新 GET 完全等於原回應；全部 Form／Question／Option DB 列（含 ID、created_at、updated_at）完全等於原快照；第一次插入的新 Question 與第二次待插入的新 Option ID 查詢均零殘留。這是故障注入，不宣稱自然 DB constraint 已故障。 |

三組混合測試逐欄比對 PATCH／GET（Form updatedAt 只容許 PostgreSQL 微秒截斷的 <10 ticks 差異）及 GET／DB，確認歸屬、題型、Config、排序、label／value；DB 檢查更新列的 createdAt 保留、updatedAt 與本次 Form 時間相同、新列 createdAt=updatedAt，以及未變更列完整保留。

實跑命令與結果：

```powershell
# 修正前的重現：2 FAIL；不可記成 PASS
dotnet test tests/CustomFormService.IntegrationTests/CustomFormService.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~Patch_normalizes" --logger "console;verbosity=normal"

# 成功的最終建置：0 warning / 0 error
dotnet build CustomFormService.sln --no-restore --configuration Debug -m:1 -p:UseSharedCompilation=false

# 修正後 focused suite：16/16 PASS，0 skipped；最終一輪 12.8846 秒
dotnet test tests/CustomFormService.IntegrationTests/CustomFormService.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~FormEndpointsTests --logger "console;verbosity=normal"

# 全套回歸：61/61 PASS，0 skipped；最終一輪 11.1485 秒
dotnet test CustomFormService.sln --no-build --configuration Debug -m:1 --logger "console;verbosity=normal"

# 下列四個檔案的 whitespace 檢查：exit 0（需沙箱外 named pipe 權限）
dotnet format whitespace CustomFormService.sln --verify-no-changes --no-restore --include src/CustomFormService.Api/Infrastructure/Persistence/OptionValueTransitionWriter.cs src/CustomFormService.Api/Validation/DraftDefinitionDelta.cs tests/CustomFormService.IntegrationTests/FormEndpointsTests.cs tests/CustomFormService.IntegrationTests/FormEndpointsTests.OptionPersistence.cs
```

環境／未通過紀錄：一般 `dotnet build ... --no-restore` 與加 `--configuration Debug` 的平行建置都被取消，MSB5021，沒有編譯錯誤；上述單 worker／非 shared compilation 建置成功。全套 solution 測試未加 `-m:1` 時無結果，不算 PASS；改用上述命令完成。格式檢查沙箱內因 MSBuild named pipe `UnauthorizedAccessException` 未能執行，沙箱外重試才取得實際診斷。

包含 Controller 的 whitespace 檢查沒有全數通過：`FormsController.cs` 約 630、658、686 行三個 switch expression 有既有縮排問題；已用 `git show HEAD:...` 確認原本即如此，本次 Controller diff 只有延遲清單命名。沒有為使格式檢查變綠而擴大修改；其餘上述四個檔案檢查通過。這不是完整 `dotnet format` 所有分析器通過的宣告。

## 4. 有限 Candidate 重構評估（尚未實作）

建議：**值得在 R7 前安排一次另行授權的有限 R6 內部重構，但不是本次已確認修正的阻塞條件。** 本次先修正與驗證，再交付此方案；不自行開始全面重構或 R7。

建議流程：在取得現有 Form lock 後，從目前 Definition 建立不含 EF Entity 的記憶體 Candidate；套用已解析的 Delta，完成 trim、Type／Config／Options 語意及完整驗證；以原 Definition 與正規化 Candidate 的結構差異產生 ChangeSet，最後只套回實際差異。

| 現有機制 | 有限重構的具體替代／仍需保留 |
| --- | --- |
| 驗證前直接修改 tracked Form／Question／Option | 移除；改在 Candidate records 上操作，失敗時沒有 tracked graph 的中間狀態。通過後才建立新增 Entity／套用差異。 |
| Entity graph 序列化成 JSON，再借用 Create validator 解析 | 抽出 typed Definition 正規化／驗證核心；Create 的 JSON adapter 與 PATCH Candidate 共用同一核心。可移除來回序列化，但 R5 Create 契約保持不變並須回歸。 |
| 全量回寫所有 Q／O、每次產生新的 Config JsonElement | 改為 ChangeSet 的新增／刪除／真正變更欄位；Config 用結構相等判定，未提及／未改列不重新賦值。保留完整 Candidate 驗證，不改成只驗 Request。 |
| 字串 Snapshot 判定 no-op／時間與手動整列 Modified | 改用 ChangeSet 判定 no-op、受影響父 Question／Form，以及逐欄差異；仍須保留「實際變更才更新時間」與原 ID／createdAt 的語意。 |
| 在 tracked navigation 混放 detached replacement／new Option，再移除／重附加；Controller 廣域關閉 AutoDetectChanges | Candidate 與 insertion plan 不進入 tracked graph；舊 key 刪除 flush 後才建立／追蹤新列。可移除這種暫態 graph 與廣域抑制；writer 是否還需局部抑制，以實際 tracker／SQL 回歸確認，不先假設 EF 自動修復全部安全。 |
| 同 ID delete → save → insert | **仍須保留等效持久化方案**：Candidate 不能解除 EF Alternate Key 的不可直接修改限制，也不能讓 PostgreSQL immediate Unique 忽略中間狀態。一般 value 修改（不是只有交換）仍需 replacement；保留 ID／createdAt、更新 updatedAt。無 key transition／刪除重用時可由 ChangeSet 安排單次保存，但有轉換時仍需兩段原子保存與本次回滾測試。 |
| Form row lock、交易、授權／Draft 檢查、驗證優先序 | 全部保留；未把單純交易當成並行方案。R7 Publish 接續相同 lock 規約的實作及 PATCH／Publish 實測仍留給 R7。 |

Alternate Key 證據：`OptionConfiguration.HasAlternateKey(QuestionId, Value)`；現有 writer 的 replacement 路徑及 value 修改／交換／重用／本次三組混合測試已在真 DB 通過。不要把 Candidate 重構說成「value 可直接更新 tracked Entity」；不得改成移除 Unique、變更 Constraint 或新增 Migration 來規避。若要選直接 SQL 更新，還會新增 tracker 同步與 Unique 排序責任，本方案不建議引入。

建議修改範圍為 `DraftDefinitionDelta`（拆解析／Candidate 套用）、新增 Candidate／ChangeSet 資料結構、`FormDefinitionValidator` 的 typed 核心與 JSON adapter、現有 writer／Controller 編排，及相關整合測試。不要重寫其他端點、改公開 Request／Response、加獨立 Q／O API 或完成 Publish。重構後必須重跑本次 16 個 Form 案例、全套回歸及實際 SQL 差異檢查，尤其 value 的同 ID 保存、混合操作、no-op、兩次保存回滾與競爭。

## 5. Path 與交接

本次沒有新增或移除 Path 映射。**不建議直接刪掉錯誤定位能力。** 未來 typed validator 可把 source metadata（原 Request Question／Option index 與欄位路徑）帶在 Candidate 上，取代目前解析 Candidate index 再反查 ID 的實作；這只是內部定位替代，不改 04 §2.4 的外部契約。碰到與未提及列的排序／value 衝突，必須定位到造成衝突的 Request 項目，不能直接輸出未排序 Candidate 的集合索引。

重複 ID 仍在 Delta preflight 回原 Request 第二次出現的位置；不交給 Candidate 排序後的索引決定。若新定位方式不能證明同等定位能力，就保留現有映射，不為簡化程式降級契約。本次保留原有 path 回歸，並在既有原子性測試補強斷言 `questions[1].id`、`questions[0].options[1].id`，沒有變更定位功能。

待處理：Owner 決定是否另行授權有限重構；Controller 既有排版診斷暫緩；正式 R6 成對驗收計畫與 Owner 審核／執行／人工確認仍未進行。R6 可進入驗收文件準備階段，但本次停止於「開發完成、待驗收」，等待明確準備文件指令。若先採用重構，須以重構後已驗證的 commit 準備計畫。
