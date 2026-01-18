import { useState } from 'react';
import { Copy, Check } from 'lucide-react';

// ============================================================================
// HeadersTab - Display message headers in a table
// ============================================================================

interface HeadersTabProps {
  headers: Record<string, string>;
}

function HeaderRow({ 
  name, 
  value, 
  isEven 
}: { 
  name: string; 
  value: string; 
  isEven: boolean 
}) {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch (err) {
      console.error('Failed to copy:', err);
    }
  };

  return (
    <tr className={isEven ? 'bg-gray-50' : 'bg-white'}>
      <td className="px-4 py-3 text-sm font-mono font-medium text-gray-700 whitespace-nowrap">
        {name}
      </td>
      <td className="px-4 py-3 text-sm font-mono text-gray-600">
        <div className="flex items-center gap-2 group">
          <span className="truncate max-w-md" title={value}>
            {value}
          </span>
          <button
            onClick={handleCopy}
            className="p-1 rounded opacity-0 group-hover:opacity-100 hover:bg-gray-200 transition-all"
            title="Copy value"
          >
            {copied ? (
              <Check size={14} className="text-green-600" />
            ) : (
              <Copy size={14} className="text-gray-400" />
            )}
          </button>
        </div>
      </td>
    </tr>
  );
}

export function HeadersTab({ headers }: HeadersTabProps) {
  const entries = Object.entries(headers);

  if (entries.length === 0) {
    return (
      <div className="p-4">
        <div className="flex flex-col items-center justify-center py-16 text-gray-500">
          <p className="text-lg font-medium">No Headers</p>
          <p className="text-sm text-gray-400 mt-1">
            This message has no custom headers
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="p-4">
      <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
        <table className="w-full">
          <thead className="bg-gray-100 border-b border-gray-200">
            <tr>
              <th className="px-4 py-3 text-left text-xs font-semibold text-gray-600 uppercase tracking-wider">
                Header Name
              </th>
              <th className="px-4 py-3 text-left text-xs font-semibold text-gray-600 uppercase tracking-wider">
                Value
              </th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {entries.map(([name, value], index) => (
              <HeaderRow
                key={name}
                name={name}
                value={value}
                isEven={index % 2 === 0}
              />
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
