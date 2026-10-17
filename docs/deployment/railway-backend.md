# Railway Backend 部署操作

適用：2026-10-17 的 Email／Password 後端部署補件。GitHub：`eddieco-cpu/custom-form-service`，分支 `master`。不含前端、F10、G1 或 R11 重新驗收；本機自測通過不代表 Railway 真實 HTTPS 已通過。

## Railway UI 設定

在同一 Railway Project／Environment 建立 PostgreSQL 17 與 GitHub 後端服務。以下將資料庫服務命名為 `Postgres`；若實際名稱不同，使用 Variables UI 選擇實際服務的 Reference。保留資料庫持久 Volume，無須開啟 Public Access／TCP Proxy。

| 後端欄位 | 設定 |
| --- | --- |
| Repository／Branch | `eddieco-cpu/custom-form-service`／`master`；確認部署最新 Push 的 SHA |
| Root Directory | `/`（Repository root） |
| Builder／Dockerfile Path | Dockerfile／`Dockerfile`；不要改成 API 子目錄 |
| Build Command | 留空，使用 Dockerfile |
| Start Command | `dotnet /app/CustomFormService.Api.dll`；與映像 CMD 相同，也可不覆寫 CMD |
| Pre-deploy Command | `dotnet /app/CustomFormService.Api.dll --migrate` |
| Pre-deploy Timeout | 建議 `300` 秒；失敗或超時先修復，不跳過 Migration |
| Healthcheck Path | `/health/ready` |
| Healthcheck Timeout | `300` 秒 |
| Variables：`PORT` | `8080` |
| Networking：Public Domain Target Port | `8080` |

映像固定監聽 `http://[::]:8080`，同時支援 IPv6 與 IPv4；不展開 `$PORT`，不使用 Development launch profile。`PORT` 與 Target Port 都必須設為 `8080`，以符合 Railway 路由及 Healthcheck。正式公開網址一律使用 Railway 產生的實際 `https://` 網址，不將 HTTP 登入請求交給 301 轉向。[Dockerfile](https://docs.railway.com/builds/dockerfiles)、[Healthcheck](https://docs.railway.com/deployments/healthchecks)、[HTTP 轉向行為](https://docs.railway.com/networking/troubleshooting/405-method-not-allowed)。

## 後端服務環境變數

| 名稱 | 值／用途 | 機密 |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production`；映像已有此預設，UI 明列可方便核對 | 否 |
| `ASPNETCORE_URLS` | 映像已設 `http://[::]:8080`；不需另設，若覆寫必須保持一致 | 否 |
| `PORT` | `8080`；Railway Healthcheck 使用的埠 | 否 |
| `ReverseProxy__HttpsRedirectionEnabled` | **`false`**；由 Railway Edge 負責公開 HTTP → HTTPS，容器及 Healthcheck 使用內部 HTTP | 否 |
| `PostgreSql__Host` | `${{Postgres.RAILWAY_PRIVATE_DOMAIN}}`；私有 DNS | 否 |
| `PostgreSql__Port` | `${{Postgres.PGPORT}}`；資料庫內部埠，通常為 `5432` | 否 |
| `PostgreSql__Database` | `${{Postgres.PGDATABASE}}` | 否 |
| `PostgreSql__Username` | `${{Postgres.PGUSER}}` | 否 |
| `PostgreSql__Password` | `${{Postgres.PGPASSWORD}}`；使用 Reference，不貼入文件或原始碼 | **是** |
| `Jwt__SigningKey` | 在平台安全設定獨立、隨機的正式 Signing Key，見下方格式 | **是** |
| `AccessTokenCookie__Secure` | 正式值必須是 `true`；程式共享預設已是 true，可在 UI 明列 | 否 |

程式不自動讀 `DATABASE_URL` 或 Compose `.env`。必須映射上述五個 `PostgreSql__*` 鍵；Migration 與 API 使用相同設定、Npgsql enum mapping。Private DNS 只在相同 Project／Environment 使用，無需公開資料庫。[PostgreSQL 變數](https://docs.railway.com/databases/postgresql)、[Reference 語法](https://docs.railway.com/variables/reference)、[私有網路](https://docs.railway.com/networking/private-networking)。

`Jwt__SigningKey` 由程式當作 **UTF-8 原始字串** 使用，至少 32 bytes；不是 Base64 解碼後再使用。建議在安全環境產生至少 32 個隨機 bytes，再以 Base64 字串保存（通常 44 個字元）；完整保留字串，不加入引號或換行。不要使用本機、測試或文件中的密碼／Signing Key；本次沒有產生正式機密。

其他非秘密設定可沿用共享預設：`Jwt__Issuer=custom-form-service`、`Jwt__Audience=custom-form-service-client`、`Jwt__LifetimeMinutes=1440`、`PasswordHashing__IterationCount=210000`。若另行覆寫，API 與 Pre-deploy 使用同一服務變數。通常不需設定 `DOTNET_ENVIRONMENT` 或 `ASPNETCORE_HTTP_PORTS`；避免多套監聽／環境設定互相矛盾。

## TLS、Proxy、Healthcheck 與 CORS

Railway Edge 處理公開 TLS，並提供 `X-Forwarded-Proto=https`；所查官方文件未提供可直接使用的固定 ingress proxy IP allowlist。本方案不猜測其網段，也不清空 ASP.NET Core 的代理信任限制。[Railway request headers](https://docs.railway.com/networking/public-networking/specs-and-limits)。

應用在最前方執行 Forwarded Headers Middleware，只處理 `X-Forwarded-Proto`、最多一跳。保留框架 loopback 信任，其他來源必須透過可選的 `ReverseProxy__KnownProxies__0`、`__1` 等逐一提供**經查證的精確代理 IP**；不接受 CIDR、未指定地址或任意來源。未列入的代理 Header 被忽略，不更改 Scheme、Host 或 Client IP。不要設定 `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`／`DOTNET_FORWARDEDHEADERS_ENABLED=true`／`ForwardedHeaders_Enabled=true`；程式拒絕這類無限制自動轉送模式。[Microsoft 代理設定](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)。

**這次 Railway 設定不需要填入 KnownProxies。** 容器看到的未知代理請求 Scheme 仍是 HTTP；目前 Email／Password API 沒有依 Scheme 產生外部回呼 URL，Cookie Secure 由明確設定控制。因此以 `ReverseProxy__HttpsRedirectionEnabled=false` 避免依不可信 Header 做重複 HTTPS redirect，正式 HTTPS 仍由公開 Edge 保證。若未來有經確認的代理 IP，可加入 allowlist，Scheme 才會由可信 Header 還原；不要把本機測試 gateway IP 複製到 Railway。未來 G1 等需要外部回呼 URL 的工作必須重新驗證代理拓撲。

其他環境的 `HttpsRedirectionEnabled` 預設 true，使用 443 轉向；Development 不轉向。精確的 `/health`、`/health/ready` HTTP 路徑不受轉向影響，避免 Railway 內部探測被 307 阻擋。`/health` 回 `{"status":"healthy"}`；`/health/ready` 回 `Healthy` 且確認 DB 可連線。**Readiness 沒有檢查 Migration history**，不能取代 Pre-deploy。若未來限制 `AllowedHosts`，須包含實際 API hostname 及 `healthcheck.railway.app`，不要把該 hostname 當代理 IP 信任依據。[Healthcheck hostname](https://docs.railway.com/deployments/healthchecks)。

前端尚未部署：**不新增任何 `Cors__AllowedOrigins__N`**，共享清單保持空陣列，不放虛構網域，不開 wildcard。後續前端完成後，才設定 `Cors__AllowedOrigins__0` 為其真正的 canonical HTTPS Origin，無尾斜線、path 或 query；使用 Cookie 的請求須帶 credentials。維持 HttpOnly、Secure、SameSite=Lax、Path `/`、Host-only，Login／Logout scope 不變。前端與 API 需採受信任的 same-site HTTPS 拓撲；跨 site 或 SameSite=None 不屬本次授權。

## Migration 與映像內容

Docker 使用官方 `sdk:10.0.400`，符合 `global.json` 的 `10.0.400`／`latestPatch`；最終使用官方 `aspnet:10.0`，以非 root 的 `app` 執行。明確 restore／publish `src/CustomFormService.Api/CustomFormService.Api.csproj`，最終只複製 publish 輸出（含診斷符號），沒有 SDK、原始碼、測試、docs、`.env`、機密或建置快取。這次取得的 Runtime 為 `10.0.13`；`10.0` tag 後續會隨官方 servicing 更新。

Migration 工具就是最終映像內的同一 API assembly 之 `--migrate` 模式，與 API 共用 `ApplicationDbContext`、設定驗證和七種 enum mapping。它建立 scope、執行 `Database.MigrateAsync()` 後退出，不啟動 HTTP listener；成功為 0、失敗為 1，避免輸出可能含 Credential 的例外內容。使用既有 `20260928122537_InitialV1Schema`，不另建 Migration、不用 EnsureCreated、不刪庫。一般 API 啟動不執行 Migration，Docker build 也不連 DB。

Railway Pre-deploy 在獨立容器執行、可使用服務變數與私有網路；非零退出阻止部署。本次以新的隔離 DB 實測從零與重跑，保留七張 Domain Table、七種 PostgreSQL enum 與一筆 Migration history。不要在舊 API 已連到尚無 enum 的空 DB 後，才補跑首次 Migration；請照 **DB ready → Pre-deploy 成功 → 新 API 啟動** 的順序。[Railway Pre-deploy](https://docs.railway.com/deployments/pre-deploy-command)。

## 本機自測結果與下一步

2026-10-17 實跑：

| 檢查 | 結果 |
| --- | --- |
| `docker build --tag custom-form-service:railway-prep-20261017 .` | PASS；正式多階段映像成功 |
| `dotnet build CustomFormService.sln --configuration Release --artifacts-path artifacts/railway-prep/build --no-restore` | PASS；0 warnings／0 errors |
| Proxy／Configuration 焦點測試（命令如下） | PASS；34／34 |
| 隔離 Docker Smoke `artifacts/railway-prep/smoke.py` | PASS；25／25（含資源清理） |
| PostgreSQL 17.11、首次 Migration、重跑、壞連線 | 七表／七 enums／唯一既有 history 正常；重跑不改 columns／types／history；壞連線非零退出 |
| Production `8080`、Health／Ready、Register／Login／Cookie users/me／Logout | PASS；Cookie flags／scope 正常；使用手動 HTTP client，未模擬瀏覽器 TLS |
| Proxy／HTTPS／CORS | PASS；HTTP health 不轉向；未知代理偽造 Header 不生效；可信代理 https 不循環轉向、http 轉向；空 CORS 無 allow headers |
| Runtime 與資料安全 | 無 SDK／測試／來源樹／`.env`；本次容器與網路已清理，DB 使用 tmpfs，未碰開發 DB 或 Volume |

```powershell
dotnet test tests/CustomFormService.IntegrationTests/CustomFormService.IntegrationTests.csproj --configuration Release --artifacts-path artifacts/railway-prep/build --no-restore --filter 'FullyQualifiedName~ReverseProxyTests|FullyQualifiedName~ConfigurationTests' --logger 'trx;LogFileName=deployment-focused-final.trx' --results-directory artifacts/railway-prep/tests
```

最初 Debug build 遇既有執行程序鎖住 DLL，改用獨立輸出，不停止使用者程序。新 Proxy 測試首次的空 CORS fixture 設定有誤，修正後 34／34。首次 Smoke 在預先啟動的 API 容器上執行 Migration 後 Auth 未通過；改成與 Railway 一致的先 Migration、再新 API 順序後 25／25。初次與最終結果均留在 ignored 的 `artifacts/railway-prep/`，未將機密、Cookie、JWT 或登入 body 寫入報告；未重跑完整 420 項或記錄 R11 新驗收結果。

Chrome 端依序完成：建立並確認 PostgreSQL 17／持久 Volume → 連接 GitHub master 並確認最新 SHA → 填入表列設定與 Reference、私密 JWT Key → 確認 Pre-deploy exit 0 → Healthcheck 200 → 產生後端公開 Domain，Target Port 8080 → 驗證真實 HTTPS 下的兩個 Health 路徑及最小 Auth／Cookie／Logout。正式 DNS、TLS、Railway 私有 DB 連線與實際 Edge 行為尚未在 Railway 驗證；瀏覽器跨網域 Cookie／前端整合維持未驗證，待真正前端上線後處理。

來源核對時本機為 `82b47ed`，遠端 master 為 `7591633`，領先一筆 Hygiene；本次提交與 Push 應一併送出它，最終 SHA 以交付訊息與 Railway 的 GitHub source SHA 核對。原有未追蹤 `SNAPSHOT.md` 保持原狀。既有 Hygiene 報告中的合成 fixture 密碼／作者 Email 歷史註記未修改，未加入任何正式機密或改寫歷史。
