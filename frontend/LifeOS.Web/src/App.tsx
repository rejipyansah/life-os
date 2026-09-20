import { useState, useEffect } from 'react';
import { getAuthMe, resumeGuestSession, logout } from './api';
import EntryScreen from './components/EntryScreen';
import LoginScreen from './components/LoginScreen';
import FinanceOverview from './components/FinanceOverview';
import './App.css';

type AppState = 'checking' | 'entry' | 'login' | 'finance';

function getInitialState(): AppState {
  if (typeof window !== 'undefined' && sessionStorage.getItem('just_logged_out') === '1') {
    sessionStorage.removeItem('just_logged_out');
    return 'entry';
  }
  return 'checking';
}

function App() {
  const [state, setState] = useState<AppState>(getInitialState);
  const [isGuest, setIsGuest] = useState(false);
  const [loginOrigin, setLoginOrigin] = useState<'entry' | 'finance'>('entry');

  useEffect(() => {
    if (state !== 'checking') return;

    let cancelled = false;

    getAuthMe()
      .then(() => {
        if (!cancelled) {
          setState('finance');
          setIsGuest(false);
        }
      })
      .catch(() => {
        if (cancelled) return;
        resumeGuestSession()
          .then(() => {
            if (!cancelled) {
              setState('finance');
              setIsGuest(true);
            }
          })
          .catch(() => {
            if (!cancelled) {
              setState('entry');
            }
          });
      });

    return () => { cancelled = true; };
  }, [state]);

  const handleEnter = (guest: boolean) => {
    setIsGuest(guest);
    setState('finance');
  };

  const handleLogout = async () => {
    try {
      await logout();
    } catch {
      // proceed with local logout even if server call fails
    }
    sessionStorage.setItem('just_logged_out', '1');
    setIsGuest(false);
    setState('entry');
  };

  const handleRequestLogin = (origin: 'entry' | 'finance') => {
    setLoginOrigin(origin);
    setState('login');
  };

  if (state === 'checking') {
    return (
      <div className="app loading-screen">
        <div className="spinner" />
      </div>
    );
  }

  if (state === 'entry') {
    return (
      <div className="app">
        <EntryScreen
          onEnter={(guest) => handleEnter(guest)}
          onRequestLogin={() => handleRequestLogin('entry')}
        />
      </div>
    );
  }

  if (state === 'login') {
    return (
      <div className="app">
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

  return (
    <div className="app">
      <FinanceOverview isGuest={isGuest} onLogout={handleLogout} onRequestLogin={() => handleRequestLogin('finance')} />
    </div>
  );
}

export default App;
