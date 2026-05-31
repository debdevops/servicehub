import { defineConfig, Plugin } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { resolve } from 'path'

/**
 * Vite plugin that injects a SPA token <meta> tag into index.html during
 * development. Fetches a fresh HMAC token from the API's /internal/spa-token
 * endpoint on every page load, mirroring what SpaTokenInjectionMiddleware
 * does in production. This ensures local dev also requires auth — Postman
 * callers who copy URLs from DevTools will get 401.
 */
function spaTokenDevPlugin(): Plugin {
  return {
    name: 'spa-token-dev',
    transformIndexHtml: {
      order: 'post',
      async handler(html) {
        try {
          const res = await fetch('http://localhost:5153/internal/spa-token');
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
// shell environment variable.  This is used in CI (e2e-simulator job) where
// the .NET API listens on a different port (5200) instead of the default 5153.
// The browser always talks to Vite on port 3000 so there are no CORS issues.
const apiProxyTarget = process.env.VITE_PROXY_TARGET ?? 'http://localhost:5153';

export default defineConfig({
  plugins: [react(), tailwindcss(), spaTokenDevPlugin()],
  resolve: {
    alias: {
      '@': resolve(__dirname, './src'),
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
      '/health': {
        target: apiProxyTarget,
        changeOrigin: true,
        secure: false,
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
          // Vendor chunk for heavy UI libraries
          if (id.includes('node_modules/recharts') || 
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
    alias: { '@': resolve(__dirname, './src') },
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
        // pure mock/fixture data — not application logic
        'src/lib/mockData.ts',
        'src/lib/aiMockData.ts',
        'src/lib/insightsMockData.ts',
        // test/demo message generator — not application logic
        'src/lib/messageGenerator.ts',
        // TypeScript type definitions only — no runtime code
        'src/lib/api/types.ts',
        // single-line QueryClient instantiation
        'src/lib/queryClient.ts',
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
