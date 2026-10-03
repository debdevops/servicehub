import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { resolve } from 'path'
import fs from 'fs'
import path from 'path'

const appVersion = fs.readFileSync(path.resolve(__dirname, '../../.version'), 'utf-8').trim();

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  define: {
    'import.meta.env.VITE_APP_VERSION': JSON.stringify(appVersion),
  },
  resolve: {
    alias: {
      '@': resolve(__dirname, './src'),
      '@servicehub/ui-shared': resolve(__dirname, '../../packages/servicehub-ui-shared/src'),
    },
  },
  server: {
    port: 3001,
    host: '0.0.0.0',
    open: false,
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    alias: {
      '@': resolve(__dirname, './src'),
      '@servicehub/ui-shared': resolve(__dirname, '../../packages/servicehub-ui-shared/src'),
    },
    exclude: ['e2e/**', 'node_modules/**'],
  },
})
