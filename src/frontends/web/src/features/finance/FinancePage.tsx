import { useState } from 'react';
import { useEngagements } from '@/features/engagements/api';
import { parseAmount } from '@/features/engagements/ProposalSection';
import { RequireArtist } from '@/features/session/guards';
import type { Session } from '@/features/session/session';
import type { EntryKind, LedgerEntry } from '@/shared/api/contracts';
import { ApiError, newIdempotencyKey } from '@/shared/api/http';
import { todayIn } from '@/shared/lib/dates';
import { civilDate, money } from '@/shared/lib/format';
import { Dialog } from '@/shared/ui/Dialog';
import { PageHeading, SectionHeading } from '@/shared/ui/PageHeading';
import { describeError, EmptyState, ErrorState, fieldError, Loading } from '@/shared/ui/States';
import { useEntries, useRecordEntry, useReverseEntry } from './api';
import { monthPeriod, PeriodPicker, periodDays, type Period } from './PeriodPicker';

function EntryForm({ today }: { today: string }) {
  const engagements = useEngagements('artist');
  const record = useRecordEntry();
  const [kind, setKind] = useState<EntryKind>('Receipt');
  const [description, setDescription] = useState('');
  const [amount, setAmount] = useState('');
  const [date, setDate] = useState(today);
  const [engagementId, setEngagementId] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [key, setKey] = useState(newIdempotencyKey);
  const value = parseAmount(amount);

  return (
    <form
      className="form-card"
      noValidate
      onSubmit={(event) => {
        event.preventDefault();
        setError(null);
        if (!description.trim()) return setError('Descreva o lançamento.');
        if (value === null) return setError('Informe um valor positivo com até duas casas decimais.');
        if (date > today) return setError('A data efetiva não pode ser futura.');
        if (kind === 'Receipt' && !engagementId) return setError('Recebimentos precisam estar vinculados a um atendimento.');
        record.mutate(
          { key, input: { kind, description: description.trim(), amount: value, effectiveDate: date, engagementId: engagementId || null } },
          {
            onSuccess: () => {
              setDescription('');
              setAmount('');
              setKey(newIdempotencyKey());
            },
            onError: (e) => {
              if (e instanceof ApiError && !e.isTransient && !e.isSessionExpired) setKey(newIdempotencyKey());
            },
          },
        );
      }}
    >
      <h3>Novo lançamento</h3>
      <p className="muted">Registro gerencial manual. Não processa pagamentos nem cobranças.</p>
      <div className="form-grid">
        <label className="field">
          <span>Tipo</span>
          <select value={kind} onChange={(e) => setKind(e.target.value as EntryKind)}>
            <option value="Receipt">Recebimento</option>
            <option value="Cost">Custo</option>
          </select>
        </label>
        <label className="field">
          <span>Valor (R$)</span>
          <input value={amount} inputMode="decimal" onChange={(e) => setAmount(e.target.value)} placeholder="450,00" />
        </label>
        <label className="field">
          <span>Data efetiva</span>
          <input type="date" value={date} max={today} onChange={(e) => setDate(e.target.value)} />
        </label>
      </div>
      <label className="field">
        <span>Descrição</span>
        <input value={description} onChange={(e) => setDescription(e.target.value)} />
      </label>
      <label className="field">
        <span>Atendimento {kind === 'Cost' && '(opcional)'}</span>
        <select value={engagementId} onChange={(e) => setEngagementId(e.target.value)}>
          <option value="">{kind === 'Cost' ? 'Custo geral' : 'Selecione…'}</option>
          {(engagements.data ?? []).map((e) => (
            <option key={e.id} value={e.id}>
              {e.idea.slice(0, 60)}
            </option>
          ))}
        </select>
      </label>
      {(error || record.isError) && (
        <p className="notice error" role="alert">
          {error ??
            fieldError(record.error, 'amount', 'effectiveDate', 'description', 'engagementId', 'kind') ??
            `${describeError(record.error).title} ${describeError(record.error).detail}`}
        </p>
      )}
      {record.isSuccess && !error && (
        <p className="notice success" role="status">
          Lançamento registrado.
        </p>
      )}
      <div className="form-actions">
        <button type="submit" className="button red" disabled={record.isPending}>
          {record.isPending ? 'Registrando…' : 'Registrar'}
        </button>
      </div>
    </form>
  );
}

function ReverseDialog({ entry, onClose }: { entry: LedgerEntry | null; onClose: () => void }) {
  const reverse = useReverseEntry();
  const [reason, setReason] = useState('');
  const [key] = useState(newIdempotencyKey);
  return (
    <Dialog open={entry !== null} onClose={onClose} title="Estornar lançamento?" eyebrow="CORREÇÃO">
      {entry && (
        <form
          className="stack"
          onSubmit={(event) => {
            event.preventDefault();
            if (!reason.trim()) return;
            reverse.mutate({ id: entry.id, reason: reason.trim(), key }, { onSuccess: onClose });
          }}
        >
          <p>
            O estorno é integral ({money(entry.amount)}) e preserva o lançamento original na data {civilDate(entry.effectiveDate)}. Não há devolução bancária.
          </p>
          <label className="field">
            <span>Motivo</span>
            <input value={reason} onChange={(e) => setReason(e.target.value)} required />
          </label>
          {reverse.isError && <p className="notice error">{describeError(reverse.error).title}</p>}
          <button type="submit" className="button red full" disabled={!reason.trim() || reverse.isPending}>
            Confirmar estorno
          </button>
        </form>
      )}
    </Dialog>
  );
}

function Content({ session }: { session: Session }) {
  const today = todayIn(session.artistTimeZone ?? undefined);
  const [period, setPeriod] = useState<Period>(() => monthPeriod(today));
  const [kind, setKind] = useState('');
  const [reversing, setReversing] = useState<LedgerEntry | null>(null);
  const valid = periodDays(period) >= 1 && periodDays(period) <= 366;
  const entries = useEntries(period.from, period.to, kind, valid);
  const reversed = new Set((entries.data ?? []).map((e) => e.originalEntryId).filter(Boolean));

  return (
    <>
      <PageHeading eyebrow="GESTÃO DO ESTÚDIO" title="Recebimentos e custos.">
        Lançamentos manuais usados nos indicadores do painel.
      </PageHeading>
      <EntryForm today={today} />
      <SectionHeading title="Lançamentos do período" count={entries.data?.length} />
      <div className="form-card" style={{ marginBottom: 16 }}>
        <PeriodPicker value={period} onChange={setPeriod} />
        <label className="field" style={{ maxWidth: 240 }}>
          <span>Tipo</span>
          <select value={kind} onChange={(e) => setKind(e.target.value)}>
            <option value="">Todos</option>
            <option value="Receipt">Recebimentos</option>
            <option value="Cost">Custos</option>
          </select>
        </label>
      </div>
      {!valid ? null : entries.isPending ? (
        <Loading />
      ) : entries.isLoadingError ? (
        <ErrorState error={entries.error} onRetry={() => void entries.refetch()} />
      ) : entries.data.length === 0 ? (
        <EmptyState title="Nenhum lançamento neste período." />
      ) : (
        <div className="table-wrap">
          <table>
            <caption className="visually-hidden">Lançamentos do período</caption>
            <thead>
              <tr>
                <th scope="col">Data</th>
                <th scope="col">Tipo</th>
                <th scope="col">Descrição</th>
                <th scope="col" style={{ textAlign: 'right' }}>
                  Valor
                </th>
                <th scope="col">
                  <span className="visually-hidden">Ações</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {entries.data.map((entry) => (
                <tr key={entry.id}>
                  <td>{civilDate(entry.effectiveDate)}</td>
                  <td>{entry.originalEntryId ? 'Estorno' : entry.kind === 'Receipt' ? 'Recebimento' : 'Custo'}</td>
                  <td>{entry.description}</td>
                  <td className="amount">{money(entry.signedAmount)}</td>
                  <td>
                    {!entry.originalEntryId && !reversed.has(entry.id) && (
                      <button type="button" className="text-button red" onClick={() => setReversing(entry)}>
                        Estornar
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <ReverseDialog key={reversing?.id ?? 'none'} entry={reversing} onClose={() => setReversing(null)} />
    </>
  );
}

export function FinancePage() {
  return <RequireArtist>{(session) => <Content session={session} />}</RequireArtist>;
}
