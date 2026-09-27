import { useState, useRef } from 'react';
import { login } from '../api';
import BrandLogo from './BrandLogo';
import ObservationCard from './ObservationCard';
import PasswordToggle from './PasswordToggle';

interface LoginScreenProps {
  onLogin: () => void;
  onBack: () => void;
}

export default function LoginScreen({ onLogin, onBack }: LoginScreenProps) {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const passwordRef = useRef<HTMLInputElement>(null);

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

  const handlePasswordToggle = (visible: boolean) => {
    if (passwordRef.current) {
      passwordRef.current.type = visible ? 'text' : 'password';
    }
  };

  return (
    <main
      className="w-full flex-1 min-h-0 flex flex-col justify-between px-6 py-6 md:px-10 md:py-8 lg:px-20 lg:py-10 antialiased"
      style={{
        background:
          'radial-gradient(90% 70% at 85% 45%, rgba(200, 224, 206, 0.45) 0%, rgba(245, 251, 244, 0.2) 65%), radial-gradient(120% 80% at 50% 0%, rgb(245, 251, 244) 0%, rgb(237, 245, 236) 55%, rgb(228, 238, 226) 100%)',
      }}
    >
      <div className="flex-1 w-full max-w-6xl mx-auto flex items-center justify-center">
        <div className="w-full grid grid-cols-1 lg:grid-cols-12 gap-8 lg:gap-16 items-start lg:items-center my-auto">
          {/* Left column — form */}
          <div className="lg:col-span-6 flex flex-col justify-center space-y-5">
            <BrandLogo size="md" showEdition />

            <div className="space-y-2 max-w-md">
              <h1 className="font-headline text-3xl lg:text-4xl text-lo-primary font-medium tracking-tight leading-snug">
                Masuk ke Life OS
              </h1>
              <p className="font-body text-base font-medium text-lo-primary/85 pt-1">
                Teman yang mengingat perjalananmu.
              </p>
              <p className="font-body text-sm text-[#3d5a49] leading-relaxed pt-1">
                Masuk untuk melanjutkan catatan dan melihat apa yang berubah
                dari waktu ke waktu.
              </p>
            </div>

            <form className="space-y-3 max-w-md w-full" onSubmit={handleSubmit}>
              {error && (
                <div className="bg-lo-error-container text-lo-error px-4 py-3 rounded-xl text-sm text-center">
                  {error}
                </div>
              )}

              <div className="space-y-2">
                <label
                  htmlFor="login-email"
                  className="block font-body text-sm font-medium text-lo-primary"
                >
                  Email
                </label>
                <input
                  id="login-email"
                  type="email"
                  autoComplete="email"
                  placeholder="nama@email.com"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  required
                  autoFocus
                  className="w-full bg-[#e8f1e7]/65 hover:bg-[#e4eee3]/85 text-lo-on-surface placeholder:text-lo-text-subtle/60 rounded-xl px-4 py-3 text-sm border border-lo-outline-variant outline-none transition-all duration-200 focus:bg-white focus:border-lo-primary focus:ring-1 focus:ring-lo-primary"
                />
              </div>

              <div className="space-y-2">
                <label
                  htmlFor="login-password"
                  className="block font-body text-sm font-medium text-lo-primary"
                >
                  Kata sandi
                </label>
                <div className="relative flex items-center">
                  <input
                    ref={passwordRef}
                    id="login-password"
                    type="password"
                    autoComplete="current-password"
                    placeholder="••••••••••••"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    required
                    className="w-full bg-[#e8f1e7]/65 hover:bg-[#e4eee3]/85 text-lo-on-surface placeholder:text-lo-text-subtle/60 rounded-xl pl-4 pr-11 py-3 text-sm border border-lo-outline-variant outline-none transition-all duration-200 focus:bg-white focus:border-lo-primary focus:ring-1 focus:ring-lo-primary"
                  />
                  <PasswordToggle onVisibilityChange={handlePasswordToggle} />
                </div>
              </div>

              <div className="flex items-center gap-4 pt-1">
                <button
                  type="submit"
                  disabled={loading}
                  className="px-8 py-3.5 rounded-full bg-lo-primary text-lo-on-primary font-medium hover:bg-lo-primary-hover transition-all duration-200 active:scale-[0.99] inline-flex items-center gap-2 shadow-sm cursor-pointer tracking-wide text-sm group disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  {loading ? 'Masuk...' : 'Masuk'}
                  {!loading && (
                    <svg
                      className="w-[18px] h-[18px] transition-transform duration-200 group-hover:translate-x-0.5"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2"
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      aria-hidden="true"
                    >
                      <path d="M5 12h14" />
                      <path d="m12 5 7 7-7 7" />
                    </svg>
                  )}
                </button>

                <button
                  type="button"
                  onClick={onBack}
                  disabled={loading}
                  className="px-7 py-3.5 rounded-full border border-lo-outline-variant text-[#2d503d] font-medium hover:bg-[#e4ede3] transition-all duration-200 cursor-pointer text-sm inline-flex items-center justify-center shadow-sm disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  Kembali
                </button>
              </div>
            </form>

            {/* Mobile privacy note — shown only when card's note is hidden */}
            <div className="lg:hidden flex items-center gap-2 text-xs text-[#5f7a6b] select-none pt-1">
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
              <span>Data tersimpan di perangkatmu secara privat.</span>
            </div>
          </div>

          {/* Right column — observation */}
          <div className="lg:col-span-6 flex justify-center lg:justify-end">
            <div className="w-full max-w-md relative">
              <div className="absolute -inset-4 bg-gradient-to-tr from-[#d3e5d3]/40 to-[#c8e0cc]/30 rounded-3xl filter blur-xl -z-10" />
              <div
                className="w-full rounded-3xl p-7 lg:p-8 shadow-sm border border-[#d2ded1] relative overflow-hidden"
                style={{
                  background:
                    'linear-gradient(165deg, rgba(255,255,255,0.92) 0%, rgba(243,248,241,0.95) 100%)',
                }}
              >
                <ObservationCard />
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Footer */}
      <footer className="w-full max-w-6xl mx-auto pb-2 flex items-center justify-between text-[#5E7064] text-xs font-body border-t border-[#d5e0d3]/40 pt-6">
        <span className="tracking-wide opacity-80">
          Designed &amp; built by Reji Pikriyansah
        </span>
      </footer>
    </main>
  );
}