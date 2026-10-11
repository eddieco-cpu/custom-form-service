# R10 — AI 正式驗收執行報告

日期：2026-10-11（Asia/Taipei）。**R10 accepted：Owner 本 chat 明確確認「R10 驗收通過」**。

確認依據：結果提交 `312592451a1d48805f9521bb4074846da32d38f1`，補驗 `r10-acceptance-20261011-02` 的 45 HTTP／3 非 HTTP PASS、同次新產物 full 420/420（R10 43/43）。人工仍為「未執行（本次僅 AI）」；Owner acceptance 與人工實跑是不同紀錄，不代填人工 PASS。首次 FAIL／BLOCKED、review runtime 限制、RV-I-01／I-01／I-02 及其他 Round pending 均保留。

下文為 Owner 確認前的執行報告歷史；其中「待 Owner review／未 accepted／R11 未開始」描述當時狀態。最新進度以本段及 [Implementation Plan](V1_Backend_Implementation_Plan.md) 為準。本次只同步 R10 確認並準備 R11 計畫，不重跑驗收；G1、前端與部署維持 pending。

## 1. 結果、授權與執行版本

Owner 在本 chat 核准 [成對 Markdown](../test-plan/R10_Acceptance_Test.md)／[.http](../test-plan/R10_Manual_Tests.http) 的 `f072b4c07c437d59e57c8cfadc6244220e5cfdfa`，明確授權 AI Execute。執行 HEAD 同上，source/tests/specs 與產品候選 `a4f9402336a4eecdc07c72e92ef09234a4d5e976` 無差異；未修改產品、測試或已核准預期。

| RunId | restore/build／native | 同次 full／R10 | 45 HTTP | 01／02／15 | 人工 |
| --- | --- | --- | --- | --- | --- |
| r10-acceptance-20261011-01（首次歷史） | 正常權限 restore 成功；build 0 warnings/errors；新 PID 23612 health/readiness PASS | 420/420／43/43，0 failed/skipped | **40 PASS／1 FAIL／4 BLOCKED** | 3 PASS；15 核對實際部分狀態 | 未執行（本次僅 AI） |
| r10-acceptance-20261011-02（目前補驗） | 本次 restore／非增量 build 0 warnings/errors；新 PID 36360 health/readiness PASS | **420/420／43/43**，0 failed/skipped | **45 PASS**，0 FAIL/BLOCKED/未執行 | **3 PASS** | 未執行（本次僅 AI） |

-02 開始 UTC `2026-10-11T05:44:05.9737549+00:00`，結果整理完成 `2026-10-11T05:53:19.7227309+00:00`（台北當日 13:44～13:53）。必要順序實際為 01 baseline → restore/build → 產物/PDB → 本次 native／01a/b → 02 同次 full／TRX／DB baseline → 03～14 原生 HTTP → 15 SQL／資料保留／清理。每次新 RunId、新 emails/UUIDs、新 EvidenceRoot；-01 失敗與合法資料未重用或清除。

兩次新 DLL runtime 都未重現 review 的 Windows 應用程式控制 0x800711C7；**沒有修改 Windows 政策，沒有使用舊 binaries 或 Linux／TestServer 代填 native 結果**。review 以經 source/PDB 核對的既有產物跑 43/420 的限制仍保留於 [獨立 review](R10_Independent_Code_Review_Report.md)，不能回溯改稱該次新 build PASS，也不宣稱政策永久解除。

## 2. 本次產物與原生程序身分

SDK 10.0.400，global.json 10.0.400／latestPatch；Windows、PowerShell 版本及非秘密 DB 配置見 [manifest.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/manifest.json)、[configuration-nonsecret.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/configuration-nonsecret.json)。DB 原有 postgres:17.11-alpine container `e8f01e2c1bee`、`127.0.0.1:5432`、原 volume 保持；原 API PID 36804／5000 未替代驗收端點。

本次輸出根為 `C:/Users/user/Desktop/custom-form-service/artifacts/r10-acceptance/r10-acceptance-20261011-02/build/Debug/net10.0/`，沒有從 src/bin／tests/bin／舊 artifacts 搬入檔案。新輸出與 obj 四個 DLL/PDB hash 相等；CreationTime／LastWriteTime 在本次 build，重編譯、依賴／runtimeconfig／PDB 逐檔 hash 在 [artifacts-manifest.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/artifacts-manifest.json)、[obj-artifacts-audit.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/obj-artifacts-audit.json)。兩次相同 DLL hash 是相同 source 的本次編譯結果，不能以 hash 相同本身代替路徑／時間／程序證據。

| 產物 | SHA256 |
| --- | --- |
| CustomFormService.Api.dll | F190354ABDB511460A254A69FFC3711FC7B6A24284BFE334261E2D639F2FF9FA |
| CustomFormService.IntegrationTests.dll | 536FEDBC715AA888916DF90E7936555845DE67587E2A4937D8F367034AE6FCD2 |

[source-pdb-audit.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/source-pdb-audit.json)：88/88 文件 checksum 一致，包括 78 repo source、9 generated、1 SDK test Program source；generated 中 7 個由本次 obj 檔核對、2 個由 PDB embedded source 核對 virtual obj path，另保存 generated source，且 obj／輸出 DLL/PDB 四個 hash 相等。不是沿用 review 的歷史「78」結論。

原生 [native-stage.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/native-stage.json)／[native.stdout.log](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/native.stdout.log) 記錄：

```text
PID: 36360
dotnet: C:/Program Files/dotnet/dotnet.exe
CommandLine: dotnet exec "<上述本次輸出根>CustomFormService.Api.dll"
Working directory / content root: C:/Users/user/Desktop/custom-form-service/src/CustomFormService.Api
Environment: Development
URL: http://localhost:5011
build finished UTC: 2026-10-11T05:44:22.9871627+00:00
launch UTC: 2026-10-11T05:46:13.2300555+00:00
```

5011 的 IPv4/IPv6 listener 均屬該 PID，啟動晚於本次 build 結束，啟動後 DLL SHA256 不變。01a HTTP 200 `{"status":"healthy"}`，01b HTTP 200 `Healthy`；均保存 [01a.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/01a.json)／[01b.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/01b.json) 與實際 raw Body。Windows 政策、User Secrets／.env、既有服務設定未持久變更。

## 3. 首次失敗、工具校正及歷史保留

首次 -01 的 12c 收到 200，raw JSON 的五題型分布、目前名單、精確 Number 全符合契約；執行器的 `$roles=if (...) {...}` 對單元素回傳 scalar `owner`，`$roles[0]` 得到字元 o，查不存在的 oId，故誤報「Correct sorted respondent ID」。保留 [首次腳本](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/native-acceptance.ps1)、[12c 原始結果](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/12c.json)、[raw JSON](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/12c.body.json)。只改執行器為外層 `@(...)` 保持 Array；[補核對](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/12c-reinspection.json) 使用同一 captured Body，未重送 HTTP，原結果仍 FAIL；12d／13a／14a／14b 原結果仍 BLOCKED。

-01 的密碼／Cookie 清除後未重建舊身份登入，未盲重送 POST；完成 15 實際 SQL／清理，保留全部合法 fixtures、main 尚未刪的狀態。-02 依原核准請求／預期重做完整流程，只有 RunId／合成 IDs 改變，無產品修改。上表／§5 逐案保留兩次結果，不覆寫首次失敗。

其他首次工具診斷也保留，不冒充產品缺陷：

- Docker named-pipe inventory 在一般 sandbox Access denied；正常核准權限只讀檢查成功。-01 restore 一般 sandbox exit 1、log 空，正常權限相同命令 exit 0；[首次 stage](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/restore-first-sandbox-stage.json)／[首次空 log](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/restore-first-sandbox.log) 保留，未推定具體底層根因。
- 首次 PDB audit 對兩個 virtual generated obj path 記 MISSING；改以其 PDB embedded source checksum＋本次 obj/output hash 完成核對，[初始 audit](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/source-pdb-audit-first.json) 保留。
- 首次 DB json_agg 多行擷取只取首行，另將 JSON datetime 自動轉 DateTime 後的預設字串截去微秒；只修正證據匯出為 jsonb_build_object 與解析 `-DateKind String`。讀取欄位／資料／預期未變；[原匯出](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/fixtures-final-first.json)、[原 verifier](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/verify-db-first.ps1)、[診斷](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/db-export-first-error.txt) 保留，最終 Receipt／DB exact 值通過。
- -02 TRX 摘要工具的 regex escape 初始丟失，preflight 因摘要檔未產生拒絕啟動 HTTP，未送任何請求；校正解析同一 TRX 後才通過 gate，沒有重新跑測試代填。[preflight 診斷](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/preflight-diagnostics.txt)／[停止 log](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/native-execution-before-gate.log) 保留。

## 4. 實際命令、自動化／SQL／同快照證據

所有階段分開保存 exit／UTC，命令在 repo 執行；產物路徑始終為各自當次 BuildRoot。下列為 -02 實際命令（完整動態絕對路徑在 stage JSON）：

| 命令／工具 | 結果／證據 |
| --- | --- |
| git status --short --branch；git diff a4f9402 -- src tests docs/specs | 起點 master ahead 24、乾淨；只同步驗收文件，產品／tests/specs 無差異 |
| docker compose ps --format json；docker exec -i custom-form-service-postgres-1 sh -c psql… | 本機 container healthy／host binding、未用 emails/titles/missing UUID、正式 schema/migration 核對；[docker-before.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/docker-before.json)、[db-prerequisites.txt](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/db-prerequisites.txt) |
| dotnet restore CustomFormService.sln | exit 0；[restore-stage.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/restore-stage.json)／[restore.log](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/restore.log) |
| dotnet build CustomFormService.sln --no-restore --no-incremental --configuration Debug --disable-build-servers -m:1 -p:BaseOutputPath=&lt;本次 build/&gt; | exit 0、0 warnings/errors；[build-stage.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/build-stage.json)／[build.log](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/build.log) |
| Portable PDB Document checksums／SHA256／obj 比對 | 88/88 match、4 obj/output match；上述 manifests |
| Start-Process dotnet exec &lt;本次 API DLL&gt;，Hidden；CIM／Get-NetTCPConnection | PID 36360／5011／新 DLL path/hash；[native-stage.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/native-stage.json) |
| dotnet test CustomFormService.sln --no-build --configuration Debug -p:BaseOutputPath=&lt;本次 build/&gt; --logger 'trx;LogFileName=r10-acceptance-full.trx' --results-directory &lt;本次 full/&gt; | exit 0、420/420、0 failed/skipped；R10 43/43；[test-stage.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/test-stage.json)、[full.log](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/full.log)、[TRX](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/full/r10-acceptance-full.trx) |
| System.Net.Http.HttpClient；[native-acceptance.ps1](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/native-acceptance.ps1) | 45 個核准 HTTP（含先前 01a/b），明確 Cookie、UseCookies=false、不用 Bearer／redirect／proxy；[native-execution-stage.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/native-execution-stage.json)、[all-http-results.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/all-http-results.json)、逐案 raw Body |
| 只讀 RepeatableRead psql baseline／fixtures／Effective SQL | GET 8 組無寫入、完整舊列不變、合法新 delta／Receipt 精確值；[baseline.sql](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/baseline.sql)、[fixtures-final.sql](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/fixtures-final.sql)、[db-verification.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/db-verification.json) |
| 只停止本人已核對的 PID 36360；再核對 listener／Docker | PASS；原 API／DB／volume 保留，5011 釋放；[cleanup.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/cleanup.json) |

Full TRX 的 TestMethod.codeBase 是本次 -02 `build/Debug/net10.0/CustomFormService.IntegrationTests.dll`。[regression-summary.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/regression-summary.json) 列出實際 R10 43 個 DataRows；細部映射沿核准 Markdown §6 的 10 methods，JWT Conditional Auth、literal empty Cookie、數值等值／極值、id DESC tie-break、disabled 歷史、受控 500 等均由此次 suite 承接。

[snapshot-evidence.txt](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/snapshot-evidence.txt)／[snapshot-summary.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/snapshot-summary.json)：visibility、delete、profiles、caller-disabled 四組各 9 SELECT、唯一 scoped Context ID、RepeatableRead、無 FOR UPDATE；第一個實際 Form SELECT 後 writer commit，完整 V0／fresh query 新狀態 assertions 通過，finally release／cancel／drain 證據由同 source/PDB 測試承接。不是只以 BeginTransaction 或舊 PASS 當成快照證據，也沒有新增原生競爭矩陣。

原生實際 raw JSON 驗證 Root 九欄／Question 六欄、匿名省略 respondents（包含 Owner）、identified 每 Item 僅 id/目前 displayName 且 count=length／零票 []、description/deadline 必須出現 null、Choice ID 與業務 value、完整排序／zero-data、H/H2 未合組且為 JSON Number、表定 UTC 微秒。分析使用 JsonDocument/GetRawText 的精確 key，不經 double/decimal。原生 18 Statistics 請求通過主要權限／錯誤優先序；03～14 建立／狀態操作亦依核准範本，無額外業務請求。

## 5. 逐案結果與人工欄位

下表順序與成對文件相同；-01 未發送的四案保留 BLOCKED。目前 AI 結果只來自 -02 新 build／新程序，不將歷史結果拼接成此輪 PASS。

| Case | 首次 -01 | 目前 -02 | 非秘密證據 | 人工 |
| --- | --- | --- | --- | --- |
| 01a | PASS／HTTP 200 | PASS／HTTP 200 | [01a.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/01a.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/01a.json) | 未執行（本次僅 AI） |
| 01b | PASS／HTTP 200 | PASS／HTTP 200 | [01b.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/01b.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/01b.json) | 未執行（本次僅 AI） |
| 03a | PASS／HTTP 201 | PASS／HTTP 201 | [03a.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/03a.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/03a.json) | 未執行（本次僅 AI） |
| 03b | PASS／HTTP 200 | PASS／HTTP 200 | [03b.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/03b.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/03b.json) | 未執行（本次僅 AI） |
| 03c | PASS／HTTP 201 | PASS／HTTP 201 | [03c.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/03c.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/03c.json) | 未執行（本次僅 AI） |
| 03d | PASS／HTTP 200 | PASS／HTTP 200 | [03d.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/03d.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/03d.json) | 未執行（本次僅 AI） |
| 03e | PASS／HTTP 201 | PASS／HTTP 201 | [03e.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/03e.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/03e.json) | 未執行（本次僅 AI） |
| 03f | PASS／HTTP 200 | PASS／HTTP 200 | [03f.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/03f.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/03f.json) | 未執行（本次僅 AI） |
| 04a | PASS／HTTP 201 | PASS／HTTP 201 | [04a.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/04a.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/04a.json) | 未執行（本次僅 AI） |
| 04b | PASS／HTTP 201 | PASS／HTTP 201 | [04b.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/04b.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/04b.json) | 未執行（本次僅 AI） |
| 04c | PASS／HTTP 201 | PASS／HTTP 201 | [04c.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/04c.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/04c.json) | 未執行（本次僅 AI） |
| 05a | PASS／HTTP 400 | PASS／HTTP 400 | [05a.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/05a.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/05a.json) | 未執行（本次僅 AI） |
| 05b | PASS／HTTP 404 | PASS／HTTP 404 | [05b.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/05b.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/05b.json) | 未執行（本次僅 AI） |
| 05c | PASS／HTTP 409 | PASS／HTTP 409 | [05c.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/05c.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/05c.json) | 未執行（本次僅 AI） |
| 05d | PASS／HTTP 404 | PASS／HTTP 404 | [05d.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/05d.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/05d.json) | 未執行（本次僅 AI） |
| 05e | PASS／HTTP 404 | PASS／HTTP 404 | [05e.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/05e.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/05e.json) | 未執行（本次僅 AI） |
| 06a | PASS／HTTP 200 | PASS／HTTP 200 | [06a.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/06a.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/06a.json) | 未執行（本次僅 AI） |
| 06b | PASS／HTTP 200 | PASS／HTTP 200 | [06b.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/06b.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/06b.json) | 未執行（本次僅 AI） |
| 06c | PASS／HTTP 200 | PASS／HTTP 200 | [06c.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/06c.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/06c.json) | 未執行（本次僅 AI） |
| 06d | PASS／HTTP 409 | PASS／HTTP 409 | [06d.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/06d.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/06d.json) | 未執行（本次僅 AI） |
| 06e | PASS／HTTP 404 | PASS／HTTP 404 | [06e.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/06e.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/06e.json) | 未執行（本次僅 AI） |
| 07a | PASS／HTTP 201 | PASS／HTTP 201 | [07a.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/07a.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/07a.json) | 未執行（本次僅 AI） |
| 07b | PASS／HTTP 201 | PASS／HTTP 201 | [07b.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/07b.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/07b.json) | 未執行（本次僅 AI） |
| 07c | PASS／HTTP 201 | PASS／HTTP 201 | [07c.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/07c.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/07c.json) | 未執行（本次僅 AI） |
| 07d | PASS／HTTP 201 | PASS／HTTP 201 | [07d.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/07d.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/07d.json) | 未執行（本次僅 AI） |
| 07e | PASS／HTTP 201 | PASS／HTTP 201 | [07e.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/07e.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/07e.json) | 未執行（本次僅 AI） |
| 08a | PASS／HTTP 200 | PASS／HTTP 200 | [08a.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/08a.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/08a.json) | 未執行（本次僅 AI） |
| 08b | PASS／HTTP 200 | PASS／HTTP 200 | [08b.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/08b.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/08b.json) | 未執行（本次僅 AI） |
| 08c | PASS／HTTP 200 | PASS／HTTP 200 | [08c.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/08c.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/08c.json) | 未執行（本次僅 AI） |
| 09a | PASS／HTTP 401 | PASS／HTTP 401 | [09a.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/09a.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/09a.json) | 未執行（本次僅 AI） |
| 09b | PASS／HTTP 401 | PASS／HTTP 401 | [09b.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/09b.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/09b.json) | 未執行（本次僅 AI） |
| 09c | PASS／HTTP 403 | PASS／HTTP 403 | [09c.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/09c.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/09c.json) | 未執行（本次僅 AI） |
| 09d | PASS／HTTP 200 | PASS／HTTP 200 | [09d.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/09d.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/09d.json) | 未執行（本次僅 AI） |
| 10a | PASS／HTTP 200 | PASS／HTTP 200 | [10a.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/10a.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/10a.json) | 未執行（本次僅 AI） |
| 10b | PASS／HTTP 200 | PASS／HTTP 200 | [10b.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/10b.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/10b.json) | 未執行（本次僅 AI） |
| 10c | PASS／HTTP 403 | PASS／HTTP 403 | [10c.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/10c.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/10c.json) | 未執行（本次僅 AI） |
| 11a | PASS／HTTP 200 | PASS／HTTP 200 | [11a.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/11a.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/11a.json) | 未執行（本次僅 AI） |
| 11b | PASS／HTTP 200 | PASS／HTTP 200 | [11b.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/11b.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/11b.json) | 未執行（本次僅 AI） |
| 12a | PASS／HTTP 200 | PASS／HTTP 200 | [12a.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/12a.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/12a.json) | 未執行（本次僅 AI） |
| 12b | PASS／HTTP 200 | PASS／HTTP 200 | [12b.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/12b.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/12b.json) | 未執行（本次僅 AI） |
| 12c | FAIL／HTTP 200 | PASS／HTTP 200 | [12c.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/12c.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/12c.json) | 未執行（本次僅 AI） |
| 12d | BLOCKED（未送出） | PASS／HTTP 200 | [12d.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/12d.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/12d.json) | 未執行（本次僅 AI） |
| 13a | BLOCKED（未送出） | PASS／HTTP 200 | [13a.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/13a.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/13a.json) | 未執行（本次僅 AI） |
| 14a | BLOCKED（未送出） | PASS／HTTP 204 | [14a.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/14a.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/14a.json) | 未執行（本次僅 AI） |
| 14b | BLOCKED（未送出） | PASS／HTTP 404 | [14b.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/14b.json)／[首次](../../artifacts/r10-acceptance/r10-acceptance-20261011-01/14b.json) | 未執行（本次僅 AI） |

| 非 HTTP | 首次 -01 | 目前 -02 | 證據 | 人工 |
| --- | --- | --- | --- | --- |
| 01 | PASS；含首次 sandbox restore 診斷 | PASS | manifests、source/PDB、native-stage、01a/b | 未執行（本次僅 AI） |
| 02 | PASS；420/420／R10 43/43；DB baseline 不變 | PASS；420/420／R10 43/43；DB baseline 不變 | 同次 TRX、regression-summary、snapshot-evidence、before/after-02 | 未執行（本次僅 AI） |
| 15 | PASS；記錄部分流程狀態、資料／清理；main 未刪 | PASS；完整流程狀態與資料／清理；main soft-deleted | fixtures-final.sql/json、db-final、db-verification、cleanup | 未執行（本次僅 AI） |

## 6. 資料保留與清理

初始七表 counts 為 users=11、identities=0、forms=30、questions=56、options=24、responses=21、answers=24，共 166 列。-01 舊 166 列不變；-02 baseline 包含 -01 fixtures，共 223 列，全部完整 row digest 不變，Migration ID／ProductVersion 不變。兩次各新增 users +3、identities +0、forms +3、questions +15、options +12、responses +5、answers +19，共 57 列。最終 counts：**users 17／identities 0／forms 36／questions 86／options 48／responses 31／answers 62**，共 280 列，沒有不明新增／刪除。

目前 -02 合成身份／Forms（全部 UUID 與 Questions／Options／Responses 另見 [fixtures.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/fixtures.json)）：

| 身份 | User ID | 目前名稱／狀態 |
| --- | --- | --- |
| other | a61c522c-d228-4ae0-b337-401950580d2e | R10 Other／Active |
| respondent | 4afec530-6961-4cfc-bc6d-81b3ffba4668 | R10 Respondent Updated／Active |
| owner | 4523426b-902d-4f59-bcd4-7fd82c7b5f42 | R10 Owner Updated／Active |

| Form | ID | 最終狀態 |
| --- | --- | --- |
| identified | d55ccf64-25f5-412c-b7d9-b832c79afba6 | Closed／public；single／identified／未刪／deadline=null |
| zero | 5f3959fa-ef3b-4cda-9e47-cda91f117a8f | Closed／public；single／identified／未刪／deadline=null |
| main | 1322a7f8-9fa9-4e9f-aa7a-825cb70098f4 | Closed／public；multiple／anonymous／soft-deleted／表定微秒 deadline |

每輪 main 3 Responses／9 Answers，identified 2／10，zero 0／0；Effective 每輪四份（main B latest、main A zero Answer、identified A/B）。latest submittedAt 嚴格晚於舊答卷，Text 缺題不回填、零 Answer 仍計 Root；全部 19 筆正規化保存值與 Receipt 對齊、H/H2 精確。第一輪 main 未刪，第二輪 main 合法 Soft Delete，所有 Questions／Options／Responses／Answers 與歷史保留。

-02 [get-snapshot-summary.json](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/get-snapshot-summary.json) 記錄 05／06／09／10／11／12／13／14 八組 GET 前後完整 baseline 相等；合法 PATCH／DELETE 已分開，不把它們當 GET 差異。[db-before-02.txt](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/db-before-02.txt)／[db-after-02.txt](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/db-after-02.txt) 相同，[db-final.txt](../../artifacts/r10-acceptance/r10-acceptance-20261011-02/db-final.txt) 舊列逐 ID/digest 與 Migration 全核對。

兩輪新 API PID 23612、36360 只在確認 CommandLine 指向本人該輪新 DLL 後停止；原 PID 36804／5000、DB container e8f01e2c1bee／volume 保留 healthy，5011 釋放。未持久更動配置／DB status／deadline／Windows 政策；沒有建立 SDK 容器、Cookie jar 或含秘密檔，HTTP runner finally 清除記憶體 credentials，成對文件 token/password 保持 placeholders。所有合法 fixtures、TRX／HTTP raw／SQL／完整 digest／腳本／首次失敗皆保留於 ignored artifacts，不將秘密或 binaries 納入 commit。

## 7. 限制、待確認與版本交付

本次未觀察到 R10 產品缺陷或規格衝突；首次 native 12c 為已證實的工具誤報，沒有修產品或改預期求 PASS。實際測試與 SQL 支持這些案例，不代表窮舉全部 PostgreSQL numeric 值域、效能或所有併發。人工全部未執行；R10 Owner acceptance 仍待確認。

R7／R9 Owner pending、RV-I-01／I-01／I-02 既有非阻擋待辦不結案；RV-I-01 Publish fresh GET 完整頂層 DTO 證據改善未補強。Google／R11／G1、前端／部署／production HTTPS 未開始。review Windows 阻擋限制保留；本輪成功不更改過去結果。

版本交付只更新本報告、成對文件結果／RunId／非秘密 UUIDs、總計畫 R10 狀態。commit SHA／最終 Git 狀態於交付訊息及提交紀錄回報。**停止於「R10 驗收結果待 Owner review」**。
