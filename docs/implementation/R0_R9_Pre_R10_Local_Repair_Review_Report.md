# R10 前局部修正獨立 review

日期：2026-10-10。結論：**F-01、F-02、F-03、S-01 均可結案；適合進入整合驗收文件更新。** 沒有確認的產品缺陷或阻擋問題；有一項 P3 測試證據改善建議。本報告不更改歷史 accepted／Owner pending 或修正開發狀態，也不代表正式驗收通過。

## 審查基準與範圍

- 產品／測試提交：`e22db98df4d7f23440e628327e0c571ba0745828`，parent 為 `b491bf7ddd2666ed45a6396191d4f362ca5b0bce`。
- 起始 HEAD：`b6961fbbb0a4f806a2973a4ed9758603a5a192c5`，分支 `master`；`git status --short` 無輸出。`git diff e22db98 HEAD -- src tests` 無輸出；後續只有開發報告及 Implementation Plan 文件變更。
- 直接審查目標提交的七檔 diff、完整受影響方法與 `FormEndpointsTests.LocalRepair.cs`，並追查 Auth／User、Form DTO、candidate／change set／writer、共用 loader、filter 與 middleware 順序、fixture cleanup、既有 no-op／rollback／snapshot 測試。沒有以開發報告或 PASS 數取代程式審查。
- 依最新 [AGENTS.md](../../AGENTS.md)、核准 specs 00～05、[Implementation Plan](V1_Backend_Implementation_Plan.md)、[局部修正計畫](R0_R9_Pre_R10_Local_Repair_Plan.md) 與 [開發報告](R0_R9_Pre_R10_Local_Repair_Development_Report.md)。AGENTS／specs／repo skills 自前次整體 review 基準 `6f61a16` 無 diff；重新核對本次涉及的 03a §2／4～6／9、03b §4～8、04 §2～4／6、05 §8～13。未發現契約衝突。
- 已讀 development skill 的範圍／handoff 規則；本次明確只授權 review，沒有啟動其實作流程或 acceptance skill 的準備／執行流程。R10、G1、I-01 README、I-02 List Count／items 快照均不屬此次修正。

## Findings（按嚴重程度）

**已確認產品缺陷：無。待驗證產品疑點：無。**

### RV-I-01 — P3／非阻擋：Publish barrier 的 fresh GET 未完整比較頂層 DTO

位置：[FormEndpointsTests.LocalRepair.cs:308](../../tests/CustomFormService.IntegrationTests/FormEndpointsTests.LocalRepair.cs#L308)，特別是第 312 行 `if (!publish)`；Publish response 在第 291～294 行只比較 status。

- 觸發：`Repair_S01_detail_split_query_returns_one_complete_version("root", true)`，暫停 GET 後 Patch、Publish 提交，再執行 fresh GET。
- 實際：暫停中的 GET 有**完整 V0 DTO** DeepEquals；fresh GET 比較 Published status、title、完整 Questions／Options，但跳過完整頂層 DTO 比較。description、responseMode／Identity、resultVisibility、deadline、creator、createdAt／updatedAt 沒有在此分支逐欄核對。DB afterWriter 比較可證明 GET 前後 Definition rows 相同，不能代替 DTO 與 DB 的內容對應。
- 期望：若此案例要支持開發報告「下一 GET 為完整 Published V1」的逐欄證據，應保留 Publish committed DTO，將 fresh DTO 與其完整比較，或用既有 DB→DTO helper 核對所有欄位。
- 影響：這個分支對 fresh GET 頂層欄位的回歸偵測較弱；**不是已觀察到的不一致回應**。非 Publish 兩案已完整比較 fresh V1，三案均完整比較 paused V0；既有 Publish Definition 不回寫／timestamps 測試及此次完整 suite 也通過，故不阻擋 S-01 結案或文件更新。
- 修正方向：後續有測試維護授權時補強既有 assertion 即可，不必新增併行矩陣。本次不修改測試。開發報告的這句宜理解為已有上述範圍的證據，不能宣稱此 Publish 分支已逐欄比對完整 fresh DTO。

## 四項結案判定與程式依據

| 項目 | 判定 | 直接核對結果 |
| --- | --- | --- |
| F-01 | 可結案 | [FormsController.cs:175](../../src/CustomFormService.Api/Controllers/FormsController.cs#L175)：page 已驗為正 int、pageSize 1～100；`page-1` 不會溢位，乘法先轉 long，最大 offset 214748364600。只有 `offset < totalItems` 的分支才轉 int，而 CountAsync 回 int，故轉型安全。超界保留 page／pageSize／totalItems／totalPages 並回 200 空 items；原 Owner／status／deleted 篩選、排序及不同 User count 保留。三個 wrap 反例各有六張 Form，能辨識錯頁而非碰巧空頁。 |
| F-02 | 可結案 | [RequestJson.cs:75](../../src/CustomFormService.Api/Validation/RequestJson.cs#L75)：catch 僅包住字串／property-name 解碼，沒有全域吞掉 InvalidOperationException。頂層仍 VALIDATION_ERROR，nested 仍 FORM_DEFINITION_INVALID，type／config 專用 detail code 保留；壞 key 定位 enclosing object。Register shape 先於值、Create 頂層先於 nested、Patch Resource→Owner→Draft 先於可解析 object 內的值／key／Delta 驗證；Visibility 同樣保留 Resource／Owner 順序。Auth／R8／unexpected server fault 均有同版回歸證據。 |
| F-02 config | 可結案 | [DraftDefinitionDelta.cs:396](../../src/CustomFormService.Api/Validation/DraftDefinitionDelta.cs#L396) 先 Clone；Clone 不解碼內容也不寫入 EF。Patch 第 336 行先完整 normalize／validate，再於第 345 行 DeepEquals change set；不合法 config 不會進 writer。candidate 與 EF graph 分離，語意等值 config（trim、property order、等值 numeric spelling）HasChanges=false，不呼叫 writer、不更新 timestamps。原 ReadCommitted＋Form lock＋兩階段 Save＋Commit 仍包住全部寫入；既有語意等值 no-op、second-save DB failure 及 commit failure 回滾測試本次均通過。 |
| F-03 | 可結案 | [Program.cs:17](../../src/CustomFormService.Api/Program.cs#L17) 以 instance 註冊 IOrderedFilter；[UnsupportedJsonContentTypeFilter.cs:11](../../src/CustomFormService.Api/Validation/UnsupportedJsonContentTypeFilter.cs#L11) order=-3001，先於內建 -3000 的 unsupported-media filter。僅當 ModelState 存在 UnsupportedContentTypeException 才 short-circuit；其他 invalid model state 繼續原 factory。Middleware Authentication／Authorization 在 MVC 前。五個 MVC body routes 現回正式 400 body／INVALID_VALUE；手動 Visibility／Submit、正常 JSON、malformed／missing body、Logout 及無 Body Publish／Close 的既有行為均有 suite 或 native 證據。 |
| S-01 | 可結案 | [FormsController.cs:255](../../src/CustomFormService.Api/Controllers/FormsController.cs#L255) 的 RepeatableRead 在 loader 前開始，涵蓋 root／Questions／Options split SELECT、resource／draft Owner 判定、DTO 建構，Commit 成功後才回應。DTO 的 Questions／Options 以 ToArray 完成，沒有延後查詢逃出交易；404／exception 時 await using dispose。只改 GetDetail，沒有把交易放入共用 loader 或改 writer 的鎖／隔離層級。三個 barrier 與新 full run 都命中並回完整 V0；兩個非 Publish fresh GET 全 DTO 為 V1，Publish 分支有 RV-I-01 的證據改善空間。 |

## Barrier 與證據核對

### S-01 harness

`RepairDetailGate` 第 374～390 行在 ReaderExecutedAsync 才暫停，限定 Form ID parameter 與第一個 GET ContextId；writer 只在 Reached 後啟動，不能先搶到 context。SQL 的 `JOIN questions`／`JOIN options` 區分 split 階段；Reached 時 completed count 分別為 1／2，Release 前再核對 count 不變且 GET 未完成，Release 後 count=3。這不是僅看到 transaction begin 或用 sleep 推測時序。

Patch HTTP 200 在 controller Commit 後才回傳；在 gate 尚未釋放時，另用獨立 Npgsql connection 查詢確認 V1 form／option rows 可見。Publish 分支另等待 Publish 200（同樣 Commit 後回應），再獨立讀 Definition rows。finally Release、cancel 並逐一 drain GET／Patch／Publish；40 秒共用 cancellation、10 秒 gate／read／drain 等待有界，非預期 worker exception 不吞掉。每案完整比較 paused GET 的 V0，Definition rows 前後比較含所有 IDs、config、sortOrder 與 timestamps。

### 保留的開發證據

| 證據 | 獨立核對結果 |
| --- | --- |
| `before4/before4.trx` | 71 executed，55 FAIL／16 PASS、0 skipped。F-01 3 FAIL：-200→500、wrap-to-4→兩筆、wrap-to-0→四筆。F-02 44 decode rows FAIL、優先序／合法 pair 1 PASS；Create／Patch title 是 500 的有效單一 title 反例。F-03 五個 MVC FAIL、兩個手動 PASS。S-01 三案均在完整 V0 DeepEquals 失敗，actual body 為舊 root＋新 child 或舊 Question＋新 Options，並記錄 no transaction、正確 count／SQL／ContextId 及 writer commit 先於 Release。Auth／server invariant 13 PASS。 |
| `before`／`before2`／`before3` | 不把 empty-cookie harness failure、錯 Visibility payload、Create timestamp 精度、重複 title 的提前 shape rejection 或 Windows application-control discovery failure當作有效反例。以上缺陷在 before4 的可用 evidence 已排除；本次沒有重新還原產品執行 baseline。 |
| `focused-delivery/focused-delivery.trx` | 171 executed／171 PASS，0 failed／notExecuted；包含完整 71 新增 rows。三個 S-01 stdout 均 RepeatableRead、指定 count／ContextId 與 V0 actual。 |
| `full-delivery/full-delivery.trx` | 377 executed／377 PASS，0 failed／notExecuted；相同三個 S-01 證據，包含 config no-op／timestamps／故障回滾及 R8／R9 回歸。未使用較早 focused2／full 取代最後 delivery。 |
| 原生 Kestrel | `native-process-evidence.json` 的 dotnet executable／DLL path／PID 39276 對應 Windows Kestrel；`native2-tests.log` 與 `native-f8e33e15abd2495dabb972e4b1df2202-http.jsonl` 有 67 PASS。實讀腳本確認 raw escaped JSON、UseCookies=false、明確 Cookie、各預期 code／path、分頁完整 assertions、拒絕前後 DB digest。health／readiness 200、無 Body lifecycle、Logout、認證優先序均有實際 response。不是正式整合驗收。 |
| DLL／來源對應 | 原生記錄 SHA-256 `82594C4EE9374DBBF0D1902C09D06371675352FCAB11EDD906736F6A9A75F1A8` 與本次 build 前仍保留在 tests/bin 的交付 API DLL **完全相同**。該處 API PDB 的 54 個非 obj repo source checksum、Tests PDB 的 18 個 source checksum均匹配目前 source（含最終 LocalRepair cleanup）。目前 source／tests 與 e22db98 無 diff，支持 native 產品與最終 suite 的交付來源一致。checksum 核對先於本次 rebuild；摘要保留於本次 RunDir。 |

限制：歷史 TRX 沒有附不可變的每次 source／binary manifest，before4 的 baseline DLL／PDB 未保留，無法從本機資料以 cryptographic hash 重新證明其全部 parent bytes；上述反例有效性是由 actual response、SQL／barrier 時序、測試 assertions 與 parent diff 交叉核對。原生記錄及交付 DLL／PDB 的對應較完整，但仍是本機證據，非簽章封存。現 API/bin 在本次 review 前已於 10/10 另建置，hash 不同；沒有把其 hash 冒充歷史 native hash。新建置測試補足目前交付 source 的獨立驗證。

## 本次實際執行與資料影響

環境：Windows、.NET SDK 10.0.400、net10.0；Docker PostgreSQL `17.11-alpine` healthy、127.0.0.1:5432。先確認現有 tests factory 使用本機開發 DB、隨機精確 emails fixture 與 cleanup，不執行 migration rebuild／volume delete。既有 native fixtures 保留。

| 實際執行 | 結果 |
| --- | --- |
| Git status／rev-parse／log／show／diff；讀取文件與來源；TRX/XML／native JSONL／Portable PDB SHA-256 核對 | 如上述基準與證據；`git diff --check` 通過。 |
| `docker compose ps`；既有 `baseline.sql` 以 stdin 執行 `docker compose exec -T postgres sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At'` | 預設 sandbox Docker pipe Access denied；改用正常本機授權後成功。測試前後只讀七表完整 ordered rows digest 及 Migration。 |
| `dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1` | 本機授權執行 exit 0，0 warnings／0 errors。未另 restore。 |
| `dotnet test CustomFormService.sln --no-build --configuration Debug --logger "trx;LogFileName=review-full.trx" --results-directory artifacts/pre-r10-repair-review-20261010/full` | 本機授權執行 exit 0，**377／377 PASS、0 skipped，29 秒**。選擇一次完整 suite 涵蓋 focused 與 no-op／rollback，不再重複跑同版 focused。三個 S-01 本次 stdout 重新核對為指定 1／2 SELECT 控制點與 RepeatableRead。 |
| 測試後 snapshot／Git scope | 七表完整列 digest、Migration 與測試前完全相同；只有本報告新增，產品／tests／規格／paired acceptance／狀態文件均無修改。 |

本次七表 rows：Users=10、Identities=0、Forms=28、Questions=53、Options=24、Responses=20、Answers=23。前後 MD5 分別為 `94607b0fb5b590305406e165f7a628d3`、`d41d8cd98f00b204e9800998ecf8427e`、`feda43fca91bc18d8a34b8434707efad`、`b61197a910d35a293df8a8f7a9caed9f`、`5bf7d7580b969694815d535fe5ca819d`、`2637149d9e0b9b6fd704b08389eaaba9`、`2738bacc148a57b31663bcec22baf71d`；Migration `20260928122537_InitialV1Schema / 10.0.11`。

本次 raw evidence：忽略的 `artifacts/pre-r10-repair-review-20261010/`，含 `delivery-pdb-audit.json`、只讀核對腳本、build／full logs、`full/review-full.trx`、db-before／after。保留舊 `artifacts/pre-r10-repair-20261007/`，不覆蓋其結果。本次沒有新增自動測試、HTTP fixtures、native process 或正式驗收結果；未重跑 native HTTP、clean-room migration、Linux、正式 HTTPS／browser CORS。沒有修改或清理既有資料。

## 交付停止點

四項可結案，**review 無阻擋問題，適合依後續 Owner 授權更新成對整合驗收文件**。更新時 candidate 應指向 e22db98，02 的 suite 前置承接本版 377 案與三個 S-01 barrier；12a～d 保留契約期待，既有結果不代填修正版。RV-I-01 可列為非阻擋證據改善，不必增加正式 HTTP 併行矩陣。

本次只新增獨立 review 報告；review 文件採單一目的提交，SHA 於交付訊息列明。**「局部修正 review 完成；可進入整合驗收文件更新，尚未更新或執行。」** R10／G1、產品／測試修正與正式驗收仍需各自明確授權。
