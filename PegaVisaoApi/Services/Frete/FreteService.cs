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
    // Opt-in explícito da loja; ausente/false mantém o frete normal.
    public bool SemFreteParaTeste => configuration.GetValue<bool>("Frete:DesabilitadoParaTeste");
    private string ConexaoAtual(bool semFrete) => semFrete ? "sem-frete-teste" : _options.ConexaoId;
    public async Task<object> CotarAsync(int usuarioId, string cep, List<CreateItemPedidoDto> itens, CancellationToken ct)
    {
        var semFrete = SemFreteParaTeste;
        var destino = FreteRegras.NormalizarCep(cep);
        var origem = semFrete ? "00000000" : FreteRegras.NormalizarCep(_options.CepOrigem);
        var carrinho = await PrepararCarrinhoAsync(itens, semFrete, ct);
        List<FreteOpcao> opcoes;
        if (semFrete)
            opcoes = [new(int.MaxValue, "Sem frete — teste", "Teste de pagamento", 0m, 0, null)];
        else
        {
            var token = await conexao.ObterAccessTokenAsync(ct);
            opcoes = await client.CotarAsync(token, origem, destino, carrinho, ct);
        }
        if (opcoes.Count == 0) throw new FreteException(422, "Nenhum serviço de entrega disponível para este CEP e carrinho.");
        var cotacao = new CotacaoFrete
        {
            Id = Guid.NewGuid(), UsuarioId = usuarioId, CepDestino = destino,
            ConexaoId = ConexaoAtual(semFrete), Sandbox = !semFrete && _options.Sandbox,
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
        var semFrete = SemFreteParaTeste;
        FreteRegras.ValidarCotacao(cotacao, usuarioId, cotacao.CepDestino, cotacao.CarrinhoHash, ConexaoAtual(semFrete), DateTime.UtcNow);
        return Resposta(cotacao);
    }

    public async Task SalvarPedidoAsync(Pedido pedido, Guid cotacaoId, int servicoId,
        List<CreateItemPedidoDto> itens, CancellationToken ct)
    {
        var cotacao = await BuscarAsync(cotacaoId, pedido.UsuarioId, ct);
        var semFrete = SemFreteParaTeste;
        var carrinho = await PrepararCarrinhoAsync(itens, semFrete, ct);
        var destino = FreteRegras.NormalizarCep(pedido.Cep);
        var hash = FreteRegras.HashCarrinho(carrinho, semFrete ? "00000000" : FreteRegras.NormalizarCep(_options.CepOrigem));
        FreteRegras.ValidarCotacao(cotacao, pedido.UsuarioId, destino, hash, ConexaoAtual(semFrete), DateTime.UtcNow);
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
        await new EstoqueService(db).ReservarAsync(pedido, ct);
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        logger.LogInformation("Frete vinculado ao pedido {PedidoId}: cotação {CotacaoId}, serviço {ServicoId}, valor {ValorFrete}, total {Total}",
            pedido.Id, cotacao.Id, servicoId, pedido.ValorFrete, pedido.ValorTotal);
    }

    private async Task<List<FreteItem>> PrepararCarrinhoAsync(List<CreateItemPedidoDto> itens, bool semFrete, CancellationToken ct)
    {
        var quantidades = FreteRegras.AgruparItens(itens);
        var ids = quantidades.Keys.ToArray();
        var variacoes = await db.VariacaoProdutos.AsNoTracking().Include(v => v.Produto)
            .Where(v => ids.Contains(v.Id)).ToListAsync(ct);
        if (variacoes.Count != ids.Length) throw new FreteException(400, "Um produto do carrinho não está mais disponível.");
        return variacoes.Select(v => FreteRegras.PrepararItem(v, quantidades[v.Id], exigirMedidas: !semFrete)).ToList();
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
        SemFreteParaTeste = c.ConexaoId == "sem-frete-teste",
        opcoes = Opcoes(c).Select(o => new { o.ServicoId, o.Servico, o.Transportadora, o.Valor, o.PrazoDias })
    };
}
