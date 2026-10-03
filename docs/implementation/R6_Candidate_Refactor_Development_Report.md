# R6 有限 Candidate 重構 — 開發交付報告

日期：2026-10-03。狀態：development verified，**開發完成、待驗收**。有限重構及必要修正已通過開發驗證；未準備／執行正式驗收，未 accepted。

## 範圍與本次修改

依 AGENTS.md、v1-round-development skill、最新正式規格與 Owner 授權，先建立測試基準，再做有限 Candidate／ChangeSet 重構。沒有修改正式規格、DB schema 或 Migration；R7、G1、驗收準備與驗收執行均未開始。

新增 `tests/CustomFormService.IntegrationTests/FormEndpointsTests.CandidateContract.cs`，共 39 個資料驅動／一般測試案例：空 Question／Option 陣列 no-op（1）、Type 切換與 Choice 互轉（6）、Config 完整取代與語意 no-op（1）、共享 Create／PATCH Config 邊界（12）、Number 精確數值（12）、JSONB 實際儲存邊界（4）、Question／Option 排序交換（1）、欄位來源 path（2）。沿用既有 fixture、查詢與清理流程；不重複新增已有的 value 交換／重用、三組混合操作或第二次 SaveChanges 故障回滾測試。

已重現並修正：Number Config 使用 decimal 拒絕 JSONB 可精確保存的 1e30／1e400，且將高精度相鄰數值或 1e-40 與 0 在比較時捨入為相同，錯誤接受 min > max。新增 DefinitionNumber 直接比較十進位有效數字與指數，保留 JSON 原數值，不經 decimal／double；超出實際 JSONB numeric 儲存能力時受控拒絕。案例先以實際 JSONB cast 證明安全／不安全，再檢查 Create／PATCH 與 DB。儲存邊界依 [PostgreSQL JSONB](https://www.postgresql.org/docs/17/datatype-json.html) 與 [numeric](https://www.postgresql.org/docs/17/datatype-numeric.html)，不是新增產品精度或業務範圍。

另修正 Config 正規化後的等值 no-op（屬性順序、1 與 1.0 不算變更），以及 sortOrder 衝突不能映射到 Request 僅改 title／label 的位置。這些是規格修正，不將舊錯誤行為固定成基準。規格對照：01 §3.3／FORM-05～06／QST、03b §6.1／7.2、04 §2.4／6、05 §10～12。

## 有限重構結果

PATCH 現在依序建立獨立 `FormDefinitionCandidate`，在記憶體套用 Delta、正規化與完整驗證，再以 `DraftDefinitionChangeSet` 比對實際差異，最後由 `DraftDefinitionWriter` 保存。Candidate 沒有 Entity、Navigation、Timestamp 或 ChangeTracker 狀態；驗證失敗不修改追蹤的 Form graph，也不新增 EF Entity。

移除 Aggregate 重新序列化成 Create JSON 的往返、Candidate 集合索引反查／字串 path 解析、DefinitionSnapshot、全量正規化回寫、手動 Entry.State 與暫停 AutoDetectChanges。Config 自身仍為正式 JSONB 欄位，保留必要的 JSON 型別驗證／正規化；沒有建立通用框架。

保留權限、Draft 檢查、原有 Form `SELECT ... FOR UPDATE` lock 與外層交易；Delta Shape → ID 歸屬 → 重複／矛盾 → 完整 Candidate 的優先序不變。錯誤來源記錄於相關 Request 欄位，sortOrder／value 衝突優先指向真正改變值的欄位，不會被僅改 title／label 或明確提供不變值的項目攔走。重複 ID 仍由 preflight 在第二次出現的 Request 索引報錯；沒有新增 Domain-ID path 格式或解析錯誤字串。

Alternate Key `(question_id, value)` 及 DB Constraint／Migration 完全未變。一般 value 修改仍不能直接改已追蹤的 key，保留「舊列刪除 → Save #1 → 同 ID、原 createdAt 的新列插入 → Save #2」；value 交換與刪除後重用同 value 也沿用此原子交易。Save #1 之前只保存 pending Candidate 資料，沒有新增／replacement Option Entity 能經由 Navigation 被 DetectChanges 提早追蹤；不是僅設定 FK 再 Add。未改 value 的 Option 僅保存實際 label／sortOrder 差異。

有 Definition 變更才更新 Form、受影響 Question／Option 的 updatedAt；新增列 createdAt = updatedAt，replacement 保留 ID／createdAt，未變更列與 no-op 不寫入。PATCH 在同一交易內 `AsNoTracking` 重新取得實際 DB 值，commit 成功後才回傳；Save #2 或 commit 失敗均回既定受控 500，交易完整回滾。Create 僅改共用 Question／Option／Config 驗證核心，沒有修改其 Controller／保存流程或擴大到其他端點。

### 明列的可觀察修正

- Number 不再因 decimal 範圍／捨入拒絕安全值或接受 min > max；不安全的原始零值指數在寫入前回 400，不再等 DB 失敗回 500。
- Config 經正規化後語意等值（含屬性順序、1 與 1.0）為 no-op，不更新時間。
- Candidate 衝突 path 指向相關 Request 欄位，不以「項目被碰到」推測來源。
- PATCH 回應使用已保存的 JSONB 表示與 PostgreSQL 微秒時間精度；與後續 GET／DB 一致，不再包含尚未保存的次微秒時間。三組既有混合操作的回應比較由允許微小時間差加強為完整一致。
- 實際 SQL 證明：僅改 Question title 時只 UPDATE title／updated_at，不 UPDATE config 或其他 Question 欄位，也不 UPDATE Options；只改 Form 不 UPDATE Question／Option；等值 Config 與空 Option delta 不產生任何 UPDATE。

不是籠統承諾所有錯誤 details 的數量／排列完全不變；契約允許不強制多筆 details。已保留規定的 top-level code、驗證階段順序、Request 定位及重複 ID 第二次出現規則。

### 受影響檔案

- `src/CustomFormService.Api/Controllers/FormsController.cs`：PATCH Candidate／ChangeSet／保存與 DB 回應流程。
- `src/CustomFormService.Api/Validation/FormDefinitionCandidate.cs`、`DraftDefinitionChangeSet.cs`：獨立型別化定義、欄位來源、差異資料。
- `src/CustomFormService.Api/Validation/DraftDefinitionDelta.cs`、`FormDefinitionValidator.cs`、`DefinitionNumber.cs`：Delta preflight、共用正規化／驗證與精確 Number。
- `src/CustomFormService.Api/Infrastructure/Persistence/DraftDefinitionWriter.cs`：取代 `OptionValueTransitionWriter.cs`，只套用差異與延後 Option 追蹤。
- `tests/CustomFormService.IntegrationTests/FormEndpointsTests.CandidateContract.cs`、`FormEndpointsTests.CandidateSafety.cs`、`FormEndpointsTests.cs`、`FormEndpointsTests.OptionPersistence.cs`：契約案例、安全探針、SQL 欄位與精確回應斷言。
- 本報告及 `V1_Backend_Implementation_Plan.md`：開發結果、證據與階段狀態。正式規格、Mapping、Migration、驗收文件均未修改。

## 實際驗證

以下結果均為開發驗證，不是正式驗收。Windows 基準與其後的 Linux 執行分開記錄。

| 命令 | 實際結果 |
| --- | --- |
| `dotnet test tests/CustomFormService.IntegrationTests/CustomFormService.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~FormEndpointsTests --logger "console;verbosity=normal"` | 修改前 16/16 PASS，13.3886 秒 |
| 同一 Form 命令增加 `-m:1 -p:UseSharedCompilation=false` | 修改前再次 16/16 PASS，11.4229 秒 |
| `dotnet test CustomFormService.sln --no-build --configuration Debug -m:1 --logger "console;verbosity=normal"` | 修改前全套 61/61 PASS，11.1368 秒 |
| 新增案例後執行 Form 命令（含 `--no-restore -m:1 -p:UseSharedCompilation=false`） | 編譯成功；探索階段載入測試 DLL 被封鎖，0x800711C7，沒有案例執行結果 |
| 一般主機環境重跑相同 Form 命令 | 同一封鎖，並非 sandbox 權限升級即可解決 |
| `dotnet build CustomFormService.sln --no-restore -t:Rebuild -m:1 -p:UseSharedCompilation=false`（一般主機工具鏈） | 成功，0 警告／0 錯誤，6.65 秒 |
| `dotnet test tests/CustomFormService.IntegrationTests/CustomFormService.IntegrationTests.csproj --no-build --no-restore -m:1 --filter FullyQualifiedName~FormEndpointsTests --logger "console;verbosity=normal"`（一般主機） | 完整 rebuild 後仍被封鎖；新增 29 案例未執行 |

唯讀診斷：`Microsoft-Windows-CodeIntegrity/Operational` 事件 3077／3033 明確指出 `testhost.exe` 載入新 `CustomFormService.IntegrationTests.dll` 不符 Code Integrity 原則；同時有 Smart App Control 3118 事件。DLL 沒有 Zone.Identifier stream。沒有變更／停用安全設定，也沒有變更測試要求或框架以取得 PASS。

Owner 已另行授權 Docker Linux 測試。官方 SDK 10.0.400 的 Docker pull 發生 EOF；主機 IPv4 可連 MCR，因此下載固定官方 amd64 manifest `sha256:1aabdb4843de1c426d3676bf1220bc040e540f82a765320b3eb2c693e8d0a7dd`，驗證 config、全部壓縮／解壓 layer SHA256 後以 docker load 匯入原映像。容器名稱 `r6-candidate-sdk`，SDK 實測 10.0.400；原始碼、NuGet 快取與本專案專用 User Secrets 唯讀掛載，編譯發生於容器 /work，連接既有 PostgreSQL 網路，不重建資料庫或 volume，不輸出 Credential。

容器中 `dotnet restore CustomFormService.sln` 成功。後續命令由 `docker exec r6-candidate-sdk bash -lc ...` 執行：

| 容器內命令／階段 | 實際結果 |
| --- | --- |
| 首次 Form focused test，含最初 29 新案例 | 45 案，32 PASS／13 FAIL；其中 5 個為新增測試錯將 Npgsql DateTime cast 為 DateTimeOffset，已修正測試的讀取型別，保留 createdAt 比較要求 |
| 修正測試讀取後 `dotnet test CustomFormService.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger "console;verbosity=minimal" --logger "trx;LogFileName=baseline.trx"` | 90 案，82 PASS／8 FAIL；Number 5、Config no-op 1、欄位來源 path 2，舊 61 案全部 PASS |
| 必要修正與 JSONB 邊界補測後 Form filter 命令 | 47/47 PASS，contracts-fixed.trx |
| 精確數值補強後 `dotnet build CustomFormService.sln --no-restore -m:1 -p:UseSharedCompilation=false` | 成功，0 警告／0 錯誤，4.90 秒 |
| `dotnet test CustomFormService.sln --no-build --no-restore -m:1 --logger "console;verbosity=minimal" --logger "trx;LogFileName=contracts-regression.trx"` | 97/97 PASS，0 skipped |
| 零值指數補測，先以必要修正階段程式執行 Number filter | 16 案，14 PASS／2 FAIL；`0e1073741824` 與 `-0e2147483647` 回 500，而不是受控 400；numeric-zero-baseline.trx |
| 零值指數修正後相同 build 與 full test 命令（numeric-zero-regression.trx） | build 成功，0 警告／0 錯誤，5.06 秒；100/100 PASS，0 skipped |

以上 TRX 已複製至工作區 `artifacts/r6-candidate-evidence/baseline/`（gitignored）。

最後邊界檢查另重現零帶極大指數的保存錯誤。PostgreSQL 的 [numeric 解析器](https://github.com/postgres/postgres/blob/REL_17_STABLE/src/backend/utils/adt/numeric.c#L6768-L6819) 即使數值為零，也拒絕原始指數絕對值大於 `int.MaxValue / 2`。唯讀 DB cast 確認 `0e1073741823` 合法，下一值 `0e1073741824` 不合法；測試覆蓋合法零值仍成功、非法輸入 Create／PATCH 400 且不寫入。此必要修正與 Candidate 重構分開提交；首筆測試基準／必要修正提交為 `a19237f6ef5d2a2eef1dc954fd26dc2850653b3a`。

### 重構後實測

新增 `CandidateSafety.cs` 共 6 案例：無效 Candidate 不改追蹤 graph（1）、欄位來源優先於明確不變值（Question sort／Option sort／Option value，共 3）、Option 追蹤時機與 DB 回應一致（1）、兩次保存成功但 commit 失敗的回滾（1）。加強既有 SQL 與三組混合操作的斷言，不重複新增案例。

| 容器內實跑命令／階段 | 實際結果 |
| --- | --- |
| 初次有限重構 Form filter | 52/52 PASS，refactor-focused.trx |
| 補安全案例與 SQL 檢查後 Form filter | 58/58 PASS；2 個 MSTest 寫法警告後續修正為 Assert.HasCount，未刪除斷言 |
| 清理上述警告後 build + full test | build 0 警告／0 錯誤；103/103 PASS，refactor-regression.trx |
| 最終 `dotnet build CustomFormService.sln --no-restore -m:1 -p:UseSharedCompilation=false` | 成功，0 警告／0 錯誤，7.11 秒 |
| 最終 `dotnet test tests/CustomFormService.IntegrationTests/CustomFormService.IntegrationTests.csproj --no-build --no-restore -m:1 --filter FullyQualifiedName~FormEndpointsTests --logger "console;verbosity=minimal" --logger "trx;LogFileName=refactor-final-focused.trx"` | 61/61 PASS，0 FAIL／0 skipped |
| 最終 `dotnet test CustomFormService.sln --no-build --no-restore -m:1 --logger "console;verbosity=minimal" --logger "trx;LogFileName=refactor-final-regression.trx"` | 106/106 PASS，0 FAIL／0 skipped |
| `git diff --check` 與 scope／secrets diff 檢查 | 通過；Git 僅提醒 LF→CRLF 正常轉換，沒有 whitespace error；無其他 Round、Secret 或正式規格／Schema 變更 |

最終 TRX 保存在 `artifacts/r6-candidate-evidence/refactor/`（gitignored），已讀回 counters 確認 61／106 案均實際執行並通過。全套包含舊 Auth／User／Create／List／Detail／Delete 與既有 R6 競爭、top-level code、驗證順序、重複 ID、原子性、value 交換／重用、兩個正規化及三組混合操作。

回滾證據：既有 `Patch_rolls_back_first_save_when_second_save_has_database_failure` 實際 Save #1 成功，Save #2 注入 PostgreSQL 錯誤，確認受控 500、重新 GET 與所有 DB 欄位（ID、createdAt、updatedAt）恢復，新增 Question／Option ID 零殘留。新增 commit 故障案例則確認 Save #1／#2 都成功後，commit 注入 PostgreSQL 錯誤仍不得回 200，DB 全量恢復。不是僅拋出記憶體 Exception 或只比較 HTTP status。

追蹤探針確認 AutoDetectChanges 維持啟用、Save #1 沒有 Added Option，追蹤 graph 不含新的 value；Save #2 才追蹤新增與 replacement Options。回應測試使用含次微秒的 deadline，要求 PATCH 與 GET 完整 JSON 相同，並逐欄比對 DB；replacement 的 ID／createdAt 不變。

### 第一輪 review 的保留判定

| 項目 | 本次結果與證據 |
| --- | --- |
| 1 並行控制 | 保留既有 Form row lock：讀取、Draft 檢查、Candidate、所有保存及回應重讀在同一交易；既有兩 PATCH 競爭測試本輪重跑 PASS。沒有提前實作 Publish，Patch／Publish 的真正端點競爭仍留 R7。 |
| 2 path | 保留 Request 索引定位，改欄位級 provenance；排序與 value 衝突雙方向／明確不變值測試 PASS，不再解析 Candidate 集合 path。 |
| 3 questions 型別 | 保留頂層 null／非陣列的 VALIDATION_ERROR；既有 top-level／path／優先序測試 PASS。 |
| 4 驗證優先序 | 保留分階段 preflight 與正式契約次序；不改為遍歷即套 Entity。 |
| 5 value 保存 | 交換與刪除後同 value 新增，三組混合操作均 PASS；合法操作沒有改成拒絕，也未變更 constraint。 |
| 6 UUID 格式 | 規格未要求固定字面格式，Owner 明確排除本次調整；Guid.TryParse 行為不變。 |
| 7 多餘 UPDATE | 不以靜態推論定為 bug；本輪以 SQL interceptor 實測 title／Form-only／no-op 的具體命令，差異保存沒有全量 UPDATE。 |

## 提交、未驗證與停止點

起始 HEAD：`afeeb2cb22cd9bc13074eeca51c22f0b6c6e401d`。

1. `a19237f6ef5d2a2eef1dc954fd26dc2850653b3a` — `fix(forms): establish exact candidate contract baseline`：測試與已重現必要修正，97/97 PASS。
2. `c9c621b38ec70179f208b69f4dd9accb22e28d3d` — `fix(forms): reject unsafe zero numeric exponents`：最後發現的必要邊界修正，獨立階段 100/100 PASS。
3. `refactor(forms): persist typed candidate changesets`：本報告所屬重構提交；完整 SHA 於開發交付訊息提供，最終 106/106 PASS。沒有 rewrite 或混入未驗證程式。

Docker 暫存容器、SDK 下載 layer／archive 與匯入 helper 已清理；僅保留官方 SDK 映像及忽略目錄內的 TRX／manifest／config 證據。既有 PostgreSQL container、volume、User Secrets 與開發／驗收 fixture 未刪除；測試只清理自身合成 fixture。可重新下載同一官方映像，未調降 Windows 安全設定。

未驗證／延後：Windows 原生新版測試仍未有成功執行證據（先前 SAC 封鎖未處理）；正式 R6 驗收未準備／未執行；R7 Patch／Publish 競爭、G1、UUID 格式／lock 取得時機優化均不在本次範圍。沒有已知失敗的本次開發案例。

Create 共用驗證已改動，過去 R5 開發／驗收紀錄屬歷史證據，本次以全套回歸驗證現況；不得宣稱舊 R5 AI case 缺口已被本次補齊或重新正式驗收。

流程文字差異：Implementation Plan §3.4／§6 仍含完成後自動準備驗收的舊描述，與 AGENTS.md「Implementation rounds and phase boundaries」不一致。本次依 AGENTS.md 及 Owner 明確授權停在開發交付，未推進受影響的驗收階段；沒有自行改寫通用流程。

R6 已可進入正式驗收文件準備階段，但需 Owner 另行要求，再使用 acceptance skill 準備成對 Markdown／.http 文件並停止待審核。本次沒有建立這兩份 R6 文件，沒有把自動測試 PASS 記成驗收 PASS，也沒有自動開始 R7。
