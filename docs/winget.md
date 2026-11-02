# Publishing to winget

[winget](https://learn.microsoft.com/windows/package-manager/) is the package manager built into
Windows. Once the app is listed, people can install it with:

```
winget install LiventNick.ThreeFingerTabScroll
```

Installs through winget don't trigger the SmartScreen warning. winget runs the same
`ThreeFingerTabScroll-win-Setup.exe` as a normal download, so the app's own automatic updates keep
working exactly the same way.

## First submission (once, by hand)

Do this after the first release (e.g. `v1.0.0`) is published on GitHub.

1. Install Microsoft's manifest tool: `winget install Microsoft.WingetCreate`
2. Create a GitHub [personal access token (classic)](https://github.com/settings/tokens/new) with the
   `public_repo` scope. wingetcreate uses it to open a pull request to Microsoft's catalog from your account.
3. Run:

   ```
   wingetcreate new https://github.com/LiventNick/ThreeFingerTabScroll/releases/download/v1.0.0/ThreeFingerTabScroll-win-Setup.exe
   ```

   Answer the prompts with:

   | Prompt | Answer |
   |---|---|
   | PackageIdentifier | `LiventNick.ThreeFingerTabScroll` |
   | PackageVersion | `1.0.0` |
   | Publisher | `LiventNick` |
   | PackageName | `Three-Finger Tab Scroll` |
   | License | `MIT` |
   | ShortDescription | `ChromeOS-style three-finger tab switching for Windows touchpads` |
   | InstallerType | `exe` |
   | Scope | `user` |
   | Silent / SilentWithProgress switches | `--silent` |
   | PackageUrl | `https://github.com/LiventNick/ThreeFingerTabScroll` |

   When asked, let it submit the pull request using your token.
4. Microsoft's bots validate the installer and a moderator reviews the pull request. This usually takes
   a few days. Fix anything their bot comments on, and it gets merged.

## Later releases (automatic)

Add the same token as a repository **secret** named `WINGET_TOKEN`
(Settings > Secrets and variables > Actions). From then on, the release workflow sends every new version
to winget by itself. Without the secret, that step is skipped.

## How updates and winget fit together

- The app updates itself from GitHub, whether it was installed through winget or not.
- After the app updates itself, winget sees the newer version in Windows' installed-apps list, so
  `winget upgrade` won't offer an old version.
- The workflow step keeps winget's catalog on the latest version, so new installs through winget get
  the latest release.
