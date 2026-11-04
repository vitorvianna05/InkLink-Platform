import { describe, expect, it } from 'vitest';
import { loginPath, safeDestination } from './destination';

describe('destino após o login', () => {
  it.each([
    ['/meu-espaco', '/meu-espaco'],
    ['/estudio/agenda?dia=2030-01-15', '/estudio/agenda?dia=2030-01-15'],
    ['/artistas/a1?aba=flashes#topo', '/artistas/a1?aba=flashes#topo'],
  ])('aceita caminho interno %s', (value, expected) => {
    expect(safeDestination(value)).toBe(expected);
  });

  it.each([
    null,
    '',
    'https://evil.example/x',
    '//evil.example/x',
    '/\\evil.example',
    'javascript:alert(1)',
    'meu-espaco',
    '/entrar',
    '/entrar?destino=/x',
    '/bff/logout',
    '/BFF/session',
    '/api/v1/profiles/me',
    '/signin-oidc',
    '/a\nb',
  ])('recusa %s e volta a Descobrir', (value) => {
    expect(safeDestination(value)).toBe('/');
  });

  it('monta o endereço de login com o destino codificado', () => {
    expect(loginPath({ pathname: '/estudio/agenda', search: '?dia=2030-01-15' })).toBe('/entrar?destino=%2Festudio%2Fagenda%3Fdia%3D2030-01-15');
    expect(loginPath({ pathname: '/' })).toBe('/entrar');
    expect(loginPath({ pathname: '/entrar', search: '?destino=/x' })).toBe('/entrar');
  });
});
