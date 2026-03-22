import { defineConfig } from 'vite'
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
})
