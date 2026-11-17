import { PageHeading } from '@/shared/ui/PageHeading';

/**
 * Termos de uso e política de privacidade: texto provisório até o responsável fornecer o conteúdo
 * (decisão 0020). A versão aceita é registrada pela Identity no cadastro.
 */
export function TermsPage() {
  return (
    <article className="terms-page">
      <PageHeading eyebrow="INKLINK" title="Termos de uso e política de privacidade">
        Versão provisória. O texto definitivo será publicado antes do lançamento, e uma nova versão pedirá novo aceite quando for o caso.
      </PageHeading>
      <section className="form-card">
        <h2>O que já vale nesta versão</h2>
        <ul>
          <li>O InkLink conecta clientes a artistas de tatuagem e organiza pedidos, propostas, conversas e agenda.</li>
          <li>Usamos seu nome, e-mail e celular para identificar a conta, confirmar seus contatos e comunicar o andamento dos atendimentos.</li>
          <li>Seu celular e sua foto de cliente só são compartilhados com artistas com quem você tem atendimento.</li>
          <li>Os termos comerciais da plataforma serão descritos na versão definitiva.</li>
        </ul>
      </section>
    </article>
  );
}
