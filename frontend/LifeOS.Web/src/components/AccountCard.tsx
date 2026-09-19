import { useState } from 'react';
import type { AccountProjection } from '../types';
import { updateAccount } from '../api';

interface AccountCardProps {
  account: AccountProjection;
  onUpdate: () => void;
}

function formatCurrency(amount: number): string {
  return `Rp ${amount.toLocaleString('id-ID', { minimumFractionDigits: 0, maximumFractionDigits: 0 })}`;
}

function accountTypeLabel(type: string): string {
  switch (type) {
    case 'Cash': return 'Tunai';
    case 'Bank': return 'Bank';
    case 'EWallet': return 'E-Wallet';
    default: return type;
  }
}

export default function AccountCard({ account, onUpdate }: AccountCardProps) {
  const [editing, setEditing] = useState(false);
  const [editName, setEditName] = useState(account.name);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  const handleSaveName = async () => {
    const trimmed = editName.trim();
    if (!trimmed || trimmed === account.name) {
      setEditing(false);
      setEditName(account.name);
      return;
    }

    setLoading(true);
    setError('');
    try {
      await updateAccount(account.id, { name: trimmed });
      setEditing(false);
      onUpdate();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to rename');
      setEditName(account.name);
    } finally {
      setLoading(false);
    }
  };

  const handleCancelEdit = () => {
    setEditing(false);
    setEditName(account.name);
    setError('');
  };

  const handleToggleArchive = async () => {
    setLoading(true);
    setError('');
    try {
      await updateAccount(account.id, { isArchived: !account.isArchived });
      onUpdate();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to update');
    } finally {
      setLoading(false);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') handleSaveName();
    if (e.key === 'Escape') handleCancelEdit();
  };

  const hasAllocation = account.allocated > 0;

  return (
    <div className={`account-item ${account.isArchived ? 'archived' : ''}`}>
      <div className="account-left">
        <div className="account-name-row">
          {editing ? (
            <input
              className="account-name-input"
              type="text"
              value={editName}
              onChange={e => setEditName(e.target.value)}
              onBlur={handleSaveName}
              onKeyDown={handleKeyDown}
              maxLength={256}
              autoFocus
              disabled={loading}
            />
          ) : (
            <span
              className="account-name"
              onClick={() => { setEditing(true); setEditName(account.name); }}
              title="Klik untuk mengubah nama"
            >
              {account.name}
            </span>
          )}
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
            className="account-action-btn"
            onClick={handleToggleArchive}
            disabled={loading}
            title={account.isArchived ? 'Unarchive akun' : 'Arsipkan akun'}
          >
            {account.isArchived ? 'Unarchive' : 'Archive'}
          </button>
        </div>
      </div>
      {error && <div style={{ position: 'absolute', bottom: -2, left: 0, fontSize: '0.6rem', color: 'var(--color-error)' }}>{error}</div>}
    </div>
  );
}
