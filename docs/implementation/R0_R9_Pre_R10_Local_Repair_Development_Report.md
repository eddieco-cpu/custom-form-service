# R10 前局部修正開發報告

日期：2026-10-07。狀態：**局部修正開發完成，待 reviewer 審查**。

Owner 核准 [局部修正計畫](R0_R9_Pre_R10_Local_Repair_Plan.md) `b491bf7ddd2666ed45a6396191d4f362ca5b0bce`，授權 F-01～03、S-01 受控確認及成立後的 GetDetail scoped RepeatableRead。起始分支 `master`、工作樹乾淨。本次未啟動正式整合驗收、R10／G1，不修改正式規格、DB Schema、歷史 accepted／Owner pending 狀態或成對驗收文件。

## 1. 修正與實際影響

| 項目 | 實作結果 |
| --- | --- |
| F-01 | List 在乘法前轉 long；Count 後對 offset ≥ totalItems 直接回空 items；其餘 offset 已小於 int totalItems，才轉 int 給 Skip。合法超界維持 200、輸入 page／pageSize 與正確 pagination，不新增 page 上限。 |
| F-02 | RequestJson 的 TryReadString／TryReadPropertyName 僅在 JSON 解碼呼叫捕捉 InvalidOperationException。Auth／User 透過原 helper 生效；Form 頂層文字／Enum／Datetime、Question／Option／Delta UUID、config key／value 改用 primitive，由原驗證層決定 code／path。無法解碼的 key 定位 enclosing object；合法 key 的未知／重複欄位仍用原 path。 |
| F-02 config | Delta 先 Clone request config，再以既有完整 Candidate validator 正規化／驗證，最後 ChangeSet 比較；移除驗證前會解碼不合法 config 的 DeepEquals。既有等值 config no-op 與 timestamps 由完整 suite 承接。ApplyOptions 的 Name 字典建立仍位於已通過完整 Shape 的內部路徑，未擴大重構。 |
| F-03 | 新增有序 MVC action filter，只辨識 input formatter 的 UnsupportedContentTypeException，order=-3001，先於內建 -3000 的 415 filter，回 400 VALIDATION_ERROR、body／INVALID_VALUE。Program 直接註冊 filter instance，保持順序。Authentication／Authorization middleware 仍在 MVC 之前；不全域改寫 status／Exception。 |
| S-01 | 已受控重現混合版本，見 §2。只在 GetDetail 的完整 split read／DTO 建構外加短 RepeatableRead transaction；第一個 Form SELECT 建立快照，成功 commit 後回應。共用 loader、writer ReadCommitted／FormAggregateLock、Patch／Publish／Visibility read-back 均不改。 |

實際變更檔案：

- [FormsController.cs](../../src/CustomFormService.Api/Controllers/FormsController.cs)：List offset、Form 解碼、Detail snapshot。
- [RequestJson.cs](../../src/CustomFormService.Api/Validation/RequestJson.cs)、[FormDefinitionValidator.cs](../../src/CustomFormService.Api/Validation/FormDefinitionValidator.cs)、[DraftDefinitionDelta.cs](../../src/CustomFormService.Api/Validation/DraftDefinitionDelta.cs)：局部安全解碼／原層級錯誤。
- [UnsupportedJsonContentTypeFilter.cs](../../src/CustomFormService.Api/Validation/UnsupportedJsonContentTypeFilter.cs)、[Program.cs](../../src/CustomFormService.Api/Program.cs)：只處理 MVC unsupported media。
- [FormEndpointsTests.LocalRepair.cs](../../tests/CustomFormService.IntegrationTests/FormEndpointsTests.LocalRepair.cs)：71 個新增 rows，沿用 Form partial 的會員／DB／factory helpers；包含 Auth／User representative cases，沒有新增產品 probe endpoint。
- 本報告及 Implementation Plan 的獨立 repair 註記。

規格對照：03a §2／4～6／9；03b §4.1～4.3、5.2、6.1～6.2、7.1～7.2、8.2～8.4；03c §4.3；04 §2／4／6；01 FORM-01／05／08、CON-01／03；02 §15／17；05 §8～9／11～13／18。未發現需更改核准契約的衝突。

## 2. S-01 控制點與首次反例

使用真 PostgreSQL、獨立 HTTP requests／DbContexts；test-only interceptor 限定本次 Form ID 與 GET ContextId。V0 是持久化後 GET 的完整 Draft，不用 Create 的未 round-trip timestamp 作基準。單次 Patch V1 同時改 Form title／description、Question title／config、Option label／排序，刪除一個 Option 並新增另一個。

| Barrier | 修正前實際結果 | 修正後預期及已觀察 focused 結果 |
| --- | --- | --- |
| Form root SELECT 的 ReaderExecutedAsync；只完成第 1 個 split SELECT | 舊 `V0 form`／`V0 description`／updatedAt，加新 `V1 question`、`variant=select`、V1 Options。回應不是任何完整 committed Definition。 | 同一 GET 的 3 個 split SELECT 使用 RepeatableRead；完整 V0，下一 GET 完整 V1。 |
| Questions SELECT 的 ReaderExecutedAsync；已完成第 2 個、尚未執行 Options SELECT | 舊 Form／`V0 question`／`variant=radio`，加 V1 Options，包含新 C、刪除舊 B、A label／排序更新。 | 完整 V0 Questions＋Options，下一 GET 完整 V1；完整集合及順序相等。 |
| root 暫停→Patch V1 commit→Publish commit→Release | 舊 Draft root 加已提交／發布的 V1 Questions／Options，仍屬混合 Definition。 | 已建立快照的 GET 回完整 V0 Draft；下一 GET 為完整 Published V1。既有 Patch×Publish／Freeze 測試由完整 suite 承接。 |

三案均先等待 Reached，獨立 DB 查詢確認 Patch committed rows，再確認 GET 尚未完成、SELECT count 不變，才 Release；不靠 Task.WhenAll／sleep／BeginTransaction 推測。保存實際 SQL、ContextId、isolation、SELECT count 與 V0／V1／Actual body。修正後還核對 3 個 SELECT 全完成、fresh GET 與持久化完整 V1、GET 前後 Definition rows 完全不變。finally Release／cancel／逐一 drain 全部 workers，非預期錯誤不吞掉。

例：`before4/before4.trx` 的 root／questions 兩個反例及 Publish 交互均 FAIL；修正後同三案 PASS、isolation=RepeatableRead。這是已確認的一致性缺陷；未將其推論成越權、必須看最新版本、Delete 違約或 R10 blocker。沒有新增原生 S-01 併行矩陣。

## 3. 首次失敗保留與反例有效性

所有 raw logs／TRX 位於忽略的 `artifacts/pre-r10-repair-20261007/`，每次使用獨立檔案，未覆蓋 FAIL。這些本機證據不是跨機器的永久附件，以下摘要隨本報告提交。

| 嘗試 | 實際結果與處理 |
| --- | --- |
| sandbox restore／Docker 查詢 | restore 首次 exit 1 無診斷；Docker named pipe Access denied。保留觀察，後續正常本機授權執行成功，未改安全政策。 |
| 未修產品 `before`，初版 64 案 | 59 FAIL／5 PASS。13 個 empty Cookie header FormatException 是 harness 錯誤；Visibility 測資多了不允許的 title；S-01 首版 Create timestamp 未用持久化精度。保留全部記錄；先校正 harness，未修改產品。 |
| 未修產品 `before2`，71 案 | 53 FAIL／18 PASS；F-01 3 FAIL、F-02 42 FAIL／3 PASS、F-03 5 FAIL／2 PASS、S-01 3 FAIL、Auth priority／server invariant 13 PASS。root／Questions barrier 都已命中並實際看到混合版本。 |
| 修正後 `focused` | 166 PASS／5 FAIL，五個 MVC endpoint 仍 415。泛型 filter registration wrapper 沒採用 class 的 Order；改為 instance registration，沒有放寬期待值。 |
| 修正後 `focused2`／`full` | 171／171、377／377 PASS，0 skipped。最後 diff 自查另發現兩個 Form title DataRows 送了重複 title，會提前被 shape 拒絕；因此不把它們當有效 title 反例，補正後重新驗證。 |
| baseline 補測 `before3` | build 成功，但 API DLL 遭 Windows 應用程式控制阻擋 `0x800711C7`，test discovery 未完成；沒有可用 case 結果。校正原版檔案集合（新 filter 不屬原版）、沿 checkout CRLF 重建，未變更系統 policy。 |
| 完整未修產品 `before4`，補正後 71 案 | **55 FAIL／16 PASS／0 skipped**。F-01 3 FAIL；F-02 44 FAIL／1 PASS（原階段優先序及合法 pair）；F-03 5 FAIL／2 PASS（原手動解析）；S-01 3 FAIL；Auth priority／server invariant 13 PASS。兩個單一 title 案例現在確實 500。原產品暫時還原只供 baseline；最終修正檔記憶體備份、finally 按 bytes 完整復原，沒有 reset／checkout／歷史改写。 |
| 原生 HTTP 首次 | health 200；harness 將 readiness 的純文字 `Healthy` 當 JSON，解析失敗；尚未建立會員／Form。保留該次 log／RunId，校正 parser 後用新 RunId 重跑。 |
| 最後 harness 清理自查 | GET 已有取消／drain；補上 Patch／Publish 共用 40 秒 cancellation，finally 逐一 drain 全部 workers。請求 body、HTTP 與一致性預期不變；重新執行 build／focused／full，另存 delivery 證據。 |

F-01 修正前 actual：`2147483647/100` offset=-200→500 正式 Server Envelope；`42949674/100` offset=4→200 卻回 2 個 items；`1073741825/4` offset=0→200 卻回 4 個 items。每案有 6 張獨立 Owner Forms，沒有「資料太少碰巧空頁」的假 PASS。修正後三者 200／空 items／原 page／pageSize／totalItems=6／totalPages=1 或 2。

## 4. 開發驗證命令與結果

環境：Windows x64、SDK 10.0.400、ASP.NET Core／runtime 10.0.11，既有本機 PostgreSQL。Linux fallback 未使用。

| 實際命令／操作 | 結果與證據 |
| --- | --- |
| `dotnet restore CustomFormService.sln` | 本機 exit 0，packages 全部最新；`restore-native.log`。首次 sandbox exit 1 另存 `restore.log`。 |
| `dotnet build CustomFormService.sln --no-restore --configuration Debug --disable-build-servers -m:1` | 最終 exit 0、0 warnings／0 errors；`delivery-build.log`。`final-build.log` 及所有 baseline／中間 logs 保留。 |
| `dotnet test CustomFormService.sln --no-build --configuration Debug --filter "FullyQualifiedName~Repair_" --logger "trx;LogFileName=before4.trx" --results-directory artifacts/pre-r10-repair-20261007/before4` | 55 FAIL／16 PASS；當時為未修產品，測試 body／預期與最終版相同。 |
| `dotnet test CustomFormService.sln --no-build --configuration Debug --filter "FullyQualifiedName~Repair_\|FullyQualifiedName~SubmissionAnswerValidator\|FullyQualifiedName~AuthUserEndpointsTests\|FullyQualifiedName~R7_visibility_rejects\|FullyQualifiedName~R9_completed_Form" --logger "trx;LogFileName=focused-final.trx" --results-directory artifacts/pre-r10-repair-20261007/focused-final` | **171／171 PASS、0 skipped**；`focused-final-tests.log`／TRX。filter 中 `\|` 在 Markdown 表格表示實際的 `|` 字元。 |
| `dotnet test CustomFormService.sln --no-build --configuration Debug --logger "trx;LogFileName=full-final.trx" --results-directory artifacts/pre-r10-repair-20261007/full-final` | **377／377 PASS、0 skipped**；`full-final-tests.log`／TRX。 |
| 最後 harness 清理後 focused／full | 使用上列相同 filter／參數，logger 分別 `trx;LogFileName=focused-delivery.trx`／`trx;LogFileName=full-delivery.trx`，results-directory 分別 `artifacts/pre-r10-repair-20261007/focused-delivery`／`artifacts/pre-r10-repair-20261007/full-delivery`。**171／171、377／377 PASS、0 skipped**；`focused-delivery-tests.log`／`full-delivery-tests.log` 及 TRX，exit 0。 |
| 原生 Kestrel | 隱藏 process，直接 `dotnet <repo>/src/CustomFormService.Api/bin/Debug/net10.0/CustomFormService.Api.dll --urls http://localhost:5010`，WorkingDirectory 為 API、Development；PID 39276，`native-process-evidence.json`／API stdout。 |
| `pwsh -NoProfile -File artifacts/pre-r10-repair-20261007/native-probe.ps1` | 補驗 **67 HTTP PASS**（包含 health／readiness／setup），RunId `native-f8e33e15abd2495dabb972e4b1df2202`；`native2-tests.log`、RunId `-http.jsonl`，不是正式驗收的 72 requests。 |
| DB snapshot | `baseline.sql` 透過 `docker compose exec -T postgres sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At'` stdin 執行，只讀七表 ordered 完整列 MD5。初次 full 後與起始完全相等；原生補驗後排除精確新會員 IDs 的既有七表也完全相等。`db-before-final-tests.txt`／`db-after-final-tests.txt`／`db-after-delivery-tests.txt` 全列及 migration 紀錄相等，包含保留的新 native fixtures。 |
| `git diff --check`／scope inspection／HTTP process cleanup | 已檢查產品 diff；native process 命令列確認後只停止本次 PID 39276，其他 API／DB／volume 保留。最後 Git state 與提交見 §7。 |

原生自測 API DLL SHA-256：`82594C4EE9374DBBF0D1902C09D06371675352FCAB11EDD906736F6A9A75F1A8`；最終重新建置後再次核對相同，因此 67 HTTP 適用於最終產品 DLL。title 補正只改測試，不改產品。原生 startup／health／readiness 是實際 Windows Kestrel，不以 TestServer PASS 代替。

## 5. 錯誤優先序與原生覆蓋

新增自動 rows 實際覆蓋：

- Auth Register displayName high／low surrogate、Login password、Auth／User property name；User displayName；Form Create／Patch 一般文字、Enum、Datetime、Question／Option／Delta UUID、config placeholder／variant／key。HTTP、top-level code、detail code／精確 path、零寫入均有 assertions。
- Form 頂層 `VALIDATION_ERROR`、nested `FORM_DEFINITION_INVALID`；Question type `INVALID_QUESTION_TYPE`、config `INVALID_QUESTION_CONFIG`；壞 key 用 body／Question／Option／config enclosing path。既有 Publish、R8 envelope／answer、合法 Unicode／no-op／rollback 由同版 suite 承接。
- User、Create、Patch、Visibility、Submit 的壞 Body／key／Content-Type，分別核對缺 Cookie、壞／過期 JWT、不存在 User、Disabled User 的 401／403 正式 Auth Error。Form 帶 invalid formId 仍先 Auth。
- JSON Object 可解析的壞值／key／nested UUID：不存在→404、非 Owner→403、Owner Published／Closed→409，才輪到 Definition 400。Register unknown／Create 頂層先於 value／nested 驗證。未預期伺服器 InvalidOperationException 故障注入仍為 500 正式 Envelope。

原生 67 項實際包括：3 種大頁碼；Register／Login／User／Form raw surrogate 及 property-name／nested；Resource／Owner 的壞值和壞 key；7 個 Body routes 的 text/plain 正式 400（5 MVC＋2 手動）；5 protected routes 缺／壞 Cookie × 壞 key／text/plain；invalid formId＋無 Cookie＋text/plain；Disabled User／Form；Publish／Close 無 Body；Published／Closed 的 Lifecycle 與 Owner 優先；Logout 壞 Cookie 204。原生過期／不存在 User 沒另造 JWT，由同版自動 tests 承接，沒有宣稱它們已做原生 HTTP。

每個原生拒絕案例都比對該次新會員、Forms、Questions、Options 完整列 digest 及 Response／Answer 計數前後相同；大頁碼 GET 同樣無寫入。原生 Check 保存實際 response body、期待 code／path，但不記錄 headers／Cookie／JWT／request password。

## 6. 資料保留與驗收影響

起始既有 rows：Users=8、Identities=0、Forms=22、Questions=47、Options=18、Responses=20、Answers=23。初次 full 後七表 digest 相等；原生自測增加 2 個合法合成 Users／6 Forms／6 Questions／6 Options，無新增 Response／Answer，全部保留。Owner 目前 active、首張 Form Closed、其餘 Draft；Disabled probe 已 finally 復原，拒絕 probe 的第三會員沒有新增。

保留 native Owner `232d0951-10eb-4398-9444-8d66c0d08aa3`、Other `f6418b20-d3f7-419c-8dc2-94328d5ac4c9`，六張 Form IDs 見本機 RunId `-fixtures.json`。排除這兩個精確 IDs 後，舊資料 digest 仍是：

| Table | 舊 rows | 原生後舊列 MD5（與起始一致） |
| --- | --- | --- |
| users | 8 | `a7f312cb0a1d527e93b46ac312cc46d4` |
| user_identities | 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| forms | 22 | `8f68b9854f4b05102655f05d1b5a9546` |
| questions | 47 | `6ca0954fa06c5f29fdf11fc607e4cc07` |
| options | 18 | `304bc35f6bbe633248acad863af29c0a` |
| responses | 20 | `2637149d9e0b9b6fd704b08389eaaba9` |
| answers | 23 | `2738bacc148a57b31663bcec22baf71d` |

沒有移除開發 volume、重建 Schema、清除歷史 fixtures 或更改持久設定。自動 tests 只清理其自己的精確 email 資料，native fixtures 保留；可用 password／Cookie／JWT 只在記憶體、結束清除，不提交原始 artifacts。

本次沒有修改 [整合 Markdown](../test-plan/R0_R9_Integration_Acceptance_Test.md)／[.http](../test-plan/R0_R9_Integration_Manual_Tests.http)，也沒有執行其中任何正式驗收操作。12a～d 的原預期仍正確，後续 candidate／02 suite 前置須對應修正 commit；S-01 barrier 優先由 02 的同版 suite 承接，不重複建立原生矩陣。舊開發／验收 PASS 是舊版本歷史，不能代填修正版共用路徑的新結果；R7／R9 Owner pending 與歷史 accepted 原樣保留。

## 7. 提交、剩餘項目與停止點

產品／tests 修正提交：`e22db98df4d7f23440e628327e0c571ba0745828`（`fix: resolve pre-R10 review regressions`）。文件另採單一目的提交，最終文件 SHA 與 Git state 於交付訊息列明。修正 commit 只有六個 API 檔與一個 test partial；正式規格、DB migrations／Schema、整合 paired files 均無 diff。原始 logs／TRX／SQL／native probe 證據保留於忽略的 RunDir，不含於 commit；本報告保存失敗摘要及證據索引。

未執行正式整合驗收、人工驗收、Linux fallback、clean-room migration rebuild、正式 HTTPS／CORS browser 實測、R10／G1；I-01 README 與 I-02 List Count／items 快照不在此次修正。正式驗收文件維持待 review／未執行，不修改其期待值或歷史狀態。

停止於 **「局部修正開發完成，待 reviewer 審查」**；開發 PASS 不代替獨立 reviewer 或正式驗收。此次沒有待處理的 F-01～03／S-01 開發失敗，後續正式整合計畫 02 應承接本版 barrier suite。
