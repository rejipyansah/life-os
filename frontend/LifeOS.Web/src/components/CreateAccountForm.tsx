import { useState } from 'react';
import type { AccountType } from '../types';
import { createAccount } from '../api';

interface CreateAccountFormProps {
  onSuccess: () => void;
  onCancel: () => void;
}

const ACCOUNT_TYPES: AccountType[] = ['Cash', 'Bank', 'EWallet'];

export default function CreateAccountForm({ onSuccess, onCancel }: CreateAccountFormProps) {
  const [name, setName] = useState('');
  const [type, setType] = useState<AccountType>('Cash');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) {
      setError('Account name is required.');
      return;
    }
    setLoading(true);
    setError('');
    try {
      await createAccount({ name: name.trim(), type });
      setName('');
      setType('Cash');
      onSuccess();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to create account');
    } finally {
      setLoading(false);
    }
  };

  return (
    <form className="account-form" onSubmit={handleSubmit}>
      <h2>New Account</h2>
      {error && <div className="error-message">{error}</div>}
      <input
        type="text"
        placeholder="Account name"
        value={name}
        onChange={e => setName(e.target.value)}
        maxLength={256}
        autoFocus
        required
      />
      <div className="type-tabs">
        {ACCOUNT_TYPES.map(t => (
          <button
            key={t}
            type="button"
            className={`type-tab ${type === t ? 'active' : ''}`}
            onClick={() => setType(t)}
          >
            {t === 'EWallet' ? 'E-Wallet' : t}
          </button>
        ))}
      </div>
      <div className="form-actions">
        <button type="submit" className="btn btn-primary" disabled={loading}>
          {loading ? 'Creating...' : 'Create'}
        </button>
        <button type="button" className="btn btn-text" onClick={onCancel} disabled={loading}>
          Cancel
        </button>
      </div>
    </form>
  );
}
