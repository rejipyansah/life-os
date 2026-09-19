import type { TransactionProjection } from '../types';
import TransactionItem from './TransactionItem';

interface TransactionListProps {
  transactions: TransactionProjection[];
  onSelect?: (transaction: TransactionProjection) => void;
}

export default function TransactionList({ transactions, onSelect }: TransactionListProps) {
  if (transactions.length === 0) {
    return (
      <div className="empty-state">
        <p>No transactions yet.</p>
        <p className="empty-hint">Type what happened above, or use Manual.</p>
      </div>
    );
  }

  return (
    <div className="transaction-list">
      {transactions.map(tx => (
        <TransactionItem key={tx.id} transaction={tx} onSelect={onSelect} />
      ))}
    </div>
  );
}
