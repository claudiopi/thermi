import { availableParallelism } from 'node:os';
import { defineConfig } from '@playwright/test';
import { edge } from './tests/edge';

// Another run waits for this one, but something else may hold the default port: THERMI_WEB_PORT picks another.
const port = Number(process.env['THERMI_WEB_PORT'] ?? 4173);

// The tests run against the built page, as the window serves it, in the Edge that Windows ships,
// so no browser is downloaded.
export default defineConfig({
  testDir: 'tests',
  forbidOnly: true,
  reporter: 'list',
  // Each worker starts an Edge of its own, a handful of processes each, and ten of them starting at once hold Windows'
  // window manager long enough to stall the user's keyboard and windows: four at most, or half the cores on fewer.
  workers: Math.max(1, Math.min(4, Math.floor(availableParallelism() / 2))),
  use: {
    baseURL: `http://localhost:${port}`,
    channel: 'msedge',
    launchOptions: edge,
  },
  // The server waits for any other run to end before it takes the port, so Playwright waits for its line instead of the
  // port, as long as a whole other run may take.
  webServer: {
    command: `node --experimental-strip-types tests/oneRunAtATime.ts ${port}`,
    wait: { stdout: /the page is served/ },
    timeout: 60 * 60 * 1000,
    reuseExistingServer: false,
  },
});
