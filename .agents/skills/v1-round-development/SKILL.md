---
name: v1-round-development
description: Implement or correct an explicitly authorized V1 backend Implementation Plan Round through a verified development handoff. Do not use for acceptance-plan preparation or execution.
---

# V1 Round Development

Use this skill when the user explicitly asks to implement, continue, or repair
one named V1 Backend Implementation Plan Round. It is for development work,
not for executing acceptance tests.

## Required input

- The named Round and requested change or review feedback.
- The current repository, Git state, and any current-Round artifacts.
- The Round's relevant formal-specification sections, as mapped by
  `docs/implementation/V1_Backend_Implementation_Plan.md`.

If no Round is explicitly authorized, identify the needed decision instead of
choosing one. Treat `AGENTS.md` as the common workflow and data-safety guardrail;
the formal specifications own behavior, schema, API, errors, authorization,
transactions, concurrency, and validation order.

## Workflow

1. Confirm the named Round, its intended endpoints or artifacts, dependencies,
   deferred work, existing implementation, current documents, and `git status`.
   Read the plan's Round entry and the relevant formal specifications before
   changing behavior. Use the plan's Round map to determine status, but report
   a mismatch between that map and current commits or artifacts rather than
   silently rewriting status.
2. Give a short development summary: scope, spec mapping, verification approach,
   and explicitly excluded later-Round or G1 work. When the work is authorized
   and the contract is clear, proceed without an extra approval gate.
3. Implement only the current Round. Do not change a frozen formal contract or
   invent an API, table, error, policy, or capacity rule. If authoritative
   documents conflict, report filenames, sections, the conflict, and affected
   work; stop only the affected work until it is resolved.
4. Run the applicable build and focused automated tests, then review the diff
   for scope, contract, secrets, and unrelated changes. Update affected
   documentation and Round progress records. Commit verified, single-purpose
   current-Round changes when the repository workflow calls for a commit.
5. For an API Round, prepare or revise the paired acceptance artifacts through
   `v1-acceptance-testing` in **Prepare** mode after development verification.
   Do not execute acceptance in this skill and do not bypass the Owner plan
   review.

## Handoff standard

Deliver the status **「開發完成、待驗收」** only when the requested development
and its appropriate development verification have succeeded. Include:

- scope completed and intentionally deferred work;
- changed files and relevant specification sections;
- commands actually run with observed results;
- unverified items, failures, risks, and follow-up work;
- commits and remaining Git state; and
- the next acceptance step, including the paired-plan paths when applicable.

Do not declare the Round completed or accepted. Owner approval of the acceptance
plan and Owner confirmation of recorded results remain separate gates.
