# Releasing updates / نشر التحديثات

Installed copies of PV Designer check `https://api.github.com/repos/<owner>/<repo>/releases/latest` at startup
(configurable in *Settings ▸ Updates*). When a newer version exists the user can update in one click:

1. the app downloads `PVDesigner-Windows-x64.zip` and `PVDesigner-Windows-x64.zip.sha256` from the release,
2. verifies the SHA-256 checksum (the update is refused if the checksum is missing or different),
3. extracts it and starts an installer script; the app only closes once the script is confirmed running,
4. the script backs up the installation, copies the new files (asking for administrator rights only if the
   folder is protected), restores the backup if anything fails, and restarts the app as the normal user.

On Meta Quest (APK) and other platforms the app opens the release page instead.

## 1. One-time setup

### Repository visibility

Clients query GitHub without credentials, so releases must be readable publicly:

* make this repository public, **or**
* keep the source private and publish releases to a separate public repository: change
  `UpdateConfig.Owner` / `UpdateConfig.Repository` in `Assets/PVDesigner/Scripts/Runtime/Updates/AutoUpdater.cs`
  and adapt the `release` job (e.g. `softprops/action-gh-release` with `repository:` and a token).

### Unity license secrets

GitHub ▸ *Settings ▸ Secrets and variables ▸ Actions ▸ New repository secret*:

| Secret | Value |
|---|---|
| `UNITY_EMAIL` | Unity account e-mail |
| `UNITY_PASSWORD` | Unity account password |
| `UNITY_LICENSE` | Unity Personal: full content of the `.ulf` license file (see GameCI "Activation" guide) |
| `UNITY_SERIAL` | Unity Pro / Plus only: serial key (then `UNITY_LICENSE` is not needed) |

Guide: <https://game.ci/docs/github/activation>

Optional for the Quest store: configure an Android keystore (`androidKeystoreName`, `androidKeystoreBase64`, ...
inputs of `game-ci/unity-builder`). Without it the APK is signed with a debug key, which is fine for sideloading.

## 2. Publishing a version

1. Update the code, make sure `dotnet test Tools/CoreTests` passes.
2. Tag and push:

   ```bash
   git tag v1.1.0
   git push origin v1.1.0
   ```

3. The **Build and release** workflow runs the tests, builds Windows (Mono) and Meta Quest (Android IL2CPP),
   then creates the GitHub release with automatically generated notes. Edit the release text afterwards if needed:
   it is shown to users in the update dialog.

Tags containing a dash (e.g. `v1.2.0-beta.1`) are published as *pre-releases*; only users who enabled
*Include beta versions* receive them.

The version shown in the app (`Application.version`) comes from the tag, so always increase it (semantic versioning).

## 3. Installing for a new client

Send the client the latest `PVDesigner-Windows-x64.zip`. Recommended installation folder:
`%LOCALAPPDATA%\PV Designer` (user-writable, so updates do not need administrator rights).
Run `PVDesigner.exe`. Projects and reports are stored in `Documents\PV Designer`.

For Meta Quest: enable developer mode and install `PVDesigner-Quest.apk` with Meta Quest Developer Hub or `adb install`.
For PC VR (Quest Link / Air Link, SteamVR, WMR) use the Windows build and click **Enter VR**.

## Manual test build

*Actions ▸ Build and release ▸ Run workflow* builds both platforms and keeps the files as workflow
artifacts (no release is created).
