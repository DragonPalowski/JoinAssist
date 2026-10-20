# DELETE planning

From 0.12.0, **JoinAssist → Prepara DELETE con dipendenze…** prepares a draft from a complete DELETE with a WHERE filter. Put the cursor inside the statement (or just after its semicolon), open the command, inspect **Dipendenze e ordine** and **Script SQL**, then choose **Inserisci script di prova**. This replaces only the original DELETE in one editor edit; Ctrl+Z restores it. Cancelling leaves the query unchanged. A changed query/caret/connection invalidates insertion.

The extension executes only three metadata SELECTs: a database VIEW DEFINITION permission check, table/key/feature metadata and database-wide foreign keys. It does not send the filter to SQL Server, read/count affected rows, grant permissions or execute any DELETE. The preview therefore has no row-count estimate. Metadata is read fresh rather than taken from the completion cache; it is still subject to permissions and can change before you execute the script.

## Scope and ordering

- Follow incoming enabled NO ACTION and CASCADE FKs from the root, across levels. Disabled FKs are reported and not followed; enabled untrusted FKs are retained and identified.
- Capture the root filter once, then materialize each dependent row scope in dedicated temporary tables **before any DELETE**. Keep primary keys and referenced unique-key columns needed by later levels. Composite FKs use every column pair. Multiple paths become OR/EXISTS scopes, without duplicate DELETEs for the same table.
- Delete children before parents using primary-key membership. Cascade-only selected tables are left to SQL Server; their blocking descendants are still handled first. A mixed CASCADE/NO ACTION target receives an explicit scoped DELETE.
- SET NULL and SET DEFAULT do not add child rows to the delete scope. They are reported as database-managed updates. Relationships between already selected tables still constrain ordering, so an automatic update cannot change a captured key before that table's deletion.

Foreign keys describe structural dependence, not exclusive business ownership: a dependent row can also refer to another parent. Review the scope and application meaning of each included table. This feature does not discover relationships enforced only in code or triggers.

## Generated script

The draft uses qualified names/internal aliases, SELECT INTO snapshots, UPDLOCK/HOLDLOCK, a transaction and TRY/CATCH. It rolls back on an error and **ends with ROLLBACK by default**. Only after inspecting/testing the whole script should you manually change the marked final rollback to COMMIT. Do not change the rollback in CATCH. Original variables/parameters must be declared or replaced when you execute the draft.

The script refuses an existing transaction and IMPLICIT_TRANSACTIONS ON. Its initial guards use RAISERROR/RETURN rather than THROW so they do not invoke XACT_ABORT rollback on a caller's transaction; see Microsoft's [XACT_ABORT documentation](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-xact-abort-transact-sql). It explicitly sets ROWCOUNT to 0 to avoid incomplete snapshots/deletes; **that setting remains in the session**. It restores the prior XACT_ABORT flag and cleans temporary scopes. Locks can block other sessions or encounter deadlocks; transaction logging and resource costs still apply during a rollback test. Review SQL Server errors and transaction state if execution is interrupted. No execution/installation is triggered by opening or confirming the preview.

Rowversion values used in captured keys are projected as binary(8), rather than copying their generated type into temporary tables. See Microsoft's [rowversion documentation](https://learn.microsoft.com/en-us/sql/t-sql/data-types/rowversion-transact-sql).

## First-version boundaries

The root must be a persistent table in the active database, with a complete WHERE. A simple existing alias is accepted. TOP, OUTPUT, OPTION, CTE targets, joined/multi-source DELETEs, temporary/variable/cross-database targets and predicates depending on global session/transaction state are rejected. Use a standalone statement, without EXECUTE AS/REVERT/SETUSER history: the metadata connection cannot replicate that execution context. WHERE presence is not proof that a filter selects few rows: inspect it yourself.

Every selected table needs a visible primary key. Cycles/self references, active DML triggers, enabled security policies, temporal/history or memory-optimized tables, missing FK/column metadata and column names differing only by case block script generation. Database-managed SET actions on tables with unsupported features are also blocked. Limits: 64 reachable tables, 256 internal links, 100,000 catalog columns and 50,000 FK-column records. A blocked plan has no insertable partial script and explains the reason.

Synthetic tests verify graph/scoping structure, generated SQL syntax, metadata-only reads and editor/UI behavior. Live SQL Server DELETE, concurrency, permissions, trigger and referential-action execution has not been exercised by the release's automated tests. Treat the result as a draft for review and a controlled test, not a guarantee that it is suitable for a production database.
