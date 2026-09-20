import { useState } from 'react';
import type { AccountProjection, AccountType } from '../types';
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

const ACCOUNT_TYPES: { value: AccountType; label: string }[] = [
  { value: 'Cash', label: 'Tunai' },
  { value: 'Bank', label: 'Bank' },
  { value: 'EWallet', label: 'E-Wallet' },
];

export default function AccountCard({ account, onUpdate }: AccountCardProps) {
  const [editing, setEditing] = useState(false);
  const [editName, setEditName] = useState(account.name);
  const [editType, setEditType] = useState<AccountType>(account.type);
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
      setError(e instanceof Error ? e.message : 'Gagal mengubah nama');
      setEditName(account.name);
    } finally {
      setLoading(false);
    }
  };

  const handleCancelEdit = () => {
    setEditing(false);
    setEditName(account.name);
    setEditType(account.type);
    setError('');
  };

  const handleSaveEdit = async () => {
    const trimmed = editName.trim();
    if (!trimmed) {
      setError('Nama akun tidak boleh kosong.');
      return;
    }

    setLoading(true);
    setError('');
    try {
      const patch: { name?: string; type?: AccountType } = {};
      if (trimmed !== account.name) patch.name = trimmed;
      if (editType !== account.type) patch.type = editType;

      if (Object.keys(patch).length === 0) {
        setEditing(false);
        return;
      }

      await updateAccount(account.id, patch);
      setEditing(false);
      onUpdate();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Gagal menyimpan perubahan');
    } finally {
      setLoading(false);
    }
  };

  const handleToggleArchive = async () => {
    setLoading(true);
    setError('');
    try {
      await updateAccount(account.id, { isArchived: !account.isArchived });
      onUpdate();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Gagal mengubah status');
    } finally {
      setLoading(false);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') {
      if (editing) handleSaveEdit();
      else handleSaveName();
    }
    if (e.key === 'Escape') handleCancelEdit();
  };

  const hasAllocation = account.allocated > 0;

  if (editing) {
    return (
      <div className={`account-item ${account.isArchived ? 'archived' : ''}`}>
        <div className="account-left" style={{ gap: 6 }}>
          <input
            className="account-name-input"
            type="text"
            value={editName}
            onChange={e => setEditName(e.target.value)}
            onKeyDown={handleKeyDown}
            maxLength={256}
            autoFocus
            disabled={loading}
            placeholder="Nama akun"
          />
          <div className="account-type-edit">
            {ACCOUNT_TYPES.map(t => (
              <button
                key={t.value}
                type="button"
                className={`account-type-option ${editType === t.value ? 'selected' : ''}`}
                onClick={() => setEditType(t.value)}
                disabled={loading}
              >
                {t.label}
              </button>
            ))}
          </div>
          <div className="account-edit-actions">
            <button
              className="account-action-btn"
              onClick={handleSaveEdit}
              disabled={loading}
            >
              {loading ? '...' : 'Simpan'}
            </button>
            <button
              className="account-action-btn"
              onClick={handleCancelEdit}
              disabled={loading}
            >
              Batal
            </button>
          </div>
        </div>
        {error && <div className="account-edit-error">{error}</div>}
      </div>
    );
  }

  return (
    <div className={`account-item ${account.isArchived ? 'archived' : ''}`}>
      <div className="account-left">
        <div className="account-name-row">
          <span
            className="account-name"
            onClick={() => { setEditing(true); setEditName(account.name); setEditType(account.type); }}
            title="Klik untuk mengedit"
          >
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
            className="account-action-btn"
            onClick={() => { setEditing(true); setEditName(account.name); setEditType(account.type); }}
            disabled={loading}
            title="Edit akun"
          >
            Edit
          </button>
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
      {error && <div className="account-edit-error">{error}</div>}
    </div>
  );
}
