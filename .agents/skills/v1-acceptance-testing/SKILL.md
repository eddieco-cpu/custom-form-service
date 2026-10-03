---
name: v1-acceptance-testing
description: Prepare or execute a V1 API Round's paired acceptance test plan after the required Owner approval gates. Do not use to implement product behavior.
---

# V1 Acceptance Testing

Use this skill for the acceptance workflow of one named V1 Round. Select an
explicit mode: **Prepare** creates or repairs the plan; **Execute** runs an
Owner-approved plan. Do not implement product behavior in this skill.

## Required input

- The named Round, its implemented scope, and relevant formal specifications.
- The mode: Prepare or Execute.
- Existing `docs/test-plan/Rn_Acceptance_Test.md` and
  `docs/test-plan/Rn_Manual_Tests.http`, when present.
- For Execute, evidence that the Owner approved the current paired plan.

Read `AGENTS.md`, the Round entry and workflow in
`docs/implementation/V1_Backend_Implementation_Plan.md`, and the relevant
formal specifications. Use `R4_Acceptance_Test.md` / `R4_Manual_Tests.http` and
`R5_Acceptance_Test.md` / `R5_Manual_Tests.http` as the local style and case-
granularity precedents; do not copy their credentials, tokens, run IDs, or
recorded results.

## Prepare

For every Round that accepts APIs, create or revise both files:

- `docs/test-plan/Rn_Acceptance_Test.md`
- `docs/test-plan/Rn_Manual_Tests.http`

Keep them executable in sequence and mutually consistent in test number, order,
synthetic data, requests, prerequisites, and expected result. The Markdown plan
is the authority for purpose, environment setup, database or other non-HTTP
operations, cleanup, and result recording. Every `.http` request is sequential
and commented with its matching Markdown test number, prerequisite operation,
and expected result; a non-HTTP Markdown operation needs a matching `.http`
comment that directs the operator to it.

Use representative main-success, authorization or existence-boundary, database-
write or atomicity, and error cases. Keep the coverage proportional to the
Round; record deferred fine-grained boundaries briefly rather than weakening
the contract or creating a large matrix. Use synthetic data, preserve the local
development database and its fixtures, and describe only temporary-state
cleanup. Never store credentials, database passwords, or usable JWTs; restore
token placeholders in `.http` files.

Each planned test must include expected results and distinct fields for **AI
acceptance result** and **manual acceptance result**, both initially
`未執行（等待 Owner review）`. Deliver the two paths, planned coverage, data
retention or cleanup approach, and any known gaps, then stop for Owner approval.
Do not execute the requests in Prepare mode.

## Execute

Execute only the exact plan the Owner approved. If that approval is absent or a
case or operation must expand the approved scope, explain the change and stop
for approval. A clerical correction that does not alter scope must update both
paired files consistently and be called out in the result.

Map every AI-performed HTTP operation to its `.http` test number. If using
`curl`, a test client, or another HTTP tool instead of the `.http` companion,
record the equivalent `.http` case and the tool used in the Markdown result.
Keep DB and other non-HTTP evidence with the matching Markdown test.

Record each actual result as `PASS`, `FAIL`, `BLOCKED`, or `未執行`, with concise
evidence, and keep AI and manual acceptance results separate. Restore only
temporary configuration and test files after execution; retain local synthetic
fixtures and never delete the regular development database volume. A destructive
rebuild may use only an isolated disposable database resource.

If an implementation defect is found, return it to current-Round development;
if a test-document defect is found, correct the paired cases; if a specification
question is found, report it for discussion. Never change an expected result
just to obtain a pass.

## Handoff standard

Report the executed commands, case-to-`.http` mapping, evidence, cleanup,
separate AI and manual result summaries, failures or unverified work, and any
development or specification follow-up. End with **「驗收結果待 Owner 確認」**.
Only the Owner may confirm the Round as accepted.
