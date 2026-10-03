import { MainLayout } from '@/components/layout';
import { DemoModeProvider } from '@servicehub/ui-shared/lib/demo/DemoContext';

// Demo Layouts — MainLayout wrapped with DemoModeProvider for each cloud.
// Lazy-loaded to defer MainLayout and its dependencies from the initial bundle.
export function DemoAzureLayout() {
  return (
    <DemoModeProvider cloudProvider="azure">
      <MainLayout />
    </DemoModeProvider>
  );
}

export function DemoAwsLayout() {
  return (
    <DemoModeProvider cloudProvider="aws">
      <MainLayout />
    </DemoModeProvider>
  );
}

export function DemoGcpLayout() {
  return (
    <DemoModeProvider cloudProvider="gcp">
      <MainLayout />
    </DemoModeProvider>
  );
}

// Real app layout
export function AppLayout() {
  return <MainLayout />;
}
