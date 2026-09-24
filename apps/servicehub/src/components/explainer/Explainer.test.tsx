import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { explanations } from '../../content/explanations'
import { ExplainerCard, ExplainerToggle } from './Explainer'
import { useExplainer } from './useExplainer'

function View() {
  const { shown, dismiss, show } = useExplainer('dead-letters')
  return (
    <>
      <h1>
        Dead letters <ExplainerToggle visible={!shown} onShow={show} />
      </h1>
      {shown && <ExplainerCard id="dead-letters" onDismiss={dismiss} />}
    </>
  )
}

describe('the explainer', () => {
  beforeEach(() => window.localStorage.clear())

  it('opens the view with its terms, taken from the one source of words', () => {
    render(<View />)
    for (const { term } of explanations['dead-letters'].terms) expect(screen.getByText(term)).toBeInTheDocument()
  })

  it('is dismissed with Got it, remembered, and brought back by the (?)', async () => {
    const first = render(<View />)
    await userEvent.click(screen.getByRole('button', { name: 'Got it' }))
    expect(screen.queryByText('Dead letter')).not.toBeInTheDocument()

    first.unmount()
    render(<View />)
    expect(screen.queryByText('Dead letter')).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'What am I looking at?' }))
    expect(screen.getByText('Dead letter')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'What am I looking at?' })).not.toBeInTheDocument()
  })

  it('never blocks the view when storage is unavailable', async () => {
    vi.spyOn(window.localStorage, 'getItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    vi.spyOn(window.localStorage, 'setItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    render(<View />)
    expect(screen.getByText('Dead letter')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Got it' }))
    expect(screen.queryByText('Dead letter')).not.toBeInTheDocument()
    vi.restoreAllMocks()
  })

  it('offers no learn-more link until Help exists', () => {
    render(<ExplainerCard id="fleet" onDismiss={() => {}} />)
    expect(screen.queryByText(/Clouds and what they can prove/)).not.toBeInTheDocument()
  })
})
