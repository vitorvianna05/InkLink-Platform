// Datas civis (yyyy-MM-dd) e conversões de horário local de um fuso IANA para instantes
// com offset explícito, exigidos pela Agenda. Nenhuma regra de disponibilidade fica aqui:
// o backend continua sendo a autoridade sobre conflitos e janelas.

export function toLocalDate(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

export function parseLocalDate(value: string): Date {
  const [y, m, d] = value.split('-').map(Number);
  return new Date(y ?? 1970, (m ?? 1) - 1, d ?? 1);
}

export function addDays(value: string, days: number): string {
  const date = parseLocalDate(value);
  date.setDate(date.getDate() + days);
  return toLocalDate(date);
}

export function startOfMonth(value: string): string {
  return `${value.slice(0, 7)}-01`;
}

export function addMonths(value: string, months: number): string {
  const date = parseLocalDate(startOfMonth(value));
  date.setMonth(date.getMonth() + months);
  return toLocalDate(date);
}

/** Dias exibidos na grade mensal (semanas completas, começando no domingo). */
export function monthGrid(month: string): string[] {
  const first = parseLocalDate(startOfMonth(month));
  const start = new Date(first);
  start.setDate(1 - first.getDay());
  const days: string[] = [];
  for (let i = 0; i < 42; i++) {
    const day = new Date(start);
    day.setDate(start.getDate() + i);
    days.push(toLocalDate(day));
  }
  const lastWeekStart = days.findIndex((d, i) => i % 7 === 0 && d.slice(0, 7) > month.slice(0, 7));
  return lastWeekStart > 0 ? days.slice(0, lastWeekStart) : days;
}

export function monthLabel(month: string): string {
  return new Intl.DateTimeFormat('pt-BR', { month: 'long', year: 'numeric' }).format(parseLocalDate(startOfMonth(month)));
}

export function todayIn(timeZone?: string): string {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
  return parts;
}

/** Offset (minutos) do fuso no instante informado. */
function offsetMinutes(instant: Date, timeZone: string): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone,
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).formatToParts(instant);
  const get = (type: string) => Number(parts.find((p) => p.type === type)?.value);
  const asUtc = Date.UTC(get('year'), get('month') - 1, get('day'), get('hour'), get('minute'), get('second'));
  return Math.round((asUtc - instant.getTime()) / 60000);
}

function formatOffset(minutes: number): string {
  const sign = minutes >= 0 ? '+' : '-';
  const abs = Math.abs(minutes);
  return `${sign}${String(Math.floor(abs / 60)).padStart(2, '0')}:${String(abs % 60).padStart(2, '0')}`;
}

/**
 * Converte data + hora local de um fuso em ISO com offset (ex.: 2026-11-10T14:00:00-03:00).
 * Retorna null quando o horário não existe ou é ambíguo nesse fuso (transição de horário),
 * para que a interface informe o problema antes de enviar.
 */
export function localToInstant(date: string, hhmm: string, timeZone: string): string | null {
  const [h, m] = hhmm.split(':').map(Number);
  const [y, mo, d] = date.split('-').map(Number);
  if ([h, m, y, mo, d].some((n) => n === undefined || Number.isNaN(n))) return null;
  const naiveUtc = Date.UTC(y!, mo! - 1, d!, h!, m!);
  const candidates = new Set<number>();
  for (const probe of [naiveUtc - 86400000, naiveUtc, naiveUtc + 86400000]) {
    candidates.add(offsetMinutes(new Date(probe), timeZone));
  }
  const matches = [...candidates].filter((offset) => {
    const instant = new Date(naiveUtc - offset * 60000);
    return offsetMinutes(instant, timeZone) === offset;
  });
  if (matches.length !== 1) return null;
  const offset = matches[0]!;
  const hh = String(h).padStart(2, '0');
  const mm = String(m).padStart(2, '0');
  return `${date}T${hh}:${mm}:00${formatOffset(offset)}`;
}
