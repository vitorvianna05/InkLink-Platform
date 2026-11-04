import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ArtistCard } from '@/shared/api/contracts';
import { anonymous, page, renderApp } from '@/test/render';
import { server } from '@/test/server';

const rafael: ArtistCard = { id: 'a1', name: 'Rafael Moura', studio: 'Moura Tattoo', city: 'São Paulo', styles: ['Blackwork'], startingPrice: 450 };

describe('Explorar', () => {
  it('começa sem cards e só exibe artistas após a busca explícita', async () => {
    anonymous();
    const searches: string[] = [];
    server.use(
      http.get('/api/v1/artists', () => HttpResponse.json(page([rafael]))),
      http.get('/api/v1/artists/suggestions', () => HttpResponse.json([])),
      http.get('/api/v1/artists/search', ({ request }) => {
        searches.push(new URL(request.url).search);
        return HttpResponse.json(page([rafael]));
      }),
    );
    renderApp('/explorar');
    expect(await screen.findByText('Cada história tem um traço.')).toBeInTheDocument();
    expect(screen.queryByText('Rafael Moura')).not.toBeInTheDocument();

    await userEvent.selectOptions(await screen.findByLabelText('Estilo de tatuagem'), 'Blackwork');
    await userEvent.click(screen.getByRole('button', { name: /Buscar artistas/ }));

    expect(await screen.findByRole('heading', { name: 'Rafael Moura' })).toBeInTheDocument();
    expect(screen.getByText('1 artista encontrado para sua pesquisa.')).toBeInTheDocument();
    expect(searches[0]).toContain('style=Blackwork');

    await userEvent.selectOptions(screen.getByLabelText('Cidade'), 'São Paulo');
    expect(screen.getByText(/Você alterou os critérios/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /Limpar busca/ }));
    expect(await screen.findByText('Cada história tem um traço.')).toBeInTheDocument();
  });

  it('percorre sugestões com setas, seleciona com Enter e fecha com Escape', async () => {
    anonymous();
    server.use(
      http.get('/api/v1/artists', () => HttpResponse.json(page([rafael]))),
      http.get('/api/v1/artists/suggestions', ({ request }) => {
        const term = new URL(request.url).searchParams.get('term');
        return HttpResponse.json(
          term === 'sao'
            ? [
                { category: 'city', value: 'São Paulo', relevance: 0.9 },
                { category: 'studio', value: 'Sao Studio', relevance: 0.5 },
              ]
            : [],
        );
      }),
    );
    renderApp('/explorar');
    const input = await screen.findByRole('combobox', { name: 'O que você está procurando?' });
    await userEvent.type(input, 'sao');
    const list = await screen.findByRole('listbox');
    expect(await within(list).findByText('São Paulo')).toBeInTheDocument();
    expect(within(list).getByText('Cidade')).toBeInTheDocument();

    await userEvent.keyboard('{ArrowDown}');
    expect(within(list).getByRole('option', { name: /São Paulo/ })).toHaveAttribute('aria-selected', 'true');
    await userEvent.keyboard('{Enter}');
    expect(input).toHaveValue('São Paulo');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();

    await userEvent.clear(input);
    await userEvent.type(input, 'xyz');
    expect(await screen.findByText(/Nenhuma sugestão/)).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });

  it('exibe estado vazio distinto de erro', async () => {
    anonymous();
    server.use(
      http.get('/api/v1/artists', () => HttpResponse.json(page([]))),
      http.get('/api/v1/artists/search', () => HttpResponse.json(page([]))),
    );
    renderApp('/explorar?buscar=1&q=nada');
    expect(await screen.findByText('Ainda não encontramos essa conexão.')).toBeInTheDocument();
  });

  it('informa indisponibilidade sem fingir resultado vazio', async () => {
    anonymous();
    server.use(
      http.get('/api/v1/artists', () => HttpResponse.json(page([]))),
      http.get('/api/v1/artists/search', () => HttpResponse.json({ status: 503, code: 'catalog.database_unavailable' }, { status: 503 })),
    );
    renderApp('/explorar?buscar=1');
    expect(await screen.findByText('Serviço temporariamente indisponível.')).toBeInTheDocument();
    expect(screen.queryByText('Ainda não encontramos essa conexão.')).not.toBeInTheDocument();
  });

  it('favoritar exige login de visitantes', async () => {
    anonymous();
    server.use(http.get('/api/v1/artists', () => HttpResponse.json(page([rafael]))));
    renderApp('/');
    const button = await screen.findByRole('button', { name: 'Salvar Rafael Moura nos favoritos' });
    const opened: string[] = [];
    window.open = ((url: string) => {
      opened.push(url);
      return null;
    }) as typeof window.open;
    await userEvent.click(button);
    await waitFor(() => expect(opened).toEqual(['/bff/login']));
    expect(await screen.findByText(/bloqueou a janela de login/)).toBeInTheDocument();
  });
});
