import { useState } from 'react';
import type { AccountType, AccountProjection } from '../types';
import { createAccount, updateAccount } from '../api';

const ACCOUNT_TYPES: { value: AccountType; label: string }[] = [
  { value: 'Cash', label: 'Tunai' },
  { value: 'Bank', label: 'Bank' },
  { value: 'EWallet', label: 'E-Wallet' },
];

interface AccountFormProps {
  editAccount?: AccountProjection;
  onSuccess: () => void;
  onCancel: () => void;
}

export default function AccountForm({ editAccount, onSuccess, onCancel }: AccountFormProps) {
  const isEdit = !!editAccount;
  const [name, setName] = useState(editAccount?.name ?? '');
  const [type, setType] = useState<AccountType>(editAccount?.type ?? 'Cash');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const trimmed = name.trim();
    if (!trimmed) {
      setError('Nama akun tidak boleh kosong.');
      return;
    }
    if (trimmed.length > 50) {
      setError('Nama akun maksimal 50 karakter.');
      return;
    }

    setLoading(true);
    setError('');
    try {
      if (isEdit) {
        const patch: { name?: string; type?: AccountType } = {};
        if (trimmed !== editAccount!.name) patch.name = trimmed;
        if (type !== editAccount!.type) patch.type = type;
        if (Object.keys(patch).length === 0) {
          onCancel();
          return;
        }
        await updateAccount(editAccount!.id, patch);
      } else {
        await createAccount({ name: trimmed, type });
      }
      onSuccess();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Gagal menyimpan akun');
    } finally {
      setLoading(false);
    }
  };

  return (
    <form className="account-form" onSubmit={handleSubmit}>
      <h2>{isEdit ? 'Edit Akun' : 'Akun Baru'}</h2>
      {error && <div className="error-message">{error}</div>}
      <input
        type="text"
        placeholder="Nama akun"
        value={name}
        onChange={e => setName(e.target.value)}
        maxLength={50}
        autoFocus
      />
      <div className="type-tabs">
        {ACCOUNT_TYPES.map(t => (
          <button
            key={t.value}
            type="button"
            className={`type-tab ${type === t.value ? 'active' : ''}`}
            onClick={() => setType(t.value)}
          >
            {t.label}
          </button>
        ))}
      </div>
      <div className="form-actions">
        <button type="submit" className="btn btn-primary" disabled={loading}>
          {loading ? 'Menyimpan...' : (isEdit ? 'Simpan' : 'Buat')}
        </button>
        <button type="button" className="btn btn-secondary" onClick={onCancel} disabled={loading}>
          Batal
        </button>
      </div>
    </form>
  );
}
