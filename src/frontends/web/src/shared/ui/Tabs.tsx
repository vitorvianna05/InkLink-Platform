import { useId, useRef, type KeyboardEvent, type ReactNode } from 'react';

export interface TabItem {
  id: string;
  label: ReactNode;
}

interface TabsProps {
  label: string;
  tabs: TabItem[];
  selected: string;
  onSelect: (id: string) => void;
  children: ReactNode;
}

/** Abas acessíveis: indicam a seleção e navegam com setas, Home e End. */
export function Tabs({ label, tabs, selected, onSelect, children }: TabsProps) {
  const base = useId();
  const refs = useRef<Record<string, HTMLButtonElement | null>>({});

  function handleKey(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    const last = tabs.length - 1;
    const next =
      event.key === 'ArrowRight' ? (index + 1) % tabs.length
      : event.key === 'ArrowLeft' ? (index - 1 + tabs.length) % tabs.length
      : event.key === 'Home' ? 0
      : event.key === 'End' ? last
      : null;
    if (next === null) return;
    event.preventDefault();
    const tab = tabs[next];
    if (!tab) return;
    onSelect(tab.id);
    refs.current[tab.id]?.focus();
  }

  return (
    <>
      <div className="tabs" role="tablist" aria-label={label}>
        {tabs.map((tab, index) => (
          <button
            key={tab.id}
            ref={(node) => {
              refs.current[tab.id] = node;
            }}
            id={`${base}-tab-${tab.id}`}
            type="button"
            role="tab"
            aria-selected={selected === tab.id}
            aria-controls={`${base}-panel`}
            tabIndex={selected === tab.id ? 0 : -1}
            className={selected === tab.id ? 'tab selected' : 'tab'}
            onClick={() => onSelect(tab.id)}
            onKeyDown={(event) => handleKey(event, index)}
          >
            {tab.label}
          </button>
        ))}
      </div>
      <div id={`${base}-panel`} role="tabpanel" aria-labelledby={`${base}-tab-${selected}`} tabIndex={0} className="tab-panel">
        {children}
      </div>
    </>
  );
}
