import { useState } from 'react';
import type { AccountProjection, TransactionType, CreateTransactionCommand } from '../types';
import { createTransaction } from '../api';

interface AddTransactionFormProps {
  accounts: AccountProjection[];
  onSuccess: () => void;
  onCancel: () => void;
}

const TYPES: TransactionType[] = ['Expense', 'Income', 'Transfer'];

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
  const [loading, setLoading] = useState(false);

  const activeAccounts = accounts.filter(a => !a.isArchived);

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
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const parsedAmount = parseFloat(amount);
    if (!parsedAmount || parsedAmount <= 0) {
      setError('Amount must be a positive number.');
      return;
    }

    if (!occurredOn) {
      setError('Date is required.');
      return;
    }

    let command: CreateTransactionCommand;

    if (type === 'Transfer') {
      if (!sourceAccountId || !destAccountId) {
        setError('Source and destination accounts are required.');
        return;
      }
      if (sourceAccountId === destAccountId) {
        setError('Source and destination accounts must differ.');
        return;
      }
      const parsedFee = feeAmount ? parseFloat(feeAmount) : undefined;
      const sourceAmount = parsedFee ? -(parsedAmount + parsedFee) : -parsedAmount;
      command = {
        type: 'Transfer',
        amount: parsedAmount,
        description: description || undefined,
        occurredOn,
        feeAmount: parsedFee,
        entries: [
          { accountId: sourceAccountId, amount: sourceAmount },
          { accountId: destAccountId, amount: parsedAmount },
        ],
      };
    } else {
      if (!accountId) {
        setError('Account is required.');
        return;
      }
      const signedAmount = type === 'Expense' ? -parsedAmount : parsedAmount;
      command = {
        type,
        amount: parsedAmount,
        description: description || undefined,
        categoryName: categoryName || undefined,
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
      setError(e instanceof Error ? e.message : 'Failed to create transaction');
    } finally {
      setLoading(false);
    }
  };

  if (activeAccounts.length === 0) {
    return (
      <div className="form-notice">
        <p>Create an account first before adding a transaction.</p>
        <button className="btn btn-text" onClick={onCancel}>Back</button>
      </div>
    );
  }

  return (
    <form className="transaction-form" onSubmit={handleSubmit}>
      <h2>New Transaction</h2>

      {error && <div className="error-message">{error}</div>}

      <div className="type-tabs">
        {TYPES.map(t => (
          <button
            key={t}
            type="button"
            className={`type-tab ${type === t ? 'active' : ''}`}
            onClick={() => { setType(t); setError(''); }}
          >
            {t}
          </button>
        ))}
      </div>

      <input
        type="number"
        placeholder="Amount"
        value={amount}
        onChange={e => setAmount(e.target.value)}
        min="0.01"
        step="any"
        required
      />

      <input
        type="text"
        placeholder="Description (optional)"
        value={description}
        onChange={e => setDescription(e.target.value)}
        maxLength={512}
      />

      {type !== 'Transfer' && (
        <input
          type="text"
          placeholder="Category (optional)"
          value={categoryName}
          onChange={e => setCategoryName(e.target.value)}
          maxLength={128}
        />
      )}

      <input
        type="date"
        value={occurredOn}
        onChange={e => setOccurredOn(e.target.value)}
        required
      />

      {type !== 'Transfer' ? (
        <select value={accountId} onChange={e => setAccountId(e.target.value)} required>
          <option value="">Select account</option>
          {activeAccounts.map(a => (
            <option key={a.id} value={a.id}>{a.name}</option>
          ))}
        </select>
      ) : (
        <>
          <select value={sourceAccountId} onChange={e => setSourceAccountId(e.target.value)} required>
            <option value="">From (source)</option>
            {activeAccounts.map(a => (
              <option key={a.id} value={a.id}>{a.name}</option>
            ))}
          </select>
          <select value={destAccountId} onChange={e => setDestAccountId(e.target.value)} required>
            <option value="">To (destination)</option>
            {activeAccounts.filter(a => a.id !== sourceAccountId).map(a => (
              <option key={a.id} value={a.id}>{a.name}</option>
            ))}
          </select>
          <input
            type="number"
            placeholder="Fee (optional)"
            value={feeAmount}
            onChange={e => setFeeAmount(e.target.value)}
            min="0"
            step="any"
          />
        </>
      )}

      <div className="form-actions">
        <button type="submit" className="btn btn-primary" disabled={loading}>
          {loading ? 'Saving...' : 'Save'}
        </button>
        <button type="button" className="btn btn-text" onClick={onCancel} disabled={loading}>
          Cancel
        </button>
      </div>
    </form>
  );
}
