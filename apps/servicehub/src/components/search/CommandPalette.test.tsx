import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as namespaces from '../../lib/api/namespaces'
import { ProviderScopeContext } from '../provider/providerScope'
import { CommandPalette } from './CommandPalette'
import { expectNoAxeViolations } from '../../test/axe'

vi.mock('../../lib/api/namespaces')

const Where = () => { const l = useLocation(); return <p data-testid="at">{l.pathname + l.search}</p> }

function setup() {
  const onClose = vi.fn(); const select = vi.fn()
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/advanced']}>
        <ProviderScopeContext.Provider value={{ selected: 'azure', select }}>
          <CommandPalette open onClose={onClose} connectedCloudCount={2} />
          <Where />
        </ProviderScopeContext.Provider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { onClose, select }
}

describe('CommandPalette', () => {
  beforeEach(() => {
    vi.mocked(namespaces.fetchNamespaces).mockResolvedValue([{ id: 'w', name: 'payments', displayName: null, provider: 'aws', environment: 'Development' } as unknown as namespaces.Namespace])
    vi.mocked(namespaces.fetchEntities).mockResolvedValue({ namespaceId: 'w', entities: [{ name: 'orders-dlq', kind: 'queue', activeMessages: 0, deadLetterMessages: null, deadLetterTargetName: null }] })
  })

  it('has no accessibility violations (6.6)', async () => {
    setup()
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'orders' } })
    await screen.findByRole('option', { name: /orders-dlq/ })
    await expectNoAxeViolations(document.body)
  })

  it('says it never searches message contents', () => {
    setup()
    expect(screen.getByText(/never message contents/)).toBeInTheDocument()
  })

  it('finds a queue in another cloud; Enter switches to that cloud and opens its dead letters', async () => {
    const { onClose, select } = setup()
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'orders' } })
    expect(await screen.findByRole('option', { name: /orders-dlq/ })).toHaveAttribute('aria-selected', 'true')
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Enter' })
    expect(select).toHaveBeenCalledWith('aws')
    expect(screen.getByTestId('at').textContent).toBe('/?tab=dlq&ns=w&entity=orders-dlq')
    expect(onClose).toHaveBeenCalled()
  })

  it('an overlay place opens over the current page; Esc closes', async () => {
    const { onClose } = setup()
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'replay many' } })
    fireEvent.click(await screen.findByRole('option', { name: /Replay many at once/ }))
    expect(screen.getByTestId('at').textContent).toBe('/advanced?panel=help')
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    expect(onClose).toHaveBeenCalledTimes(2)
  })
})
