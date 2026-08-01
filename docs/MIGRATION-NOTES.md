# Migration Notes — RC1

**No schema change and no data migration is required to upgrade to this release.** Stating this
explicitly and upfront: a release of this size will otherwise make an operator assume there's a
database step they need to run before restarting the service. There isn't one. Both persistence
stores — the JSON namespace file and the SQLite DLQ/audit/bulk-operations database — are read and
upgraded in place, automatically, the same way they always have been (see
`services/api/ARCHITECTURE.md` §16 for how the SQLite side does this). Just deploy the new build
and restart.

## Behavior changes to be aware of

- **Simulator backend has been removed.** The `ASPNETCORE_ENVIRONMENT=Simulator` backend and
  `docker-compose.yml`'s Simulator profile are no longer available. The zero-credential way to
  explore the UI is now the client-side Demo Mode (`/demo/azure`, `/demo/aws`, `/demo/gcp`) —
  fully functional, backend-free, and safe to use anywhere without credentials. See `docs/DEMO-MODE.md`.
- **AWS DLQ background monitoring is off unless opted in.** Background scanning (and "Scan Now")
  now skip AWS namespaces by default, returning a distinct "not monitored" state instead of an
  indistinguishable empty result. Set `DlqMonitor:AllowDestructivePeek:Aws=true` to opt back in.
  Why: every scan was a real SQS `ReceiveMessage` call, incrementing `ReceiveCount` and risking
  accidental dead-lettering. Azure and GCP are unaffected.
- **Restrictive CSP now applies to Staging, not just Production.** The security headers
  middleware is keyed on `IsDevelopment()`, not `IsProduction()` — previously any non-Production
  environment name risked getting the permissive development CSP by mistake. If you run a `Staging`
  environment and depended on the permissive policy there (e.g. for an in-browser tool that needs
  a looser CSP), you'll need to explicitly configure for that now.
- **Repeated authentication failures are now throttled.** A sliding-window lockout
  (`AuthFailureThrottle`, default: 10 failures / 5 minutes) now returns `429` on repeated invalid
  API-key attempts. If you have automation that retries with a bad key in a loop, it will start
  seeing `429`s where it previously saw `401`s.

## Configuration to review

- **`Swagger:Enabled` removal is a no-op.** That key never existed in any `appsettings*.json` and
  was never read by any code — Swagger/Scalar UI gating has always been `IsDevelopment()`-only. If
  you were setting `Swagger__Enabled` in your own environment, it had no effect before and has none
  now; safe to delete.
- **New flag: `DlqMonitor:AllowDestructivePeek:Aws`** (default `false`, absent from
  `appsettings.Production.json`). Only set to `true` after reading the AWS DLQ monitoring change
  above — this is the flag that re-enables the behavior that change turned off.
- **Check for leftover `SET_VIA_ENV_VAR` placeholders** in `appsettings.Production.json`:
  `ConnectionString`, `AllowedHosts`, `SiteUrl`, `EncryptionKey`,
  `Security:Authentication:...Secret`, `ScopedApiKeys[].Key`, `Security:Oidc:Authority`/`Audience`.
  Any of these left at the placeholder will fail fast or misbehave (e.g. an unset `SiteUrl` now
  falls back to the project's GitHub URL rather than emitting an invalid sitemap entry) — confirm
  your deployment sets real values for whichever of these apply to your configuration.

## Key rotation is still unsupported — do not attempt it as release hygiene

There is no rotate-and-re-encrypt tool. `Security__EncryptionKey` derives the AES-GCM key for
every stored connection string via HKDF/PBKDF2; changing it after connections have been saved makes
every previously stored connection string **permanently unreadable**, with no way to recover them
under the old key once it's discarded. This has not changed in this release and is not planned to
change — treat the encryption key as fixed for the lifetime of a deployment's stored data. Do not
rotate it as part of applying this or any other update.
