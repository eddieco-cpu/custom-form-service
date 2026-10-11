# R11 — A 階段整合／本機驗收／交接精簡計畫

2026-10-11（Asia/Taipei）；**test plan pending owner review**。本次只 Prepare，未啟動 API／Compose、未 restore／build／tests、未送 HTTP、未套 migration／修改資料或產品程式。成對請求：[R11_Manual_Tests.http](R11_Manual_Tests.http)。

R10 已依 Owner 本 chat「R10 驗收通過」確認 accepted；結果提交 `3125924`，確認提交 `3e9168d`。R11 執行仍須目前成對計畫 review／Owner 核准及明確 Execute 授權；交接文件補件或測試／產品修正另走授權的開發階段，不能在 Execute 中默默實作。**G1 pending；本次不開始前端或部署，不標記 A-stage／Backend V1 accepted。**

## 1. 精簡範圍與順序

| 步驟 | 規劃成果 |
| --- | --- |
| 01～02 | 核准版本／舊資料 baseline；乾淨 source snapshot 的 restore／非增量 build；同次產物可追溯 |
| 03～05 | 專用空 DB 從零 migrations；Schema 核對；完整 suite；Compose＋兩個本次原生 API 的 health／readiness |
| 06～10 | 兩名新合成會員，真 HTTP＋Cookie：註冊→登入→空草稿→五題型編輯→發布→multiple 填答→Latest／distinct Count→Close→anonymous 統計；三 visibility 代表情境 |
| 11～13 | single／identified／public、目前名稱、重試、Soft Delete 邊界、兩人 Logout |
| 14～15 | DB／GET 無寫入／資料保留／清理；交接文件清單、known gaps、完成 gate |

共 **49 HTTP＋6 非 HTTP（01／02／03／04／14／15）**。用兩張表單，不重建各 Round 全部驗收矩陣。主流程五題型都必須實際建立／提交／核對，少量拒絕請求穿插驗證跨功能界線；併發、Unicode、deadline、fault、零資料、完整可見性矩陣由同次 full suite 承接。

依據：[AGENTS.md](../../AGENTS.md)、[acceptance skill](../../.agents/skills/v1-acceptance-testing/SKILL.md)、[總計畫 R11／§2～3／§6](../implementation/V1_Backend_Implementation_Plan.md)。正式規格：00 §4～9；01 FORM／SUB／STA／CON；02 §3～15；03a §2～9；03b §3～8；03c **v1.1 §3～8**；04 §2～12；05 §3～15／§20。已核對八份正式規格；本次未發現無法同時滿足的正式契約衝突。

## 2. 身份、測資與操作約定

- 核准後才產生唯一 `runId=r11-acceptance-<yyyymmdd>-<unique>`，僅小寫英數／hyphen。EvidenceRoot=`artifacts/r11-acceptance/<runId>/` 必須原本不存在。重跑另取 RunId，保留失敗與合法資料，不盲重送 POST。
- A=`r11-a-<runId>@example.test`（Owner，也填 M）、B=`r11-b-<runId>@example.test`（Respondent）。只有兩個新會員；每次 AI／人工各用新 RunId。密碼本機產生10～128 code points，只供合成會員。
- M：multiple／anonymous／owner_only→respondents→public；deadline=null。07b 加五個 optional Questions（Text／Number／Single／Multiple／Rating，sortOrder=10／20／30／40／50）。Choice各兩Options（yes/no、one/two，10/20），Rating=0..2；description均null。
- S：single／identified／public，一個 optional Text，deadline=null。具名完整五題型及零票細節沿用 full suite；S不另重寫五題型矩陣。
- API：本次新 DLL、既有開發 DB=`http://localhost:5012`；同DLL、隔離DB=`http://localhost:5013`。預選 DB port=55432，先確認未佔用且不在Windows排除範圍；若不適用先記新非秘密port於manifest／工作副本。不得停止其他API或使用其listener代填。
- Companion精確定義body／順序。IDs按Question.type與Option.value辨認，不能憑Array index；A/B/User／Form／Response IDs、時間、證據路徑記入非秘密manifest。
- 每請求 `@no-cookie-jar`，只送指定身份的 `Cookie: access_token=...`；實際runner可用兩個隔離Cookie容器／記憶體token，禁用共用自動Cookie污染匿名請求。全程使用系統Login所發Cookie，不能用test factory固定JWT或Bearer取代。Logout後清除容器，13c不重送舊token（Logout並不撤銷JWT）。
- token／password僅在記憶體或ignored本機工作副本，交付恢復placeholder；login evidence遮蔽password／Set-Cookie token，只記非秘密flags。核對HttpOnly、SameSite=Lax、Path=/、無Domain／Max-Age、Development Secure=false、Expires=JWT exp、Body無token。
- 每案記status／raw body／非秘密headers／UTC時間／assertion及DB checkpoint。AI與人工分欄；前置失敗只將依賴案BLOCKED。已觀察的FAIL保留，新Run不得改寫舊結果。

## 3. 非 HTTP 01～04：環境、乾淨建置與空庫

### 01 — 核准基準及開發資料保護

記錄HEAD、核准成對版本／授權來源、`git status --short`、SDK／Docker／Compose版本、既有container／volume／API PID／port與非秘密DB host/database/username。候選產品基準為 `a4f9402336a4eecdc07c72e92ef09234a4d5e976`；`git diff a4f9402 <approved-head> -- src tests docs/specs` 目前應無差異，執行時必須讀實際diff。未review的source／tests／specs差異先停止受影響驗收。

一般HTTP沿用本機開發DB，保留fixtures。確認其migration狀態已為目前版本，**不在開發DB套migration或重建**。僅在必要時以既有設定啟動postgres service，`docker compose config --quiet`／`docker compose up -d postgres`／`docker compose ps`；不輸出展開後secret config。確認healthy、實際host binding與TCP可連線。開始前、suite後、全部HTTP後依14的七表完整列digest／Migration比較；與使用者協調DB無外部寫入的視窗，不revert他人變更。

### 02 — clean restore／build（全部命令尚未執行）

以**核准HEAD的git archive**在新的ignored RunDir建立沒有bin／obj的source snapshot；不刪原工作目錄產物／舊證據，不使用reset／git clean。snapshot不是新產品修改；git archive只取核准內容，原工作目錄未提交變更仍保留，不能把未review修改悄悄加入測試。

在repo執行（先驗證RunId、EvidenceRoot／snapshot絕對路徑都在repo的artifacts/r11-acceptance內；已存在則換RunId）：

```powershell
git archive --format=tar --output=<EvidenceRoot>/source.tar <approved-head>
# 建立新的空 <EvidenceRoot>/source 目錄後：
tar -xf <EvidenceRoot>/source.tar -C <EvidenceRoot>/source
# 切換到上述 snapshot root，逐命令保存stdout/stderr及exit，前一步失敗即停依賴：
dotnet tool restore
dotnet restore CustomFormService.sln --force --no-cache
dotnet build CustomFormService.sln --no-restore --no-incremental --configuration Debug --disable-build-servers -m:1
```

預期各exit=0，build 0 errors；warnings如有需逐項記錄與評估，不能引用R10的0 warnings代填。記snapshot文件清單／hash、API及test DLL／PDB／deps／runtimeconfig的SHA256、build起訖；DLL必須由此snapshot新建，Portable PDB的repo source checksum對應核准版（generated文件另核對本次obj）。suite和兩個native PID都只用本次產物，不能抄舊bin；Windows 0x800711C7若復發照實BLOCKED，不改安全政策或以舊產物／TestServer代稱native PASS。

### 03 — 隔離空DB、從零 migrations／Schema

**重要：**根 `compose.yaml` 的 `volumes.postgres_data.name=custom-form-service-postgres-data` 是固定名稱，**只用 `-p` 不會隔離volume**。在RunDir建立本次專用env及override工作檔，不修改根compose／.env／User Secrets。子程序的POSTGRES_*全部明確覆寫（shell環境優先於env-file），避免繼承開發設定；secret不顯示或寫進命令。

隔離參數：project=`<runId>-empty`、DB/user=`r11_empty`、volume=`<runId>-postgres-data`、loopback port=55432（依02前核對）。專用.env填四個POSTGRES_*；password自行產生，與本次子程序PostgreSql__Password相同。volume override只有：

```yaml
volumes:
  postgres_data:
    name: <runId>-postgres-data
```

所有隔離命令都帶同一完整scope：

```powershell
docker compose -p <runId>-empty --env-file <RunDir>/empty.env -f <snapshot>/compose.yaml -f <RunDir>/empty.override.yaml config --quiet
docker compose -p <runId>-empty --env-file <RunDir>/empty.env -f <snapshot>/compose.yaml -f <RunDir>/empty.override.yaml up -d postgres
docker compose -p <runId>-empty --env-file <RunDir>/empty.env -f <snapshot>/compose.yaml -f <RunDir>/empty.override.yaml ps
```

執行前以記憶體解析merged config（不存／顯示password）及Docker inventory確認volume名稱不是開發volume、尚不存在／未被舊Run使用、port不與開發重疊；執行後用container labels、Mounts、NetworkSettings.Ports核對project／實際volume／loopback bind。任何不一致即BLOCKED，禁止migration。

容器healthy後，以該 scope的 `exec -T postgres psql -v ON_ERROR_STOP=1 -U r11_empty -d r11_empty` 讀 `current_database/current_user`、UTF8／timezone、public tables與Migration History（前置應無7表且無History；不能僅以零資料宣稱空Schema）。在snapshot root，以**子程序環境**設定Development、PostgreSql__Host/Port/Database/Username/Password、獨立Jwt__SigningKey；覆寫所有連線鍵，不fallback開發DB。先核對非秘密target，再執行：

```powershell
dotnet ef database update --project src/CustomFormService.Api/CustomFormService.Api.csproj --startup-project src/CustomFormService.Api/CustomFormService.Api.csproj --configuration Debug --no-build
dotnet ef migrations list --project src/CustomFormService.Api/CustomFormService.Api.csproj --startup-project src/CustomFormService.Api/CustomFormService.Api.csproj --configuration Debug --no-build
```

預期目前唯一Migration `20260928122537_InitialV1Schema` 成功記錄，7 Domain Tables（含user_identities）＋EF History，無seed；再跑一次update為no-op。保存Schema catalog：`information_schema.columns`的udt/type/length/null/default、`pg_enum`、PK/FK/unique/check定義、index及ON DELETE／UPDATE，對照02 §3～14；無額外業務unique／cascade、只有Question→Options cascade。04的既有DatabaseSchemaTests再核對已知Schema，不以手建表／EnsureCreated代替migration。schema/default等若未被現有test斷言，依catalog逐欄補查證據即可，不在本次加測試程式。

### 04 — 完整回歸及本次原生啟動

同snapshot／同DLL、同隔離DB子程序環境，執行一次full：

```powershell
dotnet test CustomFormService.sln --no-build --configuration Debug --logger "trx;LogFileName=r11-full.trx" --results-directory <EvidenceRoot>/full
```

目前基準420 cases（R10 43）；核對**實際**discovered/executed／0 failed／0 skipped、TRX及assembly hash，0 tests不是PASS。數量變動須列原因／覆蓋，不能填歷史420。suite自己的精確fixture cleanup後，隔離7表再次全空／migration不變，開發DB全列baseline不變。04 full承接下方§6，不額外重跑focused或native race。

在snapshot API project目錄，以 `Start-Process dotnet -ArgumentList @('exec','<本次API DLL>') -Environment <子程序設定> -WindowStyle Hidden` 開兩個本次PID：5013使用隔離DB／獨立JWT key；5012使用既有Development設定／User Secrets連開發DB。不持久覆寫OS／根設定；Development環境確定一致，執行者記錄每個child有效非秘密DB target。有限30秒等待／記退出碼；核對PID command line絕對DLL、hash、開始時間／listeners，再送05a～d。不把容器healthy代替API readiness。01～04前置失敗依賴案BLOCKED；14清理仍必須做。

## 4. 49 HTTP（body／身份與先後依 companion）

所有成功資源直接回傳，無success/data Envelope；User僅id/email/displayName/avatarUrl；FormDetail依03b §3.5完整13欄、Question／Option完整且排序；Receipt僅id/formId/submittedAt/answers、Answer僅questionId/value。Error僅04 Envelope；核對status／top-level code／detail code/path，不將message或details順序當穩定契約；拒絕／GET均查無業務寫入。

| 編號／操作 | 預期結果 | AI 實際結果 | 人工實際結果 |
| --- | --- | --- | --- |
| 05a 隔離空庫 API health | 200；{"status":"healthy"} | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 05b 隔離空庫 API readiness | 200；Healthy；實際連線隔離 DB | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 05c 開發 DB API health | 200；{"status":"healthy"} | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 05d 開發 DB API readiness | 200；Healthy；記錄 PID／port／DB 對應 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06a 註冊 A | 201 User；A id；無 access_token Cookie | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06b 註冊 B | 201 User；B id；無 access_token Cookie | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06c 註冊不自動登入 | 401 AUTH_REQUIRED；Error Envelope | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06d 登入 A | 200 User.id=A；Set-Cookie flags／expiry 依 §2；保存 aToken 於記憶體 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06e 登入 B | 200 User.id=B；同 Cookie 契約；保存 bToken 於記憶體 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06f 確認本人 B | 200 User.id=B；只四欄 User | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 07a 建立空草稿 M | 201 Draft；title trim；questions=[]；multiple／anonymous／owner_only；保存 mainFormId | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 07b 編輯五題型草稿 | 200 完整 FormDetail；title 已編輯、description=null；排序10..50、Options10/20；按 type／Option.value保存IDs | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 07c 他人草稿存在性隱藏 | 404 FORM_NOT_FOUND | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 07d 拒絕無效 Delta 並保證原子性 | 400 FORM_DEFINITION_INVALID；detail questions[0].sortOrder／INVALID_SORT_ORDER；title／全部列與 timestamps 不變 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 08a 發布 M | 200 Published FormDetail；五題型 Definition 全部與07b相同；僅合法 lifecycle／updatedAt改變 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 08b 會員讀取 Published | 200 完整五題型 FormDetail；不混入Responses／Count／Statistics | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 08c Owner 修改發布定義被拒絕 | 409 FORM_NOT_EDITABLE；資料不變 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 08d 非 Owner 優先授權拒絕 | 403 FORM_ACCESS_DENIED；先於FORM_NOT_EDITABLE | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 08e Published 不提供統計 | 409 FORM_STATISTICS_NOT_AVAILABLE | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 09a B 第一份五題型回覆 | 201 Receipt；排序／trim後Text=Old；五Answers；保存oldResponseId／submittedAt | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 09b B 最新整份略過 optional Text | 201 新Receipt；只有四Answers；submittedAt嚴格晚於09a；保存latestResponseId | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 09c A 也提交五題型 | 201 Receipt；五Answers；Text=Alpha；保存aResponseId | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 09d B 最新整份 | 200；id=latestResponseId；只四題且不回填Old／Text；與09b Receipt相等 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 09e Owner 填答人數 | 200；僅count=2，非3 Responses | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 09f 非 Owner Count拒絕 | 403 FORM_ACCESS_DENIED | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 09g Owner List count對齊 | 200；只有M、pagination 1/100/1/1；M.count=2；FormSummary shape | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 10a 關閉 M | 200 Closed FormDetail；Definition保持凍結、deadline=null | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 10b Closed Submit 優先狀態拒絕 | 409 FORM_NOT_SUBMITTABLE，先於null Answer驗證；無新Response／Answer | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 10c Owner 匿名統計 | 200；完整§5預期分布／九欄Root／anonymous完全省略respondents | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 10d owner_only 拒絕 Respondent | 403 STATISTICS_ACCESS_DENIED | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 10e owner_only 要求登入 | 401 AUTH_REQUIRED | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 10f 改 respondents | 200 FormDetail；Closed／anonymous不變；visibility=respondents | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 10g Respondent 匿名統計 | 200；分布同10c，Root.resultVisibility=respondents；無respondents | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 10h 改 public | 200 FormDetail；visibility=public；Definition不變 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 10i Public 壞Cookie視匿名 | 200；分布同10c，Root.resultVisibility=public；不要求先讀FormDetail | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 11a 建立 single／identified S | 201 Draft；保存singleFormId／qIdentified；single／identified／public；一個optional Text | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 11b 發布 S | 200 Published FormDetail | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 11c B single 提交 | 201；唯一一Response／一Answer；保存singleResponseId；Text=Identified | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 11d single 重試拒絕 | 409 RESPONSE_ALREADY_SUBMITTED；無追加資料 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 11e 提交後改 B 目前名稱 | 200 User.id=B、displayName=R11 B Updated；只改本次B | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 11f 關閉 S | 200 Closed FormDetail；原singleResponse保留 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 11g Public具名統計 | 200；九欄Root、single/public、count=1；Text.count=1，Item Identified:1；respondents恰[{id:B,displayName:R11 B Updated}] | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 12a Soft Delete S | 204無Body；只deleted_at／updated_at合法變更，status=closed；子列保留 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 12b 刪除後 Latest | 404 FORM_NOT_FOUND（非RESPONSE_NOT_FOUND） | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 12c 刪除後 Count | 404 FORM_NOT_FOUND | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 12d 刪除後 Public Statistics | 404 FORM_NOT_FOUND；先於Auth | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 13a A 登出 | 204無Body；Cookie Scope同Login且失效；清除A本機Cookie | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 13b B 登出 | 204無Body；Cookie失效；清除B本機Cookie | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 13c B 登出後本人查詢 | 401 AUTH_REQUIRED；以已清除的B Cookie容器發送，不能重送保存token | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

## 5. 統計的明確預期（10c／10g／10i／11g）

M有三份歷史Responses／14 Answers，但最新有效為B的09b與A的09c，Root=count=2。B舊Text Old／number9／rating1均不能混入。按Text／Number／Single／Multiple／Rating回五題，Question counts=**1／2／2／2／2**。

| 題型 | 完整items／count |
| --- | --- |
| Text | Alpha:1；無Old |
| Number | 數值1:2（1與1.0同組；JSON Number） |
| Single | yes:1、no:1，optionId對齊保存Option |
| Multiple | one:2、two:1；加總3>Question count2合法 |
| Rating | 0:1、1:0、2:1（零票必出現） |

成功Root恰九欄：formId/title/description/creatorDisplayName/responseMode/resultVisibility/submissionDeadlineAt/count/questions；M title=`R11 main edited <runId>`、description=null、Creator=R11 A、mode=multiple、deadline=null、visibility依案更新。Question恰questionId/title/description/type/count/statistics六欄；Choice Item為optionId/label/value/count，其他為value/count。**M所有Item（含Owner）完全省略respondents，不是null或[]，無身分替代欄位。** Questions／Items完整不分頁。

S Root title=`R11 single <runId>`、Creator=R11 A、single／public、description/deadline=null、count=1；一個Text Question count1，Item value=Identified、count1、respondents只有B id及目前名R11 B Updated，count=名單長度，不回email／avatar／Identity。identified Item因此為value/count/respondents三欄，Root不增加responseIdentity／userId等欄位。

## 6. 承接有效證據與補驗（不重複矩陣）

歷史證據支持核准基準與選案，**不代填R11實際PASS**。src/tests/specs目前與a4f9402相同；R10 -02同次420 PASS（結果3125924）、局部repair產品e22db98及review7e10d71是有效來源。R4/R6/R8 accepted、R5 Owner accepted但AI case缺口保留；R7/R9結果可參照但Owner pending。執行時04必須產生新TRX／必要SQL stdout，未跑到的承接仍是未證實。

| 領域／正式依據 | 承接04既有tests／證據 | R11需要補驗 |
| --- | --- | --- |
| Auth、Cookie/CORS、Unicode；03a §2/5、03b §4/5、05 §4/11 | AuthUserEndpointsTests、ConfigurationTests、EngineeringEndpointsTests：密碼／JWT／disabled／Bearer拒絕／壞token Logout、正式Secure、origin／credentials及Unicode | 06／13原生兩身份真Cookie、Register不登入；不宣稱Browser UI或正式HTTPS實測 |
| DB；02 §3～14、05 §3/7 | DatabaseSchemaTests三方法的columns/enums/keys/checks/index/delete | 03空庫從零實際migration及額外catalog default核對；R0～R3原驗收紀錄缺失不能用歷史結案填補 |
| Draft／Lifecycle／Errors；03b §6～8、04 §4/6～8 | CandidateContract/Safety、OptionPersistence、Lifecycle/Review、DeadlinePrecision；id-only/type清理／no-op／原子fault／strict deadline；LocalRepair F-01～03及S-01三barrier | 07～08串接五題型、無效Delta原子拒絕與Auth→Lifecycle優先；repair已review可結案，不重演舊失敗矩陣 |
| Submission；03c §4、05 §8～10 | Submission／AnswerValidator／Atomicity／Concurrency：Required/type/null/Option歸屬、真partial write／fault、single waiter／Submit Close Delete／單次Time | 09／11真HTTP multiple歷史、single成功與重試、Close拒絕；不加產品hook/native race |
| Latest／Count／Soft Delete；03c §5/6、05 §13/14 | ResponseQueries的整份／UUID平手、無回答／歷史disabled、四組真SELECT快照；既有各端點Deleted邊界 | 09 Latest與List/Count去重對齊；12 Latest/Count/Statistics Deleted補驗；Detail/List/Submit刪除矩陣沿用full |
| Statistics；03c v1.1 §3.4/7/8、04 §4.3/10、05 §5.3/13 | R10 43 rows：完整角色×狀態×visibility、bad/expired Cookie、zero-data、精確大數、四組授權／metadata快照、identity/privacy全題型 | 10三種visibility代表情境與五題型精確分布；11 single具名目前名稱；只兩會員，respondents的「第三Active非respondent」由full承接 |

RV-I-01 Publish barrier fresh GET完整頂層DTO assertion未補強：04的paused V0／兩個非Publish fresh V1完整證據承接，**不宣稱Publish fresh V1已逐欄比對**；08a/b順序GET核對五題型全DTO不是並行證據。I-02 List count/items同快照是非阻擋建議，規格未要求List snapshot，不新增契約或修產品。

15須建立簡短端點級coverage索引：03a的18端點中Email A-stage **17支**分別指到本計畫案／04具名test及結果；Google一支明列G1 pending。核對04 Catalog的各端點code／detail／early return對應既有tests；沒找到證據的條目明列缺口，不能以full總PASS推論未覆蓋。不窮舉23 codes×18 endpoints；Google errors／真Provider留G1。

## 7. 非 HTTP 14 — DB證據、保留與清理

七表使用同一RepeatableRead READ ONLY交易，逐ID保存 `md5(row_to_json(t)::text)` 並按ID排序（含timestamp／JSONB／password_hash只進digest，不輸出原值）；Migration行另記。用相同方法比較01前後、04後、HTTP各GET／拒絕checkpoint及最終baseline。開發資料所有舊IDs/digests不變；只允許本次新A/B／M/S的合法delta。若有外部寫入先記影響，不擅自還原。

唯讀SQL按本次實際UUID綁定（不拼接外來字串）：以responses `DISTINCT ON(user_id) ... ORDER BY user_id, submitted_at DESC, id DESC` 選M有效整份，JOIN answers對照05的分布；M3 Responses/14 Answers，S1 Response/1 Answer；A/B users2、forms2、questions6、options4（user_identities本次新增0）。核對Receipt與JSONB、choices為Option IDs；S.deleted_at非null且status=closed，Question／Response／Answer仍存在；M Closed/public未刪。08／10／11的Definition Freeze與07d／11d拒絕前後完整列／timestamps相同。GET無寫入以每組GET前後全七表digest證明，合法PATCH／DELETE獨立記帳。

即使HTTP BLOCKED也清理：只停止command line/hash驗明屬本次的5012/5013 PID；child環境自行釋放，工作shell若暫改env需finally精確復原；保持原服務／User Secrets／根.env不變。清除工作副本credentials／Cookie jars與含秘密的empty.env，保留無秘密override／catalog／TRX／raw body／manifest及失敗歷史。**開發DB新增合成fixtures全部保留，不刪會員或歷史答卷。**

隔離container可用同一完整scope `stop postgres` 停止，**本次計畫預設保留隔離volume供review，不執行down -v**；manifest記資源名稱及停機結果。後續若Owner另指示丟棄，先確認project labels及精確volume只屬本Run再使用同scope清理；禁止對regular project或custom-form-service-postgres-data做刪除。完成隔離停止不代表刪資料，交接需明示保留位置。

## 8. 非 HTTP 15 — 必要文件補件、known gaps與完成條件

目前只盤點／列補件，**尚未修改README／設定／舊整合計畫／測試**。下列補件須在後續授權的文件／開發階段完成並review，再執行當時有效計畫：

| 檔案／項目 | 現有具體依據與必要補件／完成條件 |
| --- | --- |
| README.md（I-01） | 開頭仍R3～R5、最後Current boundaries把PATCH／Lifecycle／Submission／Statistics列未實作；補R6～R10現況／17端點索引、完整Email→統計操作、A-stage名稱及G1／部署pending，連本計畫。R4段「Delete synthetic user」與AGENTS資料保留護欄不一致，改為保留驗收fixtures，不新增User Delete能力 |
| .env.example／appsettings*.json／configuration guide | 現.env僅4個Compose鍵、Development有非秘密DB／CORS／Secure=false、共用Secure=true、Staging/Production={}是正常覆寫設計；不因空檔判缺陷。需補API child-env非秘密placeholder操作範例、連線鍵對齊／優先序、隔離volume override、readiness host binding及data保護；Secret不進Git，無需要產品設定變更的證據 |
| docs/test-plan/R0_R9_Integration_* | 未執行舊Prepare，source基準6f61a16／306 cases，仍把F-01～03與S-01當未修復。不能當修正版有效計畫。本R11提供目前整合執行入口；後續在兩份舊檔加「歷史未執行／R11取代現況整合入口」與連結即可，保留原內容，不重寫72-case矩陣或偽造結果 |
| 手動驗收／交接 | 本paired files是可執行指引；補新RunId／Cookie隔離／動態ID辨認／fixture保留、fresh新DLL／DB scope及清理說明。15報告須逐案AI／人工分開、命令exit／證據可定位、非秘密啟動設定，另一操作者可從README重現 |
| known gaps | R7／R9 Owner confirmation仍pending；R0～R3沒有repo acceptance紀錄、R5 AI紀錄缺口，不補造。RV-I-01維持非阻擋證據改善；I-02維持未定義List快照的建議；Windows新DLL policy／TCP port排除曾復發，不能用R10成功宣稱永久解除。G1真Provider、前端、Railway／domain／正式HTTPS為後续scope |

沒有新確認產品缺陷；上述README／舊計畫是文件落差，固定Compose volume是隔離操作注意，RV-I-01／I-02是既有非阻擋限制。若執行發現實際契約偏離，記確切spec檔／章節、觸發請求、actual/expected、影響；停止受影響依賴，交目前Round修正授權／development skill，不在驗收中改期望求PASS。正式規格衝突同樣只停受影響工作。

**R11完成 gate**：02新建置、03空庫migrations/Schema、04同版full、49 HTTP與14資料／清理全部有實際PASS證據；15文件補件與17端點/Error Catalog索引經review、所有FAIL/BLOCKED／未驗／限制有明確處置；再交AI報告，停於「驗收結果待 Owner 確認」。人工仍獨立記錄，Owner明確確認才標R11 accepted。**A-stage accepted另須R0～R11必要證據及Owner狀態齊備；R7／R9 pending與早期證據缺口必須由Owner處理／確認，不因R11技術PASS自動結案。** G1未完成不得標Backend V1 accepted或Product V1 released。

## 9. 非 HTTP 實際結果與本次交付

| 編號 | AI 實際結果 | 人工實際結果 |
| --- | --- | --- |
| 01 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 02 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 03 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 04 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 14 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 15 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

本次 Prepare 文件自查：PowerShell 靜態解析確認49請求編號／順序／預期與Markdown一致、19 JSON bodies可解析、18變數均有定義、55案的110個AI／人工結果欄均初始化、6非HTTP編號／本機文件連結有效；五題型及4 Responses／15 Answers測資計數一致。`git diff --check` 通過，`git diff a4f9402 -- src tests docs/specs` 無輸出。這些是文件檢查，不是build／suite／HTTP或Schema驗收PASS。

本次只執行只讀文件／repo盤點與文件靜態核對，並同步R10 Owner確認及R11 Prepare進度；提交與Git狀態於交付訊息記錄。**停止於「R11 計畫待 review」；不是本計畫已獲准或已執行。**
