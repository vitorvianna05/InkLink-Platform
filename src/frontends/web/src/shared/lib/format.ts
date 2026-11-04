const currency = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
const percent = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 2 });

export function money(value: number): string {
  return currency.format(value);
}

export function percentage(value: number): string {
  return `${percent.format(value)}%`;
}

/** Data civil (yyyy-MM-dd) por extenso curto, sem conversão de fuso. */
export function civilDate(value: string): string {
  const [year, month, day] = value.split('-');
  return `${day}/${month}/${year}`;
}

/** Instante no fuso informado (padrão do navegador). */
export function dateTime(value: string, timeZone?: string): string {
  return new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short', timeZone }).format(new Date(value));
}

export function time(value: string, timeZone?: string): string {
  return new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit', timeZone }).format(new Date(value));
}

export function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('');
}

const engagementLabels: Record<string, string> = {
  Negotiating: 'Em negociação',
  Agreed: 'Proposta aceita',
  Closing: 'Encerramento em andamento',
  Closed: 'Serviço encerrado',
};

export function engagementStateLabel(state: string): string {
  return engagementLabels[state] ?? state;
}

const proposalLabels: Record<string, string> = {
  Pending: 'Aguardando aceite',
  Superseded: 'Substituída',
  Accepted: 'Aceita',
};

export function proposalStateLabel(state: string): string {
  return proposalLabels[state] ?? state;
}
