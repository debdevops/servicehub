import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { Attribution, type AttributionActor } from './Attribution'

const at = '2026-09-25T10:42:00Z'
const now = new Date('2026-09-25T12:00:00Z')
const show = (actor: AttributionActor, role?: string | null) =>
  render(<MemoryRouter><Attribution actor={actor} at={at} now={now} role={role} /></MemoryRouter>)

describe('Attribution — never invents a person (R6)', () => {
  it('shows the name an identity provider supplied, and the role only when known', () => {
    show({ identity: 'Debasis Ghosh', kind: 'user', label: 'Debasis Ghosh', isSession: false }, 'Administrator')
    expect(screen.getByText('Debasis Ghosh')).toBeInTheDocument()
    expect(screen.getByText(/Administrator/)).toBeInTheDocument()
  })

  it('does not make up a role that is not known', () => {
    show({ identity: 'Debasis Ghosh', kind: 'user', label: 'Debasis Ghosh', isSession: false })
    expect(screen.queryByText(/Administrator|Operator|Viewer/)).toBeNull()
  })

  it('shows an API key as a credential, not a person', () => {
    show({ identity: 'ApiKey:ci-pipeline', kind: 'apiKey', label: 'ApiKey:ci-pipeline', isSession: false })
    expect(screen.getByText('ci-pipeline')).toBeInTheDocument()
    expect(screen.getByText(/API key/)).toBeInTheDocument()
  })

  it('says "from this browser session" with a link to connect a sign-in — and no name, no initials', () => {
    const { container } = show({ identity: 'session:abc', kind: 'user', label: 'from this browser session', isSession: true })
    expect(screen.getByText(/from this browser session/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Connect a sign-in/ })).toHaveAttribute('href', expect.stringContaining('modal=settings'))
    expect(container.textContent).not.toMatch(/\b[A-Z]{2}\b/)
  })

  it('shows ServiceHub itself as a component, not a person', () => {
    show({ identity: 'System:RecoveryVerificationAgent', kind: 'system', label: 'System:RecoveryVerificationAgent', isSession: false })
    expect(screen.getByText('ServiceHub · RecoveryVerificationAgent')).toBeInTheDocument()
  })
})
