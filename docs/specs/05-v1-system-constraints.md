# 自建表單設計服務 V1 — System Constraints

| 項目 | 內容 |
| --- | --- |
| 版本 | 1.0 |
| 日期 | 2026-09-28 |
| 狀態 | 正式規格 |
| 職責 | 跨 Domain、Database、API 與實作層的安全、完整性、交易、一致性及工程邊界 |
| 配套文件 | [Overview](00-v1-overview.md)、[Domain](01-v1-domain-specification.md)、[Database](02-v1-database-specification.md)、[API Common](03a-v1-api-contract-common.md)、[API Auth／User／Form](03b-v1-api-contract-auth-user-form.md)、[API Submission／Statistics](03c-v1-api-contract-submission-statistics.md)、[Errors](04-v1-error-catalog.md) |

## 1. 文件目的與效力

本文件定義程式實作者在設計、開發、測試與部署 V1 時必須遵守的跨層約束。它不指定唯一的程式架構，也不取代其他正式規格。

| 文件 | 權威範圍 |
| --- | --- |
| 00 Overview | 產品目標、V1 能力與文件導覽 |
| 01 Domain | 業務概念、Invariant、Lifecycle、Submission、Statistics 語意 |
| 02 Database | Table、Column、Type、Nullability、Default、Key、Constraint、Index、Delete Behavior |
| 03 API | Route、Request、Response、驗證流程與外部可觀察行為 |
| 04 Errors | Error Envelope、HTTP Status、Top-level Code、Detail Code 與錯誤優先序 |
| 05 Constraints | 跨層一致性、安全基線、交易保證、工程自由度與驗收邊界 |

遇到疑似衝突時，先依上表確認議題所屬的權威文件。無法消除的矛盾必須在實作前提出，不得由程式實作者自行改變 Domain、Schema、API 或 Error Contract。

## 2. 技術基線與交付範圍

| 項目 | V1 基線 |
| --- | --- |
| Backend | ASP.NET Core Web API／C# |
| Database | PostgreSQL |
| Data Access | EF Core |
| API Style | JSON over HTTP，Base Path `/api/v1` |
| Authentication | Email／Password、Google ID Token、本系統 JWT Cookie |
| 初期開發 | 允許完全從本機環境開始 |
| Deployment | 不鎖定特定雲端或容器平台 |

具體 .NET SDK、EF Core、PostgreSQL、Driver 與其他 Package Version 由程式實作者選定並記錄，但必須彼此相容，且不得改變正式規格。

本機可以使用 Docker Compose 啟動 PostgreSQL，也可以使用其他可重現的本機 PostgreSQL。部署環境是否使用 Container，不要求與本機啟動方式完全相同；Database Feature、Timezone、Collation 與 Migration 結果必須相容。

Google 真實專案、正式 Domain、雲端 Database 與 Deployment 可以分階段整合。尚未完成的外部整合必須在階段交付中明列，不能以 Email／Password 的本機功能完成宣稱整個 V1 已完成。

## 3. 設定與環境

### 3.1 設定來源

下列內容必須透過環境設定、Secret Store、Development User Secrets 或等價機制提供，不得寫死於 Source Code 或提交至版本控制：

- Database Connection String
- JWT Signing Key／Certificate
- JWT Issuer、Audience、Lifetime
- Google Client ID 及驗證所需設定
- CORS Allowed Origins
- Cookie Secure 等環境差異設定
- Logging Level 與必要的外部服務設定

Repository 可以提供不含秘密的設定範例與啟動說明。

### 3.2 環境差異

- 正式環境必須使用 HTTPS。
- 開發環境可以允許 localhost HTTP，因此 Cookie Secure 可以為 false。
- 正式環境的 Cookie Secure 必須為 true。
- 開發、測試與正式環境必須使用不同 Secret。
- Server、Database 與測試不得依賴開發者電腦的 Local Timezone。

### 3.3 Database Migration

- Schema 必須由可重現的 Migration 或等價版本化流程建立。
- 不得以應用啟動時的未審核破壞性同步，自動刪除或改寫正式資料。
- Migration 必須保留 02 定義的 7 張 Domain Table 與全部既定 Constraint／Index。
- EF Core 的 Migration History 等 Framework Metadata 不視為新增 Domain Table。
- Schema 變更必須先更新正式規格，再產生對應 Migration。

## 4. Authentication 與 Cookie 安全

### 4.1 JWT

- 本系統 JWT 由 Server 簽發。
- JWT `sub` 對應內部 `users.id`。
- 每次受保護請求都必須驗證格式、Signature、Issuer、Audience 與 Expiration。
- JWT 合法後仍須讀取 User，確認存在且 status=active。
- JWT 內的 displayName、avatarUrl 或其他可變 Profile 資料不得作為目前資料真相。
- Token Lifetime 是 Server Configuration，V1 固定為 24 小時（1,440 分鐘）；所有環境必須使用此值。V1 不提供 Refresh Token。
- JWT 過期後必須重新登入。

V1 不提供 Session Store、Token Revocation List、Token Version 或全裝置強制登出。Logout 只清除目前 Client 的 Cookie。

### 4.2 Cookie Contract

| 設定 | 本機 HTTP | 正式 HTTPS |
| --- | --- | --- |
| Name | `access_token` | `access_token` |
| HttpOnly | true | true |
| Secure | false | true |
| SameSite | Lax | Lax |
| Path | `/` | `/` |
| Domain | 不設定；Host-only | 不設定；Host-only |

V1 不另外提供 Authorization Bearer Transport，也不在 Login Response Body 回傳 Token。

### 4.3 CORS 與 CSRF 邊界

- 需要 Cookie 的 Browser Client 必須使用 Credentialed Request。
- Server 只允許明列的 Client Origin。
- 啟用 Credentials 時不得使用 `Access-Control-Allow-Origin: *`。
- 寫入 Request 使用 JSON，並遵守 API 的 Content-Type Contract。
- Host-only Cookie、SameSite=Lax、正式 Secure、HTTPS 與嚴格 CORS 共同構成 V1 的 Browser Request 安全基線。

V1 沒有獨立 CSRF Token Protocol。若正式部署改成 Cross-site 拓撲、Iframe 嵌入或需要 SameSite=None，必須在上線前重新評估 CSRF 與 Cookie Policy；不能只修改 Cookie Flag 而不檢查安全影響。

## 5. Credential、Secret 與個資

### 5.1 Password

- Password 只保存安全雜湊，不得保存明文或可逆加密結果。
- Hash 實作必須使用成熟、具 Salt 且可調整 Work Factor 的 Password Hashing 方案。
- Hash Algorithm 與 Cost 可以由程式實作者依平台能力選擇，但必須符合 `password_hash` 欄位容量。
- Password 不 trim、不轉小寫、不做 Unicode Normalization。
- Password Log、Metric、Trace、Exception 與 Error Response 均不得包含原文。

### 5.2 Google Credential

- Google ID Token 必須在 Server 驗證 Signature、Issuer、Audience、Expiration 與必要 Claims。
- 只使用驗證後的 Provider `sub`、email、email_verified、name、picture。
- 不相信 Client 自行宣告的 email、Provider User ID 或內部 userId。
- 相同 email 但沒有相同 Google Identity 時，不自動合併或綁定既有 User。
- 既有 Identity Login 不同步覆寫 User 的 email、displayName 或 avatarUrl。

### 5.3 資料揭露

- User Response 不包含 passwordHash、status、Provider Token 或 Google `sub`。
- FormDetail 可以包含 Creator 的內部 userId 與目前 displayName，但不包含 Creator email 或外部 Identity。
- Anonymous Statistics 不回 respondents，也不得回可跨 Question 連結身分的替代 ID。
- Identified Statistics 的 Respondent 只包含內部 User ID 與目前 displayName。
- Error Response 不得包含 Stack Trace、SQL、Constraint 名稱、Connection String、Credential、JWT、Password Hash 或 Provider Secret。
- Log 與 Trace 應避免保存 Token、Password、Google Credential 與完整敏感 Request Body。

## 6. Authorization 邊界

V1 使用資源關係與政策，不建立 Role／Permission Table，也不採 RBAC。

Server 必須依下列資訊判定權限：

- Current User
- Form Owner
- 是否曾成功提交而成為 Respondent
- Form status
- Form deletedAt
- resultVisibility
- responseIdentity

Form ID、分享 URL、前端 Route 或隱藏按鈕都不是權限證明。所有 Authentication、Authorization、Lifecycle 與 Visibility 檢查必須在 Server 執行。

Owner 也不能：

- 修改 Published／Closed Definition。
- 在 Published 期間讀取整體 Statistics。
- 繞過 anonymous Statistics 的身分隱藏。
- 因為擁有 Form 而跳過 Submission 的 status、Deadline、Required 或 Answer Validation。

存在性隱藏是 Endpoint Contract 的一部分。程式實作者不得把 404 一律改成 403，或把應使用 403 的管理操作一律改成 404。

## 7. Database 與 Application 責任

### 7.1 Database 必須保護

- Primary Key、Foreign Key、NOT NULL
- Enum／Status 等既定 Check Constraint
- 非負 sortOrder 等既定 Check
- Email 與 Provider Identity 等既定 Unique
- 02 明列的 Index
- `questions.id → options.question_id` 的唯一正常業務 Cascade

Application 已做驗證不代表可以移除上述 Database Constraint。

### 7.2 Application 必須保護

- 文字正規化與長度
- Question Type 與 config 對應
- Choice／非 Choice 的 Options 適用性
- 完整 Candidate Form Definition
- 同一 Parent 的最終 sortOrder 不重複
- Form Lifecycle 與 Definition Freeze
- Deadline
- Answer JSON Type 與 Question Type 對應
- Answer Question 與 Response 必須屬於同一 Form
- Choice Option 必須屬於該 Question
- Statistics、Effective Response 與 Authorization 語意

JSONB 提供儲存彈性，不代表接受任意 Shape，也不能用 JSONB 逃避 Domain Validation。

### 7.3 Delete Behavior

- Form 使用 Soft Delete；V1 不因一般 Delete API 物理刪除 Form、Question、Response 或 Answer。
- Question 在 Draft Definition 中被實際刪除時，Database Cascade 刪除其 Options。
- Choice Question 切換為非 Choice Type 時，由 Application 在同一交易中刪除其全部 Options。
- 其他 Foreign Key 不得因 ORM Default 被自動改成 Cascade 或 SET NULL。
- V1 沒有背景 Hard Delete、Retention Cleanup 或 User Delete 功能。

## 8. 原子性

下列操作必須完整成功或完整失敗：

| 操作 | 原子範圍 |
| --- | --- |
| Google 首次建立 | User + User Identity |
| Create Form | Form + Questions + Options |
| Patch Form | 完整 Candidate Definition 的全部 Delta |
| Question Type 切換 | Type + Config Reset／Replacement + Options 清理／新增 |
| Publish | Publish Validation + status 轉移 |
| Close | status 轉移 |
| Submit | Response + 全部有效 Answers |

不得以 Controller／Service 的函式呼叫順序代替 Transaction 保證，也不得在 Candidate Definition 驗證失敗後留下部分更新。

Transaction、Lock、Isolation Level、Concurrency Token、Retry 與 EF Core API 由程式實作者決定；外部結果必須符合本文件與 API Contract。

## 9. 競爭請求與線性化

### 9.1 Patch 與 Publish

Patch 與 Publish 必須形成單一可解釋的先後：

- Patch 先完成：Publish 以更新後的完整 Candidate 執行驗證。
- Publish 先完成：後續 Patch 必須失敗為 `FORM_NOT_EDITABLE`。
- 不得發生 status 已是 Published，但舊 Patch 仍在其後覆寫 Definition。

### 9.2 Submit 與 Close／Soft Delete

- Submit 先取得有效寫入結果：該 Response 保留，Close／Delete 可以在其後成功。
- Close 先取得有效狀態轉移：後續 Submit 使用 `FORM_NOT_SUBMITTABLE`。
- Soft Delete 先完成：後續 Submit 使用 `FORM_NOT_FOUND`。
- 不得產生在一致交易順序中已 Closed／Deleted 後仍被接受的新 Response。

### 9.3 single Submission

同一 single Form + User 的並行 Submission 最多一個成功：

- 成功者回 201。
- 其他競爭請求回 `RESPONSE_ALREADY_SUBMITTED`。
- 不得只用未受保護的 SELECT 後 INSERT。
- 不得用無條件 `(form_id, user_id)` Unique Constraint 破壞 multiple Mode。

具體實作可以使用符合 02 Schema 的 Transaction、Lock、條件寫入或其他策略。

### 9.4 Google 並行首次登入

- 相同 Provider + Subject 最多建立一個 Identity。
- User + Identity 必須一起成功或失敗。
- Unique Conflict 後重新讀取相同 Identity；存在時登入該既有 User。
- 只有 email 已被其他 User 使用且沒有相同 Identity 時，回 `AUTH_ACCOUNT_CONFLICT`。
- 不得因 Retry 建立孤立 User 或重複 Identity。

### 9.5 Conflict 映射

可預期的 Database／Concurrency Conflict 必須映射成既定 Error Code，不得全部回 500。若競爭後狀態已改變，Server 應依重新確認的目前狀態回傳相應 Contract Error。

## 10. 時間與 Timestamp

- 所有業務時間使用 Server Time。
- API Datetime 使用 UTC ISO 8601。
- Database 使用 02 定義的 UTC 相容 Timestamp Type。
- Client 不得指定 createdAt、updatedAt、submittedAt 或 deletedAt。
- Deadline 判定為 `submissionTime < submissionDeadlineAt`；等於 Deadline 即已截止。
- Deadline 到期不自動 Close，也不改變 Form status。

每次 Submit 必須取得一個唯一的 `submissionTime`：

1. 在一致的交易判定流程中取得 Server UTC Time。
2. 使用同一個值驗證 Deadline。
3. 使用同一個值保存 Response submittedAt。
4. 寫入前重新確認 Form status、deletedAt、Deadline 與 single 一次性仍成立。

Publish 時若有 Deadline，必須嚴格晚於 Publish Server Time。

沒有實際資料變更的合法 no-op 不更新 updatedAt，包括：

- 空 Form PATCH。
- PATCH Candidate 與目前值相同。
- resultVisibility 設為目前相同值。

## 11. Unicode、文字與數值一致性

### 11.1 Unicode 長度

所有 Contract 中的文字長度以 Unicode Code Point 計算，不以：

- UTF-8 Byte
- UTF-16 Code Unit
- 使用者感知 Grapheme Cluster

計算。

.NET `string.Length` 是 UTF-16 Code Unit 數，不能直接作為 Code Point 長度的唯一實作。

### 11.2 Trim 與 Normalization

- 一般文字只移除頭尾 Unicode Property `White_Space=Yes` 的 Code Points。
- 不移除文字中間的 Whitespace。
- 不做 Unicode Normalization。
- 不做一般文字的 Case Folding。
- Email 在一般 trim 後轉為小寫。
- Password 完全不 trim、不轉大小寫、不做 Unicode Normalization。
- Nullable description trim 後為空字串時保存為 null。

所有輸入路徑必須共用一致的正規化行為，不得因 Controller、JSON Converter、EF Core 或不同 Library 而產生差異。

### 11.3 文字等值與排序

- 一般文字等值採正規化後 Code Point Sequence 的精確、大小寫敏感比較。
- Text Statistics 使用正規化後完整 String 分組。
- 不使用 Culture-sensitive 或大小寫不敏感比較合併不同答案。
- Text Statistics 同 count 時，以固定 ordinal／binary value ASC 排序。
- Identified Respondents 以 displayName ordinal／binary ASC，再以 User ID ASC。

Database Collation、Application Comparer 與測試結果必須產生一致的可觀察結果。

### 11.4 數值

- JSON Number 不接受 Number String。
- Rating Answer 必須是 Integer，且落在 Question 的 min／max。
- Rating Config 必須為整數 `0 ≤ min < max ≤ 100`。
- Number Statistics 以數值等值分組，`1` 與 `1.0` 是同一值。
- 不得以字串序列化差異把數值拆成不同群組。
- 不得自行增加 API Contract 未定義的數值範圍、精度或四捨五入規則。

## 12. Request Validation 與 Error 一致性

- 未知欄位與唯讀欄位必須拒絕，不得由 Serializer 靜默忽略。
- 只有 Contract 明確標示 nullable 的欄位接受 JSON null。
- Domain Validation 之前必須先形成可安全處理的 Request Shape。
- 同一驗證階段可以收集多筆 Details；跨階段採 Early Return。
- Request-local 的重複 Question／Option ID 使用 `INVALID_VALUE`，並定位第二次出現的位置。
- Target Question Type 與明確非空 Option Delta 衝突時使用 `QUESTION_TYPE_CONFLICT`。
- 最終 sortOrder 重複使用 `DUPLICATE_SORT_ORDER`。
- 框架 Validation、Domain Error、Database Conflict 與未預期 Exception 必須映射到 04 的 Error Envelope。
- 不得回傳框架 ProblemDetails、HTML Error Page、422 Body、Exception Class 或 Constraint 名稱。

API 的精確驗證次序由每支 Endpoint 的「驗證流程」決定。實作層的 Middleware、Filter 或 Validator 排列不得意外改變這些可觀察行為。

## 13. Query、排序與集合完整性

### 13.1 穩定排序

| 資料 | 排序 |
| --- | --- |
| Form List | updatedAt DESC、id DESC |
| Effective／Latest Response | submittedAt DESC、id DESC |
| ResponseReceipt Answers | Question sortOrder ASC、Question ID ASC |
| Statistics Questions | Question sortOrder ASC |
| Choice Statistics Items | Option sortOrder ASC |
| Rating Statistics Items | value ASC |
| Number Statistics Items | numeric value ASC |
| Text Statistics Items | count DESC、binary value ASC |
| Identified Respondents | binary displayName ASC、User ID ASC |

不得依賴沒有 ORDER BY 的 Database Natural Order。

### 13.2 Pagination

V1 只有 `GET /forms` 使用分頁：

- page Default 1。
- pageSize Default 20，範圍 1～100。
- page 超出最後一頁回 200 + 空 items。
- 空清單 totalItems=0、totalPages=0。
- 排序為 updatedAt DESC、id DESC。

Questions、Options、ResponseReceipt Answers、Statistics Questions、Items 與 Respondents 不分頁，也不得隱性截斷。

### 13.3 Count 與 Effective Response

- FormSummary count 與 Respondent Count 是曾成功提交的不同 User 數，不是 Response 筆數。
- Statistics 每個 Form + User 只使用最新一份完整 Response。
- 最新 Response 沒回答 Optional Question 時，不從舊 Response 補答案。
- Root、Question 與 Item Count 使用各自正式定義。
- Multiple Choice 的 Item Count 加總可以大於 Question Count。
- Identified Item 必須滿足 `item.count == item.respondents.length`。

## 14. Soft Delete 與資料保留

Form Soft Delete 後：

- Form Detail 視為不存在。
- Owner Form List 排除。
- 不再接受 Submission。
- 本人 Latest Response 不可讀取。
- Respondent Count 不可讀取。
- Statistics 不可讀取。
- Questions、Options、Responses、Answers 與 Identities 不因本操作物理刪除。

所有 Query 必須一致套用 Form Soft Delete 邊界，不能只有 Form Repository 套用，而其他 Join／Projection 繞過。

V1 沒有資料保留期限、資料匿名化排程、Hard Delete API 或 Background Cleanup Job。未來若增加，必須另外定義對歷史 Response、Statistics 與個資的影響。

## 15. Idempotency、Retry 與副作用

| 操作 | V1 行為 |
| --- | --- |
| GET | 不產生業務狀態寫入副作用 |
| Logout Retry | 清除 Cookie 並回 204 |
| Empty／No-change PATCH | 回目前 Resource；不更新 updatedAt |
| Delete Retry | 已 Soft Delete 視為不存在，回 404 |
| Publish Retry | 已 Published 回 `FORM_ALREADY_PUBLISHED` |
| Close Retry | 已 Closed 回 `FORM_ALREADY_CLOSED` |
| single Submit Retry | 已成功提交後回 `RESPONSE_ALREADY_SUBMITTED` |
| multiple Submit Retry | 可能建立另一筆 Response |

V1 不支援 Idempotency-Key。Client 對 multiple Submit 在結果不明時重試，必須接受可能新增另一筆 Response 的風險。

## 16. Logging、診斷與錯誤處理

- Server 必須保留足以診斷未預期錯誤與競爭請求的內部 Log。
- Log 不得成為繞過 Statistics／Anonymous Policy 的資料查詢介面。
- Production Error Response 只回正式 Error Envelope。
- INTERNAL_SERVER_ERROR 不得攜帶內部 Exception 或環境資訊。
- 已知 Unique、Foreign Key、Check 與 Concurrency Failure 應映射到正式 Contract 或受控的 500，不直接公開 Database 訊息。
- 是否使用 Correlation ID、Structured Logging、Trace 或外部 Observability 平台由程式實作者決定。

若新增對外 Header、Error Code 或可由 Client 依賴的診斷欄位，必須先更新 API／Error Contract。

## 17. 效能與容量邊界

Question title 與 Option label 刻意不設欄位專屬最大長度；兩者仍須在 trim 後非空。整體 Request Body 安全限制屬獨立 Capacity Policy，不得被誤寫成這兩個欄位的業務上限。

V1 沒有預先承諾：

- SLA／Availability
- 峰值 Concurrent User
- Request Per Second
- Response Time Percentile
- Rate Limit
- Request Body Size
- Form Question／Option 數量上限
- Statistics Respondent 上限
- Response Quota

不得以效能優化為由自行：

- 截斷 Statistics 或 respondents。
- 為未定義的集合加入分頁。
- 改變 Count 或 Effective Response 語意。
- 改成 Eventually Consistent 的預先計算結果。
- 增加 Client 未知的 Response Quota。

程式實作者可以依實際 Query Pattern增加非唯一效能 Index、Projection、No-tracking Query 或其他內部優化。任何會限制輸入、改變輸出或新增可觀察失敗情境的 Capacity Policy，都必須先更新 Contract。

V1 不要求大型分散式基礎設施、Message Broker、Distributed Cache、Search Engine 或 CQRS。新增依賴必須對應具體需求並說明維護成本。

## 18. 實作自由度

在不改變正式規格的前提下，程式實作者可以決定：

- Solution、Project、Layer、Module 與 Folder 結構
- Controller／Minimal API 等 ASP.NET Core 組織方式
- Repository、CQRS、MediatR 或直接使用 DbContext
- DTO、Validator、Result Type 與 Exception Strategy
- EF Core Entity Configuration 與 Migration 組織
- Transaction、Locking、Isolation、Retry 與 Concurrency Strategy
- Password Hash Algorithm 與 Cost
- UUID 版本與 Server 端產生方式
- Constraint／Index 名稱
- 額外非唯一效能 Index
- Query、Projection、Tracking 與 Split Query Strategy
- Local Seed 與測試資料
- Unit、Integration、API 與 Concurrency Test Framework
- Logging、Tracing 與 Configuration Library
- 本機是否以 Docker Compose 啟動 PostgreSQL

上述自由度不得被解讀為可以：

- 增刪或改名 7 張 Domain Table。
- 增刪既定 Column 或改變 Type、Nullability、Default。
- 移除既定 PK、FK、Unique、Check 或 Index。
- 增加未確認的 Cascade、SET NULL 或業務 Unique。
- 改變 Route、Request、Response、Status、Error Code 或驗證順序。
- 改變 Cookie Transport、Authorization 或資料揭露政策。
- 以 JSONB 擴張新的產品功能。
- 新增未定義的 Lifecycle、Role、Quota、Result Resource 或 Response Editing。
- 以內部實作細節改變 Domain 與 API Contract。

## 19. 不建立的 V1 Domain 實體

V1 不建立下列 Domain Table／資源：

- submissions
- results／statistics
- user_credentials
- roles／permissions
- sessions／refresh_tokens
- form_versions
- response_drafts
- answer_history
- quota／usage
- audit_log

未建立 Table 不代表概念不存在：Submission 是操作，Statistics 是 Query Result，Authentication 使用 JWT，Log 可以存在於應用或基礎設施，但不形成 V1 的可查詢 Audit Domain。

## 20. 驗收要求

下列行為必須以自動化測試或可重現的整合驗證證明。測試框架由程式實作者決定。

| 面向 | 必須證明 |
| --- | --- |
| Auth | Register 不自動登入；Password 邊界；Google Token Mapping；同 email 不自動綁定；Logout 不被壞 Token 擋住；disabled 生效 |
| Cookie／CORS | 本機與正式 Flag；明列 Origin；Credentialed Request；正式 HTTPS |
| Unicode | Code Point 長度；White_Space trim；Password 不 trim；一般文字不做 normalization／case fold |
| Form | Draft 可不完整；Publish 完整檢核；發布後不可編輯；他人 Draft 存在性隱藏 |
| Delta | 未提及項目保留；空陣列無操作；id-only 刪除；重複 ID 拒絕；Type Change 原子清理 Options |
| Database | 7 表、Key、Check、Index 與 Delete Behavior 一致；只有 Question → Options Cascade |
| Submission | Authentication、Published、單一時間取樣、嚴格 Deadline、Required、Answer Type、Option 歸屬與正規化 |
| Concurrency | single 同時提交最多一個 201；Patch／Publish、Submit／Close／Delete、Google 首次登入競爭符合線性化規則 |
| Latest／Effective | 相同時間以 ID tie-break；只使用最新完整 Response；不逐題補舊答案 |
| Statistics | Closed-only；完整 Visibility Matrix；Published 存在性保護；Public 壞 Cookie 視為匿名；anonymous 不帶身分 |
| Empty Data | 零人 Closed Statistics 回 200；Choice／Rating 零值項目與其他空陣列正確 |
| Pagination | 只有 Form List 分頁；Default、上限、超界、空清單與穩定排序正確 |
| Soft Delete | Detail、List、Submit、Latest、Count、Statistics 全部一致不可存取 |
| Errors | Status、Top-level Code、Detail Code、Path 與 Early Return 順序一致；不洩漏內部資訊 |
| Retry | Logout、No-op PATCH、Delete、Publish、Close、single／multiple Submit 的重試行為正確 |

## 21. 變更與交付邊界

程式實作者開始前必須閱讀 00～05 的目前正式版本，並提出：

1. 實作範圍與階段。
2. 技術選擇與理由。
3. Transaction／Concurrency Strategy。
4. Validation／Error Mapping Strategy。
5. Migration 與本機啟動方式。
6. 測試與驗收方式。
7. 尚未整合的外部服務或環境項目。

每一階段交付應包含：

- 已完成項目。
- 變更的程式與 Migration。
- 執行過的測試／驗證。
- 對應的正式規格。
- 尚未完成項目。
- 新發現的規格矛盾或風險。

發現需要改變 Domain、Schema、API、Error Code、安全政策或資料揭露時，必須先提出影響與替代方案，再更新規格及實作。不得把尚未核准的工程提案視為正式規則。
