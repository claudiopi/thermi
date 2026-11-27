import { availableParallelism, constants, setPriority } from 'node:os';
import { defineConfig, type Project } from '@playwright/test';
import { edge } from './tests/edge';

// Another run waits for this one, but something else may hold the default port: THERMI_WEB_PORT picks another.
const port = Number(process.env['THERMI_WEB_PORT'] ?? 4173);

// A check runs at idle, all it starts with it, and a browser there waits behind the .NET half's builds and test hosts
// until the page's waits run out. Each worker, and the Edge it starts, goes to below normal: ahead of the check's idle
// work, and still behind every app the user runs.
if (process.env['TEST_WORKER_INDEX'] !== undefined) {
  setPriority(constants.priority.PRIORITY_BELOW_NORMAL);
}

// The engines of the web views that host the page: on Windows, WebView2's, in the Edge that Windows ships, so no browser
// is downloaded; on Linux, WebKitGTK's, and Chromium's beside it, so the page holds on both.
const engines: Project[] =
  process.platform === 'win32'
    ? [{ name: 'edge', use: { channel: 'msedge', launchOptions: edge } }]
    : [
        { name: 'chromium', use: { browserName: 'chromium', launchOptions: edge } },
        { name: 'webkit', use: { browserName: 'webkit' } },
      ];

// The tests run against the built page, as the window serves it.
export default defineConfig({
  testDir: 'tests',
  forbidOnly: true,
  reporter: 'list',
  // Each worker starts an Edge of its own, a handful of processes each, and ten of them starting at once hold Windows'
  // window manager long enough to stall the user's keyboard and windows: four at most, or half the cores on fewer.
  workers: Math.max(1, Math.min(4, Math.floor(availableParallelism() / 2))),
  // On the loopback address itself, which "localhost" may resolve away from, to IPv6 alone, in a container.
  use: { baseURL: `http://127.0.0.1:${port}` },
  projects: engines,
  // The server waits for any other run to end before it takes the port, so Playwright waits for its line instead of the
  // port, as long as a whole other run may take.
  webServer: {
    command: `node --experimental-strip-types tests/oneRunAtATime.ts ${port}`,
    wait: { stdout: /the page is served/ },
    timeout: 60 * 60 * 1000,
    reuseExistingServer: false,
  },
});
