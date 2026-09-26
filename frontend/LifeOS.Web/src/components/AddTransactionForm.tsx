import { useState } from 'react';
import type { AccountProjection, TransactionType, CreateTransactionCommand } from '../types';
import { createTransaction } from '../api';
import AccountPicker from './AccountPicker';

interface AddTransactionFormProps {
  accounts: AccountProjection[];
  onSuccess: () => void;
  onCancel: () => void;
}

const TYPES: TransactionType[] = ['Expense', 'Income', 'Transfer'];

const TYPE_LABELS: Record<TransactionType, string> = {
  Expense: 'Pengeluaran',
  Income: 'Pemasukan',
  Transfer: 'Transfer',
};

function today(): string {
  const d = new Date();
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  const dd = String(d.getDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
}

export default function AddTransactionForm({ accounts, onSuccess, onCancel }: AddTransactionFormProps) {
  const [type, setType] = useState<TransactionType>('Expense');
  const [amount, setAmount] = useState('');
  const [description, setDescription] = useState('');
  const [categoryName, setCategoryName] = useState('');
  const [occurredOn, setOccurredOn] = useState(today());
  const [accountId, setAccountId] = useState('');
  const [sourceAccountId, setSourceAccountId] = useState('');
  const [destAccountId, setDestAccountId] = useState('');
  const [feeAmount, setFeeAmount] = useState('');
  const [error, setError] = useState('');
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(false);

  const activeAccounts = accounts.filter(a => !a.isArchived);

  const clearFieldError = (field: string) => {
    setFieldErrors(prev => {
      const next = { ...prev };
      delete next[field];
      return next;
    });
  };

  const validate = (): Record<string, string> => {
    const errors: Record<string, string> = {};

    const trimmedAmount = amount.trim();
    if (!trimmedAmount) {
      errors.amount = 'Nominal harus diisi.';
    } else {
      const parsedAmount = parseFloat(trimmedAmount);
      if (isNaN(parsedAmount) || !isFinite(parsedAmount)) {
        errors.amount = 'Nominal harus berupa angka yang valid.';
      } else if (parsedAmount <= 0) {
        errors.amount = 'Nominal harus lebih dari 0.';
      }
    }

    if (!occurredOn) {
      errors.occurredOn = 'Tanggal harus diisi.';
    }

    if (type === 'Transfer') {
      if (!sourceAccountId) {
        errors.sourceAccountId = 'Akun sumber harus dipilih.';
      }
      if (!destAccountId) {
        errors.destAccountId = 'Akun tujuan harus dipilih.';
      }
      if (sourceAccountId && destAccountId && sourceAccountId === destAccountId) {
        errors.destAccountId = 'Akun tujuan harus berbeda dari akun sumber.';
      }
      const trimmedFee = feeAmount.trim();
      if (trimmedFee) {
        const parsedFee = parseFloat(trimmedFee);
        if (isNaN(parsedFee) || !isFinite(parsedFee)) {
          errors.feeAmount = 'Biaya transfer harus berupa angka yang valid.';
        } else if (parsedFee < 0) {
          errors.feeAmount = 'Biaya transfer tidak boleh negatif.';
        }
      }
    } else {
      if (!accountId) {
        errors.accountId = 'Akun harus dipilih.';
      }
    }

    return errors;
  };

  const reset = () => {
    setAmount('');
    setDescription('');
    setCategoryName('');
    setOccurredOn(today());
    setAccountId('');
    setSourceAccountId('');
    setDestAccountId('');
    setFeeAmount('');
    setError('');
    setFieldErrors({});
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    const validationErrors = validate();
    if (Object.keys(validationErrors).length > 0) {
      setFieldErrors(validationErrors);
      return;
    }

    setFieldErrors({});

    const parsedAmount = parseFloat(amount.trim());
    let command: CreateTransactionCommand;

    if (type === 'Transfer') {
      const parsedFee = feeAmount.trim() ? parseFloat(feeAmount.trim()) : undefined;
      const sourceAmount = parsedFee ? -(parsedAmount + parsedFee) : -parsedAmount;
      command = {
        type: 'Transfer',
        amount: parsedAmount,
        description: description.trim() || undefined,
        occurredOn,
        feeAmount: parsedFee,
        entries: [
          { accountId: sourceAccountId, amount: sourceAmount },
          { accountId: destAccountId, amount: parsedAmount },
        ],
      };
    } else {
      const signedAmount = type === 'Expense' ? -parsedAmount : parsedAmount;
      command = {
        type,
        amount: parsedAmount,
        description: description.trim() || undefined,
        categoryName: categoryName.trim() || undefined,
        occurredOn,
        entries: [
          { accountId, amount: signedAmount },
        ],
      };
    }

    setLoading(true);
    setError('');
    try {
      await createTransaction(command);
      reset();
      onSuccess();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Gagal membuat transaksi.');
    } finally {
      setLoading(false);
    }
  };

  if (activeAccounts.length === 0) {
    return (
      <div className="form-notice">
        <p>Buat akun terlebih dahulu sebelum menambah transaksi.</p>
        <button className="btn btn-text" onClick={onCancel}>Kembali</button>
      </div>
    );
  }

  return (
    <form className="account-form transaction-form" onSubmit={handleSubmit}>
      <h2>Transaksi Baru</h2>

      {error && <div className="error-message">{error}</div>}

      <div className="type-tabs">
        {TYPES.map(t => (
          <button
            key={t}
            type="button"
            className={`type-tab ${type === t ? 'active' : ''}`}
            onClick={() => { setType(t); setError(''); setFieldErrors({}); }}
          >
            {TYPE_LABELS[t]}
          </button>
        ))}
      </div>

      <label className="form-label">Nominal</label>
      <div className="input-group">
        <span className="input-prefix">Rp</span>
        <input
          type="number"
          placeholder="0"
          value={amount}
          onChange={e => { setAmount(e.target.value); clearFieldError('amount'); }}
          min="0.01"
          step="any"
        />
      </div>
      {fieldErrors.amount && <div className="field-error">{fieldErrors.amount}</div>}

      <label className="form-label">Deskripsi</label>
      <input
        type="text"
        placeholder="Opsional"
        value={description}
        onChange={e => setDescription(e.target.value)}
        maxLength={512}
      />

      {type !== 'Transfer' && (
        <>
          <label className="form-label">Kategori</label>
          <input
            type="text"
            placeholder="Opsional"
            value={categoryName}
            onChange={e => setCategoryName(e.target.value)}
            maxLength={128}
          />
        </>
      )}

      {type !== 'Transfer' ? (
        <>
          <label className="form-label">Akun</label>
          <AccountPicker
            value={accountId}
            onChange={(id) => { setAccountId(id); clearFieldError('accountId'); }}
            accounts={activeAccounts}
            placeholder="Pilih akun"
          />
          {fieldErrors.accountId && <div className="field-error">{fieldErrors.accountId}</div>}
        </>
      ) : (
        <>
          <label className="form-label">Dari Akun</label>
          <AccountPicker
            value={sourceAccountId}
            onChange={(id) => {
              setSourceAccountId(id);
              clearFieldError('sourceAccountId');
              if (destAccountId === id) setDestAccountId('');
            }}
            accounts={activeAccounts}
            placeholder="Pilih akun sumber"
          />
          {fieldErrors.sourceAccountId && <div className="field-error">{fieldErrors.sourceAccountId}</div>}

          <label className="form-label">Ke Akun</label>
          <AccountPicker
            value={destAccountId}
            onChange={(id) => { setDestAccountId(id); clearFieldError('destAccountId'); }}
            accounts={activeAccounts.filter(a => a.id !== sourceAccountId)}
            placeholder="Pilih akun tujuan"
          />
          {fieldErrors.destAccountId && <div className="field-error">{fieldErrors.destAccountId}</div>}

          <label className="form-label">Biaya Transfer</label>
          <div className="input-group">
            <span className="input-prefix">Rp</span>
            <input
              type="number"
              placeholder="0"
              value={feeAmount}
              onChange={e => { setFeeAmount(e.target.value); clearFieldError('feeAmount'); }}
              min="0"
              step="any"
            />
          </div>
          {fieldErrors.feeAmount && <div className="field-error">{fieldErrors.feeAmount}</div>}
        </>
      )}

      <label className="form-label">Tanggal</label>
      <input
        type="date"
        value={occurredOn}
        onChange={e => { setOccurredOn(e.target.value); clearFieldError('occurredOn'); }}
      />
      {fieldErrors.occurredOn && <div className="field-error">{fieldErrors.occurredOn}</div>}

      <div className="form-actions">
        <button type="submit" className="btn btn-primary" disabled={loading}>
          {loading ? 'Menyimpan...' : 'Catat'}
        </button>
        <button type="button" className="btn btn-secondary" onClick={onCancel} disabled={loading}>
          Batal
        </button>
      </div>
    </form>
  );
}
