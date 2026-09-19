import type { TransactionProjection } from '../types';

interface TransactionItemProps {
  transaction: TransactionProjection;
  onSelect?: (transaction: TransactionProjection) => void;
}

function formatCurrency(amount: number): string {
  return `Rp ${amount.toLocaleString('id-ID', { minimumFractionDigits: 0, maximumFractionDigits: 0 })}`;
}

function formatDate(dateStr: string): string {
  const d = new Date(dateStr + 'T00:00:00');
  return d.toLocaleDateString('id-ID', { day: 'numeric', month: 'short' });
}

export default function TransactionItem({ transaction, onSelect }: TransactionItemProps) {
  const isPositive = transaction.type === 'Income' || transaction.type === 'Refund';
  const isTransfer = transaction.type === 'Transfer';

  const sourceEntry = isTransfer ? transaction.entries.find(e => e.amount < 0) : null;
  const destEntry = isTransfer ? transaction.entries.find(e => e.amount > 0) : null;

  const accountNames = transaction.entries.map(e => e.accountName).filter(Boolean);
  const accountLabel = isTransfer && sourceEntry && destEntry
    ? `${sourceEntry.accountName} → ${destEntry.accountName}`
    : accountNames.join(', ');

  return (
    <div
      className="transaction-item"
      onClick={() => onSelect?.(transaction)}
      role={onSelect ? 'button' : undefined}
      tabIndex={onSelect ? 0 : undefined}
      onKeyDown={onSelect ? (e) => {
        if (e.key === 'Enter' || e.key === ' ') onSelect(transaction);
      } : undefined}
    >
      <div className="transaction-left">
        <span className="transaction-description">
          {transaction.description || transaction.type}
        </span>
        <div className="transaction-meta">
          <span className="transaction-type">{transaction.type}</span>
          {accountLabel && <span>{accountLabel}</span>}
        </div>
      </div>
      <div className="transaction-right">
        <span className={`transaction-amount ${isPositive ? 'positive' : 'negative'}`}>
          {isPositive ? '+' : '-'}{formatCurrency(transaction.amount)}
        </span>
        {transaction.feeAmount != null && transaction.feeAmount > 0 && (
          <span className="transaction-fee">fee {formatCurrency(transaction.feeAmount)}</span>
        )}
        <span className="transaction-date">{formatDate(transaction.occurredOn)}</span>
      </div>
    </div>
  );
}
