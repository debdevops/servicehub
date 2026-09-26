import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { useShortcuts } from './useShortcuts'

function Harness({ openSearch }: { openSearch: () => void }) {
  useShortcuts({ openSearch, simpleHref: '/', advancedHref: '/advanced' })
  const { pathname, search } = useLocation()
  return <><p data-testid="at">{pathname + search}</p><input aria-label="typing" /><input aria-label="filter" data-shortcut="filter" /></>
}
const setup = (url = '/') => { const openSearch = vi.fn(); render(<MemoryRouter initialEntries={[url]}><Harness openSearch={openSearch} /></MemoryRouter>); return openSearch }
const at = () => screen.getByTestId('at').textContent

describe('global shortcuts', () => {
  it('⌘K and Ctrl-K open search, even from a text field', () => {
    const open = setup()
    fireEvent.keyDown(window, { key: 'k', metaKey: true })
    fireEvent.keyDown(screen.getByLabelText('typing'), { key: 'k', ctrlKey: true })
    expect(open).toHaveBeenCalledTimes(2)
  })

  it('? opens Help and A switches Simple / Advanced', () => {
    setup('/?tab=dlq')
    fireEvent.keyDown(window, { key: '?' })
    expect(at()).toBe('/?tab=dlq&panel=help')
    fireEvent.keyDown(window, { key: 'a' })
    expect(at()).toBe('/advanced')
    fireEvent.keyDown(window, { key: 'A' })
    expect(at()).toBe('/')
  })

  it('R opens the replay proposal only when a message is open — it never replays', () => {
    setup('/?tab=dlq')
    fireEvent.keyDown(window, { key: 'r' })
    expect(at()).toBe('/?tab=dlq')
  })

  it('R with a message open opens the proposal', () => {
    setup('/?tab=dlq&message=m1')
    fireEvent.keyDown(window, { key: 'r' })
    expect(at()).toBe('/?tab=dlq&message=m1&modal=replay')
  })

  it('/ focuses the filter; single keys do nothing while typing', () => {
    setup()
    fireEvent.keyDown(window, { key: '/' })
    expect(document.activeElement).toBe(screen.getByLabelText('filter'))
    fireEvent.keyDown(screen.getByLabelText('typing'), { key: '?' })
    expect(at()).toBe('/')
  })
})
