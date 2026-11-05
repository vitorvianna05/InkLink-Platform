import { CalendarDays, MessageCircle, Plus, Trash2 } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { useEngagements } from '@/features/engagements/api';
import { parseAmount } from '@/features/engagements/ProposalSection';
import { useConversations } from '@/features/messaging/api';
import { RequireArtist } from '@/features/session/guards';
import type { Session } from '@/features/session/session';
import type { Reservation, TimeWindow } from '@/shared/api/contracts';
import { ApiError, newIdempotencyKey } from '@/shared/api/http';
import { addDays, localToInstant, monthGrid, parseLocalDate, startOfMonth, todayIn } from '@/shared/lib/dates';
import { money, time } from '@/shared/lib/format';
import { PageHeading } from '@/shared/ui/PageHeading';
import { describeError, EmptyState, ErrorState, fieldError, Loading } from '@/shared/ui/States';
import {
  useCalendar,
  useCompleteSession,
  useCompletion,
  useConfirmReservation,
  useDefineAvailability,
  useEngagementAvailability,
  useOwnAvailability,
} from './api';
import { MonthCalendar } from './MonthCalendar';

const longDate = new Intl.DateTimeFormat('pt-BR', { weekday: 'long', day: 'numeric', month: 'long' });

function hhmm(value: string): string {
  return value.slice(0, 5);
}

function AvailabilityEditor({ date, timeZone }: { date: string; timeZone: string }) {
  const availability = useOwnAvailability(date);
  const define = useDefineAvailability(date);
  const [draft, setDraft] = useState<TimeWindow[] | null>(null);
  const windows = draft ?? availability.data?.windows.map((w) => ({ start: hhmm(w.start), end: hhmm(w.end) })) ?? [];
  const invalid = windows.some((w) => !w.start || !w.end || w.end <= w.start);

  function change(index: number, patch: Partial<TimeWindow>) {
    setDraft(windows.map((w, i) => (i === index ? { ...w, ...patch } : w)));
  }

  if (availability.isPending) return <Loading label="Carregando disponibilidade…" />;
  if (availability.isLoadingError) return <ErrorState error={availability.error} onRetry={() => void availability.refetch()} />;

  return (
    <form
      className="form-card"
      onSubmit={(event) => {
        event.preventDefault();
        if (invalid) return;
        define.mutate({ windows, timeZoneId: availability.data.timeZoneId || timeZone }, { onSuccess: () => setDraft(null) });
      }}
    >
      <h3>Disponibilidade do dia</h3>
      <p className="muted">Horários em {availability.data.timeZoneId || timeZone}. Reduzir a disponibilidade não afeta reservas já confirmadas.</p>
      {windows.length === 0 && <p className="muted">Nenhuma janela de atendimento neste dia.</p>}
      {windows.map((window, index) => (
        <div className="row" key={index}>
          <label className="field">
            <span>Início</span>
            <input type="time" value={window.start} onChange={(e) => change(index, { start: e.target.value })} />
          </label>
          <label className="field">
            <span>Fim</span>
            <input type="time" value={window.end} onChange={(e) => change(index, { end: e.target.value })} aria-invalid={window.end <= window.start} />
          </label>
          <button type="button" className="icon-button" aria-label={`Remover janela ${index + 1}`} onClick={() => setDraft(windows.filter((_, i) => i !== index))}>
            <Trash2 size={16} aria-hidden="true" />
          </button>
        </div>
      ))}
      {invalid && <p className="field-error">Cada janela precisa terminar depois de começar.</p>}
      {define.isError && (
        <p className="notice error" role="alert">
          {fieldError(define.error, 'windows', 'timeZoneId') ?? `${describeError(define.error).title} ${describeError(define.error).detail}`}
        </p>
      )}
      {define.isSuccess && draft === null && (
        <p className="notice success" role="status">
          Disponibilidade salva.
        </p>
      )}
      <div className="form-actions">
        <button type="button" className="button outline" onClick={() => setDraft([...windows, { start: '10:00', end: '18:00' }])}>
          <Plus size={16} aria-hidden="true" /> Adicionar janela
        </button>
        <button type="submit" className="button red" disabled={invalid || define.isPending || draft === null}>
          {define.isPending ? 'Salvando…' : 'Salvar disponibilidade'}
        </button>
      </div>
    </form>
  );
}

function ReservationForm({ date, timeZone, preselected }: { date: string; timeZone: string; preselected: string }) {
  const engagements = useEngagements('artist');
  const confirm = useConfirmReservation();
  const agreed = (engagements.data ?? []).filter((e) => e.state === 'Agreed');
  const [engagementId, setEngagementId] = useState(preselected);
  const [start, setStart] = useState('');
  const [end, setEnd] = useState('');
  const [error, setError] = useState<string | null>(null);
  // A chave da operação acompanha a intenção atual; muda quando os dados mudam.
  const [operationId, setOperationId] = useState(newIdempotencyKey);
  const availability = useEngagementAvailability(engagementId, date, Boolean(engagementId));

  function reset() {
    setOperationId(newIdempotencyKey());
    confirm.reset();
  }

  return (
    <form
      className="form-card"
      noValidate
      onSubmit={(event) => {
        event.preventDefault();
        setError(null);
        if (!engagementId) return setError('Escolha o atendimento.');
        if (!start || !end) return setError('Informe início e término.');
        if (end <= start) return setError('O término deve ser posterior ao início.');
        const startInstant = localToInstant(date, start, timeZone);
        const endInstant = localToInstant(date, end, timeZone);
        if (!startInstant || !endInstant) return setError('Este horário não existe ou é ambíguo no seu fuso (mudança de horário). Escolha outro.');
        confirm.mutate(
          { engagementId, start: startInstant, end: endInstant, operationId },
          {
            onSuccess: () => {
              setStart('');
              setEnd('');
              setOperationId(newIdempotencyKey());
            },
          },
        );
      }}
    >
      <h3>Confirmar reserva</h3>
      <p className="muted">Somente atendimentos com proposta aceita podem ser agendados. O backend valida disponibilidade e conflitos.</p>
      {engagements.isPending ? (
        <Loading />
      ) : engagements.isLoadingError ? (
        <ErrorState error={engagements.error} onRetry={() => void engagements.refetch()} />
      ) : agreed.length === 0 ? (
        <p className="notice">Nenhum atendimento com proposta aceita aguardando agendamento.</p>
      ) : (
        <>
          <label className="field">
            <span>Atendimento</span>
            <select
              value={engagementId}
              onChange={(e) => {
                setEngagementId(e.target.value);
                reset();
              }}
            >
              <option value="">Selecione…</option>
              {agreed.map((e) => (
                <option key={e.id} value={e.id}>
                  {e.idea.slice(0, 60)} · {money(e.currentProposal?.amount ?? 0)}
                </option>
              ))}
            </select>
          </label>
          {engagementId && availability.isSuccess && (
            <p className="field-hint">
              Janelas do dia: {availability.data.windows.length ? availability.data.windows.map((w) => `${hhmm(w.start)}–${hhmm(w.end)}`).join(', ') : 'nenhuma'}
              {availability.data.busy.length > 0 && ` · ocupado: ${availability.data.busy.map((b) => `${time(b.start, timeZone)}–${time(b.end, timeZone)}`).join(', ')}`}. Consulta prévia
              não garante a reserva.
            </p>
          )}
          <div className="form-grid">
            <label className="field">
              <span>Início</span>
              <input
                type="time"
                value={start}
                onChange={(e) => {
                  setStart(e.target.value);
                  reset();
                }}
              />
            </label>
            <label className="field">
              <span>Término</span>
              <input
                type="time"
                value={end}
                onChange={(e) => {
                  setEnd(e.target.value);
                  reset();
                }}
              />
            </label>
          </div>
          {error && (
            <p className="field-error" role="alert">
              {error}
            </p>
          )}
          {confirm.isError && (
            <p className="notice error" role="alert">
              {confirm.error instanceof ApiError && confirm.error.status === 409
                ? 'O horário conflita com outra reserva ou está fora da sua disponibilidade. Nada foi reservado.'
                : (fieldError(confirm.error, 'start', 'end', 'engagementId') ?? `${describeError(confirm.error).title} ${describeError(confirm.error).detail}`)}
            </p>
          )}
          {confirm.isSuccess && (
            <p className="notice success" role="status">
              Reserva confirmada para {time(confirm.data.start, confirm.data.timeZoneId)}–{time(confirm.data.end, confirm.data.timeZoneId)}.
            </p>
          )}
          <div className="form-actions">
            <button type="submit" className="button red" disabled={confirm.isPending}>
              {confirm.isPending ? 'Confirmando…' : 'Confirmar reserva'}
            </button>
          </div>
        </>
      )}
    </form>
  );
}

function CompletionControl({ reservation }: { reservation: Reservation }) {
  const [now] = useState(() => Date.now());
  const ended = new Date(reservation.end).getTime() <= now;
  const completion = useCompletion(reservation.id, ended);
  const complete = useCompleteSession(reservation.id);
  const [amount, setAmount] = useState(String(reservation.referenceAmount).replace('.', ','));
  const [open, setOpen] = useState(false);
  if (!ended) return null;
  if (completion.isPending) return <small className="muted">Verificando conclusão…</small>;
  if (completion.data) return <span className="status green">Sessão concluída · {money(completion.data.amount)}</span>;
  if (!open) {
    return (
      <button type="button" className="text-button red" onClick={() => setOpen(true)}>
        Registrar conclusão da sessão
      </button>
    );
  }
  const value = parseAmount(amount);
  return (
    <form
      className="row"
      onSubmit={(event) => {
        event.preventDefault();
        if (value === null) return;
        complete.mutate({ completedAt: new Date().toISOString(), amount: value });
      }}
    >
      <label className="field">
        <span>Valor da sessão (R$)</span>
        <input value={amount} inputMode="decimal" onChange={(e) => setAmount(e.target.value)} aria-invalid={value === null} />
      </label>
      <button type="submit" className="button dark" disabled={value === null || complete.isPending}>
        Concluir
      </button>
      {complete.isError && <span className="field-error">{describeError(complete.error).title}</span>}
      <small className="muted" style={{ flexBasis: '100%' }}>
        A conclusão não registra recebimento. Lance o valor recebido em Financeiro.
      </small>
    </form>
  );
}

function DayReservations({ items }: { items: Reservation[] }) {
  const conversations = useConversations();
  if (items.length === 0) return <EmptyState title="Nenhuma reserva neste dia." icon={<CalendarDays size={24} aria-hidden="true" />} />;
  return (
    <ul className="list">
      {items.map((r) => {
        const conversation = conversations.data?.find((c) => c.engagementId === r.engagementId);
        return (
          <li key={r.id} className="list-item">
            <div className="grow">
              <h3>
                {time(r.start, r.timeZoneId)}–{time(r.end, r.timeZoneId)} · {r.clientName}
              </h3>
              <p>
                {r.project} · referência {money(r.referenceAmount)}
              </p>
              <CompletionControl reservation={r} />
            </div>
            <div className="row">
              <Link className="button outline" to={`/estudio/atendimentos/${r.engagementId}`}>
                Atendimento
              </Link>
              {conversation && (
                <Link className="icon-button" to={`/estudio/mensagens/${conversation.id}`} aria-label={`Conversa com ${r.clientName}`}>
                  <MessageCircle size={16} aria-hidden="true" />
                </Link>
              )}
            </div>
          </li>
        );
      })}
    </ul>
  );
}

function Content({ session }: { session: Session }) {
  const timeZone = session.artistTimeZone ?? 'America/Sao_Paulo';
  const today = todayIn(timeZone);
  const [params, setParams] = useSearchParams();
  const selected = /^\d{4}-\d{2}-\d{2}$/.test(params.get('dia') ?? '') ? params.get('dia')! : today;
  const [month, setMonth] = useState(startOfMonth(selected));
  const preselected = params.get('atendimento') ?? '';
  const grid = monthGrid(month);
  const from = grid[0]!;
  const to = addDays(grid[grid.length - 1]!, 1);
  const calendar = useCalendar('artist', from, to);
  const marked = useMemo(() => new Set((calendar.data ?? []).map((r) => r.localDate)), [calendar.data]);
  const dayItems = (calendar.data ?? []).filter((r) => r.localDate === selected);

  function select(day: string) {
    const next = new URLSearchParams(params);
    next.set('dia', day);
    setParams(next, { replace: true });
  }

  return (
    <>
      <PageHeading eyebrow="SUA AGENDA" title="Espaço para novas histórias.">
        Reservas do mês, horários do dia e disponibilidade em {timeZone}.
      </PageHeading>
      <div className="agenda-layout">
        <div className="stack">
          <MonthCalendar month={month} selected={selected} today={today} marked={marked} onMonthChange={setMonth} onSelect={select} />
          {calendar.isLoadingError && <ErrorState error={calendar.error} onRetry={() => void calendar.refetch()} />}
        </div>
        <div className="stack">
          <h2 style={{ textTransform: 'capitalize' }}>{longDate.format(parseLocalDate(selected))}</h2>
          {calendar.isPending ? <Loading label="Carregando reservas…" /> : calendar.isSuccess ? <DayReservations items={dayItems} /> : null}
          {selected >= today ? (
            <>
              <ReservationForm key={`${selected}-${preselected}`} date={selected} timeZone={timeZone} preselected={preselected} />
              <AvailabilityEditor key={selected} date={selected} timeZone={timeZone} />
            </>
          ) : (
            <p className="muted">Datas passadas não recebem novas reservas ou disponibilidade.</p>
          )}
        </div>
      </div>
    </>
  );
}

export function AgendaPage() {
  return <RequireArtist>{(session) => <Content session={session} />}</RequireArtist>;
}
