# EF Core migration and legacy baseline procedure

**Status (2026-11-05):** fresh-install migrations and the legacy schema contract were verified in isolated `*_e2e` databases. After taking a fresh mode-`0600` backup, the exact baseline history marker was adopted into `smes_v2`; no application table or business row was altered. `StockBill.Code` is modeled at 500 characters to match the existing schema. The API was then rebuilt and started with `MigrateAsync`; startup reported no pending migrations.

## Provider and migration baseline

- Target framework: `net10.0`.
- Resolved EF Core and `Microsoft.EntityFrameworkCore.Design`: `9.0.0`.
- MySQL provider: Pomelo `9.0.0`; connector: `MySqlConnector 2.6.2`.
- The API configures MySQL `8.4.0` explicitly.
- Baseline migration: `20261105083211_InitialSchemaBaseline` (EF product version `9.0.0`).
- `SmesDbContextModelSnapshot` was generated with the migration. `dotnet ef migrations has-pending-model-changes` reported no changes at the baseline cut.
- The baseline `Up` creates the 68 application tables from the full model. Its `Down` intentionally throws: removing a schema baseline must never silently drop application tables or data.

A design-time factory lives under `Smes.Infrastructure/Migrations`. It prevents EF tooling from booting `Program.cs` or inheriting the app's live connection string. For database-changing EF commands, set `SMES_MIGRATIONS_CONNECTION_STRING` explicitly to an isolated database; the factory's fallback is a non-live design-only target.

## Startup behavior

`Program.cs` no longer calls `EnsureCreatedAsync`. Startup first checks the selected database's migration-history state, then calls `Database.MigrateAsync()` before seeding:

- Empty schema: migrate the baseline, then seed according to the existing seed flags.
- Known migration history: apply pending migrations.
- Existing tables without `__EFMigrationsHistory`, or an empty history table alongside application tables: fail fast with an adoption instruction. Startup will not try to create baseline tables over an `EnsureCreated` schema.

The existing `scripts/migrations/20261105_material_issue_subcontract_quality.sql` is a separate additive feature SQL script, not an EF migration. It was inspected and left unchanged; it has no migration-history integration.

## Observed schemas and adoption status

The read-only inspection and baseline adoption on 2026-11-05 recorded:

| Database | Application tables (total) | Application columns (total) | Index entries (total) | Constraints (total) | `StockBill.Code` | EF history |
|---|---:|---:|---:|---:|---|---|
| Live `smes_v2` after adoption | 68 (69) | 1,196 (1,198) | 124 (125) | 120 (121) | `varchar(500)` | baseline marker present |
| E2E `smes_feature_e2e` at inspection | 68 (69) | 1,196 (1,198) | 124 (125) | 120 (121) | `varchar(100)` | baseline marker present |

The totals include the single `__EFMigrationsHistory` table. The baseline and current EF model now preserve the live `StockBill.Code varchar(500)` width. A fresh backup was created at `backups/smes-before-mobile-api-deploy-20261105.sql` (351,241 bytes, mode 0600). The strict schema verifier passed against `smes_v2`; the adoption tool created and read back only the baseline history marker. After API restart, EF reported no pending migrations. `StockBill` still contained 36 rows.

The baseline does not include a tenant-scoped unique `(TenantId, Code)` index for `RepairOrder`. Keep this as a separate follow-up migration. Before adding it, check duplicate groups and stop if any exist; never delete or rewrite rows to make the index succeed. A read-only production check found 9 repair orders and 0 duplicate tenant/code groups, but the follow-up index has not yet been generated or applied.

## Explicit legacy adoption tool

`Smes.MigrationAdoption.csproj` verifies the full schema contract from `baseline-schema-contract.json` before it can create or insert EF history. It checks exact table/type/engine/collation, every column/type/nullability/default/identity detail, indexes, and constraints. A mismatch exits nonzero without writing history. If the history table already exists, it must match EF's expected structure and be empty for adoption. After the schema passes, the tool creates only `__EFMigrationsHistory`, inserts the baseline ID/product version, and reads that exact row back. It does not run the migration's `Up` against a populated legacy schema.

The live adoption is complete for `smes_v2`. For a different legacy database, do not copy the marker blindly: take a backup, stop older API instances/DDL writers, run verify-only against that exact database, and require a full schema-contract PASS before the explicitly confirmed apply step. Never relax the contract on a mismatch.

```bash
# Read-only preflight; must report PASS before considering the apply command.
dotnet run --project scripts/migrations/Smes.MigrationAdoption.csproj -- \
  --contract scripts/migrations/baseline-schema-contract.json --verify

# Only for a first-time adoption; smes_v2 has already been adopted—do not rerun.
dotnet run --project scripts/migrations/Smes.MigrationAdoption.csproj -- \
  --contract scripts/migrations/baseline-schema-contract.json \
  --apply --confirm-smes-v2-baseline
```

The tool requires `SMES_MIGRATIONS_CONNECTION_STRING` and checks that the selected database name is exactly `smes_v2` for the production-target confirmation. For E2E validation only, apply is allowed to a database whose name ends with `_e2e` when `--allow-e2e` is present. The tool does not accept an override for schema mismatches.

To generate later migrations, keep EF Core, Pomelo, and `dotnet-ef` on major/version `9.0.0`, and use the Infrastructure project as the migrations project with API as startup project. Review generated `Up` and `Down` operations before applying anything.

## Verification evidence

The original fresh-migration/adoption E2E checks were completed before aligning the baseline `StockBill.Code` width to the live 500-character schema. After the alignment, the model snapshot check reported no pending model changes; the exact updated contract passed read-only against live `smes_v2`, and the baseline marker was applied and read back.

- `smes_migration_fresh_e2e`: `dotnet-ef database update` completed and recorded `20261105083211_InitialSchemaBaseline / 9.0.0`.
- `smes_migration_adopt_e2e`: baseline `Up` DDL was used to create a schema-only fixture without EF history; the adoption tool passed its exact contract, created history, and read back the same marker.
- Both isolated test schemas contain 69 base tables (68 application tables plus EF history). The adoption preflight passed with 68 application tables, 1,196 columns, 124 index entries, and 120 constraints.
- The active `smes_feature_e2e` database retained its earlier 100-character `StockBill.Code` schema and prior baseline marker; it was not used for the live 500-character contract after alignment.
- `dotnet build backend/src/Smes.Api/Smes.Api.csproj`: succeeded, 0 errors; existing nullable warnings remain in unrelated backend files.
- `dotnet build scripts/migrations/Smes.MigrationAdoption.csproj`: succeeded, 0 warnings/errors.
- `dotnet test backend/tests/Smes.E2E/Smes.E2E.csproj`: exited successfully. This project is a console E2E harness, not a test-SDK project, so `dotnet test` discovers no test cases; the migration and adoption E2E checks above are the substantive database validations.

No application-table schema was altered in production; the only schema addition was `__EFMigrationsHistory` and its baseline marker. No business rows were deleted, no volume was reset, and no `DROP`, `DELETE`, or `TRUNCATE` statement was executed. The temporary E2E validation schemas were left intact rather than destructively cleaned up.
