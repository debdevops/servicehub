import { renderHook, act } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { useHomeScope } from '@/lib/home/scope'
import { ProviderScopeContext } from '@/components/provider/providerScope'
import type { CloudProvider } from '@/lib/api/namespaces'

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

  describe('the sidebar\'s sticky cloud follows the cloud the URL shows (scope is one state)', () => {
    const withSticky = (initial: string, selected: CloudProvider | null, select: (p: CloudProvider) => void) =>
      ({ children }: { children: React.ReactNode }) => (
        <ProviderScopeContext.Provider value={{ selected, select }}>
          <MemoryRouter initialEntries={[initial]}>{children}</MemoryRouter>
        </ProviderScopeContext.Provider>
      )

    it('opening ?provider=aws while the sticky choice is Azure makes AWS the sticky choice — so "Dead letters" opens AWS, not Azure', () => {
      const select = vi.fn()
      renderHook(() => useHomeScope(['azure', 'aws']), { wrapper: withSticky('/?provider=aws', 'azure', select) })
      expect(select).toHaveBeenCalledWith('aws')
    })

    it('picking a cloud on the scope tabs does the same', () => {
      const select = vi.fn()
      const { result } = renderHook(() => useHomeScope(['azure', 'aws']), { wrapper: withSticky('/', 'azure', select) })
      expect(select).not.toHaveBeenCalled()
      act(() => result.current.setProvider('aws'))
      expect(select).toHaveBeenCalledWith('aws')
    })

    it('does nothing when they already agree, on All clouds, or when ?provider= is not a connected cloud', () => {
      const agree = vi.fn(), all = vi.fn(), stale = vi.fn()
      renderHook(() => useHomeScope(['azure', 'aws']), { wrapper: withSticky('/?provider=aws', 'aws', agree) })
      renderHook(() => useHomeScope(['azure', 'aws']), { wrapper: withSticky('/', 'azure', all) })
      renderHook(() => useHomeScope(['azure', 'aws']), { wrapper: withSticky('/?provider=gcp', 'azure', stale) })
      expect(agree).not.toHaveBeenCalled()
      expect(all).not.toHaveBeenCalled()
      expect(stale).not.toHaveBeenCalled()
    })
  })
})
