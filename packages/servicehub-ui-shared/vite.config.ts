import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { resolve } from 'path'

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': resolve(__dirname, './src'),
    },
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
        'src/**/*.d.ts',
        // barrel re-export files have no testable logic
        'src/**/index.ts',
        // pure mock/fixture data — not application logic
        'src/lib/mockData.ts',
        'src/lib/awsMockData.ts',
        'src/lib/gcpMockData.ts',
        'src/lib/azureMockData.ts',
        // test/demo message generator — not application logic
        'src/lib/messageGenerator.ts',
        // TypeScript type definitions only — no runtime code
        'src/lib/api/types.ts',
        // single-line QueryClient instantiation
        'src/lib/queryClient.ts',
        // thin API wrapper — no testable logic beyond axios delegation
        'src/lib/api/crossCloudTrace.ts',
      ],
      thresholds: {
        lines: 60,
        functions: 60,
        branches: 50,
        statements: 60,
      },
    },
  },
})
