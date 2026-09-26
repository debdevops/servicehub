import { render, screen } from '@testing-library/react'
import { createMemoryRouter, MemoryRouter, RouterProvider } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { RouteError } from '../components/RouteError'
import { NotFoundPage } from './NotFoundPage'

describe('an address that names no page', () => {
  it('says so in words and offers a way home, instead of a framework error', () => {
    render(<MemoryRouter><NotFoundPage /></MemoryRouter>)

    expect(screen.getByRole('heading', { level: 1, name: /doesn’t exist/ })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Go to Home' })).toHaveAttribute('href', '/')
  })
})

describe('a screen that crashes while drawing', () => {
  it('shows a plain message with Reload and Home — never a stack trace or the developer page', () => {
    const quiet = vi.spyOn(console, 'error').mockImplementation(() => {})
    const Boom = () => {
      throw new Error('secret internal detail')
    }
    render(<RouterProvider router={createMemoryRouter([{ path: '/', element: <Boom />, errorElement: <RouteError /> }])} />)

    expect(screen.getByRole('heading', { level: 1, name: /Something went wrong/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument()
    expect(screen.queryByText(/secret internal detail/)).not.toBeInTheDocument()
    quiet.mockRestore()
  })
})
