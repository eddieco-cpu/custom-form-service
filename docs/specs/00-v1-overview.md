# 自建表單設計服務 V1 — System Overview

| 項目 | 內容 |
| --- | --- |
| 版本 | 1.0 |
| 日期 | 2026-09-28 |
| 狀態 | 正式規格 |
| 定位 | V1 產品與系統規格的入口文件 |
| 主要讀者 | 專案負責人、產品／系統設計者、程式實作者、測試人員 |

## 1. 系統目的

本系統是一套以會員身分為基礎的自建表單服務。會員可以建立表單、設定問題、發布並分享；其他會員依規則提交回覆；表單關閉後，系統依結果存取政策提供統計。

V1 的主要流程是：

1. 使用者以 Email／Password 或 Google 登入。
2. 建立者建立並編輯 Draft Form。
3. 建立者 Publish Form，提供已登入會員填答。
4. 系統保存每一次成功提交的 Response 與 Answers。
5. 建立者在收集期間查看 Respondent Count。
6. 建立者 Close Form。
7. 符合 resultVisibility 的使用者查看 Statistics。

目前正式規格聚焦 Backend、API、Database 與跨層行為；Frontend 畫面與互動不在本文件組中定義。

## 2. 角色與資源關係

V1 沒有全域角色表或 RBAC。Owner、Respondent 等稱呼是使用者相對於特定 Form 的關係。

| 角色／關係 | 說明 |
| --- | --- |
| 未登入訪客 | 可以註冊、登入，以及查看符合 public 條件的 Closed Statistics |
| Active User | 可以建立 Form，並依 Submission 規則填答 Published Form |
| Owner | 建立並擁有特定 Form；可管理 Lifecycle、Definition、Result Visibility 與 Respondent Count |
| Respondent | 曾對特定 Form 成功提交 Response 的 User |
| Statistics Viewer | 依 Closed Form 的 resultVisibility 取得 Statistics 存取權的人 |

同一位 User 可以同時是某張 Form 的 Owner 與 Respondent，也可以填寫自己建立的 Form。

## 3. V1 能力概覽

| 能力 | V1 提供 |
| --- | --- |
| Authentication | Register、Email／Password Login、Google Login、Logout |
| User | 查詢 Current User、修改 displayName |
| Form | Create、Owner List、Detail、Draft Patch、Soft Delete |
| Lifecycle | Publish、Close |
| Result Policy | 隨時調整未刪除 Form 的 resultVisibility |
| Submission | 已登入會員提交整份 Answers |
| Personal Response | 查詢自己的 Latest Response |
| Progress | Owner 查詢不同 Respondent 人數 |
| Statistics | Closed 後依 Visibility 與 Identity Policy 取得聚合結果 |

完整 API 共 18 支 Endpoint，分別定義於 API Contract Part A 與 Part B。

## 4. Form Lifecycle

| 狀態 | Definition | Submission | Statistics |
| --- | --- | --- | --- |
| `draft` | Owner 可編輯；允許尚未達 Publish 完整度 | 不接受 | 不提供 |
| `published` | 已凍結，不可修改 | 符合 Deadline 與填答規則時接受 | 不提供 |
| `closed` | 維持凍結 | 不接受 | 依 resultVisibility 提供 |

狀態只能：

```text
draft → published → closed
```

`closed` 是終止狀態，V1 不支援回復 Draft 或 Reopen。

Soft Delete 與 Lifecycle 分開。Owner 可以在任一狀態 Soft Delete Form；刪除後一般 API 將該 Form 視為不存在。

Deadline 只決定能否繼續提交，不會自動 Close Form，也不會提前開放 Statistics。

## 5. 三個獨立設定維度

| 設定 | 值 | 控制內容 |
| --- | --- | --- |
| `responseMode` | `single`／`multiple` | 同一 User 可以成功提交一次或多次 |
| `responseIdentity` | `anonymous`／`identified` | Statistics 是否揭露 Respondent 身分 |
| `resultVisibility` | `owner_only`／`respondents`／`public` | Closed Statistics 可以由誰查看 |

三者不能互相替代：

- anonymous 仍要求登入提交，系統內部仍保存 User 與 Response 關係。
- public 只代表 Closed Statistics 可以由未登入訪客查看。
- multiple 會保留歷次 Response，但 Statistics 每位 User 只使用最新一份完整 Response。

## 6. Question 與 Submission 摘要

V1 支援五種 Question Type：

- `text`
- `number`
- `rating`
- `single_choice`
- `multiple_choice`

Question 與 Option 是 Form Definition 的一部分，只能在 Draft 中修改。V1 不提供獨立 Question／Option CRUD Endpoint。

一次 Submission：

- 必須由 Active User 發出。
- 必須對未刪除且 Published 的 Form 提交。
- 必須符合 Deadline、responseMode、Required、Question Type、Config 與 Option 歸屬。
- 成功時原子建立一份 Response 與全部有效 Answers。
- 不提供已提交 Response／Answer 的修改或刪除。

題型設定、Draft／Publish 驗證差異與 Answer Shape 由 Domain 與 API Contract 定義。

## 7. Statistics 摘要

Statistics 是查詢時計算的衍生結果，不是獨立資料實體。

V1 的核心語意：

- 只在 Form Closed 且未 Soft Delete 時提供。
- responseMode=multiple 時，每位 User 只採 Latest Effective Response。
- 不逐 Question 從舊 Response 補答案。
- Form／Question／Item 的 count 各有明確定義。
- anonymous 不回 respondents 或跨題身分連結。
- identified 的每個統計項目可以回 User ID 與目前 displayName。
- Closed 且沒有 Response 仍回合法的零資料結果。

完整 Access Matrix、各題型聚合方式、排序與 Response Shape 見 Domain 與 API Contract Part B。

## 8. 資料模型概覽

V1 使用 7 張 Domain Table：

| 分組 | Tables | 職責 |
| --- | --- | --- |
| User／Identity | `users`、`user_identities` | 系統會員與登入來源 |
| Form Definition | `forms`、`questions`、`options` | 表單、題目、設定與選項 |
| Submission Data | `responses`、`answers` | 成功提交的回覆與答案 |

Submission 是操作，Statistics 是 Query Result，因此不建立 submissions 或 statistics Table。

Form 採 Soft Delete。正常業務流程唯一的 Database Cascade 是：

```text
questions.id → options.question_id
```

完整 Column、Constraint、Index、JSONB 與 Delete Behavior 以 Database Specification 為準。

## 9. 關鍵系統保證

下列規則是理解 V1 的最小核心：

1. Server 是 Authentication、Authorization、Time 與資料驗證的最終權威。
2. Form Definition 只能在 Draft 修改；Publish 後不可變更。
3. Create Form、Patch Definition、首次 Google Login 與 Submit 都必須原子成功或失敗。
4. single 的並行 Submission 最多一個成功。
5. Patch／Publish 與 Submit／Close／Delete 必須有一致且可解釋的先後。
6. Soft-deleted Form 在 Detail、List、Submit、Latest Response、Count 與 Statistics 中一致不可存取。
7. Statistics 的存取權與身分揭露是兩個不同政策。
8. API 採逐階段 Early Return；Error Status、Code 與 Detail Path 是正式 Contract。
9. V1 只有 Owner Form List 使用分頁，其他集合不得隱性截斷。
10. 程式架構與技術做法可以調整，但不得改變 Domain、Schema、API、Error 或安全政策。

詳細的 Transaction、Unicode、排序、Cookie、CORS、Logging 與驗收要求見 System Constraints。

## 10. 技術輪廓

| 項目 | V1 基線 |
| --- | --- |
| Backend | ASP.NET Core Web API／C# |
| Database | PostgreSQL |
| Data Access | EF Core |
| API | JSON over HTTP；Base Path `/api/v1` |
| Authentication | Email／Password、Google ID Token、本系統 JWT HttpOnly Cookie |
| 初期環境 | 可以本機開發與驗證 |
| Deployment | 不鎖定特定雲端或容器平台 |

具體 Project／Layer、Validation Library、Transaction、Locking、Migration、Query、Testing、Logging 與 Deployment 做法屬實作決策，但必須遵守 System Constraints。

## 11. V1 範圍外

| 分類 | 不包含 |
| --- | --- |
| Account | Email Verification、Forgot／Reset Password、MFA、修改 Email、帳號綁定／解綁、User Delete |
| Authentication | Refresh Token、Session Store、Token 撤銷、跨裝置強制登出 |
| Organization | Team、Organization、RBAC、共同擁有權 |
| Form | Reopen、版本歷史、複製、模板市集、Import／Export、條件分支、檔案上傳 |
| Response | 未登入填答、Draft／Save & Resume、修改／刪除已提交回覆、歷史清單、Owner Raw Response List |
| Statistics | 預先計算 Table、Average／Median、Percentage、Completion Rate、跨題 Respondent Mapping |
| Operations | Webhook、Email 通知、Quota、Rate Limit、Audit Log Domain |

未列於 Overview 的細節不代表自動允許。V1 能力與排除項目以 Domain、API 與 System Constraints 的完整定義為準。

## 12. 規格文件地圖

本規格組共有六個規格領域，API 因篇幅拆成三份文件。

| 文件 | 主要用途 |
| --- | --- |
| `00-v1-overview.md` | 系統目的、V1 範圍、核心概念與文件導覽 |
| [`01-v1-domain-specification.md`](01-v1-domain-specification.md) | 業務規則、Lifecycle、Question、Submission、Statistics 與權限 |
| [`02-v1-database-specification.md`](02-v1-database-specification.md) | 7 張 Table、Column、Key、Constraint、Index 與 Delete Behavior |
| [`03a-v1-api-contract-common.md`](03a-v1-api-contract-common.md) | 共通格式、Authentication、驗證次序、Endpoint 索引與跨 API 規則 |
| [`03b-v1-api-contract-auth-user-form.md`](03b-v1-api-contract-auth-user-form.md) | Auth、User 與 Form；Endpoint 1～14 |
| [`03c-v1-api-contract-submission-statistics.md`](03c-v1-api-contract-submission-statistics.md) | Submission、Latest Response、Respondent Count 與 Statistics；Endpoint 15～18 |
| [`04-v1-error-catalog.md`](04-v1-error-catalog.md) | Error Envelope、HTTP Status、Top-level Code、Detail Code 與優先序 |
| [`05-v1-system-constraints.md`](05-v1-system-constraints.md) | 安全、跨層一致性、交易、競爭請求、實作自由度與驗收邊界 |

### 12.1 文件效力

Overview 只提供摘要與導覽，不取代專責文件：

| 議題 | 權威文件 |
| --- | --- |
| 業務規則與權限 | 01 Domain |
| 資料結構與 Database Constraint | 02 Database |
| Request、Response、Endpoint 驗證順序 | 03 API |
| Error Status 與 Code | 04 Error Catalog |
| 跨層安全、交易與工程限制 | 05 System Constraints |

若專責文件之間出現無法以各自職責消除的衝突，必須先同步修正规格，不得由程式實作者自行選擇較容易實作的版本。

### 12.2 建議閱讀順序

程式實作者應依序閱讀：

```text
00 Overview
→ 01 Domain
→ 02 Database
→ 03a／03b／03c API
→ 04 Error Catalog
→ 05 System Constraints
```

產品或規格 Review 可以先讀 00、01 與 03a／03b／03c，再依問題查閱 02、04、05。
