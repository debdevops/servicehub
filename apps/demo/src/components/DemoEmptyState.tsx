interface DemoEmptyStateProps {
  message: string;
}

export function DemoEmptyState({ message }: DemoEmptyStateProps) {
  return <div className="text-center py-10 text-sm text-gray-500">{message}</div>;
}
