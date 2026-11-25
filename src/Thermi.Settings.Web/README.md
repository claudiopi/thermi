# Thermi.Settings.Web

The settings window's page: React with strict TypeScript, built by Vite into `dist`, which the window hosts in WebView2 (`docs/adr/0007-settings-window-in-webview2-and-react.md`). The page holds no logic of its own and loads nothing from the network; its Content Security Policy in `index.html` allows only its own files, and for styles only the empty style elements Fluent fills through the CSS object model.

- `src/Thermi.Settings.Web/src/main.tsx` mounts the page.
- `src/Thermi.Settings.Web/src/kit` is the kit: thin wrappers over Fluent UI React v9 with the design prototype's look (Windows 11's spacing, radii, Mica and layer surfaces, light, dark and forced colors, nothing moving under reduced motion). It is the only folder that imports Fluent, so replacing Fluent touches only the kit.
- `src/Thermi.Settings.Web/src/texts` takes the texts the bridge gives, by their `Enum.Member` keys.
- `src/Thermi.Settings.Web/src/overlay/Preview.tsx` shows the pictures C# paints, the overlay's live preview and each look's tile, one of their pixels on each of the screen's (`pixels.ts`), so they are never resampled; the page draws no overlay of its own.
- `src/Thermi.Settings.Web/src/app/useInputKind.ts` marks whether the keys or the pointer were used last, so a row the page focuses itself shows its ring only after a key.
- `src/Thermi.Settings.Web/tests` holds the Playwright tests, which run against the built page served by `vite preview`, in the Edge that Windows ships, so no browser is downloaded. Each test's title is one PascalCase word, like a .NET test's name, so a spec's acceptance step can name it; `RepoRulesTests` reads the titles from the `*.spec.ts` files. `focusContract.ts` is the design prototype's focus contract as one table, and axe checks the page with `pageChecks.ts`. The fake of the C# side, `fakeBridge.ts` and `fakes`, shows rough views of its own but decides no edit: `oracle.ts` answers each edit, undo and saved look from `oracle/edits.json`, the real bridge's answers, which `pwsh scripts/oracle.ps1` writes; `fakes/fakePictures.ts` draws rough pictures of the overlays and looks in SVG, only so the preview changes as the settings do. `vite preview` serves the page on port 4173, or on `THERMI_WEB_PORT` when set, so two worktrees can check at once (`playwright.config.ts`).

ESLint (`eslint.config.js`) forbids importing Fluent outside the kit, and any text in JSX, between tags or in the attributes a person or Narrator reads: every text comes from the translations.

TypeScript is 6.0, the last version with the JavaScript API that typescript-eslint parses with; TypeScript 7 has none yet.

Every version in `package.json` is exact and `package-lock.json` is committed. `.npmrc` takes packages only from the npm registry and never runs their install scripts. Node 22.12 or later:

- `npm ci --prefix src/Thermi.Settings.Web`: install what the lockfile says.
- `npm run build --prefix src/Thermi.Settings.Web`: type-check and build into `dist`.
- `npm run lint --prefix src/Thermi.Settings.Web`: the lint rules above, with no warning allowed.
- `npm run typecheck --prefix src/Thermi.Settings.Web`: type-check the page and its tests.
- `npm run test:e2e --prefix src/Thermi.Settings.Web`: run the Playwright tests against the last build.
- `npm run test:unit --prefix src/Thermi.Settings.Web`: run the Vitest unit tests, `*.test.ts` beside the logic in `src`, which the test-count ratchet counts too.
- `npm run deadcode --prefix src/Thermi.Settings.Web`: knip reports files, exports and packages nothing uses (`knip.jsonc`).
- `npm run mutation --prefix src/Thermi.Settings.Web`: StrykerJS mutates the page's pure logic, the modules `stryker.config.json` lists, and runs the unit tests against each mutant; it fails under 78 %, as Core's does. Components and hooks are left to the page's tests. The report goes to `TestResults/mutation-web`.
