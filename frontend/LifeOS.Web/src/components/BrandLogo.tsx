interface BrandLogoProps {
  className?: string;
  size?: 'sm' | 'md';
  showEdition?: boolean;
}

export default function BrandLogo({ className = '', size = 'md', showEdition = false }: BrandLogoProps) {
  const iconSize = size === 'sm' ? 'w-7 h-7' : 'w-8 h-8';
  const iconInner = size === 'sm' ? 'w-4 h-4' : 'w-[18px] h-[18px]';
  const textSize = size === 'sm' ? 'text-lg' : 'text-2xl';

  return (
    <div className={`flex items-center gap-3 ${className}`}>
      <span
        className={`inline-flex items-center justify-center ${iconSize} rounded-full bg-lo-primary text-lo-on-primary`}
      >
        <svg
          className={iconInner}
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden="true"
        >
          <path d="M18.178 8c5.096 0 5.096 8 0 8-5.095 0-7.133-8-12.739-8-4.806 0-4.806 8 0 8 5.606 0 7.644-8 12.739-8z" />
        </svg>
      </span>
      <span className={`font-headline ${textSize} tracking-tight text-lo-primary font-semibold`}>
        Life OS
      </span>
      {showEdition && (
        <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-[#e8f0e7] text-[#2d503d] text-xs font-medium border border-lo-border-hairline">
          <span className="w-1.5 h-1.5 rounded-full bg-[#2d503d]" aria-hidden="true" />
          Edisi Tenang
        </span>
      )}
    </div>
  );
}