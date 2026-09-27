import type { RecentMemoryItem } from './homePrototypeData';

interface RecentMemoryTierProps {
  items: RecentMemoryItem[];
  onNavigateToFinance: () => void;
}

function HistoryIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M3 3v5h5" />
      <path d="M3.05 13A9 9 0 1 0 6 5.3L3 8" />
      <path d="M12 7v5l4 2" />
    </svg>
  );
}

function SnackIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M18 8h1a4 4 0 0 1 0 8h-1" />
      <path d="M2 8h16v9a4 4 0 0 1-4 4H6a4 4 0 0 1-4-4V8z" />
      <line x1="6" y1="1" x2="6" y2="4" />
      <line x1="10" y1="1" x2="10" y2="4" />
      <line x1="14" y1="1" x2="14" y2="4" />
    </svg>
  );
}

function PaymentIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <rect x="1" y="4" width="22" height="16" rx="2" ry="2" />
      <line x1="1" y1="10" x2="23" y2="10" />
    </svg>
  );
}

function TransferIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <polyline points="17 1 21 5 17 9" />
      <path d="M3 11V9a4 4 0 0 1 4-4h14" />
      <polyline points="7 23 3 19 7 15" />
      <path d="M21 13v2a4 4 0 0 1-4 4H3" />
    </svg>
  );
}

const txIcons: Record<string, React.ComponentType<{ className?: string }>> = {
  'tx-1': SnackIcon,
  'tx-2': PaymentIcon,
  'tx-3': TransferIcon,
};

const txIconBg: Record<string, string> = {
  'tx-1': 'bg-[#E5EDE3] text-lo-text-subtle',
  'tx-2': 'bg-[#E1EBE0] text-[#336946]',
  'tx-3': 'bg-[#E5EDE3] text-lo-text-subtle',
};

export default function RecentMemoryTier({ items, onNavigateToFinance }: RecentMemoryTierProps) {
  if (items.length === 0) return null;

  return (
    <section className="bg-[#F7FAF5] rounded-xl p-5 md:p-6 lg:p-7 border border-[#D5E0D3]">
      <div className="flex items-center justify-between mb-4">
        <div className="flex items-center gap-2">
          <HistoryIcon className="w-4 h-4 text-[#336946] flex-shrink-0" />
          <h2 className="text-[11px] font-semibold uppercase tracking-wider text-lo-text-subtle">
            Memori Terkini
          </h2>
        </div>
        <button
          onClick={onNavigateToFinance}
          className="text-[11px] text-lo-text-subtle hover:text-[#18241D] transition-colors font-medium"
        >
          Semua aktivitas
        </button>
      </div>

      <div className="flex flex-col divide-y divide-[#D5E0D3]/60">
        {items.map((item) => {
          const Icon = txIcons[item.id] ?? SnackIcon;
          const iconBg = txIconBg[item.id] ?? 'bg-[#E5EDE3] text-lo-text-subtle';
          return (
            <div key={item.id} className="flex items-center justify-between py-3 first:pt-0 last:pb-0">
              <div className="flex items-center gap-3 min-w-0">
                <div className={`w-9 h-9 rounded-full flex items-center justify-center shrink-0 ${iconBg}`}>
                  <Icon className="w-[18px] h-[18px]" />
                </div>
                <div className="flex flex-col min-w-0">
                  <span className="font-headline text-sm font-medium text-[#18241D] truncate">
                    {item.description}
                  </span>
                  <span className="text-xs text-lo-text-subtle">
                    {item.dateLabel}
                  </span>
                </div>
              </div>
              <div className="flex flex-col items-end shrink-0 pl-3">
                <span
                  className={`text-sm font-medium ${
                    item.amountTone === 'positive'
                      ? 'text-[#336946] font-semibold'
                      : item.amountTone === 'negative'
                        ? 'text-[#18241D]'
                        : 'text-[#18241D]'
                  }`}
                >
                  {item.amount}
                </span>
                <span className="text-xs text-lo-text-subtle">
                  {item.metaLabel}
                </span>
              </div>
            </div>
          );
        })}
      </div>
    </section>
  );
}
