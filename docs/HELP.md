# Built-in user guide — 0.19.0

Open **JoinAssist → Guida…**. The guide is included in the extension, works without an SQL editor or connection, and does not contact GitHub or query database metadata.

- Search titles, instructions, examples and notes. All entered words must match; case and accents are ignored. **Ctrl+F** selects the search box.
- Pick a topic on the left for purpose, practical steps, examples and limits. Related-topic buttons navigate within the guide and clear an old search.
- **Copia esempio** explicitly copies static demonstration SQL. It never inserts it or executes it; adapt fictional names before use.
- **Apri…** opens the relevant settings page, quick action, diagnostics or updates. It does not save preferences or apply a rule. Query tools still require a supported active SQL editor. Opening Updates performs the existing explicit update check.
- **Guida / F1** inside plugin dialogs opens the relevant topic: settings follow the selected tab, quick actions follow the selected action, and rule editors distinguish SELECT rules from snippets. F1 in the SQL editor is unchanged.
- From a modal editor, reading/navigation/copy remain available but tool-opening buttons are disabled. Close that editor first to avoid interfering with unsaved drafts. Closing the source window closes its owned guide.
- The guide follows the host theme, supports resizing/scrolling and a resizable topic list. Searches are not saved.

## Coverage

The 31 topics cover first steps; optional table-to-SELECT completion; table/column completion and tolerant search; CTEs/views/APPLY/local tables; JOIN/ON/common-FK inference; keywords and LIKE/IN templates; INSERT structures; star expansion; SQL format; SELECT rules; shortcuts and linked fields; quick-action previews; related-record drafts; clipboard lists; column selection; editable models; local/UTC dates; FK paths/cardinality; dependency search; schema column search; check queries; favorites; object information/schema window; alias rename; rule probes; dependency DELETE planning; troubleshooting; cache/shortcuts; updates; local data/privacy; and extension uninstallation.

Detailed written references: [workflows](WORKFLOWS.md), [personal rules](PERSONAL-RULES.md), [typing templates](TYPING-TEMPLATES.md), [smart completion](SMART-COMPLETION.md), [DELETE planning](DELETE-PLANNING.md) and [troubleshooting](TROUBLESHOOTING.md).

## Maintenance and validation

Versioned Italian content lives in `HelpContent.cs`, compiled into the plugin. No web renderer, downloaded content or live database is needed. Keep examples and labels aligned with the actual commands when updating features.

Help tests check content/link completeness, workspace routes, full-text and accent-insensitive search, empty results, contextual settings help, explicit tool-opening callbacks, dark/light rendering and scrolling at minimum size. Existing completion/editor/update/packaging suites still run. Tests use synthetic windows without reading the clipboard, contacting a database, changing user settings or installing an update. Live SSMS integration remains a manual check.
