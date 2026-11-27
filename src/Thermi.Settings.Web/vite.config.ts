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
  // On GitHub, failures still mark their lines, but no report of its own joins the run's summary, which a passing run
  // leaves to the CI's dashboard alone.
  ...(process.env['GITHUB_ACTIONS'] === 'true' && {
    test: { reporters: ['default', ['github-actions', { jobSummary: { enabled: false } }]] },
  }),
});
