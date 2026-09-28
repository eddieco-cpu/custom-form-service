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
concurrency behavior, and validation order.

If authoritative documents conflict, report the filenames, sections, conflict,
and affected work. Do not silently choose one interpretation.

## Implementation rounds

Execute only the implementation Round explicitly requested by the user.

For the current Round:

1. Read the relevant specifications.
2. Inspect the existing repository and Git status.
3. State the Round scope and verification approach.
4. Implement only the current Round.
5. Run the required verification.
6. Commit verified changes using single-purpose commits.
7. Report results, commands, verification evidence, unresolved issues,
   changed files, and commit SHA.
8. Stop and wait for the next explicit Round instruction.

Do not automatically start the next Round.

When review feedback concerns the current Round, complete the fixes,
repeat verification, and report the new commit before stopping.

## Current delivery boundary

The first delivery stage is the local Email/Password backend.

Google Authentication remains part of the formal V1 backend, but is scheduled
for the later G1 Round.

Keep all seven V1 domain tables, including `user_identities`.

Nuxt frontend, Railway deployment, production domain setup, and live production
HTTPS verification use separate implementation plans.

## Git and data safety

- Inspect `git status` before editing.
- Preserve existing and uncommitted user changes.
- Do not use force push, destructive reset, or history rewriting.
- Do not commit secrets, passwords, signing keys, tokens, or credentials.
- Use isolated disposable database resources for destructive rebuild tests.
- Do not delete the regular development database volume.
- Do not commit work from a later Round.