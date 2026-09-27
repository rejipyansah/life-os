import type { ProgressItem } from './homePrototypeData';

interface ProgressTierProps {
  items: ProgressItem[];
}

function TrendingUpIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <polyline points="23 6 13.5 15.5 8.5 10.5 1 18" />
      <polyline points="17 6 23 6 23 12" />
    </svg>
  );
}

function LeafIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M17 8C8 10 5.9 16.17 3.82 21.34l1.89.66.95-2.3c.48.17.98.3 1.34.3C19 20 22 3 22 3c-1 2-8 2.25-13 3.25S2 11.5 2 13.5s1.75 3.75 1.75 3.75" />
    </svg>
  );
}

export default function ProgressTier({ items }: ProgressTierProps) {
  if (items.length === 0) return null;

  return (
    <section className="bg-[#F7FAF5] rounded-xl p-5 md:p-6 lg:p-7 border border-[#D5E0D3] space-y-4">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <TrendingUpIcon className="w-4 h-4 text-[#336946] flex-shrink-0" />
          <h2 className="text-[11px] font-semibold uppercase tracking-wider text-lo-text-subtle">
            Progres Nyata
          </h2>
        </div>
        <span className="text-[11px] text-lo-text-subtle">Jangka panjang</span>
      </div>

      {items.map((item) => (
        <div key={item.id} className="space-y-3">
          <div className="flex items-center gap-1.5 text-[#336946]">
            <LeafIcon className="w-[18px] h-[18px]" />
            <span className="text-xs font-medium">{item.categoryLabel}</span>
          </div>
          <div className="space-y-1">
            <h3 className="font-headline text-lg md:text-xl text-[#18241D] font-medium leading-snug">
              {item.statement}
            </h3>
            <p className="font-body text-sm text-lo-text-subtle leading-relaxed">
              {item.detail}
            </p>
          </div>

          <div className="space-y-1.5">
            <div className="w-full h-2 rounded-full bg-[#E5EDE3] overflow-hidden">
              <div
                className="h-full bg-[#336946] rounded-full transition-all"
                style={{ width: `${item.percentage}%` }}
              />
            </div>
            <div className="flex justify-between items-center text-xs text-lo-text-subtle">
              <span>Terkumpul {item.currentAmount}</span>
              <span className="font-semibold text-[#18241D]">{item.percentage}%</span>
            </div>
          </div>
        </div>
      ))}
    </section>
  );
}
