// /cadastro não é destino final: a confirmação devolve ao destino original (decisão 0020).
const BLOCKED_PREFIXES = ['/entrar', '/cadastro', '/bff', '/api', '/signin-oidc', '/signout-callback-oidc'];
const ORIGIN = 'https://inklink.invalid';

const hasControlCharacter = (value: string) => [...value].some((character) => character.charCodeAt(0) < 0x20 || character.charCodeAt(0) === 0x7f);

/**
 * Normaliza o destino após o login (decisão 0017): somente caminhos internos do website.
 * Endereços externos, protocolo relativo, barras invertidas e rotas do BFF/API voltam a Descobrir.
 */
export function safeDestination(value: string | null | undefined): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.includes('\\') || hasControlCharacter(value)) return '/';
  let url: URL;
  try {
    url = new URL(value, ORIGIN);
  } catch {
    return '/';
  }
  if (url.origin !== ORIGIN) return '/';
  const path = url.pathname.toLowerCase();
  if (BLOCKED_PREFIXES.some((prefix) => path === prefix || path.startsWith(`${prefix}/`))) return '/';
  return `${url.pathname}${url.search}${url.hash}`;
}

/** Endereço da página de login preservando o destino atual. */
export function loginPath(location: { pathname: string; search?: string; hash?: string }): string {
  const destination = safeDestination(`${location.pathname}${location.search ?? ''}${location.hash ?? ''}`);
  return destination === '/' ? '/entrar' : `/entrar?destino=${encodeURIComponent(destination)}`;
}

/** Página de confirmação do cadastro, que devolve ao destino seguro depois de concluída. */
export function confirmationPath(destination: string | null | undefined): string {
  const target = safeDestination(destination);
  return target === '/' ? '/cadastro/confirmar' : `/cadastro/confirmar?destino=${encodeURIComponent(target)}`;
}
