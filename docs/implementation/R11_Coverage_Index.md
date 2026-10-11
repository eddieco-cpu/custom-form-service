# R11 端點／Error Catalog coverage 索引

2026-10-11（Asia/Taipei）；RunId `r11-acceptance-20261011-01`，核准修訂 `ee23e56`，產品／測試 `a4f9402`。本次 fresh snapshot full TRX：420 executed／420 passed／0 failed／0 skipped；49 HTTP PASS。此索引依具名test的實际assertion與TRX結果建立，不把總PASS推論為未覆蓋契約PASS。沒有增加HTTP矩陣。

原始證據：[TRX](../../artifacts/r11-acceptance/r11-acceptance-20261011-01/full/r11-full.trx)、[420實跑rows索引](../../artifacts/r11-acceptance/r11-acceptance-20261011-01/trx-method-index.json)、[HTTP逐案結果](../../artifacts/r11-acceptance/r11-acceptance-20261011-01/http-results.json)。HTTP編號對應[R11 companion](../test-plan/R11_Manual_Tests.http)；每案實際status/raw body/遮蔽headers與SQL在同run `<case>.http.json`／`<case>-before.sql.txt`／`<case>-after.sql.txt`。所有下列具名test均由本次TRX核對PASS（含DataRow展開），不是沿用歷史結論。

## 17個Email端點與G1

`PA` 是Catalog §12的四個Protected Auth codes；`V`=`VALIDATION_ERROR`。表中「共用」只表示共用Authentication實作有代表證據，**不等於每端點四碼全部直接覆蓋**。Catalog來源：[04 §12](../specs/04-v1-error-catalog.md)。

| #／端點（省略/api/v1） | 本次原生HTTP PASS | 本次04具名test／已核對主要errors與優先序 | 證據缺口／限制 |
| --- | --- | --- | --- |
| 1 POST /auth/register | 06a/b；06c證不自動登入 | A1/A2：V、USER_EMAIL_ALREADY_EXISTS；U：code-point/password | 未在本次原生重演全部validation邊界／並行重複Email |
| 2 POST /auth/login | 06d/e真Cookie | A1/A2/A3：V、AUTH_INVALID_CREDENTIALS、AUTH_USER_DISABLED；credential先於disabled；C：Production Secure | 正式HTTPS／browser不在本次 |
| 3 POST /auth/logout | 13a/b；13c清cookie後401 | A4：missing/invalid/expired均204；scope與expiry | JWT撤銷非V1契約 |
| 4 POST /auth/google | 未執行 | G1 pending：含AUTH_ACCOUNT_CONFLICT及真Provider | 正式V1後續Round，不列產品缺陷 |
| 5 GET /users/me | 06c/f、13c | A5/A3：PA四碼、unknown-user、Bearer拒絕 | 全部PA直接證據在本次TRX |
| 6 PATCH /users/me | 11e | A1/A6/P：V、PA四碼、Auth→Body；readonly／no-op | 未擴大HTTP矩陣 |
| 7 POST /forms | 07a、11a | F1/F2/P：V、FORM_DEFINITION_INVALID、PA四碼；shape／Candidate／零新增 | 代表性錯誤，非所有field×code矩陣 |
| 8 GET /forms | 09g | F3：V；pagination／Owner scope；L：超大page；distinct count由Q承接 | 未找到此endpoint直接PA四碼完整矩陣；共用A5不能代填 |
| 9 GET /forms/{id} | 07c、08b | F4：AUTH_REQUIRED、FORM_NOT_FOUND／Draft hiding／Deleted；D：split SELECT完整快照 | 未找到直接bad-UUID V及invalid/expired/disabled完整矩陣；RV-I-01見下 |
| 10 PATCH /forms/{id} | 07b/d、08c/d | F5/F6/F7/F2/P：V、FORM_DEFINITION_INVALID、FORM_ACCESS_DENIED、FORM_NOT_EDITABLE、PA；resource→Owner→Lifecycle；request-local IDs | FORM_NOT_FOUND在F2時序測試；Candidate／fault有實際DB證據 |
| 11 DELETE /forms/{id} | 12a | F4/S3：FORM_NOT_FOUND、FORM_ACCESS_DENIED；三狀態／retry／僅兩Form欄位變動；S2 fault | 未找到直接bad-UUID V或此endpoint PA四碼完整矩陣 |
| 12 POST /forms/{id}/publish | 08a、11b | Y1/Y2：V、FORM_NOT_FOUND、FORM_ACCESS_DENIED、FORM_ALREADY_PUBLISHED、FORM_INVALID_STATE_TRANSITION、FORM_NOT_PUBLISHABLE；missing/invalid/disabled Auth與順序 | 未找到此endpoint直接expired Cookie案例；不是AUTH_TOKEN_EXPIRED本身未測 |
| 13 POST /forms/{id}/close | 10a、11f | Y1/Y2：V、FORM_NOT_FOUND、FORM_ACCESS_DENIED、FORM_ALREADY_CLOSED、FORM_INVALID_STATE_TRANSITION；三狀態／Freeze | 未找到此endpoint直接expired Cookie案例 |
| 14 PATCH /forms/{id}/result-visibility | 10f/h | Y1/Y3/F2/P：V、FORM_NOT_FOUND、FORM_ACCESS_DENIED、PA四碼；transport／no-op／Auth先於body | 全visibility角色矩陣由T1承接 |
| 15 POST /forms/{id}/responses | 09a/b/c、10b、11c/d | S1/P：PA、V、FORM_NOT_FOUND、FORM_NOT_SUBMITTABLE、SUBMISSION_DEADLINE_EXCEEDED、RESPONSE_ALREADY_SUBMITTED；Auth→shape→existence→state→deadline→single→answers；S2真waiter／fault | COMMIT結果未知不宣稱零新增 |
| 16 GET /forms/{id}/responses/me/latest | 09d、12b | Q：PA四碼、V、FORM_NOT_FOUND、RESPONSE_NOT_FOUND；whole response／UUID tie-break／實際SELECT snapshot | 原生僅代表案，細部由本次Q承接 |
| 17 GET /forms/{id}/respondent-count | 09e/f、12c | Q：PA四碼、V、FORM_NOT_FOUND、FORM_ACCESS_DENIED；三狀態、disabled歷史／empty answers、distinct／SELECT snapshot | List snapshot I-02不是新增契約 |
| 18 GET /forms/{id}/statistics | 08e、10c/d/e/g/i、11g、12d | T1/T2/T3：V、Auth四碼、FORM_NOT_FOUND、FORM_STATISTICS_NOT_AVAILABLE、STATISTICS_ACCESS_DENIED；Lifecycle→ConditionalAuth→visibility；public壞cookie200 | 五題型／zero／exact number／四snapshot均由本次T承接 |

## 具名test鍵（來源與assertion）

| 鍵 | 來源／方法 |
| --- | --- |
| A1 | [AuthUserEndpointsTests](../../tests/CustomFormService.IntegrationTests/AuthUserEndpointsTests.cs)：`Register_login_profile_update_logout_flow_matches_contract`、`Unknown_readonly_null_and_malformed_fields_use_validation_envelope` |
| A2 | 同檔：`Duplicate_email_and_wrong_password_use_contract_errors` |
| A3 | 同檔：`Disabled_user_is_rejected_after_valid_credentials_and_on_protected_requests` |
| A4 | 同檔：`Logout_succeeds_with_missing_invalid_and_expired_tokens` |
| A5 | 同檔：`Missing_invalid_expired_and_unknown_user_tokens_are_distinguished`、`Authorization_bearer_transport_is_not_accepted` |
| A6 | 同檔：`Authentication_error_precedes_patch_body_validation`、`Empty_and_unchanged_patch_are_no_ops_for_updated_at` |
| U/C | 同檔：`Unicode_code_point_and_password_no_trim_rules_are_enforced`、`Production_login_cookie_is_secure`；[ConfigurationTests](../../tests/CustomFormService.IntegrationTests/ConfigurationTests.cs)；[EngineeringEndpointsTests](../../tests/CustomFormService.IntegrationTests/EngineeringEndpointsTests.cs)兩個Cors方法 |
| P | [LocalRepair](../../tests/CustomFormService.IntegrationTests/FormEndpointsTests.LocalRepair.cs)：`Repair_authentication_precedes_bad_path_body_and_media`（user/create/patch/visibility/submit DataRows，每row核對PA四碼）、`Repair_F03_unsupported_media_type_uses_contract` |
| L/D | 同檔：`Repair_F01_large_page_returns_empty_without_offset_wrap`、`Repair_S01_detail_split_query_returns_one_complete_version`（三barrier） |
| F1 | [FormEndpointsTests](../../tests/CustomFormService.IntegrationTests/FormEndpointsTests.cs)：`Create_nested_draft_validates_and_persists_atomically` |
| F2 | [LocalRepair](../../tests/CustomFormService.IntegrationTests/FormEndpointsTests.LocalRepair.cs)：`Repair_F02_form_decoding_preserves_layer_code_and_path`、`Repair_F02_shape_resource_owner_lifecycle_priority_and_valid_pair` |
| F3/F4 | [FormEndpointsTests](../../tests/CustomFormService.IntegrationTests/FormEndpointsTests.cs)：`List_is_owner_scoped_paginated_and_stably_sorted`、`Detail_hides_other_drafts_and_soft_delete_preserves_definition_rows` |
| F5/F6/F7 | 同檔：`Patch_rejects_invalid_delta_atomically_and_preserves_the_draft`、`Patch_enforces_scope_lifecycle_and_noop_rules`、`Patch_uses_top_level_validation_and_request_paths_before_resource_delta_errors` |
| Y1 | [Lifecycle](../../tests/CustomFormService.IntegrationTests/FormEndpointsTests.Lifecycle.cs)：`R7_errors_follow_auth_parse_existence_owner_and_domain_order`（publish/close/result-visibility） |
| Y2 | 同檔：`R7_incomplete_draft_can_be_saved_but_publish_fails_atomically`、`R7_publish_reuses_structural_rules_and_collects_precise_details_without_writes`、`R7_close_without_body_does_not_revalidate_frozen_definition_or_auto_close_at_deadline` |
| Y3 | [LifecycleReview](../../tests/CustomFormService.IntegrationTests/FormEndpointsTests.LifecycleReview.cs)：`R7_review_visibility_transport_errors_use_contract_envelope_without_writes`、`R7_review_visibility_non_string_values_are_rejected`、`R7_review_owner_cannot_access_deleted_form` |
| S1 | [Submission](../../tests/CustomFormService.IntegrationTests/FormEndpointsTests.Submission.cs)：`R8_transport_auth_and_resource_errors_keep_their_priority`、`R8_non_published_and_missing_answers_returns_form_not_submittable`、`R8_deadline_then_single_then_answers_priority_and_failed_validation_has_no_rows`、`R8_invalid_answer_paths_and_required_omission_do_not_write_rows` |
| S2/S3 | [SubmissionConcurrency](../../tests/CustomFormService.IntegrationTests/FormEndpointsTests.SubmissionConcurrency.cs)：`R8_concurrent_submissions_observe_waiter_and_enforce_mode`、`R8_submit_close_delete_share_lock_and_read_state_after_wait`、`R8_delete_keeps_owner_rules_all_states_children_and_retry_behavior`；[SubmissionAtomicity](../../tests/CustomFormService.IntegrationTests/FormEndpointsTests.SubmissionAtomicity.cs)兩個precommit_fault方法 |
| Q | [ResponseQueries](../../tests/CustomFormService.IntegrationTests/FormEndpointsTests.ResponseQueries.cs)：`R9_authentication_UUID_and_resource_errors_keep_priority`、`R9_state_owner_and_empty_data_matrix`、`R9_count_deduplicates_successful_users_including_owner_empty_answers_and_disabled_history`、`R9_latest_uses_whole_response_without_backfilling_optional_answers`、`R9_latest_timestamp_precedes_UUID_and_same_timestamp_uses_UUID_descending`、`R9_completed_Form_SELECT_establishes_snapshot_before_concurrent_write` |
| T1/T2/T3 | [Statistics](../../tests/CustomFormService.IntegrationTests/FormEndpointsTests.Statistics.cs)：`R10_LifecycleVisibilityAndCredentialPriority`、`R10_ConditionalAuthUsesExistingJwtPolicy`、`R10_InvalidPersistedAnswersReturnControlled500`；同檔FiveTypes／ZeroData／ExactNumber／EffectiveWhole／AuthorizationAndOutput方法 |

## Catalog detail code／path與共用500

| Detail code | 本次具體證據／已核對path |
| --- | --- |
| REQUIRED_FIELD | Y2：questions、questions[0].config.min；S1／[SubmissionAnswerValidatorTests](../../tests/CustomFormService.IntegrationTests/SubmissionAnswerValidatorTests.cs) `Required_missing_question_uses_question_id_path`：questions.UUID／answers[i].value |
| INVALID_VALUE | P／F2／S1：body、readonly field、answers[i].value；`Multiple_choice_duplicate_and_malformed_elements_keep_original_indexes`：answers[i].value[j]；`Item_shape_and_duplicate_ids_return_before_question_membership`：answers[i].questionId |
| QUESTION_NOT_FOUND／OPTION_NOT_FOUND | F6：cross-form questions[i].id／questions[i].options[j].id；S1／validator `Valid_uuid_options_from_other_questions_and_nonexistent_options_are_not_found`：answers[i].questionId／value |
| INVALID_SORT_ORDER | 本次07d精確核對questions[0].sortOrder／INVALID_SORT_ORDER，拒絕前後七表相同 |
| DUPLICATE_SORT_ORDER | [CandidateContract](../../tests/CustomFormService.IntegrationTests/FormEndpointsTests.CandidateContract.cs) `Candidate_collision_path_uses_the_request_source_of_the_relevant_field`；[CandidateSafety](../../tests/CustomFormService.IntegrationTests/FormEndpointsTests.CandidateSafety.cs) `Candidate_collision_paths_prefer_causal_fields_over_explicit_unchanged_fields`：sortOrder因果path；Y2 Publish |
| INVALID_QUESTION_TYPE／INVALID_QUESTION_CONFIG | F2 DataRows：questions[0].type、config.placeholder／variant／config；Y2：questions[0].config.min |
| QUESTION_TYPE_CONFLICT | F5：change-to-text＋nonempty options；**已有code assertion但該反例未明確斷言exact conflict path**，不可推論path已覆蓋 |
| INVALID_OPTION | CandidateSafety collision方法：questions[0].options[0].value（changed欄位的因果來源） |
| INSUFFICIENT_OPTIONS | Y2 single/multiple_choice：questions[0].options |
| INTERNAL_SERVER_ERROR | `Repair_unexpected_InvalidOperationException_remains_server_error`及T3：正式500 Envelope；S2／Lifecycle／OptionPersistence／CandidateSafety fault回滾。**未對17個端點逐支注入500** |

同一階段多details／early-return由F7、Y1/Y2、S1、validator `Question_membership_errors_precede_required_and_invalid_values`／`Same_answer_stage_collects_safe_errors_and_never_returns_partial_normalized_results`及T1/T2承接。本次HTTP拒絕案全部核對top-level code，07d另核對detail/path；message和details順序不當穩定契約。

## Known gaps與處置

- 上表逐端點的直接PA／bad-UUID缺口、QUESTION_TYPE_CONFLICT exact path、全端點500 injection是**證據粒度缺口**，目前沒有實際產品偏離案例；交Owner決定是否需後續獨立授權補驗，不擴大本次HTTP或修改tests。
- RV-I-01：Publish barrier fresh V1頂層DTO assertion未完整補強；現有paused V0、兩非Publish fresh V1完整證據本次PASS，08a/b順序全DTO也PASS，但不能代替Publish併行fresh V1。I-02 List count/items同快照是非阻擋建議，規格沒有要求，不新增契約。
- [歷史狀態](R11_Historical_Status.md)：R0–R3缺repo acceptance／確認來源；R5有Owner確認但AI case缺口；R7/R9缺可追溯確認來源，不等同尚未驗收。
- G1／真Provider、frontend、Railway／domain／正式HTTPS未執行；Windows policy／port exclusion是歷史環境限制，本次成功不能宣稱永久解除。

本次技術驗收結果待Owner確認；人工未執行。未把上述缺口標為PASS，未標R11／A-stage accepted或Backend V1 accepted。
