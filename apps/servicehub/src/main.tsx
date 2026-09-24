import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router-dom'
import { QueryClientProvider } from '@tanstack/react-query'
import { router } from './router'
import { queryClient } from './lib/queryClient'
import './styles/index.css'

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
