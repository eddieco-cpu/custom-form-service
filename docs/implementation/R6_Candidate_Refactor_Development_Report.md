# R6 有限 Candidate 重構 — 開發進度

日期：2026-10-03。狀態：in progress，測試基準與必要修正已完成，有限重構待完成；**未達開發完成，未進入正式驗收**。

## 範圍與本次修改

依 AGENTS.md、v1-round-development skill、最新正式規格與 Owner 授權，先建立測試基準，再做有限 Candidate／ChangeSet 重構。沒有修改正式規格、DB schema 或 Migration；R7、G1、驗收準備與驗收執行均未開始。

新增 `tests/CustomFormService.IntegrationTests/FormEndpointsTests.CandidateContract.cs`，共 36 個資料驅動／一般測試案例：空 Question／Option 陣列 no-op（1）、Type 切換與 Choice 互轉（6）、Config 完整取代與語意 no-op（1）、共享 Create／PATCH Config 邊界（12）、Number 精確數值（11）、JSONB 實際儲存邊界（2）、Question／Option 排序交換（1）、欄位來源 path（2）。沿用既有 fixture、查詢與清理流程；不重複新增已有的 value 交換／重用、三組混合操作或第二次 SaveChanges 故障回滾測試。

已重現並修正：Number Config 使用 decimal 拒絕 JSONB 可精確保存的 1e30／1e400，且將高精度相鄰數值或 1e-40 與 0 在比較時捨入為相同，錯誤接受 min > max。新增 DefinitionNumber 直接比较十進位有效數字與指數，保留 JSON 原數值，不經 decimal／double；超出實際 JSONB numeric 儲存能力時受控拒絕。案例先以實際 JSONB cast 證明安全／不安全，再檢查 Create／PATCH 與 DB。儲存邊界依 [PostgreSQL JSONB](https://www.postgresql.org/docs/17/datatype-json.html) 與 [numeric](https://www.postgresql.org/docs/17/datatype-numeric.html)，不是新增產品精度或業務範圍。

另修正 Config 正規化後的等值 no-op（屬性順序、1 與 1.0 不算變更），以及 sortOrder 衝突不能映射到 Request 僅改 title／label 的位置。這些是規格修正，不將舊錯誤行為固定成基準。規格對照：01 §3.3／FORM-05～06／QST、03b §6.1／7.2、04 §2.4／6、05 §10～12。

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

TRX 保存在容器 `/work/baseline/tests/CustomFormService.IntegrationTests/TestResults/`；交付時保留可讀證據，不以此取代驗收成對文件。

## 待處理與停止點

Docker 環境已恢复測試能力；下一步依已授權範圍實作有限重構及第二個提交。Windows 原生的新測試仍無成功執行證據，不冒稱已解除 Windows 封鎖。

重構仍須保留 Form lock、Draft／權限檢查、交易與驗證優先序；使用獨立型別化 Candidate、欄位級 Request 來源及差異 ChangeSet。新／replacement Option 在第一階段保存成功前不得進入 tracked graph；保留 Alternate Key、同 ID／createdAt 與完整交易回滾。PATCH 如重新讀取回應，須在同一交易以 AsNoTracking 取得 DB 值，commit 成功後才回傳。這些均為待實作要求，不是已完成項目。

未驗證：有限重構及其回歸、Windows 原生的新測試執行、正式驗收。測試與必要修正為第一個單一目的提交；起始 HEAD 為 `afeeb2cb22cd9bc13074eeca51c22f0b6c6e401d`。SDK 匯入輔助與下載產物位於 gitignored artifacts，不提交至產品。沒有混入其他 Round。
