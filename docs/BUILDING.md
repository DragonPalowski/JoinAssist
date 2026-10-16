# Building and testing

Run commands from the repository root on Windows. Source lives in `src/JoinAssist`, tests in `tests`, and packaging inputs in `packaging`.

## Core tests without SSMS

Install a .NET SDK able to build `net48` (CI uses .NET SDK 9) and the .NET Framework 4.8 runtime. The reference assemblies and ScriptDOM parser are restored as pinned NuGet dependencies; commit `packages.lock.json` when intentionally updating them.

```powershell
dotnet restore tests/JoinAssist.CoreTests/JoinAssist.CoreTests.csproj --locked-mode
dotnet build tests/JoinAssist.CoreTests/JoinAssist.CoreTests.csproj --configuration Release --no-restore
& ./tests/JoinAssist.CoreTests/bin/Release/net48/JoinAssist.CoreTests.exe
```

The suite includes 168 original core checks, focused completion-enhancement checks and updater checks using synthetic HTTP, with no database or network access. GitHub Actions runs it on pushes to `main` and pull requests. This job does not build the VSIX or validate a live SSMS session.

## Full extension and VSIX

Requirements:

- SSMS 22 x64, including its compiler/MSBuild and editor libraries.
- .NET Framework 4.8 targeting pack at the standard reference assemblies location.
- `Microsoft.VSSDK.BuildTools` **18.9.820**. Obtain it from NuGet and locate its `tools/VSSDK` directory. The build also checks the package using Microsoft's installed extension engine when available.

For example, with the NuGet CLI installed:

```powershell
nuget install Microsoft.VSSDK.BuildTools -Version 18.9.820 -OutputDirectory .packages
./build/Prepare-Release.ps1 -VssdkToolsPath './.packages/Microsoft.VSSDK.BuildTools.18.9.820/tools/VSSDK'
```

The default tools location is `%USERPROFILE%/.nuget/packages/microsoft.vssdk.buildtools/18.9.820/tools/VSSDK`. For a custom SSMS installation, also supply `-SsmsRoot 'D:/SSMS/Release'`.

The script compiles with warnings treated as errors, runs core, editor/controller and authentication contract checks, builds the package using Microsoft's VSIX tools, and verifies that Microsoft's extension engine accepts it. A regression check deliberately strips required packaging metadata and expects rejection.

The full suite includes 70 insertion checks, seven normal editor commands and 27 SSMS/SqlClient contract checks. Authentication checks use synthetic credentials/tokens, not a real server. Editor checks use a simulated editor: test a real query tab before releasing editor or connection changes. A WPF template check verifies that JOIN rows bind their actual condition and FK name as plain text.

Updater tests cover release/version validation, trusted HTTPS redirects, SHA-256 mismatch, truncated/oversized streams, cancellation and partial-file cleanup. The local WPF smoke check constructs and lays out the update window and verifies its button states without displaying it or contacting GitHub. To explicitly exercise the real public GitHub API and download/checksum path without opening an installer:

```powershell
& ./tests/JoinAssist.CoreTests/bin/Release/net48/JoinAssist.CoreTests.exe --updates-live
```

This opt-in check requires internet access and downloads the latest public VSIX into a temporary test directory, then removes it. It does not install the package. The normal CI suite remains offline after dependency restore.

Outputs are `artifacts/JoinAssist-<version>.vsix` and `artifacts/SHA256SUMS.txt`. Build output is ignored by Git. No Microsoft editor or SQL client binaries are redistributed. The full build uses the ScriptDOM installed with SSMS; core CI uses the separately pinned NuGet parser.

## Architecture

- `SqlAnalysis.cs`: tolerant SQL context, scopes and source analysis.
- `ContextSql.cs`: analysis-only recovery of an active SELECT when unrelated/incomplete syntax obscures its FROM bindings.
- `LocalTables.cs`: conservative static local-table definitions, source snapshots and invalidation.
- `NameMatching.cs`: deterministic prefix/fragment/abbreviation ranking.
- `Catalog.cs`, `Metadata.cs`: database metadata and direct column lineage.
- `Core.cs`, `CompletionEngine.cs`: SQL formatting and candidate generation/ranking.
- `AutomaticTrigger.cs`, `AutomaticCompletion.cs`: typing context and popup insertion.
- `EditorExtension.cs`, `JoinDialog.cs`: editor integration and manual picker.
- `SsmsConnection.cs`: active SSMS connection discovery and client compatibility.
- `Updates.cs`, `UpdateDialog.cs`: explicit GitHub update checks, verified downloads and the WPF update window.

SSMS connection discovery uses internal details through reflection. Keep failures explicit and test changed versions carefully; these are not a guaranteed public compatibility contract.
