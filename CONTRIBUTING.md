# Contributing

Bug reports, reproducible SQL examples, documentation fixes and pull requests are welcome. Please open an issue before a large feature so its scope can be discussed.

For a bug, include SSMS and JoinAssist versions, the authentication category if relevant, expected/actual behavior, and a minimal **synthetic** schema with foreign keys. Do not share production data, server addresses, credentials, tokens or private screenshots.

Follow [BUILDING.md](docs/BUILDING.md) to run core tests. Add focused regression coverage for parser, scope, ranking, formatting or insertion changes. Editor and connection changes also need the full local suite and a real SSMS smoke test; explain what you tested and any unavailable environments in the pull request.

Keep SQL completion deterministic and grounded in visible metadata. Ambiguous lineage should omit a relationship rather than invent one. Preserve user-authored aliases, avoid unnecessary formatting, and keep query editing separate from metadata reads.

Contributions are made under this repository's MIT license. Do not include third-party binaries or code without compatible redistribution rights.
