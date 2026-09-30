# 環境設定設計、操作與稽核

| 項目 | 內容 |
| --- | --- |
| 文件目的 | 說明目前 Environment／Configuration 設計、操作方式與已知落差 |
| 適用範圍 | 本機 Docker Compose PostgreSQL、ASP.NET Core API、Integration Tests、Staging／Production 設定模板 |
| 稽核基線 | Repository commit `956593f` 之後的 configuration split；本機狀態檢查日期 2026-09-30 |
| 權威性 | 本文件是 developer／operator guide；正式約束仍以 [`05-v1-system-constraints.md`](../specs/05-v1-system-constraints.md) 為準 |

## 1. 核心設計：Infrastructure 與 Application 設定分流

本專案刻意保留兩條互不自動相通的設定管線：

```text
根目錄 .env ──> Docker Compose interpolation ──> PostgreSQL container

appsettings*.json ─┐
Development Secrets ├─> ASP.NET Core Configuration ─> Options binding ─> 啟動驗證
OS environment vars ┤
command-line args ──┘
```

- 根目錄 `.env` **只供 Docker Compose** 解析 [`compose.yaml`](../../compose.yaml) 的 `${...}`。ASP.NET Core 不會讀取這個檔案。
- API 使用 `WebApplication.CreateBuilder(args)` 建立的標準 ASP.NET Core configuration pipeline。程式只依賴邏輯鍵名，不依賴值是來自 JSON、User Secrets、OS environment variables 或 command line。
- 相同的 PostgreSQL credential 在本機有兩個角色：`POSTGRES_PASSWORD` 建立／啟動資料庫端，`PostgreSql:Password` 供 API client 連線。兩者在本機必須相符，但由不同 provider 提供。
- `.env.example` 只示範 Compose 鍵；它不是 API 的完整環境變數範本。API 的完整鍵名以本文件第 4 節為準。

這個分流在 commit `956593f`（`refactor(config): separate application and infrastructure settings`）完成，目的包含：不再把 dotenv 檔案手動匯入 PowerShell、讓 Development Secret 使用 User Secrets，以及讓部署環境直接使用平台的 environment／secret 管理。

## 2. 設定來源與覆寫順序

### 2.1 Docker Compose

Compose interpolation 的優先序由高至低為：

1. 執行 `docker compose` 時的 shell environment。
2. 明確指定的 `--env-file`。
3. 未指定 `--env-file` 時，專案根目錄的 `.env`。

`compose.yaml` 使用 `${VAR:?message}` 讓必要值在解析階段 fail fast，並用 `${POSTGRES_PORT:-5432}` 提供 host port 預設值。

### 2.2 ASP.NET Core application configuration

本專案未自訂 provider 順序。一般 application setting 由低至高為：

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. Development 環境的 User Secrets
4. OS environment variables
5. Command-line arguments

較高順位對同一邏輯鍵的值會覆寫較低順位。JSON／User Secrets 使用 `Section:Key`；跨平台環境變數使用 `Section__Key`，ASP.NET Core 會把雙底線轉成冒號。陣列索引也是階層的一部分，例如：

```text
Cors:AllowedOrigins:0      # JSON／User Secrets 邏輯鍵
Cors__AllowedOrigins__0    # OS environment variable
```

環境名稱屬於 host bootstrap 設定，必須在 host 建立前確定。專案慣例使用 `ASPNETCORE_ENVIRONMENT`；雖然框架也支援 `DOTNET_ENVIRONMENT`，但不要同時設定兩者，以免 precedence 誤判。兩者都未設定時，框架預設為 `Production`。

## 3. Docker Compose `.env` 清冊

| `.env` 鍵 | `.env.example` | Secret | 必填／驗證 | 實際用途 |
| --- | --- | --- | --- | --- |
| `POSTGRES_DB` | `custom_form_service` | 否 | Compose 要求非空 | 傳入 container 的 `POSTGRES_DB`，並供 healthcheck 選擇 database |
| `POSTGRES_USER` | `custom_form_service` | 否 | Compose 要求非空 | 傳入 container 的 `POSTGRES_USER`，並供 healthcheck 選擇 role |
| `POSTGRES_PASSWORD` | placeholder | **是** | Compose 要求非空；使用前必須替換 placeholder | 傳入 container 的 `POSTGRES_PASSWORD` |
| `POSTGRES_PORT` | `5432` | 否 | 可省略；省略或空值時使用 `5432` | 只控制 host `127.0.0.1:<port>` 到 container `5432` 的 mapping |

專案將 DB、User、Password 都設為 Compose 必填，即使 PostgreSQL image 本身只有 Password 是硬性必要值。這可避免 container 意外使用 image default。

`POSTGRES_*` 是 PostgreSQL image 的**初始化設定**：既有 named volume 已有 database 時，單純修改 `.env` 並重建 container 不會自動修改既有 role、database 或 password。一般開發 volume 不得用 `docker compose down -v` 嘗試修正 credential；應先確認資料保留需求，再明確變更資料庫 credential，或只對可丟棄的隔離測試 volume 重建。

## 4. ASP.NET Core 設定清冊

### 4.1 PostgreSQL client

| 邏輯鍵 | Environment variable | Development 預設 | Secret | 啟動驗證 |
| --- | --- | --- | --- | --- |
| `PostgreSql:Host` | `PostgreSql__Host` | `localhost` | 否 | 必須為非空字串 |
| `PostgreSql:Port` | `PostgreSql__Port` | `5432` | 否 | `1`～`65535` |
| `PostgreSql:Database` | `PostgreSql__Database` | `custom_form_service` | 否 | 必須為非空字串 |
| `PostgreSql:Username` | `PostgreSql__Username` | `custom_form_service` | 否 | 必須為非空字串 |
| `PostgreSql:Password` | `PostgreSql__Password` | 無 | **是** | 必須非空，且不得等於 committed example placeholder |

這五個值綁定到 `PostgreSqlOptions`，再由 Npgsql builder 組出 connection string。專案目前不使用 `ConnectionStrings__...` 或完整 connection string 環境變數。

Staging／Production 沒有 PostgreSQL 預設值，部署時五個鍵都應明確提供；Development 只需把 Password 放入 User Secrets，除非本機 Compose metadata 已偏離 committed defaults。

### 4.2 JWT

| 邏輯鍵 | Environment variable | Shared 預設 | Secret | 啟動驗證 |
| --- | --- | --- | --- | --- |
| `Jwt:Issuer` | `Jwt__Issuer` | `custom-form-service` | 否 | 必須為非空字串 |
| `Jwt:Audience` | `Jwt__Audience` | `custom-form-service-client` | 否 | 必須為非空字串 |
| `Jwt:SigningKey` | `Jwt__SigningKey` | 無 | **是** | UTF-8 至少 32 bytes，且不得等於 example placeholder |
| `Jwt:LifetimeMinutes` | `Jwt__LifetimeMinutes` | `1440` | 否 | 必須為正整數 |

Issuer、Audience、Lifetime 可沿用 shared defaults；每個環境必須使用不同的私密 Signing Key。V1 預設 token lifetime 是 24 小時，但部署設定可以明確覆寫為其他正整數分鐘。

### 4.3 Password hashing、CORS 與 Cookie

| 邏輯鍵 | Environment variable | Shared／Development 值 | Secret | 啟動驗證與效果 |
| --- | --- | --- | --- | --- |
| `PasswordHashing:IterationCount` | `PasswordHashing__IterationCount` | `210000` | 否 | 不得低於 `100000` |
| `Cors:AllowedOrigins:{index}` | `Cors__AllowedOrigins__0`、`__1`… | Shared `[]`；Development `[http://localhost:3000]` | 否 | 每項必須是 canonical HTTP(S) origin |
| `AccessTokenCookie:Secure` | `AccessTokenCookie__Secure` | Shared `true`；Development `false` | 否 | 必須有值；非 Development 不得為 `false` |

CORS origin 必須是完整 `scheme://host[:port]`，不得使用 wildcard、trailing slash、path、query、fragment 或 userinfo。多個 origin 使用從 `0` 開始的連續索引。空清單是合法設定，但瀏覽器跨來源請求不會取得 CORS allow headers。

Cookie 的其他 V1 屬性不是環境變數：Name=`access_token`、HttpOnly=`true`、SameSite=`Lax`、Path=`/`、不設定 Domain，Expires 與 JWT `exp` 相同。

### 4.4 Framework settings

| 邏輯鍵 | Environment variable | 目前預設／來源 | 說明 |
| --- | --- | --- | --- |
| `Logging:LogLevel:Default` | `Logging__LogLevel__Default` | `Information` | ASP.NET Core logging level |
| `Logging:LogLevel:Microsoft.AspNetCore` | `Logging__LogLevel__Microsoft.AspNetCore` | `Warning` | Framework category logging level |
| `AllowedHosts` | `AllowedHosts` | `*` | ASP.NET Core host filtering；目前無自訂 validation |

Logging 與 AllowedHosts 由 framework 消費，不綁定到專案自訂 Options，也沒有專案自訂的 startup validation。

## 5. Host 與平台變數

| 變數 | 本機／部署角色 | 專案行為 |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | 專案慣用的環境選擇器 | launch profiles 設為 `Development`；選擇 environment-specific JSON 並控制 OpenAPI、HTTPS redirect 與 Cookie Secure 規則 |
| `DOTNET_ENVIRONMENT` | 框架支援的替代環境選擇器 | 專案沒有直接設定；不要與 `ASPNETCORE_ENVIRONMENT` 並存 |
| `ASPNETCORE_URLS` | Kestrel bind URLs | 本機由 `launchSettings.json` 的 `applicationUrl` 間接提供；部署可明確設定 |
| `PORT` | Railway 等平台提供的 application port | Application code 不直接讀取；只用來組成 `ASPNETCORE_URLS` |

Development 的程式差異：

- 提供 OpenAPI JSON 與 Swagger UI。
- 不啟用 `UseHttpsRedirection()`。
- 允許 `AccessTokenCookie:Secure=false`，支援標準的 HTTP localhost browser topology。

非 Development 的程式差異：

- 不提供 OpenAPI／Swagger UI。
- 啟用 HTTPS redirection。
- 啟動驗證強制 Cookie Secure=`true`。

## 6. 各環境操作方式

### 6.1 新的本機 Development 環境

從 repository root 執行：

```powershell
Copy-Item .env.example .env
notepad .env

dotnet user-secrets set --project src/CustomFormService.Api/CustomFormService.Api.csproj "PostgreSql:Password" "<same-development-password-as-.env>"
dotnet user-secrets set --project src/CustomFormService.Api/CustomFormService.Api.csproj "Jwt:SigningKey" "<at-least-32-random-bytes>"

docker compose config --quiet
docker compose up -d
dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --launch-profile http
```

注意：

- `.env` 中的 `POSTGRES_PASSWORD` 與 User Secrets 的 `PostgreSql:Password` 要相同。
- 若更改 `.env` 的 DB、User 或 Port，也要用 User Secrets 或 OS environment variable 覆寫相對應的 `PostgreSql:*`。
- User Secrets 位於 repository 外、沒有提交到 Git，但**未加密且只適合 Development**。
- `docker compose config` 的完整輸出與 `dotnet user-secrets list` 可能顯示 Secret；驗證或貼出 log 時優先使用不輸出解析內容的 `docker compose config --quiet`。

### 6.2 從 configuration split 前的 `.env` 遷移

如果根目錄 `.env` 仍包含 `PostgreSql__*`、`Jwt__*` 或 `Cors__*`：

1. 不要提交、貼出或記錄現有 Secret 值。
2. 把原 `PostgreSql__Password` 的本機開發值設定為 User Secret `PostgreSql:Password`。
3. 把原 `Jwt__SigningKey` 設定為 User Secret `Jwt:SigningKey`。
4. 將根目錄 `.env` 改成 `.env.example` 的四個 `POSTGRES_*` 鍵；`POSTGRES_PASSWORD` 應與 API 的本機 DB password 相同。
5. 執行 `docker compose config --quiet`。若 named volume 已存在，再確認實際 database credential 是否仍是初始化時的值。

不要把舊 `.env` 匯入 PowerShell；這會重新混合兩條刻意分離的設定管線。

### 6.3 Integration Tests

`CustomFormServiceApiFactory` 預設使用 `Development`，因此基礎 application configuration 仍可讀取 Development User Secrets。測試 factory 隨後加入 in-memory configuration：

- 固定的測試用 JWT Issuer、Audience 與合成 Signing Key。
- PostgreSQL Host／Port／Database／Username 使用已設定的值，缺少時回退到本機 defaults。
- 個別測試可再以 in-memory overrides 覆寫，且 overrides 排在最後。

一般 integration tests 仍需要可連線的 PostgreSQL Password；configuration validation tests 則注入合成 password。測試用 signing key 是 deterministic fixture，不是可部署 Secret。

### 6.4 Staging／Production 與 Railway 模板

`appsettings.Staging.json` 與 `appsettings.Production.json` 目前都是 `{}`。它們不是遺漏：兩者繼承 `appsettings.json` 的非秘密 defaults 與 `AccessTokenCookie:Secure=true`，再由部署平台提供環境特定值。

Railway service variables 的最小模板如下：

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:${{ PORT }}
PostgreSql__Host=...
PostgreSql__Port=5432
PostgreSql__Database=...
PostgreSql__Username=...
PostgreSql__Password=...
Jwt__SigningKey=...
Cors__AllowedOrigins__0=https://app.example.com
```

`${{ PORT }}` 是 Railway 同一 service 變數的 reference syntax；`${PORT}` 是 shell／Compose 風格，不應假設 Railway Variables UI 會以相同方式展開。若使用其他平台，依該平台的變數引用語法產生等價的 `ASPNETCORE_URLS` 值。

部署注意事項：

- Staging 與 Production 必須使用不同 Secret，且不得重用 Development 值。
- Password、Signing Key、Token 與 provider credential 應使用平台 secret／sealed variable 能力。
- 正式 CORS origin 要換成真實 HTTPS frontend origin；不得使用 wildcard。
- Production Cookie Secure 必須保持 `true`。
- 此處是設定模板，不代表 Railway、公開 domain、reverse proxy、HTTPS 或 forwarded headers 已完成線上驗證；正式部署屬另外的 implementation plan。

## 7. 啟動驗證與失敗模式

API 對 `PostgreSqlOptions`、JWT、Password Hashing、CORS 與 Cookie 設定使用 `ValidateOnStart()`。錯誤設定應在 application startup 失敗，而不是延遲到第一個 request。

| 症狀 | 最可能原因 | 處理方向 |
| --- | --- | --- |
| Compose 報 `POSTGRES_DB must be set` | `.env` 缺少新制 `POSTGRES_*`，常見於 split 前的舊檔 | 依第 6.2 節遷移，不要把 `PostgreSql__*` 當 Compose 鍵 |
| API 報 PostgreSQL password 未設定或仍是 placeholder | `.env` 不會被 API 讀取，或 User Secret 未設定 | 設定 `PostgreSql:Password` User Secret／`PostgreSql__Password` deployment variable |
| API 報 JWT signing key 無效 | Signing Key 缺少、少於 32 UTF-8 bytes 或仍是 placeholder | 由適當 secret provider 提供新的隨機值 |
| API 與 DB authentication failed | Compose 與 API 的 password 不一致，或 existing volume 仍保留舊 credential | 比對兩條設定管線；保留資料時不要直接刪 volume |
| Production／Staging 因 Cookie Secure 失敗 | `AccessTokenCookie__Secure=false` 覆寫了安全預設 | 移除該覆寫或設為 `true` |
| Browser 沒有 CORS allow headers | Origin 未列入、格式不 canonical，或陣列索引錯誤 | 使用完整且精確的 HTTPS origin 與連續索引 |
| Railway app 沒有監聽預期 port | `ASPNETCORE_URLS` 沒有引用平台 `PORT`，或引用語法錯誤 | 檢查平台解析後的變數與實際 listening log |

## 8. 2026-09-30 稽核結論

### 8.1 已對齊項目

- `05-v1-system-constraints.md` 要求的 Secret／非 Secret 分工、Development User Secrets、部署 environment variables、`.env` 僅供 Compose 等規則，與目前實作一致。
- Committed appsettings 沒有 DB password、JWT signing key、token 或 provider credential。
- PostgreSQL、JWT、password hashing、CORS 與 Cookie 的關鍵條件均採 startup validation。
- Development 與非 Development 的 Cookie Secure、CORS、OpenAPI 與 HTTPS redirection 邊界已有程式與部分 integration tests。

### 8.2 本機狀態落差

本次只讀取本機 `.env` 的**鍵名**，沒有讀取或記錄值。檢查結果顯示它仍使用 split 前的 `PostgreSql__*` 與 `Jwt__SigningKey`，沒有目前 Compose 必要的 `POSTGRES_*`。因此直接執行：

```powershell
docker compose config --quiet
```

會在 interpolation 階段回報缺少 `POSTGRES_DB`。這是被 `.gitignore` 排除的本機檔案漂移，不是 tracked source 的錯誤；本次文件工作不覆寫該檔案，使用者應依第 6.2 節自行遷移。

### 8.3 文件與驗證缺口

- 原 README 的 Railway 範例使用 `${PORT}`，容易與 Railway Variables 的 `${{ PORT }}` reference syntax 混淆；本次已在 README 與本文件校正並標示尚未做正式部署驗證。
- 現有 configuration tests 已驗證 JWT lifetime／Signing Key、PostgreSQL password placeholder、非 Development Cookie Secure、CORS canonical format 與 Development CORS behavior。
- 尚未有直接自動化測試覆蓋 PostgreSQL Host／Port／Database／Username 的每個 validation、JWT Issuer／Audience、PasswordHashing 下限、真實 OS environment provider precedence、Railway port binding 與正式 HTTPS。這些是後續測試／部署工作的候選，不在本次純文件變更中增補。

## 9. 安全護欄

- 不提交 `.env`、User Secrets、database password、JWT signing key、token 或 provider credential。
- 不在 issue、commit message、測試輸出、shell transcript 或文件範例貼真實 Secret。
- `.env.example` 與程式碼內只能出現明確 placeholder 或合成 test fixture。
- Error、log 與 trace 不得包含 connection string、credential、JWT、password hash 或敏感 request body。
- CORS 不是 CSRF protection；若部署改成 cross-site、SameSite=None、iframe 或不受信任的 same-site subdomain，必須重新做 Cookie／CSRF security review。
- 正式部署必須使用 HTTPS；平台內部 HTTP bind 不等於公開連線可以不用 HTTPS。

## 10. 參考資料

Repository：

- [`05-v1-system-constraints.md`](../specs/05-v1-system-constraints.md) §3～5、§20
- [`.env.example`](../../.env.example)
- [`compose.yaml`](../../compose.yaml)
- [`Program.cs`](../../src/CustomFormService.Api/Program.cs)
- [`appsettings.json`](../../src/CustomFormService.Api/appsettings.json) 與 [`appsettings.Development.json`](../../src/CustomFormService.Api/appsettings.Development.json)
- [`launchSettings.json`](../../src/CustomFormService.Api/Properties/launchSettings.json)
- [`ConfigurationTests.cs`](../../tests/CustomFormService.IntegrationTests/ConfigurationTests.cs) 與 [`EngineeringEndpointsTests.cs`](../../tests/CustomFormService.IntegrationTests/EngineeringEndpointsTests.cs)

官方文件：

- [ASP.NET Core configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-10.0)
- [ASP.NET Core runtime environments](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/environments?view=aspnetcore-10.0)
- [ASP.NET Core development secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0)
- [Docker Compose variable interpolation](https://docs.docker.com/compose/how-tos/environment-variables/variable-interpolation/)
- [Docker Official Image — PostgreSQL environment variables](https://github.com/docker-library/docs/blob/master/postgres/content.md#environment-variables)
- [Railway variables](https://docs.railway.com/variables)
