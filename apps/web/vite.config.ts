import { defineConfig, Plugin } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { resolve } from 'path'
import fs from 'fs'
import path from 'path'

const appVersion = fs.readFileSync(path.resolve(__dirname, '../../.version'), 'utf-8').trim();

/**
 * Vite plugin that injects a SPA token <meta> tag into index.html during
 * development. Fetches a fresh HMAC token from the API's /internal/spa-token
 * endpoint on every page load, mirroring what SpaTokenInjectionMiddleware
 * does in production. The token is a CSRF/automation mitigation, not
 * authentication — anyone who fetches the page (curl included) can read
 * and replay it. It does still stop the common case of copying a bare
 * endpoint URL out of DevTools without also copying the header.
 */
function spaTokenDevPlugin(): Plugin {
  return {
    name: 'spa-token-dev',
    apply: 'serve',
    transformIndexHtml: {
      order: 'post',
      async handler(html) {
        try {
          const target = process.env.VITE_PROXY_TARGET ?? 'http://localhost:5153';
          const res = await fetch(`${target}/internal/spa-token`);
          if (res.ok) {
            const token = await res.text();
            return html.replace('</head>', `  <meta name="spa-token" content="${token}">\n  </head>`);
          }
        } catch {
          // API not ready yet — token will be missing; refresh after API starts
        }
        return html;
      },
    },
  };
}

// https://vite.dev/config/
// Allow the proxy target to be overridden at dev-server startup time via a
// shell environment variable, for cases where the .NET API listens on a
// different port than the default 5153. The browser always talks to Vite
// on port 3000 so there are no CORS issues.
const apiProxyTarget = process.env.VITE_PROXY_TARGET ?? 'http://localhost:5153';

export default defineConfig({
  plugins: [react(), tailwindcss(), spaTokenDevPlugin()],
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
    port: 3000,
    // Bind to all network interfaces so the UI is reachable from remote machines.
    // When running locally, http://localhost:3000 still works.
    // When running on a server, http://serverip:3000 also works.
    host: '0.0.0.0',
    // Do not auto-open browser — this fails on headless/remote servers.
    open: false,
    proxy: {
      // Forward all /api requests to the .NET backend running on the same machine.
      // This means the browser only needs one port (3000) and CORS is not required
      // for the browser-to-API communication — requests appear same-origin.
      '/api': {
        target: apiProxyTarget,
        changeOrigin: true,
        secure: false,
      },
      // Also proxy health checks so they work through the same port.
      // /health is BOTH a backend endpoint and an SPA route: XHR/fetch calls
      // (Accept: application/json) go to the backend, while browser page
      // navigations (Accept: text/html — deep links, refreshes) get the SPA
      // shell so the System Health page renders instead of raw JSON.
      '/health': {
        target: apiProxyTarget,
        changeOrigin: true,
        secure: false,
        bypass: (req) => {
          if (req.headers.accept?.includes('text/html')) return '/index.html';
        },
      },
    },
  },
  build: {
    // Output React build directly into the API's wwwroot folder.
    // This means one dotnet publish produces both the API and the SPA.
    outDir: '../../services/api/src/ServiceHub.Api/wwwroot',
    emptyOutDir: true,
    rollupOptions: {
      output: {
        // Code splitting strategy: extract heavy dependencies and pages into separate chunks
        // This reduces initial bundle size and improves cold-start performance on Azure App Service
        manualChunks: (id: string) => {
          // Vendor chunk for heavy UI libraries. recharts pulls in a sizeable transitive
          // dependency graph (victory-vendor, redux, immer, the d3-* family) that Rollup
          // would otherwise resolve into whichever lazy page chunk imports recharts first
          // (observed: page-dashboard ballooning to 557kB while FleetPage's own recharts
          // usage stayed at 13kB) — group them here so the weight is shared and cached once.
          if (id.includes('node_modules/recharts') ||
              id.includes('node_modules/victory-vendor') ||
              id.includes('node_modules/d3-') ||
              id.includes('node_modules/@reduxjs') ||
              id.includes('node_modules/react-redux') ||
              id.includes('node_modules/redux') ||
              id.includes('node_modules/immer') ||
              id.includes('node_modules/es-toolkit') ||
              id.includes('node_modules/@tanstack/react-table') ||
              id.includes('node_modules/@tanstack/react-virtual')) {
            return 'vendor-ui';
          }
          // Routing and HTTP
          if (id.includes('node_modules/react-router-dom') || 
              id.includes('node_modules/axios')) {
            return 'vendor-http';
          }
          // Heavy pages (lazy loaded)
          if (id.includes('src/pages/DashboardPage.tsx')) return 'page-dashboard';
          if (id.includes('src/pages/CorrelationExplorerPage.tsx')) return 'page-correlation';
          if (id.includes('src/pages/DlqHistoryPage.tsx')) return 'page-dlq-history';
        },
      },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    alias: {
      '@': resolve(__dirname, './src'),
      '@servicehub/ui-shared': resolve(__dirname, '../../packages/servicehub-ui-shared/src'),
    },
    // Exclude Playwright E2E specs — they are run by `npm run test:e2e`, not Vitest
    exclude: ['e2e/**', 'node_modules/**'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json', 'json-summary', 'html'],
      include: ['src/**/*.{ts,tsx}'],
      exclude: [
        'src/test/**',
        'src/main.tsx',
        'src/router.tsx',
        'src/vite-env.d.ts',
        'src/**/*.d.ts',
        'src/styles/**',
        'src/assets/**',
        // barrel re-export files have no testable logic
        'src/**/index.ts',
      ],
      // ── Code Coverage Thresholds ────────────────────────────────────────
      // BUILD WILL FAIL if coverage falls below these minimums
      // This ensures code quality and prevents coverage regression
      thresholds: {
        lines: 60,       // Minimum 60% line coverage
        functions: 60,   // Minimum 60% function coverage
        branches: 50,    // Minimum 50% branch coverage
        statements: 60,  // Minimum 60% statement coverage
      },
    },
  },
})
