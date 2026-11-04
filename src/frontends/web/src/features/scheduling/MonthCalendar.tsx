import { ChevronLeft, ChevronRight } from 'lucide-react';
import { useRef, type KeyboardEvent } from 'react';
import { addDays, addMonths, monthGrid, monthLabel, parseLocalDate } from '@/shared/lib/dates';

const weekdays = ['Dom', 'Seg', 'Ter', 'Qua', 'Qui', 'Sex', 'Sáb'];
const longDate = new Intl.DateTimeFormat('pt-BR', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });

interface Props {
  month: string;
  selected: string;
  today: string;
  marked: Set<string>;
  onMonthChange: (month: string) => void;
  onSelect: (day: string) => void;
}

/** Calendário mensal navegável por teclado (setas movem o dia; Page Up/Down trocam o mês). */
export function MonthCalendar({ month, selected, today, marked, onMonthChange, onSelect }: Props) {
  const days = monthGrid(month);
  const grid = useRef<HTMLDivElement>(null);

  function move(day: string) {
    if (day.slice(0, 7) !== month.slice(0, 7)) onMonthChange(`${day.slice(0, 7)}-01`);
    onSelect(day);
    requestAnimationFrame(() => grid.current?.querySelector<HTMLButtonElement>(`[data-day="${day}"]`)?.focus());
  }

  function handleKey(event: KeyboardEvent<HTMLButtonElement>, day: string) {
    const offsets: Record<string, number> = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 };
    if (event.key in offsets) {
      event.preventDefault();
      move(addDays(day, offsets[event.key]!));
    } else if (event.key === 'PageUp' || event.key === 'PageDown') {
      event.preventDefault();
      const target = addMonths(day, event.key === 'PageUp' ? -1 : 1);
      move(`${target.slice(0, 7)}-${day.slice(8)}`.replace(/-(29|30|31)$/, '-28'));
    }
  }

  return (
    <div className="calendar">
      <div className="calendar-head">
        <button type="button" className="icon-button" onClick={() => onMonthChange(addMonths(month, -1))} aria-label="Mês anterior">
          <ChevronLeft size={18} aria-hidden="true" />
        </button>
        <h2 aria-live="polite">{monthLabel(month)}</h2>
        <button type="button" className="icon-button" onClick={() => onMonthChange(addMonths(month, 1))} aria-label="Próximo mês">
          <ChevronRight size={18} aria-hidden="true" />
        </button>
      </div>
      <div className="calendar-grid" role="grid" aria-label={`Calendário de ${monthLabel(month)}`} ref={grid}>
        {weekdays.map((weekday) => (
          <span key={weekday} className="weekday" aria-hidden="true">
            {weekday}
          </span>
        ))}
        {days.map((day) => {
          const classes = ['day'];
          if (day.slice(0, 7) !== month.slice(0, 7)) classes.push('outside');
          if (day === today) classes.push('today');
          if (day === selected) classes.push('selected');
          if (marked.has(day)) classes.push('has-items');
          return (
            <button
              key={day}
              type="button"
              data-day={day}
              className={classes.join(' ')}
              aria-pressed={day === selected}
              aria-label={`${longDate.format(parseLocalDate(day))}${marked.has(day) ? ', com reservas' : ''}`}
              tabIndex={day === selected ? 0 : -1}
              onClick={() => onSelect(day)}
              onKeyDown={(event) => handleKey(event, day)}
            >
              {Number(day.slice(8))}
            </button>
          );
        })}
      </div>
    </div>
  );
}
