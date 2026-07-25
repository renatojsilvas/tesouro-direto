namespace TesouroDireto.Application.Projecoes;

/// <summary>
/// De onde veio a projeção de mercado usada numa simulação: direto do BCB Focus
/// (<see cref="Bcb"/>) ou de uma projeção previamente obtida, servida como fallback
/// porque o BCB estava indisponível (<see cref="CacheFallback"/>). Nunca é implícito —
/// todo <see cref="ProjecaoMercado"/> carrega essa informação explicitamente.
/// </summary>
public enum OrigemProjecao
{
    Bcb,
    CacheFallback
}
