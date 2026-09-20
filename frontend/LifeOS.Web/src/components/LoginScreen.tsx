import { useState } from 'react';
import { login } from '../api';

interface LoginScreenProps {
  onLogin: () => void;
  onBack: () => void;
}

export default function LoginScreen({ onLogin, onBack }: LoginScreenProps) {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoading(true);
    setError('');
    try {
      await login(email, password);
      onLogin();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Login gagal');
      setLoading(false);
    }
  };

  return (
    <div className="entry-screen">
      <header className="entry-header">
        <div className="entry-brand">
          <span className="entry-brand-name">Life OS</span>
          <span className="entry-brand-sub">sistem personal</span>
        </div>
        <button
          className="entry-login-link"
          onClick={onBack}
          disabled={loading}
        >
          Kembali
        </button>
      </header>

      <div className="entry-body">
        <section className="entry-narrative">
          <div className="entry-narrative-content">
            <h1 className="entry-heading">
              Selamat datang kembali.
            </h1>
            <p className="entry-description">
              Masuk ke akun Life OS kamu.
            </p>

            <form className="entry-login-form" onSubmit={handleSubmit}>
              {error && <div className="entry-error">{error}</div>}
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
                className="entry-cta"
                disabled={loading}
              >
                {loading ? 'Masuk...' : 'Masuk'}
              </button>
            </form>
          </div>
        </section>

        <section className="entry-stage">
          <div className="entry-stage-inner">
            <div className="entry-stage-label">
              <span>Natural Input</span>
              <span className="entry-stage-hint">Demonstrasi</span>
            </div>

            <div className="entry-instrument">
              <div className="entry-input-area">
                <span className="entry-input-label">Apa yang baru terjadi?</span>
                <div className="entry-input-phrase">
                  jajan 18rb cash
                  <span className="entry-cursor" />
                </div>
              </div>

              <div className="entry-result visible">
                <div className="entry-result-main">
                  <div className="entry-result-amount-group">
                    <span className="entry-result-type">Pengeluaran</span>
                    <span className="entry-result-amount">Rp 18.000</span>
                  </div>
                  <div className="entry-result-detail">
                    <span className="entry-result-label">Tunai</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </section>
      </div>
    </div>
  );
}
