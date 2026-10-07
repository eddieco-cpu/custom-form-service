# R10 前局部修正計畫

日期：2026-10-07。**狀態：局部修正計畫待 review。**

基準 HEAD：`75dc3a0178d68bcefc53e745b8fa57354a75a112`；起始 `git status --short` 無輸出，分支 `master`。依據 [整體 review](R0_R9_Overall_Code_Review_20261006.md) 的 F-01～03 與 S-01，使用 repo 最新 [AGENTS.md](../../AGENTS.md) 及 [development skill](../../.agents/skills/v1-round-development/SKILL.md) 規劃既有 R4～R9 的局部 repair，不新增 Round。本次只新增本計畫；產品程式、測試、正式規格、Schema、驗收文件與進度狀態均不修改，也不執行 build、測試、HTTP 或 DB 操作。

## 1. 修改範圍與共用影響

| 項目 | 預定最小修改 | 共用路徑／保持的行為 |
| --- | --- | --- |
| F-01：已確認 | `FormsController.List` 在乘法前轉為 long，計算 `(long)(page - 1) * pageSize`；Count 後若 offset ≥ totalItems，直接回空 items；只有已證明 offset 可用 int 表示的有資料分支才轉型給 Skip。 | 只改 List 分頁運算；保留原 page、pageSize、totalItems、totalPages、Owner／deleted／status 篩選、排序、不同 User count。合法超界是 200，不新增 page 上限。List Count／items 的併行快照疑點 I-02 不納入。 |
| F-02：已確認 | 在 `RequestJson` 增加安全字串及 property-name 解碼 primitive，僅在實際 `GetString`／`JsonProperty.Name` 呼叫邊界捕捉解碼失敗。替換 `FormsController`、`FormDefinitionValidator`、`DraftDefinitionDelta` 的相關直接解碼（含文字、Enum、Datetime、Delta UUID、config value／key）。 | Auth／User 共用 RequestJson；Form Create／Patch／Visibility 共用頂層 helper；Create／Patch／Publish 共用 Definition validator。primitive 只回成功／失敗，由原呼叫層決定 code、path 與驗證階段，不提前遞迴掃描整份 Body。R8 Submission 的既有安全解碼與 object path adapter 是回歸重點，僅做必要銜接，不重構其流程。 |
| F-03：已確認 | 在 MVC input-binding 邊界辨識 unsupported Content-Type，於內建 415 short-circuit 前回 `ApiErrors.Validation`，detail 使用 `body / INVALID_VALUE`。優先採只處理 `UnsupportedContentTypeException` 標記的有序 MVC filter；實作時核對實際 filter 順序。 | MVC JsonElement body endpoints：Register、Login、PATCH me、Create、Definition Patch。位於現有 Authentication／Authorization middleware 之後；不以全域 status-code rewrite 處理。Visibility／Submit 的手動 JSON 解析保持既有 transport 規則；Logout、Publish、Close 不要求 Body，不因新 filter 強制 JSON。 |
| S-01：待驗證 | 先以 test-only EF interceptor barrier 確認 Detail split SELECT 能否合成混合版本。若成立，提出只在 `GetDetail` 包住完整讀取及 DTO 建構的短 `RepeatableRead` transaction，作為最小一致性修正。 | `LoadAvailableFormAsync` 同時被 Patch 與 lifecycle／Visibility 寫入後 read-back 使用；不把交易塞入共用 loader，也不改各 writer 的 ReadCommitted／FormAggregateLock。若未重現，記錄命中時序與結果，不把疑點升級為缺陷或預防性重構。 |

F-02 錯誤映射維持原分類：Auth／User／Form 頂層為 `400 VALIDATION_ERROR`；Question／Option／Delta／config 為 `400 FORM_DEFINITION_INVALID`；Publish Candidate 仍為 `FORM_NOT_PUBLISHABLE`。一般字串用原欄位 path 與 `INVALID_VALUE`；Question type 用 `INVALID_QUESTION_TYPE`，config 用 `INVALID_QUESTION_CONFIG`。property name 無法解碼時定位可安全命名的 enclosing object（頂層 `body`、`questions[i]`、`questions[i].options[j]`、`questions[i].config`），不回傳損壞 key 或偽造欄位名；既有合法 key 的未知／重複／required path 規則不變。不全域改寫 `InvalidOperationException`，例如 CurrentUser 或 locked Form invariant 失敗仍由既有 Server Error 處理。

依據：03a §2／4～6／9；03b §4.1～4.3、5.2、6.1～6.2、7.1～7.2、8.2～8.4；03c §4.3；04 §2／4／6；01 FORM-01／05／08、CON-01／03；02 §15／17；05 §8～9／11～13／18。此次對照未發現必須改動凍結契約的衝突。S-01 不把「GET 開始後 writer 提交」解讀成必須看到新版本；判定重點是回應是否對應完整一致版本。

## 2. 能辨識修正前後差異的必要測試

以下全為後續實作階段的預定工作，本次不新增或執行。

| 組別 | 代表案例與判定 |
| --- | --- |
| F-01 | 使用獨立 Owner 的至少 6 張未刪 Form，確保 wrap-to-4 會有資料。測 `2147483647 / 100`（舊 unchecked offset=-200）、`42949674 / 100`（offset=4）、`1073741825 / 4`（offset=0）。三者必須 200／空 items／保留輸入 page 與 pageSize／正確 totalItems、totalPages；修正前不只驗 status，須捕捉錯頁 body。承接普通分頁、空清單、小 page 超界、page=0、pageSize=101、Owner／status／soft-delete 排除與排序。 |
| F-02 值 | raw JSON 使用 lone high／low surrogate（`\uD800`／`\uDC00`），避免 serializer 預先替換。Auth Register displayName／Login password、User PATCH displayName；Form Create／Patch 的 title、nullable description、Enum／Datetime；Definition／Delta 的 Question title／type／id、Option label／value／id、config placeholder／variant 依實際改動 helper 分支做精簡 DataRows。assert HTTP、top-level code、detail code／精確 path 與零寫入，不把全部無效字串套同一 code。 |
| F-02 key | Auth／User 頂層、Form 頂層、Question／Option Object、config Object 放無效 property name；驗上述 enclosing-object path。合法 escaped key、未知欄位／重複欄位維持原判定。合法 surrogate pair、Code Point 長度、Unicode trim、Password 不 trim、nullable description／no-op 承接既有 suite；R8 lone surrogate／NUL 與 envelope／answer path 必須仍通過。 |
| F-02 優先序 | Register 未知欄位先於值解碼；Form Create 頂層錯誤先於 nested Definition。Patch 可解析 Object 內的無效字串／key，搭不存在 Form→404、非 Owner→403、Owner Published／Closed→409，均不得提前變 400；有效 Owner Draft 才做該層驗證。Visibility 同樣保留 Resource／Owner 優先於 Enum 值。另驗未預期伺服器 InvalidOperationException 仍為 500 正式 Envelope。 |
| F-03 | `text/plain`＋`{}` 覆蓋 Register、Login、PATCH me、Create、Definition Patch：Active 身分／Public 為 400 正式 Envelope、body／INVALID_VALUE，不得 415／ProblemDetails。對照 application/json 成功、malformed／missing Body，以及既有 Visibility／Submit transport tests；Logout 壞 Cookie 仍 204，Publish／Close 無 Body 仍依原契約。 |
| 認證優先序 | F-02 raw surrogate／壞 key 與 F-03 unsupported Content-Type，在 User、Form MVC 路徑及手動 Visibility／Submit 上用缺 Cookie、無效／過期 JWT、不存在 User、Disabled User 的精簡 DataRows；分別為 AUTH_REQUIRED／AUTH_TOKEN_INVALID／AUTH_TOKEN_EXPIRED／AUTH_TOKEN_INVALID／AUTH_USER_DISABLED，維持 401／403。Form 再加無效 formId＋壞 Body，確保 Auth 先返回；Active 後才允許 body transport 拒絕。 |

預定測試位置：沿用 `AuthUserEndpointsTests.cs`；Form 局部案例放 `FormEndpointsTests` 的 repair partial file，S-01 barrier 也在 tests 專案。沿用现有 fixture／factory／interceptor 注入，不加產品 probe endpoint。

## 3. S-01 的受控重現與判定

1. 使用真 PostgreSQL、兩個獨立 Request／DbContext。建立 Draft 完整 V0，含可辨識的 Form title／description、Question title／config、至少兩個 Options。單次合法 Patch 形成 V1，同時改 Form、Question 與 Options，含可辨識值與排序／集合變動。保存 V0、Patch committed response V1 與 DB Definition 快照；此處版本是測試標記，不新增 DB version 欄位。
2. gate 只匹配本次 GET 的 Form ID、實際 SQL 類型與 DbContext／connection；writer 的 lock、load、read-back 不得命中。先以 `ReaderExecutedAsync` 在 **Form root SELECT 已執行**後暫停，確認 Questions／Options SELECT 尚未執行。gate 不要求已有 RepeatableRead，避免修正前測試跳過 barrier。
3. 等 Reached 再發 Patch；收到成功回應並由獨立查詢確認 V1 已 commit，且 GET 仍停住，才 Release。保存 SQL／barrier／commit 事件順序與實際 GET body。再用獨立 fixture 在 **Questions SELECT 已執行、Options SELECT 尚未執行**處暫停，重做上述流程，排除只測到 root／child 而漏掉 Question／Option 混合。
4. GET 必須能與已記錄的完整 V0 或 V1 對應；旧 root＋新 Questions，或旧 Questions＋新 Options 都算不一致。若採 scoped RepeatableRead，snapshot 自第一個 Form SELECT 建立，這兩個受控時序預期完整 V0；釋放後下一次 GET 必須完整 V1。比對所有 Definition 欄位、IDs、config、完整集合及排序，連同 status／updatedAt，不只驗單一 title 或長度；Creator profile fixture 保持固定。
5. 若 Patch probe 證實混合，補最小 Publish 交互：GET 暫停→Patch V1 commit→Publish commit→釋放；GET 是一致的舊 Draft，下一 GET 是完整 Published V1。Publish 不改 Definition，單純 Detail×Publish 不作 Definition 混合證據。承接既有 Patch×Publish 兩種先後／Freeze、Publish Definition 不回寫測試；不新增 Delete／Close 大矩陣或越權結論。
6. 每次 gate 有有界等待與 cancellation；finally 先釋放 barrier、取消未完請求、逐一 drain 全部 workers，再清理 exact test fixtures，非預期 worker exception 不吞掉。不靠 `Task.WhenAll`、sleep 或 transaction begin 推測命中。barrier 未命中、SQL 辨識錯誤、Patch 未 commit 等記為 harness／未完成，不能報「未重現因此沒有缺陷」。

S-01 若成立，先保存反例再確認 scoped RepeatableRead 的完整版本保證；實作授權需包含這項條件式修正，否則只交付反例與最小修正提案。此選擇沿用 R9 已有讀取策略，不加 Form writer lock，不將 GET 變成業務寫入。

## 4. 後續執行順序、證據與交付 gate

1. Owner review／明確授權實作後，先新增反例測試並在未修產品上執行，保留首次 TRX／log／raw response 與 S-01 SQL 時序；F-01～03 的預期固定為正式契約。失敗與環境／harness 問題分開紀錄；若某反例修正前已通過，補查 fixture／控制點，不宣稱它證明修正。
2. 局部修正→本次 build→focused tests→完整既有 suite（原 306 案＋本次新增，以實際探索為準）。預定命令：`dotnet restore CustomFormService.sln`；`dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1`；`dotnet test CustomFormService.sln --no-build --configuration Debug --filter <本次實際新增及受影響名稱> --logger "trx;LogFileName=focused.trx" --results-directory <本次RunDir>`；full 移除 filter、使用獨立 `full.trx`。各次嘗試用獨立目錄，不覆蓋首次 FAIL。記實際命令、計數、skipped 與平台，不能沿用 review 的 306 PASS。
3. F-01～03 補 **Windows 原生 Kestrel HTTP 開發自測**：核對 source／本次 DLL／PID、health／readiness，使用 raw UTF-8 request 與明確 cookie。F-01 三個 offset 變體；F-02 Register／Login、User PATCH、Form 頂層／nested／property-name、代表 Resource／Owner／Lifecycle 優先序；F-03 MVC 各 Body 路徑、手動路徑對照及受保護端點缺／壞 Cookie 優先序。不得用 TestServer 或 Linux suite PASS 代替原生 HTTP；若平台阻擋，記未完成／blocked。這是 development verification，不啟動正式整合驗收。
4. 測試前後核對既有七表完整列快照；保留開發 DB volume 與既有 synthetic fixtures。自動測試僅清理自己的精確暫存資料；原生 HTTP 自測的合法合成 fixtures 記明並保留，拒絕請求做前後零寫入檢查。只停止本次啟動 process，復原本次暫時設定，不提交密碼／Cookie／JWT。後續 report 保存不含秘密的失敗摘要及證據索引，原始資料放忽略的 RunDir。
5. 檢查 diff，更新局部 development report／Implementation Plan 的 repair 註記，不改 R7／R9 Owner pending 或歷史 accepted。分單一目的提交已驗證修正，回報 SHA、剩餘 Git 狀態、未驗證項與舊證據適用性；開發交付停止，不自動進入 acceptance、R10 或 G1。

## 5. 既有整合驗收文件影響

現有 paired plan：[Markdown](../test-plan/R0_R9_Integration_Acceptance_Test.md)／[.http](../test-plan/R0_R9_Integration_Manual_Tests.http)，仍是待 review／未執行，本次兩檔不改。12a～d 預期已符合修正目標，不能改成 500／415 求 PASS；12b 的 offset=0 fixture 設計保留。

後續若獲授權修訂驗收文件，使用 acceptance skill 成對同步並列差異：修正 candidate commit、02 承接的新測試／實際 suite 計數及證據前置、F-02／03 新增代表 cases 的編號／順序／資料／預期，以及 S-01 確認後是否納入非 HTTP barrier 步驟與 companion 指向。72 HTTP／5 非 HTTP 是目前版本的數量，不預先宣稱修訂後相同；所有新增／受影響結果仍待執行。既有歷史結果保留，但不得當作已驗證修正版共用路徑的證據。實質計畫變更重新交 Owner review，正式 Execute 仍須本版 paired plan 核准及明確授權。

**本次交付只到「局部修正計畫待 review」；沒有修正或新測試結果，沒有開始正式整合驗收。**
