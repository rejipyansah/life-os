import { useState, useEffect } from 'react';
import { createGuestSession } from '../api';

interface EntryScreenProps {
  onEnter: (isGuest: boolean) => void;
  onRequestLogin: () => void;
}

const TYPING_TEXT = 'jajan 18rb cash';
const TYPING_SPEED = 70;
const TYPING_DELAY = 1200;

export default function EntryScreen({ onEnter, onRequestLogin }: EntryScreenProps) {
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const [displayedText, setDisplayedText] = useState('');
  const [showResult, setShowResult] = useState(false);
  const [typingDone, setTypingDone] = useState(false);

  useEffect(() => {
    let i = 0;
    let timeout: ReturnType<typeof setTimeout>;

    const typeNext = () => {
      if (i < TYPING_TEXT.length) {
        setDisplayedText(TYPING_TEXT.slice(0, i + 1));
        i++;
        timeout = setTimeout(typeNext, TYPING_SPEED);
      } else {
        setTypingDone(true);
        timeout = setTimeout(() => setShowResult(true), TYPING_DELAY);
      }
    };

    timeout = setTimeout(typeNext, 800);
    return () => clearTimeout(timeout);
  }, []);

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
    <div className="entry-screen">
      <header className="entry-header">
        <div className="entry-brand">
          <span className="entry-brand-name">Life OS</span>
          <span className="entry-brand-sub">sistem personal</span>
        </div>
        <button
          className="entry-login-link"
          onClick={onRequestLogin}
        >
          Masuk
        </button>
      </header>

      <div className="entry-body">
        <section className="entry-narrative">
          <div className="entry-narrative-content">
            <h1 className="entry-heading">
              Ceritakan apa yang terjadi.
              <br />
              Life OS merapikannya.
            </h1>
            <p className="entry-description">
              Bukan form berbelit atau dashboard rumit. Cukup ketik seperti
              caramu berbicara sehari-hari. Life OS memahami konteks, nominal,
              dan jalurnya secara otomatis.
            </p>

            <div className="entry-cta-group">
              {error && <div className="entry-error">{error}</div>}
              <button
                className="entry-cta"
                onClick={handleTryDemo}
                disabled={loading}
              >
                {loading ? 'Memulai...' : 'Coba Life OS'}
              </button>
              <span className="entry-cta-note">Tanpa registrasi atau formulir panjang.</span>
            </div>
          </div>
        </section>

        <section className="entry-stage">
          <div className="entry-stage-inner">
            <div className="entry-stage-label">
              <span>Natural Input</span>
              <span className="entry-stage-hint">
                {typingDone ? 'Dipahami otomatis' : 'Demonstrasi'}
              </span>
            </div>

            <div className="entry-instrument">
              <div className="entry-input-area">
                <span className="entry-input-label">Apa yang baru terjadi?</span>
                <div className="entry-input-phrase">
                  {displayedText}
                  <span className="entry-cursor" />
                </div>
              </div>

              <div className={`entry-result ${showResult ? 'visible' : ''}`}>
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

            <div className="entry-mobile-cta">
              {error && <div className="entry-error">{error}</div>}
              <button
                className="entry-cta"
                onClick={handleTryDemo}
                disabled={loading}
              >
                {loading ? 'Memulai...' : 'Coba Life OS'}
              </button>
              <span className="entry-cta-note">Tanpa registrasi atau formulir panjang.</span>
            </div>
          </div>
        </section>
      </div>
    </div>
  );
}
