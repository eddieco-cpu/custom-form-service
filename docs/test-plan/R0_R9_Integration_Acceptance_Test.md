> 歷史未執行計畫：基準6f61a16／306 cases，未同步後續repair。現況整合入口已由 [R11 Markdown](R11_Acceptance_Test.md)／[R11 HTTP](R11_Manual_Tests.http) 取代；保留原內容與未執行結果，不將它當目前核准執行計畫。

# R0～R9 整合回歸驗收計畫

準備日期：2026-10-07（整體 review 自 2026-10-06 開始）。
**狀態：整體 review 完成；整合驗收計畫待 Owner review，尚未執行。**

Companion：[R0_R9_Integration_Manual_Tests.http](R0_R9_Integration_Manual_Tests.http)。
基準與 findings：[整體 review 報告](../implementation/R0_R9_Overall_Code_Review_20261006.md)，source/tests/specs基準 `6f61a160863bfacb2fd85ef3d97abca271af5e22`。
本文件是**Prepare**交付，不是R11啟動／Round accepted紀錄。依 AGENTS.md 與 [acceptance skill](../../.agents/skills/v1-acceptance-testing/SKILL.md)，目前不得送本計畫 HTTP 或執行其 DB mutations。Owner 核准目前兩檔並明確授權後才能 Execute；修正 commit 需記入執行基準，實質計畫變更需重新 review。

## 1. 範圍與判定

串接R0～R9已實作16支業務API，加工程health/readiness。覆盖會員→草稿→Definition Delta→發布與Freeze→Visibility設定→五題型Submission→Latest整份回覆→不同Respondent Count→deadline→Close→各狀態soft delete→Logout。
成功、關鍵錯誤／early return、DB原子性與資料保留各用代表案例，細部邊界與併行／fault沿用02的**既有自動測試**。本次計畫共 **72 HTTP＋5個非HTTP步驟**，HTTP全部在companion，DB／startup／cleanup由本文件管理。

不測R10 Statistics的visibility access matrix、anonymous／identified投影或結果分布；不測G1；不列尚未實作為缺陷。anonymous／identified在本計畫僅核對Definition設定／freeze、認證／內部user_id與Receipt不洩漏身分；Statistics差異留R10。不重建開發DB、不新增自動測試、不覆寫歷史R4～R9結果。
R7／R9仍待Owner確認；本計畫通過本身也不擅自改accepted狀態。

通用預期：
- 所有error採04的error Envelope、status／code／detail path；message文案不作穩定判斷，details順序不當契約。
- User只回id/email/displayName/avatarUrl；FormDetail沒有Creator email／Response／Count；Receipt只回id/formId/submittedAt/answers、Answer只有questionId/value。
- GET與no-op／拒絕請求不得寫業務資料；所有Question／Option／Answer集合完整、不隱性截斷。
- 失敗的依賴案例標BLOCKED；独立的預期拒絕案例完成並不阻止後續正常路徑。全PASS必須包含02必要承接案例，12 findings不得放寬預期。
- AI及人工實際結果每case分開填PASS／FAIL／BLOCKED／未執行＋時間／簡短證據；未觀察人工執行不得填人工PASS。

## 2. Fixtures、順序與執行記錄

執行RunId：`<待 Owner 核准後產生的新唯一值>`；測試HEAD：`<待執行>`；paired plan版本／核准來源：`<待 Owner review>`；API process／同次build／TRX：`<待執行>`。
本次review曾跑的306測試只供規劃，**不能填入本計畫的實際結果**。歷史R4～R9沿用案例需在本次RunId重跑或由02觀察到的同版tests承接。

| Fixture | 建立／身份 | 狀態與資料用途 |
| --- | --- | --- |
| A | 03a新會員Owner；email=ir-a-<runId>@example.test | active；03i改為IR A Updated；擁有M、S，自己也對M提交 |
| B | 03b新會員／M、S的Respondent；email=ir-b-<runId>@example.test | active；擁有私有Draft F；不管理A表單 |
| M | 04a A建空Draft；06b改multiple／identified | 最終五題型，Text必填；B兩份、A一份；M=3R／11A／2 Users；Close後Soft Delete |
| S | 04b A建single／anonymous、Optional Text、future deadline | B成功空回覆一次，重試失敗；暫存deadline probe必須finally精確復原；Published直接Soft Delete |
| F | 04c B建Draft、Optional Text | 真實跨Form qForeign；不Publish、不Submit；最後Draft直接Soft Delete |
| temporary tree | 06b M的第六題qTemp＋temp1/temp2 | 06e id-only刪temp1；06f id-only刪qTemp cascade temp2；不是驗收清理 |

禁止沿用舊RunId、password、token、Form/Question/Option ID。既有8位會員／22Forms等筆數只是review當下baseline，不作本次執行的固定expected；執行時另讀baseline。
新email若已存在，**換新RunId，不delete／reset舊fixtures**。A/B只有本次新Form，因此05a/b、08i的清單筆數明確；跨run資料不混用。

依01→02→03～09→10 probe＋finally→11→12→13～14→15→16→17執行，不用companion「Send All」越過non-HTTP gate。
預期拒絕案例也必須記response與DB checkpoint。12已知缺陷不作其他独立case的隱含前提；Owner可批准其BLOCKED或先記FAIL，13precondition記此安排。

## 3. HTTP使用與秘密處理

companion保留可提交的placeholders。在本機忽略的`artifacts/r0-r9-integration/<runId>/`建立**工作副本**，僅工作副本填password／token／動態IDs；RunDir用新值，已存在則停止換值。RunId使用lowercase英數及hyphen，密碼10～128 code points、本地產生，與wrongPassword不同；如果字元含引號／控制字元，用正確JSON escaping，不能直接破壞body。
`futureDeadline`在執行時產生UTC ISO8601、結尾Z，至少晚於本次預計完成24小時（微秒精度或整秒）；不固定歷史日期。M的deadline仍null。

每request使用`@no-cookie-jar`且只送明確identity欄指定Cookie；工具不支援該directive時禁用自動cookie jar或每identity用獨立client。none請求真的沒有Cookie，不能被之前Login污染。
登入僅本機讀取Set-Cookie的access_token，保存aToken/bToken；證據刪除Token／完整Set-Cookie／password/body含credential的資料。
Login Cookie必須HttpOnly、SameSite=Lax、Path=/、無Domain／Max-Age、Development HTTP Secure=false，Expires與JWT exp一致；body不回Token。正式Secure configuration由02 tests承接，非live HTTPS。
03f/g每次Login都核對User ID；04a/c核對owner，06b動態IDs按title＋Option.value辨識，06c後sc1仍原ID但value從one變first，不再用Array index找id。

08d之前等待至少1秒或以安全本機時鐘安排，並核對其submittedAt嚴格晚於08c。若不符合，記FAIL／停止Latest依賴，不直接UPDATE Response時間。相同timestamp UUID tie-break由既有R9測試承接。
所有HTTP body、identity與編號以companion為精確請求；Markdown expected與body同步，避免另造curl矩陣。

## 4. 既有案例重跑／自動承接清單

| 舊文件／測試 | 本次重新串接（新fixtures） | 02同版既有自動測試承接；需實際PASS證據 |
| --- | --- | --- |
| R4 paired plan | 03 Register／Login／Profile／duplicate／wrong password、16Logout | AuthUserEndpointsTests：Password10/128／Unicode／不trim、disabled／過期／未知User、Bearer拒絕、profile no-op／read-only、壞／過期Logout、Production cookie；ConfigurationTests、EngineeringEndpointsTests：startup／CORS origins／credentials |
| R5 paired plan | 04～05 Create／List／Draft hiding、13～14Delete | FormEndpointsTests：Create nested atomicity／未知頂層、page／pageSize/status boundaries／小page超界、他人Draft；DatabaseSchemaTests：七表／constraints／index／Cascade／case sensitivity |
| R6 paired plan | 06 full Delta／排序交換／config replacement／id-only刪除／no-op／foreign ID／失敗原子性 | CandidateContract：type change reset／choice互換／完整Candidate／精確數字；CandidateSafety、OptionPersistence：alternate-key staged save／second-save failure／commit fault／timestamps；DeadlinePrecision |
| R7 paired plan | 06a不完整Publish、07發布／Freeze／三狀態Visibility（含11d）、11Close／retry | Lifecycle／LifecycleReview：Draft Close、Published Publish／Closed Publish、Closed freeze、ownership矩陣、rating completeness、deadline strict equality、visibility no-op／transport、save／commit rollback、Patch↔Publish與Delete／visibility writers |
| R8 paired plan | 08五題型／required／Option歸屬／multiple、09single／anonymous、10deadline、11closedSubmit | SubmissionAnswerValidatorTests與Submission：type/range／duplicate/null／text trim／exact number／UUID；SubmissionConcurrency：observed waiter、single兩同／不同User、multiple、Submit↔Close／Delete、time sample；SubmissionAtomicity：真partial Answer write／precommit fault／Delete rollback／成功waiter |
| R9 paired plan | 05e/f空資料、08e/g/h/iLatest／distinct Count／List、10c逾期Count、11eClosed Latest、13Deleted | ResponseQueries：整份Latest（含全optional未答）、same-time UUID、identity×visibility×deadline、非OwnerCount／DraftLatest、disabled合法歷史、跨UserForm、GET no-write、Latest／Count×Submit／Delete四組RepeatableRead Form SELECT snapshot |
| 本次F-01～03 | 12a～d（舊測試無對等case） | 既有PASS不足以承接；依Owner修正／執行安排記結果 |
| S-01疑點 | 無新並行case | 未重現、不宣稱已由其他測試排除；Owner若要追查另授權 |

承接不是引用歷史PASS：02要保存本次case-name／DataRow／outcome／TRX與必要StdOut（R8 write counts、waiter，R9四組SELECT snapshot）。不用再寫native race矩陣／修改clock／故障測試產品host。細部邊界不新增HTTP矩陣，也不弱化正式規則。

既有人工計畫章節對照（保留原結果，本次另記新case）：R4 §2～5、§7b、§10、§12 → 本次03／15／16；R5 §2、§4～6 → 本次04～06／13～15；R8 §7～12 → 本次08～11／13～14；R9 §5、§8～12 → 本次05／08～11／13。R4 §6～9／§11、R5 §3與細部分頁、R8 §13 native race與R9快照細項改由02表列的實際自動測試承接；不原封重跑舊fixtures或將歷史PASS當新Run結果。
R6-03～06的巢狀fixtures／mixed Delta／config replacement → 本次04／06；type-change、精確數字、second-save／commit rollback由02承接。R7-05～07的Draft完整度／Visibility／Publish freeze → 本次06a／07；R7傳輸矩陣與writer race改由02承接，Closed操作在本次11重新串接。沿用的是規則與案例意圖，未复制旧RunId／憑證／結果，也沒有執行舊計畫的native並行harness。

## 5. DB核對與資料保留方法

以現有已配置DB client或container psql執行唯讀SQL；密碼走既有秘密設定，不寫進命令／證據。
範例非秘密參數`$DbUser/$DbName`由本機.env的相同名稱取得；不import整份.env／不輸出password：
```powershell
# snapshot SQL以stdin傳入；不在命令列放DB password。
Get-Content <本次snapshot.sql> | docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U $DbUser -d $DbName -At -F '|'
```
snapshot.sql使用同一讀取交易，每張表輸出table、UUID、整列digest（包括time／status，password_hash只進digest、不輸出原文）：
```sql
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT 'users',id,md5(row_to_json(t)::text) FROM users t ORDER BY id;
SELECT 'user_identities',id,md5(row_to_json(t)::text) FROM user_identities t ORDER BY id;
SELECT 'forms',id,md5(row_to_json(t)::text) FROM forms t ORDER BY id;
SELECT 'questions',id,md5(row_to_json(t)::text) FROM questions t ORDER BY id;
SELECT 'options',id,md5(row_to_json(t)::text) FROM options t ORDER BY id;
SELECT 'responses',id,md5(row_to_json(t)::text) FROM responses t ORDER BY id;
SELECT 'answers',id,md5(row_to_json(t)::text) FROM answers t ORDER BY id;
COMMIT;
```
01／02前後全snapshot必須相同；02後保存`retained-baseline.tsv`，15比較baseline原有keys與digest全部相同，新增keys按本次manifest核對。若有其他人同時修改DB，先協調靜止視窗，不能把外部改動誤認套件缺陷或將其revert。

Fixture核對SQL如下，`:mId/:sId/:fId/:aId/:bId`是DB client綁定UUID參數（DBeaver parameter binding或Npgsql命名參數等價形式）；只有API實際取得且驗證歸屬的UUID可填，不拼接未驗證字串。每case前後snapshot取其相關subtree，用同樣ordered(row_id,digest)比較；不輸出任意其他User的raw資料。
```sql
SELECT id, user_id, title, status, response_mode, response_identity,
       result_visibility, submission_deadline_at, created_at, updated_at, deleted_at
FROM forms WHERE id IN (:mId,:sId,:fId) ORDER BY id;
SELECT f.id,
 (SELECT count(*) FROM questions q WHERE q.form_id=f.id) AS questions,
 (SELECT count(*) FROM options o JOIN questions q ON q.id=o.question_id WHERE q.form_id=f.id) AS options,
 (SELECT count(*) FROM responses r WHERE r.form_id=f.id) AS responses,
 (SELECT count(*) FROM answers a JOIN responses r ON r.id=a.response_id WHERE r.form_id=f.id) AS answers,
 (SELECT count(DISTINCT user_id) FROM responses r WHERE r.form_id=f.id) AS users
FROM forms f WHERE f.id IN (:mId,:sId,:fId) ORDER BY f.id;
SELECT r.id,r.form_id,r.user_id,r.submitted_at,
       a.question_id,a.value,q.sort_order
FROM responses r LEFT JOIN answers a ON a.response_id=r.id
LEFT JOIN questions q ON q.id=a.question_id
WHERE r.form_id IN (:mId,:sId,:fId)
ORDER BY r.form_id,r.user_id,r.submitted_at DESC,r.id DESC,q.sort_order,a.question_id;
-- qTemp/temp1/temp2在06f後三者皆0；參數是06b記錄的本次IDs。
SELECT count(*) FROM questions WHERE id=:qTemp;
SELECT count(*) FROM options WHERE id IN (:temp1,:temp2);
SELECT id,display_name,status,password_hash IS NOT NULL AS password_hash_present
FROM users WHERE id IN (:aId,:bId) ORDER BY id;
```
不要將LEFT JOIN展示的空Answer欄位當作DB存了NULL Answer；09b的Response有0條answers row。
每階段Definition digest與full-row digest用途不同：Publish／Visibility／Close／Delete只准Form指定columns改變，Question／Option整列digest必須不變；08、09只准Response／Answer新增，Form Definition也不變。No-op／拒絕／GET全部不變。對合法changed field排除比較時要列確切column，不把整個Form row排除而掩蓋freeze。

## 6. 依序執行案例與結果紀錄

以下每HTTP row與companion編號一對一；精確request見該檔相同編號。DB／non-HTTP有明確checkpoint及分開AI／人工欄。證據填非秘密檔名／response status、code、關鍵body與DB差異，不存Token。

### 01. 環境、Migration 狀態與本次原生啟動

**前置與命令：**Owner 核准目前成對文件並明確授權執行；填本次基準 commit／RunId。讀 AGENTS.md，記 `git status --short`、`git rev-parse HEAD`，不覆蓋既有修改。確認 DB 非秘密名稱／user／port 與 API 設定相同；不輸出 `docker compose config` 的秘密全文。
```powershell
docker compose config --quiet
docker compose up -d
docker compose ps
docker compose port postgres 5432
dotnet --info
dotnet restore CustomFormService.sln --disable-build-servers -m:1
dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1
# 在本次專用終端保持前景；只管理自己啟動的 process。
dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --launch-profile http --no-build
```
每個命令分開檢查 `$LASTEXITCODE`；restore／build 失敗停止依賴 step。port 5000 已被別人使用時，不停止未知 process；先確認自己的啟動安排，兩檔 apiBase 同步。若改用另一 loopback port，記錄實際啟動命令與 API process、DLL SHA256、本次 build log。health/readiness 必須從本次原生 process 取得，TestServer PASS 不能代填。

DBeaver／已配置 DB client 執行以下**唯讀** SQL，保存無秘密結果：
```sql
SELECT current_database(), current_user, version();
SHOW server_encoding;
SELECT * FROM "__EFMigrationsHistory" ORDER BY "MigrationId";
SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename;
```
預期 UTF8、PostgreSQL 17 相容環境；唯一已核對 migration 為 `20260928122537_InitialV1Schema`／10.0.11；七張 domain tables 加 EF history。對照 repo Migration／snapshot，若本次修正新增核准 migration，先重新 review 此前置與 Schema 對照，不能直接套用。缺表／pending migration／連線不符時標 BLOCKED；**本計畫不執行 database update／從零建庫**。

依 §5 的 snapshot SQL 保存 `baseline-before-tests.tsv`；baseline 讀取在同一 RepeatableRead READ ONLY 交易。既有資料全部納入，包括 User status／displayName、deadlines、Definition、歷史 Responses／Answers。
**預期：**本次 build／原生 load 成功，DB host binding 與 migration 明確；01a、01b 成功後才继续。

| 非HTTP編號 | AI實際結果 | 人工實際結果 | 證據／問題 |
| --- | --- | --- | --- |
| 01 | 未執行（等待 Owner review） | 未執行（等待 Owner review） | 待執行 |

| 編號／目的 | 前置／身分 | 預期HTTP／body／資料效果 | AI實際結果 | 人工實際結果 |
| --- | --- | --- | --- | --- |
| 01a 原生 API health | 01 同次 build／原生啟動完成；無Cookie | 200；{"status":"healthy"} | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 01b 原生 API readiness | 01a；無Cookie | 200；Healthy | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 02. 同版既有自動測試承接

**操作：**先核對既有測試的 fixture 隔離／清理仍只操作本次隨機 email 與相關 IDs，無全庫清理。用 01 同次 build：
```powershell
dotnet test CustomFormService.sln --no-build --configuration Debug --logger "trx;LogFileName=integration-regression.trx" --results-directory <本次RunDir>
```
保存 log、exit、TRX，逐 testcase／DataRow 核對 §4 承接清單，尤其 R7 lock waiter、R8 真正 partial write／precommit faults、R9 四组 Form SELECT RepeatableRead snapshot。預計基準版 306 cases，**以實際 TRX 為準**，不以 count 相同代替 case-name 核對。
套件後依 §5 再 snapshot，與 baseline-before-tests 必須完全一致。失敗／殘留先保留證據並停止依賴流程，不自行擴大清理或重跑覆蓋舊 FAIL。native load 不成功時本次標 BLOCKED；沒有自動使用 Linux／其他 host 替代的授權。
**預期：**必要承接案例實際 PASS、0 skipped；cleanup 後既有資料相同；將本次 TRX 證據連到 §4，不沿用本次 review 的306 PASS填驗收。

| 非HTTP編號 | AI實際結果 | 人工實際結果 | 證據／問題 |
| --- | --- | --- | --- |
| 02 | 未執行（等待 Owner review） | 未執行（等待 Owner review） | 待執行 |

### 03. 註冊／登入／本人資料

| 編號／目的 | 前置／身分 | 預期HTTP／body／資料效果 | AI實際結果 | 人工實際結果 |
| --- | --- | --- | --- | --- |
| 03a 註冊新 Owner A | 01／02；兩個新 email 不存在；無Cookie | 201 User；email 正規化、displayName=IR A、avatarUrl=null；不設定 access_token；保存 aId | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 03b 註冊新會員 B | 前一案例成功或預期拒絕已確認；無Cookie | 201 User；無登入 Cookie；保存 bId，與 aId 不同 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 03c 註冊後尚未登入 | 前一案例成功或預期拒絕已確認；無Cookie | 401 AUTH_REQUIRED；不因註冊自動登入 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 03d 重複正規化 email | 前一案例成功或預期拒絕已確認；無Cookie | 409 USER_EMAIL_ALREADY_EXISTS；A 不被更新 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 03e 錯誤密碼 | 前一案例成功或預期拒絕已確認；無Cookie | 401 AUTH_INVALID_CREDENTIALS；不設定登入 Cookie | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 03f 登入 A | 前一案例成功或預期拒絕已確認；無Cookie | 200 User id=aId；保存 aToken 僅本機；Cookie scope 按 §3 核對 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 03g 登入 B | 前一案例成功或預期拒絕已確認；無Cookie | 200 User id=bId；保存 bToken 僅本機 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 03h 查 A 本人 | 前一案例成功或預期拒絕已確認；A | 200；id=aId；public User shape | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 03i 修改目前 profile | 前一案例成功或預期拒絕已確認；A | 200；displayName=IR A Updated；DB 僅 A display_name／updated_at 改變 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 04. 本次三張草稿 fixtures

| 編號／目的 | 前置／身分 | 預期HTTP／body／資料效果 | AI實際結果 | 人工實際結果 |
| --- | --- | --- | --- | --- |
| 04a 建立空草稿 M | 前一案例成功或預期拒絕已確認；A | 201；draft／single／anonymous／owner_only／null deadline／questions=[]；description=null；creatorDisplayName=IR A Updated；保存 mId | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 04b 建立 single anonymous S | 前一案例成功或預期拒絕已確認；A | 201 draft；single／anonymous；1 Optional Text；保存 sId、qS；deadline DB 精度相容 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 04c 建立 B 的私有草稿 F | 前一案例成功或預期拒絕已確認；B | 201 Draft；owner=bId；保存 fId、qForeign；不得發布此 Form | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 05. 列表／Detail／Draft 存在性與空資料

| 編號／目的 | 前置／身分 | 預期HTTP／body／資料效果 | AI實際結果 | 人工實際結果 |
| --- | --- | --- | --- | --- |
| 05a A 的 Owner List | 前一案例成功或預期拒絕已確認；A | 200；恰有 M、S，沒有 F；totalItems=2／totalPages=1；updatedAt DESC,id DESC；兩個 count=0 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 05b B 的 Owner List | 前一案例成功或預期拒絕已確認；B | 200；僅 F，default page=1／pageSize=20；totalItems=1；count=0 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 05c Owner Detail／目前 Creator | 前一案例成功或預期拒絕已確認；A | 200；與 04a Form Definition 一致；creatorDisplayName=IR A Updated；沒有 Creator email／responses／count | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 05d 其他會員探查 Draft | 前一案例成功或預期拒絕已確認；B | 404 FORM_NOT_FOUND | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 05e Owner Draft 的本人 Latest | 前一案例成功或預期拒絕已確認；A | 404 RESPONSE_NOT_FOUND | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 05f Draft Owner Count | 前一案例成功或預期拒絕已確認；A | 200；{"count":0} | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 06. Draft Delta／排序／刪除／原子性

**DB checkpoint（按 subcase 交錯執行）：**06a 前／後 snapshot M；06c 完成後保存 M 完整 subtree（含 timestamps），06d 必須 byte／語意值相同。06e 後 temp1 不存在；06f 後 qTemp、temp2 不存在，最終5Q／4O。06g、06h 前後 snapshot 不變；F 也不受 06h 影響。06c 的 sc1 同 ID／created_at，value 正規化 first，config replacement 不留 minLength。Title、Config 及 Options 預期以 companion body 與 01 FORM-05／03b §7.2為準。無故障注入；two-save／commit fault 由02承接。

| 編號／目的 | 前置／身分 | 預期HTTP／body／資料效果 | AI實際結果 | 人工實際結果 |
| --- | --- | --- | --- | --- |
| 06a 不完整草稿不可發布 | 前一案例成功或預期拒絕已確認；A | 400 FORM_NOT_PUBLISHABLE；questions／REQUIRED_FIELD；status／timestamps／children 不變 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06b 完整新增 Definition | 前一案例成功或預期拒絕已確認；A | 200；multiple／identified；6 Questions／6 Options；按題 title 取得 qText/qNumber/qRating/qSingle/qMultiple/qTemp 與選項 IDs；至少 Text required=true | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06c 排序交換與 config replacement | 前一案例成功或預期拒絕已確認；A | 200；Question 順序 Number、Text updated、Rating、Single、Multiple、Temporary；Text config 只有 maxLength，沒有 minLength；Single options sc2→sc1，sc1 value=first；ID／created_at 保留 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06d Candidate 失敗原子性 | 前一案例成功或預期拒絕已確認；A | 400 FORM_DEFINITION_INVALID；questions[0].sortOrder／DUPLICATE_SORT_ORDER；整份 Definition 與所有 timestamps 同 06c | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06e 刪除指定 Option | 前一案例成功或預期拒絕已確認；A | 200；只刪 temp1、其餘保留；6 Questions／5 Options | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06f 刪除 Question 與 Cascade Options | 前一案例成功或預期拒絕已確認；A | 200；qTemp 與 temp2 DB row 不存在；最終5 Questions／4 Options；其他 IDs 保留 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06g 空 Delta 合法 no-op | 前一案例成功或預期拒絕已確認；A | 200；完整 Definition／Form、Question、Option updated_at 不變；不是清空 Options | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 06h 跨 Form Question ID 不可 Upsert | 前一案例成功或預期拒絕已確認；A | 400 FORM_DEFINITION_INVALID；questions[0].id／QUESTION_NOT_FOUND；M、F 完全不變 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 07. 發布、Freeze、Visibility

**DB checkpoint：**07b 前保留 normalized Definition digest（排除 form status／updated_at，仍包含 Question／Option IDs、timestamps）。Publish 只改 status／updated_at；07c／07d拒絕不留變更；07a／07e只改 visibility／form updated_at。此處僅驗證 result policy **設定**與其他 endpoint 不受影響，Statistics 存取與身分投影留 R10。

| 編號／目的 | 前置／身分 | 預期HTTP／body／資料效果 | AI實際結果 | 人工實際結果 |
| --- | --- | --- | --- | --- |
| 07a Draft Result Visibility | 前一案例成功或預期拒絕已確認；A | 200；仍 Draft、multiple／identified；只有 result_visibility／updated_at 改變 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 07b 發布 M | 前一案例成功或預期拒絕已確認；A | 200 published；Question／Option／Config Definition hash 同 06f／06g；responseMode/Identity 不變 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 07c Owner Published Definition freeze | 前一案例成功或預期拒絕已確認；A | 409 FORM_NOT_EDITABLE；完整 DB 不變 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 07d 其他會員管理優先於 freeze | 前一案例成功或預期拒絕已確認；B | 403 FORM_ACCESS_DENIED；不是 FORM_NOT_EDITABLE；DB 不變 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 07e Published Result Visibility | 前一案例成功或預期拒絕已確認；A | 200；仍 Published；Definition／identity／deadline 全部不變；只改 visibility／updated_at | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 07f 發布 S | 前一案例成功或預期拒絕已確認；A | 200 published；deadline 嚴格晚於本次 server now；single／anonymous 保留 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 08. 五題型 Submission／multiple／Latest／Count

**DB checkpoint：**08a／08b 前後 responses／answers zero increment、Definition 不变。08c B=1R／5A，08d B=2R／6A，08f M=3R／11A，DISTINCT user_id=2（A、B）。每個 Response 的 form_id／user_id／submitted_at 與 Receipt 一致，Answer 只存實際題目；依 Question sort_order、UUID 排序。08d 之前必須讓 UTC submittedAt 嚴格較後，見 §3；同時間 UUID tie-break由02承接。

| 編號／目的 | 前置／身分 | 預期HTTP／body／資料效果 | AI實際結果 | 人工實際結果 |
| --- | --- | --- | --- | --- |
| 08a Required 遺漏拒絕 | 前一案例成功或預期拒絕已確認；B | 400 VALIDATION_ERROR；questions.<qText>／REQUIRED_FIELD；Response、Answer 零新增 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 08b Option 歸屬拒絕 | 前一案例成功或預期拒絕已確認；B | 400 VALIDATION_ERROR；answers[1].value／OPTION_NOT_FOUND；零新增 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 08c B 成功提交五題型 | 前一案例成功或預期拒絕已確認；B | 201 Receipt；保存 bR1；5 Answers 按 Number→Text→Rating→Single→Multiple；Text=Hello 😀；number 數值=1（token 不要求逐字相同）；不含 userId／Answer ID | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 08d multiple 第二次完整回覆（optional 遺漏） | 前一案例成功或預期拒絕已確認；B | 201，保存 bR2；id 不同 bR1；只1 Answer Text=Latest only；bR1+5 Answers 保留；确保 bR2.submittedAt > bR1（見 §3） | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 08e Latest 不從舊答補題 | 前一案例成功或預期拒絕已確認；B | 200；id=bR2；answers 僅 Text；submittedAt／values 與 08d 持久化 Receipt 一致；不得回 bR1 的 Number／Choice／Rating | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 08f Owner 也可填答 | 前一案例成功或預期拒絕已確認；A | 201；保存 aR1；5 Answers；與 B 分開；M 總 Responses=3、Answers=11 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 08g Owner 本人 Latest | 前一案例成功或預期拒絕已確認；A | 200；id=aR1；只回 A 自己的5 Answers | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 08h Count 按 User 去重 | 前一案例成功或預期拒絕已確認；A | 200；{"count":2}；不是3；包含 Owner；無原始答案 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 08i List Count 與端點一致 | 前一案例成功或預期拒絕已確認；A | 200；M count=2、S count=0；totalItems=2；沒有 F／Questions／Answers | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 09. single／anonymous

**DB checkpoint：**S single／anonymous：成功 sBR 有 user_id=bId、0 Answer；09a／09c 拒絕不新增；Count 由10c確認為1。M multiple／identified保留3份歷史，S single只1份；兩者都須登入、Receipt都不揭露 userId。anonymous／identified 的 Statistics 身分差異不在本次驗收，不能在R10尚未實作時聲稱通過。

| 編號／目的 | 前置／身分 | 預期HTTP／body／資料效果 | AI實際結果 | 人工實際結果 |
| --- | --- | --- | --- | --- |
| 09a anonymous 仍要求登入 | 前一案例成功或預期拒絕已確認；無Cookie | 401 AUTH_REQUIRED；不新增 Response | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 09b single anonymous 的空有效回覆 | 前一案例成功或預期拒絕已確認；B | 201；保存 sBR；Receipt answers=[]；DB 仍保存 user_id=bId；S Response=1／Answers=0 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 09c single 重試 | 前一案例成功或預期拒絕已確認；B | 409 RESPONSE_ALREADY_SUBMITTED；S 仍1 Response／0 Answer | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 09d 本人 Latest 可讀 anonymous 回覆 | 前一案例成功或預期拒絕已確認；B | 200；id=sBR、answers=[]；不回 userId；anonymous 不等於未登入／DB 去識別 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 10. 截止時間暫存 probe 與 finally 復原

**只對本次新 S 操作；10a～10c 是未來 Execute 時的批准範圍。**正常 Publish後改 deadline 是測試 fixture probe，不是產品 API 功能。由 PostgreSQL transaction 固定已截止 time；預期立即測嚴格大於邊界，等於 deadline／time sample精度由02既有 Clock測試承接。

用支援參數綁定的 DB client／Npgsql helper，不拼接未驗證 UUID。事前驗證 sId 的 owner=aId、title=`IR single <runId>`、status=published、deadline 與04b相同。保存 **DB實際** deadline（微秒精度／null）及完整S snapshot；僅在本機記錄非秘密值：
```sql
-- :sId／:aId 是綁定UUID；:runTitle是本次正確標題
BEGIN;
SELECT id, submission_deadline_at, updated_at FROM forms
WHERE id=:sId AND user_id=:aId AND title=:runTitle AND status='published'
FOR UPDATE;
UPDATE forms SET submission_deadline_at=clock_timestamp()-interval '1 minute'
WHERE id=:sId AND user_id=:aId AND title=:runTitle AND status='published';
-- affected rows 必須=1，否則 ROLLBACK／BLOCKED
COMMIT;
```
**用 try/finally 包住上述變更與10a～10c。** finally 中同樣 ID、Owner、title guard，只將 submission_deadline_at 用參數恢復為保存的DB實際值；不改 status／updated_at，affected rows=1；重新snapshot確認除10a～10c皆GET／拒絕外完整S回復原樣。null用SQL NULL參數，非null用DateTimeOffset UTC。DB client不支援 finally 時使用可在失敗後確保復原的 runner，未準備不得開始probe。復原失敗保留原值與錯誤證據，BLOCKED 11～17依賴操作，請Owner處理；不得順手改其他已發布Form。

| 非HTTP編號 | AI實際結果 | 人工實際結果 | 證據／問題 |
| --- | --- | --- | --- |
| 10 | 未執行（等待 Owner review） | 未執行（等待 Owner review） | 待執行 |

| 編號／目的 | 前置／身分 | 預期HTTP／body／資料效果 | AI實際結果 | 人工實際結果 |
| --- | --- | --- | --- | --- |
| 10a 逾期優先於 single／answers | 10 僅本次 S 的 deadline 暫時 probe 已就緒；必須 finally 復原；B | 409 SUBMISSION_DEADLINE_EXCEEDED；不是 ALREADY_SUBMITTED 或 VALIDATION_ERROR；零新增 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 10b 逾期不 auto-close | 10 deadline probe 尚有效；B | 200；status=published；Definition 仍 frozen | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 10c 逾期 Owner Count | 10 deadline probe；本例後執行 10 finally 精確復原；A | 200；{"count":1} | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 11. 主動 Close 與讀取界線

**DB checkpoint：**Close只改 status／updated_at；不重新修改 Question／Option Definition；M仍3R／11A／2 Users。11b、11c零寫入，11d只改visibility／updated_at；11e GET無副作用。Closed Owner count／Owner List count、Close Draft／Publish重試／Closed Publish／Closed freeze由02已列自動案例承接。

| 編號／目的 | 前置／身分 | 預期HTTP／body／資料效果 | AI實際結果 | 人工實際結果 |
| --- | --- | --- | --- | --- |
| 11a 主動 Close M | 10 finally 復原完成，否則停止依賴案例；A | 200 closed；Definition／Responses／Answers 不變 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 11b Close 重試 | 前一案例成功或預期拒絕已確認；A | 409 FORM_ALREADY_CLOSED；DB 不變 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 11c Closed 不再提交 | 前一案例成功或預期拒絕已確認；B | 409 FORM_NOT_SUBMITTABLE；先於 missing answers；零新增 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 11d Closed Result Visibility | 前一案例成功或預期拒絕已確認；A | 200 closed；identified／Definition／回覆不變；不測 Statistics 存取 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 11e Closed 仍可查本人 Latest | 前一案例成功或預期拒絕已確認；B | 200；id=bR2、1 Answer；visibility=owner_only 不影響本人 Latest | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 12. 本次 review findings 的修正回歸

**已知受影響案例：**12a／12b對應F-01、12c對應F-02、12d對應F-03（[本次review](../implementation/R0_R9_Overall_Code_Review_20261006.md)）。
在目前基準版有已確認缺陷，故本計畫**不保證全PASS**。Owner先決定修正／正式驗收順序；若先修，記修正commit、重跑02，實質變更計畫需重新Owner review。若Owner明確授權先執行基準版，按規格記實際FAIL，不能改期待值成500／415；未獲這項安排時受影響cases為BLOCKED，其餘独立cases可依批准順序繼續。
F-01在12b用page=1073741825,pageSize=4（unchecked offset=0）避免只有兩張fixture時 wrap-to-4 偶然空頁而掩蓋錯頁。
12c確保其新probe email不存在；預期不新增此第三會員；若意外201立即停止／記FAIL、保留新增row及id，不自動hard-delete。
F-02其他解碼位置及F-03其他MVC endpoint目前以source範圍登記，不擴充逐端點矩陣；S-01沒有可確認 defect／approval，無新併行實验。

| 編號／目的 | 前置／身分 | 預期HTTP／body／資料效果 | AI實際結果 | 人工實際結果 |
| --- | --- | --- | --- | --- |
| 12a F-01 負 offset | F-01 修正版本由 Owner 指定，或 Owner 明確決定先記錄已知 FAIL；A | 200；items=[]；page 保留2147483647、pageSize=100、totalItems=2、totalPages=1 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 12b F-01 正向 wrap 到第1頁 | 同 12a；unchecked offset 為0；A | 200；items=[]；page 保留1073741825、pageSize=4、totalItems=2、totalPages=1；不得回 M／S | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 12c F-02 Unicode 解碼 | F-02 修正版本由 Owner 指定，或 Owner 明確決定先記錄已知 FAIL；無Cookie | 400 VALIDATION_ERROR；displayName／INVALID_VALUE；不新增會員／Cookie | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 12d F-03 Content-Type 正式錯誤 | F-03 修正版本由 Owner 指定，或 Owner 明確決定先記錄已知 FAIL；無Cookie | 400 VALIDATION_ERROR，正式 error Envelope；不得回 415 ProblemDetails | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 13. Closed M Soft Delete 跨端點

**DB checkpoint：**13b前保存M subtree；後除Form.deleted_at／updated_at外全部相同，status仍closed；Questions5／Options4／Responses3／Answers11。13c～13h不寫入，repeatdelete不改第一次deleted_at。Delete後Draft/Pub/Closed其他管理endpoint404由02的R7_review_owner_cannot_access_deleted_form與相關測試承接；核心Detail／List／Submit／Latest／Count此次重新串接。Statistics deleted404留R10。

| 編號／目的 | 前置／身分 | 預期HTTP／body／資料效果 | AI實際結果 | 人工實際結果 |
| --- | --- | --- | --- | --- |
| 13a 非 Owner 不能刪除 | 12 已執行或依 Owner 決定標記 BLOCKED；開始前保存 M subtree snapshot；B | 403 FORM_ACCESS_DENIED；全部資料不變 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 13b Owner Soft Delete Closed M | 前一案例成功或預期拒絕已確認；A | 204 無 body；deleted_at／updated_at 變更；status 仍 closed；5Q／4O／3R／11A 全部保留 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 13c Deleted Detail | 前一案例成功或預期拒絕已確認；B | 404 FORM_NOT_FOUND | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 13d Deleted Owner List 排除 | 前一案例成功或預期拒絕已確認；A | 200；只有 S；totalItems=1；M 排除 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 13e Deleted Submit | 前一案例成功或預期拒絕已確認；B | 404 FORM_NOT_FOUND；先於 lifecycle／required；零新增 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 13f Deleted Latest | 前一案例成功或預期拒絕已確認；B | 404 FORM_NOT_FOUND；不是 RESPONSE_NOT_FOUND | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 13g Deleted Count | 前一案例成功或預期拒絕已確認；A | 404 FORM_NOT_FOUND；不是可讀的2 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 13h Delete 重試 | 前一案例成功或預期拒絕已確認；A | 404 FORM_NOT_FOUND；deleted_at 不重寫 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 14. Published／Draft Soft Delete

**DB checkpoint：**14a／14b前各保存S／F subtree。S還原deadline後刪除，status仍published、1Q／1R／0A；F仍draft、1Q／0R／0A。只有各Form deleted_at／updated_at變更。保留M／S／F及歷史，不將soft delete當作cleanup。

| 編號／目的 | 前置／身分 | 預期HTTP／body／資料效果 | AI實際結果 | 人工實際結果 |
| --- | --- | --- | --- | --- |
| 14a Published S 可直接刪除 | S deadline 已由 10 finally 復原；先保存 S snapshot；A | 204；status 仍 published；1Q／0O／1R／0A 全保留 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 14b Draft F 可直接刪除 | 先保存 F snapshot；F 一直未發布；B | 204；status 仍 draft；1Q 保留 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 15. 最終 DB 與原資料 preservation 核對

**最終只讀核對：**執行§5 fixture SQL與snapshot。A、B active；A display_name=IR A Updated；兩個password_hash僅核對IS NOT NULL、不輸出hash；沒有新增User Identity。
M closed+deleted、multiple／identified／owner_only，5Q／4O／3R／11A／2 User；S published+deleted、single／anonymous、deadline恢復、1Q／0O／1R／0A／1 User；F draft+deleted、1Q／0R。
M最新B回覆只1 Text，A回覆5 Answers；全部成功row歸屬正確；沒有08a/b、09a/c、10a、11c的失败殘留。
先比較baseline原有各(table,id)的digest完全相同，再核對新增IDs**僅**本次fixtures與已登記成功Response/Answer；本次temp1/temp2/qTemp已在Draft Delta合法實體刪除。不可把所有extra rows一律當合法或刪除。
如果12c意外新增row或某拒絕case新增row，保存證據與明確ID，記FAIL，不清除其證據。保存各case關聯的DB結果，不把總表核對替代前後原子性checkpoint。

| 非HTTP編號 | AI實際結果 | 人工實際結果 | 證據／問題 |
| --- | --- | --- | --- |
| 15 | 未執行（等待 Owner review） | 未執行（等待 Owner review） | 待執行 |

### 16. Logout 與清除 client 狀態

| 編號／目的 | 前置／身分 | 預期HTTP／body／資料效果 | AI實際結果 | 人工實際結果 |
| --- | --- | --- | --- | --- |
| 16a Logout A 清除目前 client Cookie | 15 DB 核對完成；aToken 只在本 request 用一次；A | 204 無 body；Clear Cookie scope 同 Login；刪除本機 A Cookie／aToken 變數 | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 16b 登出後無 Cookie | 16a 後 client Cookie 清除；V1 不承諾撤銷旧 JWT；無Cookie | 401 AUTH_REQUIRED；不得重送之前的 JWT | 未執行（等待 Owner review） | 未執行（等待 Owner review） |
| 16c Logout 壞 Cookie 仍成功 | 前一案例成功或預期拒絕已確認；壞Cookie | 204 無 body；清除 Cookie；不查 DB User | 未執行（等待 Owner review） | 未執行（等待 Owner review） |

### 17. 暫存清理與證據 handoff

**清理與交付：**保留原有資料／本次A、B／三張已soft-delete的M/S/F／合法Responses、Answers／所有FAIL與非秘密證據。
只清理由本次創建、manifest明確列出的暫存 cookie jar、含Token/password的工作HTTP副本／response header檔／request body暫存、deadline probe與自己啟動的API process。
deadline必須已finally精確復原；沒有barrier/worker/fault injection殘留（02由suite清理）。不刪既有artifact、不遞迴清理其他RunId、不停止別人的API／容器、不刪任何DBvolume。Windows移除文件使用`Remove-Item -LiteralPath`，逐一確認在本次RunDir，不跨shell組合delete。
工作HTTP副本與記憶體token清理後，repository paired files保持placeholders，`git diff --check`與`git status --short`確認只含授权紀錄。
填case的AI／人工分離結果；附RunId、測試commit／build、实际HTTP數／DB步驟、必要承接測試、保存資料、cleanup與未驗證事項；FAIL／BLOCKED／未執行不得填PASS。停於「驗收結果待Owner確認」，不得自行更新Round accepted或開始R10。

| 非HTTP編號 | AI實際結果 | 人工實際結果 | 證據／問題 |
| --- | --- | --- | --- |
| 17 | 未執行（等待 Owner review） | 未執行（等待 Owner review） | 待執行 |

## 7. Handoff與尚未驗證事項

目前AI HTTP：**0/72執行**；非HTTP：**0/5執行**；人工：**全部未執行（等待 Owner review）**。沒有本計畫PASS／FAIL／BLOCKED的實跑紀錄；三項review缺陷的既有證據見review報告，不能貼成此RunId結果。

待Owner決定：是否先修F-01～03；是否要確認S-01；何時正式Execute；R7／R9驗收確認如何安排。開始Execute前明列受影響12a～d的前置狀態。不因72個主流程request或舊PASS就推論完整V1／A-stage accepted。
資料保留：A/B與M/S/F及歷史保留；只清本次manifest裡的秘密暫存／自己的process，10deadline恢復，既有fixtures／歷史驗收證據不動。無hard-delete／volume cleanup。

**整體 review 完成；整合驗收計畫待 Owner review，尚未執行。**
