import { useCallback, useEffect, useRef, useState } from 'react'
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { useNamespaces } from '../../hooks/useNamespaces'
import { useProviderScope } from '../provider/providerScope'
import { TOUR_EVENT, rememberTourSeen, stops, tourSeen } from './tour'

interface Box { top: number; left: number; width: number; height: number }

/**
 * "Show me around" (4.2.0): a walk through the product's one idea — a message failed, see why, put it back, see whether it
 * stayed fixed — on the REAL screens, not pictures of them. Demo only. It starts by itself once, and again from the banner.
 */
export default function DemoTour() {
  const [index, setIndex] = useState<number | null>(null)
  const [box, setBox] = useState<Box | null>(null)
  const navigate = useNavigate()
  const location = useLocation()
  const [search, setSearch] = useSearchParams()
  const { selected } = useProviderScope()
  const namespaces = useNamespaces()
  const heading = useRef<HTMLHeadingElement>(null)
  // The cloud the tour walks through is fixed when it starts, so choosing another mid-tour does not move it.
  const provider = useRef<string>('azure')

  const finish = useCallback(() => {
    rememberTourSeen()
    setIndex(null)
    setBox(null)
  }, [])

  const begin = useCallback(() => {
    provider.current = selected ?? 'azure'
    setIndex(0)
  }, [selected])

  // Once, the first time the demo's Home is seen — and whenever the banner asks.
  useEffect(() => {
    window.addEventListener(TOUR_EVENT, begin)
    return () => window.removeEventListener(TOUR_EVENT, begin)
  }, [begin])
  const offered = useRef(false)
  useEffect(() => {
    // `?tour=1` is the Home page's Demo button: it always walks through, even for someone who has seen the tour before.
    const asked = search.get('tour') === '1'
    // By itself it starts only on a plain Home: a link to a panel or page (`/demo/?panel=connections`) is where the visitor meant to go.
    const plainHome = location.search === '' || asked
    if (offered.current || (tourSeen() && !asked) || location.pathname !== '/' || !plainHome || !namespaces.data?.length) return
    offered.current = true
    if (asked) setSearch((p) => { const n = new URLSearchParams(p); n.delete('tour'); return n }, { replace: true })
    begin()
  }, [begin, location.pathname, location.search, namespaces.data, search, setSearch])

  const stop = index === null ? null : stops[index]

  // Arriving at a stop: go to its screen, open what it talks about, and move focus to the words.
  useEffect(() => {
    if (!stop) return
    if (stop.path) navigate(stop.path(provider.current))
    heading.current?.focus()
    if (!stop.open) return
    const { ready, press } = stop.open
    const timer = setInterval(() => {
      if (ready()) return clearInterval(timer)
      const control = document.querySelector<HTMLElement>(press)
      if (control) {
        clearInterval(timer)
        control.click()
      }
    }, 200)
    return () => clearInterval(timer)
    // eslint-disable-next-line react-hooks/exhaustive-deps -- a stop is entered once, when its number changes
  }, [index])

  // Keep the ring on the stop's subject as the screen loads, scrolls or resizes.
  useEffect(() => {
    if (!stop) return
    let last: Element | null = null
    const place = () => {
      const target = stop.target()
      if (!target) return setBox(null)
      if (target !== last) {
        last = target
        target.scrollIntoView({ block: 'center', behavior: 'smooth' })
      }
      const r = target.getBoundingClientRect()
      setBox((b) => (b && b.top === r.top && b.left === r.left && b.width === r.width && b.height === r.height ? b : { top: r.top, left: r.left, width: r.width, height: r.height }))
    }
    place()
    const timer = setInterval(place, 250)
    return () => clearInterval(timer)
  }, [stop])

  useEffect(() => {
    if (index === null) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') finish() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [index, finish])

  if (index === null || !stop) return null

  const canConfirm = namespaces.data?.find((n) => n.provider === provider.current)?.capabilities?.canProveDlqAbsence ?? false
  const last = index === stops.length - 1
  const next = () => {
    stop.nextPresses?.()?.click()
    if (last) finish()
    else setIndex(index + 1)
  }
  // The card sits in a bottom corner; it moves to the other one rather than cover what it points at.
  const coversRight = box !== null && box.left + box.width > window.innerWidth - 400 && box.top + box.height > window.innerHeight - 260
  const button = 'rounded-lg px-3 py-1.5 text-sm font-medium'

  return (
    <>
      {box && (
        <div
          aria-hidden="true"
          className="pointer-events-none fixed z-[70] rounded-xl ring-4 ring-[#7c3aed] ring-offset-2 transition-all duration-200"
          style={{ top: box.top - 4, left: box.left - 4, width: box.width + 8, height: box.height + 8 }}
        />
      )}
      <section
        role="dialog"
        aria-modal="false"
        aria-labelledby="demo-tour-title"
        className={`fixed bottom-4 z-[71] w-[min(360px,calc(100vw-2rem))] rounded-xl border border-[#c4b5fd] bg-white p-4 text-[#1f2937] shadow-2xl ${coversRight ? 'left-4' : 'right-4'}`}
      >
        <p className="text-xs font-semibold uppercase tracking-wide text-[#5b21b6]">Show me around · {index + 1} of {stops.length}</p>
        <h2 id="demo-tour-title" ref={heading} tabIndex={-1} className="mt-1 text-base font-semibold outline-none">{stop.title}</h2>
        <p className="mt-2 text-sm leading-relaxed">{stop.text(canConfirm)}</p>
        <div className="mt-4 flex items-center gap-2">
          <button type="button" onClick={finish} className={`${button} text-[#4b5563] hover:bg-[#f3f4f6]`}>Skip</button>
          <span className="flex-1" />
          {index > 0 && <button type="button" onClick={() => setIndex(index - 1)} className={`${button} border border-[#d1d5db] hover:bg-[#f3f4f6]`}>Back</button>}
          <button type="button" onClick={next} className={`${button} bg-[#5b21b6] text-white hover:bg-[#4c1d95]`}>{last ? 'Done' : 'Next'}</button>
        </div>
      </section>
    </>
  )
}
