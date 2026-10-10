# R10 — Statistics 正式驗收計畫

狀態：**R10 驗收計畫待 review**。2026-10-10 僅 Prepare，未執行 restore／build／測試／API／DB 操作，R10 未 accepted。成對請求：[R10_Manual_Tests.http](R10_Manual_Tests.http)。核准本計畫與另行授權 Execute 後才執行；人工結果與 AI 結果分開，開發或 review PASS 不代填。

## 1. 依據、目前進度與界線

依 [AGENTS.md](../../AGENTS.md)、[acceptance skill](../../.agents/skills/v1-acceptance-testing/SKILL.md)、[總計畫 R10](../implementation/V1_Backend_Implementation_Plan.md)、[核准實作計畫](../implementation/R10_Implementation_Plan.md)（06853f3）、[開發報告](../implementation/R10_Development_Report.md)（產品／tests a4f9402336a4eecdc07c72e92ef09234a4d5e976，報告 838a7a7）及 [獨立 review](../implementation/R10_Independent_Code_Review_Report.md)（8c79e09）編製。Owner 已確認 review 無阻擋缺陷或新增非阻擋 findings；R10 正式／人工驗收尚未執行。

正式來源為 [00 Overview](../specs/00-v1-overview.md)、[01 Domain](../specs/01-v1-domain-specification.md)（SUB／STA、§3.3）、[02 DB](../specs/02-v1-database-specification.md)、[03a Common](../specs/03a-v1-api-contract-common.md)（§5～6）、[03b Auth/User/Form](../specs/03b-v1-api-contract-auth-user-form.md)、[03c Submission/Statistics](../specs/03c-v1-api-contract-submission-statistics.md)（v1.1 已核准，§3.4～3.5、§7～8）、[04 Errors](../specs/04-v1-error-catalog.md)（§4.3／§10）及 [05 Constraints](../specs/05-v1-system-constraints.md)（§5.3、§10～15／§20）。

**必須保留的歷史限制**：獨立 review 的新 BaseOutputPath build 成功、source／PDB 核對一致，但新 DLL 載入遭 Windows 應用程式控制政策阻擋（0x800711C7），未執行該新產物測試。review 43/43 與 420/420 PASS 使用原位置既有、經 source／PDB 重新核對的產物；不是該次新 build runtime PASS。本計畫不改寫此限制。開發報告首次失敗／校正、局部修正歷史、舊 FAIL／BLOCKED、artifacts 與既有 DB fixtures 全部保留。

R10 前局部修正獨立 review 已完成，見 [局部 review](../implementation/R0_R9_Pre_R10_Local_Repair_Review_Report.md)；RV-I-01 是非阻擋的 Publish fresh GET 頂層 DTO 證據改善，尚未補強。R7／R9 Owner pending 與 I-01／I-02 等既有事項不推定通過；此次不結案其他 Round。不新增產品 hook、schema／Migration、驗收修復、R11／G1 工作。

原生請求 **45 個（其中 18 個 Statistics 請求，其餘為啟動檢查及測資操作）**，非 HTTP 步驟 **01／02／15**，嚴格依 01 → 01a/b → 02 → 03～14 → 15 順序。原生矩陣驗證主要邊界及實際 JSON；細部 JWT、數值等值／極值、同 timestamp tie-break、disabled／missing User、四組真實快照競爭由 02 同次新 build suite 承接，不重做原生 race 矩陣。

## 2. 共用變數、測資與紀錄方式

計畫 RunId：`r10-acceptance-20261010-01`，apiBase：`http://localhost:5011`（避免沿用舊 5000 API）。每次 AI／人工執行或重驗使用新的未用 RunId，兩檔同步 RunId／emails／證據目錄；不得重送非冪等 POST 或重用舊 JWT／UUID 取得假成功。若變更案例語意／測資／請求／預期，重新交 Owner review。

| 變數 | 初始值／取得方式 |
| --- | --- |
| ownerEmail / respondentEmail / otherEmail | r10-owner-r10-acceptance-20261010-01@example.test / r10-respondent-r10-acceptance-20261010-01@example.test / r10-other-r10-acceptance-20261010-01@example.test；01 DB 證明尚不存在 |
| testPassword | 本機生成 10～128 字元隨機密碼，只在記憶體或受保護本機操作暫存；文件保持 placeholder |
| ownerId / respondentId / otherId | 03a/c/e 回應 id，03b/d/f 登入再核對；三者不同 |
| ownerToken / respondentToken / otherToken | 03b/d/f Set-Cookie 的 access_token 值；只在本機使用，不記錄可用 token |
| missingFormId | 00000000-0000-4000-8000-000000000010；01 DB 證明不存在（含 deleted）；碰撞先換未用 UUID 並同步兩檔 |
| futureDeadline | 2036-10-10T00:00:00.123456Z；必須晚於執行／Publish／Submit 時間，保留 DB 微秒精度 |
| mainFormId / identifiedFormId / zeroFormId | 04a/b/c 的新 Form id，不用既有資料 |
| qMain* / qIdentified* | 04a/b 按 type／title 取得 Text、Number、Single、Multiple、Rating Question id |
| oMain* / oIdentified* | 04a/b 按 Question＋Option 業務 value 取得 SingleYes/No、MultipleOne/Two Option id |
| 五個 Response IDs | 07a～e 保存 mainOldResponseId、mainLatestResponseId、mainOwnerResponseId、identifiedOwnerResponseId、identifiedRespondentResponseId |
| H / H2 | 100000000000000000000000000000000000001 / 100000000000000000000000000000000000002；HTTP Body 必須直接放原始 JSON number，不透過 JavaScript Number／double／decimal 重建 |

A 為 Owner，也提交零 Answer 及 identified 答卷；B 是 Respondent；C 是 Active、未提交的 Other。03 建立三個新會員；12 只更新本次 A／B 目前 displayName。

04 三張表 title 分別 `R10 main {{runId}}`、`R10 identified {{runId}}`、`R10 zero {{runId}}`，description=null。main：multiple／anonymous／owner_only／futureDeadline；identified 與 zero：single／identified／public／deadline=null。所有 Question 可選答（省略 required 時預設 false）、description=null；建立 JSON 刻意反序，統計須按 sortOrder 回傳：

| 題名／type | sortOrder | Config／Options（建立 Body 反序；統計按以下順序） |
| --- | --- | --- |
| Text / text | 10 | 無 config |
| Number / number | 20 | 無 config |
| Single / single_choice | 30 | Yes：value=yes／sortOrder=10；No：value=no／sortOrder=20 |
| Multiple / multiple_choice | 40 | One：value=one／sortOrder=10；Two：value=two／sortOrder=20 |
| Rating / rating | 50 | min=0、max=2，統計必須包含 0／1／2 |

提交的 Choice value 是 **Option ID**；統計 Item.value 是業務 value，不能混淆。07a 的 Text ` Old ` 與 07d 的 ` A ` 保存後為 Old／A。

| 編號 | Form／User | Text | Number | Single | Multiple | Rating | Answers |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 07a | main／B 舊 | Old | 1e0 | No | One | 2 | 5 |
| 07b | main／B 新 | **完全缺題** | H | Yes | One、Two | 1 | 4 |
| 07c | main／A | 無 | 無 | 無 | 無 | 無 | 0 |
| 07d | identified／A | A | H | Yes | One、Two | 2 | 5 |
| 07e | identified／B | a | H2 | No | One | 0 | 5 |

07b 前確認服務時間已前進；Receipt／15 SQL 必須證明 submitted_at 嚴格晚於 07a。若測資沒有達成此條件，該測資 FAIL、依賴結果 BLOCKED，保留結果，重驗換新 RunId；不重送／修改歷史答卷。相同 submitted_at 的 id DESC 邊界另由 02 承接。

每次執行保存非秘密 manifest（RunId、Owner 核准版本、開始／結束 UTC、repo HEAD、product SHA、SDK／OS、指令／exit、證據路徑、fixture UUIDs、原生程序／產物身分）。逐案保存 HTTP status、必要非秘密 headers、raw Body、時間與檢查結論；login 不保存密碼／Set-Cookie。實際結果填 PASS／FAIL／BLOCKED／未執行與原因，AI／人工分開；人工不因 AI 通過而代填 PASS。

## 3. 非 HTTP 01 — 本次 restore／build、產物核對、原生啟動與 baseline

以下僅是核准後的 Execute 指令。各 stage 分別保存 stdout／stderr／exit code；前階段失敗立即停止依賴 stage，不用最後一條指令的 exit 代替前面結果。

1. 檢查 git status，記錄 HEAD 及 Owner 核准版本。候選 source/tests/specs 應與 a4f9402 相同；`git diff a4f9402336a4eecdc07c72e92ef09234a4d5e976 -- src tests docs/specs` 應無未 review 的差異（不能只看 exit 0）。保護所有原有未提交變更。確認本機 SDK 符合 global.json、Development User Secrets／DB binding 有效；只記非秘密 host／port／database／username，不顯示 secrets 或 .env 內容。
2. 使用原有 local 開發 PostgreSQL；確認 Compose container healthy、127.0.0.1 host binding 可用、既有 schema／migration 與 02 正式規格一致。必要時只啟動既有 postgres service（`docker compose up -d postgres`），禁止 down -v、刪 volume、rebuild、Migration、重置／刪除資料。既有其他服務不停止。
3. 按 §7 的完整列 digest SQL 取得 before-02 七表／migration baseline；確認新 emails／RunId titles／missingFormId 未存在。記錄既有 container／API PID／ports。5011 若被非本次程序佔用，01 BLOCKED，不把該 listener 當成新 API。建立**原本不存在**的 EvidenceRoot，不能覆寫先前執行結果。
4. 本次 restore／build 使用新輸出目錄；所有命令同一 repo，build 不用舊 bin。下面的錯誤分支及輸出記錄為必要流程，實際執行者把每次 exit／時間寫入 manifest：

```powershell
$RepoRoot = (Resolve-Path 'C:/Users/user/Desktop/custom-form-service').Path
$RunId = 'r10-acceptance-20261010-01'
$EvidenceRoot = Join-Path $RepoRoot "artifacts/r10-acceptance/$RunId"
if (Test-Path -LiteralPath $EvidenceRoot) { throw 'RunId already used; preserve evidence and choose a new RunId in both files.' }
New-Item -ItemType Directory -Path $EvidenceRoot | Out-Null
$BuildRoot = (Join-Path $EvidenceRoot 'build').Replace('\', '/') + '/'
Set-Location -LiteralPath $RepoRoot
dotnet restore CustomFormService.sln *> (Join-Path $EvidenceRoot 'restore.log')
$RestoreExit = $LASTEXITCODE
if ($RestoreExit -ne 0) { throw "01 restore failed: $RestoreExit" }
dotnet build CustomFormService.sln --no-restore --no-incremental --configuration Debug --disable-build-servers -m:1 "-p:BaseOutputPath=$BuildRoot" *> (Join-Path $EvidenceRoot 'build.log')
$BuildExit = $LASTEXITCODE
if ($BuildExit -ne 0) { throw "01 build failed: $BuildExit" }
$ApiDll = Join-Path $BuildRoot 'Debug/net10.0/CustomFormService.Api.dll'
$TestDll = Join-Path $BuildRoot 'Debug/net10.0/CustomFormService.IntegrationTests.dll'
if (!(Test-Path -LiteralPath $ApiDll) -or !(Test-Path -LiteralPath $TestDll)) { throw '01 expected new artifacts missing' }
```

5. build 預期 0 errors，warnings 如有須記錄／評估，不能套用歷史「0 warnings」。記錄 build 起訖、API／test DLL 絕對路徑／SHA256、deps.json／runtimeconfig.json／PDB hash 及 SDK；確認 artifacts 位於本次 BuildRoot、建立時間在本次 build，沒有從 src/bin／tests/bin 或舊 artifacts 搬入。讀兩份 Portable PDB 的 Document 表，依各自 HashAlgorithm（SHA256／SHA1）重新計算對應 repo source；每個非 generated 文件都須 match，generated 文件另核對本次 obj；記錄核對清單／數量（review 的 78 是歷史數量，不能代填本次）。無法確認 source／PDB／產物身分則 01 BLOCKED。
6. **原生啟動只能執行上述 $ApiDll**，不是 dotnet run、TestServer、Docker SDK 或舊 native API。使用 PowerShell 7 Start-Process 子程序環境避免改全域設定；既有 Development User Secrets 沿用，不把 key/password 放命令列：

```powershell
$ApiWorkingDirectory = Join-Path $RepoRoot 'src/CustomFormService.Api'
$ApiProcess = Start-Process -FilePath 'dotnet' -ArgumentList @('exec', ('"' + $ApiDll + '"')) -WorkingDirectory $ApiWorkingDirectory -Environment @{ ASPNETCORE_ENVIRONMENT = 'Development'; DOTNET_ENVIRONMENT = 'Development'; ASPNETCORE_URLS = 'http://localhost:5011' } -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $EvidenceRoot 'native.stdout.log') -RedirectStandardError (Join-Path $EvidenceRoot 'native.stderr.log')
# 有限最多 30 秒等候此 PID 的 5011 listener；不在這裡發出未編號 HTTP。
# 每次 Refresh、檢查 HasExited；早退記錄 ExitCode、stderr 和實際 Windows policy 原因。
```

7. listener 必須屬於 $ApiProcess.Id，記錄 PID／開始時間／CIM CommandLine 指向新 $ApiDll、工作目錄／Development／URL；確認程序開始於本次 build 結束後且 DLL hash 未改。出現未知 PID、無法核對或 30 秒未就緒即 BLOCKED；只關閉本人且已核對的程序，不動舊 API。核對後才發 01a health／01b readiness；兩者 HTTP 200 且 readiness Healthy 才可開放後續原生案例。若 readiness 503，記錄 FAIL 與 DB binding 原因，不取舊服務的成功填入。

**政策阻擋處理**：若新 DLL 仍被 Windows 政策阻擋（例如 0x800711C7），保存該次 exit／stderr／可取得的事件證據，01 記 BLOCKED。未送出的 01a/b 及依賴 03～14 都記 BLOCKED，不虛構 HTTP status。restore/build 成功、舊 source／PDB 已核對或既有 API health PASS 均不能解鎖此 gate。不默默改用舊產物、不以 Linux／TestServer PASS 代替 Windows 新 build native runtime；不自行放寬 Windows 政策。02 可以獨立嘗試**同次新 BuildRoot**的測試，但不能解除原生 gate；15 仍須完成可執行的保留／清理並列未完成項。

## 4. 成功 JSON 的完整斷言（對應 09d／10b／11b／12c/d／13a）

所有成功 Statistics raw Body 必須是 Object，Root **恰有九欄位**：formId、title、description、creatorDisplayName、responseMode、resultVisibility、submissionDeadlineAt、count、questions。description=null 必須存在；deadline=null 也必須存在。不要用重建 DTO／格式化輸出取代實際 wire JSON。禁止 Root creatorId／userId／status／responseIdentity／原始答卷／email 或額外 mapping；Creator 名稱可公開，Respondent 身分依 identity。

questions 完整五題，依 Text／Number／Single／Multiple／Rating（sortOrder 10～50）；每題恰有 questionId、title、description、type、count、statistics 六欄位，description=null，無 config／required／sortOrder。Choice Item 為 optionId／label／業務 value／count；Text、Number、Rating Item 為 value／count。identified 才另有 respondents，Anonymous 完全省略該 Property（不是 null 或 []），Owner 同樣遵守。arrays 完整、不分頁。

| 結果 | Root metadata／count | 每題 count | 預期 Items（按回傳順序） |
| --- | --- | --- | --- |
| main：09d／10b／11b／12d | main ID／title，multiple、表定 futureDeadline；Creator 09～11=R10 Owner，12d=R10 Owner Updated；visibility 依案例 owner_only → respondents → public；count=2 | 0／1／1／1／1 | Text=[]；Number H:1；Single Yes:1、No:0；Multiple One:1、Two:1；Rating 0:0、1:1、2:0 |
| identified：12c | identified ID／title，R10 Owner Updated、single、public、deadline=null；count=2 | 全部 2 | Text A:1、a:1；Number H:1、H2:1；Single Yes:1、No:1；Multiple One:2、Two:1；Rating 0:1、1:0、2:1 |
| zero：13a | zero ID／title，R10 Owner Updated、single、public、deadline=null；count=0 | 全部 0 | Text/Number=[]；Single Yes:0、No:0；Multiple One:0、Two:0；Rating 0:0、1:0、2:0 |

main B 的舊 Old／1／No／Rating2 不能補入最新缺題答卷；A 最新零 Answer 仍計 Root，Root=2 不等於 Response rows=3。main multi Item count 加總 2 > Question count 1；identified 加總 3 > 2，各 Item 內同一 User 不重複。Choice 零票仍回正確 Option id／label／value。

identified 12c 的命中名單：Text A、Number H、Single Yes、Multiple Two、Rating2 為 A；Text a、Number H2、Single No、Rating0 為 B；Multiple One 為 A、B（依 R10 Owner Updated → R10 Respondent Updated），Rating1 為 []。每個 Respondent **只有 id／displayName**，使用目前名而非提交名；item.count=respondents.length。zero 所有 Choice／Rating Item respondents=[]。anonymous 成功含 Owner 的 09d 全面檢查無 respondents、email／avatar／Provider／身分替代欄位。

Number values H／H2 的 ValueKind 為 Number，兩個值精確分開、按數值 ASC，不能轉字串或捨入。保存 raw Body，以 System.Text.Json JsonElement.GetRawText 或 Python json.load(parse_float=Decimal, parse_int=int) 等無損方式核對；不可透過 JavaScript JSON.parse 的 Number 或先轉 double／decimal。可正規化 exponent 做精確值比較，不承諾原 token spelling。Rating 0～2 為 JSON Integer。main deadline 解析為 UTC 後必須等於 DB 保存微秒 `2036-10-10T00:00:00.123456Z`，不是 Close 時間或查詢時點。

所有失敗回應依 03a／04：只回標準 Error Envelope（root error）；核對 error.code／HTTP status，message 不逐字比對。05a 至少包含 detail path=formId、code=INVALID_VALUE，不限定 details 順序；除 05a 明定的 validation details 外，其他錯誤可省略 details，但不可 details=null。不洩漏 Root／Answers／PII／SQL／JWT。

## 5. 按順序的 HTTP 案例與分開的實際結果

請求 Body／Cookie 的唯一操作範本在 companion；此表同編號／順序／身份／預期，實際請求不得沿用 cookie jar。每個 request 都使用 `@no-cookie-jar`，先 Clear All Cookies；空身份完全不送 Cookie，bad 固定 `access_token=not-a-jwt`；只靠明確 access_token Cookie，不能用 Bearer Header 認證。預期錯誤是成功的驗收斷言，不阻斷下一個獨立案例；失敗只停止依賴它的步驟，保留已執行結果。

03～14 共用 gate：01／01a／01b／02 PASS，當前所需 fixtures、授權與 lifecycle 前置成立。POST/PATCH/DELETE 的成功仍須核對 Receipt／DB（§7）。只讀 GET 前後 snapshot 見 15；禁止為了結果變更預期。

| 編號 | 請求／身份 | HTTP 預期與斷言 | AI 實際結果 | 人工實際結果 |
| --- | --- | --- | --- | --- |
| 01a 本次產物原生 health | GET `/health`；無 Cookie | 200；{"status":"healthy"}；必須是 01 已確認的本次新 DLL 程序 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 01b 本次產物原生 readiness | GET `/health/ready`；無 Cookie | 200；Healthy；本次新 API 可讀本機開發 PostgreSQL | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 03a 註冊 owner | POST `/api/v1/auth/register`；無 Cookie | 201；Active 合成 User；記錄 ownerId，email={{ownerEmail}}；不可保存 password/hash | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 03b 登入 owner | POST `/api/v1/auth/login`；無 Cookie | 200；User ID 與 03a 相同；Set-Cookie access_token，值僅放本機 ownerToken，Body 無 token | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 03c 註冊 respondent | POST `/api/v1/auth/register`；無 Cookie | 201；Active 合成 User；記錄 respondentId，email={{respondentEmail}}；不可保存 password/hash | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 03d 登入 respondent | POST `/api/v1/auth/login`；無 Cookie | 200；User ID 與 03c 相同；Set-Cookie access_token，值僅放本機 respondentToken，Body 無 token | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 03e 註冊 other | POST `/api/v1/auth/register`；無 Cookie | 201；Active 合成 User；記錄 otherId，email={{otherEmail}}；不可保存 password/hash | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 03f 登入 other | POST `/api/v1/auth/login`；無 Cookie | 200；User ID 與 03e 相同；Set-Cookie access_token，值僅放本機 otherToken，Body 無 token | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 04a 建立 main 五題型 Draft | POST `/api/v1/forms`；A Cookie | 201；Draft FormDetail；Owner A；記錄 mainFormId／全部 Question、Option IDs；五題與四 Options；測資依 §共用變數 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 04b 建立 identified 五題型 Draft | POST `/api/v1/forms`；A Cookie | 201；Draft FormDetail；Owner A；記錄 identifiedFormId／全部 Question、Option IDs；五題與四 Options；測資依 §共用變數 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 04c 建立 zero 五題型 Draft | POST `/api/v1/forms`；A Cookie | 201；Draft FormDetail；Owner A；記錄 zeroFormId／全部 Question、Option IDs；五題與四 Options；測資依 §共用變數 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 05a UUID 優先於 invalid auth | GET `/api/v1/forms/not-a-uuid/statistics`；bad Cookie | 400；VALIDATION_ERROR；至少一個 detail path=formId、code=INVALID_VALUE；Error Envelope | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 05b missing Form 優先於 invalid auth | GET `/api/v1/forms/{{missingFormId}}/statistics`；bad Cookie | 404；FORM_NOT_FOUND；01 已以 DB 確認該 UUID 不存在 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 05c Draft Active Owner 得知未開放 | GET `/api/v1/forms/{{mainFormId}}/statistics`；A Cookie | 409；FORM_STATISTICS_NOT_AVAILABLE | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 05d Draft 非 Owner 存在性保護 | GET `/api/v1/forms/{{mainFormId}}/statistics`；C Cookie | 404；FORM_NOT_FOUND | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 05e Draft bad Cookie 存在性保護 | GET `/api/v1/forms/{{mainFormId}}/statistics`；bad Cookie | 404；FORM_NOT_FOUND | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06a Publish main | POST `/api/v1/forms/{{mainFormId}}/publish`；A Cookie | 200；FormDetail.status=published；不送 Body／Content-Type | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06b Publish identified | POST `/api/v1/forms/{{identifiedFormId}}/publish`；A Cookie | 200；FormDetail.status=published；不送 Body／Content-Type | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06c Publish zero | POST `/api/v1/forms/{{zeroFormId}}/publish`；A Cookie | 200；FormDetail.status=published；不送 Body／Content-Type | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06d Published 其他 Active User 未開放 public | GET `/api/v1/forms/{{identifiedFormId}}/statistics`；C Cookie | 409；FORM_STATISTICS_NOT_AVAILABLE；public 不提前開放 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06e Published public bad Cookie 隱藏存在性 | GET `/api/v1/forms/{{identifiedFormId}}/statistics`；bad Cookie | 404；FORM_NOT_FOUND | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 07a B 的 main 舊整份答卷 | POST `/api/v1/forms/{{mainFormId}}/responses`；B Cookie | 201；保存 Receipt；5 Answers，text=Old、number=1、single=no、multiple=one、rating=2；記錄 mainOldResponseId | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 07b B 的 main 較新整份、刻意缺 Text | POST `/api/v1/forms/{{mainFormId}}/responses`；B Cookie | 201；Receipt 4 Answers，完全沒有 Text；number=100000000000000000000000000000000000001、single=yes、multiple=one/two、rating=1；記錄 mainLatestResponseId | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 07c A 的 main 最新零 Answer | POST `/api/v1/forms/{{mainFormId}}/responses`；A Cookie | 201；Receipt answers=[]；記錄 mainOwnerResponseId；Root 仍計入 A | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 07d A 的 identified 整份 | POST `/api/v1/forms/{{identifiedFormId}}/responses`；A Cookie | 201；5 Answers；text=A、number=100000000000000000000000000000000000001、single=yes、multiple=one/two、rating=2；記錄 identifiedOwnerResponseId | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 07e B 的 identified 整份相近大數 | POST `/api/v1/forms/{{identifiedFormId}}/responses`；B Cookie | 201；5 Answers；text=a、number=100000000000000000000000000000000000002、single=no、multiple=one、rating=0；記錄 identifiedRespondentResponseId | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 08a Close main | POST `/api/v1/forms/{{mainFormId}}/close`；A Cookie | 200；FormDetail.status=closed；未到期提前 Close，表定 deadline 保持；不送 Body／Content-Type | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 08b Close identified | POST `/api/v1/forms/{{identifiedFormId}}/close`；A Cookie | 200；FormDetail.status=closed；deadline=null；不送 Body／Content-Type | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 08c Close zero | POST `/api/v1/forms/{{zeroFormId}}/close`；A Cookie | 200；FormDetail.status=closed；deadline=null；不送 Body／Content-Type | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 09a Closed private 沒 Cookie | GET `/api/v1/forms/{{mainFormId}}/statistics`；無 Cookie | 401；AUTH_REQUIRED | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 09b Closed private invalid Cookie | GET `/api/v1/forms/{{mainFormId}}/statistics`；bad Cookie | 401；AUTH_TOKEN_INVALID | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 09c owner_only 拒絕 Active Respondent | GET `/api/v1/forms/{{mainFormId}}/statistics`；B Cookie | 403；STATISTICS_ACCESS_DENIED | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 09d owner_only Owner 匿名統計 | GET `/api/v1/forms/{{mainFormId}}/statistics`；A Cookie | 200；完整 main 分布 §4：Root=2、Question counts=0/1/1/1/1；目前 Creator=R10 Owner、mode=multiple、visibility=owner_only、精確保存 deadline；Owner 也無 respondents | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 10a main 改 respondents | PATCH `/api/v1/forms/{{mainFormId}}/result-visibility`；A Cookie | 200；FormDetail.resultVisibility=respondents；Definition／Response 不改 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 10b respondents 允許 B | GET `/api/v1/forms/{{mainFormId}}/statistics`；B Cookie | 200；與 09d 分布相同，只有 resultVisibility=respondents；Root 四欄位仍完整，anonymous 無 respondents | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 10c respondents 拒絕未提交 C | GET `/api/v1/forms/{{mainFormId}}/statistics`；C Cookie | 403；STATISTICS_ACCESS_DENIED | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 11a main 改 public | PATCH `/api/v1/forms/{{mainFormId}}/result-visibility`；A Cookie | 200；FormDetail.resultVisibility=public | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 11b Closed public bad Cookie 直接統計 | GET `/api/v1/forms/{{mainFormId}}/statistics`；bad Cookie | 200；與 09d 分布相同，resultVisibility=public；bad Cookie 不阻擋，無 Respondent 身分 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 12a A 提交後改 Creator／Respondent 名稱 | PATCH `/api/v1/users/me`；A Cookie | 200；public User.displayName=R10 Owner Updated；只改本次 A | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 12b B 提交後改 Respondent 名稱 | PATCH `/api/v1/users/me`；B Cookie | 200；public User.displayName=R10 Respondent Updated；只改本次 B | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 12c identified public 精確分布／目前名單 | GET `/api/v1/forms/{{identifiedFormId}}/statistics`；無 Cookie | 200；完整 identified 分布 §4：Root=2、每題=2、兩個大數精確分開排序、text=A/a；每 Item respondents 僅 id/目前名稱，零票 []；四 Root 欄位與 deadline=null | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 12d anonymous Creator 改名可見 | GET `/api/v1/forms/{{mainFormId}}/statistics`；無 Cookie | 200；main 分布不變；creatorDisplayName=R10 Owner Updated；其他 Root 值與 11b 相同，anonymous 仍完全省略 respondents | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 13a Closed public identified 五題型零資料 | GET `/api/v1/forms/{{zeroFormId}}/statistics`；無 Cookie | 200；Root=0、全部 Question=0；text/number=[]；Choice 全 Options count=0；rating 0..2 全零；每 Item respondents=[]；Root 九欄位含目前 Creator／single／public／deadline=null | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 14a 只 Soft Delete 本次 main | DELETE `/api/v1/forms/{{mainFormId}}`；A Cookie | 204；無 Body；main 的 Questions／Options／Responses／Answers 保留 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 14b Deleted 優先於 invalid auth | GET `/api/v1/forms/{{mainFormId}}/statistics`；bad Cookie | 404；FORM_NOT_FOUND；不回 Root／答案 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

## 6. 非 HTTP 02 — 同次新 build 回歸、細部與快照承接

01 restore/build／manifest 成立後，用**同一 $BuildRoot**，不 rebuild、不改回預設 bin，不繼承 review 既有產物結果。01 native 若 BLOCKED，可嘗試此同次產物，但仍不解鎖原生請求。測試載入遭政策阻擋就記 02 BLOCKED／原因；不可用舊 binaries、Linux 或其他產物代填本次結果。

```powershell
$ResultsRoot = Join-Path $EvidenceRoot 'full'
dotnet test CustomFormService.sln --no-build --configuration Debug "-p:BaseOutputPath=$BuildRoot" --logger 'trx;LogFileName=r10-acceptance-full.trx' --results-directory $ResultsRoot *> (Join-Path $EvidenceRoot 'full.log')
$TestExit = $LASTEXITCODE
# 保存 exit／實際 TRX／StdOut、執行 assembly 路徑 hash；退出 0 但 0 tests 不是 PASS。
```

full 一次即可承接 R10 focused 細項，基準 **420 total，R10 43**（10 methods 的 DataRows）。核對實際 discovery／執行數／0 failed／0 skipped、不以歷史數字代填；數量差異須列原因及受影響覆蓋。TRX 或 testhost 來源須為同次新 BuildRoot，API／tests hash 與 01 manifest 一致。02 前後七表／migration 完整 baseline 必須相同；suite 對自己合成資料的精確 cleanup 不得刪既有 fixtures。

來源：[FormEndpointsTests.Statistics.cs](../../tests/CustomFormService.IntegrationTests/FormEndpointsTests.Statistics.cs)；下列對應是**待本次實際執行**，不是目前 PASS：

| 自動化 method（FullyQualifiedName~方法名） | DataRows | 承接範圍／原生案例 |
| --- | --- | --- |
| R10_LifecycleVisibilityAndCredentialPriority | 9 | Draft／Published／Closed、各政策、Active 角色／未登入／invalid Cookie；UUID／missing／deleted 優先序；05／06／09～11／14 |
| R10_ConditionalAuthUsesExistingJwtPolicy | 13 | 原有 scheme 的 issuer／audience／signature／HMAC256／expiry／零 clock skew／cookie 政策，不另寫弱化 JWT；09～11 |
| R10_BearerHeaderDoesNotAuthenticate | 1 | 不以 Bearer 取代 access_token Cookie |
| R10_ZeroDataRootAndPrivacyShapes | 6 | 三 visibility × anonymous／identified 的九欄 Root、null、zero-data／隱私；09d／13a |
| R10_FiveTypesCountsOrderingAndIdentity | 2 | 五題型、正規化、count／排序、目前 displayName／同名 ID 次排序、disabled 歷史計入、匿名／具名；09d／12c |
| R10_ExactNumberGroupingAndSortingBeyondDecimalAndDouble | 1 | 1／1.0／1e0 同組、正負零、負數／細小小數／超出 decimal/double 精度的精確等值及排序；07／12c |
| R10_EffectiveWholeResponseTieBreakAndFormScope | 1 | 每人最新整份、submitted_at DESC／id DESC、缺題／空答、Form scope；07／09d |
| R10_DeadlinePreservesStoredUtcMicrosecondsAndNeverCloses | 2 | null／微秒／表定 deadline，不因過期自動 Close；06／08／09d／13a |
| R10_InvalidPersistedAnswersReturnControlled500 | 4 | 壞保存值受控 500，不暴露內部 exception；不在原生 DB 植入 corrupt fixtures |
| R10_AuthorizationAndOutputUseOneRealSnapshot | 4 | visibility／delete／profiles／caller-disabled 寫入交錯；同快照授權、metadata、count、身份、Body一致 |

快照證據須保存四組 TRX StdOut：首個**實際 Form SELECT**建立讀快照後 writer commit，reader 釋放後仍完整 V0、fresh query 完整新狀態；同 scoped DbContext／RepeatableRead、成功路徑 9 SELECT、無 FOR UPDATE／write SQL、bounded workers 在 finally release／cancel／drain。只見 BeginTransaction 不算已建立快照。若未取得事件／SQL／完整 Body 證據，該覆蓋不能填 PASS。

Conditional Auth 必須沿用現行 JWT scheme；literal `access_token=` 被 ASP.NET Request.Cookies 視為缺 Cookie，與既有 protected route 相同回 AUTH_REQUIRED（由 02 的 empty DataRow 承接），不得另訂 Statistics 政策。

full 亦承接既有 R4 JWT／User、R8 Submission 原子性／並行、R9 latest/count／S-01 修正回歸；不重跑全部舊 HTTP 或將其他 Round 改 accepted。suite 沒有處理 RV-I-01 的額外證據建議時，仍保留未補強註記，不推定完成。

## 7. 非 HTTP 15 — DB／只讀證據、保留資料與清理（即使前置 BLOCKED 仍進行）

本節 SQL 都是 read-only，使用已核對的本機開發 DB／DBeaver／psql；記錄工具、schema、UTC。密碼由既有私密連線來源載入，不寫入文件／shell arguments／證據。沒有資料清除、schema／Migration 或 status 直改操作。所有新 UUID 先驗證為 Guid 再作 DB parameter，避免直接拼接未驗證 HTTP 資料。

### 7.1 完整 baseline／GET 無寫入

以下同一 read-only RepeatableRead transaction 的輸出，只保存 table／id／完整 row digest，不輸出 user password_hash／Identity secrets。另記各表 count（每組輸出行數）、migration ID／ProductVersion：

```sql
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT 'users' AS table_name, id::text AS row_id, md5(row_to_json(t)::text) AS digest FROM users t
UNION ALL SELECT 'user_identities', id::text, md5(row_to_json(t)::text) FROM user_identities t
UNION ALL SELECT 'forms', id::text, md5(row_to_json(t)::text) FROM forms t
UNION ALL SELECT 'questions', id::text, md5(row_to_json(t)::text) FROM questions t
UNION ALL SELECT 'options', id::text, md5(row_to_json(t)::text) FROM options t
UNION ALL SELECT 'responses', id::text, md5(row_to_json(t)::text) FROM responses t
UNION ALL SELECT 'answers', id::text, md5(row_to_json(t)::text) FROM answers t
ORDER BY table_name, row_id;
SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";
COMMIT;
```

01 保存 before-02，02 完成保存 after-02，必須逐列及 migration 相等。after-02 同時為 before-03；15 最終核對每個舊 ID 存在、digest 完全不變，不只比 count／timestamp。並精確驗證新增 ID／關聯只屬本次 manifest 的新會員／Forms。

GET-only 組前後取同 SQL完整 snapshot，預期所有當下列 digest／counts 不變：05a～e、06d/e、09a～d、10b/c（10a PATCH 後）、11b（11a 後）、12c/d（12a/b 後）、13a、14b（14a DELETE 後）。比較排除並行第三方寫入；若有人同時改 DB，先記錄並停止受影響證據，不能默默剔除不明差異。14a 的合法 Soft Delete 與 10／11／12 的合法變更不混入 GET 差異。

### 7.2 本次 fixtures SQL／對照

在下列 SQL 綁定 :mainFormId／:identifiedFormId／:zeroFormId（經 Guid 驗證的 UUID 參數）；不可全庫輸出 Answer。查詢都於同一 read-only RepeatableRead transaction，完成後 COMMIT。使用 psql 時採已驗證 UUID 的 `:'mainFormId'::uuid` 等形式，DBeaver 則使用工具的 parameter binding。

```sql
SELECT id, user_id, status, response_mode, response_identity, result_visibility,
       submission_deadline_at, deleted_at
FROM forms
WHERE id IN (:mainFormId, :identifiedFormId, :zeroFormId)
ORDER BY id;
SELECT q.form_id, q.id, q.type, q.sort_order, q.required, q.config
FROM questions q
WHERE q.form_id IN (:mainFormId, :identifiedFormId, :zeroFormId)
ORDER BY q.form_id, q.sort_order, q.id;
SELECT q.form_id, o.question_id, o.id, o.label, o.value, o.sort_order
FROM options o JOIN questions q ON q.id = o.question_id
WHERE q.form_id IN (:mainFormId, :identifiedFormId, :zeroFormId)
ORDER BY q.form_id, q.sort_order, o.sort_order, o.id;
SELECT id, form_id, user_id, submitted_at
FROM responses
WHERE form_id IN (:mainFormId, :identifiedFormId, :zeroFormId)
ORDER BY form_id, user_id, submitted_at DESC, id DESC;
SELECT r.form_id, a.response_id, a.question_id, a.value::text AS stored_json
FROM answers a JOIN responses r ON r.id = a.response_id
WHERE r.form_id IN (:mainFormId, :identifiedFormId, :zeroFormId)
ORDER BY r.form_id, a.response_id, a.question_id;
WITH ranked AS (
  SELECT r.*, row_number() OVER
    (PARTITION BY form_id, user_id ORDER BY submitted_at DESC, id DESC) AS rank
  FROM responses r
  WHERE form_id IN (:mainFormId, :identifiedFormId, :zeroFormId)
)
SELECT form_id, id AS effective_response_id, user_id, submitted_at
FROM ranked WHERE rank = 1
ORDER BY form_id, user_id;
```

核對 Receipt 07a～e 的 Response／Answer IDs、trimmed 文本、Choice ID、exact Number 保存值與 raw Body；B 的 mainLatest 是最新**整份**且 Text Answer 不存在；A mainOwner 有 Response 無 Answer。再用 Effective IDs JOIN answers 核對 §4 每題／Item 人數；zero 無 Response。12 名稱為 users 的目前值（只查本次三個 ID 的 id／display_name／status，不查密碼），對齊 Creator／identified respondent 名單。14 main.deleted_at 非 null，但 Questions／Options／Responses／Answers 全部保留，identified／zero 仍 Closed 未刪。

全部成功時本次保留 delta：**users +3、user_identities +0、forms +3、questions +15、options +12、responses +5、answers +19**；main 3 Response／9 Answer，identified 2／10，zero 0／0。main 最後 Closed／public／soft-deleted／原表定 deadline；其他兩張 Closed／public／deadline=null；三會員 Active，A/B 更新名稱。失敗／BLOCKED 時記錄實際部分 delta，不以預期取代，也不刪已合法建立的 fixtures。

### 7.3 清理、保留與停止

保留全部舊 fixtures、本次新會員／三張表／合法歷史答卷（包括 14 合法 soft-deleted main）、歷史失敗及本次非秘密 logs／TRX／manifest／raw JSON／snapshot。重驗另取 RunId，不物理刪除已建立資料。suite 只清自己的隨機測試資料。

本次未使用 DB 臨時 disable／deadline 回寫／Clock／barrier 產品 hook，不需資料復原。若執行者臨時更動本機環境配置，記錄原值並 finally 精確恢復；不回滾他人修改。不改 Windows 政策。結束或失敗時，只停止已核對屬於本次 manifest 的新 API PID，確認 listener 釋放、原有服務／DB container 保持原狀；不殺未知 PID 或舊 API。刪除本人 usable token／cookie jar／秘密暫存並恢復兩檔 placeholders，保留非秘密證據。無法完成的清理須列出，不宣稱已恢復。

## 8. 非 HTTP 結果、後續交付與已知缺口

| 編號 | 預期／證據 | AI 實際結果 | 人工實際結果 |
| --- | --- | --- | --- |
| 01 | 本次 restore/build 成功；source/PDB/新產物 manifest；七表 baseline／未用測資；native 同次 DLL PID 可核對；01a/b 另列 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 02 | 同次新產物 full 420（R10 43）及實際 TRX／SQL／快照證據；baseline 不變 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 15 | 舊列完整不變、實際新 delta／Effective SQL／GET 無寫入；合法資料保留、秘密／本人程序／暫時環境清理 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

執行後交付核准版本／RunId、實際命令與產物／程序身分、45 HTTP 與 3 非 HTTP 的 AI 結果及獨立人工欄位、SQL／同快照證據、所有 FAIL／BLOCKED／未執行／清理與保留明細。Windows 新 DLL runtime 阻擋仍未解除時明確列為限制，不把既有產物 PASS 或 TestServer 成功轉成新 native PASS。細部 JWT／相等數值／id tie-break／競爭由 02 承接，本原生計畫不重複擴張；若 02 未執行，對應覆蓋仍未證實。Google、部署／HTTPS、R11 及未結案的其他 Round 不在本計畫。

本次交付僅規劃，所有欄位待執行；**停止於「R10 驗收計畫待 review」**。
