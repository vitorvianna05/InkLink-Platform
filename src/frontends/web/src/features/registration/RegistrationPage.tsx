import { useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Check, Loader2, Mail, ShieldCheck, Smartphone } from 'lucide-react';
import { useEffect, useId, useState, type FormEvent, type ReactNode } from 'react';
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router';
import { loginPath, safeDestination } from '@/features/session/destination';
import { useSession } from '@/features/session/session';
import { ErrorState, Loading } from '@/shared/ui/States';
import { registrationMessage, useAcceptTerms, useChangePhone, useRegistration, useSendCode, useVerifyCode, type ContactChannel, type Registration } from './api';

/** Segundos restantes até o horário informado pelo backend (reenvio liberado). */
function useCountdown(until: string | null): number {
  const [now, setNow] = useState(() => Date.now());
  const target = until ? Date.parse(until) : 0;
  useEffect(() => {
    if (target <= now) return;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [target, now]);
  return Math.max(0, Math.ceil((target - now) / 1000));
}

function Step({ number, title, done, children }: { number: number; title: string; done: boolean; children: ReactNode }) {
  return (
    <section className={done ? 'registration-step done' : 'registration-step'} aria-labelledby={`registration-step-${number}`}>
      <h2 id={`registration-step-${number}`}>
        <span className="registration-step-number" aria-hidden="true">
          {done ? <Check size={14} /> : number}
        </span>
        {title}
        {done && <span className="visually-hidden"> (concluído)</span>}
      </h2>
      {children}
    </section>
  );
}

/** Envio e conferência do código de 8 dígitos de um canal (RF23). */
function CodeStep({ channel, registration }: { channel: ContactChannel; registration: Registration }) {
  const id = useId();
  const send = useSendCode();
  const verify = useVerifyCode();
  const [code, setCode] = useState('');
  const [sent, setSent] = useState(false);
  const seconds = useCountdown(channel === 'email' ? registration.emailResendAt : registration.phoneResendAt);
  const label = channel === 'email' ? 'e-mail' : 'celular';
  const failure = verify.error ?? send.error;

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (code.length === 8) verify.mutate({ channel, code }, { onSuccess: () => setCode('') });
  };
  const request = () => send.mutate(channel, { onSuccess: () => setSent(true) });

  return (
    <form className="registration-code" onSubmit={submit} noValidate>
      <div className="row">
        <button type="button" className="button outline" onClick={request} disabled={send.isPending || seconds > 0}>
          {send.isPending ? <Loader2 className="spin" size={16} aria-hidden="true" /> : channel === 'email' ? <Mail size={16} aria-hidden="true" /> : <Smartphone size={16} aria-hidden="true" />}
          {sent || seconds > 0 ? 'Reenviar código' : 'Enviar código'}
        </button>
        {seconds > 0 && (
          <span className="muted" aria-live="polite">
            Reenvio disponível em {seconds} s
          </span>
        )}
      </div>
      {sent && !failure && (
        <p className="notice success" role="status">
          Enviamos um código de 8 dígitos para o seu {label}. Ele vale por 10 minutos.
        </p>
      )}
      <label htmlFor={`${id}-code`}>Código do {label}</label>
      <div className="row">
        <input
          id={`${id}-code`}
          className="registration-code-input"
          inputMode="numeric"
          autoComplete="one-time-code"
          pattern="[0-9]{8}"
          maxLength={8}
          value={code}
          onChange={(event) => setCode(event.target.value.replace(/\D/g, '').slice(0, 8))}
          aria-describedby={`${id}-hint`}
          aria-invalid={failure ? true : undefined}
        />
        <button type="submit" className="button red" disabled={code.length !== 8 || verify.isPending}>
          {verify.isPending ? 'Confirmando…' : 'Confirmar'}
        </button>
      </div>
      <small id={`${id}-hint`} className="muted">
        Digite ou cole os 8 dígitos recebidos.
      </small>
      {failure && (
        <p className="notice error" role="alert">
          {registrationMessage(failure)}
        </p>
      )}
    </form>
  );
}

function PhoneStep({ registration }: { registration: Registration }) {
  const id = useId();
  const change = useChangePhone();
  const [phone, setPhone] = useState(registration.phoneNumber ?? '');
  const saved = registration.phoneNumber !== null && phone === registration.phoneNumber;
  return (
    <>
      <form
        className="registration-phone"
        onSubmit={(event) => {
          event.preventDefault();
          change.mutate(phone, { onSuccess: (updated) => setPhone(updated.phoneNumber ?? '') });
        }}
        noValidate
      >
        <label htmlFor={`${id}-phone`}>Celular com DDD</label>
        <div className="row">
          <input id={`${id}-phone`} type="tel" inputMode="tel" autoComplete="tel" placeholder="(11) 98765-4321" value={phone} onChange={(event) => setPhone(event.target.value)} />
          <button type="submit" className="button outline" disabled={!phone.trim() || saved || change.isPending}>
            {change.isPending ? 'Salvando…' : saved ? 'Salvo' : 'Salvar celular'}
          </button>
        </div>
        {change.error && (
          <p className="notice error" role="alert">
            {registrationMessage(change.error)}
          </p>
        )}
      </form>
      {registration.phoneNumber && saved && <CodeStep channel="phone" registration={registration} />}
    </>
  );
}

/**
 * Confirmação do cadastro (RF22–RF23, decisão 0020): termos, e-mail e celular, em qualquer ordem.
 * A conta pendente não usa as áreas privadas até concluir; depois, volta ao destino de origem.
 */
export function RegistrationPage() {
  const [params] = useSearchParams();
  const destination = safeDestination(params.get('destino'));
  const navigate = useNavigate();
  const client = useQueryClient();
  const { status, session } = useSession();
  const registration = useRegistration(status === 'authenticated');
  const accept = useAcceptTerms();
  const [agreed, setAgreed] = useState(false);
  const termsId = useId();
  const active = registration.data?.state === 'Active';

  useEffect(() => {
    const previous = document.title;
    document.title = 'Confirmar cadastro · InkLink';
    return () => {
      document.title = previous;
    };
  }, []);

  // Cadastro concluído: a sessão é recarregada (sai do estado pendente) e o usuário volta ao destino.
  useEffect(() => {
    if (!active || !session?.pending) return;
    void client.refetchQueries({ queryKey: ['session'] });
  }, [active, session?.pending, client]);

  if (status === 'anonymous') return <Navigate to={loginPath({ pathname: '/cadastro/confirmar', search: params.toString() ? `?${params}` : '' })} replace />;

  const data = registration.data;
  return (
    <div className="login-page">
      <aside className="login-brand" aria-label="Sobre o InkLink">
        <Link className="logo" to="/" aria-label="InkLink, página inicial">
          INK<span>LINK</span>
          <i aria-hidden="true" />
        </Link>
        <div className="login-brand-copy">
          <span className="eyebrow light">Sua conta</span>
          <p className="login-brand-title">
            Falta pouco.
            <br />
            <em>Confirme seus contatos.</em>
          </p>
          <ul className="login-highlights">
            <li>
              <ShieldCheck size={18} aria-hidden="true" />
              Os códigos têm 8 dígitos, valem por 10 minutos e servem uma única vez.
            </li>
          </ul>
        </div>
        <div className="login-art" aria-hidden="true">
          <span className="ring ring-1" />
          <span className="ring ring-2" />
          <span className="ring ring-3" />
          <span className="line line-1" />
          <span className="line line-2" />
          <span className="drop" />
        </div>
      </aside>

      <main className="login-panel" id="conteudo" tabIndex={-1}>
        <div className="login-card registration-card">
          <span className="eyebrow">Cadastro</span>
          <h1>Confirme seu cadastro.</h1>
          {status === 'loading' || registration.isPending ? (
            <Loading label="Carregando seu cadastro…" />
          ) : registration.isLoadingError || !data ? (
            <ErrorState error={registration.error} onRetry={() => void registration.refetch()} />
          ) : active ? (
            <div className="stack">
              <p className="notice success" role="status">
                Cadastro concluído. Sua conta está pronta para usar.
              </p>
              <button type="button" className="button red full" onClick={() => navigate(destination === '/' ? '/meu-espaco' : destination, { replace: true })} disabled={session?.pending}>
                {session?.pending ? 'Atualizando sua sessão…' : 'Continuar'}
              </button>
            </div>
          ) : (
            <>
              <p className="muted">Para usar a sua conta, aceite os termos e confirme o e-mail e o celular. As etapas podem ser feitas em qualquer ordem.</p>
              <Step number={1} title="Termos de uso" done={data.termsAccepted}>
                {data.termsAccepted ? (
                  <p className="muted">Versão {data.termsVersion} aceita.</p>
                ) : (
                  <form
                    onSubmit={(event) => {
                      event.preventDefault();
                      if (agreed) accept.mutate(data.termsVersion);
                    }}
                  >
                    <div className="registration-terms">
                      <input id={termsId} type="checkbox" checked={agreed} onChange={(event) => setAgreed(event.target.checked)} />
                      <label htmlFor={termsId}>
                        Li e aceito os{' '}
                        <Link to="/termos" target="_blank" rel="noreferrer">
                          Termos de uso e a Política de privacidade
                        </Link>{' '}
                        (versão {data.termsVersion}).
                      </label>
                    </div>
                    <button type="submit" className="button outline" disabled={!agreed || accept.isPending}>
                      {accept.isPending ? 'Registrando…' : 'Aceitar termos'}
                    </button>
                    {accept.error && (
                      <p className="notice error" role="alert">
                        {registrationMessage(accept.error)}
                      </p>
                    )}
                  </form>
                )}
              </Step>
              <Step number={2} title="E-mail" done={data.emailConfirmed}>
                <p className="muted">{data.email ?? 'O provedor de identidade não informou o e-mail desta conta.'}</p>
                {!data.emailConfirmed && data.email && <CodeStep channel="email" registration={data} />}
              </Step>
              <Step number={3} title="Celular" done={data.phoneConfirmed}>
                {data.phoneConfirmed ? <p className="muted">{data.phoneNumber}</p> : <PhoneStep registration={data} />}
              </Step>
            </>
          )}
          <Link className="login-back" to="/">
            <ArrowLeft size={16} aria-hidden="true" />
            Voltar para Descobrir
          </Link>
        </div>
      </main>
    </div>
  );
}
