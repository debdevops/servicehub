import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import HelpPanel from './HelpPanel'
import { expectNoAxeViolations } from '../../test/axe'

describe('HelpPanel', () => {
  it('has no accessibility violations (6.6)', async () => {
    const { container } = render(<MemoryRouter><HelpPanel /></MemoryRouter>)
    await expectNoAxeViolations(container)
  })

  it('lists task answers and the keyboard shortcuts, and filters as you type', () => {
    render(<MemoryRouter><HelpPanel /></MemoryRouter>)
    expect(screen.getByText('Replay a dead-lettered message')).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Keyboard' })).toBeInTheDocument()
    fireEvent.change(screen.getByPlaceholderText('How do I…'), { target: { value: 'many' } })
    expect(screen.getByText('Replay many at once')).toBeInTheDocument()
    expect(screen.queryByText('Replay a dead-lettered message')).not.toBeInTheDocument()
  })
})
