import { defineConfig } from '@playwright/test';

const port = 4173;

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
    command: 'npm run preview',
    url: `http://localhost:${port}`,
    reuseExistingServer: false,
  },
});
