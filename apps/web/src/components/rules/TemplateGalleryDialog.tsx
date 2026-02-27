import { X, Star, Zap } from 'lucide-react';
import { useRuleTemplates } from '@/hooks/useRules';
import type { RuleTemplateResponse } from '@/lib/api/rules';

interface TemplateGalleryDialogProps {
  open: boolean;
  onClose: () => void;
  onSelect: (template: RuleTemplateResponse) => void;
}

const categoryColors: Record<string, string> = {
  Transient: 'bg-green-100 text-green-700',
  MaxDelivery: 'bg-orange-100 text-orange-700',
  Expired: 'bg-yellow-100 text-yellow-700',
  ResourceNotFound: 'bg-blue-100 text-blue-700',
  QuotaExceeded: 'bg-red-100 text-red-700',
};

const categoryIcons: Record<string, string> = {
  Transient: '🟢',
  MaxDelivery: '🔶',
  Expired: '⏳',
  ResourceNotFound: '🔍',
  QuotaExceeded: '🚫',
};

export function TemplateGalleryDialog({ open, onClose, onSelect }: TemplateGalleryDialogProps) {
  const { data: templates, isLoading } = useRuleTemplates();

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-2xl max-h-[85vh] flex flex-col mx-4">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <div className="flex items-center gap-2">
            <Zap className="w-5 h-5 text-primary-500" />
            <h2 className="text-lg font-bold text-gray-900">Choose a Rule Template</h2>
          </div>
          <button
            onClick={onClose}
            className="p-1.5 hover:bg-gray-100 rounded-lg transition-colors"
          >
            <X className="w-5 h-5 text-gray-500" />
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto p-6 space-y-4">
          {isLoading ? (
            <div className="py-12 text-center text-sm text-gray-500">Loading templates...</div>
          ) : templates && templates.length > 0 ? (
            templates.map((template) => (
              <TemplateCard
                key={template.id}
                template={template}
                onSelect={() => onSelect(template)}
              />
            ))
          ) : (
            <div className="py-12 text-center text-sm text-gray-500">No templates available</div>
          )}
        </div>
      </div>
    </div>
  );
}

function TemplateCard({
  template,
  onSelect,
}: {
  template: RuleTemplateResponse;
  onSelect: () => void;
}) {
  const colorClass = categoryColors[template.category] ?? 'bg-gray-100 text-gray-700';
  const icon = categoryIcons[template.category] ?? '📋';

  return (
    <div className="border border-gray-200 rounded-xl p-4 hover:border-primary-300 hover:shadow-sm transition-all">
      <div className="flex items-start justify-between mb-2">
        <div className="flex items-center gap-2">
          <span className="text-lg">{icon}</span>
          <h3 className="text-sm font-bold text-gray-900">{template.name}</h3>
        </div>
        <span
          className={`px-2 py-0.5 rounded-full text-xs font-medium ${colorClass}`}
        >
          {template.category}
        </span>
      </div>

      <p className="text-sm text-gray-600 mb-3">{template.description}</p>

      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4 text-xs text-gray-500">
          <span>Used {template.usageCount} times</span>
          <span className="flex items-center gap-0.5">
            <Star className="w-3.5 h-3.5 text-amber-400 fill-amber-400" />
            {template.rating.toFixed(1)}
          </span>
        </div>
        <button
          onClick={onSelect}
          className="px-3 py-1.5 text-xs font-medium text-primary-700 bg-primary-50 border border-primary-200 rounded-lg hover:bg-primary-100 transition-colors"
        >
          Use This Template
        </button>
      </div>
    </div>
  );
}
