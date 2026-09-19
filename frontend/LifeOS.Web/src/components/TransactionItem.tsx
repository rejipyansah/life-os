import type { TransactionProjection } from '../types';

interface TransactionItemProps {
  transaction: TransactionProjection;
  onSelect?: (transaction: TransactionProjection) => void;
}

function formatCurrency(amount: number): string {
  return `Rp ${amount.toLocaleString('id-ID', { minimumFractionDigits: 0, maximumFractionDigits: 0 })}`;
}

function formatTime(dateStr: string): string {
  const d = new Date(dateStr);
  return d.toLocaleTimeString('id-ID', { hour: '2-digit', minute: '2-digit', hour12: false });
}

export default function TransactionItem({ transaction, onSelect }: TransactionItemProps) {
  const isPositive = transaction.type === 'Income' || transaction.type === 'Refund';
  const isTransfer = transaction.type === 'Transfer';

  const sourceEntry = isTransfer ? transaction.entries.find(e => e.amount < 0) : null;
  const destEntry = isTransfer ? transaction.entries.find(e => e.amount > 0) : null;

  const metaParts: string[] = [];
  if (transaction.categoryName) metaParts.push(transaction.categoryName);
  if (isTransfer && sourceEntry && destEntry) {
    metaParts.push(`${sourceEntry.accountName} → ${destEntry.accountName}`);
  } else {
    const accountNames = transaction.entries.map(e => e.accountName).filter(Boolean);
    if (accountNames.length > 0) metaParts.push(accountNames.join(', '));
  }

  const amountClass = isTransfer ? 'transfer' : isPositive ? 'in' : 'out';
  const amountPrefix = isTransfer ? '' : isPositive ? '+ ' : '– ';

  return (
    <div
      className="tx-row"
      onClick={() => onSelect?.(transaction)}
      role={onSelect ? 'button' : undefined}
      tabIndex={onSelect ? 0 : undefined}
      onKeyDown={onSelect ? (e) => {
        if (e.key === 'Enter' || e.key === ' ') onSelect(transaction);
      } : undefined}
    >
      <div className="tx-left">
        <span className="tx-desc">
          {transaction.description || transaction.type}
        </span>
        {metaParts.length > 0 && (
          <div className="tx-meta">
            {metaParts.map((part, i) => (
              <span key={i}>
                {i > 0 && <span className="tx-meta-sep"> · </span>}
                {part}
              </span>
            ))}
          </div>
        )}
      </div>
      <div className="tx-right">
        <span className={`tx-amount ${amountClass}`}>
          {amountPrefix}{formatCurrency(transaction.amount)}
        </span>
        {transaction.feeAmount != null && transaction.feeAmount > 0 && (
          <span className="tx-fee">fee {formatCurrency(transaction.feeAmount)}</span>
        )}
        <span className="tx-time">{formatTime(transaction.createdAt)}</span>
      </div>
    </div>
  );
}
