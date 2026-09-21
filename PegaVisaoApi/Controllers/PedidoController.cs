using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PegaVisaoApi.Data;
using PegaVisaoApi.DTO_s;
using PegaVisaoApi.Models;
using PegaVisaoApi.Services;
using System.Security.Claims;
using PegaVisaoApi.Services.Frete;

namespace PegaVisaoApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PedidoController : ControllerBase
    {
        private readonly PegaVisaoContext _context;
        private readonly IMapper _mapper;
        private readonly MercadoPagoService _mercadoPagoService;
        private readonly FreteService _freteService;

        public PedidoController(
            PegaVisaoContext context,
            IMapper mapper,
            MercadoPagoService mercadoPagoService,
            FreteService freteService)
        {
            _context = context;
            _mapper = mapper;
            _mercadoPagoService = mercadoPagoService;
            _freteService = freteService;
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CriarPedido(CreatePedidoDto dto)
        {
            // ==========================================
            // USUÁRIO LOGADO
            // ==========================================

            var usuarioIdClaim =
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!int.TryParse(usuarioIdClaim, out int usuarioId))
            {
                return Unauthorized("Usuário não identificado.");
            }

            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Id == usuarioId);

            if (usuario == null)
            {
                return Unauthorized("Usuário não encontrado.");
            }

            // ==========================================
            // VALIDAÇÃO DOS ITENS
            // ==========================================

            if (dto.Itens == null || dto.Itens.Count == 0)
            {
                return BadRequest(
                    "O pedido precisa possuir pelo menos um item."
                );
            }

            // ==========================================
            // CRIA PEDIDO
            // ==========================================

            var pedido = new Pedido
            {
                UsuarioId = usuarioId,
                Status = Status.Pendente,

                Cep = dto.Cep,
                Rua = dto.Rua,
                Numero = dto.Numero,
                Complemento = dto.Complemento,
                Bairro = dto.Bairro,
                Cidade = dto.Cidade,
                Estado = dto.Estado,

                FormaPagamento = dto.FormaPagamento,

                Itens = new List<ItemPedido>()
            };

            try
            {
                await _freteService.SalvarPedidoAsync(pedido, dto.CotacaoFreteId ?? Guid.Empty,
                    dto.FreteServicoId, dto.Itens, HttpContext.RequestAborted);
            }
            catch (FreteException ex)
            {
                return StatusCode(ex.Status, new { mensagem = ex.Message });
            }

            // ==========================================
            // RECARREGA O PEDIDO COM OS RELACIONAMENTOS
            // ==========================================

            var pedidoComItens = await _context.Pedidos
                .Include(p => p.Itens)
                    .ThenInclude(i => i.VariacaoProduto)
                        .ThenInclude(v => v.Produto)
                .FirstOrDefaultAsync(p => p.Id == pedido.Id);

            if (pedidoComItens == null)
            {
                return StatusCode(
                    500,
                    "Não foi possível recuperar o pedido criado."
                );
            }

            try
            {
                // ==========================================
                // PIX
                // ==========================================

                if (dto.FormaPagamento.Equals(
                    "Pix",
                    StringComparison.OrdinalIgnoreCase))
                {
                    var pix =
                        await _mercadoPagoService.CriarPixAsync(
                            pedidoComItens,
                            usuario.Email,
                            usuario.Nome
                        );

                    // ==========================================
                    // ORDERS API
                    //
                    // A resposta possui:
                    //
                    // transactions
                    //   -> payments
                    //       -> payment
                    // ==========================================

                    var transactions =
                        pix.GetProperty("transactions");

                    var payments =
                        transactions.GetProperty("payments");

                    if (payments.GetArrayLength() == 0)
                    {
                        return StatusCode(
                            502,
                            new
                            {
                                mensagem =
                                    "O Mercado Pago não retornou um pagamento para o pedido.",
                                pedidoId = pedidoComItens.Id
                            }
                        );
                    }

                    var payment = payments[0];

                    // ID do pagamento é STRING
                    var paymentId =
                        payment
                            .GetProperty("id")
                            .GetString();

                    // Status do pagamento
                    var mercadoPagoStatus =
                        payment
                            .GetProperty("status")
                            .GetString();

                    string? qrCode = null;
                    string? qrCodeBase64 = null;

                    // ==========================================
                    // DADOS DO PIX
                    // ==========================================

                    if (payment.TryGetProperty(
                        "payment_method",
                        out var paymentMethod))
                    {
                        if (paymentMethod.TryGetProperty(
                            "qr_code",
                            out var qrCodeProperty))
                        {
                            qrCode =
                                qrCodeProperty.GetString();
                        }

                        if (paymentMethod.TryGetProperty(
                            "qr_code_base64",
                            out var qrCodeBase64Property))
                        {
                            qrCodeBase64 =
                                qrCodeBase64Property.GetString();
                        }
                    }

                    // Persiste a correlação mesmo quando o QR Code não é retornado.
                    // ExecuteUpdate não sobrescreve uma confirmação concorrente do webhook.
                    var pixOrderId = pix.GetProperty("id").GetString();
                    await _context.Pedidos.Where(p => p.Id == pedidoComItens.Id)
                        .ExecuteUpdateAsync(update => update
                            .SetProperty(p => p.MercadoPagoOrderId, pixOrderId)
                            .SetProperty(p => p.MercadoPagoPaymentId, paymentId)
                            .SetProperty(p => p.MercadoPagoStatus,
                                p => p.Status == Status.Pendente ? mercadoPagoStatus : p.MercadoPagoStatus)
                            .SetProperty(p => p.PixQrCode, qrCode)
                            .SetProperty(p => p.PixQrCodeBase64, qrCodeBase64));
                    await _context.Entry(pedidoComItens).ReloadAsync();

                    // ==========================================
                    // VERIFICA SE O QR CODE FOI GERADO
                    // ==========================================

                    if (string.IsNullOrWhiteSpace(qrCode))
                    {
                        return StatusCode(
                            502,
                            new
                            {
                                mensagem =
                                    "O pagamento Pix foi criado, mas o código Pix não foi retornado pelo Mercado Pago.",
                                pedidoId = pedidoComItens.Id,
                                mercadoPagoStatus =
                                    mercadoPagoStatus
                            }
                        );
                    }

                    // ==========================================
                    // SALVA DADOS DO PIX NO PEDIDO
                    // ==========================================

                }

                // ==========================================
                // CARTÃO
                // ==========================================

                else
                {
                    var order =
                        await _mercadoPagoService.CriarOrderAsync(
                            pedidoComItens
                        );

                    var orderId =
                        order
                            .GetProperty("id")
                            .GetString();

                    var mercadoPagoStatus =
                        order
                            .GetProperty("status")
                            .GetString();

                    var checkoutUrl =
                        order
                            .GetProperty("checkout_url")
                            .GetString();

                    pedidoComItens.MercadoPagoOrderId =
                        orderId;

                    pedidoComItens.MercadoPagoStatus =
                        mercadoPagoStatus;

                    pedidoComItens.MercadoPagoCheckoutUrl =
                        checkoutUrl;

                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                // O pedido continua salvo como Pendente,
                // mas informamos que o pagamento não foi iniciado.

                return StatusCode(
                    502,
                    new
                    {
                        mensagem =
                            "O pedido foi criado, mas não foi possível iniciar o pagamento.",
                        erro = ex.Message,
                        pedidoId = pedidoComItens.Id
                    }
                );
            }

            // ==========================================
            // RETORNA PEDIDO
            // ==========================================

            var pedidoDto =
                _mapper.Map<ReadPedidoDto>(pedidoComItens);

            return CreatedAtAction(
                nameof(RecuperarPedidoPorId),
                new { id = pedidoComItens.Id },
                pedidoDto
            );
        }

        // ==========================================
        // BUSCAR PEDIDO POR ID
        // ==========================================

        [HttpGet("{id}")]
        [Authorize]
        public async Task<ActionResult<ReadPedidoDto>>
            RecuperarPedidoPorId(int id)
        {
            var usuarioIdClaim =
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!int.TryParse(usuarioIdClaim, out int usuarioId))
            {
                return Unauthorized();
            }

            var pedido = await _context.Pedidos
                .Include(p => p.Itens)
                    .ThenInclude(i => i.VariacaoProduto)
                        .ThenInclude(v => v.Produto)
                .FirstOrDefaultAsync(p =>
                    p.Id == id &&
                    p.UsuarioId == usuarioId
                );

            if (pedido == null)
            {
                return NotFound();
            }

            var pedidoDto =
                _mapper.Map<ReadPedidoDto>(pedido);

            return Ok(pedidoDto);
        }

        // ==========================================
        // LISTAR PEDIDOS DO USUÁRIO
        // ==========================================

        [HttpGet]
        [Authorize]
        public async Task<ActionResult<IEnumerable<ReadPedidoDto>>>
            RecuperarPedidos()
        {
            var usuarioIdClaim =
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!int.TryParse(usuarioIdClaim, out int usuarioId))
            {
                return Unauthorized();
            }

            var pedidos = await _context.Pedidos
                .Include(p => p.Itens)
                    .ThenInclude(i => i.VariacaoProduto)
                        .ThenInclude(v => v.Produto)
                .Where(p => p.UsuarioId == usuarioId && (!p.ExcluidoPeloCliente || p.Status != Status.Cancelado))
                .OrderByDescending(p => p.DataPedido)
                .ToListAsync();

            var pedidosDto =
                _mapper.Map<List<ReadPedidoDto>>(pedidos);

            return Ok(pedidosDto);
        }

        // ==========================================
        // ATUALIZAR PEDIDO - ADMIN
        // ==========================================

        [HttpGet("{id:int}")]
        [Authorize]
        public async Task<IActionResult> RecuperarPedido(int id)
        {
            if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var usuarioId))
                return Unauthorized();

            var pedido = await _context.Pedidos.AsNoTracking()
                .Where(p => p.Id == id && p.UsuarioId == usuarioId)
                .Select(p => new { p.Id, Status = p.Status.ToString(), p.MercadoPagoStatus,
                    p.MercadoPagoOrderId, p.MercadoPagoPaymentId })
                .SingleOrDefaultAsync();
            return pedido == null ? NotFound() : Ok(pedido);
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AtualizaPedido(
            int id,
            UpdatePedidoDto dto)
        {
            var pedido = await _context.Pedidos
                .FirstOrDefaultAsync(p => p.Id == id);

            if (pedido == null)
            {
                return NotFound();
            }

            _mapper.Map(dto, pedido);

            await _context.SaveChangesAsync();

            return NoContent();
        }

        // ==========================================
        // CANCELAR PEDIDO
        // ==========================================

        [HttpPut("{id}/cancelar")]
        [Authorize]
        public async Task<IActionResult> CancelarPedido(int id)
        {
            var usuarioIdClaim =
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!int.TryParse(usuarioIdClaim, out int usuarioId))
            {
                return Unauthorized();
            }

            var pedido = await _context.Pedidos
                .FirstOrDefaultAsync(p =>
                    p.Id == id &&
                    p.UsuarioId == usuarioId
                );

            if (pedido == null)
            {
                return NotFound(new
                {
                    mensagem = "Pedido não encontrado."
                });
            }

            if (pedido.Status != Status.Pendente)
            {
                return BadRequest(new
                {
                    mensagem =
                        "Este pedido não pode mais ser cancelado."
                });
            }

            // Não sobrescreve Pago se o webhook confirmar entre a leitura e a escrita.
            var cancelados = await _context.Pedidos
                .Where(p => p.Id == id && p.UsuarioId == usuarioId && p.Status == Status.Pendente)
                .ExecuteUpdateAsync(update => update.SetProperty(p => p.Status, Status.Cancelado));
            if (cancelados == 0)
                return BadRequest(new { mensagem = "Este pedido não pode mais ser cancelado." });

            return Ok(new
            {
                mensagem = "Pedido cancelado com sucesso.",
                pedidoId = pedido.Id,
                status = Status.Cancelado.ToString()
            });
        }

        // ==========================================
        // DELETAR PEDIDO - ADMIN
        // ==========================================

        // Exclusão da lista do cliente; preserva o registro para conciliação do pagamento.
        [HttpDelete("{id:int}/cancelado")]
        [Authorize]
        public async Task<IActionResult> ExcluirPedidoCancelado(int id, CancellationToken ct)
        {
            if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var usuarioId))
                return Unauthorized();

            // A condição é verificada no UPDATE, inclusive se o webhook alterar o status.
            var excluidos = await _context.Pedidos
                .Where(p => p.Id == id && p.UsuarioId == usuarioId && p.Status == Status.Cancelado)
                .ExecuteUpdateAsync(update => update.SetProperty(p => p.ExcluidoPeloCliente, true), ct);
            if (excluidos > 0) return NoContent();

            var existe = await _context.Pedidos.AnyAsync(p => p.Id == id && p.UsuarioId == usuarioId, ct);
            if (!existe) return NotFound(new { mensagem = "Pedido não encontrado." });
            return Conflict(new { mensagem = "Somente pedidos cancelados podem ser excluídos. Atualize a lista para conferir o status." });
        }
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeletarPedido(int id)
        {
            var pedido = await _context.Pedidos
                .FirstOrDefaultAsync(p => p.Id == id);

            if (pedido == null)
            {
                return NotFound();
            }

            _context.Pedidos.Remove(pedido);

            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
