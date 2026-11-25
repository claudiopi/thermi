# Thermi.Settings.Web

The settings window's page, in React and Fluent UI with strict TypeScript. The window hosts it in WebView2 (`docs/adr/0007-settings-window-in-webview2-and-react.md`), so it looks and works the same on any PC, and Vite builds it into `dist`, which `thermi.exe` embeds.

The page holds no logic of its own. It asks C# what to show and hands every change back through one typed bridge, whose TypeScript the build writes from Core. It loads nothing from the network: its Content Security Policy in `index.html` allows only its own files.

| Folder | What lives there |
| --- | --- |
| `src` | `main.tsx`, which mounts the page. |
| `src/app` | The shell: its panes, the title row, dialogs, keyboard shortcuts and focus. |
| `src/bridge` | The calls to C#, and the generated types. |
| `src/overlay` | An overlay's page: its preview, items and appearance. |
| `src/item` | One item's page: sensor, thresholds, chart, graph. |
| `src/list` | The list of overlays and items, its rows and their menus. |
| `src/catalog` | The sensor catalog and its search. |
| `src/chart` | An item's chart. |
| `src/settings` | thermi's own settings and its hotkeys. |
| `src/status` | The status bars, the page for a broken settings file, and what is new. |
| `src/texts` | The translated texts the bridge gives. |
| `src/kit` | Thin wrappers over Fluent UI with Windows 11's look. The only folder that imports Fluent. |
| `src/kit/controls` | The kit's controls. |
| `src/kit/surfaces` | The kit's surfaces: cards, dialogs, menus, panels, toasts, tooltips. |
| `src/kit/theme` | The theme: light, dark and high contrast. |
| `tests` | The Playwright tests, with axe, against the built page in the Edge Windows ships. |
| `tests/fakes` | The fake C# side. It decides no edit: `tests/oracle.ts` answers from the real bridge's recorded answers. |

Entry points worth knowing:

- `src/main.tsx` mounts the page.
- `src/bridge` is all the page may ask of C#.
- `tests/oracle.ts` and `tests/oracle/edits.json`: the real bridge's answers, written by `pwsh scripts/oracle.ps1`.

ESLint forbids importing Fluent outside the kit, and any text in JSX: every text comes from the translations. Each Playwright test's title is one PascalCase word, so a spec's acceptance step can name it.

Every version in `package.json` is exact, `package-lock.json` is committed, and `.npmrc` never runs install scripts. With Node 22.12 or later, each `npm run <script> --prefix src/Thermi.Settings.Web`:

| Script | Does |
| --- | --- |
| `build` | Type-checks and builds into `dist`. |
| `lint` | ESLint, no warning allowed. |
| `typecheck` | Type-checks the page and its tests. |
| `test:unit` | Vitest, the `*.test.ts` files beside the logic. |
| `test:e2e` | Playwright, against the last build, on port 4173 or `THERMI_WEB_PORT`. |
| `deadcode` | knip: files, exports and packages nothing uses. |
| `mutation` | StrykerJS on the page's pure logic; fails under 78 %. |
