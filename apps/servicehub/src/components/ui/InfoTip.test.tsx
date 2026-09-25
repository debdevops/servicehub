import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { columnHelp } from '../../content/columns'
import { InfoTip } from './InfoTip'

describe('InfoTip', () => {
  it('opens on click with the title and the words, and says it is expanded', async () => {
    render(<InfoTip help={columnHelp.deadLetters.tries} />)
    const button = screen.getByRole('button', { name: 'About Tries' })
    expect(button).toHaveAttribute('aria-expanded', 'false')

    await userEvent.click(button)

    expect(button).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('note')).toHaveTextContent('How many times a consumer picked this message up')
  })

  it('closes on Escape, on a second click, and on a click elsewhere', async () => {
    render(<div><InfoTip help={columnHelp.deadLetters.size} /><p>elsewhere</p></div>)
    const button = screen.getByRole('button', { name: 'About Size' })

    await userEvent.click(button)
    await userEvent.keyboard('{Escape}')
    expect(screen.queryByRole('note')).not.toBeInTheDocument()

    await userEvent.click(button)
    await userEvent.click(button)
    expect(screen.queryByRole('note')).not.toBeInTheDocument()

    await userEvent.click(button)
    await userEvent.click(screen.getByText('elsewhere'))
    expect(screen.queryByRole('note')).not.toBeInTheDocument()
  })

  it('is reachable and operable from the keyboard', async () => {
    render(<InfoTip help={columnHelp.deadLetters.when} />)
    await userEvent.tab()
    await userEvent.keyboard('{Enter}')
    expect(screen.getByRole('note')).toBeInTheDocument()
  })
})
