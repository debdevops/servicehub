import { NavLink } from 'react-router-dom';
import { NAV_ITEMS } from '@/config/navigation';

export function SandboxSidebar() {
  return (
    <aside className="w-48 shrink-0 bg-white border-r border-gray-200 py-4 px-2 flex flex-col gap-1">
      {NAV_ITEMS.map(({ to, label }) => (
        <NavLink
          key={to}
          to={to}
          end={to === '/'}
          className={({ isActive }) =>
            `px-3 py-2 rounded-lg text-sm transition-colors ${
              isActive ? 'bg-primary-50 text-primary-700 font-medium' : 'text-gray-600 hover:bg-gray-50'
            }`
          }
        >
          {label}
        </NavLink>
      ))}
    </aside>
  );
}
