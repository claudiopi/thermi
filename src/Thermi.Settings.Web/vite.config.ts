import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

// Relative asset paths, so the page works from whatever virtual host the window serves it on.
export default defineConfig({
  base: './',
  plugins: [react()],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
  },
  test: {
    // The share of the page's code its unit tests run, which the check asks for with --coverage and the CI's dashboard
    // shows; what C# writes into the bridge is Core's.
    coverage: {
      provider: 'v8',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/**/*.test.{ts,tsx}', 'src/bridge/*.g.ts'],
      reporter: ['json-summary'],
      reportsDirectory: '../../TestResults/coverage-web',
    },
    // On GitHub, failures still mark their lines, but no report of its own joins the run's summary, which a passing run
    // leaves to the CI's dashboard alone.
    ...(process.env['GITHUB_ACTIONS'] === 'true' && {
      reporters: ['default', ['github-actions', { jobSummary: { enabled: false } }]],
    }),
  },
});
