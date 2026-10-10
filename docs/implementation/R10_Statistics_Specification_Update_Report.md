# R10 Statistics 規格更新交付

日期：2026-10-10。狀態：**Statistics 規格修訂已獲 Owner 核准**；R10 **not started**。

## 1. 授權與文件版本

依 Owner 指示及附件 `02_Statistics_API_Amendment_for_R10.md`（2026-10-10）更新文件。附件作為需求輸入，其中後續實作計畫、開發與驗收的描述不是初次規格整理的執行授權。

**Owner 確認（2026-10-10）**：本 chat 明確確認「本次 Statistics 規格更新 review 通過，我確認採用四欄位追加及相關語意」，核准規格整理提交 `0e9c03e096f45fd21e27876edbf48299f463f05f`。本次修改的正式規格版本 v1.1（2026-10-10）現為已核准；其餘正式規格維持 v1.0。Implementation Plan 的 R10 引用已同步為核准契約。本次確認亦另授權精簡實作計畫，見 [R10_Implementation_Plan.md](R10_Implementation_Plan.md)；該計畫仍待 review，不代表開發或驗收授權。

## 2. 修改檔案與契約差異

| 檔案 | 修改 |
| --- | --- |
| [03c API Part C](../specs/03c-v1-api-contract-submission-statistics.md) §3.4／§7.3／§8.4 | Root 新增四欄位；同步完整欄位表、anonymous 成功範例及成功 Shape 引用；說明零資料、目前名稱、Creator disabled、表定 deadline 與不追加的欄位；釐清匿名隱藏的是 Respondent 身分 |
| [01 Domain](../specs/01-v1-domain-specification.md) STA-02～03 | 說明 Creator 目前名稱可公開與 Respondent 身分的區別；匿名限制明確指填答者 |
| [00 Overview](../specs/00-v1-overview.md) §7 | 同步自帶顯示資訊與建立者／填答者區別；修正 Statistics Shape 引用 Part B → Part C |
| [05 Constraints](../specs/05-v1-system-constraints.md) §5.3／§6／§20 | 同步資料揭露邊界；匿名不帶身分明確指 Respondent；完整 Root 引用 03c §3.4 |
| [Backend Implementation Plan](V1_Backend_Implementation_Plan.md) §4／R10／§8 | 更新 R10 範圍與精確規格引用，保留 not started；修正不建議建立 Skill 的過時說明 |
| 本交付報告 | 記錄契約差異、跨文件核對與待確認事項；不是個別實作或驗收計畫 |

`GET /api/v1/forms/{formId}/statistics` 成功 Root 由五欄位增為九欄位：

| 新增欄位 | 型別／出現規則 | 來源／語意 |
| --- | --- | --- |
| `creatorDisplayName` | String；必須出現 | Form Creator 的目前 `users.display_name`；不保存快照；不因 Creator disabled 排除 Form |
| `responseMode` | single／multiple；必須出現 | `forms.response_mode` |
| `resultVisibility` | owner_only／respondents／public；必須出現 | `forms.result_visibility` 的目前值 |
| `submissionDeadlineAt` | UTC ISO 8601 String／null；必須出現 | `forms.submission_deadline_at` 的表定截止時間；無期限回 null；維持保存值精度，不代表實際 Close 時間 |

既有 `formId`、`title`、`description`、`count`、`questions` 維持。四個新增欄位適用所有成功 Visibility 組合、anonymous／identified 及零填答者。未追加 Creator ID、responseIdentity、status、timestamps 或 Question config；`questions[].type` 已存在。§3.4 範例採 anonymous，identified Item 範例與規則仍見 §8.5。

## 3. 跨文件核對

| 核對來源 | 結果 |
| --- | --- |
| 00 §4～7 | Closed-only、到期不自動 Close 維持；自帶顯示資訊與名稱揭露摘要已同步 |
| 01 USR-02、FORM-01／07～09、SUB-06、STA-02～05 | 目前名稱、Definition Freeze、可更新 Visibility、Effective Response、各層 count、匿名／具名語意一致；只釐清受影響的身分文字 |
| 02 §5／7／10～12 | 四欄位均有既有來源；Creator 非 nullable FK，無 User Delete；無須修改 schema／Migration，DB 規格不變 |
| 03a §2／5～6／8～9 | camelCase、enum、UTC、Conditional Authentication、完整集合、GET 無業務寫入一致；無需修改 |
| 03b §3.5／§5.2／§8.3～8.4 | FormDetail 使用目前 Creator 名稱；改名與 Closed 後 Visibility 更新可反映新查詢；Close 不改 deadline；FormDetail Shape 不變 |
| 03c §7.1～7.2／7.4／§8 | 驗證次序、Access Matrix、Error、整份 Effective Response、題型集合／排序、計數與 respondents 規則保留；§8.4 僅釐清身分為 Respondent |
| 04 §4.3／§10 | Statistics 特殊錯誤優先序與既有錯誤碼一致；無新增或修改 Error，無需修改文件 |
| 05 §5.3／§6／§10～15／§20 | 釐清 Creator／Respondent 揭露；UTC、數值精度、排序、無分頁與 GET 無寫入維持 |
| AGENTS.md 與兩個 repository-local SKILL.md | 無過時或衝突指示，均未修改；流程文件不重複欄位契約 |
| Implementation Plan | R10 範圍追加投影與引用，仍 not started；§8 的舊 Skill 建議與現行 AGENTS.md 不一致，已修正為 repo 既有流程分工 |

## 4. 衝突與待確認事項

- 未發現附件與現行正式規格中既有存取、錯誤、聚合或匿名／具名規則的實質衝突。Root 五欄位 → 九欄位是本次明確授權的契約變更；原「匿名不含身分」的概括文字已限縮為 Respondent 身分，符合既有匿名填答政策。
- v1.1 的四欄位契約及公開目前 Creator 名稱（包含 anonymous + public）、必須出現／null／deadline 語意，已由 Owner 明確核准，無剩餘規格決策。
- R10 精簡實作計畫已另獲規劃授權，交付待 review；開發、驗收準備、驗收執行與 accepted 各依既有階段邊界，未提前開始。

## 5. 初次規格更新的檢查與未驗證事項（歷史）

- 使用 `Get-Content` 閱讀附件、最新 AGENTS.md、兩個 skills、八份正式規格及 Backend Implementation Plan；使用 `rg --files`／`rg -n` 核對文件與相關文字。
- 編輯前 `git status --short` 為空，分支 `master`；`git log -3 --oneline` 確認最新紀錄，起點 `7e10d71`。
- 交付前檢查 Git diff 與 `git diff --check`（exit 0，無 whitespace error），僅修改上述文件；提交 SHA 與最終 Git 狀態見交付訊息。
- 依本次授權未執行 build、自動化測試、HTTP、DB 操作或驗收；產品行為與新欄位實作尚未驗證。未修改產品程式、DB schema 或 Migration，未產出 R10 個別實作／驗收計畫。
