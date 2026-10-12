import { useState, useEffect } from 'react';

import { getAuthMe, GuestSessionExpiredError, logout, resumeGuestSession } from './api';

import EntryScreen from './components/EntryScreen';
import LoginScreen from './components/LoginScreen';
import FinancePage from './components/finance/FinancePage';
import HomeScreen from './components/HomeScreen';
import type { NavDestination } from './components/Home/navItems';

type AppState = 'checking' | 'entry' | 'login' | 'home' | 'finance';

function getInitialState(): AppState {
  if (
    typeof window !== 'undefined' &&
    sessionStorage.getItem('just_logged_out') === '1'
  ) {
    sessionStorage.removeItem('just_logged_out');
    return 'entry';
  }

  return 'checking';
}

function App() {
  const [state, setState] = useState<AppState>(getInitialState);
  const [isGuest, setIsGuest] = useState(false);
  const [guestSessionExpired, setGuestSessionExpired] = useState(false);
  const [loginOrigin, setLoginOrigin] = useState<'entry' | 'finance'>(
    'entry'
  );

  useEffect(() => {
    const handleGuestExpiry = () => {
      setGuestSessionExpired(true);
      setIsGuest(false);
      setState('entry');
    };
    window.addEventListener('lifeos:guest-session-expired', handleGuestExpiry);
    return () => window.removeEventListener('lifeos:guest-session-expired', handleGuestExpiry);
  }, []);

  useEffect(() => {
    if (state !== 'checking') return;

    let cancelled = false;

    getAuthMe()
      .then((auth) => {
        if (cancelled) return;

        if (auth) {
          setState('finance');
          setIsGuest(false);
          return;
        }

        return resumeGuestSession();
      })
      .then((guestResult) => {
        if (cancelled || !guestResult) return;

        setIsGuest(true);

        if (guestResult.isNew) {
          setState('entry');
        } else {
          setState('finance');
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setGuestSessionExpired(error instanceof GuestSessionExpiredError);
          setState('entry');
        }
      });

    return () => {
      cancelled = true;
    };
  }, [state]);

  const handleEnter = (guest: boolean) => {
    setGuestSessionExpired(false);
    setIsGuest(guest);
    setState('finance');
  };

  const handleLogout = async () => {
    try {
      await logout();
    } catch {
      // Cookie mungkin sudah expired; tetap anggap logout sukses.
    }
    sessionStorage.setItem('just_logged_out', '1');
    setGuestSessionExpired(false);
    setIsGuest(false);
    setState('entry');
  };

  const handleRequestLogin = (origin: 'entry' | 'finance') => {
    setLoginOrigin(origin);
    setState('login');
  };

  const handleHomeNavigate = (dest: NavDestination) => {
    if (dest === 'keuangan') {
      setState('finance');
    }
  };

  const handleFinanceBackToHome = () => {
    setState('finance');
  };

  const handleFinanceNavigate = (dest: NavDestination) => {
    if (dest === 'beranda') {
      setState('home');
    }
  };

  if (state === 'checking') {
    return (
      <div className="h-dvh flex flex-col items-center justify-center overflow-y-auto">
        <div className="w-6 h-6 border-[3px] border-[var(--color-border)] border-t-[var(--color-primary)] rounded-full animate-spin" />
      </div>
    );
  }

  if (state === 'entry') {
    return (
      <div className="h-dvh flex flex-col overflow-y-auto">
        <EntryScreen
          onEnter={(guest) => handleEnter(guest)}
          onRequestLogin={() => handleRequestLogin('entry')}
          guestSessionExpired={guestSessionExpired}
        />
      </div>
    );
  }

  if (state === 'login') {
    return (
      <div className="h-dvh flex flex-col overflow-y-auto">
        <LoginScreen
          onLogin={() => handleEnter(false)}
          onBack={() => {
            if (loginOrigin === 'entry') {
              setState('entry');
            } else {
              setIsGuest(true);
              setState('finance');
            }
          }}
        />
      </div>
    );
  }

  if (state === 'home') {
    return (
      <div className="h-dvh flex flex-col overflow-y-auto">
        <HomeScreen
          onNavigate={handleHomeNavigate}
          isGuest={isGuest}
          onLogout={handleLogout}
        />
      </div>
    );
  }

  return (
    <div className="h-dvh flex flex-col overflow-y-auto">
      <FinancePage
        isGuest={isGuest}
        onRequestLogin={() => handleRequestLogin('finance')}
        onBackToHome={handleFinanceBackToHome}
        onNavigate={handleFinanceNavigate}
        onLogout={handleLogout}
      />
    </div>
  );
}

export default App;
