# 0007. The settings window is React on WebView2

**Status**: Accepted

## Context

Spec 002's window was built in WPF (ADR 0005). Its redesign, proven in an HTML prototype that five independent reviews took through four rounds, needs things WPF makes slow to build and hard to keep: a layers tree, a live chart with draggable thresholds, responsive panes that go to two columns on wide screens, Fluent motion, and full keyboard, Narrator and high-contrast support. The prototype already does all of it in HTML and CSS.

Three more reasons weigh:

- Contributors. Far more people write React than WPF, and an open-source project lives on them.
- A port. If thermi ever runs on macOS or Linux, the window should move with it and only the overlay and the sensors should need rewriting.
- Quality tooling. Web UI has mature, automatable checks for accessibility, focus and visual regressions; the prototype's own focus and axe scripts would carry over almost unchanged.

## Decision

- The window stays a process of its own (ADR 0005), and hosts one WebView2 control that fills it. The title bar stays native, so snapping, the system menu and Mica come from Windows.
- The UI is React with TypeScript and Fluent UI React v9. Fluent components are used only through a thin kit of our own, and a lint rule forbids importing Fluent anywhere else, so replacing Fluent on another platform touches only the kit.
- The UI holds no logic. It talks to the C# side through one typed interface (read settings, apply an edit, undo, redo, sensor snapshots, pick a sensor, start the fan finder). The TypeScript side of that interface is generated from the C# one, so a change that breaks either side fails the build. Config, undo, validation and the pipe stay in C# and keep their tests.
- The web app lives in a new folder, src/Thermi.Settings.Web, beside the .NET projects with its own `package.json` and README, the way Visual Studio's React and ASP.NET template pairs a client folder with its server project. The C# side that hosts it stays in `src/Thermi.Windows/Ui`, and the .NET build runs the web build and embeds its output.
- The built web files are embedded in the program and served from a virtual host. The page loads nothing from the network, and a strict Content Security Policy forbids it.
- The quality chain is the usual one for a React app: type checking, ESLint with the kit rule, unit tests, Playwright with axe for accessibility and the focus contract, a dead-code check, mutation testing on the logic that lives in the UI, and dependency updates by pull request. npm installs run with the lockfile and without install scripts.
- WebView2 is the Evergreen runtime Windows 11 already ships. The installer checks for it and installs it only if it is missing.

## What it replaces in earlier ADRs

- `docs/adr/0005-settings-window-process.md` says the window's process shows the window with WPF. Instead, it shows a WebView2 page. Everything else in that ADR stands: the separate process, the shared file, the messages to the overlay, undo.

## Rejected

- Staying on WPF: the redesign would take far longer to build and to make accessible, few contributors know it, and it cannot leave Windows.
- WinUI 3: modern controls, but the same contributor and portability limits, more memory than WPF in our measurement, and a packaging story that still changes often.
- Blazor Hybrid: keeps one language, but has a much smaller component and tooling ecosystem than React, and Fluent's Blazor components trail the React ones.
- Electron or Tauri: a second runtime to ship and update for a window Windows can already host.
- Preact, to save size: Fluent UI v9 does not support it, and the window's size is bounded by WebView2, not by React.

## Consequences

- Memory while the window is open: measured around 170 MB for an empty WebView2 page and about 240 MB for the full prototype, against 80–90 MB for WPF. It is spent only while the window is open, in its own process, and closing the window frees all of it; the overlay's memory budget (`docs/adr/0001-architecture.md`) does not change.
- The rule "only `src/Thermi.Windows/Ui` uses WPF" becomes "no WPF", and its check changes with it; the WPF views and their tests are deleted when the new window replaces them.
- The repo gains a Node toolchain, with its own lockfile and CI job, beside .NET.
- Spec 002's acceptance tests for the window move from WPF UI tests to Playwright against the page, with the C# side faked behind the typed interface; the C# side keeps its own tests.
