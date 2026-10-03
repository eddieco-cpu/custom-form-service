---
name: v1-acceptance-testing
description: Prepare paired Markdown and .http acceptance plans for a named V1 API Round, or execute the current Owner-approved plan. Do not implement product behavior.
---

# V1 Acceptance Testing

## Mode and required input

Select the mode from the user's request and session context:

- **Prepare**: create or revise the acceptance plan for Owner review.
- **Execute**: run the current paired plan after Owner approval and execution
  authorization.

When the user asks to start acceptance testing without an approved plan,
start with Prepare. Never infer approval from the plan's existence or from
development completion. Clarify ambiguous execution authorization before
running acceptance operations.

Read `AGENTS.md`, the Round entry and workflow in
`docs/implementation/V1_Backend_Implementation_Plan.md`, and relevant formal
specifications. Confirm the implemented scope and inspect existing artifacts:

- `docs/test-plan/Rn_Acceptance_Test.md`
- `docs/test-plan/Rn_Manual_Tests.http`

Use the R4 and R5 paired acceptance files as style and case-granularity
precedents. Verify their actual repository paths; do not rename existing files
just to match a precedent reference. Do not copy credentials, tokens, run IDs,
or recorded results from an earlier run.

For Execute, identify the Owner approval covering the current paired plan and
the authorization to execute it. Resolve substantive changes made since that
approval before proceeding.

## Prepare

Create or revise both paired files. Keep test numbers, order, synthetic data,
requests, prerequisites, and expected results mutually consistent.

The Markdown plan owns:

- purpose and planned coverage;
- environment prerequisites and startup instructions;
- ordered cases, requests, and expected results;
- database and other non-HTTP operations;
- data retention and temporary-state cleanup; and
- separate AI and manual actual-result fields with concise evidence.

Make each `.http` request sequential and comment it with its matching Markdown
test number, prerequisites, and expected result. For non-HTTP steps, add a
matching `.http` comment directing the operator to the Markdown instructions.
Use `.http` examples or equivalent curl requests in the Markdown; avoid
duplicating both forms unnecessarily.

Cover representative success behavior, authorization or existence boundaries,
database writes or atomicity where applicable, and error cases. Keep coverage
proportional to the Round. Briefly identify deferred fine-grained boundaries
without weakening the contract or creating large matrices.

Use synthetic fixtures and follow `AGENTS.md` for database preservation and
cleanup. Describe run identifiers, repeated-run handling, and retained data.
Use credential and token placeholders; keep usable secrets out of both files
and recorded evidence.

For a new or substantively revised case, initialize both result fields to
`未執行（等待 Owner review）`. Preserve prior results as clearly labeled
historical runs; do not present them as evidence for a changed case.

Deliver links to both files, planned coverage, data retention or cleanup, and
known gaps. Stop for Owner approval. Do not execute acceptance requests or
planned database mutations in Prepare mode.

## Execute

Run only the current paired plan covered by Owner approval. Missing approval
or a substantive plan change requires review before execution of the affected
operations.

Clerical corrections are limited to changes that do not alter test semantics,
such as formatting, spelling, or broken links. Keep the paired files consistent
and disclose these corrections in the report. Changes to requests, identities,
preconditions, expected results, case coverage, or data effects require renewed
Owner approval. Re-check any approval affected by implementation repairs.

Follow the approved sequence and prerequisites. Map every AI HTTP operation
to its `.http` test number. If using curl, a test client, or another HTTP tool,
record the equivalent `.http` case and the tool used. Passing development
integration tests alone does not establish that acceptance cases passed.

Record each case as `PASS`, `FAIL`, `BLOCKED`, or `未執行`, with concise evidence.
Keep database and non-HTTP evidence with the matching Markdown case. If a
prerequisite fails, mark dependent cases accordingly rather than treating them
as executed successfully.

Keep AI and manual results separate. Do not infer manual PASS from AI PASS.
Record manual results only from Owner-provided results or an explicitly
authorized, observed manual run, and identify their source.

Restore temporary configuration and test files according to the approved
plan and `AGENTS.md`; retain synthetic fixtures. Remove usable tokens or
credentials introduced during execution and restore placeholders. Keep
evidence sufficient to assess results without exposing secrets.

Route implementation defects to current-Round development, test-document
defects to paired-plan correction, and specification questions to discussion.
Do not silently switch to product implementation. Never change expected
results solely to obtain a pass.

## Handoff standard

Report:

- the executed plan and run identifier;
- commands actually run and tools used;
- case-to-`.http` mapping and concise evidence;
- separate AI and manual result summaries;
- retained data and completed temporary-state cleanup; and
- failures, blocked or unexecuted cases, and required follow-up.

End with **「驗收結果待 Owner 確認」**. This indicates pending review, not a
passing outcome. Do not mark the Round accepted; follow `AGENTS.md` for Owner
confirmation and progress updates.