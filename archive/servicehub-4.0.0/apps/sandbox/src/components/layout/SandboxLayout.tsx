import { Outlet } from 'react-router-dom';
import { SandboxHeader } from './SandboxHeader';
import { SandboxSidebar } from './SandboxSidebar';
import { SandboxFooter } from './SandboxFooter';

export function SandboxLayout() {
  return (
    <div className="h-screen flex flex-col bg-gray-50">
      <SandboxHeader />
      <div className="flex flex-1 overflow-hidden">
        <SandboxSidebar />
        <main className="flex-1 overflow-auto">
          <Outlet />
        </main>
      </div>
      <SandboxFooter />
    </div>
  );
}
