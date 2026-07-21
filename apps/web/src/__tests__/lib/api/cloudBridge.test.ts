import { describe, it, expect } from 'vitest';
import { getProviderCapabilities, type ProviderCapabilitiesMap } from '@/lib/api/cloudBridge';

const map: ProviderCapabilitiesMap = {
  Azure: { supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: false, supportsScheduledMessages: true, supportsRepeatablePeek: true, notes: 'azure-notes' },
  Aws: { supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: true, supportsScheduledMessages: false, supportsRepeatablePeek: false, notes: 'aws-notes' },
  Gcp: { supportsMessageCounts: false, supportsManualDeadLetter: false, supportsPurge: true, supportsScheduledMessages: false, supportsRepeatablePeek: true, notes: 'gcp-notes' },
};

describe('getProviderCapabilities', () => {
  it('translates the lowercase CloudProviderType into the map\'s PascalCase key', () => {
    expect(getProviderCapabilities(map, 'aws')).toBe(map.Aws);
    expect(getProviderCapabilities(map, 'gcp')).toBe(map.Gcp);
    expect(getProviderCapabilities(map, 'azure')).toBe(map.Azure);
  });

  it('returns undefined when the map is not loaded yet', () => {
    expect(getProviderCapabilities(undefined, 'aws')).toBeUndefined();
  });

  it('returns undefined when the provider is not specified', () => {
    expect(getProviderCapabilities(map, undefined)).toBeUndefined();
  });

  it('returns undefined for a provider not present in the map', () => {
    expect(getProviderCapabilities(map, 'kafka')).toBeUndefined();
  });
});
