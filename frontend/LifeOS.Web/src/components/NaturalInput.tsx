import { useState, useRef, useEffect, useCallback } from 'react';
import type { AccountProjection, InterpretResponse, InterpretTransactionData, InterpretAllocationData } from '../types';
import { interpret, createTransaction, createAllocation } from '../api';

interface NaturalInputProps {
  accounts: AccountProjection[];
  onSuccess: () => void;
}

function formatCurrency(amount: number): string {
  return `Rp ${amount.toLocaleString('id-ID', { minimumFractionDigits: 0, maximumFractionDigits: 0 })}`;
}

export default function NaturalInput({ accounts, onSuccess }: NaturalInputProps) {
  const [input, setInput] = useState('');
  const [interpreting, setInterpreting] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [result, setResult] = useState<InterpretResponse | null>(null);
  const [error, setError] = useState('');
  const [successMessage, setSuccessMessage] = useState('');
  const inputRef = useRef<HTMLInputElement>(null);
  const resultZoneRef = useRef<HTMLDivElement>(null);

  const activeAccounts = accounts.filter(a => !a.isArchived);

  useEffect(() => {
    if (successMessage) {
      const timer = setTimeout(() => setSuccessMessage(''), 2500);
      return () => clearTimeout(timer);
    }
  }, [successMessage]);

  const focusInput = useCallback(() => {
    requestAnimationFrame(() => {
      if (inputRef.current) {
        inputRef.current.focus();
        const len = inputRef.current.value.length;
        inputRef.current.setSelectionRange(len, len);
      }
    });
  }, []);

  const handleInterpret = async (e: React.FormEvent) => {
    e.preventDefault();
    const trimmed = input.trim();
    if (!trimmed || interpreting || submitting) return;

    setInterpreting(true);
    setError('');
    setResult(null);
    setSuccessMessage('');

    try {
      const res = await interpret(trimmed);
      setResult(res);
    } catch (err) {
      const msg = err instanceof Error ? err.message : '';
      if (msg.includes('503') || msg.includes('unavailable') || msg.includes('provider')) {
        setError('AI sedang tidak tersedia. Coba lagi beberapa saat.');
      } else {
        setError('Terjadi gangguan. Silakan coba lagi.');
      }
    } finally {
      setInterpreting(false);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      if (input.trim() && !interpreting && !submitting) {
        handleInterpret(e as unknown as React.FormEvent);
      }
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
      setSuccessMessage('Transaksi tersimpan');
      onSuccess();
    } catch (err) {
      const msg = err instanceof Error ? err.message : '';
      if (msg.includes('503') || msg.includes('unavailable') || msg.includes('provider')) {
        setError('AI sedang tidak tersedia. Coba lagi beberapa saat.');
      } else {
        setError('Gagal menyimpan. Silakan coba lagi.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  const handleConfirmAllocation = async () => {
    if (!result?.allocationCommand) return;

    setSubmitting(true);
    setError('');

    try {
      await createAllocation(result.allocationCommand);
      setInput('');
      setResult(null);
      setSuccessMessage('Alokasi tersimpan');
      onSuccess();
    } catch (err) {
      const msg = err instanceof Error ? err.message : '';
      if (msg.includes('503') || msg.includes('unavailable') || msg.includes('provider')) {
        setError('AI sedang tidak tersedia. Coba lagi beberapa saat.');
      } else {
        setError('Gagal menyimpan. Silakan coba lagi.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  const handleEdit = () => {
    setResult(null);
    setError('');
    focusInput();
  };

  const handleClear = () => {
    setInput('');
    setResult(null);
    setError('');
    setSuccessMessage('');
  };

  const handleRetry = () => {
    setError('');
    setResult(null);
  };

  if (activeAccounts.length === 0) return null;

  const hasResult = interpreting || result !== null || error !== '' || successMessage !== '';

  return (
    <>
      <div className="natural-input-row">
        <input
          ref={inputRef}
          type="text"
          className="natural-field"
          placeholder="Misal: jajan 18rb cash, atau transfer 100k ke bca..."
          value={input}
          onChange={e => setInput(e.target.value)}
          onKeyDown={handleKeyDown}
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

      {hasResult && (
        <div className="natural-result-zone" ref={resultZoneRef}>

          {interpreting && (
            <div className="res-processing">
              <span className="res-processing-spinner" />
              <span className="res-processing-text">Memproses...</span>
            </div>
          )}

          {successMessage && (
            <div className="res-success">
              <span className="res-success-check">✓</span>
              {successMessage}
            </div>
          )}

          {error && !interpreting && (
            <div className="res-error-row">
              <div className="res-error">{error}</div>
              <button className="btn-res-retry" onClick={handleRetry}>Coba lagi</button>
            </div>
          )}

          {!interpreting && result?.state === 'Ready' && result.preview && (
            <InterpretPreview
              data={result.preview}
              onConfirm={handleConfirm}
              onEdit={handleEdit}
              submitting={submitting}
            />
          )}

          {!interpreting && result?.state === 'Ready' && result.allocationPreview && (
            <AllocationPreview
              data={result.allocationPreview}
              onConfirm={handleConfirmAllocation}
              onEdit={handleEdit}
              submitting={submitting}
            />
          )}

          {!interpreting && result?.state === 'NeedsClarification' && result.clarifications.length > 0 && (
            <div className="res-clarify">
              <div className="res-clarify-header">
                <span className="res-clarify-icon">?</span>
                <span className="res-clarify-title">Masih kurang sedikit</span>
              </div>
              <div className="res-clarify-list">
                {result.clarifications.map((c, i) => (
                  <div key={i} className="res-clarify-item">{c}</div>
                ))}
              </div>
              <button className="btn-res-cancel" onClick={handleEdit} style={{ marginTop: 8 }}>Edit</button>
            </div>
          )}

          {!interpreting && result?.state === 'Unsupported' && !error && (
            <div className="res-unsupported">
              <div className="res-unsupported-text">
                Input ini belum terbaca sebagai transaksi. Coba sebutkan nominal, misal: <em>kopi 22k</em>.
              </div>
              <button className="btn-res-cancel" onClick={handleClear}>Kembali</button>
            </div>
          )}
        </div>
      )}
    </>
  );
}

function PreviewRow({ label, value, bold }: { label: string; value: string; bold?: boolean }) {
  return (
    <div className="res-preview-row">
      <span className="res-preview-label">{label}</span>
      <span className={`res-preview-value${bold ? ' res-preview-value-bold' : ''}`}>{value}</span>
    </div>
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
  const rows: { label: string; value: string; bold?: boolean }[] = [];

  if (data.type === 'Transfer') {
    rows.push({ label: 'Nominal', value: formatCurrency(data.amount), bold: true });
    if (data.account) rows.push({ label: 'Dari', value: data.account });
    if (data.toAccount) rows.push({ label: 'Ke', value: data.toAccount });
  } else {
    if (data.description) rows.push({ label: 'Deskripsi', value: data.description });
    rows.push({ label: 'Nominal', value: formatCurrency(data.amount), bold: true });
    if (data.account) rows.push({ label: 'Akun', value: data.account });
  }

  return (
    <div className="res-ready">
      <div className="res-preview-fields">
        {rows.map((row, i) => (
          <PreviewRow key={i} label={row.label} value={row.value} bold={row.bold} />
        ))}
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

function AllocationPreview({
  data,
  onConfirm,
  onEdit,
  submitting,
}: {
  data: InterpretAllocationData;
  onConfirm: () => void;
  onEdit: () => void;
  submitting: boolean;
}) {
  return (
    <div className="res-ready">
      <div className="res-preview-fields">
        {data.name && <PreviewRow label="Keperluan" value={data.name} />}
        <PreviewRow label="Nominal" value={formatCurrency(data.amount)} bold />
        {data.account && <PreviewRow label="Akun" value={data.account} />}
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
