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
  const [error, setError] = useState('');
  const errorTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    return () => {
      if (errorTimerRef.current) clearTimeout(errorTimerRef.current);
    };
  }, []);

  const setErrorWithAutoClear = (msg: string) => {
    setError(msg);
    if (errorTimerRef.current) clearTimeout(errorTimerRef.current);
    errorTimerRef.current = setTimeout(() => setError(''), 8000);
  };

  const handleArchive = async () => {
    try {
      await updateAccount(account.id, { isArchived: true });
      onUpdate();
    } catch (e: unknown) {
      setErrorWithAutoClear(e instanceof Error ? e.message : 'Gagal mengarsipkan akun');
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

  return (
    <div className={`account-item ${account.isArchived ? 'archived' : ''}`}>
      <div className="account-left">
        <div className="account-name-row">
          <span className="account-name" title={account.name}>
            {account.name}
          </span>
          <span className="account-type-tag">{accountTypeLabel(account.type)}</span>
          {account.isArchived && <span className="archive-pill">Arsip</span>}
        </div>
        {hasAllocation && !account.isArchived && (
          <span className="account-reserved-sub">Alokasi: {formatCurrency(account.allocated)}</span>
        )}
      </div>
      <div className="account-right">
        <span className="account-balance">{formatCurrency(account.balance)}</span>
        {!account.isArchived && account.available !== account.balance && (
          <span className="account-available">tersedia {formatCurrency(account.available)}</span>
        )}
        <div className="account-actions">
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
              onClick={handleArchive}
            >
              Arsipkan
            </button>
          )}
        </div>
      </div>
      {error && <div className="account-card-error">{error}</div>}
    </div>
  );
}
