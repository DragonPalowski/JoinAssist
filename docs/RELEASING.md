# Releasing JoinAssist

Releases are currently prepared locally and published manually. The in-SSMS updater is planned, not implemented.

1. Update the numeric version in `packaging/source.extension.vsixmanifest`; this is the build's version source. Preserve the extension identity so installers can recognize upgrades.
2. Add changelog and release notes, then run the core tests and full build described in [BUILDING.md](BUILDING.md). Run `build/Prepare-Release.ps1` to generate the VSIX and SHA256SUMS.txt.
3. Test installation and basic completion in a real SSMS query tab. Check the supported authentication methods where a test environment is available. State any unverified combinations in the release notes.
4. Commit the final source and ensure the GitHub core test workflow passes for that commit.
5. Create a GitHub release with tag `v<version>` targeting that commit. Attach `JoinAssist-<version>.vsix` and `SHA256SUMS.txt`. Paste the corresponding `docs/releases/v<version>.md` notes. Publish only these intended assets; build folders, credentials, connection information and private screenshots do not belong in a release.
6. Verify the published download and checksum. Never replace an existing release's binary silently; publish a new version for corrections.

Current 0.x releases are experimental even when published as ordinary GitHub releases. Use GitHub's prerelease flag for preview builds that should not be offered by the `releases/latest` link.

Future update support should identify this repository explicitly, compare numeric versions, show release information, verify the selected asset and require an SSMS restart for installation. No updater is implied by this document.
