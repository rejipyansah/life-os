import { useState, useRef, useEffect } from 'react';
import type { AccountProjection } from '../types';
import { updateAccount } from '../api';

interface AccountCardProps {
  account: AccountProjection;
  onUpdate: () => void;
  onEdit: (account: AccountProjection) => void;
}

function formatCurrency(amount: number): string {
  return `Rp ${amount.toLocaleString('id-ID', { minimumFractionDigits: 0, maximumFractionDigits: 0 })}`;
}

function accountTypeLabel(type: string): string {
  switch (type) {
    case 'Cash': return 'Cash';
    case 'Bank': return 'Bank';
    case 'EWallet': return 'E-Wallet';
    default: return type;
  }
}

export default function AccountCard({ account, onUpdate, onEdit }: AccountCardProps) {
  const [unarchiveError, setUnarchiveError] = useState('');
  const [archiveError, setArchiveError] = useState('');
  const [showArchiveConfirm, setShowArchiveConfirm] = useState(false);
  const [archiving, setArchiving] = useState(false);
  const errorTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    return () => {
      if (errorTimerRef.current) clearTimeout(errorTimerRef.current);
    };
  }, []);

  const setErrorWithAutoClear = (msg: string) => {
    setUnarchiveError(msg);
    if (errorTimerRef.current) clearTimeout(errorTimerRef.current);
    errorTimerRef.current = setTimeout(() => setUnarchiveError(''), 8000);
  };

  const handleArchiveConfirm = async () => {
    setArchiving(true);
    try {
      await updateAccount(account.id, { isArchived: true });
      setShowArchiveConfirm(false);
      setArchiveError('');
      onUpdate();
    } catch (e: unknown) {
      setArchiveError(e instanceof Error ? e.message : 'Gagal mengarsipkan akun');
    } finally {
      setArchiving(false);
    }
  };

  const handleUnarchive = async () => {
    try {
      await updateAccount(account.id, { isArchived: false });
      onUpdate();
    } catch (e: unknown) {
      setErrorWithAutoClear(e instanceof Error ? e.message : 'Gagal mengaktifkan akun');
    }
  };

  const hasAllocation = account.allocated > 0;
  const showSecondary = !account.isArchived && (hasAllocation || account.available !== account.balance);

  return (
    <div className={`account-item ${account.isArchived ? 'archived' : ''}`}>
      <div className="account-card-header">
        <span className="account-name" title={account.name}>
          {account.name}
        </span>
        <span className="account-type-tag">{accountTypeLabel(account.type)}</span>
        {account.isArchived && <span className="archive-pill">Arsip</span>}
      </div>

      {!account.isArchived ? (
        <div className="account-card-primary">
          <span className="account-primary-label">Bisa dipakai</span>
          <span className="account-primary-amount">{formatCurrency(account.available)}</span>
        </div>
      ) : (
        <div className="account-card-primary">
          <span className="account-primary-label">Saldo</span>
          <span className="account-primary-amount">{formatCurrency(account.balance)}</span>
        </div>
      )}

      {showSecondary && (
        <div className="account-card-details">
          <div className="account-detail-row">
            <span className="account-detail-label">Saldo</span>
            <span className="account-detail-value">{formatCurrency(account.balance)}</span>
          </div>
          {hasAllocation && (
            <div className="account-detail-row">
              <span className="account-detail-label">Dialokasikan</span>
              <span className="account-detail-value">{formatCurrency(account.allocated)}</span>
            </div>
          )}
        </div>
      )}

      <div className="account-card-actions">
        <button
          className="account-action-edit"
          onClick={() => onEdit(account)}
        >
          Edit
        </button>
        {account.isArchived ? (
          <button
            className="account-action-unarchive"
            onClick={handleUnarchive}
          >
            Aktifkan
          </button>
        ) : (
          <button
            className="account-action-archive"
            onClick={() => setShowArchiveConfirm(true)}
          >
            Arsipkan
          </button>
        )}
      </div>

      {unarchiveError && <div className="account-card-error">{unarchiveError}</div>}

      {showArchiveConfirm && (
        <div className="archive-dialog-overlay" onClick={() => { setShowArchiveConfirm(false); setArchiveError(''); }}>
          <div className="archive-dialog" onClick={(e) => e.stopPropagation()}>
            <h3 className="archive-dialog-title">Arsipkan akun?</h3>
            <p className="archive-dialog-account-name">{account.name}</p>
            <p className="archive-dialog-desc">
              Akun yang diarsipkan tidak dapat digunakan untuk transaksi atau alokasi baru.
            </p>
            {archiveError && <div className="archive-dialog-error">{archiveError}</div>}
            <div className="archive-dialog-actions">
              <button
                className="archive-dialog-btn archive-dialog-btn-cancel"
                onClick={() => { setShowArchiveConfirm(false); setArchiveError(''); }}
                disabled={archiving}
              >
                Batal
              </button>
              <button
                className="archive-dialog-btn archive-dialog-btn-confirm"
                onClick={handleArchiveConfirm}
                disabled={archiving}
              >
                {archiving ? 'Mengarsipkan...' : 'Arsipkan'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
