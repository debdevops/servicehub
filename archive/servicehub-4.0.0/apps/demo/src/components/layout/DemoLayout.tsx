import { Outlet } from 'react-router-dom';
import { DemoHeader } from './DemoHeader';
import { DemoSidebar } from './DemoSidebar';
import { DemoFooter } from './DemoFooter';

export function DemoLayout() {
  return (
    <div className="h-screen flex flex-col bg-gray-50">
      <DemoHeader />
      <div className="flex flex-1 overflow-hidden">
        <DemoSidebar />
        <main className="flex-1 overflow-auto">
          <Outlet />
        </main>
      </div>
      <DemoFooter />
    </div>
  );
}
