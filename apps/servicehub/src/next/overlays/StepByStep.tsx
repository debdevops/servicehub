import { useEffect, useState } from 'react'
import { guides } from '../../content/guides'
import GuideView from '../../components/help/GuideView'

/** One cloud's guide. Its body (dozens of steps with pictures) is mounted only once the section has been opened. */
function Guide({ cloud, id, open }: { cloud: string; id: string; open: boolean }) {
  const [opened, setOpened] = useState(open)
  const [expanded, setExpanded] = useState(open)
  // A deep link (?topic=guide-<cloud>) opens its guide; after that the reader's own clicks decide, so it can still be closed.
  useEffect(() => { if (open) { setExpanded(true); setOpened(true) } }, [open])
  return (
    <details id={id} open={expanded} onToggle={(e) => { setExpanded(e.currentTarget.open); if (e.currentTarget.open) setOpened(true) }} className="rounded-xl border border-[var(--color-border)]">
      <summary className="cursor-pointer px-4 py-2.5 font-semibold">{guides[cloud].title}</summary>
      {opened && (
        <div className="px-3 pb-3">
          <GuideView guide={guides[cloud]} />
        </div>
      )}
    </details>
  )
}

/** The three per-cloud guides inside the 5.0.0 Help (lazy-loaded by `HelpPanel`); `?topic=guide-<cloud>` opens one. */
export default function StepByStep({ topic, clouds, topicOf }: { topic: string | null; clouds: readonly string[]; topicOf: (cloud: string) => string }) {
  return (
    <div className="space-y-2">
      {clouds.map((cloud) => <Guide key={cloud} cloud={cloud} id={`help-${topicOf(cloud)}`} open={topic === topicOf(cloud)} />)}
    </div>
  )
}
