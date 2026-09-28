# 自建表單設計服務 V1 — API Contract Part A：Common

| 項目 | 內容 |
| --- | --- |
| 版本 | 1.0 |
| 日期 | 2026-09-28 |
| 狀態 | 正式規格 |
| Base Path | `/api/v1` |
| 範圍 | 所有 V1 API 共用的傳輸、Authentication、Response、Error、驗證與 Endpoint 索引 |
| 配套文件 | [Part B：Auth／User／Form](03b-v1-api-contract-auth-user-form.md)、[Part C：Submission／Statistics](03c-v1-api-contract-submission-statistics.md)、[Domain](01-v1-domain-specification.md)、[Database](02-v1-database-specification.md)、[Errors](04-v1-error-catalog.md)、[Constraints](05-v1-system-constraints.md) |

## 1. 文件目的與組成

V1 API Contract 由三份文件共同構成：

| 文件 | 職責 |
| --- | --- |
| Part A | 所有 Endpoint 共用的 Contract 與完整 Endpoint 索引 |
| Part B | Auth、User、Form；Endpoint 1～14 |
| Part C | Submission、Latest Response、Respondent Count、Statistics；Endpoint 15～18 |

Part B 與 Part C 必須連同本文件閱讀。個別 Endpoint 的 Request、Response、驗證流程與錯誤集合以其所屬文件為準。

程式實作者可以自行規劃 Controller、Service、Middleware、Validator、Repository 與 Transaction，但外部可觀察行為必須符合完整 API Contract。

## 2. 共通傳輸規格

### 2.1 JSON、命名與型別

- 有 Body 的 Request 使用 `Content-Type: application/json`。
- JSON、Path Parameter 與 Query Parameter 使用 camelCase。
- Enum Value 使用 snake_case，例如 `single_choice`、`owner_only`。
- Error Code 使用 UPPER_SNAKE_CASE。
- ID 是 UUID String；文件中的 `form-id`、`question-id` 等只是易讀占位符。
- Datetime 使用 UTC ISO 8601，例如 `2026-09-28T06:30:00Z`。
- 新資源 ID、Current User、createdAt、updatedAt、submittedAt 與 deletedAt 由 Server 決定。
- 未知欄位與唯讀欄位必須拒絕，不得由 JSON Serializer 靜默忽略。
- 只有 Contract 明確標示 nullable 的欄位接受 JSON `null`。
- JSON Number 不接受 Number String。

### 2.2 文字正規化

- 一般文字移除頭尾 Unicode Property `White_Space=Yes` 的 Code Points。
- 一般文字不做 Unicode Normalization 或 Case Folding。
- Email 在一般 trim 後轉為小寫。
- Password 不 trim、不轉大小寫、不做 Unicode Normalization。
- Nullable description trim 後為空字串時保存為 `null`。
- 文字長度以 Unicode Code Point 計算。

完整文字等值、排序與數值一致性要求見 Domain 與 System Constraints。

## 3. 成功 Response

| 類型 | HTTP | Body |
| --- | --- | --- |
| 單一資源查詢 | 200 | 直接回傳 Resource |
| 建立資源 | 201 | 直接回傳新 Resource |
| PATCH／Publish／Close | 200 | 直接回傳更新後 Resource |
| Logout／Delete | 204 | 無 Body |
| Form List | 200 | `items` + `pagination` |

V1 不使用 `{ success, data }` 等成功 Envelope。204 Response 不得回傳 JSON Body。

## 4. Error Contract

### 4.1 Error Envelope

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

| 欄位 | 規則 |
| --- | --- |
| `error.code` | 必填；穩定的機器識別碼 |
| `error.message` | 必填；人類可讀，不是 Client 分支條件 |
| `error.details` | 可省略；同一驗證階段可以包含多筆 |
| `details[].path` | 錯誤欄位或 Domain 位置 |
| `details[].code` | 穩定 Detail Code |
| `details[].message` | 人類可讀說明 |

完整 HTTP Status、Top-level Code、Detail Code 與 Path 規則以 Error Catalog 為準。

### 4.2 Error 輸出限制

- 不得以框架 ProblemDetails、HTML Error Page 或 422 Body 取代正式 Error Envelope。
- 不得回傳 Stack Trace、原始 SQL、Constraint 名稱、Credential、JWT、Password Hash、Provider Secret 或基礎設施資訊。
- 可預期的 Database／Concurrency Conflict 必須映射成既定業務 Error。

### 4.3 共通 Server Error

所有 Endpoint 都可能在未預期失敗時回：

```text
500 INTERNAL_SERVER_ERROR
```

## 5. Authentication Contract

### 5.1 Credential Transport

```http
Cookie: access_token=<system-issued-jwt>
```

V1 只使用本系統 JWT 的 HttpOnly Cookie，不另外提供 Authorization Bearer Transport，也不在 Login Response Body 回傳 Token。

| 設定 | 規則 |
| --- | --- |
| Name | `access_token` |
| HttpOnly | true |
| Path | `/` |
| SameSite | Lax |
| Domain | 不設定；Host-only |
| Secure | 正式 HTTPS 為 true；本機 HTTP 開發可為 false |

### 5.2 Protected Endpoint Authentication

Authentication Stage 依序執行：

1. 讀取 `access_token` Cookie。
2. 驗證 JWT 格式、Signature、Issuer、Audience。
3. 驗證 Expiration。
4. 從 JWT `sub` 取得內部 User ID。
5. 讀取 User，確認存在。
6. 確認 User status=active。

| 情境 | HTTP／Code |
| --- | --- |
| 沒有 Cookie | `401 AUTH_REQUIRED` |
| JWT 無效 | `401 AUTH_TOKEN_INVALID` |
| JWT 已過期 | `401 AUTH_TOKEN_EXPIRED` |
| JWT 合法但 User 不存在 | `401 AUTH_TOKEN_INVALID` |
| User disabled | `403 AUTH_USER_DISABLED` |

一般 Protected Endpoint 在 Authentication Stage 失敗時，不執行 Path／Body、Resource、Authorization 或 Domain Validation。

### 5.3 Authentication 例外

- Register、Login、Google Login 是 Public Endpoint，各自驗證 Credential。
- Logout 不要求有效登入；缺少、無效或過期 Cookie 都清除 Cookie 並回 204。
- Statistics 依 Form Lifecycle 與 resultVisibility 採 Optional／Conditional Authentication，精確流程見 Part C。

## 6. 驗證順序

一般 Protected Endpoint 的順序基線：

```text
Authentication
→ Path／Query／Body 可解析性
→ Resource Existence
→ Authorization
→ Lifecycle
→ Request／Domain Validation
→ Atomic Write 或 Query
```

不同驗證階段採 Early Return；前一階段失敗後，不執行後續階段。同一個 Domain／Answer Validation 階段可以針對可安全判定的多個問題一次回傳多筆 Details。

每支 Endpoint 的精確順序以 Part B／Part C 的「驗證流程」為準。Middleware、Filter 或 Validator 的內部排列不得意外改變外部可觀察順序。

## 7. Endpoint 索引

### 7.1 Part B：Auth／User／Form

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

### 7.2 Part C：Submission／Statistics

| # | Method | Path | 身分／權限 | 成功 |
| --- | --- | --- | --- | --- |
| 15 | POST | `/forms/{formId}/responses` | Active User；符合 Submission 規則 | 201 ResponseReceipt |
| 16 | GET | `/forms/{formId}/responses/me/latest` | Active User；只讀自己的 Latest Response | 200 ResponseReceipt |
| 17 | GET | `/forms/{formId}/respondent-count` | Active Owner | 200 Count |
| 18 | GET | `/forms/{formId}/statistics` | Closed + resultVisibility | 200 Statistics |

## 8. Pagination Scope

V1 只有 `GET /forms` 使用分頁，其 Default、Range 與排序見 Part B。

以下集合完整回傳，不分頁也不得隱性截斷：

- FormDetail Questions／Options
- ResponseReceipt Answers
- Statistics Questions／Items／Respondents

## 9. Atomicity、Retry 與副作用

- GET 不產生業務狀態寫入副作用。
- Create Form、Patch Form、首次 Google Login 與 Submit 的寫入範圍必須原子成功或失敗。
- Publish／Close 是原子狀態轉移。
- Empty／No-change PATCH 是合法 no-op，不更新 updatedAt。
- V1 不支援 Idempotency-Key。
- 精確 Retry 與競爭請求行為見各 Endpoint 及 System Constraints。

## 10. 文件責任

- Domain 定義業務規則與權限。
- Database 定義資料結構與 Constraint。
- API Part A／B／C 定義 HTTP 外部行為。
- Error Catalog 定義 Status 與 Code。
- System Constraints 定義跨層安全、交易與實作邊界。

本文件的共通規則適用於 Part B 與 Part C；個別 Endpoint 明確定義的例外優先於一般基線，但不得創造未記載的新外部行為。
