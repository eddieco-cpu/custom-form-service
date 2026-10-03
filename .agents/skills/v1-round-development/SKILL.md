---
name: v1-round-development
description: Implement, continue, or repair one explicitly authorized V1 backend Round and deliver verified development results. Do not prepare or execute acceptance plans.
---

# V1 Round Development

## Required input

- The named Round and requested development work or review feedback.
- The current repository, Git state, and current-Round artifacts.
- The Round entry and specification mapping in
  `docs/implementation/V1_Backend_Implementation_Plan.md`.

Read `AGENTS.md` for common safeguards and phase boundaries. Read the Round's
relevant specifications and existing implementation before changing behavior.
Use the Implementation Plan for scope and dependencies; report discrepancies
between its progress records and current commits or artifacts.

If the authorized Round cannot be determined from the request and session
context, ask for that decision rather than choosing the next Round.

## Workflow

1. Confirm the requested scope, intended endpoints or artifacts, dependencies,
   deferred work, existing implementation, documents, and `git status`.
2. Give a short development summary: scope, relevant specification sections,
   verification approach, and excluded work. Proceed when authorized work has
   a clear contract; do not add an unnecessary approval gate. Present concrete
   decisions if required behavior or scope remains unresolved.
3. Implement the current Round using existing architecture, naming, and
   patterns. Avoid unrelated refactoring and speculative features. Follow the
   approved success behavior, validation, authorization, transaction rules, and
   error contract. Handle specification conflicts through `AGENTS.md`.
4. Run the applicable build and focused automated tests. Add or update tests
   for important new behavior and regressions in proportion to the change.
   Investigate failures and repeat affected checks after fixes; do not weaken
   tests to conceal implementation defects.
5. Review the final diff for scope, contract compliance, secrets, omissions,
   and unrelated changes. Update affected documentation and progress records,
   keeping development completion distinct from Round acceptance. Commit
   verified changes according to `AGENTS.md`.
6. Deliver the development handoff and stop. Identify acceptance preparation
   as the next step for API Rounds, but do not create, revise, or execute the
   acceptance plan unless that phase is explicitly authorized. If it is
   authorized, switch to `v1-acceptance-testing` and follow its approval gates.

For repair work, identify acceptance plans or recorded results made stale by
the change. Report the affected cases and required follow-up without silently
changing their expected results or claiming earlier acceptance still covers
the repaired implementation.

## Handoff standard

Deliver **「開發完成、待驗收」** only when the requested implementation and its
required development verification have succeeded. Otherwise report the actual
incomplete or blocked state.

Include:

- completed scope and intentionally deferred work;
- changed files and relevant specification sections;
- commands actually run and observed results;
- unverified items, failures, and unresolved decisions;
- commits and remaining Git state; and
- the next acceptance step and existing paired-plan paths, if present.

Do not declare the Round accepted or automatically start another phase.