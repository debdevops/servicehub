import { useQueryClient } from '@tanstack/react-query'
import { FlaskConical } from 'lucide-react'
import { useEffect } from 'react'
import { Navigate, useParams } from 'react-router-dom'
import { storeProvider } from '../provider/providerScope'
import { DEMO_ONLY, demoProviders, enterDemo, isDemo, leaveDemo, resetDemo } from '../../lib/demo/state'
import type { CloudProvider } from '../../lib/api/namespaces'
import { startTour } from '../demo/tour'

/** Where "Get ServiceHub" goes on the public demo page, which has no real app behind it to leave to. */
const REPOSITORY = 'https://github.com/debdevops/servicehub'
const cloudNames = { azure: 'Azure', aws: 'AWS', gcp: 'Google' } as const
/** An address inside the app, whatever path it is served under (the public demo page is not at the root). */
const at = (path: string) => `${import.meta.env.BASE_URL.replace(/\/$/, '')}${path}`

/**
 * `/demo` opens all three clouds together. `/demo/azure`, `/demo/aws`, `/demo/gcp` (and any page beneath, as 4.0.0 published
 * them) open that one cloud. Each switches this browser session into demo mode. The per-cloud URLs are in the README and the
 * growth plan — they must keep working (unit 6.5).
 */
export function DemoEntry() {
  const { provider } = useParams()
  const cloud = demoProviders.find((p) => p === provider?.toLowerCase()) as CloudProvider | undefined
  const client = useQueryClient()
  // An address that names no cloud we know (`/demo/other`) is not an entry to the demo.
  const enters = !provider || !!cloud
  if (enters) enterDemo()
  if (cloud) storeProvider(cloud)
  useEffect(() => { if (enters) client.clear() }, [enters, cloud, client])
  return <Navigate to={cloud ? `/?tab=dlq&provider=${cloud}` : '/'} replace />
}

/** Visible on every screen while demo mode is on: this is made-up data, and nothing is sent. */
export function DemoBanner() {
  const client = useQueryClient()
  if (!isDemo()) return null
  const link = 'hover:underline'
  const button = 'rounded-md border border-[#c4b5fd] bg-white px-2 py-0.5 hover:bg-[#ede9fe]'
  return (
    <div role="status" className="flex flex-wrap items-center gap-3 border-b border-[#c4b5fd] bg-[#f5f3ff] px-5 py-2 text-[13px] text-[#4c1d95]">
      <FlaskConical className="h-4 w-4" aria-hidden="true" />
      <span className="min-w-0 flex-1"><b>Demo.</b> Everything here is made up, and nothing you do is sent anywhere. The clouds behave as they really do — AWS and Google can’t prove a fix held, so you’ll see “verification required” there.</span>
      <span className="flex flex-wrap items-center gap-2 font-semibold">
        <a href={at('/demo')} className={link}>All clouds</a>
        {demoProviders.map((p) => <a key={p} href={at(`/demo/${p}`)} className={link}>{cloudNames[p]}</a>)}
        <button type="button" onClick={startTour} className={`ml-2 ${button}`}>Show me around</button>
        <button type="button" onClick={() => { void resetDemo().then(() => client.invalidateQueries()) }} className={button}>Reset demo</button>
        {DEMO_ONLY
          ? <a href={REPOSITORY} className={button}>Get ServiceHub</a>
          : <button type="button" onClick={() => { leaveDemo(); window.location.assign(at('/')) }} className={button}>Leave the demo</button>}
      </span>
    </div>
  )
}
