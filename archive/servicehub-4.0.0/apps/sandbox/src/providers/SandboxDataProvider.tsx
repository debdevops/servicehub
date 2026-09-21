import { createContext, useContext, useState, type ReactNode } from 'react';
import type { CloudProviderType } from '@servicehub/ui-shared/lib/api/types';

export interface SandboxDataContextValue {
  cloudProvider: CloudProviderType;
  setCloudProvider: (provider: CloudProviderType) => void;
}

const SandboxDataContext = createContext<SandboxDataContextValue | undefined>(undefined);

interface SandboxDataProviderProps {
  children: ReactNode;
  initialCloudProvider?: CloudProviderType;
}

/** Sandbox-owned data context — no dependency on apps/web's DemoContext. */
export function SandboxDataProvider({ children, initialCloudProvider = 'azure' }: SandboxDataProviderProps) {
  const [cloudProvider, setCloudProvider] = useState<CloudProviderType>(initialCloudProvider);

  return (
    <SandboxDataContext.Provider value={{ cloudProvider, setCloudProvider }}>
      {children}
    </SandboxDataContext.Provider>
  );
}

export function useSandboxData(): SandboxDataContextValue {
  const context = useContext(SandboxDataContext);
  if (!context) {
    throw new Error('useSandboxData must be used within a SandboxDataProvider');
  }
  return context;
}
