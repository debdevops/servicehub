import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { isDemo } from '../../lib/demo/state'

/**
 * ServiceHub Bot's floating button (`servicehub-bot/` at the repo root; owner's decision (c), 2026-11-15: the bot comes with
 * `./run.sh` and development only, not with the Docker image).
 *
 * `run.sh` sets `VITE_SERVICEHUB_BOT=true` only when it started the bot, and Vite proxies `/bot` to it. Without that flag this
 * renders nothing and makes no request, so Docker, the public demo page and the test suites are unchanged. With it, the bot
 * must also answer `/bot/health` with its own JSON before anything is shown: a missing bot never leaves a broken button.
 *
 * The bot guides; it never acts. It reads ServiceHub with GET only, as the person using this page (the proxy passes the
 * browser's own headers), and its links only open screens. In the demo it is told `live="false"`: there is no API behind the
 * demo, so it answers from its knowledge base alone.
 */
export const BOT_ENABLED: boolean = import.meta.env.VITE_SERVICEHUB_BOT === 'true'

const BOT = '/bot'
const DEMO_BASE = '/demo'

export function BotLauncher({ enabled = BOT_ENABLED }: { readonly enabled?: boolean }) {
  const navigate = useNavigate()
  const navigateRef = useRef(navigate)
  navigateRef.current = navigate

  useEffect(() => {
    if (!enabled) return
    let stopped = false
    let element: HTMLElement | null = null
    const demo = isDemo()
    const base = demo ? DEMO_BASE : ''

    // The widget's links are full addresses (`/demo/aws/replays`); the router wants them without its basename.
    const onNavigate = (event: Event) => {
      const href = (event as CustomEvent<{ href?: unknown }>).detail?.href
      if (typeof href !== 'string') return
      event.preventDefault()
      if (href.startsWith('?')) navigateRef.current({ search: href })
      else navigateRef.current(base && href.startsWith(`${base}/`) ? href.slice(base.length) : href === base ? '/' : href)
    }

    void (async () => {
      if (!(await botAnswers()) || stopped) return
      if (!document.querySelector('script[data-servicehub-bot]')) {
        const script = document.createElement('script')
        script.type = 'module'
        script.src = `${BOT}/widget.js`
        script.dataset.servicehubBot = ''
        document.head.append(script)
      }
      element = document.createElement('servicehub-bot')
      element.setAttribute('endpoint', `${BOT}/ask`)
      element.setAttribute('live', demo ? 'false' : 'true')
      if (base) element.setAttribute('app-base', base)
      element.addEventListener('servicehub-bot:navigate', onNavigate)
      document.body.append(element)
    })()

    return () => {
      stopped = true
      element?.removeEventListener('servicehub-bot:navigate', onNavigate)
      element?.remove()
    }
  }, [enabled])

  return null
}

/** True only when the bot itself answers: its own JSON with `ok: true`, not an HTML page or a proxy error. */
async function botAnswers(): Promise<boolean> {
  try {
    const response = await fetch(`${BOT}/health`, { headers: { accept: 'application/json' } })
    if (!response.ok || !(response.headers.get('content-type') ?? '').includes('application/json')) return false
    const body = (await response.json()) as { ok?: unknown }
    return body.ok === true
  } catch {
    return false
  }
}
