import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { resolve } from 'node:path'
import fs from 'node:fs'
import type { Plugin } from 'vite'

// The repository's release pointer. One source for the version the UI shows (rule R5 applies to
// the footer as much as to a stat tile).
const appVersion = fs.readFileSync(resolve(import.meta.dirname, '../../.version'), 'utf-8').trim()

// The API in development. The browser only ever talks to Vite, so there is no CORS to configure —
// in production they are the same origin anyway (ADR-0014 D3).
const apiProxyTarget = process.env.VITE_PROXY_TARGET ?? 'http://localhost:5153'

// The public demo page (`VITE_DEMO_ONLY=true`) is a client-rendered app, so a crawler or link-preview bot that does not run
// JavaScript sees an empty page. For that build only, give <head> a real title, description, Open Graph / Twitter tags and a
// canonical URL, and put a short plain-text description inside <noscript>. The real app's index.html is not touched.
const DEMO_URL = process.env.VITE_DEMO_URL ?? 'https://debdevops.github.io/servicehub/'
const DEMO_TITLE = 'ServiceHub demo — dead-letter recovery for Azure, AWS and GCP'
const DEMO_DESCRIPTION =
  'Try ServiceHub in your browser, no install and no cloud account. Browse made-up dead-letter queues from Azure Service Bus, AWS SQS/SNS and ' +
  'Google Pub/Sub, see why each message failed, preview a replay, and watch whether the fix held. Nothing leaves your browser.'
const demoPageMeta = (): Plugin => ({
  name: 'servicehub-demo-page-meta',
  transformIndexHtml: {
    order: 'pre',
    handler(html) {
      if (process.env.VITE_DEMO_ONLY !== 'true') return html
      const image = `${DEMO_URL}og-image.png`
      const head = [
        `<link rel="canonical" href="${DEMO_URL}" />`,
        `<meta property="og:type" content="website" />`,
        `<meta property="og:site_name" content="ServiceHub" />`,
        `<meta property="og:title" content="${DEMO_TITLE}" />`,
        `<meta property="og:description" content="${DEMO_DESCRIPTION}" />`,
        `<meta property="og:url" content="${DEMO_URL}" />`,
        `<meta property="og:image" content="${image}" />`,
        `<meta name="twitter:card" content="summary_large_image" />`,
        `<meta name="twitter:title" content="${DEMO_TITLE}" />`,
        `<meta name="twitter:description" content="${DEMO_DESCRIPTION}" />`,
        `<meta name="twitter:image" content="${image}" />`,
      ].join('\n    ')
      const noscript =
        '<noscript><h1>ServiceHub demo</h1><p>' + DEMO_DESCRIPTION + '</p>' +
        '<p>This page needs JavaScript. Source, docs and the release: <a href="https://github.com/debdevops/servicehub">github.com/debdevops/servicehub</a>.</p></noscript>'
      return html
        .replace(/<meta name="description"[^>]*>/, `<meta name="description" content="${DEMO_DESCRIPTION}" />`)
        .replace('<title>ServiceHub</title>', `<title>${DEMO_TITLE}</title>\n    ${head}`)
        .replace('<div id="root"></div>', `<div id="root"></div>\n    ${noscript}`)
    },
  },
})

export default defineConfig({
  plugins: [react(), tailwindcss(), demoPageMeta()],

  // Served at the origin root. No prefix, no basename, no cutover (ADR-0014 D3).
  // The one exception is the public demo page (`npm run build:demo`), which GitHub Pages serves under a path.
  base: process.env.VITE_BASE ?? '/',

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
