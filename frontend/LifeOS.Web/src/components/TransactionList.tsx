import type { TransactionProjection } from '../types';
import TransactionItem from './TransactionItem';

interface TransactionListProps {
  transactions: TransactionProjection[];
  onSelect?: (transaction: TransactionProjection) => void;
}

function toLocalDateKey(dateStr: string): string {
  return dateStr;
}

function groupByDate(transactions: TransactionProjection[]): TransactionProjection[][] {
  const groups: TransactionProjection[][] = [];
  let current: TransactionProjection[] = [];

  for (const tx of transactions) {
    if (current.length > 0 && current[0].occurredOn !== tx.occurredOn) {
      groups.push(current);
      current = [];
    }
    current.push(tx);
  }
  if (current.length > 0) groups.push(current);

  return groups;
}

function formatGroupDate(dateStr: string): string {
  const today = new Date();
  const todayKey = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;

  const yesterday = new Date(today);
  yesterday.setDate(yesterday.getDate() - 1);
  const yesterdayKey = `${yesterday.getFullYear()}-${String(yesterday.getMonth() + 1).padStart(2, '0')}-${String(yesterday.getDate()).padStart(2, '0')}`;

  const key = toLocalDateKey(dateStr);
  if (key === todayKey) return 'Today';
  if (key === yesterdayKey) return 'Yesterday';

  const [y, m, d] = dateStr.split('-');
  const monthNames = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  return `${parseInt(d)} ${monthNames[parseInt(m) - 1]} ${y}`;
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

  const groups = groupByDate(transactions);

  return (
    <div className="transaction-list">
      {groups.map(group => (
        <div key={group[0].occurredOn} className="transaction-date-group">
          <div className="transaction-date-header">{formatGroupDate(group[0].occurredOn)}</div>
          {group.map(tx => (
            <TransactionItem key={tx.id} transaction={tx} onSelect={onSelect} />
          ))}
        </div>
      ))}
    </div>
  );
}
