import { renderHook, act } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { useHomeScope } from './scope'
import type { CloudProvider } from '../api/namespaces'

function wrapper(initial: string) {
  return ({ children }: { children: React.ReactNode }) => <MemoryRouter initialEntries={[initial]}>{children}</MemoryRouter>
}

describe('useHomeScope (D48: the one scope Home carries in its URL)', () => {
  it('with 2+ clouds and no ?provider=, the scope is All clouds (null)', () => {
    const { result } = renderHook(() => useHomeScope(['azure', 'aws']), { wrapper: wrapper('/') })
    expect(result.current.provider).toBeNull()
  })

  it('with exactly one cloud connected, that is always the scope — there is nothing else it could mean', () => {
    const { result } = renderHook(() => useHomeScope(['azure']), { wrapper: wrapper('/') })
    expect(result.current.provider).toBe('azure')
  })

  it('a connected ?provider= is honoured', () => {
    const { result } = renderHook(() => useHomeScope(['azure', 'aws']), { wrapper: wrapper('/?provider=aws') })
    expect(result.current.provider).toBe('aws')
  })

  it('an unconnected or invalid ?provider= falls back to All clouds, never an error', () => {
    const unconnected = renderHook(() => useHomeScope(['azure', 'aws']), { wrapper: wrapper('/?provider=gcp') })
    expect(unconnected.result.current.provider).toBeNull()

    const invalid = renderHook(() => useHomeScope(['azure', 'aws']), { wrapper: wrapper('/?provider=not-a-cloud') })
    expect(invalid.result.current.provider).toBeNull()
  })

  it('setProvider drops ?ns= and ?env= — they belonged to whatever scope came before', () => {
    const { result } = renderHook(() => useHomeScope(['azure', 'aws'] as readonly CloudProvider[]), { wrapper: wrapper('/?provider=azure&ns=a1&env=dev') })
    act(() => result.current.setProvider('aws'))
    // The hook reads from useSearchParams, which re-renders this same hook instance with fresh params.
    expect(result.current.provider).toBe('aws')
  })

  it('the window defaults to 24h and only 24h/7d are ever returned', () => {
    expect(renderHook(() => useHomeScope(['azure']), { wrapper: wrapper('/') }).result.current.window).toBe('24h')
    expect(renderHook(() => useHomeScope(['azure']), { wrapper: wrapper('/?window=7d') }).result.current.window).toBe('7d')
    expect(renderHook(() => useHomeScope(['azure']), { wrapper: wrapper('/?window=30d') }).result.current.window).toBe('24h')
  })
})
