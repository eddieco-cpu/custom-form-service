# R8 A — Code review 補件索引

2026-10-04；**A 開發完成、待 review；B 未授權／未開始**。原開發提交：`986cfd86be2ff906734ba4b4895918cdc24ba1ea`。本次僅補交 review 材料、一個缺少的代表測試與必要驗證，沒有修改 validator／Definition Validation／正式規格或擴大 UUID／Unicode 矩陣。

## 1. 四項依賴

以下依賴均未在原 R8 A 或本次補件修改；review snapshots 保留於 `artifacts/r8-a-review/dependencies/`。前三檔副本與 repo 檔案相同；Factory class 擷取自原檔第 109 行至結尾，連同原 using／namespace 與明確的 review-only 註解提供，不拆動編譯來源。

| 材料 | Repository 原位置 | Review snapshot |
| --- | --- | --- |
| RequestJson.cs | [原檔](../../src/CustomFormService.Api/Validation/RequestJson.cs) | [副本](../../artifacts/r8-a-review/dependencies/RequestJson.cs) |
| DefinitionNumber.cs | [原檔](../../src/CustomFormService.Api/Validation/DefinitionNumber.cs) | [副本](../../artifacts/r8-a-review/dependencies/DefinitionNumber.cs) |
| TextRules.cs | [原檔](../../src/CustomFormService.Api/Validation/TextRules.cs) | [副本](../../artifacts/r8-a-review/dependencies/TextRules.cs) |
| CustomFormServiceApiFactory | 實際沒有獨立 CustomFormServiceApiFactory.cs；class 位於 [EngineeringEndpointsTests.cs](../../tests/CustomFormService.IntegrationTests/EngineeringEndpointsTests.cs) 第 109 行 | [class 擷取檔](../../artifacts/r8-a-review/dependencies/CustomFormServiceApiFactory.cs)、[完整原檔副本](../../artifacts/r8-a-review/dependencies/EngineeringEndpointsTests.original.cs) |

Factory 中只有既有固定 synthetic test JWT 設定；補件不包含 User Secrets／DB credentials／真實 tokens。副本不加入產品編譯或 Git source tree。

## 2. 完整 commit diff

- [原 A commit 完整 diff](../../artifacts/r8-a-review/986cfd8-full.diff)：使用 `git show --format=fuller --binary --no-ext-diff --no-color 986cfd86be2ff906734ba4b4895918cdc24ba1ea` 匯出，不省略／截斷任何 hunk。`git apply --stat` 已確認 **5 檔、749 insertions、13 deletions**，包含 validator、原 78 案測試、R8_A_Development_Report.md、R8_Implementation_Plan.md 與 V1_Backend_Implementation_Plan.md。
- [本次補件完整 diff](../../artifacts/r8-a-review/review-supplement-full.diff)：本次單一目的 commit 後以同一 git show flags 匯出，包含新增代表測試／probe 名稱修正／文件界線與實測結果；不取代原 A diff。
- [完整 review 材料 ZIP](../../artifacts/r8-a-review/R8_A_Review_Bundle.zip)：包含兩份完整 diff、四項依賴、Factory 原檔及擷取檔、目前 validator／tests、三份交付文件與兩份計畫、build／focused／full logs 與 TRX；不包含秘密設定或 fixtures 資料。

## 3. minLength 代表測試

原 78 案**沒有非空 Text 短於 minLength 的失敗案例**：minLength=5 的空 text 是 omission，`Text_length_counts_code_points_without_replacement_or_normalization` 的失敗是長度超過 maxLength=1，不能冒稱 minLength 下界證據。

新增唯一案例：`Nonempty_text_shorter_than_minLength_returns_invalid_value`，位於 [SubmissionAnswerValidatorTests.cs](../../tests/CustomFormService.IntegrationTests/SubmissionAnswerValidatorTests.cs)。Optional text、minLength=2、輸入 `" a "` trim 後仍非空、長度 1；斷言 `answers[0].value` + `INVALID_VALUE`、無 partial normalized results，並經 ApiErrors.Validation 驗證正式 400 envelope。這只驗 A validator／ObjectResult，不是 Submit HTTP 測試。Linux focused／full TRX 均已逐名核對該案 Passed。

## 4. 命令與結果

原 `986cfd8` 的 Windows build／載入封鎖、Linux 78 focused／245 full、命令、初次 build 錯誤／修正與資料安全證據直接引用 [R8_A_Development_Report.md「實跑命令與結果」](R8_A_Development_Report.md#實跑命令與結果)，不覆寫歷史結果。以下為本次補件的新增實跑紀錄（證據：`artifacts/r8-a-review/`）：

| 實跑命令／執行位置 | 結果 |
| --- | --- |
| Windows `dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1 -p:UseSharedCompilation=false` | exit 0、**0 warnings／0 errors**；9.82 秒；[windows-build.log](../../artifacts/r8-a-review/windows-build.log)。 |
| Windows `dotnet test CustomFormService.sln --no-build --no-restore --configuration Debug -m:1 --filter FullyQualifiedName~Nonempty_text_shorter_than_minLength_returns_invalid_value --logger "console;verbosity=normal" --logger "trx;LogFileName=windows-minlength-first.trx" --results-directory artifacts/r8-a-review` | exit 1；discovery 載入 API DLL 仍被應用程式控制政策封鎖 0x800711C7，未執行案例；[log](../../artifacts/r8-a-review/windows-minlength-first.log)／[TRX](../../artifacts/r8-a-review/windows-minlength-first.trx)。未停用／變更安全政策，不填 Windows PASS。 |
| `docker exec r8-a-review-sdk bash /evidence/linux-prepare.sh` | `/work/r8-a-review` 暫存副本 restore／同 Windows build flags，exit 0、0 warnings／0 errors；[script](../../artifacts/r8-a-review/linux-prepare.sh)／[log](../../artifacts/r8-a-review/linux-build.log)。官方 SDK 10.0.400，source／NuGet／User Secrets 唯讀。 |
| Linux `/work/r8-a-review`：`dotnet test CustomFormService.sln --no-build --no-restore --configuration Debug -m:1 --filter FullyQualifiedName~SubmissionAnswerValidatorTests --logger "console;verbosity=normal" --logger "trx;LogFileName=linux-a.trx" --results-directory /evidence` | **79/79 PASS**，0 failed／notExecuted，exit 0，1.1027 秒；[log](../../artifacts/r8-a-review/linux-a.log)／[TRX](../../artifacts/r8-a-review/linux-a.trx)。 |
| Linux 同位置：`dotnet test CustomFormService.sln --no-build --no-restore --configuration Debug -m:1 --logger "console;verbosity=normal" --logger "trx;LogFileName=linux-full.trx" --results-directory /evidence` | **246/246 PASS**（既有 167 + A 79），0 failed／notExecuted，exit 0，7.8870 秒；[log](../../artifacts/r8-a-review/linux-full.log)／[TRX](../../artifacts/r8-a-review/linux-full.trx)。 |

Docker test wrapper 為 `docker exec r8-a-review-sdk bash -lc '<cd /work/r8-a-review && 上表命令>'`。stdout／stderr 經 Tee-Object 保留，記錄實際 exit code；沒有把 Linux 結果冒稱 Windows PASS。完成後 before／after 非秘密 fixtures 快照逐字相同，stop／rm 本次唯一 SDK 容器；原 PostgreSQL 17.11 healthy、volume 未動。`git diff --check` 通過。

## 5. JSONB 證據界線與停止點

`Number_jsonb_probes_verify_compatibility_and_exact_numeric_equality`（只更正原名稱）與 `Text_jsonb_probe_rejects_nul_and_accepts_valid_pair_without_replacement` 使用參數化 SQL SELECT：前者判 storable／unstorable JSONB 及精確數值等值，後者判 NUL 不相容／合法 pair 可解碼。**沒有 EF INSERT、Response／Answer 保存或 Receipt round-trip 證據**；這些工作與驗證均留 B，沒有新增或提前執行 Submit。

補件以單一目的 commit 提交，SHA／提交後 Git 狀態見交付訊息及 supplement diff header。仍停止於 **「A 開發完成、待 review」**，未授權 B，未準備／執行正式驗收。
