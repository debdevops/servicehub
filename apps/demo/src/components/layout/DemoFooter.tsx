export function DemoFooter() {
  return (
    <footer className="h-10 shrink-0 bg-primary-500 text-white text-xs flex items-center justify-center">
      ServiceHub Demo v{import.meta.env.VITE_APP_VERSION}
    </footer>
  );
}
