# Backup, retention, and recovery

SQL Server LocalDB created a full copy-only backup of `SalesManagement` at **2026-11-16 00:55:41 UTC**. The backup used `CHECKSUM`, and `RESTORE VERIFYONLY WITH CHECKSUM` reported that backup set 1 was valid. Its size is **9,921,024 bytes** and its SHA-256 is shown below.

```text
EBFF01542263F14F96B48A3A306B7676C86FFB1AD11C5D502938F0F43E27C776
```

Backup path within the original workspace:

```text
build_verify/demo-database/private/backups/SalesManagement-20261116T005541484Z.bak
```

A JSON evidence file with the same filename stem records the verification command, timestamp and hash. The backup contains the original rows and requires the same handling as the original database. Windows permissions on the private directory allow the current account, local administrators and SYSTEM. The verification checked the backup set and checksums. A full restore to a recovery catalog is the next check if recovery becomes necessary.

## Return to the original application

Close the demo window and launch the application normally without `--demo-database`. The default connection is `SalesManagement`. An already running normal application continues to use its own original catalog.

If the original database ever needs recovery, first hash the backup with `Get-FileHash -Algorithm SHA256` and compare it to the evidence. In SQL Server Management Studio, connect to `(LocalDB)\MSSQLLocalDB` and use **Restore Database** with a new recovery catalog name such as `SalesManagement_Recovery_20261116`. In the Files pane choose new `.mdf` and `.ldf` paths. After restoring, run `DBCC CHECKDB` on the recovery catalog, compare its table counts to the original inventory, and verify a known authorized login. A default app connection change or restoration over the original name requires a separately reviewed operation.

## Proposed retention and deletion plan for review

1. Keep `SalesManagement` as the original reference and keep the verified backup. Use `SalesManagement_Demo` for demonstrations.
2. If cleanup of the original is later requested, take a new copy-only backup and prove it restores to a separate recovery catalog. Compare counts, catalog options, audit history and authorized accounts against the current original.
3. Identify test series by reviewed record IDs and provenance, including the legacy initializer's sample markers where present. Review customer, product, receipt, sales and audit chains together. Record exclusions for any mixed or uncertain chain.
4. Preserve schema, category/reference data required by retained products, roles and offices required by retained employees, one verified active administrator, and all dependencies and audit records of retained transactions. Prefer retaining an archive and using visible/hidden states to separate ordinary work from test records.
5. Bring the proposed row set, expected counts, impact on current balances, account recovery and tested rollback to the owner for approval. Any cleanup would need an explicit dependency-aware transaction and post-operation reconciliation. This package contains no cleanup job for the original catalog.

Separate approval is required before deleting or resetting original data, replacing an existing demo database, overwriting existing credentials, changing the application's default connection, or changing the application schema. The inventory includes shared reference and audit dependencies for that review.
