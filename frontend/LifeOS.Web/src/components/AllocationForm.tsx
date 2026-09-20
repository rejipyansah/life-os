import { useState } from 'react';
import type { AccountProjection, AllocationProjection } from '../types';
import { createAllocation, updateAllocation } from '../api';
import AccountPicker from './AccountPicker';

interface AllocationFormProps {
  accounts: AccountProjection[];
  editAllocation?: AllocationProjection | null;
  onSuccess: () => void;
  onCancel: () => void;
}

export default function AllocationForm({ accounts, editAllocation, onSuccess, onCancel }: AllocationFormProps) {
  const [name, setName] = useState(editAllocation?.name ?? '');
  const [amount, setAmount] = useState(editAllocation ? String(editAllocation.amount) : '');
  const [accountId, setAccountId] = useState(editAllocation?.accountId ?? '');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const activeAccounts = accounts.filter(a => !a.isArchived);
  const isEdit = !!editAllocation;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    const trimmedName = name.trim();
    if (!trimmedName) {
      setError('Nama alokasi harus diisi.');
      return;
    }

    const parsedAmount = parseFloat(amount);
    if (!parsedAmount || parsedAmount <= 0) {
      setError('Nominal harus lebih dari 0.');
      return;
    }

    if (!accountId) {
      setError('Pilih akun.');
      return;
    }

    setLoading(true);
    setError('');

    try {
      if (isEdit) {
        await updateAllocation(editAllocation!.allocationId, {
          name: trimmedName,
          amount: parsedAmount,
          accountId,
        });
      } else {
        await createAllocation({
          name: trimmedName,
          amount: parsedAmount,
          accountId,
        });
      }
      onSuccess();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Gagal menyimpan alokasi');
    } finally {
      setLoading(false);
    }
  };

  if (activeAccounts.length === 0) {
    return (
      <div className="form-notice">
        <p>Buat akun terlebih dahulu sebelum membuat alokasi.</p>
        <button className="btn btn-text" onClick={onCancel}>Kembali</button>
      </div>
    );
  }

  return (
    <form className="allocation-form" onSubmit={handleSubmit}>
      <h2>{isEdit ? 'Edit Alokasi' : 'Alokasi Baru'}</h2>

      {error && <div className="error-message">{error}</div>}

      <label className="form-label" htmlFor="alloc-name">Nama</label>
      <input
        id="alloc-name"
        type="text"
        placeholder="WiFi, Liburan, Servis motor"
        value={name}
        onChange={e => setName(e.target.value)}
        maxLength={256}
        required
      />

      <label className="form-label" htmlFor="alloc-amount">Nominal</label>
      <div className="input-group">
        <span className="input-prefix">Rp</span>
        <input
          id="alloc-amount"
          type="number"
          placeholder="350000"
          value={amount}
          onChange={e => setAmount(e.target.value)}
          min="0.01"
          step="any"
          required
        />
      </div>

      <label className="form-label">Akun</label>
      <AccountPicker
        value={accountId}
        onChange={setAccountId}
        accounts={activeAccounts}
        placeholder="Pilih akun"
        required
      />

      <div className="form-actions">
        <button type="submit" className="btn btn-primary" disabled={loading}>
          {loading ? 'Menyimpan...' : isEdit ? 'Simpan perubahan' : 'Simpan'}
        </button>
        <button type="button" className="btn btn-text" onClick={onCancel} disabled={loading}>
          Batal
        </button>
      </div>
    </form>
  );
}
