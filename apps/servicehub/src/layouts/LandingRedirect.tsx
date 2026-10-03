import { useEffect, useRef } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { landingPath } from '../nav/navigation'
import { lastPage, readPreferences } from '../lib/preferences'

/**
 * Applies the landing rule (IA §2.4) once, when the app first opens. Only a bare visit to `/` is
 * redirected: a deep link (`/?modal=…`, an Advanced page) goes where it says. And only once —
 * afterwards Home is a place you can choose to go, so clicking it must not bounce you anywhere else.
 *
 * `landingPath()` is always `/` since Fleet Overview merged into Home (2026-09-27) — this component
 * is left in place because "Open on: last used" (Settings) can still send a first visit somewhere
 * other than `/`, and a Connect page or another landing rule could reintroduce a real destination.
 */
export function LandingRedirect({ ready }: { ready: boolean }) {
  const { pathname, search } = useLocation()
  const navigate = useNavigate()
  const applied = useRef(false)

  useEffect(() => {
    if (!ready || applied.current) return
    applied.current = true
    if (pathname !== '/' || search !== '') return
    // "Open on: last used" (Settings) wins over the landing rule; the landing rule is the default.
    const last = readPreferences().openOn === 'last' ? lastPage() : null
    const target = last ?? landingPath()
    if (target !== '/') navigate(target, { replace: true })
  }, [ready, pathname, search, navigate])

  return null
}
