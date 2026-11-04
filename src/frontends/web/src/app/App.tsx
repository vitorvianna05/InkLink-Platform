import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useState } from 'react';
import { BrowserRouter, Link, Route, Routes } from 'react-router';
import { ArtistPage } from '@/features/catalog/ArtistPage';
import { DiscoverPage } from '@/features/catalog/DiscoverPage';
import { ExplorePage } from '@/features/catalog/ExplorePage';
import { PortfolioPage } from '@/features/catalog/PortfolioPage';
import { EngagementPage } from '@/features/engagements/EngagementPage';
import { MySpacePage } from '@/features/engagements/MySpacePage';
import { FinancePage } from '@/features/finance/FinancePage';
import { StudioDashboardPage } from '@/features/finance/StudioDashboardPage';
import { ConversationPage } from '@/features/messaging/ConversationPage';
import { ConversationsPage } from '@/features/messaging/ConversationsPage';
import { AgendaPage } from '@/features/scheduling/AgendaPage';
import { SessionProvider } from '@/features/session/session';
import { ApiError } from '@/shared/api/http';
import { EmptyState } from '@/shared/ui/States';
import { Layout } from './Layout';

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 15_000,
        refetchOnWindowFocus: false,
        // Erros de cliente (4xx) não são repetidos; falhas transitórias têm uma nova tentativa.
        retry: (count, error) => count < 1 && (!(error instanceof ApiError) || error.isTransient),
      },
      mutations: { retry: false },
    },
  });
}

function NotFound() {
  return (
    <EmptyState title="Vamos encontrar sua próxima conexão?">
      <p>Este endereço não existe ou não está disponível.</p>
      <Link className="button red" to="/">
        Voltar ao início
      </Link>
    </EmptyState>
  );
}

export function AppRoutes() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<DiscoverPage />} />
        <Route path="explorar" element={<ExplorePage />} />
        <Route path="artistas/:artistId" element={<ArtistPage />} />
        <Route path="meu-espaco" element={<MySpacePage />} />
        <Route path="atendimentos/:engagementId" element={<EngagementPage perspective="client" />} />
        <Route path="mensagens" element={<ConversationsPage perspective="client" />} />
        <Route path="mensagens/:conversationId" element={<ConversationPage perspective="client" />} />
        <Route path="estudio">
          <Route index element={<StudioDashboardPage />} />
          <Route path="agenda" element={<AgendaPage />} />
          <Route path="atendimentos/:engagementId" element={<EngagementPage perspective="artist" />} />
          <Route path="mensagens" element={<ConversationsPage perspective="artist" />} />
          <Route path="mensagens/:conversationId" element={<ConversationPage perspective="artist" />} />
          <Route path="financeiro" element={<FinancePage />} />
          <Route path="portfolio" element={<PortfolioPage />} />
        </Route>
        <Route path="*" element={<NotFound />} />
      </Route>
    </Routes>
  );
}

export function App() {
  const [client] = useState(createQueryClient);
  return (
    <QueryClientProvider client={client}>
      <BrowserRouter>
        <SessionProvider>
          <AppRoutes />
        </SessionProvider>
      </BrowserRouter>
    </QueryClientProvider>
  );
}
