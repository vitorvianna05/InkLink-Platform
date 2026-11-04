import { Search } from 'lucide-react';
import { useDeferredValue, useId, useState } from 'react';
import { useSuggestions } from './api';

const categoryLabels: Record<string, string> = { artist: 'Artista', studio: 'Estúdio', style: 'Estilo', city: 'Cidade' };

export function categoryLabel(category: string): string {
  return categoryLabels[category.toLowerCase()] ?? category;
}

interface Props {
  value: string;
  onChange: (value: string) => void;
}

/**
 * Campo livre com sugestões do Catálogo (combobox ARIA). Setas percorrem as opções,
 * Enter seleciona e Escape fecha. Selecionar só preenche o campo; a busca é explícita.
 */
export function SearchCombobox({ value, onChange }: Props) {
  const id = useId();
  const listId = `${id}-options`;
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);
  const deferred = useDeferredValue(value);
  const suggestions = useSuggestions(deferred, open);
  const options = suggestions.data ?? [];
  const hasTerm = value.trim().length > 0;
  const visible = open && hasTerm;

  function choose(index: number) {
    const option = options[index];
    if (!option) return;
    onChange(option.value);
    setOpen(false);
    setActive(-1);
  }

  return (
    <div
      className="combobox"
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) setOpen(false);
      }}
    >
      <label className="field" htmlFor={`${id}-input`}>
        <span>O que você está procurando?</span>
      </label>
      <div className="search-input">
        <Search size={22} aria-hidden="true" />
        <input
          id={`${id}-input`}
          role="combobox"
          aria-autocomplete="list"
          aria-expanded={visible}
          aria-controls={listId}
          aria-activedescendant={visible && active >= 0 ? `${id}-option-${active}` : undefined}
          autoComplete="off"
          placeholder="Artista, estúdio, estilo ou cidade…"
          value={value}
          onFocus={() => setOpen(true)}
          onChange={(event) => {
            onChange(event.target.value);
            setOpen(true);
            setActive(-1);
          }}
          onKeyDown={(event) => {
            if (event.key === 'Escape') {
              if (visible) event.preventDefault();
              setOpen(false);
              setActive(-1);
            } else if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') && options.length > 0) {
              event.preventDefault();
              setOpen(true);
              setActive((current) =>
                event.key === 'ArrowDown' ? (current + 1) % options.length : current <= 0 ? options.length - 1 : current - 1,
              );
            } else if (event.key === 'Enter' && visible && active >= 0) {
              event.preventDefault();
              choose(active);
            }
          }}
        />
      </div>
      {visible && (
        <ul className="suggestions" id={listId} role="listbox" aria-label="Sugestões de pesquisa">
          {suggestions.isPending || deferred !== value ? (
            <li className="suggestion-status" role="presentation">
              Buscando sugestões…
            </li>
          ) : suggestions.isError ? (
            <li className="suggestion-status" role="presentation">
              Não foi possível carregar sugestões agora.
            </li>
          ) : options.length === 0 ? (
            <li className="suggestion-status" role="presentation">
              Nenhuma sugestão para “{value.trim()}”.
            </li>
          ) : (
            options.map((option, index) => (
              <li
                key={`${option.category}-${option.value}`}
                id={`${id}-option-${index}`}
                role="option"
                aria-selected={index === active}
                onMouseDown={(event) => event.preventDefault()}
                onClick={() => choose(index)}
              >
                <span>{option.value}</span>
                <small>{categoryLabel(option.category)}</small>
              </li>
            ))
          )}
        </ul>
      )}
    </div>
  );
}
