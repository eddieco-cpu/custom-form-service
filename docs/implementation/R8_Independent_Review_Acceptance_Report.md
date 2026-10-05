# R8 獨立 review 與本次 AI 驗收

2026-10-05；**驗收結果待 Owner review**。本次整體 **BLOCKED**：目前環境 readiness gate 失敗，沒有完成本次產品驗收。未標記 R8 accepted，未開始 R9／R10／G1；未修改業務程式、測試語意、正式規格或核准預期。

## Review 結論與版本

本次 Owner 指示明確核准以最新 repo 獨立 review，無阻塞時執行現有核准成對計畫。核准計畫為 `8680cccd17e98646a6f257757dfa65654c039153`；本次 HEAD **`0bb78428226104890b8c9ec73e7bb6ae2fbf68f1`**，RunId **`r8-acceptance-20261005-01`**。初始 working tree 乾淨。

已閱讀 AGENTS.md、development／acceptance skills、V1 Implementation Plan、R8 Implementation Plan、A／B 開發與 review 補件、兩份既有執行報告、成對計畫，以及 R8 正式規格。獨立核對範圍為 Submit、Answer validator、Receipt、共用 Form lock、Delete 局部交易、既有 Auth／Clock／JSON／數值／EF 依賴與 R8 測試工具。

**未發現具體 R8 產品缺陷或需要實質修改核准計畫的問題。** 此結論限於已讀程式及可核對證據，不代表本次環境上的完整驗收 PASS：

- Authentication 在 Path／Body 前；未知／唯讀欄位先拒絕，answers 必填／Array 延後至 Published／deadline／single 後。Question 歸屬独立 early return，Required／型別／範圍／Choice details 與 omission 對照 03c §4、04 §6／9。
- Submit 以 ReadCommitted＋active Form FOR UPDATE，鎖後重讀 Published 才取唯一 submissionTime；deadline 判定、寫入前重確認及 submittedAt 使用同值。single 在持鎖內查重，multiple 不增加唯一約束；Close／Delete 使用同鎖形成先後，對照 05 §8～10。
- Response＋Answers 同交易保存；從 DB AsNoTracking 讀回排序 Receipt，commit 後才回 201。Receipt 僅正式欄位，Optional 不補 null，Definition／timestamps 不因 Submit 更新。Response／Answer mapping 未增加 schema 或破壞 multiple，對照 02 §10～11、03c §3／4.7。
- Text 碼點／trim、NUL／surrogate 受控錯誤、精確 Number 比較、Rating Integer、UUID 歸屬與重複、JsonElement 生命週期，以及 Clock／部分 INSERT／precommit／cleanup 測試與證據界線均有對應。COMMIT 已送出但結果未知仍未驗證，不由 precommit rollback 推論其零新增。

`git diff d3f75b3 HEAD -- src tests` 無差異；54 個 Method／URL／headers／原始 Body 與核准 `.http` 相同。結果欄與 RunId 更新不改案例語意。兩份現行計畫適用此 source/tests 版本，**環境 readiness 是本次唯一已確認的驗收阻塞**。

## 既有證據獨立核對

未直接沿用舊報告結論。實際讀取 `r8-acceptance-20261004-02` 的 manifest、逐案 JSON、TRX、案前後完整 snapshots、Receipt、deadline 復原與四組 waiter chain：manifest 全部檔案存在且 SHA256 相符；58 案 PASS，TRX 實際 280／280 Passed、0 failed／notExecuted，含 12 waiter rows、4 precommit fault rows。四組 native race 均有兩個不同 PID、Form FOR UPDATE、blocking chain 指向自己的 barrier，共用 10 秒內確認、finally 清理完成。錯誤案例完整 snapshots 無變更；舊列／deadline 復原比對成立。

此為 **2026-10-04 同 source/tests 的歷史 PASS 證據**，本次沒有重跑 full／Clock／fault／native races，不能代填本次 PASS。首次 `r8-acceptance-20261004-01` 的 FAIL／BLOCKED、後續修復紀錄及第二次 fixtures 全部保留。兩份計畫的本次改寫前完整 snapshot 另存本次 evidence。

## 本次實跑結果

| 項目 | 本次觀察 |
| --- | --- |
| Restore | sandbox 首次 exit 1、無診斷；保留空 log 與 exit metadata。其後 require_escalated restore exit 0，套件最新 |
| Build | exit 0，0 warnings／0 errors，27.68 秒 |
| Windows native API | 本次 build 後啟動 Development／localhost:5000；launcher PID 33972，API PID 38356 |
| 01a health | **PASS**：首次 200，`{"status":"healthy"}` |
| 01b readiness | **FAIL**：首次 503，`Unhealthy`；臺北 17:53:58～17:54:03（UTC 09:53:58～09:54:03），5.391 秒 |
| HTTP 54 requests | **1 PASS／1 FAIL／52 BLOCKED**；實際只送 01a／01b，03a～13h 未送 |
| 非 HTTP 01／02／13／14 | **01 FAIL；02／13／14 BLOCKED**。本次 full／DB 寫入／四組 races 未執行 |
| 人工 | 全部未執行；未由 AI 或歷史 PASS 代填 |

本次 DLL SHA256：`038A4D7F471A4119B5EB35D1C344B9F8EB81AE71592113FD4A4EB5A4F6029C9F`。Windows SDK 10.0.400。原生 process command line 明確指向此 repo DLL；沒有沿用舊 API 或 TestServer 代填 startup。

Docker Compose 的 PostgreSQL 17.11 容器內部 healthy，但實際 `NetworkSettings.Ports` 為 **`{"5432/tcp":[]}`**，未實際發布 localhost:5432；TCP 連線回 ConnectionRefusedError。本次 API log 同時記 postgresql health check Unhealthy。**案例 01b 預期 200 Healthy，實際 503 Unhealthy；影響 02 及其後全部依賴驗收。** 這是環境連線阻塞，沒有從此失敗推論 R8 Submit 產品缺陷。未修改產品、timeout、Compose、持久網路設定或安全政策來取得 PASS。

## 命令、資料保留與證據

實際命令（PowerShell repo root；stdout／stderr／exit metadata 存 evidence）：

```powershell
git status --short
git rev-parse HEAD
git diff d3f75b3 HEAD -- src tests
docker compose config --quiet
docker compose ps
dotnet --version
dotnet restore CustomFormService.sln --disable-parallel --verbosity minimal
dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --launch-profile http --no-build
docker inspect custom-form-service-postgres-1 --format '{{json .NetworkSettings.Ports}}'
```

API 用 `Start-Process -WindowStyle Hidden` 啟動，啟動前確認 5000 無 listener。Python standard library `http.client` 各送一次核准 `.http` 01a／01b；未傳 Cookie。`audit.py`／`verify-history.py` 核對歷史證據；`gate.py` 記本次 health、逐案結果及唯讀資料比較。DB 使用原容器的 `psql -X -q -A -t -v ON_ERROR_STOP=1`，SQL stdin、不輸出 credentials。

本次唯讀全表非秘密 snapshot 與前次最終 snapshot **完整相同**：4 Users／14 Forms／32 Questions／15 Options／9 Responses／10 Answers。未新建 fixture，未登入、未操作 deadline／barrier／worker，未產生可用 JWT／Cookie jar／秘密 request 暫存；既有五張 R8 fixtures、歷史 Responses／Answers、軟刪除資料與原 volume 均保留。此唯讀核對不是完整 14 PASS。API／DB 留存，未改設定。

證據根目錄：[r8-acceptance-20261005-01](../../artifacts/r8-acceptance/r8-acceptance-20261005-01/)。主要索引：

- [historical-audit.json](../../artifacts/r8-acceptance/r8-acceptance-20261005-01/historical-audit.json)、[獨立案例核對](../../artifacts/r8-acceptance/r8-acceptance-20261005-01/independent-evidence-check.json)
- [build log](../../artifacts/r8-acceptance/r8-acceptance-20261005-01/windows-build-first.log)、[DLL 身分](../../artifacts/r8-acceptance/r8-acceptance-20261005-01/api-build-identity.json)、[native process](../../artifacts/r8-acceptance/r8-acceptance-20261005-01/windows-api-process.json)
- [01a](../../artifacts/r8-acceptance/r8-acceptance-20261005-01/01a.json)、[01b 首次 503](../../artifacts/r8-acceptance/r8-acceptance-20261005-01/01b.json)、[58 案結果](../../artifacts/r8-acceptance/r8-acceptance-20261005-01/results.json)、API stdout／stderr 同目錄
- [實際 bindings](../../artifacts/r8-acceptance/r8-acceptance-20261005-01/docker-actual-bindings.json)、[資料保留核對](../../artifacts/r8-acceptance/r8-acceptance-20261005-01/retention-readonly.json)、baseline-current.json 同目錄
- [成對 Markdown](../test-plan/R8_Acceptance_Test.md)／[.http](../test-plan/R8_Manual_Tests.http)：58 個 AI 欄同步本次結果，人工欄保持未執行；舊結果保留於歷史段及完整 evidence snapshots。
- [文件／秘密核對](../../artifacts/r8-acceptance/r8-acceptance-20261005-01/documents-secret-verification.json)、[證據 SHA256 index](../../artifacts/r8-acceptance/r8-acceptance-20261005-01/evidence-index.json)：54 原始請求、預期及人工欄未變，58 個成對結果一致，known-secret／JWT／private-key 掃描 0 hits／0 read errors；`git diff --check` 通過。

## 剩餘待辦與停止點

需要恢復原 localhost:5432 的 PostgreSQL host binding 並再次證明本次 build readiness 200 Healthy。環境可用後，以新 RunId／fresh IDs 依同一核准計畫補驗；不能重放既有 single／已刪除 fixtures 或覆寫本次首次失敗。若後續產品或計畫語意改變，再依 Owner 指示與既定階段 gate 處理。

本次只提交成對結果、本報告與兩份 Implementation Plan 的狀態同步；ignored 原始證據留本機。Commit SHA／最終 Git 狀態見交付訊息。本次沒有產品修正或計畫修改要求。停止於 **「驗收結果待 Owner review」**（驗收結果待 Owner 確認）；環境阻塞尚未解決，R8 未 accepted，R9 未開始。
