import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router-dom'
import { QueryClientProvider } from '@tanstack/react-query'
import { router } from './router'
import { queryClient } from './lib/queryClient'
import './styles/index.css'
import { api } from './lib/api/client'

// Forced error/loading/empty/not-allowed states for the 6.1 sweep. Development only — never in a production build.
if (import.meta.env.DEV) (await import('./dev/forceStates')).installForceStates(api)

const root = document.getElementById('root')
if (!root) {
  throw new Error('No #root element — index.html and main.tsx disagree.')
}

createRoot(root).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
)
