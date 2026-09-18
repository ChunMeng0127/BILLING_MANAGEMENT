# A3 Immutable Deployment Runbook

A3 removes mutable `master` from the production deployment path.

## Release model

Production is selected by a full 40-character Git commit SHA. Source is fetched at
that exact SHA, then published into a release-specific directory:

`/docker/billing-control/releases/<SHA>/publish`

The release directory is retained after deployment so an application rollback can
reuse the previously validated artifact instead of rebuilding it.

The production Compose file also pins its Alpine, .NET SDK/runtime and PostgreSQL
container images by digest. Updating those digests is a deliberate maintenance
change, not an implicit side effect of redeploying.

## Promotion gate

`deploy/release.sh <SHA>` refuses promotion unless GitHub contains a successful
`Build and PostgreSQL tests` workflow run for that exact SHA.

Use a full SHA only. Branch names such as `master`, `main`, or feature branches are
not valid deployment identifiers.

## Standard release

```bash
sudo /usr/local/lib/billing-control/release.sh <FULL_40_CHAR_SHA>
```

The script:

1. verifies successful CI for the exact SHA;
2. fetches that exact source revision;
3. builds or verifies its retained immutable artifact;
4. records an artifact SHA-256;
5. runs the A1 pre-deploy database backup;
6. stops the application before migrations;
7. runs EF migrations with `billing_migrator`;
8. recreates the app with `billing_runtime`;
9. requires HTTPS 200;
10. verifies runtime release labels/files;
11. records current/previous release metadata.

Release state is written under `/var/lib/billing-control-release`.

## Prepare without deploying

```bash
sudo /usr/local/lib/billing-control/release.sh <FULL_40_CHAR_SHA> --prepare-only
```

This validates CI, exact source fetch and artifact construction without backup,
migration, app stop, or production cutover.

## Application rollback

Database rollback is never automatic.

After reviewing migration compatibility:

```bash
sudo /usr/local/lib/billing-control/rollback-app.sh <PREVIOUS_SHA> --confirm-db-compatible
```

The rollback command requires an already prepared, hash-verified artifact and only
recreates the application. It does not run reverse migrations or restore a database.

## Runtime verification

```bash
docker inspect billing-control-app-1 \
  --format '{{index .Config.Labels "com.billing-control.release-sha"}}'

cat /var/lib/billing-control-release/current
```

The deployed SHA is also available inside the app container as
`/app/RELEASE_SHA`, with the artifact content hash in
`/app/RELEASE_ARTIFACT_SHA256`.

## GitHub branch protection

Production deployment no longer follows `master`, so a direct push cannot
implicitly redeploy production. Repository rules/branch protection should still
be enabled for `master` when repository administration access is available,
requiring pull requests and the production CI check.

The current ChatGPT GitHub integration does not have repository-administration
permission to create that ruleset; this control must be configured in GitHub
Settings by a repository administrator.
