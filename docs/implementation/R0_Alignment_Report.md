# R0 — Implementation Alignment Report

| 項目 | 結論 |
| --- | --- |
| 日期 | 2026-09-28 |
| 範圍 | R0 規格、環境與實作規劃對齊；未建立任何 .NET 專案或正式業務程式 |
| 狀態 | 已盤點；可在專案負責人審查後開始 R1 |
| 規格基線 | 00、01、02、03a、03b、03c、04、05 的 V1 1.0 正式版本 |

## 1. 已核對的基線與 repository

已依序完整閱讀下列正式文件：

1. `docs/specs/00-v1-overview.md`
2. `docs/specs/01-v1-domain-specification.md`
3. `docs/specs/02-v1-database-specification.md`
4. `docs/specs/03a-v1-api-contract-common.md`
5. `docs/specs/03b-v1-api-contract-auth-user-form.md`
6. `docs/specs/03c-v1-api-contract-submission-statistics.md`
7. `docs/specs/04-v1-error-catalog.md`
8. `docs/specs/05-v1-system-constraints.md`
9. `docs/implementation/V1_Backend_Implementation_Plan.md`

Repository 目前只有上述文件與根目錄 `AGENTS.md`；沒有 solution、`.csproj`、`global.json`、程式碼、測試專案、Compose 檔案或本機設定檔。R0 開始時工作樹乾淨，基線提交為 `91bdf9b`（`init`）。

權威分工依 `00` §12.1 與 `05` §1：Domain 行為以 01 為準、資料庫結構以 02 為準、HTTP 外部行為以 03a/03b/03c 為準、錯誤契約以 04 為準、跨層約束以 05 為準。API Base Path 是 `/api/v1`。不建立 `submissions` 或 `statistics` Table；7 張 Domain Table 中唯一的正常業務 CASCADE 是 `questions.id → options.question_id`（02 §12、05 §7.3）。

## 2. 開發環境盤點

| 項目 | 實測結果 | 判定 |
| --- | --- | --- |
| 作業系統 | Windows 10.0.26200 x64 | 可用 |
| .NET SDK | 10.0.400；ASP.NET Core Runtime 10.0.11 | 可用 |
| Git | 2.51.2.windows.1 | 可用 |
| Docker CLI | 28.5.1 | 可用 |
| Docker Compose | v2.40.0-desktop.1 | 可用 |
| Docker daemon | Docker Desktop 28.5.1，`desktop-linux`、`overlayfs` | 可用；當下沒有 Compose 專案 |
| 全域／本機 dotnet tool | 均無 | R1/R3 需決定是否以 local tool manifest 安裝 `dotnet-ef` |
| 建議開發連接埠 | 5000、5001、5432 均未監聽 | 可保留為 API HTTP、API HTTPS、PostgreSQL 預設值 |

此盤點只表示本機可開始 R1/R2；尚未拉取 image、建立 volume、啟動資料庫或驗證 API 連線。

## 3. 7 張 Domain Table 與 API／Round 對照

所有 7 張 Table 必須在 R3 一次建立，即使 A 階段尚未實作 Google 登入也必須建立 `user_identities`（02 §1、實作計畫 R3）。

| Table | 主要責任 | 直接使用的 Endpoint | 交付 Round |
| --- | --- | --- | --- |
| `users` | 內部會員、Email/Password、Profile、status | 1–6；4 亦讀寫 User | R3 Schema；R4 使用；G1 補 4 |
| `user_identities` | Google provider + subject 對 User 的唯一對應 | 4 | R3 Schema；G1 使用 |
| `forms` | Owner、Definition metadata、Lifecycle、Visibility、Soft Delete | 7–18 | R3 Schema；R5–R10 使用 |
| `questions` | Form Definition 的題目與 JSONB config | 7、9、10、12、15、18 | R3 Schema；R5/R6/R7/R8/R10 使用 |
| `options` | Choice Question 選項；唯一正常業務 Cascade 的 child | 7、9、10、12、15、18 | R3 Schema；R5/R6/R7/R8/R10 使用 |
| `responses` | 每次成功提交；Effective Response 的候選 | 8、15–18 | R3 Schema；R5 count 查詢、R8–R10 使用 |
| `answers` | Response 對 Question 的 JSONB answer | 15、16、18 | R3 Schema；R8–R10 使用 |

`forms` 與其定義在 Draft 可變、Published/Closed 凍結；Form Soft Delete 不物理刪除相關資料。`responses` 與 `answers` 只會由成功 Submit 原子寫入，Statistics 是即時計算 Query Result（01 FORM/SUB/STA，02 §10–11，05 §8、§14）。

## 4. 18 個 API Endpoint 與交付切面

| # | Method / Path | 功能 | Round |
| --- | --- | --- | --- |
| 1 | `POST /auth/register` | Email/Password 註冊，不自動登入 | R4 |
| 2 | `POST /auth/login` | Password 登入與 JWT Cookie | R4 |
| 3 | `POST /auth/logout` | 不要求有效 JWT 的 Cookie 清除 | R4 |
| 4 | `POST /auth/google` | Google ID Token 登入／首次 User + Identity 原子建立 | G1 |
| 5 | `GET /users/me` | 目前 Active User | R4 |
| 6 | `PATCH /users/me` | 修改 displayName | R4 |
| 7 | `POST /forms` | 原子建立 Draft Form + Question/Option Tree | R5 |
| 8 | `GET /forms` | Owner List、唯一分頁集合 | R5 |
| 9 | `GET /forms/{formId}` | Detail；他人 Draft 存在性隱藏 | R5 |
| 10 | `PATCH /forms/{formId}` | Draft Definition Delta | R6 |
| 11 | `DELETE /forms/{formId}` | Owner Soft Delete | R5 |
| 12 | `POST /forms/{formId}/publish` | Publish 驗證與凍結 | R7 |
| 13 | `POST /forms/{formId}/close` | Published → Closed | R7 |
| 14 | `PATCH /forms/{formId}/result-visibility` | Owner 調整統計可見性 | R7 |
| 15 | `POST /forms/{formId}/responses` | 原子 Submit Response + Answers | R8 |
| 16 | `GET /forms/{formId}/responses/me/latest` | 自己的 Latest 完整 Response | R9 |
| 17 | `GET /forms/{formId}/respondent-count` | Owner 的不同填答者人數 | R9 |
| 18 | `GET /forms/{formId}/statistics` | Closed Statistics 與 Visibility Matrix | R10 |

共通 JSON、Cookie、Authentication、Early Return 與 Error Envelope 由 03a 規範；端點 1–14 的精確流程由 03b 規範，15–18 由 03c 規範。04 的 HTTP Status、Top-level Code、Detail Code 與錯誤優先序是正式契約，不使用框架預設 ProblemDetails 或 422。

## 5. A 階段與 G1 邊界

| 里程碑 | 包含內容 | 不得宣稱／不得遺漏 |
| --- | --- | --- |
| A 階段（R0–R11） | 本機 ASP.NET Core、EF Core、PostgreSQL；7 表；Email/Password、User、Form、Submission、Latest、Count、Statistics 與其驗收 | 可稱「本機 Email/Password Backend 完成」，不可稱完整 V1 後端完成；不可省略 `user_identities` Schema |
| G1 | 真實 Google ID Token 驗證、Endpoint 4、User + Identity 原子建立、同 email 不自動綁定、併發首次登入與全後端回歸 | 不以假成功、501 或永久 non-scope 取代 Google Authentication |
| 後續前端／部署計畫 | Nuxt、Railway、正式 Domain、實際 HTTPS 驗收 | 不屬本實作計畫的 A 階段交付 |

這個邊界來自實作計畫 §2、§4–5，以及 01 USR-05、03b §4.4、05 §2、§5、§20。Google Authentication 是完整 V1 定義功能；只是在 A 階段暫緩外部 Provider 實作與驗證。

## 6. 相容版本建議

建議 R1 將下列 major line 寫入可審查的版本管理檔，並在首次 restore 時選定、鎖定相同 release line 的實際 patch 版本：

| 元件 | 建議 | 理由 |
| --- | --- | --- |
| SDK / Target Framework | .NET SDK 10.0.400、`net10.0` | 已安裝；.NET 10 為 LTS，支援至 2028-11。 |
| ASP.NET Core / EF Core | 10.0.x | 與 `net10.0` 同一 major line，避免 framework/provider 跨 major 混用。 |
| PostgreSQL EF Provider | `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.x | Npgsql 的 EF Core Provider；與 EF Core major line 對齊。 |
| EF CLI | `dotnet-ef` 10.0.x local tool | 可重現的 Migration 指令，不依賴每台機器的全域 tool。 |
| PostgreSQL | PostgreSQL 17.x，R2 將 Compose image 固定到可用 patch tag | 支援 UUID、JSONB、Enum、TIMESTAMPTZ 與所需交易語意；避免浮動 `latest` image。 |

Microsoft 現行支援資料將 .NET 10 列為 LTS 至 2028-11；Npgsql 將 EF Core provider 作為 `Npgsql.EntityFrameworkCore.PostgreSQL` 發行；PostgreSQL 17 的官方文件可用。R1/R2 實際新增版本時仍應以當時 NuGet 與 Docker registry 的可用 patch 為準，並固定在專案檔或集中套件管理檔中。

## 7. 最小專案結構建議

R1 建議採一個 API 專案與一個整合測試專案，不在尚未需要時預先拆分多個 class library：

```text
CustomFormService.sln
src/CustomFormService.Api/
  Features/                 # Auth, Users, Forms, Responses, Statistics
  Persistence/              # DbContext, EF mappings, migrations
  Infrastructure/           # clock, authentication, error handling, configuration
  Contracts/                # request/response DTOs
tests/CustomFormService.IntegrationTests/
docs/
```

Feature 內可保留 command/query/validation 的內部組織，但不得讓它改變 03a–03c 的 Route、Request/Response shape、Early Return 或 04 的 error shape。Clock 應以可替換抽象提供，支援 05 §10 的單次 Submit 時間取樣與 deadline 測試。R1 的 health endpoint 與 OpenAPI 僅為工程端點，不能被列為第 19 支 V1 業務 API。

## 8. 測試策略與 Round 依賴

1. 對文字正規化、Unicode code point 長度、DTO shape、題型／答案驗證與 Error Mapping 做快速 unit test；不以 .NET `string.Length` 取代 code point 計算。
2. 以真實 PostgreSQL 的 API/integration test 驗證 EF mapping、JSONB、Enum、Constraint、Migration、Cookie、404/403 邊界與原子交易；不用 InMemory provider 證明 PostgreSQL 行為。
3. 使用專用、可丟棄的 Compose project/volume 或等效隔離資源做從零重建與併發測試；絕不對一般開發資料庫的 volume 執行 `down -v`。
4. 以可控制 Clock 做 deadline；以並發整合測試驗證 single Submit、Patch/Publish、Submit/Close/Delete；G1 再測 Google 首次登入競爭。
5. R11 以 cookie jar 或 API test 重走 Register → Create → Submit → Close → Statistics，並逐項覆核 05 §20。

Round 依賴為：`R0 → R1 → R2 → R3 → R4 → R5 → R6 → R7 → R8 → R9 → R10 → R11`；G1 依賴 R3 的 `user_identities` Schema 與既有認證／錯誤基礎設施，但必須另經明確指示才開始。

## 9. 問題分類

### 9.1 規格衝突或缺失

沒有阻塞 R1 的權威規格衝突或缺失。R0 Review 已確認並修正原先的非阻塞索引不一致：`00-v1-overview.md` §3 現已明確列出 Part A、Part B、Part C（03a、03b、03c），與同檔 §12、03a §1/§7 及實作計畫 §1 一致。後續實作以三份 API Contract 為準。

### 9.2 可由實作者自行決定的技術細節

- Controller 或 Minimal API、Feature folder、DTO/validator/result type 與 Exception strategy。
- UUID 版本、密碼 hash algorithm/cost、EF mapping/constraint/index 名稱與是否增加非唯一效能 index。
- Transaction、lock、isolation、retry 與 concurrency strategy，只要符合 05 §8–9 的外部結果。
- OpenAPI 的工程設定、logging/tracing、local seed、測試框架與 DB isolation implementation。
- Compose service 名稱、非秘密的本機 database/user 名稱，以及使用 5000/5001/5432 的實際 binding；以上可在 R1/R2 記錄。

### 9.3 真正需要專案負責人提供的資料或決策

- R1 無外部資料阻塞：可先採本報告的本機端口與版本方向。
- G1 開始前需要 Google Client ID（audience）、受控 Google Project 設定，以及可安全使用的驗證帳號或測試環境；不得提交 credential。
- 建置實際跨來源前端或部署前，需要正式允許的 CORS origins、HTTPS host/domain 與 production secret 管理方式。
- 若要將真實使用者資料、密碼、token 或 Google credential 用於測試或提交，必須先取得專案負責人明確同意；在此之前使用合成 fixtures。

## 10. R1 建議做法（尚未執行）

R1 應只建立 solution、`src/CustomFormService.Api`、非秘密設定範例、`.gitignore`、README 與測試專案；選定並記錄上列相容版本，提供 `/health` 工程端點與開發 OpenAPI，統一 API 啟動連接埠。R1 不連接資料庫、不建立業務表、不提供任何 18 支 V1 業務 Endpoint，也不建立 Google Authentication 假端點。

R1 驗收應在乾淨環境依 README 實跑 `dotnet restore`、`dotnet build`、`dotnet run`，並取得 health 200 與 OpenAPI 文件。達成後以單一目的 commit（建議 `chore: initialize ASP.NET Core API`）交付，然後停止等待下一 Round 指示。

## 11. R0 驗證證據與未驗證項目

| 檢查 | 命令或方法 | 結果 |
| --- | --- | --- |
| 工作樹與基線 | `git status --short --branch`、`git log --oneline`、`git ls-files` | PASS：開始時乾淨，只有文件基線 |
| 正式規格與計畫 | 完整讀取 00–05、03a–03c 與 implementation plan | PASS：8 份正式規格與計畫均存在且為 1.0 |
| .NET / Git | `dotnet --info`、`git --version` | PASS：SDK 10.0.400、Git 2.51.2 |
| Docker / Compose | `docker --version`、`docker compose version`、Docker daemon inspection | PASS：CLI、Compose 與 daemon 可用；無運行 Compose project |
| 本機 port | 讀取 5000、5001、5432 的 listening socket | PASS：三者均可用 |
| 既有程式與測試 | repository tree、solution/project/tool manifest 搜尋 | PASS：尚未建立，符合 R0 範圍 |

未驗證：NuGet restore、Docker image pull、Compose start、PostgreSQL 連線、Migration、OpenAPI、health endpoint、任何 18 支 V1 Endpoint、Google Provider 與正式 HTTPS；它們全都屬後續 Round，未在 R0 提前執行。

R0 closeout 最終 Git 狀態：`## master`，工作樹乾淨；本次 commit 只包含 `00-v1-overview.md` 的 API 文件索引修正與本報告更新。沒有阻塞 R1 的未決問題。

## 12. References

- 00 §8、§9、§12；01 USR-05、FORM、QST、SUB、STA；02 §1、§10–15；03a §1、§5–9；03b §4–8；03c §4–8；04 全文；05 §1–3、§5、§7–10、§12–15、§20–21。
- `V1_Backend_Implementation_Plan.md` §1–5，特別是 R0、R1、R3、R11 與 G1。
- [Microsoft .NET release support](https://learn.microsoft.com/dotnet/core/releases-and-support)、[Npgsql EF Core Provider](https://www.npgsql.org/efcore/)、[PostgreSQL 17 documentation](https://www.postgresql.org/docs/17/)。
