import { describe, it, expect } from 'vitest';
import { getProviderConnectionState } from '../../lib/providerConnectionState';
import type { ProviderStatusMap } from '../../lib/api/cloudBridge';
import type { Namespace } from '../../lib/api/types';

function ns(overrides: Partial<Namespace> & { id: string }): Namespace {
  return {
    name: overrides.id,
    isActive: true,
    createdAt: '2024-01-01T00:00:00Z',
    ...overrides,
  };
}

const allEnabled: ProviderStatusMap = { Azure: true, Aws: true, Gcp: true };

describe('getProviderConnectionState', () => {
  it('is unavailable when the provider flag is off', () => {
    const status: ProviderStatusMap = { Azure: true, Aws: false, Gcp: true };
    expect(getProviderConnectionState(status, [], 'aws')).toBe('unavailable');
  });

  it('is unavailable when provider status has not loaded yet (treated as off until known)', () => {
    expect(getProviderConnectionState(undefined, [], 'aws')).toBe('unavailable');
  });

  it('Azure is never unavailable given the real provider-status payload (always registered)', () => {
    expect(getProviderConnectionState(allEnabled, [], 'azure')).not.toBe('unavailable');
  });

  it('is available-unconfigured when enabled but no namespace exists for the provider', () => {
    const namespaces = [ns({ id: 'ns-1', cloudProvider: 'azure' })];
    expect(getProviderConnectionState(allEnabled, namespaces, 'aws')).toBe('available-unconfigured');
  });

  it('is available-unconfigured when namespaces is undefined', () => {
    expect(getProviderConnectionState(allEnabled, undefined, 'gcp')).toBe('available-unconfigured');
  });

  it('is connected when ≥1 namespace exists and none have a failed last test', () => {
    const namespaces = [
      ns({ id: 'ns-1', cloudProvider: 'aws', lastConnectionTestSucceeded: true }),
      ns({ id: 'ns-2', cloudProvider: 'aws', lastConnectionTestSucceeded: null }),
    ];
    expect(getProviderConnectionState(allEnabled, namespaces, 'aws')).toBe('connected');
  });

  it('is connected when lastConnectionTestSucceeded has never been recorded (null/undefined)', () => {
    const namespaces = [ns({ id: 'ns-1', cloudProvider: 'aws' })];
    expect(getProviderConnectionState(allEnabled, namespaces, 'aws')).toBe('connected');
  });

  it('is connection-issue when any namespace for the provider has a known-failed last test', () => {
    const namespaces = [
      ns({ id: 'ns-1', cloudProvider: 'aws', lastConnectionTestSucceeded: true }),
      ns({ id: 'ns-2', cloudProvider: 'aws', lastConnectionTestSucceeded: false }),
    ];
    expect(getProviderConnectionState(allEnabled, namespaces, 'aws')).toBe('connection-issue');
  });

  it('ignores namespaces belonging to other providers', () => {
    const namespaces = [
      ns({ id: 'ns-1', cloudProvider: 'azure', lastConnectionTestSucceeded: false }),
    ];
    expect(getProviderConnectionState(allEnabled, namespaces, 'gcp')).toBe('available-unconfigured');
  });

  it('never derives connected/connection-issue from data availability (0 messages is not part of this model)', () => {
    // The function only ever looks at namespace existence + last-test outcome — asserting
    // this by construction: no messageCount/dlqCount field exists on Namespace, so there is
    // nothing for the derivation to (mis)read as "empty" here.
    const namespaces = [ns({ id: 'ns-1', cloudProvider: 'aws', lastConnectionTestSucceeded: true })];
    expect(getProviderConnectionState(allEnabled, namespaces, 'aws')).toBe('connected');
  });
});
