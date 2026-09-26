import { useQueryClient } from '@tanstack/react-query'
import { FlaskConical } from 'lucide-react'
import { useEffect } from 'react'
import { Navigate, useParams } from 'react-router-dom'
import { storeProvider } from '../provider/providerScope'
import { demoProviders, enterDemo, isDemo, leaveDemo } from '../../lib/demo/state'
import type { CloudProvider } from '../../lib/api/namespaces'

/**
 * `/demo/azure`, `/demo/aws`, `/demo/gcp` (and any page beneath, as 4.0.0 published them): switch this browser session into demo
 * mode on that cloud, then open Home. The URLs are in the README and the growth plan — they must keep working (unit 6.5).
 */
export function DemoEntry() {
  const { provider } = useParams()
  const cloud = demoProviders.find((p) => p === provider?.toLowerCase()) as CloudProvider | undefined
  const client = useQueryClient()
  if (cloud) {
    enterDemo()
    storeProvider(cloud)
  }
  useEffect(() => { if (cloud) client.clear() }, [cloud, client])
  return <Navigate to={cloud ? '/?tab=dlq' : '/'} replace />
}

/** Visible on every screen while demo mode is on: this is made-up data, and nothing is sent. */
export function DemoBanner() {
  if (!isDemo()) return null
  return (
    <div role="status" className="flex flex-wrap items-center gap-3 border-b border-[#c4b5fd] bg-[#f5f3ff] px-5 py-2 text-[13px] text-[#4c1d95]">
      <FlaskConical className="h-4 w-4" aria-hidden="true" />
      <span className="min-w-0 flex-1"><b>Demo.</b> Everything here is made up, and nothing you do is sent anywhere. The clouds behave as they really do — AWS and Google can’t prove a fix held, so you’ll see “verification required” there.</span>
      <span className="flex gap-2 font-semibold">
        {demoProviders.map((p) => <a key={p} href={`/demo/${p}`} className="hover:underline">{({ azure: 'Azure', aws: 'AWS', gcp: 'Google' } as const)[p]}</a>)}
        <button type="button" onClick={() => { leaveDemo(); window.location.assign('/') }} className="ml-2 rounded-md border border-[#c4b5fd] bg-white px-2 py-0.5 hover:bg-[#ede9fe]">Leave the demo</button>
      </span>
    </div>
  )
}
