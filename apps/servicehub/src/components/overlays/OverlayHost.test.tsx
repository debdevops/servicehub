import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { overlayBodies, overlayEntries } from './registry'
import { OverlayHost } from './OverlayHost'

function Where() {
  const { pathname, search } = useLocation()
  return <output data-testid="where">{pathname + search}</output>
}

function renderHost(url: string, connectedCloudCount = 1) {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <Where />
      <button>page control</button>
      <OverlayHost connectedCloudCount={connectedCloudCount} />
    </MemoryRouter>,
  )
}

const where = () => screen.getByTestId('where').textContent

describe('OverlayHost — the URL is the only switch', () => {
  it('opens nothing when the URL names nothing', () => {
    renderHost('/')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('opens a modal from ?modal=, titled from the navigation entry', () => {
    renderHost('/?modal=settings')
    const dialog = screen.getByRole('dialog', { name: 'Settings' })
    expect(dialog).toHaveAttribute('data-overlay', 'modal')
  })

  // Whichever overlay is still unbuilt says so, rather than opening nothing (R5). Once all are built this has nothing to check.
  const unbuilt = overlayEntries.find((e) => !(e.id in overlayBodies) && e.visibility === 'always')
  it.skipIf(!unbuilt)('an overlay that is not built yet opens a frame that says so', () => {
    renderHost(`/?${unbuilt!.kind}=${unbuilt!.value}`)
    expect(screen.getByRole('dialog', { name: unbuilt!.label })).toHaveTextContent(`Not built yet — Wave ${unbuilt!.wave}`)
  })

  it('opens a panel from ?panel=', () => {
    renderHost('/?panel=help')
    expect(screen.getByRole('dialog', { name: 'Help' })).toHaveAttribute('data-overlay', 'panel')
  })

  it('opens nothing for a value that names nothing, or for the wrong kind', () => {
    renderHost('/?modal=nonsense&panel=add-cloud')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('does not offer an overlay that makes no sense yet — Replay before any cloud is connected', () => {
    renderHost('/?modal=replay', 0)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('can have a panel and a modal open together', () => {
    renderHost('/?panel=help&modal=settings')
    expect(screen.getAllByRole('dialog')).toHaveLength(2)
  })

  it('closes with Esc by removing only its own parameter', async () => {
    renderHost('/?tab=dlq&modal=settings')
    await userEvent.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(where()).toBe('/?tab=dlq')
  })

  it('closes with ✕', async () => {
    renderHost('/?modal=settings')
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(where()).toBe('/')
  })

  it('closes when the scrim is clicked', async () => {
    renderHost('/?panel=help')
    await userEvent.click(screen.getByTestId('overlay-scrim'))
    expect(where()).toBe('/')
  })

  it('opens again from the URL alone — it is linkable and survives a refresh', () => {
    const first = renderHost('/?modal=settings')
    first.unmount()
    renderHost('/?modal=settings')
    expect(screen.getByRole('dialog', { name: 'Settings' })).toBeInTheDocument()
  })

  it('moves focus into the dialog and hands it back on close', async () => {
    renderHost('/?modal=settings')
    expect(screen.getByRole('dialog')).toHaveFocus()
    await userEvent.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('keeps Tab inside the dialog', async () => {
    renderHost('/?modal=settings')
    await userEvent.tab()
    expect(screen.getByRole('button', { name: 'Close' })).toHaveFocus()
    await userEvent.tab()
    expect(screen.getByRole('button', { name: 'Close' })).toHaveFocus()
  })
})

describe('overlay registry', () => {
  it('only registers bodies for overlays the navigation array actually lists', () => {
    const ids = new Set(overlayEntries.map((e) => e.id))
    for (const id of Object.keys(overlayBodies)) expect(ids.has(id), id).toBe(true)
  })
})
