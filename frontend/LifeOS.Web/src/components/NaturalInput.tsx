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

function formatDate(dateStr: string | null): string {
  if (!dateStr) return '';
  const [y, m, d] = dateStr.split('-');
  return `${d}/${m}/${y}`;
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

  return (
    <section className="natural-input-section">
      <form className="natural-input-form" onSubmit={handleInterpret}>
        <input
          type="text"
          className="natural-input"
          placeholder="What happened?"
          value={input}
          onChange={e => setInput(e.target.value)}
          disabled={interpreting || submitting}
        />
        <button
          type="submit"
          className="btn btn-primary natural-input-btn"
          disabled={interpreting || submitting || !input.trim()}
        >
          {interpreting ? '...' : 'Go'}
        </button>
      </form>

      {!hasTransactions && !hintDismissed && !result && !error && (
        <div className="natural-input-hint">
          Try: &ldquo;Makan siang 50rb cash&rdquo;
        </div>
      )}

      {error && <div className="error-message" style={{ marginTop: 8 }}>{error}</div>}

      {result?.state === 'Ready' && result.preview && (
        <InterpretPreview
          data={result.preview}
          onConfirm={handleConfirm}
          onEdit={handleEdit}
          submitting={submitting}
        />
      )}

      {result?.state === 'NeedsClarification' && result.clarifications.length > 0 && (
        <div className="interpret-clarification">
          {result.clarifications.map((c, i) => (
            <p key={i}>{c}</p>
          ))}
          <p className="interpret-clarification-hint">Edit input dan tekan Go untuk mencoba lagi.</p>
          <button className="btn btn-text" onClick={handleClear}>Kembali</button>
        </div>
      )}

      {result?.state === 'Unsupported' && (
        <div className="interpret-unsupported">
          <p>Input ini bukan transaksi keuangan.</p>
          <button className="btn btn-text" onClick={handleClear}>Kembali</button>
        </div>
      )}
    </section>
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
  const typeLabel = data.type === 'Transfer' ? 'Transfer'
    : data.type === 'Income' ? 'Income'
    : 'Expense';

  return (
    <div className="interpret-preview">
      <div className="interpret-preview-row">
        <span className="interpret-preview-type">{typeLabel}</span>
        <span className="interpret-preview-amount">{formatCurrency(data.amount)}</span>
      </div>
      <div className="interpret-preview-details">
        {data.account && <span>{data.account}</span>}
        {data.type === 'Transfer' && data.toAccount && (
          <span> → {data.toAccount}</span>
        )}
        {data.date && <span className="interpret-preview-date">{formatDate(data.date)}</span>}
      </div>
      {data.description && (
        <div className="interpret-preview-desc">{data.description}</div>
      )}
      <div className="interpret-preview-actions">
        <button
          className="btn btn-primary"
          onClick={onConfirm}
          disabled={submitting}
        >
          {submitting ? 'Saving...' : 'Confirm'}
        </button>
        <button className="btn btn-secondary" onClick={onEdit} disabled={submitting}>
          Edit
        </button>
      </div>
    </div>
  );
}
