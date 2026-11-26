import { availableParallelism } from 'node:os';
import { defineConfig } from '@playwright/test';
import { edge } from './tests/edge';

// Another worktree's check may hold the default port at the same time: THERMI_WEB_PORT picks another.
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
  globalSetup: './tests/oneRunAtATime.ts',
  use: {
    baseURL: `http://localhost:${port}`,
    channel: 'msedge',
    launchOptions: edge,
  },
  webServer: {
    command: `npx vite preview --port ${port} --strictPort`,
    url: `http://localhost:${port}`,
    reuseExistingServer: false,
  },
});
