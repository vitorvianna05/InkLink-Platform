/** Navegação de página inteira, isolada em um objeto para que os testes possam observá-la (jsdom não navega). */
export const browser = {
  assign(url: string): void {
    window.location.assign(url);
  },
};
