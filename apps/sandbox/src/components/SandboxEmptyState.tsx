interface SandboxEmptyStateProps {
  message: string;
}

export function SandboxEmptyState({ message }: SandboxEmptyStateProps) {
  return <div className="text-center py-10 text-sm text-gray-500">{message}</div>;
}
