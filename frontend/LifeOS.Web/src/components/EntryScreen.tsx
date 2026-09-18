import { useState } from 'react';
import { createGuestSession, login } from '../api';

interface EntryScreenProps {
  onEnter: (isGuest: boolean) => void;
}

export default function EntryScreen({ onEnter }: EntryScreenProps) {
  const [mode, setMode] = useState<'idle' | 'login'>('idle');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const handleTryDemo = async () => {
    setLoading(true);
    setError('');
    try {
      await createGuestSession();
      onEnter(true);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to start demo');
    } finally {
      setLoading(false);
    }
  };

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoading(true);
    setError('');
    try {
      await login(email, password);
      onEnter(false);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Login failed');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="entry-screen">
      <div className="entry-card">
        <h1>Life OS</h1>
        <p className="entry-subtitle">Your life, organized.</p>

        {mode === 'idle' ? (
          <div className="entry-actions">
            <button
              className="btn btn-primary"
              onClick={handleTryDemo}
              disabled={loading}
            >
              {loading ? 'Starting...' : 'Try Demo'}
            </button>
            <button
              className="btn btn-secondary"
              onClick={() => setMode('login')}
              disabled={loading}
            >
              Owner Login
            </button>
          </div>
        ) : (
          <form className="login-form" onSubmit={handleLogin}>
            {error && <div className="error-message">{error}</div>}
            <input
              type="email"
              placeholder="Email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
              autoFocus
            />
            <input
              type="password"
              placeholder="Password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
            <button
              type="submit"
              className="btn btn-primary"
              disabled={loading}
            >
              {loading ? 'Logging in...' : 'Login'}
            </button>
            <button
              type="button"
              className="btn btn-text"
              onClick={() => { setMode('idle'); setError(''); }}
              disabled={loading}
            >
              Back
            </button>
          </form>
        )}
      </div>
    </div>
  );
}
