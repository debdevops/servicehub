export function DemoHeader() {
  return (
    <header className="h-[var(--header-height)] shrink-0 flex items-center px-6 border-b border-gray-200 bg-white">
      <img src="/favicon.svg" alt="" className="h-5 w-5 mr-2" />
      <span className="font-semibold text-primary-700">ServiceHub Demo</span>
    </header>
  );
}
