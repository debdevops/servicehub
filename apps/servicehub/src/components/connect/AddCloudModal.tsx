import { useState, type ReactNode } from 'react'
import { useMe } from '../../hooks/useIdentity'
import { permission } from '../../lib/permissions'
import { NotAllowed } from '../ui/NotAllowed'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { CheckCircle2, Eye, EyeOff, TriangleAlert } from 'lucide-react'
import { useProviderScope } from '../provider/providerScope'
import type { OverlayBodyProps } from '../overlays/registry'
import { useConnectNamespace, useRemoveNamespace, useTestConnection } from '../../hooks/useNamespaces'
import { toProblem } from '../../lib/api/client'
import { fetchNamespaceStats } from '../../lib/api/namespaces'
import type { CloudProvider, EnvironmentKind, Namespace } from '../../lib/api/namespaces'
import { providerLabel } from '../../lib/providers'
import { buildConnectInput, describeFound, describeProof, emptyForm, type CloudForm } from './credentials'

const clouds: readonly { readonly id: CloudProvider; readonly label: string }[] = [
  { id: 'azure', label: 'Azure Service Bus' },
  { id: 'aws', label: 'AWS SQS / SNS' },
  { id: 'gcp', label: 'Google Pub/Sub' },
]

const environments: readonly { readonly id: EnvironmentKind; readonly label: string }[] = [
  { id: 'dev', label: 'Development' },
  { id: 'uat', label: 'UAT' },
  { id: 'prod', label: 'Production' },
]

type Step =
  | { readonly at: 'form'; readonly problem?: string }
  | { readonly at: 'checking' }
  | { readonly at: 'connected'; readonly namespace: Namespace; readonly found: string }
  | { readonly at: 'failed'; readonly namespace: Namespace; readonly reason: string }

const inputClass =
  'w-full rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2 text-sm text-[var(--color-text)] placeholder:text-[var(--color-text-muted)]'

function parseCloud(value: string | null): CloudProvider {
  return value === 'aws' || value === 'gcp' ? value : 'azure'
}

/**
 * Add a cloud (D45) — one namespace at a time, one credential per cloud.
 *
 * The API can only test a namespace it has stored, so this saves first and then tests, and says
 * plainly what it found. If the test fails the namespace is not left behind by surprise: the person
 * chooses to remove it and try again, or keep it and fix it later.
 */
export default function AddCloudModal({ close }: OverlayBodyProps) {
  const mayConnect = permission(useMe().data, 'Admin', 'connect a cloud')
  const [params] = useSearchParams()
  const [form, setForm] = useState<CloudForm>(() => emptyForm(parseCloud(params.get('cloud'))))
  const [step, setStep] = useState<Step>({ at: 'form' })
  const [reveal, setReveal] = useState(false)
  const [keyFileName, setKeyFileName] = useState<string | null>(null)

  const connect = useConnectNamespace()
  const test = useTestConnection()
  const remove = useRemoveNamespace()
  const { select } = useProviderScope()
  const navigate = useNavigate()

  const set = <K extends keyof CloudForm>(key: K, value: CloudForm[K]) => setForm((f) => ({ ...f, [key]: value }))

  function chooseCloud(cloud: CloudProvider) {
    setForm((f) => ({ ...emptyForm(cloud), displayName: f.displayName, environment: f.environment }))
    setKeyFileName(null)
    setStep({ at: 'form' })
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault()
    const built = buildConnectInput(form)
    if (!built.ok) return setStep({ at: 'form', problem: built.problem })

    setStep({ at: 'checking' })
    let namespace: Namespace
    try {
      namespace = await connect.mutateAsync(built.input)
    } catch (error) {
      return setStep({ at: 'form', problem: toProblem(error).message })
    }

    try {
      const result = await test.mutateAsync(namespace.id)
      if (!result.isConnected) {
        return setStep({ at: 'failed', namespace, reason: result.message })
      }
    } catch (error) {
      return setStep({ at: 'failed', namespace, reason: toProblem(error).message })
    }

    let found: string
    try {
      found = describeFound(await fetchNamespaceStats(namespace.id))
    } catch {
      found = 'Connected. ServiceHub could not list what is inside yet.'
    }
    setStep({ at: 'connected', namespace, found })
  }

  async function removeAndRetry(namespace: Namespace) {
    try {
      await remove.mutateAsync(namespace.id)
      setStep({ at: 'form' })
    } catch (error) {
      setStep({ at: 'failed', namespace, reason: toProblem(error).message })
    }
  }

  function openHome(provider: CloudProvider) {
    select(provider)
    close()
    navigate('/')
  }

  if (step.at === 'checking') {
    return <p role="status" className="py-6 text-center text-sm text-[var(--color-text-muted)]">Connecting and checking what ServiceHub can see…</p>
  }

  if (step.at === 'connected') {
    const { namespace, found } = step
    return (
      <div className="space-y-4">
        <div className="flex gap-3 rounded-xl border border-[var(--color-success)] bg-[var(--color-success-light)] p-4">
          <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0 text-[var(--color-success)]" aria-hidden="true" />
          <div className="space-y-1 text-sm text-[var(--color-text)]">
            <p className="font-semibold">Connected — ServiceHub can see {namespace.displayName ?? namespace.name}</p>
            <p>{found}</p>
            <p>{describeProof(namespace.capabilities)}</p>
          </div>
        </div>
        <p className="text-xs text-[var(--color-text-muted)]">Encrypted at rest on this server.</p>
        <div className="flex justify-end">
          <button type="button" onClick={() => openHome(namespace.provider)} className={primaryButton}>
            Open Home
          </button>
        </div>
      </div>
    )
  }

  if (step.at === 'failed') {
    const { namespace, reason } = step
    return (
      <div className="space-y-4">
        <div role="alert" className="flex gap-3 rounded-xl border border-[var(--color-warning)] bg-[var(--color-warning-light)] p-4">
          <TriangleAlert className="mt-0.5 h-5 w-5 shrink-0 text-[var(--color-warning)]" aria-hidden="true" />
          <div className="space-y-1 text-sm text-[var(--color-text)]">
            <p className="font-semibold">ServiceHub saved it but could not connect</p>
            <p>{reason}</p>
          </div>
        </div>
        <div className="flex justify-end gap-2">
          <button type="button" onClick={close} className={secondaryButton}>
            Keep it and fix it later
          </button>
          <button type="button" onClick={() => void removeAndRetry(namespace)} disabled={remove.isPending} className={primaryButton}>
            Remove it and try again
          </button>
        </div>
      </div>
    )
  }

  return (
    <form onSubmit={(e) => void submit(e)} className="space-y-4" noValidate>
      <div role="group" aria-label="Cloud" className="grid grid-cols-3 gap-1 rounded-lg bg-[var(--color-surface-muted)] p-1">
        {clouds.map((c) => (
          <button
            key={c.id}
            type="button"
            aria-pressed={form.cloud === c.id}
            onClick={() => chooseCloud(c.id)}
            className={`rounded-md px-2 py-1.5 text-sm ${form.cloud === c.id ? 'bg-[var(--color-surface)] font-medium shadow-sm' : 'text-[var(--color-text-muted)]'}`}
          >
            {c.label}
          </button>
        ))}
      </div>

      <Field label="Name" hint="What you'll see in ServiceHub.">
        {(id) => <input id={id} className={inputClass} value={form.displayName} onChange={(e) => set('displayName', e.target.value)} placeholder="orders-dev" autoComplete="off" />}
      </Field>

      <fieldset>
        <legend className="mb-1 text-sm font-medium text-[var(--color-text)]">Environment</legend>
        <div className="flex gap-4 text-sm">
          {environments.map((env) => (
            <label key={env.id} className="flex items-center gap-1.5">
              <input type="radio" name="environment" checked={form.environment === env.id} onChange={() => set('environment', env.id)} />
              {env.label}
            </label>
          ))}
        </div>
        {form.environment === 'prod' && (
          <p className="mt-1 text-xs text-[var(--color-text-muted)]">Production is watched only — every replay there needs a person.</p>
        )}
      </fieldset>

      {form.cloud === 'azure' && (
        <>
          <Field label="Connection string">
            {(id) => (
              <div className="flex gap-2">
                <input
                  id={id}
                  className={`${inputClass} font-mono`}
                  type={reveal ? 'text' : 'password'}
                  value={form.connectionString}
                  onChange={(e) => set('connectionString', e.target.value)}
                  placeholder="Endpoint=sb://…;SharedAccessKeyName=…;SharedAccessKey=…"
                  autoComplete="off"
                />
                <RevealButton reveal={reveal} onToggle={() => setReveal((r) => !r)} />
              </div>
            )}
          </Field>
          <Hint>Least privilege: a <b>Listen</b> policy is enough to watch. Replaying also needs <b>Send</b>.</Hint>
        </>
      )}

      {form.cloud === 'aws' && (
        <>
          <Field label="Access key ID">
            {(id) => <input id={id} className={`${inputClass} font-mono`} value={form.awsAccessKeyId} onChange={(e) => set('awsAccessKeyId', e.target.value)} placeholder="AKIAIOSFODNN7EXAMPLE" autoComplete="off" />}
          </Field>
          <Field label="Secret access key">
            {(id) => (
              <div className="flex gap-2">
                <input id={id} className={`${inputClass} font-mono`} type={reveal ? 'text' : 'password'} value={form.awsSecretAccessKey} onChange={(e) => set('awsSecretAccessKey', e.target.value)} autoComplete="off" />
                <RevealButton reveal={reveal} onToggle={() => setReveal((r) => !r)} />
              </div>
            )}
          </Field>
          <Field label="Region">
            {(id) => <input id={id} className={inputClass} value={form.awsRegion} onChange={(e) => set('awsRegion', e.target.value)} placeholder="us-east-1" autoComplete="off" />}
          </Field>
          <Hint>
            To watch: <code>sqs:ListQueues</code>, <code>sqs:GetQueueUrl</code>, <code>sqs:GetQueueAttributes</code>,{' '}
            <code>sqs:ReceiveMessage</code>. To replay also: <code>sqs:SendMessage</code>, <code>sqs:DeleteMessage</code>.
          </Hint>
        </>
      )}

      {form.cloud === 'gcp' && (
        <>
          <Field label="Project ID">
            {(id) => <input id={id} className={inputClass} value={form.gcpProjectId} onChange={(e) => set('gcpProjectId', e.target.value)} placeholder="my-project-123" autoComplete="off" />}
          </Field>
          <Field label="Service-account key file (.json)">
            {(id) => (
              <input
                id={id}
                type="file"
                accept=".json,application/json"
                className="text-sm"
                onChange={async (e) => {
                  const file = e.target.files?.[0]
                  if (!file) return
                  set('gcpServiceAccountJson', await file.text())
                  setKeyFileName(file.name)
                }}
              />
            )}
          </Field>
          {keyFileName && <p className="text-xs text-[var(--color-text-muted)]">Using {keyFileName}</p>}
          <Hint>
            To watch: <code>roles/pubsub.subscriber</code> and <code>roles/pubsub.viewer</code>. To replay also:{' '}
            <code>roles/pubsub.publisher</code>.
          </Hint>
        </>
      )}

      {step.problem && (
        <p role="alert" className="rounded-lg bg-[var(--color-error-light)] px-3 py-2 text-sm text-[var(--color-text)]">
          {step.problem}
        </p>
      )}

      <p className="text-xs text-[var(--color-text-muted)]">
        Encrypted at rest on this server. {providerLabel[form.cloud]} is checked as soon as you connect.
      </p>

      <div className="flex justify-end gap-2">
        <button type="button" onClick={close} className={secondaryButton}>
          Cancel
        </button>
        <button type="submit" className={primaryButton} disabled={!mayConnect.allowed}>
          Connect
        </button>
      </div>
      <NotAllowed reason={mayConnect.reason} />
    </form>
  )
}

const primaryButton =
  'rounded-lg bg-[var(--color-primary-600)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--color-primary-700)] disabled:opacity-50'
const secondaryButton =
  'rounded-lg border border-[var(--color-border)] px-4 py-2 text-sm font-medium text-[var(--color-text)] hover:bg-[var(--color-surface-muted)]'

function Field({ label, hint, children }: { label: string; hint?: string; children: (id: string) => ReactNode }) {
  const id = `field-${label.toLowerCase().replace(/[^a-z0-9]+/g, '-')}`
  return (
    <div>
      <label htmlFor={id} className="mb-1 block text-sm font-medium text-[var(--color-text)]">
        {label}
      </label>
      {children(id)}
      {hint && <p className="mt-1 text-xs text-[var(--color-text-muted)]">{hint}</p>}
    </div>
  )
}

function Hint({ children }: { children: ReactNode }) {
  return <p className="rounded-lg bg-[var(--color-surface-muted)] px-3 py-2 text-xs text-[var(--color-text-muted)]">{children}</p>
}

function RevealButton({ reveal, onToggle }: { reveal: boolean; onToggle: () => void }) {
  return (
    <button type="button" onClick={onToggle} aria-label={reveal ? 'Hide value' : 'Show value'} className="rounded-lg border border-[var(--color-border)] px-2.5">
      {reveal ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
    </button>
  )
}
