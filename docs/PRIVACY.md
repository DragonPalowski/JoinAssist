# Privacy and connections

JoinAssist runs in the SSMS process. It reads the current query text locally to identify sources, aliases, scopes and the completion position. It queries SQL Server catalog metadata for names, columns, definitions, dependencies and foreign keys. Metadata stays in the running extension's memory. From 0.7.0 it is cached for 2 minutes by default (configurable 1–15), isolated by native connection/session, server and database. **Aggiorna metadati** invalidates that scope. Bounded analysis caches also retain recent query text only in process memory.

It uses the active query tab's SSMS client connection information to open a metadata connection. With Microsoft Entra, compatible client tokens or token callbacks may be reused in memory. It does not save passwords or tokens, run a separate login service, or create credential files.

The extension does not execute the query text you are editing. It has no AI provider, analytics service, telemetry collection or external query transmission.

From 0.5.0, opening **Aggiornamenti…** or clicking **Ricontrolla** sends an unauthenticated HTTPS request to the public GitHub Releases API for `DragonPalowski/JoinAssist`. Clicking **Scarica aggiornamento** downloads the selected VSIX from that repository and GitHub's asset hosts. Requests include a JoinAssist user agent with the installed extension version; GitHub receives normal network information such as your IP address under its own policies. Query text, database/server names, account information, SQL credentials and Entra tokens are not sent. There is no background polling, GitHub login, stored GitHub token or telemetry.

Downloads are stored in `%LOCALAPPDATA%/JoinAssist/Updates`. Incomplete or invalid downloads are removed; verified VSIX files remain until you remove them. The extension does not launch them automatically. Closing the update window cancels its network operation. Version 0.4.1 has no update-check functionality.

Catalog visibility follows your SQL permissions. Local tooling, SSMS itself, database auditing and GitHub have their own behavior outside this extension's scope. If reporting a bug, share a synthetic schema and sanitized messages rather than real business queries, credentials or screenshots with connection details.

From 0.7.0, `%LOCALAPPDATA%/JoinAssist/preferences.json` contains user preferences only: automatic completion, timing, shortcut, acceptance keys, formatting and two-hop paths. It contains no query, connection, metadata or credential fields. **Copia diagnostica** puts extension/host versions and the diagnostic code on the clipboard; it does not send a report or include raw status messages, SQL, identifiers, account names or credentials.
