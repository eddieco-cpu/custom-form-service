# R10 — Independent Code Review

2026-10-10（Asia/Taipei）。本次只做獨立 code review、必要 build 與既有自動化測試；不修改產品／測試、不新增測試、不準備或執行正式驗收、不標記 accepted。

## 1. 結論與 findings

**本次審查未發現可確認的 R10 阻擋缺陷，亦無新增非阻擋 finding。** Conditional Auth、同一快照、Effective Response、五題型聚合、數值精確性、JSON Shape 與資料揭露在以下審查範圍內符合已核准契約。可交 Owner 確認本次 review 結論；這不是正式／人工驗收通過或 R10 accepted。

| 嚴重度／類別 | 結果 |
| --- | --- |
| P0／P1／P2 阻擋缺陷 | 0 |
| P3 非阻擋改善 finding | 0；沒有將未證實的疑慮列為產品缺陷 |

本次獨立 build 成功（0 warnings／0 errors），但其新輸出 DLL 被 Windows 應用程式控制政策阻擋載入。測試改以**原位置既有、已重新核對 source／PDB 的產物**正常執行 `--no-build`，本次 R10 **43/43**、完整 suite **420/420** PASS，均 0 failed／skipped。不得將這兩次 PASS 寫成「本次新編譯產物已通過 runtime 驗證」；完整限制見 §5～6。

## 2. 審查基準與方法

- 產品／測試：`a4f9402336a4eecdc07c72e92ef09234a4d5e976`。
- 文件：`838a7a705c9cf6379930bcef31f19b290941ed44`，亦為審查開始的 HEAD；master、`git status --short` 無輸出。
- 開發起點及核准 R10 計畫：`06853f32201a168989c25b5dbb03f8f4ec1c1e77`。核對產品提交新增的六個檔案與 Program.cs DI 修改、全部新增測試，以及文件提交的三個檔案。
- 已讀最新 [AGENTS.md](../../AGENTS.md)、[development skill](../../.agents/skills/v1-round-development/SKILL.md)、[acceptance skill](../../.agents/skills/v1-acceptance-testing/SKILL.md)。兩者沒有獨立 review 執行模式；本次依 Owner 明確指定的 read-only 產品範圍審查，沒有轉入開發修補或驗收。
- 已讀正式 00／01／02／03a／03b／03c／04／05、[R10 計畫](R10_Implementation_Plan.md)、總計畫 R10 與階段邊界、[開發報告](R10_Development_Report.md)。主要依據為 01 §3.3／SUB-06／STA-02～05、03a §5～6、03c v1.1 §3.4～3.5／7～8、04 §4.3／5／10、05 §5.3／11～15。未發現需要 Owner 解決的契約衝突。
- 直接讀程式與實際 Git diff，核對 R4 JWT／Logout、DefinitionNumber、R8 writer／fixture helper、R9 Latest／Count、共用錯誤與 JSON 設定；解析 TRX Counters／案例 stdout，檢視保存的 SQL 與原生 HTTP 原始 body。沒有只採開發報告的 PASS 標記。

## 3. 五個重點的具體證據

### 3.1 Conditional Auth、Lifecycle／Visibility 與優先序

[StatisticsController.cs](../../src/CustomFormService.Api/Controllers/StatisticsController.cs) 第 25～57 行先驗 UUID、查未刪除 Form，再處理 Lifecycle／Credential。Draft 只有 Active Owner 得到 409；Published 任一 Active User 得到 409，均不因 Visibility／deadline 提前開放。Closed public 略過 Credential；private 先區分 missing／invalid／expired／disabled，再判 Owner 或目標 Form 的成功 Response existence。錯誤不攜 Statistics Root。

[StatisticsCredentialResolver.cs](../../src/CustomFormService.Api/Authentication/StatisticsCredentialResolver.cs) 第 19～43 行使用現有 scheme 的 TokenHandlers 與 TokenValidationParameters.Clone，沒有另建簽章／issuer／audience／algorithm／ClockSkew 政策。比對 AuthenticationServiceCollectionExtensions 的配置與 OnTokenValidated：使用經驗證的 UUID sub、從 DB 讀 User／Active 狀態；不相信 token Profile、不接受 Bearer transport。現行配置只有既定 TokenHandlers；本次沒有修改共用 JWT events／Auth／Logout。

[FormEndpointsTests.Statistics.cs](../../tests/CustomFormService.IntegrationTests/FormEndpointsTests.Statistics.cs) 第 29、83、114 行起的案例實際送 HTTP，涵蓋九組 Lifecycle × Visibility、角色／匿名／壞 Cookie、UUID／missing／deleted 優先序，以及 13 種 Credential failure。JWT outcome 同時與 `/users/me` 比較；HS512 案使用合法但足夠長的相同 signing key，確實驗證算法限制。literal empty Cookie 依既有 ASP.NET Cookies 解析結果視為缺少，沒有為 Statistics 改政策。這些案例本次重新 PASS。

### 3.2 同一快照與最新整份 Response

Controller 第 27 行起建立 RepeatableRead；第一個 Form SELECT 建立 PostgreSQL 快照。Credential Resolver 接收同一 ApplicationDbContext，授權 existence query、Definition、Creator／Respondent 名稱、Effective Responses／Answers 及 DTO 建構皆在交易內完成；完整物化／Clone 後才 commit。early return 由 await using 回收交易，GET 無 SaveChanges／Form lock。

[StatisticsQuery.cs](../../src/CustomFormService.Api/Queries/StatisticsQuery.cs) 第 21～28 行先以參數化 SQL 選整份 Response，之後第 37～38 行只查選定 ID 的 Answers：

```sql
SELECT DISTINCT ON (user_id) id AS "Id", user_id AS "UserId"
FROM responses
WHERE form_id = @p0
ORDER BY user_id, submitted_at DESC, id DESC;

-- 後續 EF 查詢：
SELECT a.response_id AS "ResponseId", a.question_id AS "QuestionId", a.value AS "Value"
FROM answers AS a
WHERE a.response_id = ANY (@responseIds);
```

因此最新零 Answer 仍增加 Root count，最新缺答不補舊題；不先 inner join Answers、不把 timestamp 平手的 Response 全部合併、不在記憶體載入全部歷史取最新。SQL 限定目標 Form，另有 Answer Question scope 檢查。R9 Latest 使用 DB 的 submitted_at／UUID DESC，與本輪一致。

測試第 287 行起使用同 timestamp 的已知 `10000000-...0001`／`f0000000-...0001`，DB 與 Statistics 都選後者，只留下 number=7、各題 count 為 0／1／0／0／0；另 Form 的答卷不混入。第 196～198 行的 Owner 最新全空答卷則確認 Root=6、各 Question=5。

測試第 374～432 行在 **ReaderExecutedAsync 的真實 Form SELECT** 暫停，用 helper 的獨立 Npgsql connection 完成 mutation 後才 Release。比較當次完整 JSON 與 V0，下一次 GET 必須反映 V1／拒絕；finally 釋放、取消與有界 drain workers。沒有用 sleep 或 BeginTransaction 當快照成立證據。本次 focused TRX 的四組實際記錄如下，每組九個 SELECT 共用一個 ContextId／RepeatableRead；完整 suite 也重新執行四案：

| mutation | 本次 focused ContextId | fresh GET |
| --- | --- | --- |
| visibility | `28a47f1f-d035-4d9f-add8-4e351dd85f50` | 403 STATISTICS_ACCESS_DENIED |
| delete | `95913e9c-246c-44d9-8458-e563fe119525` | 404 FORM_NOT_FOUND |
| profiles | `3c56b6e4-44c3-471c-bd92-5b50d592a4b8` | Creator／Respondent 目前名稱均 V1 |
| caller-disabled | `88a2d48d-21ed-4b88-9c7b-a78700f236ce` | 403 AUTH_USER_DISABLED |

### 3.3 五題型、精確數值、排序、count／缺答／零資料

[StatisticsAggregator.cs](../../src/CustomFormService.Api/Queries/StatisticsAggregator.cs) 第 15～92 行依同一 Effective Answer 集合聚合：text 採 ordinal 等值、count DESC／value ASC；number 採 DefinitionNumber 精確 key／CompareTo；choice 回完整配置與 Option 業務 value，Item User 去重；rating 回 min～max 全整數與零票。Question count 是有 Answer 的不同 User，Item count 是命中不同 User；multiple choice 每人可命中多 Item。

Query 的 Question／Option 投影明確 OrderBy(sortOrder)。Names 採 ordinal displayName／Guid ASC；不使用 DB 自然順序或文化語系排序。identified count 與名單由同一去重 User 集合產生，不篩掉後來 disabled 的歷史 Respondent；沒有分頁／截斷。

第 131、174、255、287 行起的測試核對零資料完整題目／Options／Rating 集合、text 大小寫／Unicode、三層 count、multiple Item 加總 10 > Question 5、Owner 最新零 Answer、目前改名／disabled 歷史與同名 ID 排序。精確數值案例不是以 double／decimal 當 oracle：經 R8 真實 submission／JSONB 保存 81 位相近整數、101 位小數及負值，預期七組／各組 count 與排序；用 PostgreSQL JSONB text 對照實際 JSON Number raw token。1／1.0／1e0 與正負零等值合組。Number output 直接保留 JsonElement，沒有有限精度轉型。上述案例本次重新 PASS。

### 3.4 實際 JSON、四個欄位、null 與敏感資料

[StatisticsResponse.cs](../../src/CustomFormService.Api/Contracts/Statistics/StatisticsResponse.cs) 第 6～26 行 Root 恰九個欄位；只有 Item respondents 使用 WhenWritingNull。Root／Question description、Root deadline 的 null 不受該設定影響。Program 沒有新增全域忽略 null。Items 為具體 DTO，輸出不帶內部 query row／Entity／type discriminator。

Query 第 39～57 行由同快照讀 Creator 目前名稱、responseMode／resultVisibility／保存期限；不以 Creator status 排除 Form。anonymous 不讀 Respondent Profile；identified 使用一次批次投影，僅 id／displayName。期限轉 UTC 且保留 DB 微秒精度；沒有把它替換成 Close 時間。

測試第 131、174、323、456～467 行核對**序列化後 JSON**的完整 Property 集合，涵蓋六種 Visibility × Identity 的零資料、required null、anonymous respondents 完全省略、identified 零票 []、每個 Respondent 僅兩欄、各題／Item Shape、目前 Creator 名稱與保存期限。Owner 與帶壞 Cookie guest 在 public Form 的完整 JSON 相等。

另直接檢視開發證據 native-http.jsonl：anonymous public／private Owner body 都將 `100000000000000000000000000000000000001` 回為未捨入 JSON number，沒有 respondents；identified zero body 的 Rating Items 皆 `respondents: []`。九欄位、description／deadline null 與不可揭露欄位的實際 body 與程式／測試相符。這些原生 HTTP 是**歷史證據核對**，本次沒有重跑該會新增 fixtures 的 native probe。

### 3.5 測試證明力與既有功能回歸

不是只測成功狀態碼：有 actual JSON Property 集合／分布／raw numeric、真 DB UUID winner／SQL 形狀、同快照完整 JSON 與 fresh GET、讀前後完整列及受控 500 的檢查。四個無效持久 Answer 案沒有假造零資料成功，亦沒有公開內部 SQL／exception。

產品 diff 除 Program 三個新增行外均為新 Statistics 檔案；未改 AuthenticationServiceCollectionExtensions、AuthController、Logout、R8／R9 controller、DefinitionNumber、Entity／Mapping／Migration。完整 suite 本次 420/420 PASS，包含既有 Auth／Logout、R8 validation／atomicity／concurrency、R9 queries、F-01～03／S-01。測試使用隨機精確 email 與既有 cleanup；沒有刪 regular volume／重建庫。這提供實際回歸證據，但不是所有未測情境的保證。

## 4. 開發報告／SQL／資料證據核對

開發 focused-final TRX 的 Counters 為 43 executed／43 passed；full 為 420／420。SQL 檔與各 snapshot case stdout 一致：Form／政策、Caller 狀態、Response existence、DISTINCT ON、Definition、Answers、Creator 與 batch Respondents 共用同一 RepeatableRead。anonymous SQL 只有 Creator profile projection，identified 額外一個 batch，沒有 N+1／FOR UPDATE／業務寫入。

開發 db-before／db-after-suite／native-db-after-old 的七表列 digest 與 Migration 紀錄相符。開發 native process evidence 的 HEAD 仍是候選版基準 06853f3，報告已明示 candidate source；本次重新核對原位置 API／Tests PDB 的 **78 個非 obj source checksum 全部一致**，並確認 API build／tests-bin API DLL SHA-256 相同，與 native evidence 的 `12AB1A7D125938F9A8F790DDDA41FBF15CF37C8B477D0E0878E43FEF07E12FAB` 一致。不是只因 report 宣稱 source 一致就接受。

本次測試前後另外以 RepeatableRead READ ONLY、按 id 的 row_to_json 完整列 digest（包含 timestamps／JSONB／password_hash，但只輸出 hash）核對，七表與 Migration 全部相同。起迄列數：users 11、user_identities 0、forms 30、questions 56、options 24、responses 21、answers 24。將 digest delimiter 改成開發證據使用的 `|` 後，逐行與 db-final.txt 完全相同；原始檔換行格式不同不視為資料變動。

## 5. 本次實際命令、結果與可重讀證據

本機 Windows／.NET SDK 10.0.400／PostgreSQL 17.11；review 證據目錄為 [artifacts/r10-review-20261010](../../artifacts/r10-review-20261010/)（ignored，只保留本機，不提交 build／TRX／秘密）。

| 實際命令／操作 | 實際結果 |
| --- | --- |
| git status／log／show、產品與文件 diff；Get-Content／rg 讀規則、規格、程式、測試、報告及證據 | 審查開始乾淨、HEAD 是指定文件提交；7 個產品／測試檔＋3 個文件，沒有 schema 或其他 Round 變更 |
| `git diff --check a4f9402^ 838a7a7` | exit 0，無 whitespace 錯誤 |
| `dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1` | 一般 sandbox 首次延遲輸出後取消（exit 1）；正常權限重跑仍 exit 1，10 warnings／2 errors：MSB3026／3027／3021，PID 36804 鎖住原 API DLL，非編譯錯誤；保留 API，未終止程序 |
| 同 build 命令追加 `-p:BaseOutputPath=C:/Users/user/Desktop/custom-form-service/artifacts/r10-review-20261010/bin/` | exit 0，0 warnings／0 errors；[build.log](../../artifacts/r10-review-20261010/build.log)；新 API／Tests PDB 亦為 78 source checksums 全部一致 |
| `dotnet test CustomFormService.sln --no-build --configuration Debug -p:BaseOutputPath=C:/Users/user/Desktop/custom-form-service/artifacts/r10-review-20261010/bin/ --filter 'FullyQualifiedName~R10_' --logger 'trx;LogFileName=r10-review-focused.trx' --results-directory artifacts/r10-review-20261010/focused` | exit 1，探索時 DLL 載入被應用程式控制政策阻擋 0x800711C7，未執行案例；focused.log／focused TRX 保存 |
| 同新輸出 test，移除 filter、logger `r10-review-full.trx`、results `artifacts/r10-review-20261010/full` | exit 1，同一政策阻擋，未執行案例；full.log／full TRX 保存 |
| 重新讀取原位置 PDB 並對每個非 obj source 算 SHA-256；API DLL hash 比對 | 78/78 source 一致；src-bin／tests-bin API hash 相同；[existing-pdb-audit.json](../../artifacts/r10-review-20261010/existing-pdb-audit.json) |
| `dotnet test CustomFormService.sln --no-build --configuration Debug --filter 'FullyQualifiedName~R10_' --logger 'trx;LogFileName=r10-review-existing-focused.trx' --results-directory artifacts/r10-review-20261010/existing-focused` | exit 0，43/43 PASS，0 failed／skipped，22 秒；[focused TRX](../../artifacts/r10-review-20261010/existing-focused/r10-review-existing-focused.trx) |
| `dotnet test CustomFormService.sln --no-build --configuration Debug --logger 'trx;LogFileName=r10-review-existing-full.trx' --results-directory artifacts/r10-review-20261010/existing-full` | exit 0，420/420 PASS，0 failed／skipped，54 秒；[full TRX](../../artifacts/r10-review-20261010/existing-full/r10-review-existing-full.trx) |
| `Get-Content artifacts/r10-review-20261010/digest.sql \| docker exec -i custom-form-service-postgres-1 sh -c 'exec psql -X -A -t -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"'`（前後各一次） | exit 0；[db-before.txt](../../artifacts/r10-review-20261010/db-before.txt)／[db-after.txt](../../artifacts/r10-review-20261010/db-after.txt) 逐字相同。既有 fixtures、timestamps、Migration 均保留 |

Docker 一般 sandbox inventory 曾受 named-pipe access denied；使用正常核准權限做容器只讀檢查／digest 後成功。以上沒有修改 Windows 政策、解除 DLL 阻擋、變更持久設定或執行 native probe。沒有新 restore／migration／rebuild；build 使用現有 restore 資產。

## 6. 範圍限制、未驗證事項與交付

- **新編譯 DLL 的 runtime 尚未驗證**，受 Windows 政策阻擋；本次 PASS 是重新核對過的既有 build，不是新 BaseOutputPath 產物。兩套 PDB 都與目前 source 一致；新 build 編譯成功與既有產物重跑結果分開記錄。
- 原生 Kestrel 21 HTTP／raw body 僅核對歷史保存證據，未在本次重送；沒有另啟動／停止原 API。Linux、Google、前端、部署／production HTTPS、負載與所有 JSONB numeric 極限值未執行。現有精確數值案例足以排除小值測試掩蓋的 decimal／double 合組與排序問題，不等於窮舉 PostgreSQL 全值域。
- 本次沒有新增 shuffled Definition／大型資料／額外故障注入案例。排序的明確 OrderBy／ordinal comparer 已審查，現有案例及輸出已核對；未將這些可選覆蓋補強描述為已發現的阻擋缺陷。
- 歷史 R7／R9 Owner confirmation、R0～R9 正式整合驗收文件／執行、RV-I-01／I-01／I-02 既有非阻擋待辦保持原狀；本次不替它們確認或結案。
- 僅新增本 review 報告；產品、測試、規格、計畫狀態與 acceptance 檔案均未修改。報告採單一目的文件提交，SHA 與最終 Git state 在交付訊息回報；ignored evidence 保留供本機重讀。

**停止於「R10 獨立 code review 已完成，待 Owner 確認」**。正式驗收準備／執行與 accepted 維持既有階段及授權邊界。
