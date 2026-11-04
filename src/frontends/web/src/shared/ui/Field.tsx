import { useId, type ReactNode } from 'react';

export interface FieldControlProps {
  id: string;
  'aria-invalid': boolean;
  'aria-describedby': string | undefined;
}

/** Campo com rótulo, dica e erro associados ao controle sem alterar seu nome acessível. */
export function Field({ label, error, hint, children }: { label: string; error?: string; hint?: string; children: (props: FieldControlProps) => ReactNode }) {
  const id = useId();
  const errorId = `${id}-error`;
  const hintId = `${id}-hint`;
  const describedBy = [error ? errorId : null, hint ? hintId : null].filter(Boolean).join(' ') || undefined;
  return (
    <div className="field">
      <label htmlFor={id} className="field-label">
        {label}
      </label>
      {children({ id, 'aria-invalid': Boolean(error), 'aria-describedby': describedBy })}
      {hint && (
        <small className="field-hint" id={hintId}>
          {hint}
        </small>
      )}
      {error && (
        <span className="field-error" id={errorId}>
          {error}
        </span>
      )}
    </div>
  );
}
