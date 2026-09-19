import { useState } from 'react';
import type { AccountProjection, InterpretResponse, InterpretTransactionData } from '../types';
import { interpret, createTransaction } from '../api';

interface NaturalInputProps {
  accounts: AccountProjection[];
  hasTransactions: boolean;
  onSuccess: () => void;
}

function formatCurrency(amount: number): string {
  return `Rp ${amount.toLocaleString('id-ID', { minimumFractionDigits: 0, maximumFractionDigits: 0 })}`;
}

export default function NaturalInput({ accounts, hasTransactions, onSuccess }: NaturalInputProps) {
  const [input, setInput] = useState('');
  const [interpreting, setInterpreting] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [result, setResult] = useState<InterpretResponse | null>(null);
  const [error, setError] = useState('');
  const [hintDismissed, setHintDismissed] = useState(false);

  const activeAccounts = accounts.filter(a => !a.isArchived);

  const handleInterpret = async (e: React.FormEvent) => {
    e.preventDefault();
    const trimmed = input.trim();
    if (!trimmed) return;

    setInterpreting(true);
    setError('');
    setResult(null);

    try {
      const res = await interpret(trimmed);
      setResult(res);
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Gagal menginterpretasi input';
      if (msg.includes('503') || msg.includes('unavailable') || msg.includes('provider')) {
        setError('AI sedang tidak tersedia. Coba lagi beberapa saat.');
      } else {
        setError(msg);
      }
    } finally {
      setInterpreting(false);
    }
  };

  const handleConfirm = async () => {
    if (!result?.command) return;

    setSubmitting(true);
    setError('');

    try {
      await createTransaction(result.command);
      setInput('');
      setResult(null);
      setHintDismissed(true);
      onSuccess();
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Gagal membuat transaksi';
      setError(msg);
    } finally {
      setSubmitting(false);
    }
  };

  const handleEdit = () => {
    setResult(null);
    setError('');
  };

  const handleClear = () => {
    setInput('');
    setResult(null);
    setError('');
  };

  if (activeAccounts.length === 0) return null;

  const hasResult = result !== null || error !== '';

  return (
    <>
      <div className="natural-input-row">
        <input
          type="text"
          className="natural-field"
          placeholder="Misal: jajan 18rb cash, atau transfer 100k ke bca..."
          value={input}
          onChange={e => setInput(e.target.value)}
          disabled={interpreting || submitting}
        />
        <button
          type="button"
          className="btn-natural-submit"
          onClick={handleInterpret}
          disabled={interpreting || submitting || !input.trim()}
        >
          {interpreting ? '...' : 'Catat'}
        </button>
      </div>

      {!hasTransactions && !hintDismissed && !hasResult && (
        <div className="natural-input-hint">
          Try: &ldquo;Makan siang 50rb cash&rdquo;
        </div>
      )}

      {hasResult && (
        <div className="natural-result-zone">
          {error && <div className="res-error">{error}</div>}

          {result?.state === 'Ready' && result.preview && (
            <InterpretPreview
              data={result.preview}
              onConfirm={handleConfirm}
              onEdit={handleEdit}
              submitting={submitting}
            />
          )}

          {result?.state === 'NeedsClarification' && result.clarifications.length > 0 && (
            <div className="res-clarify">
              <div className="res-clarify-title">Perlu sedikit konfirmasi</div>
              {result.clarifications.map((c, i) => (
                <div key={i} className="res-clarify-sub">{c}</div>
              ))}
              <button className="btn-text" onClick={handleClear} style={{ marginTop: 8, padding: '4px 0', minHeight: 'auto', fontSize: 12 }}>Kembali</button>
            </div>
          )}

          {result?.state === 'Unsupported' && !error && (
            <div className="res-clarify">
              <div className="res-clarify-title">Belum terbaca</div>
              <div className="res-clarify-sub">Input ini bukan transaksi keuangan. Coba sebutkan nominal (misal: <em>kopi 22k</em>).</div>
              <button className="btn-text" onClick={handleClear} style={{ marginTop: 8, padding: '4px 0', minHeight: 'auto', fontSize: 12 }}>Kembali</button>
            </div>
          )}
        </div>
      )}
    </>
  );
}

function InterpretPreview({
  data,
  onConfirm,
  onEdit,
  submitting,
}: {
  data: InterpretTransactionData;
  onConfirm: () => void;
  onEdit: () => void;
  submitting: boolean;
}) {
  const metaParts: string[] = [];
  if (data.description) metaParts.push(data.description);
  if (data.account) metaParts.push(data.account);
  if (data.type === 'Transfer' && data.toAccount) metaParts.push(`→ ${data.toAccount}`);

  return (
    <div className="res-ready">
      <div className="res-ready-left">
        <span className="res-amount">
          {data.type === 'Income' ? '+' : data.type === 'Transfer' ? '' : '–'} {formatCurrency(data.amount)}
        </span>
        {metaParts.length > 0 && (
          <span className="res-meta">{metaParts.join(' · ')}</span>
        )}
      </div>
      <div className="res-actions">
        <button className="btn-res-cancel" onClick={onEdit} disabled={submitting}>
          Batal
        </button>
        <button className="btn-res-save" onClick={onConfirm} disabled={submitting}>
          {submitting ? 'Menyimpan...' : 'Simpan'}
        </button>
      </div>
    </div>
  );
}
