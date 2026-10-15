# Privacy and connections

JoinAssist runs in the SSMS process. It reads the current query text locally to identify sources, aliases, scopes and the completion position. It queries SQL Server catalog metadata for names, columns, definitions, dependencies and foreign keys. Metadata stays in the running extension's memory.

It uses the active query tab's SSMS client connection information to open a metadata connection. With Microsoft Entra, compatible client tokens or token callbacks may be reused in memory. It does not save passwords or tokens, run a separate login service, or create credential files.

The extension does not execute the query text you are editing. It has no AI provider, analytics service, telemetry collection or external query transmission. Version 0.4.1 does not contact GitHub automatically and has no update-check button. Downloading or publishing releases uses GitHub separately under its own policies.

Catalog visibility follows your SQL permissions. Local tooling, SSMS itself, database auditing and GitHub have their own behavior outside this extension's scope. If reporting a bug, share a synthetic schema and sanitized messages rather than real business queries, credentials or screenshots with connection details.
