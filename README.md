# Three-Finger Tab Scroll

ChromeOS-style three-finger tab scrubbing for Windows. Put three fingers on your touchpad and slide left
or right to move through your browser tabs. Works with Chromium- and Firefox-based browsers:
Chrome, Edge, Brave, Vivaldi, Opera, Firefox, LibreWolf, Waterfox, Floorp and Zen.

- Three styles: a **tab list**, a browser-style **hover card** under the tab, or **live** switching
- Adjustable speed (how many tabs one swipe moves), with a one-swipe calibration
- Runs quietly in the system tray, starts with Windows, updates itself
- No admin rights needed

Requires Windows 10 or 11 and a Precision Touchpad (almost every laptop from the last several years).

## Install

1. Download **`ThreeFingerTabScroll-Installer.exe`** from the
   [latest release](https://github.com/LiventNick/ThreeFingerTabScroll/releases/latest), open it and
   follow the steps. You can choose whether the app **starts with Windows** (you can change it later in
   Settings). When it's done, the app starts and shows a short welcome window.
2. Turn off Windows' own three-finger swipe so the two don't fight:
   **Settings > Bluetooth & devices > Touchpad > Three-finger gestures > Swipes: Nothing**.
   The welcome window has a button that opens this page, and the app reminds you at startup until it's off.

That's it. The app lives in the system tray as a blue arrow icon (if you don't see it, click the `^`
arrow on the taskbar).

> **"Windows protected your PC"?** Windows SmartScreen can warn about new apps it hasn't seen much yet.
> Click **More info**, then **Run anyway**.

**Prefer not to install?** Download `ThreeFingerTabScroll-win-Portable.zip`, unzip it anywhere and run
`ThreeFingerTabScroll.exe`. The portable version updates itself too.

## Using it

Open a browser, put three fingers on the touchpad and slide.

| Mode | What you see |
|---|---|
| **Tab list** (default) | A list of your tabs pops up and a highlight follows your fingers. Lift to switch to the highlighted tab. Slide back to where you started to cancel. |
| **Hover card** | A card like the browser's own tab hover card appears under the tab you're heading to. Lift to switch. |
| **Live** | Tabs switch immediately as your fingers move. |

Right-click the tray icon to switch modes, turn the gesture off, or open **Settings** (double-clicking
the icon opens Settings too).

## Settings

- **Tabs per full swipe** - how many tabs one slide from one edge of the touchpad to the other moves.
- **Measure my swipe** - do one normal swipe so the app learns how far your fingers actually travel.
  Then "15 tabs per swipe" really means 15.
- **Wrap around** - when on, going past the last tab continues from the first (and the other way round).
  When off, it stops at the ends.
- **Reverse direction**, **Start with Windows**.
- **Updates** - *Ask me before installing* (default), *Install automatically*, or *Don't check
  automatically*. You can always use **Check for updates** in Settings or the tray menu.

Advanced options live in `%AppData%\ThreeFingerTabScroll\settings.json` (tray menu > **Edit settings
file...**, then **Reload settings file**):

- `BrowserProcesses` - programs the gesture works in. Add e.g. `"thorium.exe"` or `"mullvadbrowser.exe"`.
  Any app that switches tabs with Ctrl+Tab works, e.g. `"Code.exe"` (VS Code). If the tab list can't
  read an app's tabs, it shows a simple "3 tabs right" counter instead.
- `TabsPerSwipe` - decimals allowed, e.g. `12.5`.

## Updates

The app checks GitHub for new versions a little after it starts and every few hours. With the default
setting you get a notification: click it (or **Install update** in the tray menu or Settings) and the new
version downloads and installs right away, with a progress window. The app restarts on its own a few
seconds later. With *Install automatically*, this happens in the background whenever you're not in the
middle of a gesture, and a notification tells you afterwards.

## Troubleshooting

- **"No precision touchpad found"** - the app needs a Precision Touchpad. To check, open
  **Settings > Bluetooth & devices > Touchpad**: a precision touchpad has extra options like
  *Three-finger gestures*. Some older laptops only get one after updating the touchpad driver from the
  laptop maker's website. External mice and non-precision touchpads aren't supported.
- **"Windows' three-finger swipe is still on"** - Windows switches apps with the same gesture. Set
  **Settings > Bluetooth & devices > Touchpad > Three-finger gestures > Swipes** to *Nothing*.
  (You can turn the reminder off in the app's Settings.)
- **Nothing happens** - make sure Windows' three-finger swipe is set to *Nothing* and the tray icon
  isn't gray (gray = disabled). If it still doesn't work, see *Debug mode* below.
- **Works in one browser but not another** - if that browser isn't in the list above, add its program
  name to `BrowserProcesses`. If the browser runs as administrator, Windows blocks other apps from
  sending it keystrokes; run it normally.
- **Firefox hover card shows no website** - Firefox doesn't reliably report which address belongs to
  which tab, so in Firefox the card shows the title only. Memory use is a Chromium feature, so it's not
  shown for Firefox either.
- **Too fast or too slow** - change **Tabs per full swipe**, or use **Measure my swipe**.
- **Tab list shows "3 tabs right" instead of tab names** - that browser's tab strip can't be read;
  switching still works.
- **Hover card has no website shown for some tabs** - browsers only reveal the address of the tab you're
  on, so the card shows a tab's site once you've visited that tab while the app was running.
- **Browser feels slower** - the tab list and hover card read tab names through Windows' accessibility
  interface, which can make Chromium turn on some accessibility features. Live mode never does this.
- **Errors** are logged to `%AppData%\ThreeFingerTabScroll\error.log`.

**Debug mode** - exit the app from the tray, then run it from a terminal with `--debug` to get a console
that logs every gesture and tab switch (`--frames` also logs raw touchpad data). `--list-tabs` prints the
tabs the app can read in each open browser window. The installed app is at
`%LocalAppData%\ThreeFingerTabScroll\current\ThreeFingerTabScroll.exe`.

## Uninstall

**Settings > Apps > Installed apps > Three-Finger Tab Scroll > Uninstall.** Your settings stay in
`%AppData%\ThreeFingerTabScroll`; delete that folder too if you want them gone.

## How it works

- Reads your touchpad directly through Windows' Raw Input API, in the background. No drivers or hooks.
- While exactly three fingers are down, it follows their average horizontal position and turns the
  movement into tab steps. Vertical swipes are ignored.
- Switches tabs by sending the browser's own shortcuts (Ctrl+Tab, or Ctrl+Page Down in Firefox-based
  browsers).
- The tab list and hover card read tab names from the browser's tab strip through Windows' accessibility
  interface. Web page content is never read.
- Installs and updates are handled by [Velopack](https://velopack.io), using this repository's releases.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). From the repository folder:

```
dotnet run -- --debug
```

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org).

- Committers and reviewers: [LiventNick](https://github.com/LiventNick)
- Approvers: [LiventNick](https://github.com/LiventNick)

Every signed release is built from this repository by GitHub Actions; nothing is signed from a personal
computer.

### Privacy

This program does not collect or send any personal data. The only network access is checking this
project's GitHub Releases for new versions (and downloading them when you choose to update). You can
turn automatic checks off in **Settings > Updates**. Everything else, including touchpad input and tab
names, stays on your computer.

## License

[MIT](LICENSE)

---

Made by [LiventNick](https://liventnick.xyz). If this saves you some clicks, you can
[buy me a coffee](https://ko-fi.com/liventnick).
