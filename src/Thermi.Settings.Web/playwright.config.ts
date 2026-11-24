import { defineConfig } from '@playwright/test';

// Another worktree's check may hold the default port at the same time: THERMI_WEB_PORT picks another.
const port = Number(process.env['THERMI_WEB_PORT'] ?? 4173);

// The tests run against the built page, as the window serves it, in the Edge that Windows ships,
// so no browser is downloaded.
export default defineConfig({
  testDir: 'tests',
  forbidOnly: true,
  reporter: 'list',
  use: {
    baseURL: `http://localhost:${port}`,
    channel: 'msedge',
  },
  webServer: {
    command: `npx vite preview --port ${port} --strictPort`,
    url: `http://localhost:${port}`,
    reuseExistingServer: false,
  },
});
