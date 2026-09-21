import { createContext, useContext, useState, type ReactNode } from 'react';
import type { CloudProviderType } from '@servicehub/ui-shared/lib/api/types';

export interface DemoDataContextValue {
  cloudProvider: CloudProviderType;
  setCloudProvider: (provider: CloudProviderType) => void;
}

const DemoDataContext = createContext<DemoDataContextValue | undefined>(undefined);

interface DemoDataProviderProps {
  children: ReactNode;
  initialCloudProvider?: CloudProviderType;
}

/** Demo-owned data context — no dependency on apps/web's DemoContext. */
export function DemoDataProvider({ children, initialCloudProvider = 'azure' }: DemoDataProviderProps) {
  const [cloudProvider, setCloudProvider] = useState<CloudProviderType>(initialCloudProvider);

  return (
    <DemoDataContext.Provider value={{ cloudProvider, setCloudProvider }}>
      {children}
    </DemoDataContext.Provider>
  );
}

export function useDemoData(): DemoDataContextValue {
  const context = useContext(DemoDataContext);
  if (!context) {
    throw new Error('useDemoData must be used within a DemoDataProvider');
  }
  return context;
}
