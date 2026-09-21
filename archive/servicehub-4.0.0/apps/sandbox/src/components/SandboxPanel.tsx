import type { ReactNode } from 'react';

interface SandboxPanelProps {
  title: string;
  children: ReactNode;
}

export function SandboxPanel({ title, children }: SandboxPanelProps) {
  return (
    <div className="bg-white border border-gray-200 rounded-lg shadow-sm">
      <div className="px-4 py-3 border-b border-gray-200">
        <h2 className="text-sm font-semibold text-gray-900">{title}</h2>
      </div>
      <div className="p-4">{children}</div>
    </div>
  );
}
