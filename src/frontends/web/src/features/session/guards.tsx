import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { Dialog } from '@/shared/ui/Dialog';
import { EmptyState, ErrorState, Loading, SignInPrompt } from '@/shared/ui/States';
import { useSession, type Session } from './session';

/** Renderiza o conteúdo apenas com sessão válida; visitantes recebem o convite para entrar. */
export function RequireSession({ message, children }: { message: string; children: (session: Session) => ReactNode }) {
  const { status, session, signIn, retry } = useSession();
  if (status === 'loading') return <Loading label="Verificando sua sessão…" />;
  if (status === 'error') return <ErrorState error={null} onRetry={retry} />;
  if (!session) return <SignInPrompt message={message} onSignIn={signIn} />;
  return <>{children(session)}</>;
}

/** Área do artista: exige a função Artist confirmada pelo backend. */
export function RequireArtist({ children }: { children: (session: Session) => ReactNode }) {
  return (
    <RequireSession message="Entre com sua conta de artista para acessar o estúdio.">
      {(session) =>
        session.isArtist ? (
          children(session)
        ) : (
          <EmptyState title="Esta área é exclusiva para artistas.">
            <p>Sua conta ainda não possui a função de artista. A ativação é feita pela operação do InkLink.</p>
            <Link className="button outline" to="/meu-espaco">
              Ir para Meu espaço
            </Link>
          </EmptyState>
        )
      }
    </RequireSession>
  );
}

/** Avisos globais de sessão: expiração e bloqueio da janela de login. */
export function SessionNotices() {
  const { expired, signIn, signingIn, dismissExpired, popupBlocked } = useSession();
  return (
    <>
      <Dialog open={expired} title="Sua sessão expirou" eyebrow="SEGURANÇA" onClose={dismissExpired}>
        <p>Por segurança, a sessão do InkLink dura poucos minutos. Entre novamente para continuar de onde parou; nada desta página será perdido.</p>
        <button type="button" className="button red full" onClick={signIn} disabled={signingIn} data-autofocus>
          {signingIn ? 'Aguardando login…' : 'Entrar novamente'}
        </button>
      </Dialog>
      {popupBlocked && (
        <div className="toast toast-warning" role="alert">
          O navegador bloqueou a janela de login. Permita pop-ups para este site e tente novamente.
        </div>
      )}
    </>
  );
}
