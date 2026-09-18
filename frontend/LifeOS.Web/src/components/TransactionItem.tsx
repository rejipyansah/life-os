import type { TransactionProjection } from '../types';

interface TransactionItemProps {
  transaction: TransactionProjection;
}

function formatCurrency(amount: number): string {
  return `Rp ${amount.toLocaleString('id-ID', { minimumFractionDigits: 0, maximumFractionDigits: 0 })}`;
}

function formatDate(dateStr: string): string {
  const d = new Date(dateStr + 'T00:00:00');
  return d.toLocaleDateString('id-ID', { day: 'numeric', month: 'short' });
}

export default function TransactionItem({ transaction }: TransactionItemProps) {
  const isPositive = transaction.type === 'Income' || transaction.type === 'Refund';
  const isTransfer = transaction.type === 'Transfer';

  const sourceEntry = isTransfer ? transaction.entries.find(e => e.amount < 0) : null;
  const destEntry = isTransfer ? transaction.entries.find(e => e.amount > 0) : null;

  const accountNames = transaction.entries.map(e => e.accountName).filter(Boolean);
  const accountLabel = isTransfer && sourceEntry && destEntry
    ? `${sourceEntry.accountName} → ${destEntry.accountName}`
    : accountNames.join(', ');

  return (
    <div className={`transaction-item ${isPositive ? 'positive' : 'negative'}`}>
      <div className="transaction-left">
        <span className="transaction-type">{transaction.type}</span>
        <span className="transaction-description">
          {transaction.description || transaction.type}
        </span>
        {accountLabel && (
          <span className="transaction-account">{accountLabel}</span>
        )}
      </div>
      <div className="transaction-right">
        <span className={`transaction-amount ${isPositive ? 'positive' : 'negative'}`}>
          {isPositive ? '+' : '-'} {formatCurrency(transaction.amount)}
        </span>
        {transaction.feeAmount != null && transaction.feeAmount > 0 && (
          <span className="transaction-fee">Fee: {formatCurrency(transaction.feeAmount)}</span>
        )}
        <span className="transaction-date">{formatDate(transaction.occurredOn)}</span>
      </div>
    </div>
  );
}
