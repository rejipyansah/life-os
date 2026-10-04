import { useState } from 'react';
import HomeHeader from './Home/HomeHeader';
import ObservationTier from './Home/ObservationTier';
import PresentRealitiesTier from './Home/PresentRealitiesTier';
import ProgressTier from './Home/ProgressTier';
import RecentMemoryTier from './Home/RecentMemoryTier';
import {
  prototypeObservation,
  prototypePresentRealities,
  prototypeProgress,
  prototypeRecentMemory,
} from './Home/homePrototypeData';
import type { NavDestination } from './Home/navItems';
import Navigation from './Navigation';
import AppFooter from './AppFooter';

interface HomeScreenProps {
  onNavigate: (dest: NavDestination) => void;
  isGuest?: boolean;
  onLogout?: () => void;
}

export default function HomeScreen({ onNavigate, isGuest, onLogout }: HomeScreenProps) {
  const [activeNav, setActiveNav] = useState<NavDestination>('beranda');

  const handleNavigate = (dest: NavDestination) => {
    setActiveNav(dest);
    onNavigate(dest);
  };

  return (
    <div className="min-h-screen bg-[#f5fbf4]">
      <Navigation
        active={activeNav}
        onNavigate={handleNavigate}
        showLogout={!isGuest}
        onLogout={onLogout}
      />

      {/* Mobile: single column */}
      <main className="md:hidden pt-16 pb-24 min-h-screen bg-[#f5fbf4]">
        <div className="flex flex-col w-full px-5 pb-8">
          <HomeHeader className="pt-4 pb-5" />

          <div className="flex flex-col gap-6">
            <ObservationTier
              data={prototypeObservation}
              onNavigateToFinance={() => handleNavigate('keuangan')}
            />
            <PresentRealitiesTier items={prototypePresentRealities} onNavigateToFinance={() => handleNavigate('keuangan')} />
            <ProgressTier items={prototypeProgress} />
            <RecentMemoryTier
              items={prototypeRecentMemory}
              onNavigateToFinance={() => handleNavigate('keuangan')}
            />
          </div>
        </div>
      </main>

      {/* Desktop: two-column editorial grid */}
      <main className="hidden md:block pt-14 min-h-screen bg-[#f5fbf4]">
        <div className="w-full max-w-6xl mx-auto px-6 lg:px-12 py-8 lg:py-10">
          <HomeHeader className="mb-8 lg:mb-10" />

          <div className="grid grid-cols-1 lg:grid-cols-12 gap-x-12 gap-y-8 items-start">
            {/* Left column: Tier 1 (Observation) + Tier 3 (Progress) */}
            <div className="lg:col-span-7 flex flex-col gap-6">
              <ObservationTier
                data={prototypeObservation}
                onNavigateToFinance={() => handleNavigate('keuangan')}
              />
              <ProgressTier items={prototypeProgress} />
            </div>

            {/* Right column: Tier 2 (Present Realities) + Tier 4 (Recent Memory) */}
            <div className="lg:col-span-5 flex flex-col gap-6">
              <PresentRealitiesTier items={prototypePresentRealities} onNavigateToFinance={() => handleNavigate('keuangan')} />
              <RecentMemoryTier
                items={prototypeRecentMemory}
                onNavigateToFinance={() => handleNavigate('keuangan')}
              />
            </div>
          </div>
        </div>
      </main>

      <AppFooter />
    </div>
  );
}
