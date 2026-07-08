/**
 * DemoContext — Provider Pattern for Demo Mode
 *
 * When a user visits /demo/azure, /demo/aws, or /demo/gcp, this context
 * is mounted around the MainLayout. All data hooks check `isDemoMode` first
 * and return realistic mock data instead of calling the real API.
 *
 * This gives Demo mode the EXACT SAME UI as the real app — same pages,
 * same components, same routing — the ONLY difference is the data source.
 */

import { createContext, useContext, type ReactNode } from 'react';
import type { CloudProviderType } from '@/lib/api/types';

export interface DemoContextValue {
  /** Whether we are currently in demo mode */
  isDemoMode: boolean;
  /** Which cloud provider is being demonstrated */
  cloudProvider: CloudProviderType | null;
  /** Human-readable provider name */
  cloudProviderName: string;
  /** Scenario name shown in the demo banner */
  scenarioName: string;
  /** Accent color class for the banner */
  accentColor: string;
}

const DemoContext = createContext<DemoContextValue>({
  isDemoMode: false,
  cloudProvider: null,
  cloudProviderName: '',
  scenarioName: '',
  accentColor: '',
});

const PROVIDER_META: Record<
  CloudProviderType,
  { name: string; scenario: string; accent: string }
> = {
  azure: {
    name: 'Azure Service Bus',
    scenario: 'Contoso Commerce · contoso-prod-bus',
    accent: 'blue',
  },
  aws: {
    name: 'AWS SQS / SNS',
    scenario: 'AcmeRetail E-Commerce · acme-prod',
    accent: 'orange',
  },
  gcp: {
    name: 'GCP Pub/Sub',
    scenario: 'MedStream Healthcare · medstream-prod',
    accent: 'green',
  },
};

interface DemoModeProviderProps {
  cloudProvider: CloudProviderType;
  children: ReactNode;
}

/**
 * Wrap MainLayout children with this provider to activate demo mode.
 * All hooks that call the real API will short-circuit and return mock data.
 */
export function DemoModeProvider({ cloudProvider, children }: DemoModeProviderProps) {
  const meta = PROVIDER_META[cloudProvider];

  const value: DemoContextValue = {
    isDemoMode: true,
    cloudProvider,
    cloudProviderName: meta.name,
    scenarioName: meta.scenario,
    accentColor: meta.accent,
  };

  return <DemoContext.Provider value={value}>{children}</DemoContext.Provider>;
}

/**
 * Hook to read the demo context.
 * Safe to call in any component — returns isDemoMode=false outside provider.
 */
export function useDemoContext(): DemoContextValue {
  return useContext(DemoContext);
}
