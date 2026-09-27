interface ObservationCardProps {
  className?: string;
}

export default function ObservationCard({ className = '' }: ObservationCardProps) {
  return (
    <div
      className={`w-full space-y-5 ${className}`}
    >
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2 text-[#234B34]">
          <svg
            className="w-[18px] h-[18px] flex-shrink-0"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
          >
            <path d="M22 12h-4l-3 9L9 3l-3 9H2" />
          </svg>
          <span className="font-body text-xs uppercase tracking-wider font-semibold text-[#3C644E]">
            Pengamatan Bulan Ini
          </span>
        </div>
        <span className="font-body text-xs px-2.5 py-0.5 rounded-full bg-[#eef5ed] text-[#2C523C] font-medium border border-[#d5e0d3]">
          Bulan ini
        </span>
      </div>

      {/* Observation statement */}
      <div className="space-y-2">
        <h2 className="font-headline text-xl lg:text-2xl text-lo-primary tracking-tight leading-snug font-medium">
          Pengeluaranmu turun Rp450.000 dibanding bulan lalu.
        </h2>
        <p className="font-body text-sm text-lo-text-subtle leading-relaxed">
          Total pengeluaran tercatat Rp3.420.000, dibandingkan Rp3.870.000 pada bulan sebelumnya.
        </p>
      </div>

      {/* Desktop: full comparison box */}
      <div className="hidden lg:block rounded-2xl bg-[#f4f8f3] p-5 space-y-4 border border-[#dce6dc]">
        <div className="grid grid-cols-2 gap-4">
          <div className="space-y-1">
            <span className="text-[11px] text-lo-text-subtle uppercase tracking-wider font-semibold">
              Bulan ini
            </span>
            <div className="font-body text-2xl text-[#18241D] font-semibold tracking-tight">
              Rp3.420.000
            </div>
          </div>
          <div className="space-y-1">
            <span className="text-[11px] text-lo-text-subtle uppercase tracking-wider font-semibold">
              Bulan lalu
            </span>
            <div className="font-body text-2xl text-lo-text-subtle font-semibold tracking-tight opacity-75">
              Rp3.870.000
            </div>
          </div>
        </div>

        <div className="pt-1 flex items-center gap-2">
          <svg
            className="w-4 h-4 text-[#2d503d] flex-shrink-0"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
          >
            <rect x="3" y="11" width="18" height="11" rx="2" ry="2" />
            <path d="M7 11V7a5 5 0 0 1 10 0v4" />
          </svg>
          <p className="text-xs text-[#5f7a6b]">
            Enkripsi privat lokal &middot; Data tersimpan aman di perangkatmu.
          </p>
        </div>
      </div>

      {/* Mobile: compact comparison (stacked on small screens) */}
      <div className="lg:hidden flex flex-col gap-1.5 text-[12px] font-medium text-[#364A3E]">
        <span className="opacity-70">Perbandingan:</span>
        <span className="inline-flex items-center gap-1.5 font-semibold text-lo-primary bg-[#F4F9F2]/70 px-2.5 py-1 rounded-md border border-[#D1E1CE]/70 w-fit">
          Rp3.420.000{' '}
          <span className="text-[#2F6145]">&rarr;</span>{' '}
          Rp3.870.000
        </span>
      </div>
    </div>
  );
}