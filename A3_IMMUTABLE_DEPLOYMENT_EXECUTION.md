# A3 Immutable Deployment Execution Record

Status: PRODUCTION COMPLETE; GitHub branch-protection administration follow-up pending

## Objective

Remove mutable `master` from the production deployment path and make every release
traceable to one exact Git revision and one verified application artifact.

## Production baseline

Legacy production was running:

`6677680feb12141eefe97dd5d21b37fd3b6b4bf1`

The legacy Compose flow cloned the latest `master` when rebuilding. A redeploy on a
different day could therefore build different code.

## Implemented release model

Production now requires a full 40-character Git commit SHA.

Each release is stored under:

`/docker/billing-control/releases/<SHA>/publish`

The release directory contains:

- exact-source metadata;
- the published application;
- `RELEASE_SHA`;
- `RELEASE_ARTIFACT_SHA256`;
- a release manifest containing CI and build metadata.

Production Compose pins the Alpine, .NET SDK, .NET runtime and PostgreSQL images by
digest. It no longer clones or follows `master`.

## Release safety gates

`deploy/release.sh` now:

1. accepts only a full commit SHA;
2. requires a successful `Build and PostgreSQL tests` GitHub Actions run for that exact SHA;
3. fetches and verifies the exact Git revision;
4. builds or verifies the retained release artifact;
5. requires an A1 pre-deploy database backup;
6. runs EF migrations with `billing_migrator`;
7. deploys the app with `billing_runtime`;
8. requires external HTTPS 200;
9. verifies the runtime release SHA and artifact hash;
10. records current, previous and historical release metadata.

The first legacy-to-A3 cutover bootstraps `RELEASE_SHA` from the actually running
legacy production SHA before the backup gate. This keeps A1 backup operations valid
without claiming that a new release is already deployed.

## Deterministic artifact validation

ASP.NET static web asset metadata initially embedded build-time Last-Modified timestamps,
so two clean builds of the same Git SHA produced different whole-artifact hashes.

A3 normalizes those generated timestamps to the source Git commit timestamp.

Two independent clean builds of the production SHA then produced exactly the same
artifact SHA-256:

`4cb02da15cfc9500fc8e237f3d7527b94cb25a6d088344d885f964b448f663a7`

This satisfies the A3 reproducibility requirement for the tested release.

## CI gate validation

Release SHA:

`6677680feb12141eefe97dd5d21b37fd3b6b4bf1`

Historical successful CI run:

`https://github.com/ChunMeng0127/BILLING_MANAGEMENT/actions/runs/34467511472`

The A3 CI gate accepted this release.

The gate was also tested against:

`06680f7bedf8ca4a1af2e9660e7f7e9cd608edbf`

That revision has no successful required workflow run because of the repository's
known three date-dependent integration-test failures. A3 correctly refused promotion
before source build or production change.

## Production cutover

Cutover target:

`6677680feb12141eefe97dd5d21b37fd3b6b4bf1`

No business-code upgrade was performed during A3; the live application remained on the
same known-good revision while the deployment mechanism changed.

Pre-deploy backup:

- file: `billing-20260918T055531Z.dump`
- SHA-256: `0cccfe3930c67e78cce5189d224a311cd486a00c54d728f9039026a652d2972a`
- Microsoft 365 offsite read-back: PASS

Migration check:

- role: `billing_migrator`
- pending migrations: none
- result: PASS

Runtime verification:

- release label SHA: `6677680feb12141eefe97dd5d21b37fd3b6b4bf1`
- in-container `RELEASE_SHA`: same
- artifact SHA-256: `4cb02da15cfc9500fc8e237f3d7527b94cb25a6d088344d885f964b448f663a7`
- app mount: release-specific publish directory
- HTTPS: 200
- PostgreSQL: healthy
- recent application DB/permission errors: 0
- mutable `master` references in production Compose: none

## Backup restore regression

The A3 pre-deploy backup was restored in an isolated PostgreSQL instance.

Result:

- restore: PASS
- tables: 28
- migrations: 9
- ownership mismatches: 0
- runtime table grants: 28/28
- backup table grants: 28/28

One first-start readiness race occurred in the temporary restore container; an immediate
debug rerun completed the full restore successfully. Production was not affected.

## Rollback rehearsal

`deploy/rollback-app.sh` was exercised against the already prepared current release.

- application recreate: PASS
- HTTPS: 200
- migration history before: 9
- migration history after: 9
- automatic database restore: not performed
- reverse migration: not performed

The rollback path therefore remains application-only by design.

## Production release state

Current release metadata is maintained under:

`/var/lib/billing-control-release`

Runtime SHA can also be checked with:

`docker inspect billing-control-app-1 --format '{{index .Config.Labels "com.billing-control.release-sha"}}'`

## Rollback safety

Application rollback and database rollback remain separate decisions. A previous
application artifact may be selected only after migration compatibility is reviewed.

The rollback script never restores a database automatically.

## GitHub branch protection follow-up

The repository currently has no ruleset and `master` is not protected.

The available GitHub integration does not have repository-administration permission to
create branch protection/rulesets. A repository administrator should enable protection
for `master`, requiring pull requests and the production CI check.

This is a repository-governance follow-up, not a production deployment blocker:
production no longer follows `master`, so direct changes to `master` cannot implicitly
redeploy production.

## Known CI baseline

The repository still has three pre-existing date-dependent .NET integration-test failures.
They are unrelated to A3 and remain scheduled for A6.
