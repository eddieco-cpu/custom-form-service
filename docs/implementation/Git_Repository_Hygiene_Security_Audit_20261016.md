# Git Repository Hygiene 與敏感資訊檢查

日期：2026-10-16（Asia/Taipei）

檢查基線：`master` / `7591633137c5a6f57c1fce8e1ba19ab394015880`

範圍：Git 追蹤狀態、Ignore Rules、本機文字與封存 Evidence、Git 物件及 Docker Build Context。未執行 Push、部署、資料庫操作或產品功能修改。

## 1. 結論與授權界線

安全的 Ignore Rules 已調整，正式規格、Implementation Plan、開發／複核／驗收報告、測試程式、Skills 與歷史紀錄均保留。沒有取消追蹤、刪除原件、改寫歷史或變更任何測試結果。

在本次可讀文字與已檢查封存成員中，未命中實際 JWT、常見平台 API Token、私鑰區塊或帶實際憑證的連線 URL；本機 `.env` 有資料庫密碼，原已被 Git 忽略。這項密碼的記憶體比對未在其他已掃描內容或 Git 文字物件中命中。

需要 Owner 決定的是 R4／R5 驗收文件中的固定／可推導合成帳號密碼，以及作者 Email 的公開隱私。部分合成帳號密碼亦存在於分支可達的歷史物件；不能把新的 Ignore Rules 當作歷史去敏。本次只記錄方案，尚未修改這些內容。未確認任何正式環境憑證外洩，也未驗證保留帳號目前是否仍可使用上述測試密碼登入。

## 2. 初始盤點

| 分類 | 檔案數 | 處理 |
| --- | ---: | --- |
| `src/` | 66 | 保留全部追蹤；正式設定與 Migration 保留 |
| `tests/` | 20 | 保留測試與合成 Fixtures |
| `docs/` | 60 | 保留規格、計畫、報告與 HTTP 驗收計畫 |
| `.agents/` | 2 | 保留兩份工作流程 Skill |
| 根目錄追蹤檔 | 8 | 包括 AGENTS、README、方案、Compose、SDK／工具設定與 `.env.example` |
| **已追蹤合計** | **156** | 沒有既有檔案被本次規則新排除 |
| 原有未追蹤 | 1 | `SNAPSHOT.md`，未修改、未代為提交；文字掃描未命中候選項 |
| 原有 ignored 檔案 | 7,390 | 約 493.3 MiB；保留本機，包含 `.env`、artifacts、編譯結果與測試輸出 |

初始 Git 狀態只有 `?? SNAPSHOT.md`。`frontend-docs/`、根目錄 `output/` 沒有檔案可列入初始清單；空目錄不受 Git 追蹤。

原有 `artifacts/` 與 `test-results/` 規則沿用，因其中混有原始執行材料與快照；沒有將它們視為無保存價值。值得公開的證據應逐檔審查後保留在 `docs/evidence/`，原始版本繼續留本機。

完整本機清單及只含位置／類別的掃描結果位於 ignored 的 `artifacts/repository-hygiene/`：`inventory.json`、`findings.json`、`summary.json`。新增稽核輸出與 Docker 匯出檔不計入上述初始數字。

## 3. `.gitignore` 調整

- 保留既有 .NET／Visual Studio、NuGet／Node 依賴、快取、編譯與測試輸出規則。
- 補上 `.env.*`、`.envrc`，並明確保留 `.env.example`、`.env.*.example`、`.env.template`、`.env.*.template`。
- 補上 `appsettings.[Ll]ocal.json`、`appsettings.*.[Ll]ocal.json`、`http-client.private.env.json`、`*.local.env`、`*.private.env`、`secrets.json`、`.secrets/`。
- 補上私密金鑰／容器副檔名 `*.p12`、`*.key`、`*.pem`、`*.snk`；原有 `*.pfx` 保留。未來刻意使用的公開或合成金鑰 Fixture 須個別審查再加入例外，勿使用實際金鑰。
- 補上 `**/output/`、PID、Python 虛擬環境／快取、本機 NuGet 快取與 Playwright 執行輸出。
- 提供 `*.private.*` 與 `**/evidence/raw/`，讓原始敏感證據可明確留在本機。

沒有整體忽略 `docs/`、`tests/`、`review/`、`evidence/`、`AGENTS.md` 或 `.agents/`。正式 `appsettings.json`、Development／Staging／Production JSON 仍保留。`.gitignore` 不會自動移除已追蹤內容。

去敏後需公開的文字證據建議使用 `.json`、`.txt` 或 Markdown；既有 `*.log` 規則仍生效。若必須保留已審查的 `.log`，另加精確單檔例外，勿整包強制加入。

## 4. 敏感資訊與待確認清單

### 4.1 保留追蹤，建議 Owner 核准後去敏

| 檔案／位置 | 發現與影響 | 建議方案 |
| --- | --- | --- |
| `docs/test-plan/R4_Acceptance_Test.md:19` | 可由 RunId 推導的本機驗收帳號密碼 | 密碼改由本機安全輸入；保留 RunId、案例、執行日期、PASS／FAIL 與原始測試事實 |
| `docs/test-plan/R4_Manual_Tests.http:18` | 相同類型的密碼組成方式 | 改由執行時的本機變數取得；保持配對 Markdown／HTTP 一致 |
| `docs/test-plan/R5_Acceptance_Test.md:17` | 固定的 R4 合成帳號登入密碼 | 改成本機輸入／明確去敏標記，保留原驗收事實與案例映射 |
| `docs/test-plan/R5_Manual_Tests.http:19` | 固定的既有合成帳號密碼 | 保留檔案追蹤，替換密碼來源；原件保存在 ignored 的本機私人副本 |
| `docs/specs/03b-v1-api-contract-auth-user-form.md:63` | 規格示例使用非保留網域 Email；未據此判定為真人資料 | 可選擇換成保留示例網域；須 Owner 核准，且不可改動 API 契約 |

**以上均未修改。沒有建議整份取消追蹤的既有文件。** 自動化測試的明確合成密碼、測試用 Signing Key、無效 Cookie、空 Cookie、Token 佔位符與示例設定保留；它們支援可重現的測試，不應當作部署憑證。

### 4.2 原始 Evidence 保留本機

| 檔案／範圍 | 判讀與建議 |
| --- | --- |
| `.env:4` | 實際本機資料庫密碼。已 ignored；不複製到報告、Docker Context 或 Git |
| `artifacts/r11-acceptance/r11-acceptance-20261011-01/source/docs/test-plan/R5_Manual_Tests.http` | 含固定驗收密碼的舊快照。保留原件，若需公開則另外製作去敏副本 |
| `artifacts/r11-acceptance/r11-acceptance-20261011-01/source.tar` | 含原始文件快照及同類驗收密碼；已在記憶體檢查文字成員，整包維持本機保存 |
| `artifacts/r8-a-review/986cfd8-full.diff`、`review-supplement-full.diff`、`R8_A_Review_Bundle.zip` | Patch／封存成員包含 Git 作者 Email。公開副本可去識別作者 Header，但保留 commit、diff 與審查結論 |
| `artifacts/r8-b/B-full.diff`、`artifacts/r8-b.zip`、其他帶作者 Header 的 `*.diff` | 同類作者隱私事項，原件不變；不要直接提交整包 Evidence |
| `test-results/R4Concurrent/users.json`、`results.json`、`Program.cs` | 本機合成帳號與執行資料；未辨識出正式憑證，但公開前應逐欄審查，必要時產出去敏副本 |
| `artifacts/**/baseline.sql`、其他 SQL／HTTP／TRX／log 輸出 | 保留原始測試證據。本次未命中實際 JWT 或密碼 Hash；公開副本仍需檢查帳號、Header、Cookie、個資及本機路徑 |

可優先整理的證據包括 `artifacts/r10-development-20261010/native-summary.json`、`native-http.jsonl` 與 `artifacts/r11-acceptance/r11-acceptance-20261011-01/` 的案例結果。若 Owner 授權去敏，另存 `docs/evidence/<round>/<run-id>/`，附原檔對照與刪改欄位清單；保留狀態碼、錯誤碼、斷言結果、時間、RunId、證據鏈及既有驗收限制。不要把去敏包裝作重新執行或驗收通過。

### 4.3 Git History 單獨風險

- 盤點 82 個可達 commits；檢查本機物件庫全部 437 個 blobs，包括 unreachable 物件。435 個文字 blobs 已掃描；另外兩個 unreachable ZIP blobs，補查 96 個文字成員。
- R4／R5 合成帳號密碼的歷史需保留風險註記。相關可達歷史包括 R4 Markdown `e12428c`、R4 HTTP `c996dd8`、R5 Markdown `9eff384`、R5 HTTP `496e097`。新的去敏 commit 無法移除舊版本。
- Git 作者／提交者有一個非保留網域 Email。這是 Git metadata，Owner 應確認願意公開；本報告不列出值。
- `origin` 為 HTTPS，檢查未發現 URL password 或 query token；未查詢遠端，也未判定歷史是否曾公開。
- `.env` 沒有出現在已查可達檔案歷史；目前本機 `.env` 密碼亦未命中 Git 文字物件。未發現需立即輪替的正式憑證，但合成密碼絕不可重用於 Railway、資料庫正式帳號或其他環境。
- 若上述合成密碼曾被重用，Owner 應先決定受影響帳號與輪替／停用方案。若要求舊歷史中也不得出現，須另行授權歷史處理及備份協調，本次不執行。
- `git count-objects -v` 顯示一個約 34.6 MiB 的 `.git/objects/14/tmp_obj_mz0GD7` 暫存垃圾；`git fsck --full` exit 0，列出 dangling objects。這些資訊不等於資料損毀；沒有執行 prune、gc 或刪除，以保留可能的復原價值。該暫存檔不是有效 Git blob，未納入內容掃描。

## 5. Docker 與 Railway 相容性

原專案沒有 `.dockerignore`、Dockerfile 或 Railway 專用建置設定。本次新增 repository-root `.dockerignore`，供後續 backend Docker 建置使用。

- 排除 Git／Agent／IDE metadata、文件與 Review／Evidence、artifacts、測試結果、bin／obj／output、依賴快取、機密設定、私鑰、本機 Compose 與手動 `.http` 請求檔。
- 保留 `global.json`、`CustomFormService.sln`、`dotnet-tools.json`、兩個 `.csproj`、全部必要原始碼、Migration 與正式 `appsettings*.json`。
- 特意保留測試專案與測試原始碼，因根方案引用該專案；後續 Dockerfile 可選擇只 publish API，且不會因 solution restore 找不到測試專案而失敗。
- Docker Engine 28.5.1 實際執行 `FROM scratch`／`COPY .` 的本機 Context 匯出，最終 **89 檔／616,895 bytes**。與預期建置清單逐檔比對，無遺漏、無額外檔案、內容無變更。`.env`、`.git`、docs、原始 Evidence 與編譯輸出均未進入匯出。

這項檢查確認 Context 規則；沒有建立正式應用映像、執行 .NET Docker publish 或部署 Railway。

後續部署計畫仍需決定：

1. 提供相容 `global.json` 之 .NET 10 SDK／runtime 的正式 Dockerfile，並確認 build root。Railway 會在設定的 source root 偵測 Dockerfile；根 `.dockerignore` 須位於實際 Context root。若日後改用其他 root 或 Dockerfile-specific ignore，需重驗。[Railway Dockerfiles](https://docs.railway.com/builds/dockerfiles)、[Docker Context](https://docs.docker.com/build/concepts/context/)。
2. 配置 production PostgreSQL、`PostgreSql__*` 與獨立 `Jwt__SigningKey` 等 Railway service variables；不要將本機 `.env`、User Secrets、測試 Signing Key 或 fixture 密碼帶入映像。現有程式使用五個 `PostgreSql__*` 鍵，不能假設直接提供 `DATABASE_URL` 就會自動綁定。
3. 確認服務監聽 Railway `PORT`，並選擇 `/health/ready` 作資料庫 readiness 候選。驗證 proxy／HTTPS、轉送標頭、Cookie Secure、CORS 與 migration 流程；現有非 Development 會啟用 HTTPS redirection，需實際驗證 healthcheck 能取得成功回應。[Railway Healthchecks](https://docs.railway.com/deployments/healthchecks)。
4. 本機 Compose 不是 Railway 的已完成部署方案；Railway 不會直接執行該 Compose 檔，需按平台流程配置服務。[Railway Compose Guide](https://docs.railway.com/guides/docker-compose)。

## 6. 執行檢查與結果

| 實際命令／操作 | 結果 |
| --- | --- |
| `git status --short`、`git ls-files`、`git ls-files --others --exclude-standard` | 初始 156 tracked／1 untracked；保留使用者的 SNAPSHOT |
| `git ls-files --others --ignored --exclude-standard` | 初始 7,390 ignored；納入本機文字盤點，沒有只檢查 tracked 檔 |
| Bundled Python 執行 `artifacts/repository-hygiene/scan_repository.py` | 最終 exit 0；2,439 個本機文字檔、ZIP 304 個文字成員；另補查 tar 154 個文字成員。報告只輸出類別與位置，沒有秘密值 |
| `git rev-list --count --all`、`git rev-list --objects --all --reflog`、`git cat-file --batch-all-objects`／`--batch` | 82 commits、437 blobs；本機所有物件納入分類，另區分可達與 unreachable |
| `git log --follow --reverse --format="%h %cs" -- <path>` | 複核 R4／R5 路徑歷史，不把密碼放進 command line |
| `git check-ignore --no-index -z --stdin` 搭配 Python assertions | 33 個正反例全部通過：機密／輸出被忽略，範本／規格／Skills／測試／去敏證據可保留 |
| `git ls-files -ci --exclude-standard` | 無輸出；沒有既有 tracked 檔案落入 Ignore Rules |
| `docker build --file artifacts/repository-hygiene/ContextProbe.Dockerfile --output type=local,dest=artifacts/repository-hygiene/docker-context-final .` | exit 0；只用 scratch，不拉取映像或執行應用，匯出內容逐檔檢查通過 |
| `git fsck --full` | exit 0；有 dangling objects，無回報 missing／corrupt objects |
| `git diff --check` | 通過 |

掃描使用本機 Python 規則，涵蓋敏感欄位、JWT、常見 provider token、私鑰、Authorization／Cookie、密碼 Hash、credential URL、SQL password、Email 與部分個資形態；並用目前 `.env` 密碼做記憶體精確比對。Gitleaks／TruffleHog 未安裝，未聲稱執行它們。初次 Python PATH alias 無法執行，已改用 bundled Python；長路徑與舊文字編碼處理後重跑完成。最初受 sandbox 限制的 Docker 查詢已透過工具權限檢查後成功重跑。

限制：不對所有任意格式秘密提供零風險保證；3,334 個生成／依賴檔跳過內容掃描，1,771 個二進位檔由本機主掃描分類略過（其中 source.tar 已另補查），其餘編譯檔沒有做反組譯。Git 內兩個二進位 ZIP 已補查文字成員，編譯成員略過。未掃描 repository 外的 User Secrets／憑證庫、資料庫或遠端 GitHub，也未驗證憑證是否仍有效。未執行產品 build／integration tests／驗收；本次沒有產品變更。

## 7. 交付與待 Owner 處理

本次提交範圍僅 `.gitignore`、`.dockerignore` 與本報告；敏感文件保留原狀。提交 SHA 及提交後 Git 狀態由本次交付訊息提供，避免報告自我引用 commit。

待 Owner：

- 核准或保留第 4.1 節四份 R4／R5 密碼文件的去敏方案；若核准，先保留 ignored 私人原件，再成對更新文件，不改驗收事實。
- 決定規格示例 Email 是否替換，以及 Git 作者 Email 是否可隨歷史公開。
- 如需公開更多原始 Evidence，指定或核准逐檔去敏與 `docs/evidence/` 副本方案；不取消追蹤整個目錄、不刪除本機原件。
- 判斷歷史合成密碼是否曾重用。輪替、帳號停用、歷史處理與 Push 均尚未執行。
- `SNAPSHOT.md` 仍是原有未追蹤資料，留待 Owner 決定是否另行提交；Railway 正式部署按獨立計畫處理。
