import { useState } from 'react';
import type { PresentRealityItem } from './homePrototypeData';

interface PresentRealitiesTierProps {
  items: PresentRealityItem[];
  onNavigateToFinance: () => void;
}

function ClockIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <circle cx="12" cy="12" r="10" />
      <polyline points="12 6 12 12 16 14" />
    </svg>
  );
}

export default function PresentRealitiesTier({ items, onNavigateToFinance }: PresentRealitiesTierProps) {
  const [dismissed, setDismissed] = useState<Set<string>>(new Set());

  const visibleItems = items.filter((item) => !dismissed.has(item.id));

  if (visibleItems.length === 0) return null;

  const handleDismiss = (id: string) => {
    setDismissed((prev) => new Set(prev).add(id));
  };

  return (
    <section>
      <div className="flex items-center justify-between mb-3">
        <div className="flex items-center gap-2">
          <ClockIcon className="w-4 h-4 text-[#C77048] flex-shrink-0" />
          <h2 className="text-[11px] font-semibold uppercase tracking-wider text-lo-text-subtle">
            Penting Sekarang
          </h2>
        </div>
        <span className="text-[11px] text-lo-text-subtle">
          {visibleItems.length} item
        </span>
      </div>

      <div className="flex flex-col gap-2.5">
        {visibleItems.map((item) => (
          <PresentRealityCard
            key={item.id}
            item={item}
            onDismiss={() => handleDismiss(item.id)}
            onNavigateToFinance={onNavigateToFinance}
          />
        ))}
      </div>
    </section>
  );
}

function PresentRealityCard({
  item,
  onDismiss,
  onNavigateToFinance,
}: {
  item: PresentRealityItem;
  onDismiss: () => void;
  onNavigateToFinance: () => void;
}) {
  return (
    <div className="bg-[#F7FAF5] p-4 rounded-xl border border-[#D5E0D3] flex flex-col gap-3 transition-all hover:bg-[#eff5ee]">
      <div className="flex items-start justify-between gap-3">
        <div className="space-y-0.5 min-w-0">
          <span className="font-headline text-sm font-medium text-[#18241D] block truncate">
            {item.title}
          </span>
          <span className={`text-xs block ${item.statusTone === 'warning' ? 'text-[#C77048]' : 'text-lo-text-subtle'}`}>
            {item.subtitle}
          </span>
        </div>
        <div className="text-right space-y-0.5 shrink-0">
          <span className="font-headline text-sm font-medium text-[#18241D] block">
            {item.amount}
          </span>
          <span
            className={`inline-block px-2 py-0.5 rounded text-[10px] font-medium ${
              item.statusTone === 'warning'
                ? 'bg-[#ffdbd0] text-[#76321b]'
                : 'bg-[#b5f0c4] text-[#195130]'
            }`}
          >
            {item.statusLabel}
          </span>
        </div>
      </div>

      <div className="flex items-center justify-end gap-2 pt-2 border-t border-[#D5E0D3]/60">
        <button
          onClick={onDismiss}
          className="px-3 py-1 rounded-full text-xs font-medium text-lo-text-subtle hover:text-[#18241D] transition-colors"
        >
          {item.secondaryAction.label}
        </button>
        <button
          onClick={onNavigateToFinance}
          className={`px-3 py-1 rounded-full text-xs font-medium transition-colors ${
            item.primaryAction.variant === 'primary'
              ? 'bg-lo-primary text-lo-on-primary hover:opacity-85'
              : 'bg-[#E5EDE3] text-[#18241D] hover:bg-[#dee4dd]'
          }`}
        >
          {item.primaryAction.label}
        </button>
      </div>
    </div>
  );
}
