interface BrandLogoProps {
  className?: string;
  size?: 'sm' | 'md';
  showEdition?: boolean;
}

export default function BrandLogo({ className = '', size = 'md', showEdition = false }: BrandLogoProps) {
  const iconSize = size === 'sm' ? 'w-7 h-7' : 'w-8 h-8';
  const textSize = size === 'sm' ? 'text-lg' : 'text-2xl';

  return (
    <div className={`flex items-center gap-3 ${className}`}>
      <img
        src="/life-os-logo.png"
        alt="Life OS"
        className={`${iconSize} rounded-full object-cover`}
      />
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