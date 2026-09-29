import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import type { DeadLetter } from '../../lib/api/deadLetters'
import { MessageTable } from './MessageTable'

const row = (over: Partial<DeadLetter> = {}): DeadLetter => ({
  id: 1, namespaceId: 'n', messageId: 'm-1', sequenceNumber: 1, entityName: 'orders', entityType: 'queue', topicName: null,
  detectedAtUtc: '2026-09-25T10:00:00Z', enqueuedTimeUtc: '2026-09-25T09:00:00Z', deliveryCount: 10, sizeInBytes: 2048,
  deadLetterReason: 'MaxDeliveryCountExceeded', deadLetterErrorDescription: 'Message could not be consumed after 10 delivery attempts.', status: 'active', ...over,
})

const renderTable = (rows: DeadLetter[]) =>
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><MemoryRouter><MessageTable rows={rows} /></MemoryRouter></QueryClientProvider>)

describe('the dead-letter table', () => {
  it('shows a topic’s subscription as topic › subscription, tagged, and a queue as a queue', () => {
    renderTable([row(), row({ id: 2, messageId: 'm-2', entityName: 'orders-topic/subscriptions/billing', entityType: 'subscription', topicName: 'orders-topic' })])

    const rows = screen.getAllByRole('row').slice(1)
    expect(within(rows[0]).getByText('Queue')).toBeInTheDocument()
    expect(within(rows[1]).getByText('orders-topic')).toBeInTheDocument()
    expect(within(rows[1]).getByText('billing')).toBeInTheDocument()
    expect(within(rows[1]).getByText('Topic subscription')).toBeInTheDocument()
  })

  it('says what failed in three layers: the recorded reason, the error text, and a marked plain-English reading', () => {
    renderTable([row()])

    const cell = screen.getAllByRole('row')[1]
    expect(within(cell).getByText('MaxDeliveryCountExceeded')).toBeInTheDocument()
    expect(within(cell).getByText('Message could not be consumed after 10 delivery attempts.')).toBeInTheDocument()
    expect(within(cell).getByText('Delivered 10 times and never completed')).toBeInTheDocument()
    expect(within(cell).getByLabelText('Suggestion')).toBeInTheDocument()
  })

  it('says the one true thing when a cloud records no reason at all — set aside automatically after N deliveries', () => {
    renderTable([row({ deadLetterErrorDescription: null, deadLetterReason: null, deliveryCount: 4 })])

    expect(screen.getByText('Reason not recorded')).toBeInTheDocument()
    expect(screen.getByText(/Set aside automatically after 4 deliveries that were never completed/)).toBeInTheDocument()
    expect(screen.queryByLabelText('Suggestion')).not.toBeInTheDocument() // nothing to read, so no reading is invented
  })

  it('never says “0 deliveries” — a cloud that does not report the count gets a dash and plain words', () => {
    renderTable([row({ deadLetterErrorDescription: null, deadLetterReason: null, deliveryCount: 0 })])

    expect(screen.getByText(/Set aside automatically after its deliveries ran out/)).toBeInTheDocument()
    expect(screen.queryByText(/0 deliver/)).not.toBeInTheDocument()
    expect(within(screen.getAllByRole('row')[1]).getByTitle('This cloud does not report how many times the message was delivered.')).toHaveTextContent('—')
  })

  it('says the cloud gave no error text when there is a reason but no text', () => {
    renderTable([row({ deadLetterErrorDescription: null })])

    expect(screen.getByText('The cloud gave no error text for this one.')).toBeInTheDocument()
  })

  it('gives “Failed because” the most room, and calls the link Details', () => {
    renderTable([row()])

    const headers = screen.getAllByRole('columnheader')
    expect(headers.find((h) => h.textContent?.startsWith('Failed because'))?.className).toContain('w-[42%]')
    expect(screen.getByRole('link', { name: 'Details of message m-1' })).toHaveTextContent('Details →')
    expect(screen.queryByText(/Open/)).not.toBeInTheDocument()
  })

  it('puts an (i) on every column, so nothing has to be guessed', () => {
    renderTable([row()])

    const headers = screen.getAllByRole('columnheader')
    for (const h of headers) expect(within(h).getByRole('button', { name: /^About / })).toBeInTheDocument()
  })

  it('a row’s Replay opens the Replay modal by itself — the message drawer is only for Details', async () => {
    renderTable([row({ id: 7, messageId: 'm-7' })])
    const replay = await screen.findByRole('link', { name: 'Replay message m-7' })
    expect(replay.getAttribute('href')).toContain('modal=replay')
    expect(replay.getAttribute('href')).toContain('replay=7')
    expect(replay.getAttribute('href')).not.toContain('message=')
    expect(screen.getByRole('link', { name: 'Details of message m-7' }).getAttribute('href')).toContain('message=7')
  })

  it('a row’s Replay puts away a drawer left open for another row', async () => {
    render(<QueryClientProvider client={new QueryClient()}><MemoryRouter initialEntries={['/?tab=dlq&message=3&view=full']}><MessageTable rows={[row({ id: 7, messageId: 'm-7' })]} /></MemoryRouter></QueryClientProvider>)
    const href = (await screen.findByRole('link', { name: 'Replay message m-7' })).getAttribute('href')!
    expect(href).toContain('replay=7')
    expect(href).not.toContain('message=')
    expect(href).not.toContain('view=')
  })
})
