import { useState } from 'react';
import type { TransactionProjection } from '../types';
import { createTransaction } from '../api';

interface TransactionDetailProps {
  transaction: TransactionProjection;
  isAlreadyReversed: boolean;
  allTransactions: TransactionProjection[];
  onSuccess: () => void;
}

function formatCurrency(amount: number): string {
  return `Rp ${amount.toLocaleString('id-ID', { minimumFractionDigits: 0, maximumFractionDigits: 0 })}`;
}

function formatDate(dateStr: string): string {
  const d = new Date(dateStr + 'T00:00:00');
  return d.toLocaleDateString('id-ID', { day: 'numeric', month: 'long', year: 'numeric' });
}

function formatDateTime(dateStr: string): string {
  const d = new Date(dateStr);
  return d.toLocaleDateString('id-ID', { day: 'numeric', month: 'long', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

export default function TransactionDetail({
  transaction,
  isAlreadyReversed,
  allTransactions,
  onSuccess,
}: TransactionDetailProps) {
  const [confirming, setConfirming] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');

  const isPositive = transaction.type === 'Income' || transaction.type === 'Refund';
  const isTransfer = transaction.type === 'Transfer';

  const relatedTx = transaction.relatedTransactionId
    ? allTransactions.find(t => t.id === transaction.relatedTransactionId)
    : null;

  const reversedByTx = allTransactions.find(
    t => t.type === 'Reversal' && t.relatedTransactionId === transaction.id
  );

  const canReverse = !isAlreadyReversed && !reversedByTx;

  const handleReverse = async () => {
    setSubmitting(true);
    setError('');

    try {
      const reversalEntries = transaction.entries.map(entry => ({
        accountId: entry.accountId,
        amount: -entry.amount,
      }));

      await createTransaction({
        type: 'Reversal',
        amount: transaction.amount,
        description: transaction.description ? `Reversal: ${transaction.description}` : 'Reversal',
        occurredOn: transaction.occurredOn,
        relatedTransactionId: transaction.id,
        entries: reversalEntries,
      });

      onSuccess();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to create reversal');
      setSubmitting(false);
    }
  };

  return (
    <div className="transaction-detail">
      <div className="detail-header">
        <span className="detail-description">
          {transaction.description || transaction.type}
        </span>
        <span className={`detail-amount ${isPositive ? 'positive' : 'negative'}`}>
          {isPositive ? '+' : '-'}{formatCurrency(transaction.amount)}
        </span>
      </div>

      <div className="detail-rows">
        <div className="detail-row">
          <span className="detail-label">Type</span>
          <span className="detail-value">{transaction.type}</span>
        </div>

        <div className="detail-row">
          <span className="detail-label">Date</span>
          <span className="detail-value">{formatDate(transaction.occurredOn)}</span>
        </div>

        {isTransfer ? (
          transaction.entries.map((entry, i) => (
            <div className="detail-row" key={entry.accountId}>
              <span className="detail-label">{i === 0 ? 'From' : 'To'}</span>
              <span className="detail-value">
                {entry.accountName} ({entry.amount < 0 ? '-' : '+'}{formatCurrency(Math.abs(entry.amount))})
              </span>
            </div>
          ))
        ) : (
          transaction.entries.map(entry => (
            <div className="detail-row" key={entry.accountId}>
              <span className="detail-label">Account</span>
              <span className="detail-value">
                {entry.accountName} ({entry.amount < 0 ? '-' : '+'}{formatCurrency(Math.abs(entry.amount))})
              </span>
            </div>
          ))
        )}

        {transaction.categoryName && (
          <div className="detail-row">
            <span className="detail-label">Category</span>
            <span className="detail-value">{transaction.categoryName}</span>
          </div>
        )}

        {transaction.feeAmount != null && transaction.feeAmount > 0 && (
          <div className="detail-row">
            <span className="detail-label">Fee</span>
            <span className="detail-value">{formatCurrency(transaction.feeAmount)}</span>
          </div>
        )}

        {relatedTx && (
          <div className="detail-row">
            <span className="detail-label">Related</span>
            <span className="detail-value detail-value-link">
              {relatedTx.description || relatedTx.type} ({formatCurrency(relatedTx.amount)})
            </span>
          </div>
        )}

        {reversedByTx && (
          <div className="detail-row">
            <span className="detail-label">Reversed by</span>
            <span className="detail-value detail-value-link">
              {reversedByTx.description || reversedByTx.type} ({formatDate(reversedByTx.occurredOn)})
            </span>
          </div>
        )}

        <div className="detail-row">
          <span className="detail-label">Created</span>
          <span className="detail-value detail-value-secondary">{formatDateTime(transaction.createdAt)}</span>
        </div>
      </div>

      {canReverse && (
        <div className="detail-actions">
          {!confirming ? (
            <button
              className="btn btn-secondary"
              onClick={() => setConfirming(true)}
              disabled={submitting}
            >
              Reverse Transaction
            </button>
          ) : (
            <div className="detail-confirm">
              <p className="detail-confirm-text">
                The original transaction will remain unchanged. A corrective reversal transaction will be created.
              </p>
              {error && <div className="error-message">{error}</div>}
              <div className="form-actions">
                <button
                  className="btn btn-secondary"
                  onClick={() => { setConfirming(false); setError(''); }}
                  disabled={submitting}
                >
                  Cancel
                </button>
                <button
                  className="btn btn-primary"
                  onClick={handleReverse}
                  disabled={submitting}
                >
                  {submitting ? 'Reversing...' : 'Confirm Reversal'}
                </button>
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
