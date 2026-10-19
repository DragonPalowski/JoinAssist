# Releasing JoinAssist

Releases are prepared locally and published manually. From 0.5.0, the in-SSMS updater reads this repository's latest ordinary release and offers a verified VSIX download.

1. Update the numeric version in `packaging/source.extension.vsixmanifest`; this is the build's version source. Preserve the extension identity so installers can recognize upgrades.
2. Add changelog and release notes, then run the core tests and full build described in [BUILDING.md](BUILDING.md). Run `build/Prepare-Release.ps1` to generate the VSIX and SHA256SUMS.txt.
3. Test installation and basic completion in a real SSMS query tab. Check the supported authentication methods where a test environment is available. State any unverified combinations in the release notes.
4. Commit the final source and ensure the GitHub core test workflow passes for that commit.
5. Create a GitHub release with tag `v<version>` targeting that commit. Attach `JoinAssist-<version>.vsix` and `SHA256SUMS.txt`. Paste the corresponding `docs/releases/v<version>.md` notes. Publish only these intended assets; build folders, credentials, connection information and private screenshots do not belong in a release.
6. Verify the published download and checksum. Never replace an existing release's binary silently; publish a new version for corrections.

Current 0.x releases are experimental even when published as ordinary GitHub releases. Use GitHub's prerelease flag for preview builds that should not be offered by the `releases/latest` link.

For updater compatibility, use a numeric three- or four-part version with optional `v` tag prefix and exactly one asset named `JoinAssist-<numeric-version>.vsix`. Wait until the asset is fully uploaded and GitHub exposes its `sha256:` digest. Missing/invalid digests block in-app downloads. The 0.5.0 updater limits packages to 25 MiB, metadata to 2 MiB and redirects to HTTPS GitHub asset hosts. It compares the version embedded in the installed assembly (generated from the manifest), excludes drafts/prereleases and never proposes downgrades.

Installation remains opt-in: users can manually open the VSIX after saving queries and closing SSMS, or, from 0.10.0, explicitly request that the bundled helper open the normal Microsoft installer after SSMS closes. Installer confirmations remain user actions. Publishing a release does not push or run an installer on users' computers. Keep the checksum file for manual verification as well.
