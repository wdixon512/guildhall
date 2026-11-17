import { useEffect, useId, useRef, useState } from 'react';

export interface DropUpOption {
  value: string;
  label: string;
  group?: string;
  disabled?: boolean;
}

export function DropUpSelect({
  ariaLabel,
  prefixLabel,
  value,
  placeholder = 'Select…',
  options,
  disabled = false,
  onChange,
}: {
  ariaLabel: string;
  prefixLabel?: string;
  value: string;
  placeholder?: string;
  options: DropUpOption[];
  disabled?: boolean;
  onChange: (value: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const listId = useId();

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node))
        setOpen(false);
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        setOpen(false);
      }
    };
    document.addEventListener('pointerdown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('pointerdown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [open]);

  const selected = options.find(opt => opt.value === value);
  const groups: { name: string | null; items: DropUpOption[] }[] = [];
  for (const opt of options) {
    const groupName = opt.group ?? null;
    const existing = groups.find(g => g.name === groupName);
    if (existing) existing.items.push(opt);
    else groups.push({ name: groupName, items: [opt] });
  }

  return (
    <div ref={rootRef} className="dropup-select">
      <button
        type="button"
        className="dropup-trigger"
        aria-label={ariaLabel}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={open ? listId : undefined}
        disabled={disabled}
        onClick={() => setOpen(current => !current)}
      >
        <span className="dropup-trigger-text">
          {prefixLabel && <span className="dropup-prefix">{prefixLabel}:</span>}
          <span className="dropup-trigger-label">{selected?.label ?? placeholder}</span>
        </span>
        <span className="dropup-chevron" aria-hidden="true">▴</span>
      </button>

      {open && (
        <div id={listId} className="dropup-menu" role="listbox" aria-label={ariaLabel}>
          <div className="dropup-menu-title">{ariaLabel}</div>
          {groups.map(group => (
            <div key={group.name ?? '__default'} className="dropup-group">
              {group.name && <div className="dropup-group-label">{group.name}</div>}
              {group.items.map(opt => {
                const isSelected = opt.value === value;
                return (
                  <button
                    key={opt.value}
                    type="button"
                    role="option"
                    aria-selected={isSelected}
                    aria-disabled={opt.disabled || undefined}
                    disabled={opt.disabled}
                    className={`dropup-option ${isSelected ? 'is-selected' : ''} ${opt.disabled ? 'is-disabled' : ''}`}
                    onClick={() => {
                      if (opt.disabled) return;
                      setOpen(false);
                      onChange(opt.value);
                    }}
                  >
                    <span>{opt.label}</span>
                    {isSelected && <span aria-hidden="true">✓</span>}
                  </button>
                );
              })}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
