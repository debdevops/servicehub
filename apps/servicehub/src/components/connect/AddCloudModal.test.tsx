import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as namespacesApi from '../../lib/api/namespaces'
import type { Namespace, NamespaceStats } from '../../lib/api/namespaces'
import { AppLayout } from '../../layouts/AppLayout'
import { HomePage } from '../../pages/HomePage'

vi.mock('../../lib/api/namespaces')
const api = vi.mocked(namespacesApi)

const azureCs = 'Endpoint=sb://orders-dev.servicebus.windows.net/;SharedAccessKeyName=servicehub;SharedAccessKey=abc'

const azureNamespace = (over: Partial<Namespace> = {}): Namespace =>
  ({
    id: 'n1', name: 'orders-dev', displayName: 'Orders', provider: 'azure', lastConnectionTestSucceeded: true,
    capabilities: { canProveDlqAbsence: true } as Namespace['capabilities'], ...over,
  }) as Namespace

const stats: NamespaceStats = {
  namespaceId: 'n1', entities: [{ kind: 'queue', count: 7 }, { kind: 'topic', count: 4 }], activeMessages: 3,
  deadLetterMessages: 96, messageCountsSupported: true, observedAt: 'now',
}

function Where() {
  const { pathname, search } = useLocation()
  return <output data-testid="where">{pathname + search}</output>
}

function renderApp(initial = '/') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[initial]}>
        <Where />
        <Routes>
          <Route element={<AppLayout />}>
            <Route index element={<HomePage />} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const where = () => screen.getByTestId('where').textContent

async function fillAzure(dialog: HTMLElement) {
  await userEvent.type(await within(dialog).findByLabelText('Name'), 'Orders')
  await userEvent.type(within(dialog).getByLabelText('Connection string'), azureCs)
  await userEvent.click(within(dialog).getByRole('button', { name: 'Connect' }))
}

describe('Add a cloud — welcome → modal → Home', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    window.localStorage.clear()
    api.fetchNamespaces.mockResolvedValue([])
  })

  it('shows the welcome with three cloud cards while nothing is connected', async () => {
    renderApp()
    expect(await screen.findByRole('heading', { name: 'Welcome' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Connect Azure' })).toHaveAttribute('href', '/?modal=add-cloud&cloud=azure')
    expect(screen.getByRole('link', { name: 'Connect AWS' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Connect Google' })).toBeInTheDocument()
  })

  it('takes someone with only a connection string from the welcome to Home', async () => {
    api.connectNamespace.mockResolvedValue(azureNamespace())
    api.testConnection.mockResolvedValue({ isConnected: true, message: 'Connection successful.', testedAt: 'now' })
    api.fetchNamespaceStats.mockResolvedValue(stats)
    renderApp()

    await userEvent.click(await screen.findByRole('link', { name: 'Connect Azure' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add a cloud' })
    api.fetchNamespaces.mockResolvedValue([azureNamespace()])
    await fillAzure(dialog)

    expect(await within(dialog).findByText(/Connected — ServiceHub can see Orders/)).toBeInTheDocument()
    expect(within(dialog).getByText('Found 7 queues and 4 topics. 96 messages are dead-lettered right now.')).toBeInTheDocument()
    expect(within(dialog).getByText(/can say Verified here/)).toBeInTheDocument()
    expect(api.connectNamespace).toHaveBeenCalledWith(
      expect.objectContaining({ provider: 'azure', authType: 'connectionString', name: 'orders-dev', connectionString: azureCs, environment: 'dev' }),
    )

    await userEvent.click(within(dialog).getByRole('button', { name: 'Open Home' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(where()).toBe('/')
    expect(screen.queryByRole('heading', { name: 'Welcome' })).not.toBeInTheDocument()
  })

  it('opens on the cloud the card named', async () => {
    renderApp('/?modal=add-cloud&cloud=aws')
    const dialog = await screen.findByRole('dialog', { name: 'Add a cloud' })
    expect(await within(dialog).findByLabelText('Access key ID')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'AWS SQS / SNS' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('asks for nothing the cloud does not need', async () => {
    renderApp('/?modal=add-cloud&cloud=azure')
    const dialog = await screen.findByRole('dialog', { name: 'Add a cloud' })
    await within(dialog).findByLabelText('Connection string')
    expect(within(dialog).queryByLabelText('Region')).not.toBeInTheDocument()
    expect(within(dialog).queryByLabelText('Project ID')).not.toBeInTheDocument()

    await userEvent.click(within(dialog).getByRole('button', { name: 'Google Pub/Sub' }))
    expect(within(dialog).getByLabelText('Project ID')).toBeInTheDocument()
    expect(within(dialog).queryByLabelText('Connection string')).not.toBeInTheDocument()
  })

  it('says what is missing in a sentence and does not call the API', async () => {
    renderApp('/?modal=add-cloud')
    const dialog = await screen.findByRole('dialog', { name: 'Add a cloud' })
    await userEvent.click(await within(dialog).findByRole('button', { name: 'Connect' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Give this cloud a name')
    expect(api.connectNamespace).not.toHaveBeenCalled()
  })

  it('shows a plain sentence, never a raw error, when connecting fails — and keeps what was typed', async () => {
    api.connectNamespace.mockRejectedValue(new Error('AxiosError: Request failed with status code 409'))
    renderApp('/?modal=add-cloud')
    const dialog = await screen.findByRole('dialog', { name: 'Add a cloud' })
    await fillAzure(dialog)

    const alert = await within(dialog).findByRole('alert')
    expect(alert).not.toHaveTextContent(/409|AxiosError/)
    expect(within(dialog).getByLabelText('Name')).toHaveValue('Orders')
  })

  it('when the test fails, says so, and offers to remove it and try again', async () => {
    api.connectNamespace.mockResolvedValue(azureNamespace())
    api.testConnection.mockResolvedValue({ isConnected: false, message: 'Connection test failed: unauthorized', testedAt: 'now' })
    api.removeNamespace.mockResolvedValue()
    renderApp('/?modal=add-cloud')
    const dialog = await screen.findByRole('dialog', { name: 'Add a cloud' })
    await fillAzure(dialog)

    expect(await within(dialog).findByText('Connection test failed: unauthorized')).toBeInTheDocument()
    expect(within(dialog).queryByText(/Connected — ServiceHub can see/)).not.toBeInTheDocument()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Remove it and try again' }))

    expect(api.removeNamespace).toHaveBeenCalledWith('n1')
    expect(await within(dialog).findByLabelText('Connection string')).toBeInTheDocument()
  })

  it('closes with Esc and drops ?modal from the URL', async () => {
    renderApp('/?modal=add-cloud&cloud=aws')
    await screen.findByRole('dialog', { name: 'Add a cloud' })
    await userEvent.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(where()).toBe('/')
  })
})
