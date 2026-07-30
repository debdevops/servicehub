export interface NavItem {
  to: string;
  label: string;
}

export const NAV_ITEMS: NavItem[] = [
  { to: '/', label: 'Home' },
  { to: '/namespaces', label: 'Namespaces' },
  { to: '/queues', label: 'Queues' },
  { to: '/topics', label: 'Topics' },
  { to: '/about', label: 'About Sandbox' },
  { to: '/coming-soon', label: 'Coming Soon' },
];
