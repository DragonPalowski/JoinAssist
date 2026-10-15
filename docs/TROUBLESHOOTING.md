# Troubleshooting

## No JoinAssist strip or suggestions

Confirm that the VSIX was installed into **SSMS 22 x64**, close all SSMS windows and reopen it. Open an ordinary SQL query tab, connect it to a database, and enable `Completamento automatico` in the strip below the editor. The main Extensions menu's **Customize Menu** controls menu placement; it does not uninstall an extension.

After `JOIN`, include a trailing space. The editor's own completion list can still appear without JoinAssist: its presence alone does not prove a third-party extension is installed.

## No foreign-key candidates

JoinAssist requires real, visible foreign-key metadata. Matching column names alone are insufficient. Check the selected database in the query toolbar and your metadata permissions. Test two simple base tables first. Expressions, cross-database sources or ambiguous CTE/view lineage may intentionally produce no candidates.

Do not grant broader permissions solely to troubleshoot without involving your database administrator.

## Connection or Microsoft Entra errors

Read the full status message in the JoinAssist strip. A connected Object Explorer is not sufficient: the **query tab itself** must have a working connection. Reconnect that tab using its normal SSMS connection dialog. Do not send passwords, access tokens or connection strings in an issue.

If reconnecting does not help, report the SSMS version, JoinAssist version, authentication method and sanitized message. SSMS updates can change internal connection integration. Do not change authentication methods just to bypass the extension's error.

## Query or connection changed

Suggestions belong to a particular query snapshot and connection. If either changed before insertion, reopen the suggestions. Repeated context-change warnings with an unchanged tab are a bug: include a minimal reproduction in an issue. Undo an unexpected insertion with Ctrl+Z.

## Invalid VSIX or installation error

Download the `.vsix` release asset again; do not rename GitHub's source ZIP into a VSIX. If desired, compare `Get-FileHash -Algorithm SHA256` with the release's `SHA256SUMS.txt`. Preserve Microsoft's installer log and remove personal paths or credentials before sharing it.

## Remove JoinAssist

Save queries and close every SSMS window. Run [Uninstall-JoinAssist.cmd](../Uninstall-JoinAssist.cmd) from this repository; it invokes Microsoft's installer for **only JoinAssist**, and the installer handles removal confirmation. It does not remove other extensions or SSMS.

For a custom SSMS location, invoke that installation's `VSIXInstaller.exe` with:

```text
/uninstall:JoinAssist.FK.4d45475a-842b-42b3-b2f7-b392df87bdb8
```

If removal fails, keep the installer log. Avoid deleting entire SSMS extension/cache directories: they may contain unrelated components.

## Report a reproducible problem

Use the [bug report form](https://github.com/DragonPalowski/JoinAssist/issues/new?template=bug_report.yml). Replace business names with generic tables, and include minimal table/foreign-key definitions when relevant. Public issues must not contain customer data, corporate server addresses, account names or secrets.
