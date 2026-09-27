import type { ObservationData } from './homePrototypeData';

interface ObservationTierProps {
  data: ObservationData;
  onNavigateToFinance: () => void;
}

function EyeIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" />
      <circle cx="12" cy="12" r="3" />
    </svg>
  );
}

function ArrowDownIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <line x1="12" y1="5" x2="12" y2="19" />
      <polyline points="19 12 12 19 5 12" />
    </svg>
  );
}

function ArrowForwardIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <line x1="5" y1="12" x2="19" y2="12" />
      <polyline points="12 5 19 12 12 19" />
    </svg>
  );
}

function CheckCircleIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14" />
      <polyline points="22 4 12 14.01 9 11.01" />
    </svg>
  );
}

export default function ObservationTier({ data, onNavigateToFinance }: ObservationTierProps) {
  return (
    <article className="bg-[#F7FAF5] rounded-xl p-5 md:p-6 lg:p-7 border border-[#D5E0D3] relative overflow-hidden">
      <div className="flex items-center justify-between pb-4">
        <div className="flex items-center gap-2">
          <EyeIcon className="w-4 h-4 text-[#336946] flex-shrink-0" />
          <span className="text-[11px] font-semibold uppercase tracking-wider text-lo-text-subtle">
            {data.label}
          </span>
        </div>
        <span className="text-[11px] text-lo-text-subtle bg-[#E5EDE3] px-2.5 py-1 rounded-full font-medium">
          {data.period}
        </span>
      </div>

      <div className="space-y-2 my-4">
        <h2 className="font-headline text-xl md:text-[22px] lg:text-2xl text-[#18241D] font-medium leading-snug tracking-tight">
          {data.statement}
        </h2>
        <p className="font-body text-sm text-lo-text-subtle leading-relaxed">
          {data.detail}
        </p>
      </div>

      <div className="grid grid-cols-3 gap-3 py-4 my-2 bg-[#E5EDE3]/60 rounded-lg px-4">
        <div className="space-y-0.5">
          <span className="text-[11px] text-lo-text-subtle block font-medium">{data.currentMonth.label}</span>
          <span className="font-headline text-sm md:text-base text-[#18241D] font-medium">{data.currentMonth.amount}</span>
        </div>
        <div className="space-y-0.5">
          <span className="text-[11px] text-lo-text-subtle block font-medium">{data.previousMonth.label}</span>
          <span className="font-body text-sm md:text-base text-[#414843] font-medium">{data.previousMonth.amount}</span>
        </div>
        <div className="space-y-0.5">
          <span className="text-[11px] text-lo-text-subtle block font-medium">{data.difference.label}</span>
          <span className="font-headline text-sm md:text-base text-[#336946] font-semibold flex items-center gap-0.5">
            <ArrowDownIcon className="w-4 h-4" />
            {data.difference.value}
          </span>
        </div>
      </div>

      <div className="pt-4 flex items-center justify-between">
        <button
          onClick={onNavigateToFinance}
          className="inline-flex items-center gap-1.5 text-sm font-medium text-lo-primary hover:underline transition-colors group"
        >
          <span>{data.ctaLabel}</span>
          <ArrowForwardIcon className="w-4 h-4 transition-transform group-hover:translate-x-0.5" />
        </button>
        <span className="text-[11px] text-lo-text-subtle/80 flex items-center gap-1">
          <CheckCircleIcon className="w-3.5 h-3.5 text-[#336946]" />
          {data.statusLabel}
        </span>
      </div>
    </article>
  );
}
