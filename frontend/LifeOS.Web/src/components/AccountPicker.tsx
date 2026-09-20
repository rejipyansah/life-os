import { useState, useRef, useEffect, useCallback } from 'react';
import type { AccountProjection } from '../types';

interface AccountPickerProps {
  value: string;
  onChange: (accountId: string) => void;
  accounts: AccountProjection[];
  placeholder?: string;
  id?: string;
  required?: boolean;
}

export default function AccountPicker({ value, onChange, accounts, placeholder = 'Pilih akun', id, required }: AccountPickerProps) {
  const [open, setOpen] = useState(false);
  const [highlightIndex, setHighlightIndex] = useState(-1);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const listRef = useRef<HTMLDivElement>(null);

  const selected = accounts.find(a => a.id === value);
  const displayText = selected?.name ?? '';

  const close = useCallback(() => {
    setOpen(false);
    setHighlightIndex(-1);
    triggerRef.current?.focus();
  }, []);

  // Close on outside click
  useEffect(() => {
    if (!open) return;
    const handler = (e: MouseEvent) => {
      if (listRef.current && !listRef.current.contains(e.target as Node) &&
          triggerRef.current && !triggerRef.current.contains(e.target as Node)) {
        close();
      }
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [open, close]);

  // Close on Escape
  useEffect(() => {
    if (!open) return;
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault();
        close();
      }
    };
    document.addEventListener('keydown', handler);
    return () => document.removeEventListener('keydown', handler);
  }, [open, close]);

  const handleTriggerKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      setOpen(prev => !prev);
      if (!open) setHighlightIndex(0);
    }
    if (e.key === 'ArrowDown' && !open) {
      e.preventDefault();
      setOpen(true);
      setHighlightIndex(0);
    }
  };

  const handleListKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setHighlightIndex(i => Math.min(i + 1, accounts.length - 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setHighlightIndex(i => Math.max(i - 1, 0));
    } else if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      if (highlightIndex >= 0 && highlightIndex < accounts.length) {
        onChange(accounts[highlightIndex].id);
        close();
      }
    }
  };

  const handleSelect = (accountId: string) => {
    onChange(accountId);
    close();
  };

  return (
    <div className={`account-picker ${open ? 'open' : ''}`}>
      <button
        ref={triggerRef}
        type="button"
        id={id}
        className={`account-picker-trigger ${!displayText ? 'placeholder' : ''}`}
        onClick={() => { setOpen(prev => !prev); if (!open) setHighlightIndex(0); }}
        onKeyDown={handleTriggerKeyDown}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-required={required || undefined}
      >
        {displayText || placeholder}
        <span className="account-picker-chevron" />
      </button>
      {open && (
        <div
          ref={listRef}
          className="account-picker-dropdown"
          role="listbox"
          tabIndex={-1}
          onKeyDown={handleListKeyDown}
        >
          {accounts.map((a, i) => (
            <div
              key={a.id}
              className={`account-picker-option ${a.id === value ? 'selected' : ''} ${i === highlightIndex ? 'highlighted' : ''}`}
              role="option"
              aria-selected={a.id === value}
              onMouseEnter={() => setHighlightIndex(i)}
              onMouseDown={(e) => { e.preventDefault(); handleSelect(a.id); }}
            >
              {a.name}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
