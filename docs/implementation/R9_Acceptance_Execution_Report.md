# R9 — AI 驗收執行報告

2026-10-05；**R9 驗收結果待 Owner review**。AI 補驗 `r9-acceptance-20261005-02`：Windows 原生 API、完整回歸 306/306（R9 26/26）、40 HTTP／4 組非 HTTP 全部 PASS。人工全部未執行，未自行 accepted，未開始 R10。首次工具失敗及合法資料完整保留。

## 核准與執行版本

Owner 核准計畫 `17b1c2f`，指定 exit code／完整建置輸入／missingFormId 碰撞三項補強後直接 Execute；成對修訂 `f8a8026`。Owner 隨後明確指示「動態的註冊 & 登入兩個新會員」，成對修訂 `f93d7fc`：03a／03b 註冊及登入 A，03c／03d 註冊及登入 B，HTTP 38→40，R9 業務請求／預期不變。首次工具缺陷修正後以新 RunId 補驗，保留首輪歷史於 `2644eb3`。

執行 HEAD `2644eb33168357eafd36ba6404ad77c0d47016dd`；`git diff 32bb41c -- src tests docs/specs` 為空。未修改業務程式／schema／正式規格。依 [AGENTS.md](../../AGENTS.md)、[acceptance skill](../../.agents/skills/v1-acceptance-testing/SKILL.md)、[核准實作計畫](R9_Implementation_Plan.md) 與正式規格執行；[Markdown](../test-plan/R9_Acceptance_Test.md)／[.http](../test-plan/R9_Manual_Tests.http)已回填逐案 UTC／平台／實際結果／證據路徑，人工與 AI 分開。

## 實際執行結果

| 工作 | 首次 -01（歷史保留） | 補驗 -02（本次結果） |
| --- | --- | --- |
| Windows restore／build | exit 0／0，build 20.29 秒，0 warnings／errors | 沿用同次已核對 build，source／DLL SHA256 不變；未宣稱另做一次 restore/build |
| Native API／01a／01b | 本 repo DLL，Development，API PID 34980／launcher 8848；health 200 `{"status":"healthy"}`，readiness 200 `Healthy` | 實際 PID／hash再核對；重新依序 health/readiness PASS |
| Windows full／02 | 首次 306/306，26.8885 秒，但開始早於 health；保留並依序再跑 306/306，25.2318 秒 | health 後本次新 full 306/306，28.0511 秒；R9 26/26、九個方法與四組 SELECT StdOut，0 failed／notExecuted |
| HTTP | 6 PASS／04a FAIL／33 BLOCKED；沒有送出後續業務 GET | 40/40 PASS，逐案對應 companion；0 FAIL／BLOCKED |
| 非 HTTP | 01／02 PASS；10 未進入；13 首次 FAIL 是工具精度誤報，另留精確復核 | 01／02／10／13 4/4 PASS；10z finally PASS，未算成額外 HTTP 或第五組工作 |
| Linux | 未執行 | 未執行，原生 Windows 無需 fallback，不以 Linux PASS 代替原生驗收 |
| 人工 | 全部未執行 | 全部未執行 |

同一次授權執行的 [Windows restore log](../../artifacts/r9-acceptance/r9-acceptance-20261005-01/windows-restore-first.log)／[build log](../../artifacts/r9-acceptance/r9-acceptance-20261005-01/windows-build-first.log)與各自 `.exit.txt` 保留；[啟動 stdout](../../artifacts/r9-acceptance/r9-acceptance-20261005-01/api-stdout-first.log)／[程序身分](../../artifacts/r9-acceptance/r9-acceptance-20261005-01/api-process-identity.json)／[相同 build 證明](../../artifacts/r9-acceptance/r9-acceptance-20261005-02/same-build-reference.json)。DLL 來源 `src/CustomFormService.Api/bin/Debug/net10.0/CustomFormService.Api.dll`，Content root 指向本 repo API，5000 listener 是其子程序；沒有沿用其他服務的 health。R9 開發時 `0x800711C7` 仍保留歷史，本次成功不代表已查明先前政策失敗根因；本次未改 application-control policy。

[補驗 full TRX](../../artifacts/r9-acceptance/r9-acceptance-20261005-02/r9-acceptance-full-first.trx)／[full log](../../artifacts/r9-acceptance/r9-acceptance-20261005-02/windows-full-first.log)／[回歸核對與 SELECT StdOut](../../artifacts/r9-acceptance/r9-acceptance-20261005-02/regression-summary.json)／[逐案結果](../../artifacts/r9-acceptance/r9-acceptance-20261005-02/results.json)／[HTTP log](../../artifacts/r9-acceptance/r9-acceptance-20261005-02/http-first.log)。本機 `artifacts` 被 Git 忽略，未提交秘密或大量 runtime evidence；成對文件與本報告保留可查位置。

## 覆蓋與證據

- 08a～d：反序提交 Text／Number，Receipt 及 Latest 按 Question sortOrder 排序；精確長小數與 DB JSONB 相等，Text 正規化為 `Old 😀`。較新回覆只送 `1e0` Number，Latest 回新 Response 的 Number=1，沒有補回舊 Text／null；驗證 Response 的 User／Form／submittedAt 及全部保存 Answers。
- 09a～c：Owner 新回覆 `answers=[]`，Latest 只回 Owner 整份零答案回覆；兩份 B＋一份 A 歷史，Count=2，非 Response 數 3。Count 非 Owner 403，GET 無寫入。
- 05／07／10／11／12：Draft Owner／非 Owner Latest 錯誤不同、Owner Count=0／非 Owner 403；Published／Closed 空資料及已有回覆正確。截止後仍 Published，Latest／Count 可讀；Closed 不受 anonymous／owner_only 存取政策影響。Soft Delete 204 保留全部子列與歷史，後續兩 GET 均 FORM_NOT_FOUND，存在性先於 Count Owner 判斷。
- 06：缺 Cookie＋壞 UUID 為 AUTH_REQUIRED；無效 Cookie＋壞 UUID 為 AUTH_TOKEN_INVALID；Active User 壞 UUID 為 VALIDATION_ERROR，`error.details` 定位 formId／INVALID_VALUE；非 Owner 查不存在 Form 為 FORM_NOT_FOUND。HTTP 與正式 Error Envelope 的 `error.code` 同時核對。
- 02：成對計畫列明九個方法／26 DataRows，完整回歸重新證明全五題型、single／multiple、同 submittedAt 的 UUID DESC、時間優先於 UUID、跨 User／Form、disabled 歷史去重、Identity×Visibility、完整 Authentication Stage。四組 TestServer＋真實 PostgreSQL 競爭在 ReaderExecutedAsync 已完成 Form SELECT 且確認 RepeatableRead 後才暫停，另一 Submit／Delete 已 commit；釋放後舊 GET 穩定、下一個 GET 見新值／404。不是以 BeginTransaction 當快照，也未宣稱是 native HTTP race。

每個普通 GET／錯誤查詢保存 before／after 完整快照，全部相同；Submit 僅新增合法 Response／Answers，Definition 不變；Close／Delete 只變契約允許的 Form 欄位。各證據均以同號 JSON／snapshot 命名，登入與註冊請求密碼、Set-Cookie 不保存。03b/d 登入即時快照已在 harness 記憶體比對；保存檔由相鄰註冊 after／下一步 before 的同一未變狀態派生，來源與無中間寫入依 [login-snapshot-provenance.json](../../artifacts/r9-acceptance/r9-acceptance-20261005-02/login-snapshot-provenance.json) 明列，不冒充另一次 DB 查詢。

## 首次失敗與補驗方式

1. -01 環境 harness 用預設 cp950 讀 UTF-8 文件而 exit 1，在 DB connection 成功後停止，尚未 build／啟動／異動 DB。改為 UTF-8 接續，保留 [首次環境錯誤](../../artifacts/r9-acceptance/r9-acceptance-20261005-01/prepare-first-error.log)，不覆寫已完成的階段 logs。
2. -01 首次 full 開始早於 01a/b；保留原 TRX，再於 health/readiness 後執行新 full 才放行 HTTP。兩次均 306/306；不把提前的結果當成符合計畫順序的 02。
3. -01 04a 的 HTTP 201 成功，隨後 harness 誤查 `is_required` 觸發 KeyError（正式欄位 `required`），記 04a FAIL、其後 33 BLOCKED。未重放 POST；保留新 A/B、main Draft／兩 Questions。13 初次使用已損失精度的轉存 JSON，誤報舊 Answers 變更；原始 `baseline-before.log` 與精確 Decimal 比對確認舊六表全列相同。首輪的 lossy `baseline-before.json` 不能當精確數值基線，保留作歷史並改用原始 log。

[首次失敗](../../artifacts/r9-acceptance/r9-acceptance-20261005-01/first-http-failure.json)／[首次逐案結果](../../artifacts/r9-acceptance/r9-acceptance-20261005-01/results.json)／[精確保留復核](../../artifacts/r9-acceptance/r9-acceptance-20261005-01/retention-correction.json)均保留。這些是臨時驗收工具缺陷，不是產品缺陷；修正僅限工具欄位及數值解析，未改預期求 PASS。-02 使用全新 RunId／A/B／Forms，先將 -01 合法資料納入新 baseline，再重新 health／full／全部 HTTP。沒有發現產品缺陷；細粒度 native race、Linux 與人工未執行，依核准範圍明確保留此限制。

## 建置輸入、exit code 與 missing UUID

Windows native stage 存 log／`$LASTEXITCODE`，restore/build 非零即停止依賴操作；API 啟動使用隱藏 Start-Process 並核對實際 listener／PID／DLL，而非只看 launcher 成功。Linux 腳本同步 `set -Eeuo pipefail`、tar 兩側 PIPESTATUS、copy SHA256／restore／build／full 各自 exit，外層 docker exec 同樣檢查；本次 fallback 未需要執行，未宣稱 Linux copy／restore／build／full PASS。

核對根目錄與上層自動匯入檔、兩 csproj Import／ProjectReference：沒有 Directory.Build.*／Directory.Packages.props／NuGet.Config、額外 Import；ProjectReference 位於 src/tests。存在時帶入並記錄的規則與建置資產相對路徑已同步兩檔；[輸入清單與 SHA256](../../artifacts/r9-acceptance/r9-acceptance-20261005-02/build-inputs.json)記錄 src/tests 全量非 bin/obj 檔案盤點，含既有忽略輸出，未作為已執行的 Linux copy 證據。不得使用舊二進位取代本次 build。

兩輪均以只讀 SQL 查證 `00000000-0000-4000-8000-000000000009` 未存在；[補驗 missing check](../../artifacts/r9-acceptance/r9-acceptance-20261005-02/missing-uuid-check.log)為 0，故無實際碰撞／UUID 更換。碰撞時生成新 UUID、再查不存在並同步兩檔；不能只換 RunId 的規則已修訂。

## 保留資料與清理

| 資料 | Users | Forms | Questions | Options | Responses | Answers |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 首輪前原有 | 4 | 19 | 42 | 18 | 17 | 20 |
| -01 新合法部分成果 | +2 | +1 | +2 | +0 | +0 | +0 |
| -02 新合法成果 | +2 | +2 | +3 | +0 | +3 | +3 |
| 最終保留 | 8 | 22 | 47 | 18 | 20 | 23 |

原有全部 ID 的六表完整列與原始 baseline 相同；-01 所有部分合法成果也與 -02 baseline 相同。-02 main Closed＋Soft Delete，B 兩份歷史與 A 零 Answer 保留；empty Closed、未刪除、零 Response。[fixture IDs](../../artifacts/r9-acceptance/r9-acceptance-20261005-02/fixture-ids.json)／[清理摘要](../../artifacts/r9-acceptance/r9-acceptance-20261005-02/retention-cleanup-summary.json)／[兩輪完整保留核對](../../artifacts/r9-acceptance/r9-acceptance-20261005-02/combined-retention.json)。

Deadline 只暫改本次 A＋RunId＋main id 的一列，原值 `2036-10-05T00:00:00.123456Z`；[probe-before](../../artifacts/r9-acceptance/r9-acceptance-20261005-02/10-probe-before.json)與 [finally restored](../../artifacts/r9-acceptance/r9-acceptance-20261005-02/10z-restored.json)全列精確相等。regular PostgreSQL／volume 未重建、刪除或 force recreate；沒有 SDK 容器需要回收；測試 workers 已結束。Native API PID 34980 保留可用；沒有修改持久設定，password／Cookie 僅記憶體，harness 已退出、秘密暫存檔 0，計畫變數維持 placeholders。交付前系統程序只讀盤點首次受 sandbox 拒絕存取，未把 shell exit 0 當成功；改用 ErrorActionPreference=Stop／授權存取後確認 [API listener／workers](../../artifacts/r9-acceptance/r9-acceptance-20261005-02/process-cleanup-final.json)；[首次錯誤](../../artifacts/r9-acceptance/r9-acceptance-20261005-02/process-cleanup-first-error.log)保留。

## 實際命令與提交邊界

Windows／native commands（各階段 exit 保存於本機 evidence）：

```powershell
git status --short
git rev-parse HEAD
git diff 32bb41c -- src tests docs/specs
dotnet --info
docker compose config --quiet
docker compose ps
docker inspect custom-form-service-postgres-1 --format '{{json .NetworkSettings.Ports}}'
docker compose exec -T postgres psql -U $DbUser -d $DbName -X -q -A -t -v ON_ERROR_STOP=1
dotnet restore CustomFormService.sln --disable-parallel --verbosity minimal
dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1 -p:UseSharedCompilation=false
Start-Process dotnet -ArgumentList @('run','--project','src/CustomFormService.Api/CustomFormService.Api.csproj','--launch-profile','http','--no-build','--no-restore') -WindowStyle Hidden
dotnet test CustomFormService.sln --no-build --no-restore --configuration Debug -m:1 --logger 'console;verbosity=normal' --logger 'trx;LogFileName=r9-acceptance-full-first.trx' --results-directory $EvidenceRoot
# -01 第二次 full 使用 r9-acceptance-full-after-health.trx；-02 使用新的 EvidenceRoot。
```

Python standard-library harness 執行 `prepare-execution.py`／UTF-8 `prepare-continuation.py`、`windows-execute.ps1`、`health-gate.py`、`verify-regression.py`、`execute-http.py`；-02 新 evidence 的相同角色腳本分開保存，40 HTTP 與 companion 同號。DB SQL 由 stdin 傳入，所有 subprocess 檢查 returncode；HTTP raw Number 不經 double 重新序列化，snapshot 以 Decimal 精確解析。Linux image/network inspect 只讀完成，docker run／Linux tests 未執行。

交付變更只有成對驗收結果、本報告、兩份 Implementation Plan 的進度紀錄；單一目的結果提交 SHA 隨最終交付回報。實跑 `python artifacts/r9-acceptance-plan/audit-final.py`（bundled Python）與 `git diff --check`，文件靜態核對 PASS：40 編號／順序／預期／變數／JSON／Cookie／各案證據、health→full→業務 HTTP 時序、26 案例、probe 精確復原、DLL hash、placeholders／秘密掃描；[核對摘要](../../artifacts/r9-acceptance/r9-acceptance-20261005-02/audit-final.json)。此核對不取代正式結果。沒有未解產品缺陷；人工未執行與首次工具失敗保留，是否 accepted 由 Owner 決定。**R9 驗收結果待 Owner review（驗收結果待 Owner 確認），不進 R10。**
