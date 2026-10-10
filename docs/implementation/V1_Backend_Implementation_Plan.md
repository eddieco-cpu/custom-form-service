# 自建表單設計服務 — V1 Backend Implementation Plan

| 項目 | 定義 |
| --- | --- |
| 版本 | 1.0，2026-09-28 |
| 性質 | 實作順序、驗收與 Codex 執行協議；不修改正式業務規格 |
| 適用 | 本機 ASP.NET Core Web API + EF Core + PostgreSQL；逐 Round 開發與審查 |
| 開始方式 | 先交付本文件及正式規格，僅指示執行 R0 |

## 1. 本計畫與正式規格的關係

規格全集依序閱讀：`00-v1-overview.md` → `01-v1-domain-specification.md` → `02-v1-database-specification.md` → `03a-v1-api-contract-common.md` → `03b-v1-api-contract-auth-user-form.md` → `03c-v1-api-contract-submission-statistics.md` → `04-v1-error-catalog.md` → `05-v1-system-constraints.md`。以這八份**最新正式版本**為準；不要用舊版單篇 `03-v1-api-contract.md` 或先前的 Step 草稿取代 03a／03b／03c。

權責：01 管業務行為；02 管資料庫；03a～03c 管端點／傳輸／驗證次序；04 管錯誤；05 管跨層限制；00 為導覽。本文件只管理開發階段、工作交接、驗證及停止點。若專責文件互相矛盾，記錄文件、章節、實際衝突及受影響工作，**停止受影響的實作**；不要偷偷選較方便的版本。與衝突無關且已獲授權的工作可繼續。

本計畫不是新增 API Contract。規格更新由專案負責人決定，更新正式文件後才調整程式與測試。Codex 可以提出 implementation decision，但不能自行擴充產品能力。

## 2. 交付邊界與里程碑

### A 階段：本機 Email／Password Backend

- ASP.NET Core Web API、EF Core、PostgreSQL；本機 PostgreSQL 使用 Docker Compose 與 named volume。
- 建立正式規格的**全部 7 張 Domain Table**，包括暫時未使用的 `user_identities`；不可把已定稿 Schema 縮成 Email-only 模型。
- 完成 Email／Password、User、Form、Submission、Latest、Count、Statistics 等適用端點與跨層保證。
- 本階段可標為「本機 Email／Password Backend 完成」；**不可標為 V1 全部完成**。

### B 階段：完整 V1 待補的外部整合

- Google ID Token 登入與 `POST /api/v1/auth/google` 是正式 V1 功能。A 階段暫緩 Google Project／Credential／真實 Provider 整合，B 階段另開 Round 完成與驗證，不能視為永久 non-scope，也不以假成功或 501 端點充數。
- Nuxt 畫面、Railway、正式 Domain、部署拓撲與正式 HTTPS 實測，交由後續前端／部署計畫。即便後端未部署，正式環境 Cookie、CORS、Secrets 行為仍需在設計與可重現的設定測試中驗證；實際線上驗收須在部署時補做。
- Email verification、忘記密碼、Refresh Token、RBAC、未登入填答等是正式 V1 的**範圍外**，無須在 B 階段補做。

### 完成狀態用語

| 標記 | 意義 |
| --- | --- |
| Round accepted | 本 Round 的驗收、審查與 commit 已完成 |
| A-stage accepted | R0～R11 已通過，本機 Email／Password 範圍完整 |
| Backend V1 accepted | A 階段及 Google Auth 後續 Round 完成；正式文件的後端端點與驗收項均獲證明 |
| Product V1 released | 前端、實際環境與部署驗收另行完成；非本計畫的 A 階段承諾 |

## 3. 全程執行協議

1. **一次只執行明確指定的一個 Round。** 每次讀該 Round 相關正式規格、確認現有 repo 與 Git 狀態，列出本次 tasks／驗收方法；沒有歧義就執行，不把正常技術選擇一律變成等待批准的問題。
2. 確認本 Round 的範圍、工作階段與暫緩項。遇到新需求，列為後續候選，不順手實作；完成已授權階段後交付並停止，等待 Owner 明確指定下一階段或 Round。**已明確授權的同一 Round 修正應繼續完成；不自動推進驗收或下一 Round。**
3. 允許選擇 Controller／Service 劃分、DI、測試框架、EF Mapping、Lock／Transaction 方案及相容版本；記錄重要取捨。不得改變 7 張表、18 個端點的正式契約、權限、錯誤優先序、資料語意。
4. **開發交付**：依開發 skill 實作已授權 Round，照常執行必要 build／自動測試、檢查 diff、更新開發紀錄並提交已驗證變更；交付報告後停止於「開發完成、待驗收」。開發自測不需要驗收計畫核准，也不代替獨立 code review、AI 驗收或人工驗收。**驗收文件準備**只有在 Owner 另行要求時才使用 acceptance skill；不得因開發完成而自動開始。在此階段於 `docs/test-plan/` 同時準備 `Rn_Acceptance_Test.md` 與 `Rn_Manual_Tests.http`：Markdown 依執行順序包含目的、前置操作、請求、預期結果及「實際結果：待執行」；`.http` 的註解須標示對應測試編號、必要操作與預期結果。兩份文件的編號、順序、合成測資、請求與預期結果一致；DB 與非 HTTP 操作在 Markdown，並於 `.http` 指向該操作。交付兩份文件連結後停止待 Owner 審核，此階段不得執行驗收操作。
5. 測試計畫優先覆蓋本 Round 主要功能、權限、DB 寫入與代表性錯誤。可暫緩細部邊界時，簡短記錄暫緩項；不得將其擴充為大量矩陣，也不得放寬正式規格。
6. **AI 驗收執行**僅依目前 Owner 已核准的成對計畫及明確執行授權進行；核准計畫在 Owner 指示清楚包含執行意圖時才同時授權執行。Owner 可以明確授權多個階段，但執行前的計畫核准 gate 仍保留。不得把文件準備、開發完成、一般回覆或結果已讀當成核准。逐項記錄實際 PASS／FAIL／BLOCKED／未執行及證據，交付後停止待人工驗收與 Owner 確認。**人工驗收與完成**：AI 與人工結果分開；只有 Owner 根據已記錄結果及同意的待處理事項明確確認通過後，才標記 Round `accepted`。
7. 一般驗收沿用本機開發 DB，保留合成資料供後續 Round 使用，並恢復驗收期間暫時修改的狀態及設定。驗證從零建庫或其他破壞性重建時，才使用**專用、可丟棄的測試 volume／Compose project**；其 `down -v` 僅針對該測試資源。先檢查 `git status`；保護使用者的未提交修改、既有資料與 Secret，不做 force push、reset 或刪除真實開發 volume。
8. 認證／資料庫密碼、JWT signing secret、Google credential 不提交。提供 `.env.example` 等不含秘密的範例；README 寫如何自行設定。使用者提供的測試資料如含個資或憑證，先確認可否存檔／提交；預設僅存合成 fixtures。
9. 一個 Round 可有數個單一目的 commit，分別記錄開發、文件準備、實際執行或 Owner 確認；不要夾帶下一 Round、Secret 或使用者無關變更。驗證失敗時在已授權範圍修正／重跑，仍無法完成則回報實際未完成狀態；repo 未建、既有變更無法安全隔離或規格衝突時說明受影響工作與原因，**不可偽稱檢查通過或已 commit**。Review 修正須重跑受影響開發驗證並回報新提交；若使舊驗收計畫或結果失效，標示其已非現況證據，後續準備／執行仍須遵守階段與核准 gate，不自動重跑驗收。R0 若只輸出讀書報告，可以不 commit。
10. 每次依已授權階段交付：本 Round 的開發報告／或驗收結果表、主要變更及檔案、實跑命令與結果、失敗或未驗證事項、偏離規格的問題、commit SHA／Git 狀態、後續前置條件。只有驗收文件準備交付須同時列出 Markdown 與 `.http` 連結；開發交付不要求建立驗收文件。不得自動進入下一階段或 Round。

### 建議的 Round 交付模板

```text
Round: Rn／名稱
Status: in progress / development verified（開發完成、待驗收） / test plan pending owner review / acceptance executing / acceptance results pending owner confirmation / accepted / blocked
Scope completed: ...
Key files: ...
Development verification: build or automated test → observed result
Acceptance plan: 未獲授權準備／或成對文件路徑與連結、Owner 核准狀態
Acceptance results: 未執行／或已授權計畫的實際結果與證據（AI 與人工分開）
Spec mapping: relevant document sections / endpoints
Open issues or deviations: ...
Commits: <SHA and subject> or why not committed
Next round: prerequisite only; not started
```

## 4. Round 地圖

| Round | 主題 | 交付切面 | 狀態／驗收紀錄 |
| --- | --- | --- | --- |
| R0 | 規格與環境對齊 | 規格／依賴／歧義清單，不寫業務程式 | 歷史報告；未另記錄 accepted：[`R0_Alignment_Report.md`](R0_Alignment_Report.md) |
| R1 | .NET Repo 與 API 骨架 | API 可啟動，開發環境文件可用 | 驗收紀錄未存於 repo；不補造狀態 |
| R2 | Compose PostgreSQL 與連線 | DB 存活、資料持久、API 可連線 | 驗收紀錄未存於 repo；不補造狀態 |
| R3 | EF Core 與 7 表 Migration | 可從空庫重建正式 Schema | 驗收紀錄未存於 repo；不補造狀態 |
| R4 | Email Auth 與 User | 註冊、登入、登出、本人資料／更新 | **accepted（2026-10-01，人工確認完成）**：[`R4_Acceptance_Test.md`](../test-plan/R4_Acceptance_Test.md)；[`R4_Manual_Tests.http`](../test-plan/R4_Manual_Tests.http) |
| R5 | Form 基礎讀寫 | Create／List／Detail／Delete；安全邊界 | **accepted（2026-10-03，Owner 人工驗收確認通過；AI case 執行紀錄仍有缺口）**：[`R5_Acceptance_Test.md`](../test-plan/R5_Acceptance_Test.md)；[`R5_Manual_Tests.http`](../test-plan/R5_Manual_Tests.http) |
| R6 | Draft Definition 與 Delta | 完整 Question／Option／Config 編輯與原子性 | **accepted（2026-10-04，Owner review 提交證據後明確確認；人工未執行）**。AI：Windows 原生 API PASS、完整回歸 118/118、HTTP／DB 56/56 PASS；Linux 本次未執行（fallback 不需要）。計畫提交 `034a0be`、執行紀錄提交 `81b64f3`；保留資料與復原／秘密清理完成。成對紀錄：[`R6_Acceptance_Test.md`](../test-plan/R6_Acceptance_Test.md)、[`R6_Manual_Tests.http`](../test-plan/R6_Manual_Tests.http)。 |
| R7 | Lifecycle 與 Result Policy | Publish／Close／Visibility、Freeze 與並行 | **acceptance results pending owner confirmation（2026-10-04）**。Owner 核准 `28cb8e2` 並授權三項成對局部修訂後直接執行；修訂 `858d6ae`。RunId `r7-acceptance-20261004-01`：Windows 本次 restore/build／原生 API PASS、full 首次 167/167、M2 1/1、HTTP／DB 42/42 PASS；Linux 未執行，人工全部未執行。M2 未重現、根因未明；既有資料不變、新 fixtures 保留、probe／秘密清理完成，詳見 [執行報告](R7_Acceptance_Execution_Report.md) 與 [成對紀錄](../test-plan/R7_Acceptance_Test.md)。未 accepted，未開始 R8。 |
| R8 | Submission | 全題型 Answer／Response、single／multiple、並行 | **accepted（2026-10-05，Owner 本 chat 明確確認 R8 驗收通過）**。依據本次重驗 [R8_Acceptance_Rerun_20261005_Report.md](R8_Acceptance_Rerun_20261005_Report.md) 與既有成對驗收紀錄；人工未執行，不改填人工 PASS。**以下保留確認前紀錄：** **2026-10-05 本次重驗完成，驗收結果待 Owner review。** RunId `r8-acceptance-20261005-02`，測試 HEAD `d4d93e75ce35947015280f77794527ff8c9b5631`；核准計畫 `8680ccc`，source/tests/specs 與候選相同、請求／預期未改。Windows restore/build 0 warnings/errors，本次 native health/readiness PASS；全套 **280/280 PASS**、**54 HTTP／4 非 HTTP 全部 PASS**。四組 native race 各確認兩 waiter 後釋放 barrier；deadline／workers／barriers 清理完成，舊列完整不變、新五張 fixtures 與合法歷史保留。所有前次 FAIL／BLOCKED 與 PASS 保留歷史；人工未執行。詳見 [本次重驗報告](R8_Acceptance_Rerun_20261005_Report.md)。未 accepted、未進 R9。 **以下保留歷史：** **2026-10-05 獨立 review／本次 AI 驗收：BLOCKED，驗收結果待 Owner review。** HEAD `0bb7842`、核准 `8680ccc`，source/tests 與 `d3f75b3` 相同、54 請求未改；未發現具體 R8 產品／計畫缺陷。新 RunId `r8-acceptance-20261005-01`：build 0 warnings/errors、native 01a PASS；01b 首次 503 Unhealthy FAIL，PostgreSQL 實際 host binding 空。HTTP 1 PASS／1 FAIL／52 BLOCKED；非 HTTP 01 FAIL、02／13／14 BLOCKED，full／後續未執行。舊完整資料與前次最終 snapshot 相同，無新 fixture；所有舊失敗與補驗 PASS 保留歷史、人工未執行。詳見 [本次獨立報告](R8_Independent_Review_Acceptance_Report.md)。需恢復環境後新 RunId 補驗；未 accepted、未進 R9。 **以下為 2026-10-04 歷史：** **正式補驗完成，待 Owner review（2026-10-04）**。核准 `8680ccc`、Execute 授權持續有效；新 RunId `r8-acceptance-20261004-02`：Windows restore/build 0 warnings/errors、本次 native API health/readiness PASS、全套實際 280/280、54 HTTP／4 非 HTTP 全部 PASS。四組 native 兩 waiter／共用 10 秒／finally 與 Clock／EF／precommit faults 證據完整，旧列不變、新五 fixtures／9 Responses／10 Answers 保留。首次 FAIL／BLOCKED 與環境修復紀錄保留；人工未執行，Owner 指定本輪依 AI 證據審核、不要求人工作為 accepted 必要條件。详見 [正式補驗報告](R8_Acceptance_Reexecution_Report.md)及成對結果。未改產品／預期，未 accepted、未進 R9／R10／G1。 |
| R9 | Latest 與 Respondent Count | 最新整份回覆與有效人數 | **acceptance results pending owner confirmation（2026-10-05，R9 驗收結果待 Owner review）**。Owner 核准 `17b1c2f`，授權三項補強 `f8a8026` 後 Execute，並指定新會員註冊／登入修訂 `f93d7fc`；首輪工具失敗歷史 `2644eb3`。補驗 RunId `r9-acceptance-20261005-02`：Windows 同次 build 0 warnings/errors／本次原生 health/readiness PASS；新 full **306/306（R9 26/26）**、**40 HTTP／4 非 HTTP PASS**，四組實際 SELECT＋RepeatableRead 證據齊全。Linux 未執行，人工全部未執行。首輪 04a 工具欄位錯誤／13 精度誤報與全部 FAIL／BLOCKED 保留；舊資料及首輪合法 fixtures 完整不變、本次新會員／fixtures 保留，deadline finally 精確復原。詳見 [執行報告](R9_Acceptance_Execution_Report.md)與[成對結果](../test-plan/R9_Acceptance_Test.md)。未自行 accepted／未開始 R10。 |
| R10 | Statistics | Closed／Visibility／anonymous／identified／分布；Root 四欄位投影（03c §3.4） | **development verified／開發完成、待驗收（2026-10-10）**；Owner 核准 [精簡實作計畫](R10_Implementation_Plan.md) `06853f3` 並授權開發；產品／測試 `a4f9402`，Windows build 0 warnings/errors、focused 43/43、同版 full 420/420、短 native 21 HTTP 及隱私／null／精確數值檢查 PASS。詳見 [開發報告](R10_Development_Report.md)。待獨立 code review；未準備／執行正式驗收、未 accepted |
| R11 | A 階段整合與交付 | 契約回歸、從零建庫、本機驗收、缺口清單 | not started |
| G1（後續） | Google Auth 補齊 | 真實 Google 登入，含併發與全 V1 後端驗收 | not started |

Round 索引是交付順序，非強制類別／檔案結構。為支撐某端點而提前引入共用 Error、Clock 或 Auth 基礎設施是合理依賴，但不能提前完成下一 Round 的業務 API。

## 5. Round 詳細驗收

### R0 — Implementation Alignment

**輸入**：八份正式文件、現有 repo（如有）、本計畫。**工作**：逐份確認版本與路徑；畫出 7 表／18 端點與里程碑對照；盤點 SDK、Docker、Git、可用端口、現有程式、測試工具；提出相容版本、最小專案結構與測試策略；區分「規格衝突／缺失」、「可自行決定的實作細節」、「需要專案負責人提供的外部資訊（例如使用者測資或 Google 憑證）」。

**驗收／證據**：交出簡短 Alignment Report，列引用章節、疑問與建議、A／B 邊界、Round 依賴及阻塞項。特別核實 Google 是 V1 已定義功能、`user_identities` 仍在 R3 建立、只有 Question→Options Cascade、API Base Path `/api/v1`。不得寫正式業務功能；若新建 repo 尚無提交，可在 R1 初始化 Git。**停止**：交付報告，待審查後才做 R1。

### R1 — Repository／.NET API 起步

**輸入**：R0 結論；00、03a、05。**工作**：建立 solution、API project、Git ignore、開發設定範例及 README；選定並記錄相容的 .NET／套件版本；開發環境 OpenAPI 與可測的簡單 health endpoint（明確為工程端點）；統一開發端口及啟動命令。

**驗收／證據**：新環境照 README `dotnet restore`、`dotnet build`、`dotnet run`；實際 HTTP 呼叫 health 得 200，開發 OpenAPI 文件可取得。OpenAPI JSON 與 Swagger UI 是兩件事：若要求互動 UI，需明確安裝／設定並實際打開，不能因 `MapOpenApi()` 存在就報「Swagger UI 可用」。不依賴 DB。**提交**：如 `chore: initialize ASP.NET Core API`。**停止**：回報實測 URL／命令後停在 R1。

### R2 — Local PostgreSQL／Docker Compose

**輸入**：02、05 §2–3；R1 repo。**工作**：Compose 單一 PostgreSQL service、named volume、可設定的 host port、非秘密範例；配置 ASP.NET Connection String／DbContext 最小連通，加入 DB readiness 的實際查詢或明確的開發診斷。**一個 PostgreSQL container 可管理多張表**，不是每張表一個 container。

**驗收／證據**：`docker compose config`、`docker compose up -d`、容器 healthy；API 實際連線成功；提供 DBeaver Host、Port、Database、Username 的非秘密資訊與由使用者自行核對的步驟。用一筆可丟棄資料測試停止／重建 container 後仍存在；**不要對正式開發 volume 執行 `down -v`**。R2 不建立業務表。**提交**：如 `chore: add local PostgreSQL compose and connection`。**停止**：DB＋API 連通且持久性有證據。

### R3 — EF Core Domain Schema／Migration

**輸入**：02 全文、01 關聯、05 §3／7／8。**工作**：按正式 02 建立 `users`、`user_identities`、`forms`、`questions`、`options`、`responses`、`answers` 的實體、Mapping 與首版 Migration；PostgreSQL enum／JSONB／UUID／UTC、Nullable、PK／FK、Unique／Check／Index、Delete Behavior 逐項核對。`user_identities` 在此只建立資料結構；Google 業務流程留 G1。

**驗收／證據**：在專用空白測試 DB 執行 `dotnet ef database update`；查閱實際 schema 並與 02 表格逐項對照；從零重建重跑，檢查無未預期 schema diff；驗證只有 `options.question_id → questions.id` 為 Cascade，其餘 FK 為 NO ACTION。不得增設 submissions、statistics、roles、refresh tokens 等 V1 排除的 Domain Table；EF migration history 例外。業務初始化測資不混入 Schema Migration。**提交**：如 `feat(db): add V1 schema and initial migration`。**停止**：留下 schema 對照表與 Migration 命令。

### R4 — Email Auth／User

**輸入**：01 USR、03a 認證／驗證順序、03b §3～5、04、05 §4～6／11／12。**工作**：實作 1～3、5～6 號端點：Register、Password Login、Logout、GET／PATCH `/users/me`；密碼 hash、Email 規則、User 狀態、JWT `sub` 與 Cookie `access_token`，統一 Error Envelope。不要實作 Google 成功流程或假端點；註冊不自動登入，登入不在 body 回 JWT，保護端點從 Cookie 讀身分且查 DB 確認 Active。

**驗收／證據**：實打註冊→無登入 Cookie→登入→含 Cookie 取得／更新本人資料→登出→受保護請求被拒；涵蓋重複 email、錯誤密碼、缺失／過期／無效 Token、Disabled、Cookie flags、Logout 帶壞 Token；邊界文字／Password 不 trim 與可觀測錯誤順序依正式規格驗證。使用者提供的測資另行接收；在此以前用合成測資驗證，不把使用者真實密碼提交。**提交**：可拆 `feat(auth): implement password registration and login`、`feat(user): implement profile APIs`。**停止**：列出 Google Auth 暫緩項及不完整的 4 號端點。

### R5 — Form Create／List／Detail／Soft Delete

**輸入**：01 FORM／QST、02、03b §3／6／7.1／8.1、04、05。**工作**：完成 7、8、9、11 號端點；Create 的 nested questions／options 依規格原子建立（不能僅做空 Form），Draft 允許不完整，清單僅 Owner、固定排序／分頁，Detail 的 Owner／他人／Draft 存在性隱藏，Delete 做 Soft Delete。

**驗收／證據**：用 owner／非 owner 登入測 create、list、detail、delete；驗證含 nested 定義的成功／失敗原子性、預設值、另一會員不能取得 Draft、刪除後 list／detail 不可見、URL ID 本身不授權；空清單與分頁上下界。若完整 nested Create 的驗證需 R6 的共用程式，R5 要提供相符的實際 Create 行為，不能暫時接受錯誤資料；可先完成其最小正確實作，R6 重構並覆蓋 Delta。**提交**：可依讀寫／刪除拆分。**停止**：Create Contract 確認無臨時格式。

### R6 — Draft Patch／Question／Option Delta

**輸入**：01 FORM-01／04～06、QST、02 §8～9、03b §7.2、04／05。**工作**：完成 10 號 `PATCH /forms/{formId}`；依正式規格處理未指定欄位保留、nullable replacement、Question／Option Delta、id-only 刪除、type change、config／排序與完整 Candidate Definition 驗證。沒有獨立 Question／Option API；不得自己增加。只在 Draft 可修改 Definition。

**驗收／證據**：對所有正式 Question type／config 矩陣與 nesting 測新增、更新、刪除、空陣列 no-op、未提及保留、重複 ID、跨 Form／Question ID、非法順序與 Value；Choice→非 Choice 清理 Options，且失敗時原狀保留；未授權與已刪除情形的 status／code／優先序正確。以真 DB 驗證整份 Aggregate 的原子性。**提交**：如 `feat(forms): implement draft definition delta patch`。**停止**：交付可重現的 Delta 案例。

**前次開發驗證（2026-10-03）**：已修正 PATCH 新增 Option 的一般文字 trim；新 Option 與既有 value 修改的同 ID replacement 均在完整 Candidate 驗證後、舊 key 刪除保存後才追蹤／插入。新增 6 個測試案例（2 個正規化、3 個 Owner 指定的混合操作、1 個第二次保存故障回滾），另保留原有競爭、value 交換／重用、錯誤 path 與 SQL 回歸測試。正式驗收尚未準備或執行；開發測試 PASS 不代表驗收 PASS。格式檢查另發現 Controller 既有 switch expression 縮排問題，未擴大修正，見前次開發報告。

**前次開發進度（2026-10-03，有限 Candidate 重構完成）**：先重新實跑舊 Windows Form 16/16 與全套 61/61 PASS；新增 DLL 隨後被 Smart App Control 封鎖，Owner 手動執行亦總計 0。依 Owner 授權改以官方 SDK 10.0.400 Linux 容器跑同套測試，沒有降低安全原則或測試要求。先重現 8 個契約案例失敗，再修正 Number decimal 範圍／捨入誤判、Config 等值 no-op 與 sortOrder 欄位來源；另補零值極大指數的 DB 邊界重現／受控拒絕。契約測試共新增 39 案，必要修正階段全套 100/100 PASS，與重構分開提交。有限重構已完成「獨立型別化 Candidate → 正規化／完整驗證 → 差異 ChangeSet → EF 保存」，移除 Aggregate JSON 往返、字串 path 解析、Snapshot 與手動狀態管理；保留權限／Draft／Form lock／交易／驗證次序、延後 Option 追蹤與同 ID／createdAt replacement。新增重構安全 6 案、加強實際 SQL 欄位與三組混合操作精確回應比較；Save #2 與 commit 故障均實測完整回滾。最終 Form 61/61、全套 106/106 PASS，0 skipped；build 0 警告／0 錯誤，diff 檢查通過。PATCH 於交易內以 AsNoTracking 取得實際 DB 回應，commit 成功後才回傳；已明列此時間／JSONB 表示改善及必要修正的可觀察差異。Linux 結果不冒稱 Windows 原生執行結果，開發 PASS 不代表驗收 PASS。該次停在開發交付，未準備／執行正式驗收，R7／G1 未開始。細節、命令、TRX 位置與未驗證事項見前次重構開發報告。

**歷史開發進度（2026-10-03，正式測試前有限收尾）**：保留 Candidate／ChangeSet 架構。新精度測試先以真實 Npgsql／PostgreSQL round-trip 確認假變更：12 案中 10 FAIL／2 PASS，含相同截止時間重送及僅差次微秒但保存值相同。ChangeSet 現以既有 Npgsql 10.0.3 的微秒編碼比較，保留 2000 年前後與 MinValue／MaxValue 的原保存行為，不改輸入格式／小數位限制／DB schema。修正後精度 12/12、受影響 Form 73/73 PASS，0 skipped；必要 build 0 警告／0 錯誤。DefinitionNumber 只將既有 PostgreSQL 17 限制值改為具名常數並補來源，不改數值範圍；後續 R8 Answer／R10 Statistics 須依 01 §3.3、05 §11.4 維持與 Validation／JSONB 一致的精確數值等值與排序，不能經 decimal／double 暗中捨入，本次僅登記、不實作。§3／6／7 已移除開發完成後自動準備／執行驗收的要求，明列四階段及 Owner gate。命令與證據見收尾開發報告；該開發階段只重跑受影響測試，尚未進入正式驗收。其後 Windows 原生確認、全套回歸與 Owner 確認已完成，現況見下方驗收收尾；UUID／lock 時機等延後項目維持原決定。

**驗收收尾（2026-10-04）**：Owner 在本 chat 明確表示「R6 驗收證據已 review，我確認 R6 accepted」，依提交 `81b64f3c87b2e9efb4f1955cf0397acb99125114` 的 AI 執行證據確認通過。RunId `r6-acceptance-20261003-01`：Windows 原生 API PASS、全套 118/118 PASS、HTTP／DB 56/56 PASS、暫存狀態復原與秘密清理 PASS；Linux 未執行，人工全部未執行，沒有改填人工 PASS。兩份驗收文件已同步狀態並修正過期敘述；本次僅文件收尾，未擴充或重跑測試，R4／R5／R6 fixtures 保留。R6 文件收尾當時，R7 另只準備精簡實作計畫；其後 R7 授權與進度見 R7 紀錄。

### R7 — Publish／Close／Result Visibility

**輸入**：01 FORM-01／03／04／07／09、CON；03b §8.2～8.4、04、05 §8～10。**工作**：完成 12～14 號端點；Draft 發布完整性驗證、Published Definition Freeze、Closed 不可 Reopen、未刪除時獨立修改 resultVisibility；與 R6 的 Patch／Publish 競爭控制。

**驗收／證據**：不完整 Draft 被拒並回正式 error details；發布後 definition 修改被拒，visibility 仍可依規格變更；Close／Publish 重試依狀態回 409；截止時間**不自動 Close**。並行 Patch／Publish 測試保證正式定義不被舊寫入改動。**提交**：可拆 lifecycle／visibility。**停止**：記錄原子及並行策略與測試結果。

**本次計畫／授權（2026-10-04）**：Owner 已 review 並核准提交 `0f1f70a` 的 [R7 精簡實作計畫](R7_Implementation_Plan.md)，明確授權開發；補充 Publish／Close 不要求 Body／JSON、Result Visibility 才解析並驗證 JSON Body，以及 Publish 正規化只改獨立 Candidate、成功後 Definition／子列及 timestamps 原樣保留並以 DB 測試證明。直接納入實作，不需重送計畫。計畫涵蓋共用 Validator／Form lock、生命週期與獨立 Visibility、必要局部整理與開發測試。後續開發交付、成對驗收計畫 review、授權 AI 執行及 Owner 確認仍依 §3 四階段，未提前進入 R8～R11／G1。

**開發交付（2026-10-04）**：實作提交 `0496041c762062f310bf61662276d44c8ea83e09`；[R7 開發報告](R7_Development_Report.md) 記錄規格對照、命令、資料安全與平台證據。完整 DB 快照／UPDATE SQL 證明 Publish 共用正規化不回寫 Definition 或子列；37 個開發案例涵蓋必要錯誤／no-op／deadline／rollback／七種可觀察 lock waiter 的並行。Windows 首次 testhost 封鎖與全套 CORS TestFactory 例外均保留紀錄，最終 focused 37/37、full 155/155；Linux 同套亦 37/37、155/155。CORS 例外為「未重現、根因未明」，撤回先前偶發判斷，未改該測試預期。R4／R5／R6 保留資料完整快照相等，SDK 暫存容器已清理、PostgreSQL／volume 與重新啟動的 API 保留。僅 development verified，不以自測標 accepted。

**外部 review 有限確認（2026-10-04）**：依 Owner 指示核對 M2 首次堆疊及 TestFactory／TimeProvider diff；本次單獨重跑前後各 1/1、Windows R7 49/49／full 167/167 PASS，仍為「未重現、根因未明」。L3 缺少／錯誤 Content-Type 原回 200，僅於 Visibility 修正為正式 `400 VALIDATION_ERROR`；四種傳輸錯誤、非字串值、Owner 已刪除存取及 R7／Delete 競爭均通過針對性案例。未做 L2 重構或 Capacity Policy；Linux 此次未重跑。資料快照不變，詳見 [確認報告](R7_Review_Confirmation_Report.md)。無已重現的 R7 阻擋事項，Owner 已授權接續準備驗收計畫；未授權執行，未標 accepted。

**驗收計畫準備（2026-10-04）**：依本次 Owner 授權進入 Prepare，交付 [R7_Acceptance_Test.md](../test-plan/R7_Acceptance_Test.md) 與 [R7_Manual_Tests.http](../test-plan/R7_Manual_Tests.http)，15 組／42 個 HTTP 子項，優先非字串 Visibility／Owner 已刪除存取，包含 Windows 啟動、全套／M2、Candidate 隔離、三狀態 no-op、正式 Envelope、Freeze 真正並行、保留及復原。預定唯一 RunId `r7-acceptance-20261004-01`，所有 AI／人工結果「未執行（等待 Owner review）」；沒有執行計畫中的 HTTP／DB 操作，沒有用開發證據代填。停止待 Owner 審核及執行授權。

**AI 驗收執行（2026-10-04）**：Owner review 通過 `28cb8e2`，並直接核准本次 build→原生 startup／health→full 的順序、兩 waiter 確認後釋放 barrier、回歸名稱核對三項修訂；提交 `858d6ae` 後執行。RunId `r7-acceptance-20261004-01`：Windows restore／build 0 warnings/errors、重新啟動本次 DLL／health/readiness PASS；首次 full 167/167、M2 targeted 1/1 PASS；42 個 HTTP 子項與必要 DB／並行 PASS。M2 本次未重現、根因未明，歷史失敗及本次 helper CLI／UTF-8 處理錯誤均保留，不以重跑覆蓋。Linux 未執行（fallback 不需要）；人工全部未執行。既有 R4／R5／R6 完整快照不變，新三張保留，raw／deadline 復原與秘密掃描完成；API／DB／volume 保留。詳見 [R7_Acceptance_Execution_Report.md](R7_Acceptance_Execution_Report.md)；交付「驗收結果待 Owner 確認」並停止，未 accepted、未開始 R8／G1。

### R8 — Submit／Response／Answer

**Owner 驗收確認（2026-10-05）**：Owner 在本 chat 明確確認 R8 驗收通過，並於 R9 開發授權再次要求同步此進度；R8 現標記 accepted。依據 RunId `r8-acceptance-20261005-02` 的 [本次重驗報告](R8_Acceptance_Rerun_20261005_Report.md)、[Markdown 驗收紀錄](../test-plan/R8_Acceptance_Test.md) 與 [.http](../test-plan/R8_Manual_Tests.http)：Windows full 280/280、54 HTTP／4 非 HTTP PASS。人工未執行，不改填人工 PASS；原始 FAIL／BLOCKED、補驗 PASS、測試版本與證據完整保留。此次只同步 Owner 確認，不重跑或覆寫 R8 驗收。下方待確認／未 accepted 敘述保留為歷史。


**2026-10-05 本次重驗完成，驗收結果待 Owner review。** RunId `r8-acceptance-20261005-02`，測試 HEAD `d4d93e75ce35947015280f77794527ff8c9b5631`；核准計畫 `8680ccc`，source/tests/specs 與候選相同、請求／預期未改。Windows restore/build 0 warnings/errors，本次 native health/readiness PASS；全套 **280/280 PASS**、**54 HTTP／4 非 HTTP 全部 PASS**。四組 native race 各確認兩 waiter 後釋放 barrier；deadline／workers／barriers 清理完成，舊列完整不變、新五張 fixtures 與合法歷史保留。所有前次 FAIL／BLOCKED 與 PASS 保留歷史；人工未執行。詳見 [本次重驗報告](R8_Acceptance_Rerun_20261005_Report.md)。未 accepted、未進 R9。

**以下保留先前歷史。**

**2026-10-05 獨立 review／本次 AI 驗收：BLOCKED，驗收結果待 Owner review。** HEAD `0bb7842`、核准 `8680ccc`，source/tests 與 `d3f75b3` 相同、54 請求未改；未發現具體 R8 產品／計畫缺陷。新 RunId `r8-acceptance-20261005-01`：build 0 warnings/errors、native 01a PASS；01b 首次 503 Unhealthy FAIL，PostgreSQL 實際 host binding 空。HTTP 1 PASS／1 FAIL／52 BLOCKED；非 HTTP 01 FAIL、02／13／14 BLOCKED，full／後續未執行。舊完整資料與前次最終 snapshot 相同，無新 fixture；所有舊失敗與補驗 PASS 保留歷史、人工未執行。詳見 [本次獨立報告](R8_Independent_Review_Acceptance_Report.md)。需恢復環境後新 RunId 補驗；未 accepted、未進 R9。

**正式補驗完成，待 Owner review（2026-10-04）**。核准 `8680ccc`、Execute 授權持續有效；新 RunId `r8-acceptance-20261004-02`：Windows restore/build 0 warnings/errors、本次 native API health/readiness PASS、全套實際 280/280、54 HTTP／4 非 HTTP 全部 PASS。四組 native 兩 waiter／共用 10 秒／finally 與 Clock／EF／precommit faults 證據完整，旧列不變、新五 fixtures／9 Responses／10 Answers 保留。首次 FAIL／BLOCKED 與環境修復紀錄保留；人工未執行，Owner 指定本輪依 AI 證據審核、不要求人工作為 accepted 必要條件。详見 [正式補驗報告](R8_Acceptance_Reexecution_Report.md)及成對結果。未改產品／預期，未 accepted、未進 R9／R10／G1。

**後續環境修復（2026-10-04；非正式補驗）**：Owner 授權直接修復原 5432 環境。首次 Compose up 因 Windows bind access permissions 失敗；WinNAT 重啟後 5432 不再位於 active excluded range，同一命令 exit 0，原 named volume／`127.0.0.1:5432 -> 5432/tcp` 保留、容器 healthy。同版已建置 API 重新啟動後，獨立環境檢查 TCP 成功／readiness 200 Healthy；完整非秘密資料快照與先前 final 相等（4 users／9 forms／22 questions／12 options／0 responses／0 answers）。未修改持久網路設定／產品／timeout／預期；原正式首次 FAIL／BLOCKED 不改填 PASS，正式補驗尚未執行。證據與命令見 [執行報告](R8_Acceptance_Execution_Report.md)「後續環境修復」。

**AI 驗收執行（2026-10-04）**：Owner 核准 `8680cccd17e98646a6f257757dfa65654c039153` 並授權 Execute；RunId `r8-acceptance-20261004-01`。先 restore/build（exit 0、0 warnings/errors），啟動本次 Windows native API（PID 28640／launcher 35112），首次 health 200 PASS；readiness 503 Unhealthy FAIL。實際 Docker 5432/tcp 無 host binding、API 設定 localhost:5432 無 listener，容器內部 healthy；按 gate 將 02 full／03～13 HTTP／DB probe／並行記 BLOCKED，不跳過 readiness、不改產品／預期。54 requests 只有 01a/b 送出：1 PASS／1 FAIL／52 BLOCKED；非 HTTP 01 FAIL、02／13／14 BLOCKED，人工全部未執行。本次 RunId 零新 Form／Response／Answer，probe／barrier／workers／可用 secret 都未產生，既有 API／DB／volume 保留；失敗後兩次唯讀完整 snapshot 相同（4 users／9 forms／22 questions／12 options），不是成功路徑 baseline 或完整 14 PASS。所有首次失敗／未完成工作／命令與證據見 [R8_Acceptance_Execution_Report.md](R8_Acceptance_Execution_Report.md)，實際結果同步成對文件。環境恢復與補驗待下一步指示，保留本次記錄；停止於「R8 驗收結果待 Owner review」，未 accepted，未開始 R9。下方 Prepare／開發記錄保留歷史。

**驗收計畫局部修訂（2026-10-04）**：Owner 僅授權修改文件與靜態檢查；以 `63bd1a9` 成對計畫為基準，13 的兩 waiter 觀察／輪詢／SQL／delay 共用單一 monotonic 10 秒預算，兩個真實 waiter 都確認後才釋放 barrier，finally 仍逐一回收全部 workers，產品 timeout 設定不變。原始 `.http` 54 requests（含 05b）逐段核對與基準內容相同（忽略 CRLF／LF）；分隔符／URL／headers／variables／0-byte Body、刻意壞 JSON、合法 JSON 跳脫、精確 number token 與無 Markdown 貼上標記均通過靜態檢查，命令／證據記在 [Markdown 計畫](../test-plan/R8_Acceptance_Test.md)末節。停止於「R8 修訂驗收計畫待 review」，本次未執行正式回歸／HTTP／DB probe／並行 harness；所有驗收 AI／人工欄仍未執行，未 accepted。原 Prepare 提交／開發結果保留歷史；Execute 待本次核對通過及另行授權。

**驗收準備交付（2026-10-04）**：Owner 明確授權局部收尾後直接 Prepare；候選基準 `d3f75b37623f5f774d8dabe5817c4488a96eb368`。已準備 [R8_Acceptance_Test.md](../test-plan/R8_Acceptance_Test.md)／[R8_Manual_Tests.http](../test-plan/R8_Manual_Tests.http)：54 個 HTTP requests，另有環境／同版全套（預計 280，以實際探索為準）／DB／四組 native barrier／finally／資料保留檢查。新增重點為 05e application/json＋0-byte Body、06c 真實另一張 Form 的 Question ID＋QUESTION_NOT_FOUND／零新增、06a 非 Owner Submit Draft 409 與 06b Detail 404。計畫使用五張 fresh RunId 合成 Forms，保留舊資料與合法歷史，deadline probe 僅操作本次新列且 finally 復原，並行須先確認兩 waiter；只做文件靜態自查，沒有執行本計畫 HTTP／DB probe／並行／正式回歸。AI／人工欄全為未執行（等待 Owner review）；停止於「R8 驗收計畫待 review」，Execute 仍需目前成對計畫核准與明確授權，未 accepted。下方保留歷史開發授權／結果。

**B review／收尾與新授權（2026-10-04）**：Owner 確認 `14aac30` 的 B code review 通過，授權 Drain helper 局部收尾後直接準備驗收。Helper 現逐一有限等待、收集非預期失敗，全部處理後 AggregateException；只有真正 canceled Task 可視為預期取消。新增三案，修正前兩案 FAIL 證據保留，Windows build 0 warnings/errors、最終 R8 affected 34/34 PASS、12 個 waiter／四項提交前故障仍通過、資料 snapshot 相等。只改測試 helper，未改業務契約；本次未重跑 full，原 277/277 為 B 歷史結果，不能代填当前 280 full。詳見 [B 報告收尾節](R8_B_Development_Report.md#b-review-後局部收尾2026-10-04)。接續驗收準備已授權；尚未執行正式驗收，最終交付停止於「R8 驗收計畫待 review」，不改填 accepted。下方原 A／B 授權与交付保留當時狀態。

**目前授權（2026-10-04）**：Owner 已確認 A code review 通過，明確授權依 [核准計畫](R8_Implementation_Plan.md) 開發 B（Submit／Receipt、Clock、原子保存／並行與 Delete 局部交易／鎖）。不重做 A／Definition Validation，不開始 R9／R10／G1；交付停止於「R8 開發完成、待驗收」，尚未授權驗收準備／執行。下方 A 交付與補件保留當時歷史狀態。

**B 開發交付（2026-10-04）**：新增 Submit／ResponseReceipt；取 Form lock 並重讀 Published 後只取一次 submissionTime，驗證順序沿 03c §4.3，寫入前以同時間重確認。Response＋正規化 Answers 同交易 EF 保存後 AsNoTracking 讀回，Receipt 按 Question sortOrder／ID 排序，commit 成功才回 201。Delete 已局部加入共用鎖／ReadCommitted／鎖後 Owner 判定／commit。Windows restore／build 成功、0 warnings/errors，B 最終 31/31、full 首次 277/277 PASS（含 A 79 與原 167），0 skipped；實際 EF JSONB／timestamp round-trip、12 個 PostgreSQL waiter、部分 Answer／全部 Save 後 precommit 及 Delete UPDATE／precommit 故障通過。首輪測試工具錯誤與診斷保留，不覆寫成 PASS；COMMIT 結果未知情形未測且不推論零新增。既有 fixture 完整快照相等，未移除 DB volume，Linux fallback 未使用。Git 權限審查逾時後獲 Owner 新明確指示完成必要提交；詳見 [R8_B_Development_Report.md](R8_B_Development_Report.md)。停止於「R8 開發完成、待驗收」，沒有準備或執行正式驗收，未 accepted。

**輸入**：01 SUB／CON、02 §10～11、03c §3～4、04、05 §8～12。**工作**：完成 15 號端點，登入者（Owner 亦可）提交；一次成功建立 Response 和全部有效 Answers；選項歸屬、required、optional omission、全題型 value／正規化、deadline、responseMode；single 並行最多一次成功；multiple 保留歷史。Submit／Close／Delete 實作可解釋的交易順序。

**A 開發交付（2026-10-04）**：Owner review 通過計畫提交 `1a0cf46`，授權六項局部補充後直接開發 A。新增兩階段無副作用 validator（Envelope 不提前驗 answers 必填／Array；答案階段完成 Shape、Question 歸屬、Required、五題型／精確數值／Choice 與 omission）；NUL／孤立 surrogate 受控錯誤、合法 pair 保留、明確 null 使用 INVALID_VALUE、輸入／輸出 JSON 生命週期已測。Windows restore／build 成功，但 focused testhost 被安全政策封鎖（未執行案例）；Linux 同套 A 首次 78/78、full 首次 245/245 PASS，0 skipped，既有資料完整快照相等，暫存 SDK 容器已清理。詳見 [R8_A_Development_Report.md](R8_A_Development_Report.md)。停止於「A 開發完成、待 review」；沒有 Submit 路由、Receipt、交易保存或 Delete 修正，不以 A 自測宣稱 R8 完成／accepted。沒有準備或執行正式驗收。

**A review 補件（2026-10-04）**：初步 code review 無明確阻擋缺陷；依 Owner 要求提供四項依賴、`986cfd8` 全部 5 檔完整 diff 與命令／結果引用。確認原案缺少非空 Text 短於 minLength，僅補一案並更正 JSONB probe 名稱／描述：只驗相容性／數值等值，不涵蓋 EF 保存／Receipt round-trip。Windows build 0 warnings/errors、單案仍被 DLL 安全政策封鎖；Linux A 79/79、full 246/246 PASS、既有資料快照不變。Validator／Definition Validation 未修改，UUID／Unicode 矩陣未擴充；詳見 [補件索引](R8_A_Review_Materials.md)。仍停於 A 待 review，B 未授權。

**R8 前置事項（R7 review M1）**：現有 Delete 在讀取／Owner 判定時沒有使用共用 Form lock 或明確交易；不能以最終 UPDATE 的隱含列鎖宣稱 Submit 競爭安全。R8 開發須將 Delete 與 Submit／既有 Close 納入同一 `FormAggregateLock`／交易策略：每一寫入自己的交易先鎖未刪除 Form，再讀目前 Aggregate、判權限／狀態／deadline／single，持鎖直到 commit／rollback。Delete 仍只軟刪除，不改 status／子列；Submit 保證 Response + 全部 Answers 原子保存，與 Close／Delete 形成一致先後。必要測試涵蓋 Submit 先提交→Response 保留且 Close／Delete 可成功；Close 先提交→`409 FORM_NOT_SUBMITTABLE`；Delete 先提交→`404 FORM_NOT_FOUND`，含等待後重讀以及故障回滾。本項只是依 03b §8.1／8.3、03c §4.3、05 §8／9.2 登記的實作前置，不授權提前修改 Delete 或新增 Submit。

**驗收／證據**：匿名僅指 Statistics 身分揭露模式，未登入仍不可填；`serverNow < deadline` 才接受，等於 deadline 拒絕；錯誤答卷零殘留；同時兩筆 single 至多一個 201；multiple 兩筆均保存；Close／Delete 競爭測試，狀態及 Response 一致；ResponseReceipt 形狀與錯誤 details 正確。時間測試使用可控制 Clock，避免等待真實時鐘。**提交**：可拆 answer validation／transaction。**停止**：提供 DB 狀態與 API 驗證證據。

### R9 — My Latest／Respondent Count

**AI 驗收交付（2026-10-05）**：Owner 核准計畫及三項局部修訂後直接 Execute，再明確指定動態註冊／登入兩位新會員（HTTP 38→40）；修訂 `f8a8026`／`f93d7fc`。首輪 -01 Windows build／原生 API／health/readiness／full PASS，但 04a 被 harness 的 is_required 錯誤中止，13 lossy JSON 精度比較誤報；首輪 6 HTTP PASS／1 FAIL／33 BLOCKED 與合法 2 Users／1 Draft／2 Questions 原樣保留，精確 raw baseline 確認舊列未變。`2644eb3` 保留歷史並記新 RunId -02；未改程式／預期。補驗先核對相同 source／DLL／native PID 34980，再 health/readiness／本次 Windows full 306/306（R9 26、四組實際 Form SELECT 快照）／40 HTTP，全部 PASS；01／02／10／13 非 HTTP 4/4 PASS，Linux 未執行，人工全部未執行。新增 2 Users／2 Forms／3 Questions／3 Responses／3 Answers 保留，所有舊列與首輪合法 fixtures 精確不變，deadline probe finally 原值／全列相等，秘密僅記憶體且清除，API 保留。詳見 [R9_Acceptance_Execution_Report.md](R9_Acceptance_Execution_Report.md)／[Markdown](../test-plan/R9_Acceptance_Test.md)／[.http](../test-plan/R9_Manual_Tests.http)。停止於「R9 驗收結果待 Owner review」，未 accepted／未開始 R10。下方保留準備與開發歷史。

**驗收準備交付（2026-10-05）**：Owner 本 chat 確認 R9 code review 通過，授權只設計驗收計畫；候選 source/tests/specs `32bb41c`。已準備 [Markdown](../test-plan/R9_Acceptance_Test.md) 與 [.http](../test-plan/R9_Manual_Tests.http)，38 順序 requests、01／02／10／13 非 HTTP 工作、2 fresh Forms；Latest 整份／排序／omission、Count 去重／Owner 零 Answer、Draft／Published／Closed／空資料／Deleted／代表性錯誤覆蓋。02 承接九個方法／26 rows 並要求本次同版 full 新 TRX，四組 ReaderExecutedAsync 實際 SELECT 快照證據，不代填開發 PASS。01 原生 API 必須本 repo／本次 build 且 health/readiness PASS；DLL policy 失敗阻擋後續 HTTP，Linux 只作測試 fallback。保留舊資料／新合法 fixtures；deadline probe 限本次新 Form 並 finally 精確復原。僅 Python 文件靜態核對與 Git diff，自查 38 編號／順序／JSON／變數／results／證據連結一致；未 build／startup／送 HTTP／跑測試或 DB 操作，全部 AI／人工欄未執行。停止於「驗收計畫待 review」，Execute 仍需本版 codex-reviewer 審查及 Owner 核准／明確授權，未 accepted／未開始 R10。下方保留原開發紀錄。


**開發交付（2026-10-05）**：Owner 已核准 [精簡計畫](R9_Implementation_Plan.md) `14442b2` 並授權開發，維持 RepeatableRead；並行以 ReaderExecutedAsync 的實際 Form SELECT 完成為快照控制點，非 BeginTransaction。Latest 依目前 User／Form 在 DB 選整份 Response（submitted_at DESC／id DESC），不補舊答案；Count 對成功 Response 的 User ID 去重。四組 Latest／Count × Submit／Delete 快照競爭、錯誤／狀態／空資料／去重／Receipt／無寫入測試完成；Windows build 0 warnings/errors，Windows DLL policy 阻擋測試探索與原生 API 恢復；Linux 同版 focused 26/26、full 306/306 首次 PASS，保留資料快照相同。R8 accepted 已獨立同步 `0f44286`，原證據保留。詳見 [開發報告](R9_Development_Report.md)；停止於「開發完成、待驗收」，未準備／執行正式驗收，未 accepted／未開始 R10。


**輸入**：01 SUB-06～07／STA-01、03c §5～6、04／05。**工作**：完成 16～17 號端點；本人最新回覆僅按完整 Response 選取，`submittedAt DESC, id DESC`；Owner Count 用不同有效填答者，並守 Deleted／Draft／Closed 權限規則。

**驗收／證據**：multiple 的較新回覆故意略過 optional 題時，最新回覆不可從舊答補題；同時間 tie-break 有穩定結果；非 Owner 不能取得 Count；依完整端點矩陣驗證空資料／Soft Delete／身分邊界。**提交**：如 `feat(responses): add latest receipt and respondent count`。**停止**：回報 Effective Response 實測資料。

### R10 — Statistics／Result Access

**開發交付（2026-10-10）**：Owner 已核准 [精簡實作計畫](R10_Implementation_Plan.md) `06853f3` 並明確授權開發；產品／測試 `a4f9402336a4eecdc07c72e92ef09234a4d5e976`。新增端點 18，Conditional Auth 使用既有 JwtBearer TokenHandlers／驗證參數；首個 Form SELECT 的 RepeatableRead 快照涵蓋 Caller Active、政策／資格、Creator／Respondent 名稱及完整統計。DB `DISTINCT ON(user_id)` 選最新整份 Response，精確 Number 分組／排序、零資料、各層 count 與四個 Root 欄位完成；actual JSON 九欄位、anonymous omission、identified []、nullable deadline 與 number token 已驗證。Windows 最終 build 0 warnings/errors、focused **43/43**、同版 full **420/420**、短原生 **21 HTTP** 及 JSON assertions PASS；四組實際 SELECT 快照並行、SQL 與舊業務資料不變證據齊全。首次 build／測試失敗與校正保留於 [開發報告](R10_Development_Report.md)。停止於 **「開發完成、待驗收」**，供獨立 code review；未準備或執行正式驗收、未 accepted、未開始 R11／G1。歷史 Round pending／非阻擋待辦仍見 §10。

**以下保留原規格核准／規劃交付歷史（2026-10-10）**：當時 R10 **not started**。Owner 明確確認規格整理提交 `0e9c03e096f45fd21e27876edbf48299f463f05f` review 通過，採用四欄位追加及相關語意；[03c v1.1 §3.4／§7.3](../specs/03c-v1-api-contract-submission-statistics.md#34-statistics-root) 為已核准契約，相關正式文件與[規格更新報告](R10_Statistics_Specification_Update_Report.md)已同步核准標示。原精簡實作計畫交付停止於「R10 實作計畫待 review」，當時未修改產品／schema／Migration、未 build／測試。此歷史不代表目前仍缺開發授權，也不推定歷史 Round accepted 或正式整合驗收通過。

**輸入**：01 STA、FORM-07；02 §5／§7 的既有 User／Form 欄位；03c §3.4／§7～8（v1.1 已核准）；03b §3.5 的 Creator 目前名稱語意；04 §4.3／§10 統計特殊錯誤優先序；05 §5.3／§10～13／§20。**工作**：完成 18 號端點；只在 Closed 提供，依 owner_only／respondents／public 全矩陣授權；Public 壞 Cookie 視為匿名；匿名不出 respondents 或跨題 mapping，identified 的 respondents 僅 ID＋目前 displayName。即時計算每人最新**整份** Response 的題型分布，排序、count、zero-data 與回傳 metadata 對齊契約。Root 追加 `creatorDisplayName`、`responseMode`、`resultVisibility`、`submissionDeadlineAt`，全部由既有資料投影，依 03c §3.4 的必須出現、目前名稱、enum、nullable 與表定截止時間語意回傳；零資料及 anonymous 亦提供。`questions[].type` 已存在，維持原 Shape；不新增 endpoint、名稱快照或 schema／Migration。

**R8 A 數值依賴登記／R10 落實**：Answer 沿用 DefinitionNumber 的 PostgreSQL JSONB 安全保存範圍（01 §3.3），不經 decimal／double 捨入；Receipt 不承諾逐字保留原始 token。R10 已依 01 §3.3／05 §11.4 重用此精確 key／CompareTo 分組及排序，`1`／`1.0`／`1e0` 同組、正負零同組，以 DB JsonElement 作 JSON Number 輸出；超過 decimal／double 精度的相近值保持分開。既有 DefinitionNumber 範圍／程式未改。

**驗收／證據**：角色 × lifecycle × visibility 存取矩陣；未登入及壞／過期 Cookie 下 Published／Draft 的存在性保護；同一人多次提交只算最新完整回覆；選項 count、multiple choice 總數、空值項目、zero-data 200、identified item.count 與 respondents 長度一致；anonymous 結果（包含 Owner）無 Respondent 身分資訊；成功 Root 依 03c §3.4 完整回傳。此為既有 Round 範圍摘要，個別驗收計畫與執行仍依另行授權及核准 gate。**提交**：如 `feat(statistics): implement closed form statistics`。**停止**：附測試矩陣及漏項。

### R11 — A 階段整合／本機驗收／交接

**輸入**：全部正式文件，尤其 03a 端點索引與 05 §20；R0～R10 報告。**工作**：執行 clean restore／build／tests，在專用空庫從零 apply migrations，啟動 Compose＋API，依 API 契約用 cookie jar／`.http`／測試套件重走註冊→建表→填答→關閉→統計；對 Error Catalog 做端點級核對；補完整 README、設定範例、必要測試 fixtures、手動驗收指引與 known gaps。依 05 §20 檢核 Unicode、分頁、Soft Delete、併發、Retry、身分揭露。

**驗收／證據**：逐項列 PASS／FAIL／NOT RUN 及命令／輸出；修正本階段已實作功能的回歸，必要時提交一個或數個有目的修正 commit。註明 Google 端點與真實 Provider 驗證未做、正式部署及 HTTPS 線上測試未做；不得把 A 階段稱作 Backend V1 Freeze。**提交**：如 `test: verify local email backend integration`、`docs: document local backend setup`。**停止**：等待審查，進入 G1 需另次明確指示。

### G1 — 後續 Google Authentication 補齊（預留，不屬 A 階段指令）

讀 01 USR-05、02 `user_identities`、03b §4.4、04、05 §5／9／20；完成 Google ID Token 驗證、User＋Identity 原子建立、同 email 不自動綁定、existing identity login、首次並行登入最多一組有效 User／Identity、JWT Cookie 與可測驗證。使用者提供 Google Project 設定或確認可用的受控驗證環境後，實測外部整合；若缺憑證則保持 pending，不宣稱全 V1 Backend 完成。最後重新走 18 個端點與 §20 驗收；部署線上條件另行驗收。

## 6. Commit、審查與停止機制

此流程依現行 AGENTS.md 的階段邊界執行；狀態順序描述依賴，不授權自動推進。每個 Round 的預設停止點為：

| 階段 | 授權與狀態 | 交付及停止點 |
| --- | --- | --- |
| 開發交付 | Owner 指定 Round 開發／修正；`in progress → development verified` | 必要 build／自測／diff／文件與單一目的提交，回報 SHA／Git 狀態後停於「開發完成、待驗收」；不自動準備文件 |
| 驗收文件準備 | Owner 另行要求；`test plan pending owner review` | 使用 acceptance skill 交付成對 Markdown／.http 後停止待審核；不執行驗收 |
| AI 驗收執行 | 目前成對計畫已獲 Owner 核准，且有明確執行授權；`acceptance executing → acceptance results pending owner confirmation` | 只執行核准計畫、記錄實際結果及證據，交付後停止待人工驗收／Owner 確認 |
| 人工驗收與完成 | Owner 根據已記錄 AI／人工結果及同意的待處理事項明確確認 | 分開保留 AI／人工紀錄，才標記 `accepted`；不自動開始下一 Round |

Owner 可明確授權多階段，但不得省略執行前的計畫核准 gate。開發 build／自測不需要驗收計畫核准，其 PASS 不代表驗收 PASS，也不代替獨立 review。Commit 是各階段的版本化紀錄，不是進入下一階段的授權。Review 修正只處理已授權 Round，重跑受影響開發驗證；使舊驗收證據失效時先標示，再依 Owner 指示進入對應準備／執行階段，不自動重跑驗收。

例子：`chore: initialize ASP.NET Core API`、`chore: add local PostgreSQL compose`、`feat(db): add initial V1 migration`、`feat(auth): implement password login`、`feat(forms): implement draft definition patch`。Commit message 表達功能，不能以 `feat: finish backend` 隱藏範圍；未通過的 Round 不能為了達成提交指標而製作虛假乾淨 commit。

## 7. 放在 repository 的精簡 `AGENTS.md` 建議

本計畫放在 repo，例如 `docs/implementation/V1_Backend_Implementation_Plan.md`；正式 00～05 規格放在 repo 的固定目錄並維持版本一致。下列是**repo 建立後**建議放進 `AGENTS.md` 的短規則，路徑依實際結構調整：

```md
# Agent instructions

- Read `docs/implementation/V1_Backend_Implementation_Plan.md` and the current
  `docs/specs/00`–`05` specifications before editing related behavior.
- Execute only the implementation Round explicitly named in the user's request.
  During development, run the required build and automated tests, then commit and
  report the verified current-Round changes. Stop at development delivery; do
  not automatically prepare acceptance files, execute acceptance or start the
  next Round. Development tests do not require acceptance-plan approval and do
  not replace independent review, AI acceptance or manual acceptance.
- Only when the Owner requests acceptance preparation, use the acceptance skill
  to prepare both
  `docs/test-plan/Rn_Acceptance_Test.md` and
  `docs/test-plan/Rn_Manual_Tests.http`. The executable Markdown records
  purpose, prerequisites, curl or `.http` examples, expected results, and
  actual results. The commented `.http` companion follows the same test
  numbers, order, synthetic data, requests, and expected results; keep DB and
  other non-HTTP operations in Markdown and point to them from `.http`.
  Provide links to both files and stop for Owner review. Do not execute
  acceptance operations during preparation.
- Execute only the current Owner-approved paired plan with explicit execution
  authorization. Plan approval authorizes execution only when the Owner's
  instruction clearly includes that intent. Multi-phase authorization is
  allowed, but the pre-execution approval gate remains. Do not infer approval
  from preparation, development completion or acknowledgment of results.
  Record actual results and evidence; restore temporary configuration and
  retain synthetic acceptance fixtures. Deliver the AI execution report and
  stop for manual acceptance and explicit Owner confirmation. Keep AI and
  manual results separate; mark accepted only on that confirmation based on
  recorded results and agreed outstanding work. Never equate development PASS
  with acceptance PASS.
- For authorized review fixes, repeat affected development verification and
  report the new commit. Flag stale acceptance plans or results; any follow-up
  acceptance work must retain the phase and approval gates.
- Focus acceptance on the Round's main behavior, authorization, DB writes and
  representative errors. Record deferred fine-grained boundaries briefly; do
  not relax specs or replace them with large test matrices.
- The approved specs own business rules, database schema, API contracts, error
  codes and cross-layer constraints. Report unresolved conflicts with sections;
  do not silently redefine behavior.
- Current A stage is local Email/Password backend. Google Auth remains a required
  later V1 round; keep all seven tables. Nuxt and deployment are separate work.
- Protect uncommitted changes, secrets and persistent data. Use isolated test
  resources for destructive database rebuilds; ordinary acceptance uses the
  local development DB and preserves its synthetic fixtures.
- Commit only verified, single-purpose changes belonging to the current Round.
```

`AGENTS.md` 是長期在該 repo 生效的簡短執行護欄；完整驗收仍留在本計畫。若還沒有 repo，現在不憑空宣稱已安裝指令。

## 8. 要不要做成 Skill？

Repo 現已採用 [v1-round-development](../../.agents/skills/v1-round-development/SKILL.md) 與 [v1-acceptance-testing](../../.agents/skills/v1-acceptance-testing/SKILL.md) 兩個 repository-local workflow skills，適用階段依 AGENTS.md；原「現在不建議」的建立初期建議已過時。Skills 只組織開發／驗收流程，業務契約仍由正式規格管理，Round 範圍與進度仍由本計畫管理，共通護欄與階段邊界仍由 AGENTS.md 管理。不得將 API 欄位細節複製到 skills 或 AGENTS.md。本次文件整理不啟動開發或驗收 skill 的執行流程。

## 9. 第一次交給 Codex 的指令（複製使用）

```text
請閱讀 repository 中最新的 00～05 正式規格（03 以 03a／03b／03c 為準）
及 V1_Backend_Implementation_Plan.md。這次只執行 R0 — Implementation Alignment。
先檢查現有 repository、工具版本及 Git 狀態；交付 R0 規定的規格／
端點／資料表對照、A／B scope、技術決策建議、真正阻塞的疑問與驗證方案。
不要開始 R1，不寫正式業務功能，不把 Google Auth 從 V1 定義中移除。
完成後回報 R0 結果並停止，等我審查後再指定下一 Round。
```

## 10. R10 前局部 review 修正（2026-10-07）

Owner 核准 [局部修正計畫](R0_R9_Pre_R10_Local_Repair_Plan.md) 並授權開發；本次僅修正 F-01～03、受控確認 S-01 及成立後的 GetDetail scoped RepeatableRead，不啟動新 Round。S-01 在 root／Questions split SELECT barrier 及 Patch→Publish 時序均確認混合 Definition；修正後同一 GET 完整 V0。下一 GET 的兩個非 Publish 案完整比較 V1；Publish 案的 fresh GET 頂層 DTO 比較有下述非阻擋證據改善建議。

狀態：**局部修正獨立 review 已完成（2026-10-10），無阻擋問題，可依授權更新整合驗收文件；正式驗收尚未執行**。依 [獨立 review 報告](R0_R9_Pre_R10_Local_Repair_Review_Report.md)（提交 `7e10d71`），產品／測試基準 `e22db98` 的 F-01～03、S-01 均可結案；RV-I-01 是 Publish barrier fresh GET 完整頂層 DTO 比較的 P3 非阻擋證據改善，尚未補強。review 當次 Windows build 0 warnings／0 errors、完整 suite 377／377 PASS，七表完整列 digest 不變。下述開發歷史及其「待 reviewer」交付文字保留，不冒充本次新執行結果。

開發歷史：最終 Windows build 0 warnings／0 errors；focused 171／171、完整 suite 377／377、原生 Kestrel HTTP 開發自測 67 項 PASS。首次 FAIL、harness 校正、實際命令、資料保留、共用路徑及提交詳見 [局部修正開發報告](R0_R9_Pre_R10_Local_Repair_Development_Report.md)。既有 DB 七表 rows／完整列 digest 保持一致，原生新合成 fixtures 保留。

此為獨立 repair 註記；上方歷史 accepted／Owner pending 不更新。正式整合 Markdown／.http 尚未同步修正版或執行，舊結果不得代填修正版證據。後續 S-01 barrier 優先由整合計畫 02 的同版 suite 承接，不建立原生併行矩陣；R7／R9 Owner pending、I-01／I-02 非阻擋建議仍保留。R10 原僅獲規劃授權，現已經 Owner 核准計畫並另行授權完成開發（見 R10 開發報告）；正式驗收、R11 與 G1 仍需另次授權。
