import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { Welcome } from '@/components/connect/Welcome'

const renderWelcome = () => render(<MemoryRouter><Welcome /></MemoryRouter>)

describe('Welcome (Home before anything is connected)', () => {
  it('offers the fake-data demo first, as a link to /demo/azure', () => {
    renderWelcome()
    const demo = screen.getByRole('link', { name: 'Try it with sample data' })
    expect(demo).toHaveAttribute('href', '/demo/azure')
    expect(screen.getByText(/Nothing is connected and nothing is sent anywhere/)).toBeInTheDocument()
  })

  it('puts the demo link before the first connect card in reading order', () => {
    renderWelcome()
    const demo = screen.getByRole('link', { name: 'Try it with sample data' })
    const azure = screen.getByRole('link', { name: 'Connect Azure' })
    expect(demo.compareDocumentPosition(azure) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('does not promise that every cloud confirms a fix held', () => {
    renderWelcome()
    expect(screen.queryByText(/Then it watches to confirm the fix held/)).not.toBeInTheDocument()
    expect(screen.getByText(/on Azure it can confirm the fix held; on AWS and Google it tells you verification is still required/)).toBeInTheDocument()
  })

  it('still offers a connect card for each cloud', () => {
    renderWelcome()
    expect(screen.getByRole('link', { name: 'Connect Azure' })).toHaveAttribute('href', '/?modal=add-cloud&cloud=azure')
    expect(screen.getByRole('link', { name: 'Connect AWS' })).toHaveAttribute('href', '/?modal=add-cloud&cloud=aws')
    expect(screen.getByRole('link', { name: 'Connect Google' })).toHaveAttribute('href', '/?modal=add-cloud&cloud=gcp')
  })

  it('does not claim a read-only connection is enough, or that every cloud can prove a fix held', () => {
    renderWelcome()
    expect(screen.queryByText(/Read-only is enough/)).not.toBeInTheDocument()
    expect(screen.getByText(/AWS and Google say "verification required"/)).toBeInTheDocument()
  })
})
