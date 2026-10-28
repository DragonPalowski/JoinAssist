# Removing JoinAssist — 0.19.0

Open **JoinAssist → Disinstalla JoinAssist…**, save your queries and check the destination/scope shown. Click **Avvia disinstallazione** to open Microsoft's normal VSIX removal UI. Complete its prompts and close SSMS when requested. JoinAssist does not close SSMS, confirm the installer, delete extension directories or run a silent uninstall.

The command resolves the **currently running SSMS 22 instance** using the locally installed Microsoft `vswhere.exe`. It launches that instance's `Common7/IDE/VSIXInstaller.exe` with the fixed JoinAssist extension identity and an explicit `/instanceIds:` target. It never calls the SSMS product uninstaller or a generic all-instance removal. Missing, ambiguous, incomplete, unsupported or experimental RootSuffix installations are refused.

For an extension loaded from the current installation's common `IDE/Extensions` directory, the dialog reports the machine-wide scope and uses the installer's `/admin` mode; Windows may request administrative approval. An ordinary per-user copy does not request that mode. The target and scope are rediscovered for the explicit click, and a changed target requires another inspection. The installer file must still match its inspected SHA-256 hash.

An active deferred JoinAssist update blocks removal: cancel it under **Aggiornamenti…**, then choose **Riprova**. The dialog checks again before launch. Keep other SSMS instances from scheduling a new update during removal. The Microsoft installer controls conflicts with other installer operations and reports the final outcome; the plugin reports only that it launched the removal procedure.

Rules, settings, favorites and choice memory in `%LOCALAPPDATA%/JoinAssist` are retained. No personal-data cleanup is performed. Copies installed by manually copying DLLs outside the VSIX registration system are not silently deleted; the installer may require separate manual cleanup of such legacy copies.

## Validation and references

Tests use synthetic setup inventory, paths and injected launch delegates. They check exact-instance matching, fixed extension identity, user/machine scope, malformed/injected arguments, pending-update gates, changed targets, duplicate clicks, close-during-discovery and themes. No test launches a real uninstaller or removes the installed plugin. End-to-end uninstall in a disposable SSMS installation remains a manual check.

Microsoft documents [instance discovery with vswhere](https://github.com/microsoft/vswhere) and provides a [VSIX install/uninstall example using explicit instance IDs](https://github.com/microsoft/WindowsAppSDK/blob/main/dev/Templates/VSIX/build-local-VSIX-package/Install-LocalDev-VSIX.ps1). JoinAssist uses the interactive removal UI and preserves its prompts.
