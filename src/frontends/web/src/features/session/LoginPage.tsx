import { AlertTriangle, ArrowLeft, CalendarCheck, Loader2, LogIn, MessagesSquare, Search, ShieldCheck } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Link, Navigate, useSearchParams } from 'react-router';
import { safeDestination } from './destination';
import { useSession } from './session';

const highlights = [
  { icon: Search, text: 'Descubra artistas por estilo, cidade e faixa de preço.' },
  { icon: MessagesSquare, text: 'Negocie a ideia e a proposta em uma conversa própria.' },
  { icon: CalendarCheck, text: 'Agende a sessão sem conflito de horários.' },
];

/**
 * Página de login dividida 50/50 (T10.14, decisão 0017 revisada). Não há campos de credenciais:
 * a ação leva ao fluxo Authorization Code + PKCE do BFF, que devolve o navegador ao destino
 * já autenticado. Falhas do provedor voltam a esta página com ?erro=login.
 */
export function LoginPage() {
  const [params] = useSearchParams();
  const destination = safeDestination(params.get('destino'));
  const failed = params.get('erro') === 'login';
  const { status, signIn, retry } = useSession();
  const [redirecting, setRedirecting] = useState(false);

  // Voltar do provedor pelo histórico pode restaurar esta página do cache com o botão ainda bloqueado.
  useEffect(() => {
    const reset = (event: PageTransitionEvent) => {
      if (event.persisted) setRedirecting(false);
    };
    window.addEventListener('pageshow', reset);
    return () => window.removeEventListener('pageshow', reset);
  }, []);

  useEffect(() => {
    const previous = document.title;
    document.title = 'Entrar · InkLink';
    return () => {
      document.title = previous;
    };
  }, []);

  if (status === 'authenticated') return <Navigate to={destination} replace />;

  const start = () => {
    setRedirecting(true);
    signIn(destination);
  };

  return (
    <div className="login-page">
      <aside className="login-brand" aria-label="Sobre o InkLink">
        <Link className="logo" to="/" aria-label="InkLink, página inicial">
          INK<span>LINK</span>
          <i aria-hidden="true" />
        </Link>
        <div className="login-brand-copy">
          <span className="eyebrow light">Plataforma de tatuagem</span>
          <p className="login-brand-title">
            Conectando histórias.
            <br />
            <em>Marcando a pele.</em>
          </p>
          <ul className="login-highlights">
            {highlights.map(({ icon: Icon, text }) => (
              <li key={text}>
                <Icon size={18} aria-hidden="true" />
                {text}
              </li>
            ))}
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
        <div className="login-card">
          <span className="eyebrow">Entrar</span>
          <h1>Bem-vindo ao InkLink.</h1>
          <p className="muted">Entre para acompanhar pedidos, conversas, favoritos e, se você for artista, o seu estúdio.</p>

          {status === 'loading' ? (
            <div className="state state-loading" role="status" aria-live="polite">
              <Loader2 className="spin" size={22} aria-hidden="true" />
              <span>Verificando sua sessão…</span>
            </div>
          ) : status === 'error' ? (
            <div className="login-notice" role="alert">
              <AlertTriangle size={18} aria-hidden="true" />
              <span>Não foi possível verificar sua sessão agora.</span>
              <button type="button" className="button outline" onClick={retry}>
                Tentar novamente
              </button>
            </div>
          ) : (
            <>
              {failed && (
                <div className="login-notice" role="alert">
                  <AlertTriangle size={18} aria-hidden="true" />
                  <span>Não foi possível concluir o login. Tente novamente.</span>
                </div>
              )}
              <button type="button" className="button red full login-action" onClick={start} disabled={redirecting}>
                {redirecting ? <Loader2 className="spin" size={18} aria-hidden="true" /> : <LogIn size={18} aria-hidden="true" />}
                {redirecting ? 'Redirecionando…' : 'Entrar'}
              </button>
              <p className="login-status" role="status" aria-live="polite">
                {redirecting ? 'Abrindo a página segura de login do InkLink.' : ''}
              </p>
            </>
          )}

          <p className="login-security">
            <ShieldCheck size={18} aria-hidden="true" />
            <span>Você será levado à página segura do provedor de identidade do InkLink e voltará para cá já conectado. Nenhuma senha é digitada nesta página.</span>
          </p>

          <Link className="login-back" to="/">
            <ArrowLeft size={16} aria-hidden="true" />
            Voltar para Descobrir
          </Link>
        </div>
        <small className="login-footnote">Nenhuma cobrança é feita pela plataforma.</small>
      </main>
    </div>
  );
}
