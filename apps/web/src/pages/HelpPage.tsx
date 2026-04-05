import { useState, useMemo } from 'react';
import { Link } from 'react-router-dom';
import { Search, BookOpen, ChevronDown, ChevronRight, Play, HelpCircle } from 'lucide-react';
import { helpSections } from '@/lib/helpContent';
import { resetTour } from '@/components/help/GuidedTour';

/**
 * Help & Quick Reference page — searchable guide covering every feature,
 * Azure Service Bus concepts, and the guided tour.
 */
export function HelpPage() {
  const [searchQuery, setSearchQuery] = useState('');
  const [expandedSections, setExpandedSections] = useState<Set<string>>(
    new Set(helpSections.map((s) => s.id)),
  );

  const filteredSections = useMemo(() => {
    if (!searchQuery.trim()) return helpSections;
    const q = searchQuery.toLowerCase();
    return helpSections
      .map((section) => ({
        ...section,
        items: section.items.filter(
          (item) =>
            item.question.toLowerCase().includes(q) ||
            item.answer.toLowerCase().includes(q),
        ),
      }))
      .filter((section) => section.items.length > 0);
  }, [searchQuery]);

  const toggleSection = (id: string) => {
    setExpandedSections((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const handleStartTour = () => {
    resetTour();
    // Dispatch a custom event that MainLayout listens for
    window.dispatchEvent(new CustomEvent('servicehub:start-tour'));
  };

  return (
    <div className="h-full overflow-y-auto">
      <div className="max-w-3xl mx-auto px-6 py-8">
        {/* Header */}
        <div className="flex items-start justify-between mb-8">
          <div>
            <div className="flex items-center gap-3 mb-2">
              <div className="w-10 h-10 bg-primary-100 rounded-xl flex items-center justify-center">
                <BookOpen className="w-5 h-5 text-primary-600" />
              </div>
              <h1 className="text-2xl font-bold text-gray-900">Help & Quick Reference</h1>
            </div>
            <p className="text-sm text-gray-500 ml-[52px]">
              Everything you need to know about ServiceHub — search below or browse by topic.
            </p>
          </div>
          <button
            onClick={handleStartTour}
            className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-primary-700 bg-primary-50 hover:bg-primary-100 rounded-lg transition-colors border border-primary-200"
          >
            <Play className="w-4 h-4" />
            Take a Tour
          </button>
        </div>

        {/* Search */}
        <div className="relative mb-8">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input
            type="text"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="Search help topics…"
            className="w-full pl-10 pr-4 py-2.5 text-sm bg-white border border-gray-200 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-transparent shadow-sm"
          />
          {searchQuery && (
            <button
              onClick={() => setSearchQuery('')}
              className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
              aria-label="Clear search"
            >
              ×
            </button>
          )}
        </div>

        {/* No results */}
        {filteredSections.length === 0 && (
          <div className="text-center py-12">
            <HelpCircle className="w-10 h-10 text-gray-300 mx-auto mb-3" />
            <p className="text-sm text-gray-500">
              No results for "<span className="font-medium">{searchQuery}</span>"
            </p>
            <p className="text-xs text-gray-400 mt-1">Try a different search term.</p>
          </div>
        )}

        {/* Sections */}
        <div className="space-y-4">
          {filteredSections.map((section) => {
            const isExpanded = expandedSections.has(section.id);
            return (
              <div
                key={section.id}
                className="bg-white rounded-xl border border-gray-200 shadow-sm overflow-hidden"
              >
                {/* Section header */}
                <button
                  onClick={() => toggleSection(section.id)}
                  className="w-full flex items-center gap-3 px-5 py-4 text-left hover:bg-gray-50 transition-colors"
                >
                  <span className="text-lg">{section.icon}</span>
                  <span className="flex-1 text-sm font-semibold text-gray-900">
                    {section.title}
                  </span>
                  <span className="text-xs text-gray-400 mr-2">
                    {section.items.length} {section.items.length === 1 ? 'item' : 'items'}
                  </span>
                  {isExpanded ? (
                    <ChevronDown className="w-4 h-4 text-gray-400" />
                  ) : (
                    <ChevronRight className="w-4 h-4 text-gray-400" />
                  )}
                </button>

                {/* Items */}
                {isExpanded && (
                  <div className="border-t border-gray-100">
                    {section.items.map((item, idx) => (
                      <div
                        key={idx}
                        className={`px-5 py-3.5 ${
                          idx < section.items.length - 1 ? 'border-b border-gray-50' : ''
                        }`}
                      >
                        <h3 className="text-sm font-medium text-gray-800">{item.question}</h3>
                        <p className="mt-1 text-xs text-gray-600 leading-relaxed">
                          {item.answer}
                        </p>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            );
          })}
        </div>

        {/* Keyboard shortcuts */}
        <div className="mt-8 bg-gray-50 rounded-xl border border-gray-200 p-5">
          <h2 className="text-sm font-semibold text-gray-900 mb-3">Keyboard Shortcuts</h2>
          <div className="grid grid-cols-2 gap-2 text-xs">
            <div className="flex items-center justify-between bg-white px-3 py-2 rounded-lg">
              <span className="text-gray-600">Search messages</span>
              <kbd className="bg-gray-100 text-gray-700 px-1.5 py-0.5 rounded font-mono">⌘K</kbd>
            </div>
            <div className="flex items-center justify-between bg-white px-3 py-2 rounded-lg">
              <span className="text-gray-600">Refresh data</span>
              <kbd className="bg-gray-100 text-gray-700 px-1.5 py-0.5 rounded font-mono">⌘R</kbd>
            </div>
            <div className="flex items-center justify-between bg-white px-3 py-2 rounded-lg">
              <span className="text-gray-600">Close tour / popover</span>
              <kbd className="bg-gray-100 text-gray-700 px-1.5 py-0.5 rounded font-mono">Esc</kbd>
            </div>
            <div className="flex items-center justify-between bg-white px-3 py-2 rounded-lg">
              <span className="text-gray-600">Open help</span>
              <kbd className="bg-gray-100 text-gray-700 px-1.5 py-0.5 rounded font-mono">?</kbd>
            </div>
          </div>
        </div>

        {/* Footer */}
        <div className="mt-8 pb-4 flex flex-wrap items-center justify-between gap-3">
          <p className="text-xs text-gray-400">
            ServiceHub — Azure Service Bus Management Dashboard
          </p>
          <Link
            to="/security"
            className="text-xs text-green-600 hover:text-green-800 hover:underline font-medium"
          >
            Security &amp; Privacy →
          </Link>
        </div>
      </div>
    </div>
  );
}
