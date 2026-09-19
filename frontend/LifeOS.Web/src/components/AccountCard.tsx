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
    case 'Cash': return 'Cash';
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

  return (
    <div className={`account-row ${account.isArchived ? 'account-row-archived' : ''}`}>
      <div className="account-row-left">
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
            className="account-row-name"
            onClick={() => { setEditing(true); setEditName(account.name); }}
            title="Click to rename"
          >
            {account.name}
          </span>
        )}
        <span className="account-row-type">{accountTypeLabel(account.type)}</span>
        {account.isArchived && <span className="account-row-archived-badge">Archived</span>}
      </div>
      <div className="account-row-right">
        <span className="account-row-balance">{formatCurrency(account.balance)}</span>
        <span className="account-row-available">avail. {formatCurrency(account.available)}</span>
        <button
          className="account-archive-btn"
          onClick={handleToggleArchive}
          disabled={loading}
          title={account.isArchived ? 'Unarchive account' : 'Archive account'}
        >
          {account.isArchived ? 'Unarchive' : 'Archive'}
        </button>
      </div>
      {error && <div className="account-row-error">{error}</div>}
    </div>
  );
}
