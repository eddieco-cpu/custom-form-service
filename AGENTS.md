# Project Instructions

## Source of truth

Before changing project behavior, read:

1. `docs/specs/00-v1-overview.md`
2. `docs/specs/01-v1-domain-specification.md`
3. `docs/specs/02-v1-database-specification.md`
4. `docs/specs/03a-v1-api-contract-common.md`
5. `docs/specs/03b-v1-api-contract-auth-user-form.md`
6. `docs/specs/03c-v1-api-contract-submission-statistics.md`
7. `docs/specs/04-v1-error-catalog.md`
8. `docs/specs/05-v1-system-constraints.md`
9. `docs/implementation/V1_Backend_Implementation_Plan.md`

The approved specifications are the source of truth for business rules,
database schema, API contracts, error codes, authorization, transactions,
concurrency behavior, and validation order. Use the Implementation Plan for
Round scope, dependencies, and progress.

If authoritative documents conflict, report the filenames, sections, conflict,
and affected work. Stop only the affected work until the conflict is resolved;
do not silently choose one interpretation or change a frozen contract.

## Implementation rounds and phase boundaries

Execute only the Round and workflow phase explicitly authorized by the user.
The default sequence is:

1. Development: implement the requested Round, run its required build and
   necessary automated tests, inspect the diff, and update affected documents.
   Deliver the development report and stop. Do not automatically prepare
   acceptance artifacts.
2. Acceptance preparation: when requested, use the acceptance skill to create
   or revise the paired Markdown and `.http` plan. Deliver both files and stop
   for the Owner's review. Do not execute acceptance operations in this phase.
3. AI acceptance execution: execute only the current Owner-approved paired
   plan. Record actual results and deliver the execution report, then stop
   for manual acceptance and Owner confirmation.
4. Manual acceptance and completion: keep AI and manual results separate.
   Mark the Round accepted only after the Owner explicitly confirms acceptance
   based on the recorded results and any agreed outstanding work.

A request may explicitly authorize multiple phases. Follow that authorization,
but retain the approval gate before acceptance execution. Approval of a plan
authorizes execution when the Owner's instruction makes that intent clear.
Do not treat plan preparation, development completion, or acknowledgment of
results as acceptance approval.

Development builds, automated tests, and self-checks do not require acceptance
plan approval. Development self-checks do not replace a separately requested
code review or manual acceptance.

Commit verified, authorized changes using single-purpose commits. Preserve the
distinction between development, plan preparation, execution, and acceptance
in progress records. Report actual commands and results, changed files,
unverified work, unresolved issues, commit SHA, and remaining Git state.
Never claim an unexecuted check passed.

When review feedback concerns the current Round, complete the authorized
fixes, repeat affected verification, and report the new commit. If a fix
invalidates an existing acceptance plan or recorded result, flag it and keep
that result from being treated as current; follow the appropriate phase and
approval gates for any subsequent acceptance work.

Do not automatically start the next phase or the next Round.

## Current delivery boundary

The first delivery stage is the local Email/Password backend.

Google Authentication remains part of the formal V1 backend, but is scheduled
for the later G1 Round. Do not implement it before G1 is explicitly requested.

Keep the complete V1 schema, including `user_identities`, as defined by the
approved database specification.

Nuxt frontend, Railway deployment, production domain setup, and live production
HTTPS verification use separate implementation plans.

## Repository-local workflow skills

- Use `.agents/skills/v1-round-development/SKILL.md` for an explicitly
  authorized Round's development or current-Round repair.
- Use `.agents/skills/v1-acceptance-testing/SKILL.md` for acceptance-plan
  preparation or Owner-approved acceptance execution. The skill owns the
  paired-file format, case mapping, result recording, and handoff details.
- These skills organize workflows. This file owns common safeguards and phase
  boundaries; the approved specifications own all business contracts.
- If the Implementation Plan or a skill conflicts with these phase boundaries,
  report the conflict before advancing to the affected phase.

## Git and data safety

- Inspect `git status` before editing.
- Preserve existing and uncommitted user changes.
- Do not use force push, destructive reset, or history rewriting.
- Do not commit secrets, passwords, signing keys, tokens, or credentials.
- Use isolated disposable database resources for destructive rebuild tests.
- Do not delete the regular development database volume.
- Use the local development DB for ordinary acceptance and preserve its
  synthetic fixtures.
- Restore temporary configuration and test files without reverting user changes
  or deleting retained acceptance fixtures.
- Do not commit unrelated work or work from a later Round.