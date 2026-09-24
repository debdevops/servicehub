import { Link } from 'react-router-dom'
import { ShieldCheck } from 'lucide-react'
import type { CloudProvider } from '../../lib/api/namespaces'

const cards: readonly { cloud: CloudProvider; name: string; detail: string; need: string; action: string }[] = [
  { cloud: 'azure', name: 'Azure Service Bus', detail: 'Queues, topics and subscriptions', need: 'a connection string', action: 'Connect Azure' },
  { cloud: 'aws', name: 'AWS SQS / SNS', detail: 'Queues and their dead-letter queues', need: 'an access key and a region', action: 'Connect AWS' },
  { cloud: 'gcp', name: 'Google Pub/Sub', detail: 'Topics and pull subscriptions', need: 'a project ID and a service-account key', action: 'Connect Google' },
]

const steps = [
  { title: 'Connect', body: 'Read-only is enough to start. Credentials are encrypted on this server.' },
  { title: "See what's stuck, and why", body: 'Dead letters grouped by how they failed, with the failing field called out.' },
  { title: 'Replay — and know it held', body: 'A preview before anything runs; a verdict after. Nothing is retried behind your back.' },
]

/** Home before anything is connected (D45): the front door. Each card opens Add a cloud in place. */
export function Welcome() {
  return (
    <section className="mx-auto max-w-4xl px-6 py-12">
      <h1 className="text-3xl font-semibold text-[var(--color-text)]">Welcome</h1>
      <p className="mt-1 text-lg text-[var(--color-text)]">See what's stuck in your message queues — and put it back, safely.</p>
      <p className="mt-2 max-w-2xl text-[var(--color-text-muted)]">
        Connect a cloud and ServiceHub shows every dead-lettered message, why it failed, and a one-click replay that shows you
        exactly what will happen first. Then it watches to confirm the fix held.
      </p>

      <ul className="mt-8 grid gap-4 sm:grid-cols-3">
        {cards.map((c) => (
          <li key={c.cloud} className="flex flex-col rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-5">
            <h2 className="font-semibold text-[var(--color-text)]">{c.name}</h2>
            <p className="text-sm text-[var(--color-text-muted)]">{c.detail}</p>
            <p className="mt-3 text-sm text-[var(--color-text)]">
              You'll need: <b>{c.need}</b>
            </p>
            <Link
              to={`/?modal=add-cloud&cloud=${c.cloud}`}
              className="mt-4 rounded-lg bg-[var(--color-primary-600)] px-4 py-2 text-center text-sm font-medium text-white hover:bg-[var(--color-primary-700)]"
            >
              {c.action}
            </Link>
          </li>
        ))}
      </ul>

      <h2 className="mt-10 text-sm font-semibold text-[var(--color-text)]">How it works — three steps, no setup beyond the connection</h2>
      <ol className="mt-3 grid gap-4 sm:grid-cols-3">
        {steps.map((s, i) => (
          <li key={s.title} className="text-sm">
            <span className="mr-2 inline-flex h-6 w-6 items-center justify-center rounded-full bg-[var(--color-primary-100)] text-xs font-semibold text-[var(--color-primary-700)]">
              {i + 1}
            </span>
            <b className="text-[var(--color-text)]">{s.title}</b>
            <p className="mt-1 text-[var(--color-text-muted)]">{s.body}</p>
          </li>
        ))}
      </ol>

      <p className="mt-10 flex items-start gap-2 text-sm text-[var(--color-text-muted)]">
        <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
        <span>
          <b>Runs on your server.</b> ServiceHub talks only to the clouds you connect. Credentials are encrypted at rest; nothing is
          sent anywhere else.
        </span>
      </p>
    </section>
  )
}
