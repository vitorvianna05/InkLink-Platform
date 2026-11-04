import { useQuery, useQueryClient } from '@tanstack/react-query';
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import type { ArtistCalendarContext, Profile } from '@/shared/api/contracts';
import { ApiError, getJson, onSessionExpired, resetCsrfToken } from '@/shared/api/http';

export interface Session {
  name: string | null;
  profile: Profile;
  /** Função Artist confirmada pelo backend (não há troca de personagens). */
  isArtist: boolean;
  artistTimeZone: string | null;
}

type Status = 'loading' | 'anonymous' | 'authenticated' | 'error';

interface SessionContextValue {
  status: Status;
  session: Session | null;
  expired: boolean;
  signingIn: boolean;
  popupBlocked: boolean;
  signIn: () => void;
  signOut: () => Promise<void>;
  dismissExpired: () => void;
  retry: () => void;
}

const SessionContext = createContext<SessionContextValue | null>(null);

/** Consulta a sessão do BFF sem disparar o aviso de expiração (visitantes recebem 401). */
async function readBffSession(): Promise<{ authenticated: boolean; name: string | null } | null> {
  const response = await fetch('/bff/session', { credentials: 'same-origin', headers: { Accept: 'application/json' } });
  if (response.status === 401) return null;
  if (!response.ok) throw new ApiError({ status: response.status, code: `http.${response.status}` });
  return (await response.json()) as { authenticated: boolean; name: string | null };
}

async function loadSession(): Promise<Session | null> {
  const bff = await readBffSession();
  if (!bff?.authenticated) return null;
  // O primeiro GET do perfil vincula a identidade federada e provisiona a função Client.
  const profile = await getJson<Profile>('/api/v1/profiles/me');
  let isArtist = false;
  let artistTimeZone: string | null = null;
  try {
    const context = await getJson<ArtistCalendarContext>('/api/v1/scheduling/artist-context');
    isArtist = true;
    artistTimeZone = context.timeZoneId;
  } catch (error) {
    if (!(error instanceof ApiError) || error.status !== 403) throw error;
  }
  return { name: bff.name, profile, isArtist, artistTimeZone };
}

const POLL_MS = 1500;
const LOGIN_TIMEOUT_MS = 5 * 60 * 1000;

export function SessionProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: ['session'], queryFn: loadSession, staleTime: 60_000, retry: false });
  const [expired, setExpired] = useState(false);
  const [signingIn, setSigningIn] = useState(false);
  const [popupBlocked, setPopupBlocked] = useState(false);
  const timer = useRef<number | null>(null);
  const authenticated = query.data != null;

  useEffect(
    () =>
      onSessionExpired(() => {
        if (authenticated) setExpired(true);
      }),
    [authenticated],
  );

  useEffect(() => () => {
    if (timer.current) window.clearInterval(timer.current);
  }, []);

  const completeSignIn = useCallback(async () => {
    resetCsrfToken();
    setExpired(false);
    setSigningIn(false);
    await queryClient.resetQueries({ queryKey: ['session'] });
    await queryClient.invalidateQueries();
  }, [queryClient]);

  const signIn = useCallback(() => {
    // O login acontece em uma janela própria para preservar o contexto desta página:
    // o BFF conclui o fluxo OIDC e esta aba detecta a nova sessão consultando /bff/session.
    const popup = window.open('/bff/login', 'inklink-login', 'popup,width=520,height=720');
    if (!popup) {
      setPopupBlocked(true);
      return;
    }
    setPopupBlocked(false);
    setSigningIn(true);
    const started = Date.now();
    if (timer.current) window.clearInterval(timer.current);
    timer.current = window.setInterval(async () => {
      const stop = () => {
        if (timer.current) window.clearInterval(timer.current);
        timer.current = null;
      };
      if (Date.now() - started > LOGIN_TIMEOUT_MS) {
        stop();
        setSigningIn(false);
        return;
      }
      try {
        const session = await readBffSession();
        if (session?.authenticated) {
          stop();
          if (!popup.closed) popup.close();
          await completeSignIn();
          return;
        }
      } catch {
        // Falha transitória: a próxima verificação tenta novamente.
      }
      if (popup.closed) {
        stop();
        setSigningIn(false);
      }
    }, POLL_MS);
  }, [completeSignIn]);

  const signOut = useCallback(async () => {
    try {
      const token = await fetch('/bff/antiforgery', { credentials: 'same-origin' }).then((r) =>
        r.ok ? (r.json() as Promise<{ requestToken: string }>) : null,
      );
      if (token) {
        // O BFF remove o ticket da sessão antes de redirecionar ao provedor; o redirecionamento
        // externo não é seguido pela aplicação.
        await fetch('/bff/logout', {
          method: 'POST',
          credentials: 'same-origin',
          redirect: 'manual',
          headers: { 'X-CSRF-TOKEN': token.requestToken },
        });
      }
    } finally {
      resetCsrfToken();
      setExpired(false);
      // Atualiza a sessão observada antes de descartar o restante do cache: limpar tudo primeiro
      // desliga o observador da consulta antiga e manteria a identidade anterior na tela.
      queryClient.setQueryData(['session'], null);
      queryClient.removeQueries({ predicate: (query) => query.queryKey[0] !== 'session' });
    }
  }, [queryClient]);

  const value = useMemo<SessionContextValue>(() => {
    const status: Status = query.isPending ? 'loading' : query.isError ? 'error' : authenticated ? 'authenticated' : 'anonymous';
    return {
      status,
      session: query.data ?? null,
      expired,
      signingIn,
      popupBlocked,
      signIn,
      signOut,
      dismissExpired: () => setExpired(false),
      retry: () => void query.refetch(),
    };
  }, [query, authenticated, expired, signingIn, popupBlocked, signIn, signOut]);

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession(): SessionContextValue {
  const value = useContext(SessionContext);
  if (!value) throw new Error('useSession requer SessionProvider.');
  return value;
}
