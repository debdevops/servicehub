import type { ProviderStatusMap } from './api/cloudBridge';
import type { CloudProviderType, Namespace } from './api/types';

/**
 * The single derived model for "where does this provider stand for this
 * installation," composed client-side from data three endpoints already serve
 * (`provider-status`, `namespaces`) — no new network call, no new persisted state.
 *
 * - unavailable            — the provider's server-side flag is off (Azure never is).
 * - available-unconfigured — flag on, but this operator hasn't added a namespace yet.
 * - connected              — ≥1 namespace, none with a known-failed last connection test.
 * - connection-issue       — ≥1 namespace, ≥1 with `lastConnectionTestSucceeded === false`.
 *
 * Deliberately does not fold in "0 messages"/"0 DLQ" — empty data is not part of this
 * state machine, which is what keeps "not configured" from ever reading as "no data."
 */
export type ProviderInstallState =
  | 'unavailable'
  | 'available-unconfigured'
  | 'connected'
  | 'connection-issue';

function isProviderEnabled(
  providerStatus: ProviderStatusMap | undefined,
  provider: CloudProviderType,
): boolean {
  if (!providerStatus) return false;
  const key = provider.charAt(0).toUpperCase() + provider.slice(1).toLowerCase();
  return providerStatus[key] === true;
}

export function getProviderConnectionState(
  providerStatus: ProviderStatusMap | undefined,
  namespaces: Namespace[] | undefined,
  provider: CloudProviderType,
): ProviderInstallState {
  if (!isProviderEnabled(providerStatus, provider)) return 'unavailable';

  const nsForProvider = (namespaces ?? []).filter((ns) => ns.cloudProvider === provider);
  if (nsForProvider.length === 0) return 'available-unconfigured';

  const hasFailed = nsForProvider.some((ns) => ns.lastConnectionTestSucceeded === false);
  return hasFailed ? 'connection-issue' : 'connected';
}
