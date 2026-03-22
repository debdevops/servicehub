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
    open: true,
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
      reporter: ['text', 'json', 'html'],
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
        // TypeScript type definitions only — no runtime code
        'src/lib/api/types.ts',
        // single-line QueryClient instantiation
        'src/lib/queryClient.ts',
      ],
    },
  },
})
