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
  const [dropUp, setDropUp] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const listRef = useRef<HTMLDivElement>(null);

  const selected = accounts.find(a => a.id === value);
  const displayText = selected?.name ?? '';

  const close = useCallback((restoreFocus = true) => {
    setOpen(false);
    setHighlightIndex(-1);
    setDropUp(false);
    if (restoreFocus) {
      triggerRef.current?.focus();
    }
  }, []);

  // Focus listbox when open
  useEffect(() => {
    if (open) {
      requestAnimationFrame(() => {
        listRef.current?.focus();
      });
    }
  }, [open]);

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

  const openDropdown = () => {
    const trigger = triggerRef.current;
    if (trigger) {
      const rect = trigger.getBoundingClientRect();
      const spaceBelow = window.innerHeight - rect.bottom;
      const estimatedHeight = Math.min(accounts.length * 48, 240);
      setDropUp(spaceBelow < estimatedHeight + 8);
    }
    setOpen(true);
    const idx = accounts.findIndex(a => a.id === value);
    setHighlightIndex(idx >= 0 ? idx : 0);
  };

  const handleTriggerKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      if (open) {
        close();
      } else {
        openDropdown();
      }
    }
    if (e.key === 'ArrowDown' && !open) {
      e.preventDefault();
      openDropdown();
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
    } else if (e.key === 'Home') {
      e.preventDefault();
      setHighlightIndex(0);
    } else if (e.key === 'End') {
      e.preventDefault();
      setHighlightIndex(accounts.length - 1);
    }
  };

  const handleOptionClick = (accountId: string) => {
    onChange(accountId);
    close(false);
  };

  // Scroll highlighted option into view
  useEffect(() => {
    if (!open || highlightIndex < 0) return;
    const option = listRef.current?.children[highlightIndex] as HTMLElement | undefined;
    option?.scrollIntoView({ block: 'nearest' });
  }, [highlightIndex, open]);

  const dropdownClass = `account-picker-dropdown${dropUp ? ' drop-up' : ''}`;

  return (
    <div className={`account-picker ${open ? 'open' : ''}`}>
      <button
        ref={triggerRef}
        type="button"
        id={id}
        className={`account-picker-trigger ${!displayText ? 'placeholder' : ''}`}
        onClick={() => { if (open) { close(); } else { openDropdown(); } }}
        onKeyDown={handleTriggerKeyDown}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-required={required || undefined}
      >
        {displayText || placeholder}
        <span className="account-picker-chevron" />
      </button>
      {open && (
        <div className={dropdownClass}>
          <div
            ref={listRef}
            className="account-picker-list"
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
                onClick={() => handleOptionClick(a.id)}
              >
                <span className="account-picker-option-name">{a.name}</span>
                {a.id === value && <span className="account-picker-check" />}
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
