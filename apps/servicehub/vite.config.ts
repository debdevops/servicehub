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
const apiProxyTarget = process.env.VITE_PROXY_TARGET ?? 'http://localhost:5153'

export default defineConfig({
  plugins: [react(), tailwindcss()],

  // Served at the origin root. No prefix, no basename, no cutover (ADR-0014 D3).
  base: '/',

  define: {
    'import.meta.env.VITE_APP_VERSION': JSON.stringify(appVersion),
  },

  resolve: {
    alias: {
      '@': resolve(import.meta.dirname, './src'),
      // Test-only: the shared helpers in tests/web/support.
      '@tests': resolve(import.meta.dirname, '../../tests/web'),
    },
  },

  server: {
    port: 3000,
    proxy: {
      '/api': { target: apiProxyTarget, changeOrigin: true },
      '/health': { target: apiProxyTarget, changeOrigin: true },
    },
  },

  build: {
    // Straight into the API project. This folder is git-ignored, so a local build never
    // dirties the tree (ARCHITECTURE §9.3).
    outDir: '../../services/api/src/ServiceHub.Api/wwwroot',
    emptyOutDir: true,
    sourcemap: true,
    rollupOptions: {
      output: {
        // Split the vendors that change on their own schedule, so a product change does not
        // invalidate them. Charts joined it in Wave 2 (unit 2.10) and load only where Home draws one.
        manualChunks(id: string) {
          if (!id.includes('node_modules')) return undefined
          if (/[\\/]node_modules[\\/](react|react-dom|react-router|react-router-dom)[\\/]/.test(id)) {
            return 'vendor-react'
          }
          if (id.includes('@tanstack')) return 'vendor-query'
          if (/[\\/]node_modules[\\/](recharts|d3-[^\\/]+|victory-vendor|recharts-scale)[\\/]/.test(id)) return 'vendor-charts'
          return undefined
        },
      },
    },
  },

  test: {
    globals: true,
    environment: 'jsdom',
    // Every test lives under the repository-root tests/ folder. Browser specs (tests/e2e) run under
    // `npm run e2e`, not here.
    include: ['../../tests/web/unit/**/*.test.{ts,tsx}'],
    exclude: ['node_modules/**'],
    setupFiles: ['../../tests/web/support/setup.ts'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'lcov'],
      // Started at 50%, raised to 60% at Gate 6 (measured 2026-09-28: 85% lines · 73% branches · 75% functions). A floor to
      // stop rot — never the reason a unit is finished. The gate is.
      thresholds: { lines: 60, statements: 60, functions: 60, branches: 60 },
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['**/*.config.*', 'src/main.tsx'],
    },
  },
})
