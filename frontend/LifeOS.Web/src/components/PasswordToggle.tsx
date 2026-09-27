import { useState } from 'react';

interface PasswordToggleProps
  extends Omit<React.ButtonHTMLAttributes<HTMLButtonElement>, 'onClick'> {
  onVisibilityChange: (visible: boolean) => void;
}

export default function PasswordToggle({
  onVisibilityChange,
  ...buttonProps
}: PasswordToggleProps) {
  const [visible, setVisible] = useState(false);

  const handleToggle = () => {
    const next = !visible;
    setVisible(next);
    onVisibilityChange(next);
  };

  return (
    <button
      type="button"
      aria-label={visible ? 'Sembunyikan kata sandi' : 'Tampilkan kata sandi'}
      onClick={handleToggle}
      className="absolute right-3.5 text-lo-text-subtle hover:text-lo-on-surface transition-colors p-1 flex items-center justify-center cursor-pointer"
      {...buttonProps}
    >
      {visible ? (
        <svg
          className="w-[19px] h-[19px]"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden="true"
        >
          <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94" />
          <path d="M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19" />
          <path d="M14.12 14.12a3 3 0 1 1-4.24-4.24" />
          <line x1="1" y1="1" x2="23" y2="23" />
        </svg>
      ) : (
        <svg
          className="w-[19px] h-[19px]"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden="true"
        >
          <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" />
          <circle cx="12" cy="12" r="3" />
        </svg>
      )}
    </button>
  );
}