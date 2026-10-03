import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agents from '@/lib/api/agents'
import * as identity from '@/lib/api/identity'
import * as namespacesApi from '@/lib/api/namespaces'
import type { CloudProvider, Namespace } from '@/lib/api/namespaces'
import * as settings from '@/lib/api/settings'
import { enterDemo, leaveDemo } from '@/lib/demo/state'
import { AppLayout } from '@/layouts/AppLayout'

vi.mock('@/lib/api/namespaces')
vi.mock('@/lib/api/settings')
vi.mock('@/lib/api/agents')
vi.mock('@/lib/api/identity')

const ns = (provider: CloudProvider): Namespace =>
  ({ id: `${provider}-1`, name: `${provider}-1`, provider, lastConnectionTestSucceeded: null }) as Namespace

function renderApp() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route element={<AppLayout />}>
            <Route index element={<div />} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

/**
 * Unit 6.10: one shared banner slot, above everything, full width — and when more than one
 * condition is true at once, the order is fixed: emergency › paused › demo. Proven here by putting
 * emergency stop and demo mode on together and reading the DOM order, not just presence.
 */
describe('AppLayout — the banner slot orders emergency before demo when both are true', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    window.localStorage.clear()
    leaveDemo()
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([ns('azure')])
    vi.mocked(agents.fetchAgents).mockResolvedValue([])
    vi.mocked(identity.fetchMe).mockResolvedValue({
      ownerId: 'o', authMethod: 'session', actor: { identity: 's', kind: 'user', label: 'l', isSession: true }, effectiveRole: 'Admin',
    })
  })

  it('shows the emergency banner before the demo banner in the DOM', async () => {
    vi.mocked(settings.fetchEmergencyStop).mockResolvedValue({ active: true, by: 'Dana', at: '2026-09-27T10:00:00Z', reason: 'incident' })
    enterDemo()

    renderApp()

    const emergency = await screen.findByRole('alert')
    const demo = screen.getByText(/Everything here is made up/)
    expect(emergency.compareDocumentPosition(demo) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()

    leaveDemo()
  })

  it('shows only the demo banner when emergency stop is off', async () => {
    vi.mocked(settings.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    enterDemo()

    renderApp()

    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
    expect(screen.getByText(/Everything here is made up/)).toBeInTheDocument()

    leaveDemo()
  })
})
