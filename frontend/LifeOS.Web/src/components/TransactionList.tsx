import type { TransactionProjection } from '../types';
import TransactionItem from './TransactionItem';

interface TransactionListProps {
  transactions: TransactionProjection[];
}

export default function TransactionList({ transactions }: TransactionListProps) {
  if (transactions.length === 0) {
    return (
      <div className="empty-state">
        <p>No transactions yet.</p>
        <p className="empty-hint">Tap + Add transaction to get started.</p>
      </div>
    );
  }

  return (
    <div className="transaction-list">
      {transactions.map(tx => (
        <TransactionItem key={tx.id} transaction={tx} />
      ))}
    </div>
  );
}
