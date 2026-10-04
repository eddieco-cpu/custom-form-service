# R8 A — Answer 驗證與正規化開發報告

**目前狀態（2026-10-04）**：Owner 已確認 A code review 通過，明確授權依核准計畫開始 B。下方保留 A 初次交付／補件的歷史命令與停止狀態；不將開發 review 視為 R8 正式驗收。

2026-10-04；**A 開發完成、待 review**。Owner 核准計畫 `1a0cf46`，明確授權六項局部補充及 A；B 未授權／未開始。開發驗證不代表 R8 完成或正式驗收，R7 accepted 狀態亦未改填。

## 主要變更與規格對照

- 新增 `src/CustomFormService.Api/Validation/SubmissionAnswerValidator.cs`，無 DbContext／Clock／EF 修改：`TryReadEnvelope` 僅判 Body object、未知／唯讀／重複欄位及可安全巡覽的 Item 欄位；`answers` 缺少、null、非 Array 均延後。`TryNormalizeAnswers` 在 B 的 Published／deadline／single 判定後才執行 Item Shape／UUID／重複 ID → Question 歸屬 → Required／題型／Config／Option → omission。對照 03c §4.1～4.4、03a §6、04 §6.1／9、05 §12。
- 五題型正規化：text Unicode trim／Code Point 長度，不改大小寫或碼點序列；number 沿用 DefinitionNumber 精確比較，Clone 保存精確 JSON 值；rating 沿用 ValueKind.Number + TryGetInt32；Choice 用對應 Question 的 Option UUID，解析後判重並統一 D 表示，multiple 保留輸入 ID 順序、一題一筆 normalized value。對照 01 §3／QST／SUB-01～03、02 §11、03c §4.2。
- Required 完全缺題 path 為 `questions.<id>`，合法空 text／空 multiple 為 `answers[i].value`，均 REQUIRED_FIELD；明確 value:null 在全部 Required／Optional 題型均 INVALID_VALUE。Question／Option 歸屬錯誤用正式 detail code，不變成 HTTP 404。錯誤不返回部分正規化結果。
- Envelope 輸出的 JsonElement 使用 Clone；number value 使用 Clone，重建 text／rating／Choice value 使用 SerializeToElement。來源 JsonDocument 釋放後仍可讀取與再序列化，五題型均有生命週期測試。
- 新增 `tests/CustomFormService.IntegrationTests/SubmissionAnswerValidatorTests.cs`，78 案（含 DataRow 展開）；只有兩個 DB probe 用參數化 SELECT，其餘不啟動 API／不寫 DB。補充計畫、更新 Implementation Plan 的 A 狀態與 R10 精確數值依賴。

未修改現有 RequestJson／TextRules／DefinitionNumber／FormDefinitionValidator／Controllers／DI／Schema／Migration。Validator 的定義輸入須已有效且凍結；未重複執行 Publish Validation，不重新設計共用 Definition 架構。

## 六項 review 補充的實測與界線

1. **Text 受控處理**：先用既有 .NET 10.0.400 runtime probe 確認 `JsonDocument.Parse("\"\\uD800\"")` 可成功，但 GetString 拋 InvalidOperationException；low surrogate 同樣拒絕。`a\u0000b` 可解碼，PostgreSQL 17.11 JSONB SELECT 實際拒絕，SQLSTATE 22P05。A 在 String 解碼點捕捉孤立 surrogate，在 trim／保存前拒絕 NUL，回 `answers[i].value` + INVALID_VALUE；pair `\uD83D\uDE00` 解碼為原 emoji，Code Point 計數 1，JSONB SELECT 輸出原 emoji，證明字串相容性。不移除／替換不合法字元，也不宣稱 R6 已具有此保護。孤立 surrogate property name 亦受控定位 Body／Item。
2. **Rating／null**：既有 Integer predicate 的實測為 5 接受；5.0、5e0、2147483648、1e1000 拒絕。min／max 含邊界。Required 缺題及合法空值用 REQUIRED_FIELD；明確 null 不降格成 omission，全部題型／Required 與 Optional 均用 INVALID_VALUE，正確 path 已斷言。
3. **生命週期**：Envelope Clone 可在原 document Dispose 後驗證；最終五種 normalized value 可在直接來源 Dispose 後讀取及重新序列化，沒有借用來源 buffer。
4. **驗證次序**：四案確認 Envelope 對缺少／null／object／boolean answers 均不提前報錯，後段才回 answers 的 REQUIRED_FIELD／INVALID_VALUE。這保留「非 Published + 缺少 answers → 409 FORM_NOT_SUBMITTABLE」的 B 呼叫次序；本次沒有 Submit 路由，**未宣稱這個 HTTP 409 已整合實測**，其端點測試已列 B。A 的 400 測試實測 validator details 經 ApiErrors.Validation 形成的正式 ObjectResult／JSON envelope，不冒稱 Submit HTTP 實測。
5. **Number 依據**：依 01 §3.3 的「超過系統可安全處理範圍必須拒絕」，沿用現有 DefinitionNumber.cs 的 PostgreSQL 17 numeric／JSONB 範圍：131072 integral digits、16383 fractional digits、raw exponent guard int.MaxValue / 2（含零）。本次 SELECT probes 接受 1e131071／1e-16383，拒絕 1e131072／1e-16384／0e±2147483647；精度超過 decimal 的合法值與 min／max 精確比較及 JSONB 等值均實測，不經 double／decimal。既有依據連結已在 DefinitionNumber.cs 註解；本次未改範圍或列為規格衝突。Receipt 不承諾 token 字面相等；R10 的精確數值分組／排序已登記，未提前實作。
6. **B 證據界線**：計畫已明列「INSERT Response／至少一筆 Answer 後、剩餘保存前」及「全部 Save 後、TransactionCommittingAsync／COMMIT 送出前」兩注入點；Delete 對應 UPDATE 後／COMMIT 前。只對已確認提交前故障主張零殘留，COMMIT 已送達但結果未知不能推論零新增。不新增重試框架。gate／waiter／完成等待均有限時、finally 釋放與清理；未確認 waiter 不可稱並行 PASS。以上本次僅修訂計畫，未實作或測試 B；B 可拆 Delete／Submit commits，不增加 review gate。

## 實跑命令與結果

本機證據（gitignored，保留）：`artifacts/r8-a/`。Windows SDK 10.0.400；Linux 官方 SDK image `mcr.microsoft.com/dotnet/sdk:10.0.400`；PostgreSQL `postgres:17.11-alpine` healthy。未調整 Windows 安全設定。

| 命令／實際執行 | 結果與證據 |
| --- | --- |
| `dotnet run --project artifacts/r8-a/runtime-probe/runtime-probe.csproj` | exit 0；實測 String decode／Integer predicate，結果如上。工具輸出保留於本 chat；probe 原始碼留 ignored artifacts。 |
| 沙箱內 `dotnet restore CustomFormService.sln --disable-parallel`（兩次） | exit 1、無診斷輸出；沒有宣稱 restore 成功。首個 redirected log 為空。 |
| 已獲執行權限的 `dotnet restore CustomFormService.sln --disable-parallel --verbosity minimal` | exit 0，套件均最新；`windows-restore-escalated.log`。 |
| `dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1 -p:UseSharedCompilation=false` | 首次測試 raw string 編譯錯誤 CS9007；修正後第二次 build 成功但有 10 個新增 MSTest analyzer warnings；修正 assertions 後最終 **0 warnings／0 errors**。依次 `windows-build-first.log`、`windows-build-second.log`、`windows-build.log`，原錯誤紀錄保留。 |
| Windows `dotnet test CustomFormService.sln --no-build --no-restore --configuration Debug -m:1 --filter FullyQualifiedName~SubmissionAnswerValidatorTests --logger "console;verbosity=normal" --logger "trx;LogFileName=windows-a-first.trx" --results-directory artifacts/r8-a` | exit 1，MSTest discovery 載入 API DLL 被應用程式控制政策封鎖 0x800711C7，**未執行案例**。`windows-a-first.log/.trx`；Windows full 未執行，不能填 PASS。 |
| `docker exec r8-a-development-sdk bash /evidence/linux-prepare.sh` | source tar 排除 bin／obj，暫存副本 `/work/r8-a` restore／相同 flags build，exit 0、0 warnings/errors；`linux-prepare-first.log`。source／NuGet／既有 User Secrets 唯讀。 |
| Linux `dotnet test`，相同 solution／Debug／no-build／no-restore／-m:1，filter `FullyQualifiedName~SubmissionAnswerValidatorTests`，TRX `linux-a-first.trx`，results `/evidence` | **首次 78/78 PASS**，0 failed／notExecuted；0.7713 秒，exit 0；`linux-a-first.log/.trx`。 |
| Linux 同上、無 filter，TRX `linux-full-first.trx` | **首次全套 245/245 PASS**（原有 167 + A 78），0 failed／notExecuted；6.7645 秒，exit 0；`linux-full-first.log/.trx`。含既有 R6／R7／CORS 回歸，不把 Linux 結果冒稱 Windows PASS。 |
| 唯讀 fixtures snapshot、TRX counters、diff／提交範圍檢查 | `fixtures-before.json` 與 `fixtures-after.json` 逐字相等；245 executed／passed、0 failed／notExecuted；`git diff --check` 通過。 |

## 資料安全、未驗證事項與交付

既有 fixtures 非秘密完整快照（Users 不含 password hash）前後相同：4 Users、9 Forms、22 Questions、12 Options、0 Responses、0 Answers。新 DB probes 均 SELECT，不建立 Response／Answer；原有 regression 只清測試自身隨機 fixtures。未改 User Secrets／環境設定／安全政策，未刪 DB volume。證據匯出後已 stop／rm 本次唯一 `r8-a-development-sdk` 容器，沒有移除 PostgreSQL 或 volume。

未做：Submit HTTP、Deadline／Clock、Receipt、原子保存／並行、Delete 修正、R9／R10／G1、正式驗收計畫／執行。未確認：本次 DLL 在 Windows testhost 的成功載入與 Windows focused／full；目前由 Linux 同套必要測試提供開發證據。沒有 R8 既有驗收結果需要作廢；R4～R7 正式文件與歷史結果未改填。

主要變更檔為 validator、tests、R8 計畫、本報告與 Implementation Plan（5 檔）。單一目的開發提交 SHA 與提交後 Git 狀態見本次交付訊息。停止於 **「A 開發完成、待 review」**，等待 Owner review 及 B 明確授權。

## Code review 補件（2026-10-04）

原 A 開發提交為 `986cfd86be2ff906734ba4b4895918cdc24ba1ea`。本次補件索引／原 commit 完整 diff／依賴檔案位置見 [R8_A_Review_Materials.md](R8_A_Review_Materials.md)。原 78 案中，非空 Text 的失敗案例只覆蓋超過 maxLength，未覆蓋短於 minLength；本次只補 `Nonempty_text_shorter_than_minLength_returns_invalid_value`：optional text、minLength=2、輸入 `" a "` trim 後一個 Code Point、非空，預期 `answers[0].value` + INVALID_VALUE／正式 400，不能被當成 omission。未修改 validator／Definition Validation，亦未擴大 UUID／Unicode 矩陣。

所有 A JSONB probes 僅以參數化 SQL SELECT 驗證相容性／數值等值，不涉及 EF INSERT、Response／Answer 保存、Receipt 映射或 HTTP round-trip。數值 probe 改名為 `Number_jsonb_probes_verify_compatibility_and_exact_numeric_equality`，只更正名稱，查詢與斷言不變。EF 保存與 Receipt round-trip 明確留 B。原 build／78 focused／245 full 證據保持歷史紀錄；本次新增案例的實跑命令與結果另列補件索引。
