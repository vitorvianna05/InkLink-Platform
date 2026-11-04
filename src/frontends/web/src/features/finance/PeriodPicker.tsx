import { addDays, addMonths, startOfMonth } from '@/shared/lib/dates';

export interface Period {
  from: string;
  /** Limite exclusivo [from, to). */
  to: string;
}

export function monthPeriod(day: string): Period {
  const from = startOfMonth(day);
  return { from, to: addMonths(from, 1) };
}

export function periodDays(period: Period): number {
  const ms = new Date(`${period.to}T00:00:00Z`).getTime() - new Date(`${period.from}T00:00:00Z`).getTime();
  return Math.round(ms / 86_400_000);
}

/** Seleção do período civil: o último dia exibido é inclusivo; o contrato usa fim exclusivo. */
export function PeriodPicker({ value, onChange }: { value: Period; onChange: (period: Period) => void }) {
  const lastDay = addDays(value.to, -1);
  const days = periodDays(value);
  const invalid = days < 1 || days > 366;
  return (
    <fieldset className="form-grid" style={{ border: 'none', padding: 0, margin: 0 }}>
      <legend className="visually-hidden">Período</legend>
      <label className="field">
        <span>De</span>
        <input type="date" value={value.from} onChange={(e) => e.target.value && onChange({ ...value, from: e.target.value })} />
      </label>
      <label className="field">
        <span>Até (inclusive)</span>
        <input type="date" value={lastDay} onChange={(e) => e.target.value && onChange({ ...value, to: addDays(e.target.value, 1) })} aria-invalid={invalid} />
      </label>
      <div className="field">
        <span className="field-label">Atalhos</span>
        <div className="row">
          <button type="button" className="button outline" onClick={() => onChange(monthPeriod(addMonths(value.from, -1)))}>
            Mês anterior
          </button>
          <button type="button" className="button outline" onClick={() => onChange(monthPeriod(addMonths(value.from, 1)))}>
            Próximo mês
          </button>
        </div>
      </div>
      {invalid && <p className="field-error">O período deve ter de 1 a 366 dias.</p>}
    </fieldset>
  );
}
