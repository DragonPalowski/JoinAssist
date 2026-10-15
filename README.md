# JoinAssist

Free table, column and foreign-key JOIN completion for **SQL Server Management Studio 22 (x64)**.

[Download the latest release](https://github.com/DragonPalowski/JoinAssist/releases/latest) · [Italiano](docs/README.it.md) · [Report a bug](https://github.com/DragonPalowski/JoinAssist/issues)

JoinAssist reads your database metadata and suggests SQL while you type. JOIN conditions come from actual foreign keys, including composite keys. It does not use AI or guess relationships from column names.

**Early-stage community project.** The current interface is in Italian. SSMS updates can affect integration with its editor and connection internals; please report compatibility problems. This project is not affiliated with Microsoft.

## What it does

- Suggests related tables after `JOIN`, and conditions after `ON`.
- Completes table/view names and columns, including `table.column` and `alias.column`.
- Ranks matching names by closeness to your prefix: exact names first, then fewer extra characters. For example, `custo` puts `customers_tb` before `customerActivityHistory_vw`.
- Understands CTEs, derived queries and direct column lineage through views. It follows real foreign keys only where the relationship can be resolved safely.
- Generates compact SQL: no automatic alias, schema or square brackets unless required for valid, unambiguous SQL. Existing aliases are respected.
- Supports both directions of a foreign key, multiple JOINs and INNER/LEFT/RIGHT/FULL JOINs.

For example, given foreign keys from `OrderItems.OrderId` to `Orders.OrderId` and from `OrderItemDetails.OrderItemId` to `OrderItems.OrderItemId`, it can insert:

```sql
SELECT * FROM Orders
INNER JOIN OrderItems ON Orders.OrderId = OrderItems.OrderId
INNER JOIN OrderItemDetails ON OrderItems.OrderItemId = OrderItemDetails.OrderItemId
```

## Install

1. Download `JoinAssist-<version>.vsix` from [Releases](https://github.com/DragonPalowski/JoinAssist/releases/latest). Use the VSIX asset, rather than GitHub's source ZIP.
2. Save your work and close **all SSMS windows**.
3. Open the VSIX and select **SQL Server Management Studio 22** in Microsoft's installer.
4. Reopen SSMS, connect a query tab to your database and start typing.

You do not need build tools or a separate service. Earlier SSMS versions and other SQL editors are not supported. Avoid running multiple third-party completion providers together while diagnosing problems.

## Use

The query editor shows a small JoinAssist strip at the bottom:

| Control | Purpose |
| --- | --- |
| `Completamento automatico` | Enable suggestions while typing |
| `JOIN da foreign key…` | Open the manual foreign-key picker |
| `Aggiornamenti…` | Check GitHub and download a verified update |
| Status message | Explain loading, context or connection problems |

Try `SELECT * FROM Ord`, then press **Tab** to accept the selected table. After `JOIN `, choose a related table; after `ON `, choose its foreign-key condition. Use **↑/↓** to select, **Tab/Enter** to accept and **Esc** to dismiss. Insertions can be undone with **Ctrl+Z**.

Suggestions use the **active query tab's connection and database**. Database permissions determine which metadata is visible. SQL authentication, Windows authentication and Microsoft Entra integration depend on the active SSMS connection; JoinAssist does not provide a separate login screen.

See [a disposable demo schema](examples/demo-schema.sql) and [troubleshooting](docs/TROUBLESHOOTING.md).

## Scope and limitations

- CTEs, derived queries and views support direct column mappings, renamed columns and resolvable `*` expansions. Recursive/UNION outputs can offer column names, but ambiguous lineage does not produce inferred JOINs.
- Same-database, schema-qualified synonyms are supported. Table-valued functions can expose catalog column names; they do not acquire invented foreign keys.
- Temporary tables, table variables, cross-database/server names, dynamic SQL and PIVOT/UNPIVOT are not supported. SQLCMD and multi-server query contexts are excluded. A `USE` statement in the current text does not switch the metadata connection.
- This is a focused completion helper, not a complete T-SQL language service. Very large inputs are bounded (500,000 characters and 40 resolved sources).
- Automated tests cover parsing, metadata, ranking and editor insertion. They do not replace live testing on every SSMS release and authentication configuration.

## Privacy

JoinAssist reads catalog metadata through the active connection. It does not execute the SQL in your editor, send queries to external services, store passwords/tokens, or collect telemetry. It opens a metadata connection using the active SSMS client's connection information. The update button contacts GitHub on request without sending query text or database information. See [privacy and connection details](docs/PRIVACY.md).

## Updates and roadmap

From **0.5.0**, click **Aggiornamenti…** in the query editor strip. The window checks the latest ordinary GitHub release, shows the installed/available version and release notes, and enables **Scarica aggiornamento** when a newer version exists. No GitHub account is required. Checks run only when you open this window or click **Ricontrolla**; there is no background polling.

The VSIX is downloaded to `%LOCALAPPDATA%/JoinAssist/Updates` and verified against GitHub's SHA-256 digest before it becomes available. Use **Apri cartella** to locate it, save your queries, close every SSMS window and open the VSIX to install. JoinAssist never closes SSMS or starts an installer automatically. Old verified downloads remain on disk until you remove them.

**Upgrading from 0.4.1:** download and install 0.5.0 manually once; the previous version does not have the update button. Manual downloads remain available in [Releases](https://github.com/DragonPalowski/JoinAssist/releases/latest). If Microsoft's installer requests removal of an old copy, follow [the removal guide](docs/TROUBLESHOOTING.md#remove-joinassist).

The updater excludes drafts and prereleases and never offers a downgrade. The checksum detects incomplete or changed downloads; it is not a code-signing certificate. Planned improvements include an English interface and wider compatibility testing.

## Development

[Build and test](docs/BUILDING.md) · [Contribute](CONTRIBUTING.md) · [Release process](docs/RELEASING.md) · [Changelog](CHANGELOG.md)

The source, tests and build scripts are available under the [MIT license](LICENSE). Microsoft components used to build or run the extension retain their own licenses and are not bundled in the VSIX.
