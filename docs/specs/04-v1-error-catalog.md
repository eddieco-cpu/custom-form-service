# 自建表單設計服務 V1 — Error Catalog

| 項目 | 內容 |
| --- | --- |
| 版本 | 1.0 |
| 日期 | 2026-09-28 |
| 狀態 | 正式規格 |
| Base Path | `/api/v1` |
| 範圍 | 23 個 Top-level Error Code、11 個 Detail Code、HTTP 語意、錯誤優先序與 18 支 Endpoint 對照 |
| 配套文件 | [API Common](03a-v1-api-contract-common.md)、[API Auth／User／Form](03b-v1-api-contract-auth-user-form.md)、[API Submission／Statistics](03c-v1-api-contract-submission-statistics.md)、[Domain](01-v1-domain-specification.md)、[Database](02-v1-database-specification.md)、[Constraints](05-v1-system-constraints.md) |

## 1. 文件目的

本文件定義 V1 對外 Error Contract。HTTP Status、`error.code`、`details[].code` 與 Detail Path 規則均屬 Client 可以依賴的介面。

`message` 是人類可讀說明，可以因翻譯或文案調整而改變，不是 Client 分支條件。Client 必須依 HTTP Status、Top-level Error Code 與需要時的 Detail Code 判斷。

程式實作者可以自行設計 Exception、Result Type、Middleware、Filter、Validator 與內部錯誤分類，但回傳給 Client 的結果必須符合本文件。

## 2. Error Envelope

### 2.1 沒有 Details

```json
{
  "error": {
    "code": "AUTH_TOKEN_EXPIRED",
    "message": "Your session has expired. Please sign in again."
  }
}
```

### 2.2 包含 Details

```json
{
  "error": {
    "code": "FORM_DEFINITION_INVALID",
    "message": "The form definition is invalid.",
    "details": [
      {
        "path": "questions[0].config.max",
        "code": "INVALID_QUESTION_CONFIG",
        "message": "The configured maximum is invalid."
      }
    ]
  }
}
```

| 欄位 | 型別 | 規則 |
| --- | --- | --- |
| `error` | Object | 必填；Error Response 的 Root Property |
| `error.code` | String | 必填；穩定的 Top-level Error Code |
| `error.message` | String | 必填；人類可讀，不是穩定 Contract |
| `error.details` | Array | 可省略；提供可定位的驗證細節 |
| `details[].path` | String | 必填；錯誤所在欄位或 Domain 位置 |
| `details[].code` | String | 必填；穩定的 Detail Code |
| `details[].message` | String | 必填；人類可讀，不是穩定 Contract |

V1 不使用其他 Error Envelope，也不得讓框架預設的 ProblemDetails、HTML Error Page 或 422 Response 取代本格式。

### 2.3 Details 使用規則

- 不需要欄位定位時，可以省略 `details`，不得以 `details: null` 代替。
- 同一驗證階段可以回傳一筆或多筆 Details。
- 前一驗證階段失敗後採 Early Return，不為了收集更多 Details 而繼續執行後續階段。
- Details 的陣列順序不構成 Client Contract；Client 不得依索引判斷錯誤類型。
- Detail Code 只描述 Top-level Error 內的具體原因，不自行改變 HTTP Status。

### 2.4 Detail Path

一般欄位使用 camelCase Property Path；Array Item 使用零起算索引：

```text
email
submissionDeadlineAt
questions[0].title
questions[0].options[1].label
answers[0].questionId
answers[0].value
answers[2].value[1]
```

Submission 中，Required Question 完全沒有出現在 Request 時，使用 Question ID 定位：

```text
questions.<questionId>
```

例如：

```json
{
  "path": "questions.51ad4270-0957-4c6c-b108-fc8df63ce6cd",
  "code": "REQUIRED_FIELD",
  "message": "This question is required."
}
```

## 3. HTTP Status 語意

| HTTP | V1 語意 |
| --- | --- |
| 400 Bad Request | Request、Path、Query、欄位、Answer、Form Definition 或 Publish Candidate 無效 |
| 401 Unauthorized | Endpoint 需要有效登入身分，但身分缺少、Token 無效或 Token 已過期 |
| 403 Forbidden | 已辨識身分，但 User disabled 或不具操作／結果存取權 |
| 404 Not Found | 從目前 API 視角找不到資源；包含不存在、Soft Delete 與刻意隱藏存在性 |
| 409 Conflict | Request 與目前帳號、資源狀態、生命週期、Deadline 或一次性限制衝突 |
| 500 Internal Server Error | 未預期且無法映射成既定業務錯誤的 Server Failure |

V1 不使用 422。內容不完整而不能 Publish 是 400；對已 Published 的 Form 再 Publish 是 409。非 Owner 操作是 403；Owner 修改已發布 Definition 是 409。

## 4. 錯誤判定與優先序

### 4.1 一般受保護 Endpoint

一般受保護 Endpoint 依下列階段 Early Return：

```text
Authentication
→ Path／Query／Body 可解析性
→ Resource Existence
→ Authorization
→ Lifecycle
→ Request／Domain Validation
→ Atomic Write 或 Query
```

每支 Endpoint 的精確順序仍以 API Contract 的「驗證流程」為準。Public、Logout、Google Login 與 Statistics 不機械式套用不需要的階段。

### 4.2 同時成立的錯誤

| 情境 | 優先回傳 |
| --- | --- |
| Protected Endpoint 同時沒有有效登入且 formId／Body 無效 | Authentication Error |
| Form 不存在或已 Soft Delete，且狀態也不符合操作 | `FORM_NOT_FOUND` |
| 非 Owner 嘗試修改 Published／Closed Form Definition | `FORM_ACCESS_DENIED`；Authorization 先於 Lifecycle |
| Owner 修改 Published／Closed Form Definition | `FORM_NOT_EDITABLE` |
| Submit 的 Form 非 Published，且 Deadline／Answers 也無效 | `FORM_NOT_SUBMITTABLE` |
| Submit 同時逾期且 single 已提交 | `SUBMISSION_DEADLINE_EXCEEDED` |
| single 並行提交競爭 | 一個成功；其餘映射為 `RESPONSE_ALREADY_SUBMITTED` |
| Email 並行註冊造成 Unique Conflict | `USER_EMAIL_ALREADY_EXISTS` |
| Google Identity 並行建立衝突 | 重新讀取相同 Identity；只有 email 衝突時回 `AUTH_ACCOUNT_CONFLICT` |

### 4.3 Statistics 特殊順序

Statistics 是否需要 Authentication 取決於 Form Lifecycle 與 resultVisibility：

- Draft：只有有效 Active Owner 可以得到 `FORM_STATISTICS_NOT_AVAILABLE`；其他呼叫者得到 `FORM_NOT_FOUND`。
- Published：有效 Active User 得到 `FORM_STATISTICS_NOT_AVAILABLE`；沒有有效 Active 身分的呼叫者得到 `FORM_NOT_FOUND`。
- Closed + public：不要求 Authentication；無效、過期或 Disabled User Cookie 視為匿名，不產生 Auth Error。
- Closed + owner_only／respondents：執行完整 Authentication，再依 Visibility 判定。

不存在或已 Soft Delete 的 Form 一律先視為 `FORM_NOT_FOUND`。Closed Public 且零 Respondent 是合法的 200 Response，不是 Error。

## 5. Authentication／User Codes

| Code | HTTP | 觸發條件 |
| --- | --- | --- |
| `AUTH_REQUIRED` | 401 | Endpoint 需要有效身分，但 Request 沒有 `access_token` Cookie |
| `AUTH_INVALID_CREDENTIALS` | 401 | Email／Password Login 失敗；不區分 email 不存在、沒有 Password Identity 或 Password 錯誤 |
| `AUTH_TOKEN_INVALID` | 401 | 本系統 JWT 或 Google ID Token 的格式、Signature、Issuer、Audience、必要 Claim 無效；亦用於合法系統 JWT 對應 User 不存在 |
| `AUTH_TOKEN_EXPIRED` | 401 | 本系統 JWT 或 Google ID Token 已過期 |
| `AUTH_USER_DISABLED` | 403 | Credential 已辨識到 User，但 User status 為 disabled |
| `USER_EMAIL_ALREADY_EXISTS` | 409 | Register 使用的正規化 email 已屬於任何 User，包含 Google-only User |
| `AUTH_ACCOUNT_CONFLICT` | 409 | Google Identity 尚未存在，但該 Google email 已屬於其他既有 User；V1 不自動合併或綁定 |

### 5.1 Authentication 邊界

- Login 先驗證 Credential，再檢查 User status；Password 錯誤時不揭露帳號是否 disabled。
- Google Token 缺少已驗證 email，只在建立新 User 所需的流程中映射為 `AUTH_TOKEN_INVALID`。
- 既有 Google Identity 不因 Google Profile 改變而更新 User Profile。
- Logout 不要求有效身分。缺少、無效或過期 Cookie 都清除 Cookie 並回 204。
- Closed Public Statistics 的壞 Cookie 不回 Auth Error。

## 6. Validation／Form Definition Codes

| Code | HTTP | 觸發條件 |
| --- | --- | --- |
| `VALIDATION_ERROR` | 400 | 一般 Path、Query、Body、欄位、型別、null、未知／唯讀欄位或 Submission Answer 驗證失敗 |
| `FORM_DEFINITION_INVALID` | 400 | Create／Patch 的 Question、Option、Config、Delta 或完整 Draft Candidate Definition 無效 |
| `FORM_NOT_PUBLISHABLE` | 400 | Draft 未達 Publish 完整性要求 |

`FORM_DEFINITION_INVALID` 不用於 Submission Answer；Submission 使用 `VALIDATION_ERROR`。Publish Candidate 不完整時使用 `FORM_NOT_PUBLISHABLE`，不退回一般 Definition Code。

### 6.1 Detail Codes

| Code | 意義 | 典型情境 |
| --- | --- | --- |
| `REQUIRED_FIELD` | 必要欄位或答案缺少 | Form／Question／Option 必填內容缺少；Required Question 未作答 |
| `INVALID_VALUE` | 值、型別或值域不合法 | Enum、null、字串長度、Answer JSON Type、Rating Answer 超出範圍 |
| `QUESTION_NOT_FOUND` | 目標 Form 範圍內找不到 Question | ID 不存在或屬於其他 Form；對外不區分 |
| `OPTION_NOT_FOUND` | 目標 Question 範圍內找不到 Option | ID 不存在或屬於其他 Question；對外不區分 |
| `INVALID_SORT_ORDER` | sortOrder 格式或範圍無效 | 非 Integer 或小於零 |
| `DUPLICATE_SORT_ORDER` | 同一 Parent 的最終 sortOrder 重複 | 同一 Form 的 Questions，或同一 Question 的 Options |
| `INVALID_QUESTION_TYPE` | Question Type 不支援 | 非 text、number、rating、single_choice、multiple_choice |
| `INVALID_QUESTION_CONFIG` | Config 不符合目標 Question Type | 非 Object、未知 Property、型別錯誤，或 Rating 不符合整數 `0 ≤ min < max ≤ 100` |
| `QUESTION_TYPE_CONFLICT` | Type Change 與明確 Delta 互相矛盾 | Target Type 為非 Choice，Request 卻提供非空 Option Delta |
| `INVALID_OPTION` | Option Definition 無效 | Label／Value／Shape 無效，且沒有更精確 Detail Code |
| `INSUFFICIENT_OPTIONS` | Publish 時 Choice Option 不足 | single_choice／multiple_choice 沒有至少一個有效 Option |

`QUESTION_NOT_FOUND` 與 `OPTION_NOT_FOUND` 在 Create／Patch／Submission Validation 中是 Detail Code，不把父 Response 改成 HTTP 404。

Request 內的重複 ID 不新增專屬 Detail Code，統一使用 `INVALID_VALUE`，且 `details.path` 指向第二次出現的位置：

| 情境 | Top-level Code | Path 形式 |
| --- | --- | --- |
| Submission `answers[]` 重複 questionId | `VALIDATION_ERROR` | `answers[i].questionId` |
| multiple_choice Answer 重複 Option ID | `VALIDATION_ERROR` | `answers[i].value[j]` |
| Form Patch 重複 Question ID | `FORM_DEFINITION_INVALID` | `questions[i].id` |
| Form Patch 同一 Question 內重複 Option ID | `FORM_DEFINITION_INVALID` | `questions[i].options[j].id` |

### 6.2 Publish 完整性

`FORM_NOT_PUBLISHABLE` 可以搭配 Details，至少涵蓋：

- Form 沒有 Question。
- Choice Question 沒有至少一個有效 Option。
- Rating 缺少 min／max，或不符合整數 `0 ≤ min < max ≤ 100`。
- submissionDeadlineAt 不是未來時間。
- 其他使完整 Publish Candidate 不合法的 Definition 問題。

## 7. Form Resource／Authorization Codes

| Code | HTTP | 觸發條件 |
| --- | --- | --- |
| `FORM_NOT_FOUND` | 404 | Form 不存在、已 Soft Delete，或 Endpoint 規定隱藏該 Form 的存在性 |
| `FORM_ACCESS_DENIED` | 403 | Active User 已登入，但不是 Owner，卻嘗試編輯、Publish、Close、Delete、調整 Result Visibility 或查 Respondent Count |
| `FORM_NOT_EDITABLE` | 409 | Owner 嘗試修改 Published／Closed Form Definition |

`FORM_NOT_FOUND` 的存在性隱藏至少包含：

- Form Detail 查詢他人的 Draft。
- 本人 Latest Response 查詢他人的 Draft。
- Statistics 對無法形成相應有效身分的 Draft／Published 探查。

非 Owner 的一般管理操作使用 `FORM_ACCESS_DENIED`，不為了隱藏存在性一律改成 404。

## 8. Lifecycle Codes

| Code | HTTP | 觸發條件 |
| --- | --- | --- |
| `FORM_ALREADY_PUBLISHED` | 409 | Owner 對 Published Form 再執行 Publish |
| `FORM_ALREADY_CLOSED` | 409 | Owner 對 Closed Form 再執行 Close |
| `FORM_INVALID_STATE_TRANSITION` | 409 | Draft 執行 Close、Closed 執行 Publish，或其他 V1 不支援的狀態轉移 |

專用重複操作碼優先於泛用 State Transition：

| 操作／目前狀態 | Code |
| --- | --- |
| Publish／Published | `FORM_ALREADY_PUBLISHED` |
| Publish／Closed | `FORM_INVALID_STATE_TRANSITION` |
| Close／Closed | `FORM_ALREADY_CLOSED` |
| Close／Draft | `FORM_INVALID_STATE_TRANSITION` |

V1 不支援回復 Draft 或 Reopen，也不接受透過 PATCH status 執行生命週期轉移。

## 9. Submission／Response Codes

| Code | HTTP | 觸發條件 |
| --- | --- | --- |
| `FORM_NOT_SUBMITTABLE` | 409 | 未刪除 Form 的 status 不是 Published |
| `SUBMISSION_DEADLINE_EXCEEDED` | 409 | `submissionTime >= submissionDeadlineAt`；到期不自動 Close |
| `RESPONSE_ALREADY_SUBMITTED` | 409 | responseMode=single 的 Form + User 已成功提交，包含並行競爭失敗 |
| `RESPONSE_NOT_FOUND` | 404 | 本人 Latest Response 查詢沒有符合的 Response，或 Owner 查詢 Draft 的本人最新 Response |

已 Soft Delete 的 Form 使用 `FORM_NOT_FOUND`，不是 `FORM_NOT_SUBMITTABLE` 或 `RESPONSE_NOT_FOUND`。

Submission 的 Question／Option 歸屬、Required、Answer Type、Range 與 Multiple Choice 重複等問題使用：

```text
400 VALIDATION_ERROR + details
```

## 10. Statistics Codes

| Code | HTTP | 觸發條件 |
| --- | --- | --- |
| `FORM_STATISTICS_NOT_AVAILABLE` | 409 | Form 尚未 Closed，且呼叫者依 Endpoint 規則可以得知目前 Statistics 尚不可用 |
| `STATISTICS_ACCESS_DENIED` | 403 | Closed Private Statistics 已完成 Authentication，但 Active User 不符合 resultVisibility |

Closed Private Statistics 的 Authentication 失敗仍使用第 5 節 Auth Codes：

| 情境 | Code |
| --- | --- |
| 沒有 Cookie | `AUTH_REQUIRED` |
| JWT 無效／User 不存在 | `AUTH_TOKEN_INVALID` |
| JWT 已過期 | `AUTH_TOKEN_EXPIRED` |
| User disabled | `AUTH_USER_DISABLED` |
| Active User 不符合 owner_only／respondents | `STATISTICS_ACCESS_DENIED` |

Anonymous Statistics 直接省略 respondents，不回額外錯誤。Closed 且零資料回 200，不使用 Statistics Not Found 類型的錯誤。

## 11. Server Code

| Code | HTTP | 觸發條件 |
| --- | --- | --- |
| `INTERNAL_SERVER_ERROR` | 500 | 未預期且無法映射成既定業務錯誤的 Server Failure |

```json
{
  "error": {
    "code": "INTERNAL_SERVER_ERROR",
    "message": "An unexpected error occurred."
  }
}
```

Response 不得包含 Stack Trace、原始 SQL、Constraint 名稱、Credential、JWT、Password Hash、Provider Secret 或基礎設施資訊。

已知的 DB／Transaction Conflict 必須映射成既定 Code，例如：

- email Unique Conflict → `USER_EMAIL_ALREADY_EXISTS`
- single Submission Unique Conflict → `RESPONSE_ALREADY_SUBMITTED`
- Google Identity 競爭 → 重讀相同 Identity 或映射 `AUTH_ACCOUNT_CONFLICT`

## 12. Endpoint Error 對照

下表省略所有 Endpoint 可能發生的 `500 INTERNAL_SERVER_ERROR`。

`Protected Auth` 代表依情境適用：

```text
AUTH_REQUIRED
AUTH_TOKEN_INVALID
AUTH_TOKEN_EXPIRED
AUTH_USER_DISABLED
```

| # | Endpoint（省略 `/api/v1`） | 主要 Top-level Error Codes |
| --- | --- | --- |
| 1 | POST `/auth/register` | `VALIDATION_ERROR`、`USER_EMAIL_ALREADY_EXISTS` |
| 2 | POST `/auth/login` | `VALIDATION_ERROR`、`AUTH_INVALID_CREDENTIALS`、`AUTH_USER_DISABLED` |
| 3 | POST `/auth/logout` | Token 缺少、無效或過期不阻止 204 |
| 4 | POST `/auth/google` | `VALIDATION_ERROR`、`AUTH_TOKEN_INVALID`、`AUTH_TOKEN_EXPIRED`、`AUTH_USER_DISABLED`、`AUTH_ACCOUNT_CONFLICT` |
| 5 | GET `/users/me` | Protected Auth |
| 6 | PATCH `/users/me` | Protected Auth、`VALIDATION_ERROR` |
| 7 | POST `/forms` | Protected Auth、`VALIDATION_ERROR`、`FORM_DEFINITION_INVALID` |
| 8 | GET `/forms` | Protected Auth、`VALIDATION_ERROR` |
| 9 | GET `/forms/{formId}` | Protected Auth、`VALIDATION_ERROR`、`FORM_NOT_FOUND` |
| 10 | PATCH `/forms/{formId}` | Protected Auth、`VALIDATION_ERROR`、`FORM_NOT_FOUND`、`FORM_ACCESS_DENIED`、`FORM_NOT_EDITABLE`、`FORM_DEFINITION_INVALID` |
| 11 | DELETE `/forms/{formId}` | Protected Auth、`VALIDATION_ERROR`、`FORM_NOT_FOUND`、`FORM_ACCESS_DENIED` |
| 12 | POST `/forms/{formId}/publish` | Protected Auth、`VALIDATION_ERROR`、`FORM_NOT_FOUND`、`FORM_ACCESS_DENIED`、`FORM_ALREADY_PUBLISHED`、`FORM_INVALID_STATE_TRANSITION`、`FORM_NOT_PUBLISHABLE` |
| 13 | POST `/forms/{formId}/close` | Protected Auth、`VALIDATION_ERROR`、`FORM_NOT_FOUND`、`FORM_ACCESS_DENIED`、`FORM_ALREADY_CLOSED`、`FORM_INVALID_STATE_TRANSITION` |
| 14 | PATCH `/forms/{formId}/result-visibility` | Protected Auth、`VALIDATION_ERROR`、`FORM_NOT_FOUND`、`FORM_ACCESS_DENIED` |
| 15 | POST `/forms/{formId}/responses` | Protected Auth、`VALIDATION_ERROR`、`FORM_NOT_FOUND`、`FORM_NOT_SUBMITTABLE`、`SUBMISSION_DEADLINE_EXCEEDED`、`RESPONSE_ALREADY_SUBMITTED` |
| 16 | GET `/forms/{formId}/responses/me/latest` | Protected Auth、`VALIDATION_ERROR`、`FORM_NOT_FOUND`、`RESPONSE_NOT_FOUND` |
| 17 | GET `/forms/{formId}/respondent-count` | Protected Auth、`VALIDATION_ERROR`、`FORM_NOT_FOUND`、`FORM_ACCESS_DENIED` |
| 18 | GET `/forms/{formId}/statistics` | 依 Lifecycle／Visibility 適用 `VALIDATION_ERROR`、Auth Codes、`FORM_NOT_FOUND`、`FORM_STATISTICS_NOT_AVAILABLE`、`STATISTICS_ACCESS_DENIED` |

每支 Endpoint 的精確 Early Return 順序以 API Contract 為準；本表只列可能出現的主要 Top-level Codes。

## 13. Catalog 維護規則

- 同一語意必須優先重用既有 Code。
- 不得把 DB Constraint 名稱、Exception Class、Framework Error Name 或 Provider Error 原文直接當成 API Code。
- 不得因實作框架不同而改用另一種 Error Envelope 或 HTTP Status。
- 新增、刪除或改變 Code 語意，均屬 API Contract 變更。
- 只調整 `message` 文案或翻譯，不視為 Code Contract 變更。
- Log 可以保存必要的內部診斷資訊，但不得把敏感資訊回傳給 Client。
