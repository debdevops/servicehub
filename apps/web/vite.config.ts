import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { resolve } from 'path'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
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
        target: 'http://localhost:5153',
        changeOrigin: true,
        secure: false,
      },
      // Also proxy health checks so they work through the same port.
      '/health': {
        target: 'http://localhost:5153',
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
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    alias: { '@': resolve(__dirname, './src') },
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
    },
  },
})
