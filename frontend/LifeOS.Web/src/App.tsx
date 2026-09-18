import { useState, useEffect } from 'react';
import { getAuthMe } from './api';
import EntryScreen from './components/EntryScreen';
import FinanceOverview from './components/FinanceOverview';
import './App.css';

type AppState = 'checking' | 'entry' | 'finance';

function App() {
  const [state, setState] = useState<AppState>('checking');
  const [isGuest, setIsGuest] = useState(false);

  useEffect(() => {
    getAuthMe()
      .then(() => {
        setState('finance');
        setIsGuest(false);
      })
      .catch(() => {
        setState('entry');
      });
  }, []);

  const handleEnter = (guest: boolean) => {
    setIsGuest(guest);
    setState('finance');
  };

  const handleLogout = () => {
    setIsGuest(false);
    setState('entry');
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
        <EntryScreen onEnter={(guest) => handleEnter(guest)} />
      </div>
    );
  }

  return (
    <div className="app">
      <FinanceOverview isGuest={isGuest} onLogout={handleLogout} />
    </div>
  );
}

export default App;
