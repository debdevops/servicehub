import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { resolve } from 'node:path'
import fs from 'node:fs'

// The repository's release pointer. One source for the version the UI shows (rule R5 applies to
// the footer as much as to a stat tile).
const appVersion = fs.readFileSync(resolve(import.meta.dirname, '../../.version'), 'utf-8').trim()

// The API in development. The browser only ever talks to Vite, so there is no CORS to configure —
// in production they are the same origin anyway (ADR-0014 D3).
const apiProxyTarget = process.env.VITE_PROXY_TARGET ?? 'http://localhost:5000'

export default defineConfig({
  plugins: [react(), tailwindcss()],

  // Served at the origin root. No /new prefix, no basename, no cutover — ADR-0014 D3 removed the
  // two-surfaces problem by not serving the archive at all.
  base: '/',

  define: {
    'import.meta.env.VITE_APP_VERSION': JSON.stringify(appVersion),
  },

  resolve: {
    alias: { '@': resolve(import.meta.dirname, './src') },
  },

  server: {
    port: 5173,
    proxy: {
      '/api': { target: apiProxyTarget, changeOrigin: true },
      '/health': { target: apiProxyTarget, changeOrigin: true },
    },
  },

  build: {
    // Straight into the API project. This folder is git-ignored — in 4.0.0 it was tracked, so
    // every local build dirtied the tree (ARCHITECTURE §9.3).
    outDir: '../../services/api/src/ServiceHub.Api/wwwroot',
    emptyOutDir: true,
    sourcemap: true,
    rollupOptions: {
      output: {
        // Split the vendors that change on their own schedule, so a product change does not
        // invalidate them. The archive proved this split worth having; charts join it in Wave 2.
        manualChunks(id: string) {
          if (!id.includes('node_modules')) return undefined
          if (/[\\/]node_modules[\\/](react|react-dom|react-router|react-router-dom)[\\/]/.test(id)) {
            return 'vendor-react'
          }
          if (id.includes('@tanstack')) return 'vendor-query'
          return undefined
        },
      },
    },
  },

  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'lcov'],
      // Starts at 50%, rises to 60% at Gate 6. A floor to stop rot — never the reason a unit is
      // finished. The gate is.
      thresholds: { lines: 50, statements: 50, functions: 50, branches: 50 },
      exclude: ['src/test/**', '**/*.config.*', 'src/main.tsx'],
    },
  },
})
