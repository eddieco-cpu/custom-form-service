# R6 正式測試前有限收尾 — 開發交付

日期：2026-10-03。狀態：**development verified／開發完成、待驗收**，不是正式驗收 PASS 或 accepted。

## 範圍與結論

依 Owner 本次授權、AGENTS.md 與 v1-round-development skill，只處理截止時間 no-op、Implementation Plan §3／6／7 階段對齊，以及既有數值限制的具名常數整理。保留 Candidate／ChangeSet、權限／Draft／Form lock／交易／保存與回應重讀流程；不改正式規格、DB schema／Migration、日期解析格式或輸入小數位數限制。未準備或執行正式驗收，未啟動 R7／R8／R10／G1。

契約依據：01 FORM-05、03a §9、03b §7.2 步驟 13、05 §10／§15 的「無實際變更不更新 updatedAt」；數值交接依 01 §3.3、05 §11.4。

### 截止時間疑點成立

先加測試、以原程式跑真實 DB：12 案 **2 PASS／10 FAIL**，0 skipped。8 案在重送相同原始 Request 時失敗；另 2 案在輸入只差 100ns、實際保存值相同時失敗。例如 `2026-12-31T00:00:00.1234567Z` 保存為 `.123456Z`，重送原字串仍回 200，但 SQL 發出 UPDATE，DB 截止時間不變而 Form updatedAt 改變。不是僅依靜態閱讀判斷。

`DraftDefinitionChangeSet.cs` 現將 deadline 依現用 Npgsql 10.0.3 的實際時間編碼比較：微秒、相對 2000-01-01 截斷至零；保留 MinValue／MaxValue 的既有 infinity 轉換。不能直接把 .NET ticks 向下取整，否則 2000 年前的保存語意會不一致。此公式僅用於差異判斷，未改 Candidate 的輸入或既有寫入方式。

來源：[Npgsql 10.0.3 PgTimestamp](https://github.com/npgsql/npgsql/blob/v10.0.3/src/Npgsql/Internal/Converters/Temporal/PgTimestamp.cs)。新增測試的預期值由 `SELECT @deadline::timestamptz` 經真實 driver／DB round-trip 取得，不複製應用程式公式作為 oracle。

新增 `FormEndpointsTests.DeadlinePrecision.cs`，12 個資料案例涵蓋整秒、微秒、7／8 位小數、原已接受的空格日期格式、2000 年前後、DateTime 最小／最大值及鄰近有限值。每案確認首次實際變更成功；原值／GET 值／保存等值的次微秒輸入重送時不發 UPDATE，所有 DB 列、ID、createdAt、updatedAt 完全不變；PATCH、GET JSON 一致。適用的有限時間另驗證真正差一微秒仍更新；清除 deadline 後重送 null 仍為 no-op。

可觀察的刻意修正：保存等值的 deadline 不再改動 updatedAt。實際保存時間、接受格式及真正變更的更新行為不變。

### 流程與小整理

- Implementation Plan §3／6／7 明列開發交付、驗收文件準備、AI 驗收執行、人工驗收與 Owner 確認四個階段。開發交付後停止；文件需另行授權，交付成對檔案後待審核；執行需目前核准計畫及明確執行授權；只有 Owner 根據分開記錄的 AI／人工結果及同意的待處理事項確認後才 accepted。多階段授權也不省略計畫核准 gate。
- 前次 Candidate 報告的「流程文字差異」是當時歷史紀錄；本次依 Owner 明確授權解決，不回寫或掩蓋前次結果。
- DefinitionNumber 的 `131072`、`16383`、`int.MaxValue / 2` 改為具名常數，沒有更動數值、判定次序或可接受範圍；註解補 PostgreSQL 17（實測 17.11）版本與 [numeric 限制](https://www.postgresql.org/docs/17/datatype-numeric.html)、[JSONB 數值](https://www.postgresql.org/docs/17/datatype-json.html)、[numeric.c 解析器](https://github.com/postgres/postgres/blob/REL_17_STABLE/src/backend/utils/adt/numeric.c) 來源。
- 後續 Answer／Statistics 必須與現有 Validation／JSONB 維持精確數值等值、範圍判定與 numeric 排序；`1`／`1.0` 同值，不得轉成 decimal／double 暗中捨入或截斷。只登記交接要求，不建立框架或實作 R8／R10。

## 實際開發驗證

環境：沿用 Owner 已授權的官方 Docker SDK `mcr.microsoft.com/dotnet/sdk:10.0.400`，本次暫存容器 `r6-closeout-sdk`、工作副本 `/work/closeout`；PostgreSQL 17.11，Npgsql 10.0.3。唯讀掛載工作區／NuGet／既有 User Secrets，未印出或提交憑證；未調降 Windows 安全設定。每項命令由 `docker exec r6-closeout-sdk bash -lc 'cd /work/closeout && …'` 執行。

| 實跑命令／階段 | 結果 |
| --- | --- |
| `dotnet restore CustomFormService.sln`、`dotnet --version` | restore 成功；SDK 10.0.400 |
| 原程式 `dotnet test tests/CustomFormService.IntegrationTests/CustomFormService.IntegrationTests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~Patch_deadline_uses_actual_storage_precision_for_no_op --logger "console;verbosity=minimal" --logger "trx;LogFileName=deadline-baseline.trx"` | 12 案，2 PASS／10 FAIL，0 skipped；已重現假變更 |
| 局部修正後相同精度命令，TRX 改為 `deadline-fixed.trx` | **12/12 PASS**，0 FAIL／0 skipped |
| `dotnet build CustomFormService.sln --no-restore -m:1 -p:UseSharedCompilation=false` | **成功，0 警告／0 錯誤**，4.18 秒 |
| `dotnet test tests/CustomFormService.IntegrationTests/CustomFormService.IntegrationTests.csproj --no-build --no-restore -m:1 --filter FullyQualifiedName~FormEndpointsTests --logger "console;verbosity=minimal" --logger "trx;LogFileName=closeout-form.trx"` | **73/73 PASS**，0 FAIL／0 skipped；含原有 61 案與新增 12 案 |
| `docker exec custom-form-service-postgres-1 postgres --version` | PostgreSQL 17.11 |
| `git diff --check`、staged diff 與範圍檢查 | 通過；僅正常 LF→CRLF 提醒，無 whitespace error、其他 Round、Secret 或 schema 變更 |

三份 TRX 已保留至 `artifacts/r6-closeout-evidence/`（gitignored），以 `rg 'Counters' artifacts/r6-closeout-evidence -g '*.trx'` 讀回確認實際 12／12／73 案均執行。最終 Form suite 包含 Number 邊界、Create／讀寫、並行 PATCH、Option value replacement／混合操作與回滾等既有回歸。

## 提交、未確認與停止點

起始 HEAD：`da83b86473af959eba2ce5a428af5b7288e60515`，Git 起始乾淨。本次程式依單一目的分開提交：

1. `5f8872bcb5f9b457ff0d0e3e278ad4548dd172aa` — `fix(forms): compare deadline no-op at storage precision`，局部修正及 12 案回歸。
2. `ab01dd295d2488164b46cf28e53a7fdf67039ee4` — `refactor(forms): name existing PostgreSQL numeric limits`，不改行為的具名常數整理。
3. `docs: align R6 delivery gates and record closeout` — 本報告及 Implementation Plan；完整 SHA／最終 Git 狀態於交付訊息提供。

未確認／延後：本次依 Owner 範圍只跑必要 build 與受影響 Form suite，**未重跑全套回歸**；前次 106/106 僅是歷史證據。Windows 原生新版測試仍未有成功執行證據。正式 R6 驗收未準備／未執行，無可宣稱已通過的 R6 正式驗收結果。UUID 格式、lock 取得時機等延後項維持原決定；Npgsql 版本或 infinity 設定將來若變更，須重驗精度測試。

測試只建立／清理自身合成資料，不刪除開發 DB volume、既有 fixture 或 User Secrets。證據保留後清理本次 SDK 暫存容器，保留官方映像與既有 PostgreSQL 資源。

已依開發 skill 停在「開發完成、待驗收」。下一步如需驗收文件準備，等待 Owner 另行要求；本次不建立成對驗收文件、不進行驗收或 R7。
