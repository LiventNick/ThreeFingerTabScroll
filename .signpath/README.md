# Code signing setup (SignPath Foundation)

SignPath Foundation signs open-source projects for free. Signed releases show a verified publisher
("SignPath Foundation") instead of "Unknown publisher", and SmartScreen warnings become much less likely.

Until this is set up, the release workflow publishes unsigned builds. Nothing breaks either way.

## One-time setup

1. **Make sure the repository is public** and has the MIT `LICENSE` (already included), and that the
   README contains the *Code signing policy* section (already included).
2. **Turn on two-factor authentication** for your GitHub account (SignPath requires it).
3. **Apply** at https://signpath.org/apply. Point them to
   https://github.com/LiventNick/ThreeFingerTabScroll and mention that releases are built by
   GitHub Actions (`.github/workflows/release.yml`). They review applications by hand, so this takes
   a while, and approval isn't guaranteed.
4. **Once approved**, in the SignPath web app:
   - Connect the GitHub repository as a *trusted build system* (SignPath's onboarding walks you through it).
   - Create a project with the slug `ThreeFingerTabScroll`.
   - Create a signing policy with the slug `release-signing`.
   - Create two artifact configurations, pasting in the files from `artifact-configurations/`:
     slug `app-files` from `app-files.xml`, and slug `installer` from `installer.xml`.
   - Create an API token for a CI user that is allowed to submit signing requests.
5. **In GitHub** (repository > Settings > Secrets and variables > Actions):
   - Add a **variable** `SIGNPATH_ORGANIZATION_ID` with your SignPath organization ID.
   - Add a **secret** `SIGNPATH_API_TOKEN` with the API token.

The next tagged release is signed automatically. If you used different slugs in SignPath, change them
in `.github/workflows/release.yml`.

## What gets signed

- `ThreeFingerTabScroll.exe` and `ThreeFingerTabScroll.dll` (the app's own code), before packaging,
  so installed copies and every update are signed.
- `ThreeFingerTabScroll-win-Setup.exe`, the installer people download.

The .NET runtime files are already signed by Microsoft and are left alone. SignPath Foundation only
signs code from the project itself.
