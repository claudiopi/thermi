import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// Relative asset paths, so the page works from whatever virtual host the window serves it on.
export default defineConfig({
  base: './',
  plugins: [react()],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
  },
});
