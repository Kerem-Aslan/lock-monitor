# Lock Monitor — Privacy Policy

_Last updated: 31 October 2026_

Lock Monitor is a Windows widget that shows how busy your PC is. **It does not collect, store,
sell or send any personal data, and it does not communicate over the network at all.**

## What the app reads, on your PC only

To draw the widget, Lock Monitor reads system information that Windows provides to any program
running as the signed-in user:

| Information | Used for |
|---|---|
| CPU, memory and GPU usage, video memory, GPU and system (ACPI) temperatures | the usage figures on the widget |
| Network bytes sent and received by your network adapters (totals only) | the download/upload speed |
| Total and free space of your fixed drives | the disk rows |
| Battery level and charging state | the battery row |
| Names and CPU time of running programs | the "Top processes" list (large size only) |
| Time since Windows started | the uptime row |

These values are only shown on the widget and kept in memory for the next update. Nothing is
written to disk, and no content of your files, network traffic, browsing or keystrokes is read.
The app reads information only while a Lock Monitor widget is visible.

## What is stored

- **Widget settings** (update interval, which drives and graphics card to show, whether to show
  the GPU temperature) are stored by Windows together with the pinned widget.
- **A small diagnostic log** of widget events (widget shown/hidden, settings saved, errors) is kept
  on your PC at
  `%LOCALAPPDATA%\Packages\<Lock Monitor package>\LocalCache\Local\LockMonitor\provider.log`.
  It contains timestamps, widget identifiers and error messages, never the values shown on the
  widget. It is limited to 512 KB and is never sent anywhere.

Uninstalling Lock Monitor removes both.

## Sharing

Lock Monitor has no networking code, no analytics, no advertising and no third-party SDKs. No
information leaves your PC.

## Contact

Questions about this policy: open an issue at the project's repository or contact the publisher
through the Microsoft Store listing.
