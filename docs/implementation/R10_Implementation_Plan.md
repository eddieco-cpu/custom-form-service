# R10 — Statistics 精簡實作計畫

2026-10-10；狀態：**R10 實作計畫待 review**；R10 **not started**。Owner 本次只授權規格核准標示同步與實作規劃；計畫確認後仍須另行授權開發。本次沒有修改產品／測試／schema／Migration，未執行 build、測試或正式驗收。

依最新 [AGENTS.md](../../AGENTS.md)、[development skill](../../.agents/skills/v1-round-development/SKILL.md)、[Backend Implementation Plan](V1_Backend_Implementation_Plan.md) R10。正式契約：00 §7；01 §3.3、FORM-07／09、SUB-06、STA-02～05；02 §5／7～11；03a §2／5～6／8～9；03b §3.5；**03c v1.1（2026-10-10，Owner 已核准）§3.4～3.5／§7～8**；04 §4.3／§5／§10；05 §5.3／§10～15／§20。四欄位修訂核准基準 `0e9c03e096f45fd21e27876edbf48299f463f05f`；未發現契約衝突。

## 1. 既有進度與前置狀態

規劃起點 `0e9c03e`、分支 master、Git 乾淨；`git diff e22db98 HEAD -- src tests` 無輸出。現有程式已含 R8 Submit、R9 Latest／Count、Detail scoped RepeatableRead；尚無 Statistics 路由／DTO／聚合器。

| 項目 | 實際狀態與本計畫處理 |
| --- | --- |
| 四欄位正式規格 | Owner 本 chat 已確認 review 通過；同步 00／01／03c／05、規格更新報告及總計畫核准標示，版本仍 v1.1 |
| R10 前 F-01～03、S-01 | [獨立 review](R0_R9_Pre_R10_Local_Repair_Review_Report.md)，提交 `7e10d71`，結論均可結案、無阻擋問題；歷史 review build／full 377 PASS 不代填 R10 結果；總計畫原「待 reviewer」已同步 |
| RV-I-01 | 尚未補強 Publish barrier fresh GET 完整頂層 DTO assertion；review 明列 P3 非阻擋，非已確認產品缺陷；不納入 R10 順手修改 |
| 正式 R0～R9 整合驗收 | 成對文件尚未同步 `e22db98` 修正版／未執行；舊結果不能證明修正版。保留待處理狀態，不在本次準備或執行 |
| R7／R9 | 總計畫仍為 acceptance results pending owner confirmation，人工未執行、未 accepted；本次規劃授權不代表確認這兩輪驗收 |
| I-01／I-02 | README 範圍過時、List Count／items 快照為整體 review 的非阻擋改善；仍未處理，不擴充 R10 |
| 開發啟動條件 | 本計畫 review 確認＋Owner 另行開發授權尚缺；正式整合驗收及歷史 Round 確認是已列出的待辦，現有 review 未將其定為 R10 技術 blocker，不自行宣稱通過或升為新 gate |

## 2. 端點與 Conditional Authentication

只新增端點 18：`GET /api/v1/forms/{formId}/statistics`。新增專用 `StatisticsController`，沿用直接 DbContext／ApiErrors 的架構，GET 不綁 Body、不要求 JSON Content-Type；不新增管理、原始答卷、Google、前端或部署功能。

採 `[AllowAnonymous]` 讓共用 middleware 不先 challenge；UUID 使用 string 路由＋明確解析，避免 `{formId:guid}` 將非法 UUID 變成 404。先解析 UUID、再查未刪除 Form，之後才按下表解析 Credential／判權限，逐階段 early return。

| 階段／情境 | 回應 |
| --- | --- |
| UUID 無效（不論 Cookie） | 400 VALIDATION_ERROR，detail path=formId |
| Form 不存在／Soft Delete（不論 Cookie） | 404 FORM_NOT_FOUND，優先於 Auth／狀態 |
| Draft | 有效 Active Owner → 409 FORM_STATISTICS_NOT_AVAILABLE；其他／缺少或無效身分 → 404 FORM_NOT_FOUND |
| Published，不論 Visibility／deadline | 任一有效 Active User → 409 FORM_STATISTICS_NOT_AVAILABLE；缺少／無效／過期／不存在 User／disabled → 404 FORM_NOT_FOUND |
| Closed public | 不要求登入，直接查詢；缺少／壞／過期／不存在 User／disabled Cookie 都不能阻擋，不依 Caller 身分決定揭露 |
| Closed owner_only | 完整 Auth 後 Owner 200，其他 Active User 403 STATISTICS_ACCESS_DENIED |
| Closed respondents | 完整 Auth 後 Owner 或曾成功提交過的 Active User 200，其他 Active User 403 STATISTICS_ACCESS_DENIED；資格用目標 Form 的成功 Response existence，不要求最新有 Answers |
| Closed private Auth 失敗 | 缺 Cookie → 401 AUTH_REQUIRED；JWT 無效／User 不存在 → 401 AUTH_TOKEN_INVALID；過期 → 401 AUTH_TOKEN_EXPIRED；disabled → 403 AUTH_USER_DISABLED |

現有 `OnMessageReceived` 對 `[AllowAnonymous]` 呼叫 `NoResult()`，故不能以已略過的 `CurrentUserContext` 或同 scheme 的 cached `AuthenticateAsync()` 當完整驗證。新增 Statistics scoped Credential resolver：讀 `access_token`，沿用已配置 JwtBearer token validator／TokenValidationParameters（signature、issuer、audience、algorithm、expiration、零 ClockSkew），取得 UUID sub 後在目前查詢交易內讀 User，返回明確的 Active／missing／invalid／expired／disabled outcome；Controller 依生命週期決定對外錯誤。不得使用 unvalidated token、JWT 內 Profile 或 Bearer header，不另定 JWT 政策。public 分支可以完全略過 Credential 驗證。

共用 R4 Auth、Logout、受保護端點的驗證順序保持不變；只在需要共用既有配置時局部整理，不移動全域 middleware。拒絕請求只回 Error Envelope，不載入／洩漏四個 Root 欄位或答案；無效持久資料／未預期例外沿用受控 500，不新增錯誤碼或假造零資料成功。

## 3. 查詢一致性與 Effective Response

採短 `RepeatableRead` transaction，第一個未刪除 Form SELECT 建立快照；Caller User（若需要）、Visibility／資格、Definition、Creator／Respondent 名稱、Effective Responses、Answers 與 DTO 建構皆在同一 DbContext／交易內完成。成功 DTO 完整物化、JSON 值 Clone 後才 commit／回 200；early return／例外透過 await using 回收。不取 FormAggregateLock／FOR UPDATE、不呼叫 SaveChanges、不寫業務列。

已刪除／政策修改在快照建立前完成時依新狀態判定；建立快照後並行 Delete／Visibility／Profile 修改，當次回快照一致結果，下次 GET 反映新狀態。這是沿用 R9／Detail 的讀取策略，不新增 GET 必須讀到回應瞬間最新值或寫入線性化契約。Published 快照即回 409，不能因後續 Close 改成當次成功。

先在 PostgreSQL 以參數化 `DISTINCT ON (user_id)`、限定 `form_id`、`ORDER BY user_id, submitted_at DESC, id DESC` 選出每人一個 Response ID／User ID（使用不加入 EF model 的內部 query row）；再只投影這些 Response 的 Answers，所有題型使用同一選定集合。保留 UUID 的 PostgreSQL tie-break 語意，不將 Max(time) 後的平手全數合併，不先 inner join Answers 再選最新，也不載入全部歷史於記憶體取最新。開發時以實際 SQL／真 DB tie-break 自測核對此選取。

Definition 用 AsNoTracking 最小欄位投影：全部 Questions（含計數為零者）、Choice 全部 Options、Rating min／max；內部 config 不輸出。Definition 的多段讀取共享快照，避免 root／child 混版。Root count 取選定的不同 User 數，包括最新零 Answer、Owner 自己提交及後來 disabled 的填答者；不以 User status 或 Answer join 篩掉有效歷史。anonymous 只保留內部必要 User ID 去重，不查 Respondent Profile；identified 才批次讀名單，禁止逐 Item／User N+1。

## 4. 聚合、精確數值與身分

先按 Effective Answer 建立每題不同 User 集合，再按 Item 建立命中 User 集合。Question count 不含最新整份缺答者；Item count 每人每值只算一次，Multiple Choice 各 Item 加總可以超過 Question count。Questions 按 sortOrder ASC，Items 一律 Array，所有集合完整回傳，不分頁或截斷。

| 題型 | 聚合與輸出 | 排序／零資料 |
| --- | --- | --- |
| text | 使用已保存／正規化的完整 String，固定 ordinal 等值，大小寫敏感；不再 trim／normalize | count DESC，再 ordinal value ASC；無實際值 → [] |
| number | 依精確數值等值分組，輸出 JSON Number | 精確 numeric value ASC；無實際值 → [] |
| single_choice | Answer Option ID 對應配置；Item 為 optionId／label／業務 value／count | 全部 Options 按 sortOrder ASC，零票仍有 Item |
| multiple_choice | 每個被選 Option 命中該 User；不將 label／業務 value 當 Answer ID | 同上，每個 Option 內去重 |
| rating | 已配置 min 到 max（含兩端）的全部整數 Item，value 為 JSON Integer | value ASC，零票仍有 Item |

Number 重用 `DefinitionNumber.TryRead` 的正規化 Sign／Digits／Exponent key 與 CompareTo，在既有 PostgreSQL JSONB 安全保存範圍內做等值／排序；`1`／`1.0`／`1e0` 同組，正負零同組。不以 raw token 文本、decimal、double 或字串排序處理。每組保留一個 DB 保存值的 JsonElement Clone 作 JSON Number 輸出，不捨入、不轉 number string；不承諾保留原始 token spelling。以極大整數、細小小數與負數驗證，避免只測小數值。Rating 以精確數值確認整數值，再映射已配置範圍。

anonymous：DTO 在每個 Item 完全省略 respondents（只對這個屬性使用 WhenWritingNull 或等效明確 Shape），不回 null／替代 ID／跨題 Mapping，Owner 同樣遵守。Root Creator 名稱照常提供，不讓內部計數 ID 序列化。

identified：每個 Item 的 respondents 只有 id＋同一快照內目前 displayName，依 ordinal displayName ASC、User ID ASC；以同一命中集合產生 count 與名單，確保 item.count=respondents.length，零票為 []。後來 disabled 的既有填答者仍計入，改名後下一查詢反映新名稱。不含 email／avatar／Provider 資料，不提供 Root 原始答卷或跨題 Mapping。

Closed 無任何 Response 仍回 200，Root／Question count=0；Choice／Rating 全部配置零票，Text／Number=[]，identified 零票 respondents=[]。最新零 Answer 的成功 Response 仍使 Root count 增加，但不增加任何 Question／Item count。

## 5. Root DTO 與四欄位投影

新增 Statistics Root／Question／Choice Item／Value Item／Respondent DTO，依 03c §3.4 九欄位與 §8 題型 Shape；可用不同 Item DTO 共用最小介面，序列化不得帶內部欄位／type discriminator。沿用 camelCase 與既有 enum-to-API mapping；不直接序列化 Entity／FormDetail。

| 新欄位 | DTO／投影 |
| --- | --- |
| creatorDisplayName | 非 null string；成功授權後由 forms.user_id 查 users.display_name，與 FormDetail 相同目前值；無名稱快照，不篩 Creator active，不輸出 Creator ID |
| responseMode | 非 null string；forms.response_mode → single／multiple |
| resultVisibility | 非 null string；與當次授權使用的同快照 forms.result_visibility 相同，owner_only／respondents／public |
| submissionDeadlineAt | DateTimeOffset?；DB 保存值，以 UTC ISO 8601 輸出並保留保存精度；null 必須出現，不套用全域 null 忽略；提前 Close／已到期／未到期均回表定值 |

四欄位在所有成功 Visibility／Identity 組合及零資料均出現。保留 formId（不改成 id）、title、nullable description、count、questions。Root 不追加 status／responseIdentity／timestamps；Question 仍包含 questionId、title、description、type、count、statistics，不輸出 config／required／sortOrder。

## 6. 預計檔案與必要開發自測

| 預計新增／修改 | 用途 |
| --- | --- |
| src/CustomFormService.Api/Controllers/StatisticsController.cs（新增） | 路由、UUID／生命週期／Visibility early return、短查詢交易、Error 與 DTO 回應 |
| src/CustomFormService.Api/Authentication/StatisticsCredentialResolver.cs（新增）、Program.cs（DI 註冊） | Conditional Credential outcome；重用已配置 JWT 驗證參數，避免 AllowAnonymous 驗證快取問題 |
| src/CustomFormService.Api/Queries/StatisticsQuery.cs（新增） | 參數化 Effective Response 選取、Definition／Answer／Profile 最小投影；接受同一 DbContext／交易，不另開連線 |
| src/CustomFormService.Api/Queries/StatisticsAggregator.cs（新增） | 五題型聚合、各層 count、精確 Number key、排序、Privacy Shape；重用 DefinitionNumber，不改其既有範圍 |
| src/CustomFormService.Api/Contracts/Statistics/StatisticsResponse.cs（新增） | Root／Question／Items／Respondents 與精確 JSON Value、nullable／省略規則 |
| tests/CustomFormService.IntegrationTests/FormEndpointsTests.Statistics.cs（新增） | 沿用 partial、MSTest／TestFactory、真 PostgreSQL、合成 fixture／Clock／interceptor |
| docs/implementation/R10_Development_Report.md（開發時新增）、總計畫與本計畫狀態 | 實際 SQL／測試／diff／提交／未驗證項與開發交付紀錄 |

原 AuthenticationServiceCollectionExtensions.cs 僅在重用配置確有需要時局部整理，必須保留既有 Protected／Public／Logout 行為；不改 Entity、EF Mapping、ApplicationDbContext model、Migration／Schema、共用 Form lock、R8 writer 或 R9／Detail 快照策略。檔案切分是內部選擇，可在同範圍局部調整並於開發報告說明。

必要開發自測（不是正式成對驗收計畫）：

- 真 PostgreSQL 覆蓋 Draft／Published／Closed × Visibility 與 Owner／Respondent／其他 Active／匿名；private 五種 Auth outcome、public 壞／過期／不存在 User／disabled Cookie，並測壞 UUID 先於 Auth、不存在／Deleted 先於 Auth、Draft／Published 存在性保護及 deadline 不提前開放。
- 同人多次只選最新整份、同時間已知 UUID tie-break、較新 optional 缺答／全空 Answers、跨 Form／User 隔離、Owner／disabled 歷史保留；核對實際選取 SQL 不包含逐題補舊答案。
- 五題型精確預期分布、三層 count、Multiple Choice 加總大於題 count、完整 Question／Option／Rating 集合、零資料、文字大小寫／Unicode／排序；Number 等值 spelling、超過 decimal／double 精度的相近不同值與排序、負值／零、raw response 中仍為 number。
- anonymous（含 Owner）所有 Items 不存在 respondents／替代 ID；identified 名單去重／排序／目前改名、零票 []、count=length；Root 僅九欄位、Question／各類 Item 的 Shape 與敏感欄位限制。
- 四新欄位在全部成功組合／zero-data 回傳，single／multiple、Creator 改名／disabled、無期限 null、有期限微秒保存精度／UTC、提前 Close／已過／未到期限；錯誤回應不攜 Root 資訊。
- 快照自測以 `ReaderExecutedAsync` 的實際首個 Form SELECT 為控制點，記錄 ContextId／isolation／SQL；暫停後讓獨立 Visibility、Delete、Creator／Respondent 改名提交，核對當次完整舊版與下一 GET 新版／拒絕，不能以 BeginTransaction 或 sleep 當快照證據。不在 Closed 假造 Submit；既有 Submit→Close 先後由 R8 同版回歸承接。finally 釋放 barrier、取消並逐一 drain 全部 workers，有界等待、非預期錯誤保留。
- 同版 R4 Auth／Logout、R8 Submit、R9 queries、局部修正／S-01 回歸與業務列／timestamps 前後不變。只清理本次自測精確 fixtures，不刪開發 volume／保留驗收資料；不執行破壞性 rebuild。開發時核對現有測試 helper／DB cleanup，既有 377 是歷史數量，以當次探索為準。

獲開發授權後實跑：`dotnet restore CustomFormService.sln`、`dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1`、focused `dotnet test CustomFormService.sln --no-build --configuration Debug --filter "FullyQualifiedName~R10_"`（新案例採 R10_ 命名）、同版全套 `dotnet test CustomFormService.sln --no-build --configuration Debug`，各自以當次 RunDir／TRX 保存實際結果。補短 Windows 原生 Kestrel 開發自測：確認本次 source／DLL／PID、health/readiness，代表性 public 壞 Cookie、private 錯誤及九欄位／精確數值輸出；不複製正式驗收矩陣。若原生環境受阻，明列未完成，不以 TestServer／Linux PASS 代稱原生成功。

## 7. 交付與停止點

本次實際只做 `Get-Content`／`rg` 文件與來源核對、`git status`／`log`／`diff`，交付前檢查文件 diff 與 `git diff --check`；沒有跑上述未來命令、HTTP、DB、build／tests，未新增測試或正式驗收文件。AGENTS.md／skills 無需修改。

規格核准標示同步與本計畫準備採不同單一目的提交，於交付訊息回報 SHA／最終 Git state。**本次停止於「R10 實作計畫待 review」**，等待 Owner 確認計畫後另行授權開發；不將規劃狀態改為 in progress／development verified／accepted。

後續開發獲授權且必要驗證成功時，交付實際變更、命令／結果、SQL／一致性證據、未驗證／前置待辦、diff／提交與 Git state，停止於「開發完成、待驗收」。獨立 code review、正式驗收準備、核准計畫執行及 Owner accepted 各保留既有授權邊界；不自動建立或執行成對驗收計畫、不開始 R11／G1。
