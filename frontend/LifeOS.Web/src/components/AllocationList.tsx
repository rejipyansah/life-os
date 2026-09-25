import { useState, useRef, useEffect } from 'react';
import type { AccountProjection, AllocationProjection } from '../types';
import { updateAllocation } from '../api';

interface AllocationListProps {
  allocations: AllocationProjection[];
  accounts: AccountProjection[];
  hasActiveAccounts: boolean;
  onRefresh: () => void;
  onEdit: (allocation: AllocationProjection) => void;
  onAdd: () => void;
}

function formatCurrency(amount: number): string {
  return `Rp ${amount.toLocaleString('id-ID', { minimumFractionDigits: 0, maximumFractionDigits: 0 })}`;
}

export default function AllocationList({ allocations, accounts, hasActiveAccounts, onRefresh, onEdit, onAdd }: AllocationListProps) {
  const accountMap = new Map(accounts.map(a => [a.id, a.name]));
  const active = allocations.filter(a => a.status === 'active');
  const completed = allocations.filter(a => a.status === 'completed');
  const cancelled = allocations.filter(a => a.status === 'cancelled');
  const history = [...completed, ...cancelled];

  const [confirmTarget, setConfirmTarget] = useState<{ allocation: AllocationProjection; action: 'completed' | 'cancelled' } | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const errorTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    return () => {
      if (errorTimerRef.current) clearTimeout(errorTimerRef.current);
    };
  }, []);

  const openConfirm = (allocation: AllocationProjection, action: 'completed' | 'cancelled') => {
    setError('');
    setConfirmTarget({ allocation, action });
  };

  const closeConfirm = () => {
    setConfirmTarget(null);
    setError('');
    if (errorTimerRef.current) clearTimeout(errorTimerRef.current);
  };

  const handleConfirm = async () => {
    if (!confirmTarget) return;
    setLoading(true);
    setError('');
    try {
      await updateAllocation(confirmTarget.allocation.allocationId, { status: confirmTarget.action });
      closeConfirm();
      onRefresh();
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Gagal mengubah status alokasi.';
      setError(msg);
      if (errorTimerRef.current) clearTimeout(errorTimerRef.current);
      errorTimerRef.current = setTimeout(() => setError(''), 8000);
    } finally {
      setLoading(false);
    }
  };

  const dialogTitle = confirmTarget?.action === 'completed' ? 'Selesaikan Alokasi?' : 'Batalkan Alokasi?';
  const dialogDesc = confirmTarget?.action === 'completed'
    ? 'Selesaikan alokasi ini? Sebuah transaksi pengeluaran akan dibuat dari akun terkait dengan nominal yang sama.'
    : 'Batalkan alokasi ini? Dana yang dialokasikan akan kembali tersedia tanpa membuat transaksi.';
  const confirmBtnText = confirmTarget?.action === 'completed' ? 'Selesaikan' : 'Batalkan';
  const confirmBtnClass = confirmTarget?.action === 'completed'
    ? 'archive-dialog-btn archive-dialog-btn-primary'
    : 'archive-dialog-btn archive-dialog-btn-confirm';

  if (allocations.length === 0 && !hasActiveAccounts) return null;

  return (
    <div className="allocation-section">
      <div className="allocation-head">
        <span className="section-title">Alokasi</span>
        {hasActiveAccounts && (
          <button className="btn-add-allocation" onClick={onAdd}>
            + Tambah Alokasi
          </button>
        )}
      </div>

      {active.length > 0 && (
        <div className="allocation-list">
          {active.map(a => (
            <div key={a.allocationId} className="allocation-item active">
              <div className="allocation-left">
                <span className="allocation-name">{a.name}</span>
                <span className="allocation-meta">
                  {formatCurrency(a.amount)}{accountMap.get(a.accountId) ? ` · ${accountMap.get(a.accountId)}` : ''}
                </span>
              </div>
              <div className="allocation-actions">
                <button className="allocation-action-btn alloc-action-complete" onClick={() => openConfirm(a, 'completed')}>
                  Selesaikan
                </button>
                <button className="allocation-action-btn alloc-action-edit" onClick={() => onEdit(a)}>
                  Edit
                </button>
                <button className="allocation-action-btn alloc-action-cancel" onClick={() => openConfirm(a, 'cancelled')}>
                  Batalkan
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      {history.length > 0 && (
        <div className="allocation-history">
          <div className="allocation-history-label">Riwayat Transaksi</div>
          {history.map(a => (
            <div key={a.allocationId} className="allocation-item completed">
              <div className="allocation-left">
                <span className="allocation-name">{a.name}</span>
                <span className="allocation-meta">
                  {formatCurrency(a.amount)}{accountMap.get(a.accountId) ? ` · ${accountMap.get(a.accountId)}` : ''}
                </span>
              </div>
              <span className="allocation-status-badge">
                {a.status === 'completed' ? 'Selesai' : 'Dibatalkan'}
              </span>
            </div>
          ))}
        </div>
      )}

      {confirmTarget && (
        <div className="archive-dialog-overlay" onClick={closeConfirm}>
          <div className="archive-dialog" onClick={e => e.stopPropagation()}>
            <h3 className="archive-dialog-title">{dialogTitle}</h3>
            <p className="archive-dialog-account-name">{confirmTarget.allocation.name}</p>
            <p className="archive-dialog-desc">{dialogDesc}</p>
            {error && <div className="archive-dialog-error">{error}</div>}
            <div className="archive-dialog-actions">
              <button
                className="archive-dialog-btn archive-dialog-btn-cancel"
                onClick={closeConfirm}
                disabled={loading}
              >
                Batal
              </button>
              <button
                className={confirmBtnClass}
                onClick={handleConfirm}
                disabled={loading}
              >
                {loading ? 'Menyimpan...' : confirmBtnText}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
