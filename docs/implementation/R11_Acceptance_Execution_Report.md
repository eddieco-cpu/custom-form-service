# R11 獨立 AI 驗收執行報告

2026-10-11（Asia/Taipei）。**R11 驗收結果待 Owner 確認**。本次獨立重核基準及實跑：49 HTTP＋6非HTTP最終PASS；fresh full **420 executed／420 passed／0 failed／0 skipped**。人工55案均未執行。沒有發現可重現的產品／既有測試程式缺陷或正式規格衝突；沒有修改src/tests/specs、預期或HTTP矩陣。工具首次失敗保留，下列coverage缺口不標PASS。未自行標R11、A-stage或Backend V1 accepted。

## 授權、版本與有效產物

- Owner在「執行 R11 AI 驗收」chat `01a129b8-f8bd-70e3-b0b1-d0b1519ea629`明確確認 `f766548` 成對計畫review通過，授權fixture實際連線核對、§8文件補件及修訂後直接Execute。修訂／補件提交 `ee23e56`；没有再擴大範圍。
- RunId=`r11-acceptance-20261011-01`；[證據目錄](../../artifacts/r11-acceptance/r11-acceptance-20261011-01/)。開始工作樹乾淨，`git diff a4f9402 ee23e56 -- src tests docs/specs`空；以 `git archive ee23e56` 在全新ignored source snapshot建置，沒有舊bin/obj。
- .NET SDK 10.0.400、Docker Engine 28.5.1、Compose v2.40.0-desktop.1。開發container `custom-form-service-postgres-1`、volume `custom-form-service-postgres-data`、binding `127.0.0.1:5432`、database/user `custom_form_service`；既有Migration正確，未對開發DB套migration。
- API DLL SHA256=`9e55cc285d6d76ee7ad2cfabf2e6adfa39459132e4cd912573de81c53542d442`；test DLL=`b8685eb734e3d8c9c441dfeaaa17f01b82554c530e2660459371b7b6b623ec43`。test output內API copy hash相同；DLL/PDB/deps/runtimeconfig全部於結束再次核對未變。Portable PDB 152個document checksum通過，包括3份未落地generated source的PDB embedded內容，不以missing-file跳過。完整[hashes](../../artifacts/r11-acceptance/r11-acceptance-20261011-01/assembly-hashes.json)、[PDB](../../artifacts/r11-acceptance/r11-acceptance-20261011-01/pdb-source-checksums-02.json)。
- 原生PID 32312／5013連隔離DB，PID 42484／5012連開發DB；absolute DLL command line、開始時間、hash及listener PID都核對，两个Development子程序，不借用原listener或TestServer代填HTTP。[身分](../../artifacts/r11-acceptance/r11-acceptance-20261011-01/native-processes.json)。本次Windows新產物可執行，歷史policy問題不宣稱永久解除。

## 實際命令、工具與結果

以下工作目錄除註明外為snapshot root；stdout/stderr及exit保留證據目錄。restore/build/ef/test均實際執行，沒有引用歷史PASS。

| 命令／操作 | 本次結果／證據 |
| --- | --- |
| `git status --short`；`git diff a4f9402 ee23e56 -- src tests docs/specs`；`git archive --format=tar --output=<RunDir>/source.tar ee23e56`；`tar -xf ... -C <source>` | baseline乾淨、source diff空；新snapshot |
| `dotnet tool restore` | exit0；snapshot工具還原成功 |
| `dotnet restore CustomFormService.sln --force --no-cache` | sandbox首次exit1且無stdout；正常權限重試exit0，`restore-escalated.log` |
| `dotnet build CustomFormService.sln --no-restore --no-incremental --configuration Debug --disable-build-servers -m:1` | exit0／0 warnings／0 errors，`build-first.log` |
| Docker config/up（完整scope如下） | 各exit0；volume inventory確認本次名稱不存在，merged config只在記憶體解析，inspect labels/Mounts/binding一致 |
| scoped `exec -T postgres psql`的等價 `docker exec -i <exact-container> psql -X -A -t -v ON_ERROR_STOP=1 -U r11_empty -d r11_empty` | 前置public schema空／無History、UTF8／UTC；命令證據`empty-pre-migration` |
| `dotnet ef database update --project src/CustomFormService.Api/CustomFormService.Api.csproj --startup-project src/CustomFormService.Api/CustomFormService.Api.csproj --configuration Debug --no-build` | exit0，從零唯一 `20260928122537_InitialV1Schema`；第二次同命令exit0 no-op |
| `dotnet ef migrations list --project ... --startup-project ... --configuration Debug --no-build` | exit0，唯一migration已套用；`migration-list.log` |
| 独立唯讀fixture probe，載入本次原test DLL／DbContext | 最終exit0；Development/Production有效DI Options／providers及SQL身分通過；詳下節 |
| `dotnet test CustomFormService.sln --no-build --configuration Debug --logger "trx;LogFileName=r11-full.trx" --results-directory <RunDir>/full` | 首次full exit0，420/420；0 failure/skip。TRX逐420 rows核對Passed，R10 43 rows包含其中，沒有另重跑focused/native race |
| `Start-Process dotnet -ArgumentList @('exec','<absolute fresh DLL>') -Environment <child settings> -WindowStyle Hidden` | 兩PID/listener核對；05a～d原生200 |
| Python urllib，逐段解析核准 `.http`，顯式Cookie、無cookie processor | 49 requests按原編號／順序；19JSON body、1.0 raw number保持；49最終PASS；每案response＋SQL checkpoint |
| RepeatableRead READ ONLY七表 `md5(row_to_json(t)::text)`／Migration | 初始、full後、每HTTP前後、最終／清理後比較；舊完整列未變；GET／拒絕均零寫入 |
| 最終合成資料、DISTINCT ON(user_id) effective整份、receipt／JSONB／timestamp比較 | SQL／assertions通過；工具誤讀隔離空庫的首次證據仍保留 |
| Portable PDB／source／assembly hash、文件靜態核對、`git diff --check` | 最終全部通過；generated-source診斷首次錯誤另保留 |

隔離Compose scope全程為：`docker compose -p r11-acceptance-20261011-01-empty --env-file <RunDir>/empty.env -f <source>/compose.yaml -f <RunDir>/empty.override.yaml config --quiet`／`up -d postgres`／`stop postgres`。子程序四個POSTGRES_*全部明確覆寫，volume override=`r11-acceptance-20261011-01-postgres-data`、loopback55432；不只靠 `-p`。55432未佔用且不在當次Windows排除範圍。根.env／User Secrets／OS持久設定未變。

## Fixture實際來源與隔離保證

直接讀核准 `EngineeringEndpointsTests.cs` 的 `CustomFormServiceApiFactory.ConfigureWebHost`，遍查factory overrides與SQL helpers：業務helper都從factory DI `IOptions<PostgreSqlOptions>.Value.BuildConnectionString()`取得，不另有硬寫開發DB的連線。ConfigurationTests的假密碼／invalid options只測startup，沒有建立業務DB連線；其他overrides為JWT、Clock、interceptors／MaxBatchSize等，不換業務DB。

Full前以本次test DLL建立原Development及Production factory；獨立診斷entry assembly只補snapshot API content root（與generated MvcTesting manifest一致），**不覆寫DB配置、不修改原tests**。從DI讀Options、DbContext與IConfiguration providers；DbContext實際開connection執行SQL：

| 核對項 | Development／Production結果 |
| --- | --- |
| Host／Port／Database／Username | `127.0.0.1`／`55432`／`r11_empty`／`r11_empty` |
| 非秘密鍵最後生效provider | `MemoryConfigurationProvider`，TestFactory把已解析值寫回；不是只檢shell env |
| Password最後provider | `EnvironmentVariablesConfigurationProvider`，只記相符boolean，不輸出值 |
| 原DbContext vs Options | 非秘密target相同；NpgsqlDataSource在connection string隱藏password，原connection仍可開啟 |
| 實際SQL | current_database=`r11_empty`，current_user=`r11_empty`，server=`172.20.0.2/32:5432`，對應本次inspect container／55432 binding |

完整[fixture-target.json](../../artifacts/r11-acceptance/r11-acceptance-20261011-01/fixture-target.json)與[隔離inspect](../../artifacts/r11-acceptance/r11-acceptance-20261011-01/empty-inspect-nonsecret.json)。套migration前schema空，套後7表＋History且無seed；catalog columns/type/length/null/default、enums、PK/FK/unique/check/index/delete/update對照02 §3～14，DatabaseSchemaTests三方法亦本次PASS。只有options.question_id CASCADE，其他FK NO ACTION，沒有額外業務unique。

## 55案與資料／清理結果

01 baseline、02 clean restore/build、03空庫migration/schema、04fixture/full/native、14資料/清理、15文件/coverage 最終均PASS。05～13 **49 HTTP全部PASS**；具體逐案證據見[成對Markdown](../test-plan/R11_Acceptance_Test.md)。人工全部未執行，不用AI代填。

兩新會員、兩表單的UUID／各ResponseID在[manifest](../../artifacts/r11-acceptance/r11-acceptance-20261011-01/manifest.json)。保留users2／forms2／questions6／options4／responses4／answers15／user_identities新增0；M三份14answers、S一份1answer。M最新有效B四答＋A五答，共9effective answers；text Old及舊number9/rating1未混入。M Closed/public未刪，S status仍closed且deleted_at非null，S子列／Response／Answer保留。Receipt與DB JSONB和UTC submitted_at一致。07d、11d及所有拒絕／GET前後全列相同；合法Lifecycle／Visibility／Delete只有對應Form row變動，S刪除後只合法timestamps改變，Definition及子列保持。

舊七表完整列digest及Migration於full後、全部HTTP後及清理後與baseline相同；沒有觀察外部寫入，未還原別人資料。合成fixtures全部保留，無regular volume刪除。只停止已驗明本次的PID32312/42484；原程序未停止。隔離container stopped／volume **保留**，名稱如上，未down -v；開發container仍healthy／原5432 binding。工作secret `private.json`／`empty.env`已移除；token/password只在HTTP runner記憶體，headers證據遮蔽access_token，成對檔仍placeholder；無秘密進Git。[cleanup](../../artifacts/r11-acceptance/r11-acceptance-20261011-01/cleanup-pids.json)／[最終驗證](../../artifacts/r11-acceptance/r11-acceptance-20261011-01/verification-summary.json)。

## 首次失敗保留與findings

以下為本次操作／工具錯誤，不冒充產品FAIL，也沒有更改預期求PASS：

1. sandbox Docker/CIM access denied、restore exit1無輸出：正常權限重試成功；sandbox結果保留於本chat及run說明。沒有修改安全政策。
2. Fixture probe首次DirectoryNotFound：獨立entry assembly缺VSTest content-root metadata；只給診斷host正確content root。第二次因Npgsql password redaction誤判DI不相同；第三次重複Open已開啟connection；第四次原DbContext SQL成功。`fixture-probe*.log`／command JSON全部保留，full在第四次核對成功後才跑。
3. HTTP runner最初中文緊接204使word-boundary解析錯誤，未送HTTP、未寫DB；`http-runner-parse-first.log`保留。修正parser後首次實際49 requests全部PASS，無業務POST重送。
4. PDB工具首次假設generated source都落地而失敗；改讀PDB embedded generated source，152 checksums全驗，第一次錯誤保留`pdb-first-failure.txt`，不跳過來源核對。
5. 最終唯讀工具import guard及誤讀隔離空庫：`final-db-first-failure.txt`／`final-verification-02.log`／`final-synthetic-first-empty-target.*`保留；修正為開發DB後verify通過，無資料變更或HTTP重送。
6. 文件／snapshot靜態checker首次使用Windows cp950讀UTF-8失敗、後續未考量git core.autocrlf=true的archive checkout換行，均保留`doc-check-first-failure.txt`／`doc-check-02.log`。明確UTF-8及Git path-specific clean filter核對archive blobs；PDB仍按實際編譯bytes核對，不修改snapshot取得相符。

**產品／既有測試findings：無已確認缺陷；沒有受影響依賴需交修。** 若Owner另行repair，本次證據版本應重新判有效性，再補受影響驗收。

## 文件補件、coverage與known gaps

已完成README A-stage現況／17端點索引與Email→Closed Statistics指引、非秘密child-env／priority／volume/readiness/data guide、舊R0–R9成對計畫的歷史未執行／由R11取代入口標記。未改appsettings／.env.example／產品或tests；空Staging/Production JSON不是缺陷。實跑結果於本次驗收後才回填。

[Coverage索引](R11_Coverage_Index.md)逐17端點列本次HTTP／具名test／Catalog top-level、11個detail codes與early-return／實際限制；Google單支G1 pending。Known gaps：

- 一些端點直接PA四碼／bad-UUID矩陣、QUESTION_TYPE_CONFLICT exact path、17端點逐支500 injection沒有找到足夠直接證據；不以shared auth或420總PASS補推。此為證據粒度缺口，沒有實際產品反例，不擴大本次矩陣；Owner可另授權補驗。
- RV-I-01 Publish barrier fresh V1頂層DTO精確assertion改善、I-02 List snapshot非阻擋建議保留；不把本次順序HTTP當併行證明。
- [歷史來源索引](R11_Historical_Status.md)：R0–R3缺repo acceptance／Owner來源，不斷言尚未驗收；R5 Owner accepted但AI case缺口；R7/R9 repo待確認、目前缺可追溯明確確認來源，不斷言沒有做過驗收。已查相關chat，未新增無來源accepted，不重跑旧Round矩陣。
- 真Google Provider／G1、frontend、Railway／domain／正式HTTPS未執行；本次native成功不宣稱Windows policy／port問題永久解除。

交付提交SHA與Git狀態見chat最終訊息；原始run證據ignored且本機保留。R11狀態維持 **acceptance results pending owner confirmation**，人工未執行，**G1 pending**。停止於 **「R11 驗收結果待 Owner 確認」**。
