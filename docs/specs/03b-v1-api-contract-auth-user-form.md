# 自建表單設計服務 V1 — API Contract Part B：Auth、User、Form

| 項目 | 內容 |
| --- | --- |
| 版本 | 1.0 |
| 日期 | 2026-09-28 |
| 狀態 | 正式規格 |
| Base Path | `/api/v1` |
| 範圍 | Authentication、User、Form；Endpoint 1～14 |
| 必要前置 | [Part A：Common](03a-v1-api-contract-common.md) |
| 配套文件 | [Part C：Submission／Statistics](03c-v1-api-contract-submission-statistics.md)、[Domain](01-v1-domain-specification.md)、[Database](02-v1-database-specification.md)、[Errors](04-v1-error-catalog.md)、[Constraints](05-v1-system-constraints.md) |

## 1. 文件目的與適用範圍

本文件定義 V1 Auth、User、Form API 的 Resource Shape、Request、Response、驗證流程與 Endpoint Error 對照。

所有共通 JSON、Datetime、Cookie、Authentication Stage、Error Envelope、Early Return 與 Pagination Scope 先依 Part A。本文件明確定義的 Endpoint 例外優先於共通基線。

## 2. Endpoint 總表

| # | Method | Path | 身分／權限 | 成功 |
| --- | --- | --- | --- | --- |
| 1 | POST | `/auth/register` | Public | 201 User |
| 2 | POST | `/auth/login` | Public | 200 User + Cookie |
| 3 | POST | `/auth/logout` | 不要求有效登入 | 204 + Clear Cookie |
| 4 | POST | `/auth/google` | Public | 200 GoogleAuth + Cookie |
| 5 | GET | `/users/me` | Active User | 200 User |
| 6 | PATCH | `/users/me` | Active User | 200 User |
| 7 | POST | `/forms` | Active User | 201 FormDetail |
| 8 | GET | `/forms` | Active User；只列自己的 Form | 200 FormSummary Page |
| 9 | GET | `/forms/{formId}` | Active User；Draft 僅 Owner | 200 FormDetail |
| 10 | PATCH | `/forms/{formId}` | Owner；Draft | 200 FormDetail |
| 11 | DELETE | `/forms/{formId}` | Owner | 204 |
| 12 | POST | `/forms/{formId}/publish` | Owner；Draft | 200 FormDetail |
| 13 | POST | `/forms/{formId}/close` | Owner；Published | 200 FormDetail |
| 14 | PATCH | `/forms/{formId}/result-visibility` | Owner | 200 FormDetail |

V1 沒有獨立 Question／Option CRUD Endpoint。Question 與 Option 只能隨 Form Aggregate 建立或在 Draft Form PATCH 中修改。

## 3. Resource Shapes

### 3.1 User

```json
{
  "id": "user-id",
  "email": "eddie@example.com",
  "displayName": "Eddie",
  "avatarUrl": null
}
```

- `id`、`email`、`displayName` 為 String。
- `avatarUrl` 為 String 或 `null`。
- 不回傳 passwordHash、status、User Identity、JWT 或 Provider Token。

### 3.2 GoogleAuth

```json
{
  "user": {
    "id": "user-id",
    "email": "eddie@gmail.com",
    "displayName": "Eddie",
    "avatarUrl": "https://example.com/avatar.png"
  },
  "isNewUser": true
}
```

`isNewUser` 只表示本次 Google Login 是否建立新 User。

### 3.3 Option

```json
{
  "id": "option-id",
  "label": "滿意",
  "value": "satisfied",
  "sortOrder": 0
}
```

### 3.4 Question

```json
{
  "id": "question-id",
  "type": "single_choice",
  "title": "是否滿意？",
  "description": null,
  "required": true,
  "sortOrder": 0,
  "config": {
    "variant": "radio"
  },
  "options": [
    {
      "id": "option-id",
      "label": "滿意",
      "value": "yes",
      "sortOrder": 0
    }
  ]
}
```

### 3.5 FormDetail

```json
{
  "id": "form-id",
  "userId": "owner-id",
  "creatorDisplayName": "Eddie",
  "title": "活動回饋",
  "description": null,
  "status": "draft",
  "responseMode": "single",
  "responseIdentity": "anonymous",
  "resultVisibility": "owner_only",
  "submissionDeadlineAt": null,
  "questions": [],
  "createdAt": "2026-09-28T06:30:00Z",
  "updatedAt": "2026-09-28T06:30:00Z"
}
```

- Questions 依 `sortOrder ASC` 回傳。
- Options 依 `sortOrder ASC` 回傳。
- `description`、`submissionDeadlineAt` 可為 `null`。
- `userId` 是 Creator 的內部 User ID；`creatorDisplayName` 是 Creator 目前的 displayName。FormDetail 不揭露 Creator email 或外部 Identity。
- 不含 deletedAt、Question／Option Timestamp、Responses、Answers、Statistics 或動態 Count。
- Published／Closed 使用相同 FormDetail Shape。

### 3.6 FormSummary Page

```json
{
  "items": [
    {
      "id": "form-id",
      "title": "活動回饋",
      "status": "published",
      "responseMode": "single",
      "responseIdentity": "anonymous",
      "resultVisibility": "owner_only",
      "submissionDeadlineAt": null,
      "count": 27,
      "createdAt": "2026-09-28T06:30:00Z",
      "updatedAt": "2026-09-28T06:30:00Z"
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 20,
    "totalItems": 35,
    "totalPages": 2
  }
}
```

`count` 是曾成功提交該 Form 的不同 User 數，不是 Response 筆數。FormSummary 不包含完整 Question／Option Tree。

## 4. Auth API

### 4.1 POST `/auth/register`

#### Request

```json
{
  "email": "eddie@example.com",
  "password": "a password!",
  "displayName": "Eddie"
}
```

允許欄位只有 `email`、`password`、`displayName`，三者皆必填且不可為 `null`。

#### 驗證流程

1. 解析 JSON Body；拒絕未知欄位。
2. 驗證三個欄位存在且為 String。
3. 正規化 email 與 displayName。
4. 驗證 email 格式及最長 254 個 Code Points。
5. 驗證 displayName 非空且最長 100 個 Code Points。
6. 驗證原始 Password 長度為 10～128 個 Code Points；不 trim、不要求字元組合。
7. 查詢正規化 email 是否已屬於任何 User。
8. Hash Password，原子建立 User。
9. 回傳 User；不建立登入狀態。

同一 email 並行 Register 時，DB Unique Conflict 必須映射成 `USER_EMAIL_ALREADY_EXISTS`。

#### 成功

```text
201 Created
```

Body：User。不得設定 `access_token` Cookie。

#### 錯誤

| 情境 | HTTP／Code |
| --- | --- |
| Body／欄位／格式／長度無效 | `400 VALIDATION_ERROR` |
| Email 已存在 | `409 USER_EMAIL_ALREADY_EXISTS` |

### 4.2 POST `/auth/login`

#### Request

```json
{
  "email": "eddie@example.com",
  "password": "a password!"
}
```

#### 驗證流程

1. 解析 JSON Body；只允許 `email`、`password`。
2. 驗證兩個欄位存在、為 String 且非空。
3. 正規化 email；Password 保持原值。
4. 查找 User 並驗證 Password Hash。
5. Email 不存在、沒有 Password 或 Password 錯誤時，一律回相同錯誤。
6. Credential 正確後，確認 User status。
7. 建立本系統 JWT，設定 Cookie，回傳 User。

#### 成功

```text
200 OK
Set-Cookie: access_token=...
```

Body：User。

#### 錯誤

| 情境 | HTTP／Code |
| --- | --- |
| Body 結構或必要欄位無效 | `400 VALIDATION_ERROR` |
| Email／Password 驗證失敗 | `401 AUTH_INVALID_CREDENTIALS` |
| Credential 正確但 User disabled | `403 AUTH_USER_DISABLED` |

### 4.3 POST `/auth/logout`

不需要 Body，也不要求有效登入。

#### 驗證流程

1. 不驗證 JWT，不查詢 User。
2. 設定清除／失效的 `access_token` Cookie。
3. 回傳 204。

有效、缺少、無效或過期 Cookie 都採相同行為。Authentication Middleware 不得先攔截此 Endpoint。

#### 成功

```text
204 No Content
```

Logout 只清除目前 Client Cookie，不撤銷 Server 端 JWT。

### 4.4 POST `/auth/google`

#### Request

```json
{
  "credential": "<Google ID Token>"
}
```

只允許非空 String `credential`。

#### 驗證流程

1. 解析 JSON Body；拒絕未知欄位。
2. 驗證 credential 存在、為 String 且非空。
3. 驗證 Google Token Signature、Issuer、Audience。
4. 驗證 Expiration。
5. 驗證必要安全 Claim，至少包含穩定的 `sub`。
6. 以 `(google, sub)` 查找 User Identity。
7. 若 Identity 已存在：讀取 User，確認 active；不同步 Google Profile。
8. 若 Identity 不存在：要求有效 email 且 `email_verified = true`，再正規化 email。
9. 若 email 已屬於既有 User：拒絕自動合併／綁定。
10. 若 email 未使用：以 Google name 或 email Local Part 建立 displayName，picture 缺少時使用 `null`。
11. 原子建立 User + User Identity。
12. 處理並行唯一性衝突：重新查找相同 Identity；找到則登入既有 User，只有 email 衝突則仍回 Account Conflict。
13. 確認最終 User active，建立本系統 JWT，設定 Cookie。

既有 Identity Login 不得自動覆寫 User 的 email、displayName 或 avatarUrl。

#### 成功

```text
200 OK
Set-Cookie: access_token=...
```

Body：GoogleAuth。新建立 User 時 `isNewUser=true`；既有 Identity 或並行競爭後讀取既有 Identity 時為 `false`。

#### 錯誤

| 情境 | HTTP／Code |
| --- | --- |
| credential 缺少或 Request Shape 無效 | `400 VALIDATION_ERROR` |
| Token Signature／Issuer／Audience 無效，或缺必要 Claim | `401 AUTH_TOKEN_INVALID` |
| Token 已過期 | `401 AUTH_TOKEN_EXPIRED` |
| 新 User 流程缺少有效已驗證 email | `401 AUTH_TOKEN_INVALID` |
| User disabled | `403 AUTH_USER_DISABLED` |
| Email 已存在但沒有相同 Google Identity | `409 AUTH_ACCOUNT_CONFLICT` |

## 5. User API

### 5.1 GET `/users/me`

#### 驗證流程

1. 執行完整 Authentication Stage。
2. 回傳 Authentication Stage 取得的目前 User。

#### 成功

```text
200 OK
```

Body：User。

Client 應使用此 Endpoint 確認目前會員狀態，不以自行解碼 JWT 或前端快取作為 Server 狀態真相。

### 5.2 PATCH `/users/me`

#### Request

```json
{
  "displayName": "Eddie Ko"
}
```

V1 只允許修改 `displayName`。

#### 驗證流程

1. 執行完整 Authentication Stage。
2. 解析 JSON Body；拒絕 id、email、password、status、avatarUrl、Identity 與未知欄位。
3. 空 Object 是合法 no-op，直接回傳目前 User，不更新 updatedAt。
4. displayName 出現時必須是非 null String。
5. Trim 後必須非空且最長 100 個 Code Points。
6. 保存實際變更並更新 updatedAt。
7. 回傳更新後 User。

#### 成功

```text
200 OK
```

Body：User。

#### 錯誤

| 情境 | HTTP／Code |
| --- | --- |
| 欄位、型別、null、內容或唯讀欄位無效 | `400 VALIDATION_ERROR` |
| Authentication 失敗 | 對應 Auth Error |

## 6. Form Create 與 List

### 6.1 POST `/forms`

#### Request

```json
{
  "title": "活動回饋",
  "description": null,
  "responseMode": "single",
  "responseIdentity": "anonymous",
  "resultVisibility": "owner_only",
  "submissionDeadlineAt": null,
  "questions": [
    {
      "type": "single_choice",
      "title": "是否滿意？",
      "description": null,
      "required": true,
      "sortOrder": 0,
      "config": {},
      "options": [
        {
          "label": "滿意",
          "value": "yes",
          "sortOrder": 0
        }
      ]
    }
  ]
}
```

不接受 Client 提供 Form／Question／Option ID、userId、status、createdAt、updatedAt 或 deletedAt。

#### Default

| 欄位 | Default |
| --- | --- |
| description | `null` |
| responseMode | `single` |
| responseIdentity | `anonymous` |
| resultVisibility | `owner_only` |
| submissionDeadlineAt | `null` |
| questions | `[]` |
| status | `draft`；不可由 Client 指定 |

新增 Question 必填 `type`、`title`、`sortOrder`；description 預設 `null`、required 預設 `false`、config 預設 `{}`、options 預設 `[]`。

新增 Option 必填 `label`、`value`、`sortOrder`。

#### 驗證流程

1. 執行完整 Authentication Stage。
2. 解析 Body；拒絕未知及唯讀欄位。
3. 套用 Form／Question Default。
4. 正規化一般文字與 nullable description。
5. 驗證 Form title 必填、非空、最長 200 個 Code Points。
6. 建立完整 Draft Candidate Definition。
7. 驗證 Question／Option ID 不由 Client 指定。
8. 驗證題型、Config Object、允許屬性、已提供欄位、Option、排序與歸屬。
9. Draft 可沒有 Question；Choice 可沒有 Option；Rating 可以缺少 min／max。
10. 原子建立 Form + Questions + Options。
11. 回傳完整 FormDetail。

同一 Domain Validation Stage 可以回傳多筆巢狀 Details。

#### 成功

```text
201 Created
```

Body：FormDetail。

#### 錯誤

| 情境 | HTTP／Code |
| --- | --- |
| Body Envelope、頂層欄位或唯讀欄位無效 | `400 VALIDATION_ERROR` |
| Question／Option／Config／完整 Draft Definition 無效 | `400 FORM_DEFINITION_INVALID` + details |
| Authentication 失敗 | 對應 Auth Error |

### 6.2 GET `/forms`

只列 Current User 擁有且尚未 Soft Delete 的 Forms，不是公開全站 Form List。

#### Query Parameters

| Query | 必填 | Default | 規則 |
| --- | --- | --- | --- |
| `page` | 否 | `1` | 大於等於 1 的 Integer |
| `pageSize` | 否 | `20` | 1～100 的 Integer |
| `status` | 否 | 無 | `draft`／`published`／`closed` |

#### 驗證流程

1. 執行完整 Authentication Stage。
2. 解析 Query；未知、無法解析或超出範圍時拒絕。
3. 依 Current User、`deleted_at IS NULL` 與可選 status 篩選。
4. 依 `updatedAt DESC, id DESC` 穩定排序。
5. 計算 totalItems、totalPages，取得指定 Page。
6. 為每個 Form 計算曾成功提交的不同 User count。
7. 回傳 FormSummary Page。

#### 分頁邊界

- page 超過最後一頁時回 200，`items=[]`，保留指定 page。
- 空清單：`totalItems=0`、`totalPages=0`。
- 不將非法 pageSize 靜默縮成 100。

#### 成功

```text
200 OK
```

Body：FormSummary Page。

#### 錯誤

| 情境 | HTTP／Code |
| --- | --- |
| Query 無法解析或無效 | `400 VALIDATION_ERROR` |
| Authentication 失敗 | 對應 Auth Error |

### 6.3 Pagination Scope

V1 只有 `GET /forms` 使用分頁。Form Detail Questions／Options、Response Receipt Answers、Statistics Questions／Items／Respondents 均依各自 Contract 完整回傳，不得自行加入隱性截斷或分頁。

## 7. Form Read 與 Update

### 7.1 GET `/forms/{formId}`

#### 驗證流程

1. 執行完整 Authentication Stage。
2. 解析 formId UUID。
3. 查找 `deleted_at IS NULL` 的 Form；不存在或已刪除回 Not Found。
4. 若 Form 為 Draft，檢查 Current User 是否為 Owner；非 Owner 對外仍為 Not Found。
5. Published／Closed 不要求 Owner。
6. 讀取 Questions／Options，依 sortOrder 排序。
7. 回傳 FormDetail。

Deadline 已過不影響 Form Detail。resultVisibility 只控制 Statistics，不控制此 Endpoint。

#### 成功矩陣

| Form 狀態 | Owner | 其他 Active User |
| --- | --- | --- |
| draft | 200 | 404 FORM_NOT_FOUND |
| published | 200 | 200 |
| closed | 200 | 200 |
| 不存在／已刪除 | 404 | 404 |

#### 錯誤

| 情境 | HTTP／Code |
| --- | --- |
| formId 不是 UUID | `400 VALIDATION_ERROR` |
| 不存在、已刪除或他人 Draft | `404 FORM_NOT_FOUND` |
| Authentication 失敗 | 對應 Auth Error |

### 7.2 PATCH `/forms/{formId}`

要求 Owner 且 Form 為 Draft。

#### 允許的頂層欄位

- `title`
- `description`
- `responseMode`
- `responseIdentity`
- `submissionDeadlineAt`
- `questions`

不接受 id、userId、creatorDisplayName、status、resultVisibility、createdAt、updatedAt、deletedAt。

#### Nullable 與 Replacement

- `description`、`submissionDeadlineAt` 可以用 `null` 清除。
- Question description 可以用 `null` 清除。
- 其他非 nullable 欄位收到 `null` 時拒絕。
- 未出現欄位保持原值。
- config 出現時完整取代舊 Object，不做 Deep Merge。
- 空 Body `{}` 是合法 no-op，回目前 FormDetail，不更新 updatedAt。

#### Question／Option Delta

| Item Shape | 操作 |
| --- | --- |
| 沒有 id | Create；必須具備建立必要欄位；ID 由 Server 產生 |
| id + 一個以上可修改欄位 | Update 指定欄位 |
| 只有 id | Delete |
| id 不存在或不屬於目前 Parent | Error；不得 Upsert |

`questions: []`、`options: []` 表示沒有 Delta，不表示清空集合。未提及項目保持原樣。

同一次 Request 中：

- 相同 Question ID 最多出現一次。
- 同一 Question 內，相同 Option ID 最多出現一次。
- 同一 Item 不得同時更新與刪除。
- 重複或矛盾操作使整個 PATCH 失敗。

#### Question Type 切換

| Type 變更 | Options 行為 |
| --- | --- |
| single_choice ↔ multiple_choice | 保留現有 Options，再套用明確 Option Delta |
| Choice → text／number／rating | Server 自動刪除該 Question 的全部 Options |
| 非 Choice → Choice | 初始沒有 Options；可在同一次 PATCH 新增 |
| 非 Choice → 另一個非 Choice | 維持沒有 Options |

Type 改變時：

- 若 Request 提供 config，以新 Object 完整取代。
- 若未提供 config，Server 將 config 重設為 `{}`。
- Target Type 為非 Choice 時，`options` 省略或 `[]` 均可；任何非空 Option Delta 都視為矛盾操作。
- Type、Config、Options 與其他 Definition 變更必須原子成功或失敗。

#### 驗證流程

1. 執行完整 Authentication Stage。
2. 解析 formId 與 JSON Body。
3. 查找未刪除 Form。
4. 驗證 Current User 是 Owner。
5. 驗證 status 為 Draft。
6. 驗證頂層允許欄位、null 與 Delta Shape。
7. 驗證 Question／Option ID 歸屬；不揭露錯誤 ID 真正所屬其他資源。
8. 驗證同一 Request 沒有重複或矛盾 Delta。
9. 將 Delta 套入目前 Draft；執行 Type Change 的 Config Reset 與 Options 清理。
10. 對完整 Candidate Definition 正規化並驗證。
11. 排序交換以最終 Candidate 驗證，不以暫時中間狀態判定重複。
12. 原子保存所有變更。
13. 若沒有實際變更，不更新任何 updatedAt。
14. 回傳完整 FormDetail。

#### 錯誤

| 情境 | HTTP／Code |
| --- | --- |
| formId／Body Envelope／頂層欄位無效 | `400 VALIDATION_ERROR` |
| 不存在／已刪除 | `404 FORM_NOT_FOUND` |
| 非 Owner | `403 FORM_ACCESS_DENIED` |
| Published／Closed | `409 FORM_NOT_EDITABLE` |
| Candidate／Question／Option／Config 無效 | `400 FORM_DEFINITION_INVALID` + details |
| Question ID 不在目前 Form | Detail `QUESTION_NOT_FOUND` |
| Option ID 不在目前 Question | Detail `OPTION_NOT_FOUND` |
| Type 與 Option Delta 矛盾 | Detail `QUESTION_TYPE_CONFLICT` |

Question／Option Not Found 是 Detail Code，不把整個 PATCH 改成 HTTP 404。

## 8. Form Delete 與 Lifecycle

### 8.1 DELETE `/forms/{formId}`

Draft／Published／Closed 都可由 Owner Soft Delete，不需要先 Close。

#### 驗證流程

1. 執行完整 Authentication Stage。
2. 解析 formId UUID。
3. 查找未刪除 Form。
4. 驗證 Current User 是 Owner。
5. 以 Server Time 設定 deletedAt；不修改 status，不刪除子資料。
6. 回傳 204。

#### 成功

```text
204 No Content
```

#### 錯誤

| 情境 | HTTP／Code |
| --- | --- |
| formId 無效 | `400 VALIDATION_ERROR` |
| 不存在、已刪除或重複 Delete | `404 FORM_NOT_FOUND` |
| 非 Owner | `403 FORM_ACCESS_DENIED` |
| Authentication 失敗 | 對應 Auth Error |

### 8.2 POST `/forms/{formId}/publish`

不需要 Body。

#### 驗證流程

1. 執行完整 Authentication Stage。
2. 解析 formId UUID。
3. 查找未刪除 Form。
4. 驗證 Current User 是 Owner。
5. 驗證 Lifecycle：Published 使用 Already Published；Closed 使用 Invalid State Transition。
6. 建立完整 Publish Candidate。
7. 驗證至少一個 Question。
8. 驗證所有 Question Type、Title、Config、排序與歸屬。
9. 驗證每個 Choice Question 至少一個有效 Option。
10. 驗證 Rating 具有整數 min／max，且 `0 ≤ min < max ≤ 100`。
11. 若有 submissionDeadlineAt，驗證嚴格晚於 Publish Server Time。
12. 同一 Validation Stage 可以收集多筆發布 Details。
13. 原子將 status 轉為 published；與並行 Patch 形成一致先後。
14. 回傳 FormDetail。

#### 成功

```text
200 OK
```

Body：Published FormDetail。

#### 錯誤

| 情境 | HTTP／Code |
| --- | --- |
| formId 無效 | `400 VALIDATION_ERROR` |
| 不存在／已刪除 | `404 FORM_NOT_FOUND` |
| 非 Owner | `403 FORM_ACCESS_DENIED` |
| 已 Published | `409 FORM_ALREADY_PUBLISHED` |
| 已 Closed | `409 FORM_INVALID_STATE_TRANSITION` |
| Definition／Options／Rating／Deadline 未達發布要求 | `400 FORM_NOT_PUBLISHABLE` + details |

### 8.3 POST `/forms/{formId}/close`

不需要 Body。

#### 驗證流程

1. 執行完整 Authentication Stage。
2. 解析 formId UUID。
3. 查找未刪除 Form。
4. 驗證 Current User 是 Owner。
5. 驗證 Lifecycle：Closed 使用 Already Closed；Draft 使用 Invalid State Transition。
6. 原子將 Published Form 轉為 closed；與並行 Submission 形成一致先後。
7. 回傳 FormDetail。

Close 不重新執行已凍結 Definition 的 Publish Validation。

#### 成功

```text
200 OK
```

Body：Closed FormDetail。

#### 錯誤

| 情境 | HTTP／Code |
| --- | --- |
| formId 無效 | `400 VALIDATION_ERROR` |
| 不存在／已刪除 | `404 FORM_NOT_FOUND` |
| 非 Owner | `403 FORM_ACCESS_DENIED` |
| 已 Closed | `409 FORM_ALREADY_CLOSED` |
| Draft | `409 FORM_INVALID_STATE_TRANSITION` |

### 8.4 PATCH `/forms/{formId}/result-visibility`

#### Request

```json
{
  "resultVisibility": "public"
}
```

只允許 `resultVisibility`，合法值為 `owner_only`、`respondents`、`public`。

#### 驗證流程

1. 執行完整 Authentication Stage。
2. 解析 formId 與 JSON Body。
3. 查找未刪除 Form。
4. 驗證 Current User 是 Owner。
5. 驗證 Body 只有非 null resultVisibility，且 Enum Value 合法。
6. 若值與目前相同，視為成功 no-op，不更新 updatedAt。
7. 否則原子更新 resultVisibility 與 updatedAt。
8. 回傳 FormDetail。

Draft／Published／Closed 都允許此操作。此操作不改變 responseIdentity，也不提前開放 Statistics。

#### 成功

```text
200 OK
```

Body：FormDetail。

#### 錯誤

| 情境 | HTTP／Code |
| --- | --- |
| formId／Body／Enum 無效 | `400 VALIDATION_ERROR` |
| 不存在／已刪除 | `404 FORM_NOT_FOUND` |
| 非 Owner | `403 FORM_ACCESS_DENIED` |
| Authentication 失敗 | 對應 Auth Error |

## 9. Atomicity、Retry 與可觀察行為

| 操作 | 保證／重複呼叫 |
| --- | --- |
| GET | 不產生業務狀態寫入副作用 |
| Register | email 並行衝突穩定映射為 Already Exists |
| Google 首次建立 | User + Identity 一起成功或失敗 |
| Create Form | Form + Questions + Options 一起成功或失敗 |
| Patch Form | 完整 Candidate Definition 一起成功或失敗 |
| Publish／Close | 原子狀態轉移；重複操作依目前狀態回 409 |
| Delete 重試 | 已刪除視為不存在，回 404 |
| Logout 重試 | 清除 Cookie，回 204 |

Patch 與 Publish 的先後以 Server 的一致交易判定點為準；不得讓 Published Definition 被並行中的舊 Patch 修改。

## 10. Part B Endpoint Error 對照

下表省略所有 Endpoint 可能發生的 `500 INTERNAL_SERVER_ERROR`。

| Endpoint | 主要 Error Codes |
| --- | --- |
| POST `/auth/register` | VALIDATION_ERROR、USER_EMAIL_ALREADY_EXISTS |
| POST `/auth/login` | VALIDATION_ERROR、AUTH_INVALID_CREDENTIALS、AUTH_USER_DISABLED |
| POST `/auth/logout` | Token 失效不阻止 204 |
| POST `/auth/google` | VALIDATION_ERROR、AUTH_TOKEN_INVALID、AUTH_TOKEN_EXPIRED、AUTH_USER_DISABLED、AUTH_ACCOUNT_CONFLICT |
| GET `/users/me` | AUTH_REQUIRED、AUTH_TOKEN_INVALID、AUTH_TOKEN_EXPIRED、AUTH_USER_DISABLED |
| PATCH `/users/me` | Auth Errors、VALIDATION_ERROR |
| POST `/forms` | Auth Errors、VALIDATION_ERROR、FORM_DEFINITION_INVALID |
| GET `/forms` | Auth Errors、VALIDATION_ERROR |
| GET `/forms/{formId}` | Auth Errors、VALIDATION_ERROR、FORM_NOT_FOUND |
| PATCH `/forms/{formId}` | Auth Errors、VALIDATION_ERROR、FORM_NOT_FOUND、FORM_ACCESS_DENIED、FORM_NOT_EDITABLE、FORM_DEFINITION_INVALID |
| DELETE `/forms/{formId}` | Auth Errors、VALIDATION_ERROR、FORM_NOT_FOUND、FORM_ACCESS_DENIED |
| POST `/forms/{formId}/publish` | Auth Errors、VALIDATION_ERROR、FORM_NOT_FOUND、FORM_ACCESS_DENIED、FORM_ALREADY_PUBLISHED、FORM_INVALID_STATE_TRANSITION、FORM_NOT_PUBLISHABLE |
| POST `/forms/{formId}/close` | Auth Errors、VALIDATION_ERROR、FORM_NOT_FOUND、FORM_ACCESS_DENIED、FORM_ALREADY_CLOSED、FORM_INVALID_STATE_TRANSITION |
| PATCH `/forms/{formId}/result-visibility` | Auth Errors、VALIDATION_ERROR、FORM_NOT_FOUND、FORM_ACCESS_DENIED |

完整 Error Code 與 Detail Code 定義以 Error Catalog 為準。
