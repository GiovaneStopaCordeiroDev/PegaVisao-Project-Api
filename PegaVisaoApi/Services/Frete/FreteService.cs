using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PegaVisaoApi.Data;
using PegaVisaoApi.DTO_s;
using PegaVisaoApi.Models;
using PegaVisaoApi.Services.MelhorEnvio;

namespace PegaVisaoApi.Services.Frete;

public sealed class FreteService(PegaVisaoContext db, MelhorEnvioService conexao,
    MelhorEnvioFreteClient client, IOptions<MelhorEnvioOptions> options, IConfiguration configuration,
    ILogger<FreteService> logger)
{
    private readonly MelhorEnvioOptions _options = options.Value;

    public async Task<object> CotarAsync(int usuarioId, string cep, List<CreateItemPedidoDto> itens, CancellationToken ct)
    {
        var destino = FreteRegras.NormalizarCep(cep);
        var origem = FreteRegras.NormalizarCep(_options.CepOrigem);
        var carrinho = await PrepararCarrinhoAsync(itens, ct);
        var token = await conexao.ObterAccessTokenAsync(ct);
        var opcoes = await client.CotarAsync(token, origem, destino, carrinho, ct);
        if (opcoes.Count == 0) throw new FreteException(422, "Nenhum serviço de entrega disponível para este CEP e carrinho.");
        var cotacao = new CotacaoFrete
        {
            Id = Guid.NewGuid(), UsuarioId = usuarioId, CepDestino = destino,
            ConexaoId = _options.ConexaoId, Sandbox = _options.Sandbox,
            CarrinhoHash = FreteRegras.HashCarrinho(carrinho, origem),
            Subtotal = carrinho.Sum(i => i.Preco * i.Quantidade),
            OpcoesJson = JsonSerializer.Serialize(opcoes), CriadaEm = DateTime.UtcNow,
            ExpiraEm = DateTime.UtcNow.AddMinutes(15)
        };
        db.CotacoesFrete.Add(cotacao);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Frete cotado: {CotacaoId}, {Quantidade} serviços, sandbox {Sandbox}", cotacao.Id, opcoes.Count, cotacao.Sandbox);
        return Resposta(cotacao);
    }

    public async Task<object> ConsultarAsync(Guid id, int usuarioId, CancellationToken ct)
    {
        var cotacao = await BuscarAsync(id, usuarioId, ct);
        FreteRegras.ValidarCotacao(cotacao, usuarioId, cotacao.CepDestino, cotacao.CarrinhoHash, _options.ConexaoId, DateTime.UtcNow);
        return Resposta(cotacao);
    }

    public async Task SalvarPedidoAsync(Pedido pedido, Guid cotacaoId, int servicoId,
        List<CreateItemPedidoDto> itens, CancellationToken ct)
    {
        var cotacao = await BuscarAsync(cotacaoId, pedido.UsuarioId, ct);
        var carrinho = await PrepararCarrinhoAsync(itens, ct);
        var destino = FreteRegras.NormalizarCep(pedido.Cep);
        var hash = FreteRegras.HashCarrinho(carrinho, FreteRegras.NormalizarCep(_options.CepOrigem));
        FreteRegras.ValidarCotacao(cotacao, pedido.UsuarioId, destino, hash, _options.ConexaoId, DateTime.UtcNow);
        if (cotacao.Sandbox && !configuration.GetValue<bool>("MercadoPago:PixTeste"))
            throw new FreteException(409, "O frete está em Sandbox. Para cobrar de verdade, conecte o Melhor Envio em produção. Para testar, configure também o pagamento em ambiente de teste.");
        if (cotacao.Sandbox && !string.Equals(pedido.FormaPagamento, "Pix", StringComparison.OrdinalIgnoreCase))
            throw new FreteException(409, "Nesta etapa, os pagamentos com frete Sandbox são testados via Pix de teste.");
        var opcao = Opcoes(cotacao).SingleOrDefault(o => o.ServicoId == servicoId)
            ?? throw new FreteException(400, "Escolha uma opção de entrega válida para esta cotação.");
        pedido.Cep = destino;
        pedido.ValorFrete = opcao.Valor;
        pedido.FreteServicoId = opcao.ServicoId;
        pedido.FreteServico = opcao.Servico;
        pedido.FreteTransportadora = opcao.Transportadora;
        pedido.FretePrazoDias = opcao.PrazoDias;
        pedido.FreteSandbox = cotacao.Sandbox;
        pedido.FreteCotacaoId = cotacao.Id;
        pedido.FreteVolumesJson = opcao.VolumesJson;
        pedido.Itens = carrinho.Select(i => new ItemPedido
        {
            VariacaoProdutoId = i.VariacaoId, Quantidade = i.Quantidade, PrecoUnitario = i.Preco
        }).ToList();
        pedido.ValorTotal = carrinho.Sum(i => i.Preco * i.Quantidade) + opcao.Valor;

        // Reserva a cotação e cria o pedido na mesma transação. Clique/requisição duplicados
        // não criam duas cobranças com a mesma cotação. Nenhuma chamada externa sob esse bloqueio.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var consumidas = await db.CotacoesFrete.Where(c => c.Id == cotacao.Id &&
            c.UsuarioId == pedido.UsuarioId && c.ConsumidaEm == null && c.ExpiraEm > DateTime.UtcNow)
            .ExecuteUpdateAsync(update => update.SetProperty(c => c.ConsumidaEm, DateTime.UtcNow), ct);
        if (consumidas != 1) throw new FreteException(409, "Cotação vencida ou já utilizada. Confira Meus pedidos e recalcule o frete.");
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        logger.LogInformation("Frete vinculado ao pedido {PedidoId}: cotação {CotacaoId}, serviço {ServicoId}, valor {ValorFrete}, total {Total}",
            pedido.Id, cotacao.Id, servicoId, pedido.ValorFrete, pedido.ValorTotal);
    }

    private async Task<List<FreteItem>> PrepararCarrinhoAsync(List<CreateItemPedidoDto> itens, CancellationToken ct)
    {
        var quantidades = FreteRegras.AgruparItens(itens);
        var ids = quantidades.Keys.ToArray();
        var variacoes = await db.VariacaoProdutos.AsNoTracking().Include(v => v.Produto)
            .Where(v => ids.Contains(v.Id)).ToListAsync(ct);
        if (variacoes.Count != ids.Length) throw new FreteException(400, "Um produto do carrinho não está mais disponível.");
        return variacoes.Select(v => FreteRegras.PrepararItem(v, quantidades[v.Id])).ToList();
    }

    private async Task<CotacaoFrete> BuscarAsync(Guid id, int usuarioId, CancellationToken ct) =>
        await db.CotacoesFrete.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.UsuarioId == usuarioId, ct)
        ?? throw new FreteException(404, "Cotação não encontrada. Volte ao checkout para calcular o frete.");

    private static List<FreteOpcao> Opcoes(CotacaoFrete cotacao) =>
        JsonSerializer.Deserialize<List<FreteOpcao>>(cotacao.OpcoesJson)
        ?? throw new FreteException(503, "Não foi possível recuperar a cotação.");

    private static object Resposta(CotacaoFrete c) => new
    {
        c.Id, c.CepDestino, c.Subtotal, c.ExpiraEm, c.Sandbox,
        opcoes = Opcoes(c).Select(o => new { o.ServicoId, o.Servico, o.Transportadora, o.Valor, o.PrazoDias })
    };
}
