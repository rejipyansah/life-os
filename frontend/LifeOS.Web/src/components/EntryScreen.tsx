import { useState } from 'react';
import { createGuestSession } from '../api';
import BrandLogo from './BrandLogo';
import ObservationCard from './ObservationCard';

interface EntryScreenProps {
  onEnter: (isGuest: boolean) => void;
  onRequestLogin: () => void;
}

export default function EntryScreen({ onEnter, onRequestLogin }: EntryScreenProps) {
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const handleTryDemo = async () => {
    setLoading(true);
    setError('');
    try {
      await createGuestSession();
      onEnter(true);
    } catch {
      setError('Life OS sedang tidak dapat diakses. Coba lagi.');
    } finally {
      setLoading(false);
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
        <div className="w-full grid grid-cols-1 lg:grid-cols-12 gap-10 lg:gap-14 items-center my-auto">
          {/* Left column — narrative */}
          <div className="lg:col-span-7 flex flex-col justify-center space-y-6 lg:space-y-7">
            <BrandLogo size="md" showEdition />

            <div className="space-y-3.5 max-w-xl">
              <h1 className="font-headline text-3xl lg:text-4xl lg:leading-tight text-lo-primary font-medium tracking-tight">
                Teman yang mengingat perjalananmu.
              </h1>
              <p className="font-body text-base lg:text-lg text-[#3d5a49] leading-relaxed">
                Life OS menyimpan hal-hal kecil yang terjadi dalam hidupmu dan
                membantu menunjukkan apa yang berubah dari waktu ke waktu.
              </p>
            </div>

            <div className="space-y-4 pt-1">
              {error && (
                <div className="bg-lo-error-container text-lo-error px-4 py-3 rounded-xl text-sm text-center">
                  {error}
                </div>
              )}

              <div className="flex flex-wrap items-center gap-4">
                <button
                  onClick={handleTryDemo}
                  disabled={loading}
                  className="px-8 py-3.5 rounded-full bg-lo-primary text-lo-on-primary font-medium hover:bg-lo-primary-hover transition-all duration-200 inline-flex items-center gap-2 shadow-sm cursor-pointer tracking-wide text-sm lg:text-base group disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  {loading ? 'Memulai...' : 'Coba Life OS'}
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
                  onClick={onRequestLogin}
                  className="px-7 py-3.5 rounded-full border border-lo-outline-variant text-[#2d503d] font-medium hover:bg-[#e4ede3] transition-all duration-200 cursor-pointer text-sm lg:text-base"
                >
                  Masuk
                </button>
              </div>

              <div className="flex items-center gap-2 text-[#5f7a6b] pt-1">
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
                <p className="text-xs text-[#5f7a6b]">
                  Enkripsi privat lokal &middot; Data tersimpan aman di perangkatmu.
                </p>
              </div>
            </div>
          </div>

          {/* Right column — observation */}
          <div className="lg:col-span-5 flex flex-col justify-center relative">
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

      {/* Footer */}
      <footer className="w-full max-w-6xl mx-auto pb-6 flex items-center justify-between text-[#5E7064] text-xs font-body border-t border-[#d5e0d3]/40 pt-6">
        <span className="tracking-wide opacity-80">
          Designed &amp; built by Reji Pikriyansah
        </span>
      </footer>
    </main>
  );
}