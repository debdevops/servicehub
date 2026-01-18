import { AlertOctagon, AlertTriangle, BarChart3, Zap, Shield } from 'lucide-react';
import type { InsightCategory } from '@/lib/insightsMockData';

// ============================================================================
// InsightsSidebar - Category navigation for AI Insights Hub
// ============================================================================

interface InsightsSidebarProps {
  selectedCategory: InsightCategory;
  onSelectCategory: (category: InsightCategory) => void;
  categoryCounts: Record<InsightCategory, number>;
}

// Category icon mapping
const CATEGORY_ICONS = {
  critical: AlertOctagon,
  warnings: AlertTriangle,
  patterns: BarChart3,
  performance: Zap,
  security: Shield,
} as const;

// Category metadata
const CATEGORY_META: Record<InsightCategory, { label: string; description: string }> = {
  critical: {
    label: 'Critical Issues',
    description: 'Requires immediate attention',
  },
  warnings: {
    label: 'Warnings',
    description: 'Performance degradation',
  },
  patterns: {
    label: 'Patterns Detected',
    description: 'Recurring behaviors',
  },
  performance: {
    label: 'Performance',
    description: 'Optimization opportunities',
  },
  security: {
    label: 'Security',
    description: 'No issues detected',
  },
};

// Category colors
const CATEGORY_COLORS: Record<InsightCategory, { text: string; bg: string; count: string }> = {
  critical: {
    text: 'text-red-600',
    bg: 'bg-red-50',
    count: 'bg-red-100 text-red-700',
  },
  warnings: {
    text: 'text-amber-600',
    bg: 'bg-amber-50',
    count: 'bg-amber-100 text-amber-700',
  },
  patterns: {
    text: 'text-blue-600',
    bg: 'bg-blue-50',
    count: 'bg-blue-100 text-blue-700',
  },
  performance: {
    text: 'text-sky-700',
    bg: 'bg-sky-50',
    count: 'bg-sky-100 text-sky-700',
  },
  security: {
    text: 'text-green-600',
    bg: 'bg-green-50',
    count: 'bg-green-100 text-green-700',
  },
};

const CATEGORIES: InsightCategory[] = ['critical', 'warnings', 'patterns', 'performance', 'security'];

export function InsightsSidebar({
  selectedCategory,
  onSelectCategory,
  categoryCounts,
}: InsightsSidebarProps) {
  return (
    <div className="w-72 bg-white border-r border-gray-200 flex flex-col shrink-0">
      {/* Header */}
      <div className="p-4 border-b border-gray-200 bg-white">
        <h2 className="text-lg font-semibold text-gray-900">Issue Categories</h2>
        <p className="text-sm text-gray-500 mt-1">AI-detected patterns & anomalies</p>
      </div>

      {/* Category List */}
      <nav className="flex-1 overflow-y-auto p-2">
        {CATEGORIES.map((category) => {
          const isSelected = selectedCategory === category;
          const Icon = CATEGORY_ICONS[category];
          const meta = CATEGORY_META[category];
          const colors = CATEGORY_COLORS[category];
          const count = categoryCounts[category];

          return (
            <button
              key={category}
              onClick={() => onSelectCategory(category)}
              className={`
                w-full text-left px-3 py-3 rounded-lg mb-1 transition-all
                ${isSelected
                  ? `bg-gray-50 border border-gray-200`
                  : 'hover:bg-gray-50 border border-transparent'
                }
              `}
            >
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <Icon
                    size={20}
                    className={isSelected ? colors.text : 'text-gray-400'}
                  />
                  <div>
                    <div className={`text-sm font-medium ${isSelected ? colors.text : 'text-gray-900'}`}>
                      {meta.label}
                    </div>
                    <div className="text-xs text-gray-500">{meta.description}</div>
                  </div>
                </div>
                <span
                  className={`
                    px-2 py-0.5 rounded-full text-xs font-medium
                    ${isSelected ? colors.count : 'bg-gray-100 text-gray-600'}
                  `}
                >
                  {count}
                </span>
              </div>
            </button>
          );
        })}
      </nav>
    </div>
  );
}
