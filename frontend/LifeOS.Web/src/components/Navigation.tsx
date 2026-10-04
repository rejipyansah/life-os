import { navItems, type NavDestination } from './Home/navItems';

interface NavigationProps {
  active: NavDestination;
  onNavigate: (dest: NavDestination) => void;
  brand?: string;
  showLogout?: boolean;
  onLogout?: () => void;
}

function BerandaIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M12 22V10" />
      <path d="M7 10c0-3 2.5-5 5-5s5 2 5 5" />
      <path d="M5 22h14" />
      <path d="M9 22v-5a3 3 0 0 1 6 0v5" />
      <circle cx="12" cy="6" r="1.5" fill="currentColor" stroke="none" />
    </svg>
  );
}

function WalletIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <rect x="2" y="6" width="20" height="14" rx="2" />
      <path d="M2 10h20" />
      <path d="M16 14a1 1 0 1 0 0-2 1 1 0 0 0 0 2z" fill="currentColor" stroke="none" />
    </svg>
  );
}

function BookIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M4 19V5a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v14" />
      <path d="M14 3v14a2 2 0 0 1-2 2H6" />
      <path d="M18 9v6a2 2 0 0 1-2 2H4" />
      <line x1="10" y1="7" x2="14" y2="7" />
      <line x1="10" y1="10" x2="14" y2="10" />
    </svg>
  );
}

function FlagIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z" />
      <line x1="4" y1="22" x2="4" y2="15" />
    </svg>
  );
}

const iconMap: Record<NavDestination, React.ComponentType<{ className?: string }>> = {
  beranda: BerandaIcon,
  keuangan: WalletIcon,
  jurnal: BookIcon,
  target: FlagIcon,
};

const logoutButtonClass =
  'px-3 py-1.5 rounded-lg text-sm font-medium text-lo-error hover:bg-lo-error/10 transition-colors cursor-pointer';

export default function Navigation({
  active,
  onNavigate,
  brand,
  showLogout,
  onLogout,
}: NavigationProps) {
  const showLogoutButton = Boolean(showLogout && onLogout);

  return (
    <>
      {/* Mobile: top bar (logo + logout) */}
      <nav className="md:hidden fixed top-0 left-0 right-0 z-50 h-12 bg-[#f5fbf4] border-b border-[#D5E0D3]/60">
        <div className="w-full h-full px-5 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <img src="/life-os-logo.png" alt="" className="w-5 h-5 rounded-full object-cover" />
            <span className="font-headline text-sm tracking-tight text-lo-primary font-semibold">
              {brand ?? 'Life OS'}
            </span>
          </div>
          {showLogoutButton && (
            <button type="button" onClick={onLogout} className={logoutButtonClass}>
              Keluar
            </button>
          )}
        </div>
      </nav>

      {/* Mobile: bottom tab bar */}
      <nav className="md:hidden fixed bottom-0 left-0 right-0 z-50 bg-[#f5fbf4] border-t border-[#D5E0D3]/60">
        <div className="flex items-center justify-around h-16 px-1">
          {navItems.map((item) => {
            const Icon = iconMap[item.id];
            const isActive = active === item.id;
            return (
              <button
                key={item.id}
                onClick={() => item.available && onNavigate(item.id)}
                disabled={!item.available}
                className={`flex flex-col items-center justify-center gap-1 min-w-[56px] min-h-[44px] transition-colors ${
                  isActive
                    ? 'text-lo-primary font-semibold'
                    : item.available
                      ? 'text-lo-text-subtle'
                      : 'text-lo-text-subtle/40 cursor-not-allowed'
                }`}
              >
                <Icon className="w-5 h-5" />
                <span className="text-[11px] font-medium leading-none">{item.label}</span>
              </button>
            );
          })}
        </div>
      </nav>

      {/* Desktop: top navigation bar */}
      <nav className="hidden md:flex fixed top-0 left-0 right-0 z-50 h-14 bg-[#f5fbf4] border-b border-[#D5E0D3]/60">
        <div className="w-full max-w-6xl mx-auto px-6 lg:px-12 flex items-center">
          <div className="flex items-center gap-6">
            <div className="flex items-center gap-2">
              <img src="/life-os-logo.png" alt="" className="w-6 h-6 rounded-full object-cover" />
              <span className="font-headline text-base tracking-tight text-lo-primary font-semibold">
                {brand ?? 'Life OS'}
              </span>
            </div>
            <div className="flex items-center gap-1">
              {navItems.map((item) => {
                const isActive = active === item.id;
                return (
                  <button
                    key={item.id}
                    onClick={() => item.available && onNavigate(item.id)}
                    disabled={!item.available}
                    className={`px-3 py-1.5 rounded-lg text-sm font-medium transition-colors ${
                      isActive
                        ? 'bg-lo-primary/10 text-lo-primary'
                        : item.available
                          ? 'text-lo-text-subtle hover:text-lo-on-surface hover:bg-lo-surface-container/60'
                          : 'text-lo-text-subtle/40 cursor-not-allowed'
                    }`}
                  >
                    {item.label}
                  </button>
                );
              })}
            </div>
          </div>
          {showLogoutButton && (
            <button type="button" onClick={onLogout} className={`${logoutButtonClass} ml-auto`}>
              Keluar
            </button>
          )}
        </div>
      </nav>
    </>
  );
}
