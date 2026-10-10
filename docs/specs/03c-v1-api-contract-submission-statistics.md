# 自建表單設計服務 V1 — API Contract Part C：Submission、Response、Statistics

| 項目 | 內容 |
| --- | --- |
| 版本 | 1.1 |
| 日期 | 2026-10-10 |
| 狀態 | 正式規格；本次 Statistics Root 欄位修訂待 review |
| Base Path | `/api/v1` |
| 範圍 | Submission、Latest Response、Respondent Count、Statistics；Endpoint 15～18 |
| 必要前置 | [Part A：Common](03a-v1-api-contract-common.md) |
| 配套文件 | [Part B：Auth／User／Form](03b-v1-api-contract-auth-user-form.md)、[Domain](01-v1-domain-specification.md)、[Database](02-v1-database-specification.md)、[Errors](04-v1-error-catalog.md)、[Constraints](05-v1-system-constraints.md) |

## 1. 文件目的與適用範圍

本文件定義 V1 Submission、Latest Response、Respondent Count 與 Statistics API 的 Resource Shape、Request、Response、驗證流程、計算規格與 Endpoint Error 對照。

所有共通 JSON、Datetime、Cookie、Authentication Stage、Error Envelope、Early Return 與 Pagination Scope 先依 Part A。Statistics 的 Conditional Authentication 是本文件明確定義的例外。

## 2. Endpoint 總表

| # | Method | Path | 身分／權限 | 成功 |
| --- | --- | --- | --- | --- |
| 15 | POST | `/forms/{formId}/responses` | Active User；符合 Submission 規則 | 201 ResponseReceipt |
| 16 | GET | `/forms/{formId}/responses/me/latest` | Active User；只讀自己的 Latest Response | 200 ResponseReceipt |
| 17 | GET | `/forms/{formId}/respondent-count` | Active Owner | 200 Count |
| 18 | GET | `/forms/{formId}/statistics` | Closed + resultVisibility | 200 Statistics |

V1 沒有 Response History List、Owner Raw Response List、指定任意 userId 查詢、Response Update／Delete 或 Answer CRUD Endpoint。

## 3. Resource Shapes

### 3.1 ResponseReceipt

```json
{
  "id": "response-id",
  "formId": "form-id",
  "submittedAt": "2026-09-28T06:30:00Z",
  "answers": [
    {
      "questionId": "question-id",
      "value": "Hello"
    }
  ]
}
```

- 只回傳實際保存的 Answers。
- Optional 未作答題目不補 `null`。
- 不回 Answer ID 或 userId。
- Answers 依 Question `sortOrder ASC, id ASC` 排序，不沿用 Client Request Array 順序。
- value 已完成 Server 正規化。

### 3.2 Answer Value Shape

| Question Type | Request／Receipt Value |
| --- | --- |
| text | JSON String |
| number | JSON Number |
| rating | JSON Integer |
| single_choice | Option ID 的 JSON String |
| multiple_choice | Option ID 的 JSON String Array |

Choice Submission 使用 Option ID，不使用 Option value 或 label。

### 3.3 Respondent Count

```json
{
  "count": 27
}
```

count 是至少成功提交過一次的不同 User 數，不是歷史 Response 筆數。

### 3.4 Statistics Root

```json
{
  "formId": "form-id",
  "title": "活動回饋",
  "description": null,
  "creatorDisplayName": "Eddie",
  "responseMode": "multiple",
  "resultVisibility": "public",
  "submissionDeadlineAt": "2026-10-10T10:30:00Z",
  "count": 2,
  "questions": [
    {
      "questionId": "question-id",
      "title": "是否滿意？",
      "description": null,
      "type": "single_choice",
      "count": 2,
      "statistics": [
        {
          "optionId": "option-id",
          "label": "滿意",
          "value": "yes",
          "count": 2
        }
      ]
    }
  ]
}
```

Statistics 自帶 Form 與 Question 顯示資料，Public Client 不需要先呼叫受保護的 Form Detail。

以上為 Closed、public、anonymous 的成功回應範例；identified 時依 §8.5 在每個 Statistic Item 加入 respondents，Root Shape 相同。

Root 固定包含以下九個欄位；所有欄位必須出現，nullable 欄位沒有值時明確回 `null`，不得省略。

| 欄位 | 型別／值域 | 資料來源與語意 |
| --- | --- | --- |
| `formId` | UUID String | `forms.id`；維持 formId，不改為 id |
| `title` | String | `forms.title` |
| `description` | String／`null` | `forms.description` |
| `creatorDisplayName` | String | `forms.user_id` 對應 `users.display_name` 的目前名稱；與 FormDetail 語意一致，不保存名稱快照 |
| `responseMode` | `single`／`multiple` | `forms.response_mode`；單次／多次提交模式 |
| `resultVisibility` | `owner_only`／`respondents`／`public` | `forms.result_visibility`；目前結果存取政策 |
| `submissionDeadlineAt` | UTC ISO 8601 String／`null` | `forms.submission_deadline_at`；表定截止時間，無期限時回 null；維持既有保存值的時間精度 |
| `count` | 非負 Integer | 具有 Effective Response 的不同 User 數，非 Response 筆數；見 §8.2 |
| `questions` | Array | 完整 Statistics Question 集合，依 §8.3 排序 |

- Creator 改名後，下一次 Statistics 查詢反映目前名稱；不回 Creator email、外部 Provider ID 或其他 Identity 資料。Creator 後來 disabled 不會使原本符合存取規則的 Form 被排除；Private Statistics 呼叫者仍須通過既有 Active 身分規則。
- `creatorDisplayName` 在 anonymous／identified 及所有允許的 Visibility 組合均提供；anonymous 限制的是 Respondent 身分，不是建立者名稱。
- `submissionDeadlineAt` 不是實際關閉時間。提前 Close、Closed 時期限已過或尚未到，均回保存的表定截止時間；到期不會自動 Close，也不提前開放 Statistics。
- Root 不追加 `id`、`userId`、`creatorId`、`status`、`responseIdentity`、`createdAt`、`updatedAt`、`deletedAt` 或 `closedAt`。題目既有欄位維持 `questionId`、`title`、`description`、`type`、`count`、`statistics`；`questions[].type` 已提供題型，不新增欄位或 Question config。
- 零填答者也回完整 Root，包含上述四個新增欄位；各層 count、Item 集合與匿名／具名規則仍依 §8。

### 3.5 Identified Respondent

```json
{
  "id": "user-id",
  "displayName": "Eddie"
}
```

不包含 email、avatarUrl、Google `sub` 或其他 Identity 資料。

## 4. POST `/forms/{formId}/responses`

### 4.1 Request

```json
{
  "answers": [
    {
      "questionId": "text-question-id",
      "value": " Hello "
    },
    {
      "questionId": "number-question-id",
      "value": 18
    },
    {
      "questionId": "rating-question-id",
      "value": 5
    },
    {
      "questionId": "single-choice-question-id",
      "value": "option-id"
    },
    {
      "questionId": "multiple-choice-question-id",
      "value": [
        "option-id-a",
        "option-id-b"
      ]
    }
  ]
}
```

只允許頂層 `answers`。answers 必須存在且為 Array；所有 Question 都是 Optional 時可以傳空 Array。

每個 Answer 只允許 `questionId`、`value`。Body 不接受 userId、formId、responseId、submittedAt 或 Answer ID。

### 4.2 Answer 正規化

| Question Type | 正規化／未作答規則 |
| --- | --- |
| text | 移除頭尾 Unicode `White_Space=Yes`；空字串視為未作答 |
| number | 不接受 Number String；`1` 與 `1.0` 視為相同數值 |
| rating | 必須為 Integer |
| single_choice | 必須是一個 Option ID String |
| multiple_choice | 空 Array 視為未作答；非空時每個 Option ID 必須唯一 |

Optional 未作答不建立 Answer；Required 未作答使 Submission 失敗。

### 4.3 驗證流程

1. 執行完整 Authentication Stage。
2. 解析 formId UUID 與 JSON Body；拒絕未知／唯讀欄位。
3. 查找 `deleted_at IS NULL` 的 Form。
4. 驗證 Form status 為 published。
5. 在一致交易判定點取得唯一的 `submissionTime`。
6. 若有 Deadline，驗證 `submissionTime < submissionDeadlineAt`。
7. 驗證 responseMode：single 已有成功 Response 時拒絕；multiple 繼續。
8. 驗證 answers 為 Array，每個 Item Shape 正確，questionId 在 Request 中不重複。
9. 驗證每個 questionId 都屬於目標 Form；不存在或屬於其他 Form 對外不區分。
10. 依 Form Questions 檢查所有 Required Question 是否有有效 Answer。
11. 驗證每個 Answer 的 JSON Type、Question Type、Config Range 與文字／數值規則。
12. 驗證所有 Choice Option 都屬於對應 Question；Multiple Choice ID 不重複。
13. 移除 Optional 未作答項目，建立正規化 Answers。
14. 在寫入前重新確保 Form 狀態、Soft Delete、Deadline 與 single 一次性仍成立。
15. 原子建立 Response + 全部有效 Answers；submittedAt 使用步驟 5 的 submissionTime。
16. 依 Question 排序回傳 ResponseReceipt。

不同驗證層採 Early Return。步驟 10～12 的 Answer Validation 可以針對可安全判定的多個問題一次回傳多筆 Details。

### 4.4 Detail Path

Request 中已有 Answer 的錯誤使用 Array Path，例如：

```text
answers[0].questionId
answers[0].value
answers[2].value[1]
```

Required Question 完全未出現在 Request 時，使用 Question ID 定位：

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

### 4.5 成功

```text
201 Created
```

Body：ResponseReceipt。

### 4.6 錯誤

| 驗證階段／情境 | HTTP／Code |
| --- | --- |
| Authentication 失敗 | 對應 Auth Error |
| formId／Body Shape 無效 | `400 VALIDATION_ERROR` |
| Form 不存在／已刪除 | `404 FORM_NOT_FOUND` |
| Form 非 Published | `409 FORM_NOT_SUBMITTABLE` |
| `submissionTime >= Deadline` | `409 SUBMISSION_DEADLINE_EXCEEDED` |
| single 已提交或並行競爭失敗 | `409 RESPONSE_ALREADY_SUBMITTED` |
| Answer、Required、Question、Option、Type、Range 無效 | `400 VALIDATION_ERROR` + details |

Question／Option 歸屬錯誤使用 `QUESTION_NOT_FOUND`／`OPTION_NOT_FOUND` Detail Code，不將整個 Submission 改為 HTTP 404。

### 4.7 Atomicity 與 Retry

- Response 與所有有效 Answers 一起成功或失敗。
- Submit 與 Close／Soft Delete 必須形成一致交易先後。
- single 並行請求最多一個回 201，其餘回 RESPONSE_ALREADY_SUBMITTED。
- V1 不支援 Idempotency-Key。
- single Request 重試依一次性規則拒絕重複。
- multiple Request 在 Client 不確定先前結果時重試，可能新增另一筆歷史 Response。

## 5. GET `/forms/{formId}/responses/me/latest`

只依 Current User 查詢，不接受任意 userId。

### 5.1 驗證流程

1. 執行完整 Authentication Stage。
2. 解析 formId UUID。
3. 查找未刪除 Form。
4. 若 Form 為 Draft：
   - Current User 是 Owner：回 RESPONSE_NOT_FOUND。
   - Current User 非 Owner：回 FORM_NOT_FOUND。
5. Published／Closed 繼續；Deadline 與 resultVisibility 不影響此查詢。
6. 依 `(formId, CurrentUserId)` 查詢 `submittedAt DESC, id DESC` 第一筆 Response。
7. 沒有 Response 時回 RESPONSE_NOT_FOUND。
8. 依 Question `sortOrder ASC, id ASC` 排序 Answers。
9. 回傳 ResponseReceipt。

### 5.2 成功

```text
200 OK
```

Body：ResponseReceipt。

### 5.3 錯誤

| 情境 | HTTP／Code |
| --- | --- |
| Authentication 失敗 | 對應 Auth Error |
| formId 無效 | `400 VALIDATION_ERROR` |
| Form 不存在／已刪除／他人 Draft | `404 FORM_NOT_FOUND` |
| Owner 查 Draft，或 Published／Closed 尚無自己的 Response | `404 RESPONSE_NOT_FOUND` |

此 Endpoint 不提供歷史清單，也不從舊 Response 補回最新 Response 未回答的 Question。

## 6. GET `/forms/{formId}/respondent-count`

Draft／Published／Closed 都可以由 Owner 查詢。

### 6.1 驗證流程

1. 執行完整 Authentication Stage。
2. 解析 formId UUID。
3. 查找未刪除 Form。
4. 驗證 Current User 是 Owner。
5. 計算至少成功提交過一次的不同 User 數。
6. 回傳 Count。

Response Mode 為 multiple 時，同一 User 的多筆歷史仍只計算一次。Draft 正常回 `count=0`。

### 6.2 成功

```text
200 OK
```

Body：Respondent Count。

### 6.3 錯誤

| 情境 | HTTP／Code |
| --- | --- |
| Authentication 失敗 | 對應 Auth Error |
| formId 無效 | `400 VALIDATION_ERROR` |
| Form 不存在／已刪除 | `404 FORM_NOT_FOUND` |
| 非 Owner | `403 FORM_ACCESS_DENIED` |

## 7. GET `/forms/{formId}/statistics`

Statistics 只對未刪除且 Closed 的 Form 開放，再依 resultVisibility 判定存取。

### 7.1 驗證流程

1. 解析 formId UUID。
2. 查找未刪除 Form；不存在或已刪除回 FORM_NOT_FOUND。
3. 若 Form 為 Draft，進行 Draft 存在性保護：
   - 只有有效、Active 且為 Owner 的 Credential 可得到 FORM_STATISTICS_NOT_AVAILABLE。
   - 未登入、無效／過期 Cookie、Disabled User 或非 Owner 一律得到 FORM_NOT_FOUND。
4. 若 Form 為 Published，進行 Published 存在性保護：
   - 有效、Active User 得到 FORM_STATISTICS_NOT_AVAILABLE；不要求 Owner 或 Respondent 身分。
   - 未登入、無效／過期 Cookie、User 不存在或 Disabled User 一律得到 FORM_NOT_FOUND。
   - 此階段不因 resultVisibility 改變。
5. Form 為 Closed 時，依 resultVisibility 判斷：
   - `public`：不要求 Authentication；無效、過期或 Disabled User Cookie 視為匿名，不阻止存取。
   - `owner_only`：執行完整 Authentication，只有 Owner 通過。
   - `respondents`：執行完整 Authentication，Owner 或曾成功提交的 User 通過。
6. 選出每個 User 的 Effective Response：`submittedAt DESC, id DESC` 第一筆。
7. 依 responseIdentity 計算匿名或具名統計。
8. 套用 Question、Item 與 Respondent 排序。
9. 回傳 Statistics；零 Respondent 仍回 200。

Published Form 的 Statistics 尚不可用；其 resultVisibility 不會提前開放結果。

### 7.2 Access Matrix

前提：Form 為 Closed 且未刪除。

| resultVisibility | Owner | 非 Owner Respondent | 其他 Active User | 未登入訪客 |
| --- | --- | --- | --- | --- |
| owner_only | 允許 | 拒絕（403） | 拒絕（403） | 拒絕（401） |
| respondents | 允許 | 允許 | 拒絕（403） | 拒絕（401） |
| public | 允許 | 允許 | 允許 | 允許 |

Respondent 指至少曾成功提交該 Form 的 User。

對 `owner_only` 與 `respondents` 而言，登入只是必要條件，不代表登入後必然有權讀取；已登入但不符合身分者仍以 `STATISTICS_ACCESS_DENIED` 拒絕。

### 7.3 成功

```text
200 OK
```

Body：[§3.4 Statistics Root](#34-statistics-root)，包含全部九個根欄位；anonymous／identified 使用相同 Root，Item 是否含 respondents 依 §8.4～8.5。成功 Shape 的追加不改變 §7.1～7.2 的存取判定或 §7.4 的錯誤與優先序。

### 7.4 錯誤與優先序

| 階段／情境 | HTTP／Code |
| --- | --- |
| formId 無效 | `400 VALIDATION_ERROR` |
| Form 不存在／已刪除 | `404 FORM_NOT_FOUND` |
| Draft Owner | `409 FORM_STATISTICS_NOT_AVAILABLE` |
| Draft 非 Owner／匿名／無效身分 | `404 FORM_NOT_FOUND` |
| Published，有效 Active User | `409 FORM_STATISTICS_NOT_AVAILABLE` |
| Published，匿名／Token 無效或過期／User 不存在／Disabled User | `404 FORM_NOT_FOUND` |
| Closed Private，沒有 Cookie | `401 AUTH_REQUIRED` |
| Closed Private，JWT 無效／User 不存在 | `401 AUTH_TOKEN_INVALID` |
| Closed Private，JWT 過期 | `401 AUTH_TOKEN_EXPIRED` |
| Closed Private，User disabled | `403 AUTH_USER_DISABLED` |
| Closed Private，已登入但不符 Visibility | `403 STATISTICS_ACCESS_DENIED` |

Closed Public 的壞 Cookie 不產生 Auth Error，呼叫者直接視為匿名訪客。

## 8. Statistics 計算規格

### 8.1 Effective Response

每個 Form + User 只使用：

```text
submittedAt DESC
id DESC
```

排序第一筆完整 Response。

不得逐 Question 從歷史 Response 補 Answer。最新完整 Response 沒回答 Optional Question 時，該 User 不計入該 Question。

### 8.2 Count 語意

| 層次 | count |
| --- | --- |
| Form Root | 具有 Effective Response 的不同 User 數 |
| Question | Effective Response 中有該題 Answer 的不同 User 數 |
| Statistic Item | Effective Answer 命中該值／Option 的不同 User 數 |

Multiple Choice 中同一 User 可以命中多個 Items，因此 Item Count 加總可以大於 Question Count；同一 Item 內不能重複計算同一 User。

### 8.3 Item Shape 與排序

| Question Type | Item Shape | 集合與排序 |
| --- | --- | --- |
| single_choice | `optionId`、`label`、`value`、`count` | 全部配置 Options，包含零票；Option sortOrder ASC |
| multiple_choice | `optionId`、`label`、`value`、`count` | 全部配置 Options，包含零票；Option sortOrder ASC |
| rating | `value`、`count` | min 到 max 的每個 Integer，包含零票；value ASC |
| number | `value`、`count` | 只回實際值；數值等值分組；value ASC |
| text | `value`、`count` | 只回正規化後實際值；count DESC，再以固定 ordinal／binary value ASC |

Choice Statistic Item 的 `value` 是 Option 業務語意值；Submission Answer 的 `value` 是 Option ID，兩者不得混淆。

Questions 依 `sortOrder ASC` 回傳。`statistics` 永遠是 Array。

### 8.4 Anonymous

responseIdentity = `anonymous` 時：

- Statistic Item 完全省略 `respondents` Property。
- 不回 `respondents: null` 或可串接跨 Question 身分的替代 ID。
- Owner 也不能取得 Respondent 身分或跨 Question Mapping。

### 8.5 Identified

responseIdentity = `identified` 時，每個 Statistic Item 加入 respondents：

```json
{
  "optionId": "option-id",
  "label": "滿意",
  "value": "yes",
  "count": 2,
  "respondents": [
    {
      "id": "user-a",
      "displayName": "Amy"
    },
    {
      "id": "user-b",
      "displayName": "Eddie"
    }
  ]
}
```

- `respondents` 位於每個 Item，不在 Root 提供原始答卷清單。
- 使用目前的 displayName，不保存提交當下 Snapshot。
- 不包含 email、avatarUrl 或 Identity 內部資料。
- 依 `displayName ASC, id ASC` 排序；文字排序採固定 ordinal／binary 語意。
- `item.count == item.respondents.length`。
- 零票 Item 回 `respondents: []`。
- 一次完整回傳，不分頁或截斷。

### 8.6 Zero-data Response

Closed Form 即使沒有 Response，仍回 200：

- Form count = 0。
- 所有 Question count = 0。
- Text／Number statistics = `[]`。
- Choice 回全部配置 Options，count = 0。
- Rating 回 min 到 max 的全部 Integer，count = 0。
- Identified 的零票 Item respondents = `[]`。

### 8.7 不提供的 Statistics 欄位

V1 不回傳：

- percentage
- skippedCount
- average／median
- completionRate
- targetCount／remainingCount
- maxResponses／responseQuota
- Raw Response List
- Cross-question Respondent Mapping

## 9. Part C Endpoint Error 對照

下表省略所有 Endpoint 可能發生的 `500 INTERNAL_SERVER_ERROR`。

| Endpoint | 主要 Error Codes |
| --- | --- |
| POST `/forms/{formId}/responses` | Auth Errors、VALIDATION_ERROR、FORM_NOT_FOUND、FORM_NOT_SUBMITTABLE、SUBMISSION_DEADLINE_EXCEEDED、RESPONSE_ALREADY_SUBMITTED |
| GET `/forms/{formId}/responses/me/latest` | Auth Errors、VALIDATION_ERROR、FORM_NOT_FOUND、RESPONSE_NOT_FOUND |
| GET `/forms/{formId}/respondent-count` | Auth Errors、VALIDATION_ERROR、FORM_NOT_FOUND、FORM_ACCESS_DENIED |
| GET `/forms/{formId}/statistics` | 視 Lifecycle／Visibility 適用 VALIDATION_ERROR、Auth Errors、FORM_NOT_FOUND、FORM_STATISTICS_NOT_AVAILABLE、STATISTICS_ACCESS_DENIED |

完整 Error Code 與 Detail Code 定義以 Error Catalog 為準。
