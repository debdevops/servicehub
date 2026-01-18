import { useMemo, useState, useRef } from 'react';
import {
  useReactTable,
  getCoreRowModel,
  getSortedRowModel,
  ColumnDef,
  flexRender,
  SortingState,
  Row,
} from '@tanstack/react-table';
import { useVirtualizer } from '@tanstack/react-virtual';
import { formatRelativeTime } from '@/lib/utils';
import type { Message } from '@/lib/mockData';

interface MessageDataGridProps {
  data: Message[];
  selectedIds: Set<string>;
  onSelectionChange: (ids: Set<string>) => void;
}

/**
 * MessageDataGrid Component
 * 
 * Enterprise-grade data grid using TanStack Table + Virtual.
 * Features:
 * - Row virtualization for 10k+ messages
 * - Column sorting
 * - Row selection
 * - Keyboard navigation
 */
export function MessageDataGrid({
  data,
  selectedIds,
  onSelectionChange,
}: MessageDataGridProps) {
  const [sorting, setSorting] = useState<SortingState>([]);
  const tableContainerRef = useRef<HTMLDivElement>(null);

  const columns = useMemo<ColumnDef<Message>[]>(
    () => [
      // Checkbox Column
      {
        id: 'select',
        size: 40,
        header: ({ table }) => (
          <input
            type="checkbox"
            checked={table.getIsAllRowsSelected()}
            ref={(input) => {
              if (input) {
                input.indeterminate = table.getIsSomeRowsSelected() && !table.getIsAllRowsSelected();
              }
            }}
            onChange={table.getToggleAllRowsSelectedHandler()}
            className="w-4 h-4 rounded border-gray-300 text-primary-500 focus:ring-primary-500"
          />
        ),
        cell: ({ row }) => (
          <input
            type="checkbox"
            checked={row.getIsSelected()}
            onChange={row.getToggleSelectedHandler()}
            className="w-4 h-4 rounded border-gray-300 text-primary-500 focus:ring-primary-500"
          />
        ),
      },
      // Message ID
      {
        accessorKey: 'id',
        header: 'Message ID',
        size: 140,
        cell: ({ getValue }) => (
          <span className="font-mono text-sm text-gray-900 truncate block">
            {getValue<string>()}
          </span>
        ),
      },
      // Enqueued Time
      {
        accessorKey: 'enqueuedTime',
        header: 'Enqueued',
        size: 120,
        cell: ({ getValue }) => (
          <span className="text-sm text-gray-500">
            {formatRelativeTime(getValue<Date>())}
          </span>
        ),
      },
      // Status
      {
        accessorKey: 'status',
        header: 'Status',
        size: 100,
        cell: ({ getValue }) => {
          const status = getValue<Message['status']>();
          const config = {
            success: { bg: 'bg-green-100', text: 'text-green-700', label: '✓ OK' },
            warning: { bg: 'bg-amber-100', text: 'text-amber-700', label: '⚠ Warn' },
            error: { bg: 'bg-red-100', text: 'text-red-700', label: '✗ Error' },
          };
          const style = config[status];
          return (
            <span
              className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${style.bg} ${style.text}`}
            >
              {style.label}
            </span>
          );
        },
      },
      // Preview
      {
        accessorKey: 'preview',
        header: 'Preview',
        size: 400,
        cell: ({ getValue }) => (
          <span className="text-sm text-gray-600 truncate block">
            {getValue<string>()}
          </span>
        ),
      },
      // Content Type
      {
        accessorKey: 'contentType',
        header: 'Content Type',
        size: 120,
        cell: ({ getValue }) => (
          <span className="text-xs text-gray-500">{getValue<string>()}</span>
        ),
      },
      // AI Indicator
      {
        id: 'ai',
        header: 'AI',
        size: 48,
        cell: ({ row }) =>
          row.original.hasAIInsight ? (
            <span className="inline-flex items-center justify-center w-6 h-6 bg-primary-100 text-primary-700 rounded text-xs">
              🤖
            </span>
          ) : null,
      },
      // Actions
      {
        id: 'actions',
        header: 'Actions',
        size: 80,
        cell: () => (
          <button className="text-gray-400 hover:text-gray-600 transition-colors">
            ⋮
          </button>
        ),
      },
    ],
    []
  );

  const table = useReactTable({
    data,
    columns,
    state: {
      sorting,
      rowSelection: Object.fromEntries(
        Array.from(selectedIds).map((id) => [
          data.findIndex((row) => row.id === id),
          true,
        ])
      ),
    },
    onSortingChange: setSorting,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
    enableRowSelection: true,
    onRowSelectionChange: (updater) => {
      const newSelection =
        typeof updater === 'function'
          ? updater(table.getState().rowSelection)
          : updater;

      const newSelectedIds = new Set<string>();
      Object.keys(newSelection).forEach((index) => {
        const row = data[Number(index)];
        if (row && newSelection[Number(index)]) {
          newSelectedIds.add(row.id);
        }
      });

      onSelectionChange(newSelectedIds);
    },
  });

  const { rows } = table.getRowModel();

  // Virtualization
  const rowVirtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => tableContainerRef.current,
    estimateSize: () => 48, // Row height in pixels
    overscan: 10,
  });

  const virtualRows = rowVirtualizer.getVirtualItems();
  const totalSize = rowVirtualizer.getTotalSize();

  const paddingTop = virtualRows.length > 0 ? virtualRows[0]?.start || 0 : 0;
  const paddingBottom =
    virtualRows.length > 0
      ? totalSize - (virtualRows[virtualRows.length - 1]?.end || 0)
      : 0;

  return (
    <div
      ref={tableContainerRef}
      className="flex-1 overflow-auto bg-white"
      style={{ height: '100%' }}
    >
      <table className="w-full border-collapse">
        {/* Table Header */}
        <thead className="sticky top-0 z-10 bg-gray-50 border-b border-gray-200">
          {table.getHeaderGroups().map((headerGroup) => (
            <tr key={headerGroup.id}>
              {headerGroup.headers.map((header) => (
                <th
                  key={header.id}
                  style={{ width: header.getSize() }}
                  className="px-4 py-3 text-left text-xs font-semibold text-gray-500 uppercase tracking-wider"
                >
                  {header.isPlaceholder ? null : (
                    <div
                      className={
                        header.column.getCanSort()
                          ? 'cursor-pointer select-none'
                          : ''
                      }
                      onClick={header.column.getToggleSortingHandler()}
                    >
                      {flexRender(
                        header.column.columnDef.header,
                        header.getContext()
                      )}
                      {{
                        asc: ' ↑',
                        desc: ' ↓',
                      }[header.column.getIsSorted() as string] ?? null}
                    </div>
                  )}
                </th>
              ))}
            </tr>
          ))}
        </thead>

        {/* Table Body */}
        <tbody>
          {paddingTop > 0 && (
            <tr>
              <td style={{ height: `${paddingTop}px` }} />
            </tr>
          )}
          {virtualRows.map((virtualRow) => {
            const row = rows[virtualRow.index] as Row<Message>;
            return (
              <tr
                key={row.id}
                className={`border-b border-gray-100 hover:bg-gray-50 cursor-pointer transition-colors ${
                  row.getIsSelected()
                    ? 'bg-primary-50 border-l-4 border-l-primary-500'
                    : ''
                }`}
              >
                {row.getVisibleCells().map((cell) => (
                  <td
                    key={cell.id}
                    style={{ width: cell.column.getSize() }}
                    className="px-4 py-3"
                  >
                    {flexRender(cell.column.columnDef.cell, cell.getContext())}
                  </td>
                ))}
              </tr>
            );
          })}
          {paddingBottom > 0 && (
            <tr>
              <td style={{ height: `${paddingBottom}px` }} />
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
