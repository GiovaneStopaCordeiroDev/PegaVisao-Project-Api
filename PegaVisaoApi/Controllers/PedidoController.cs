using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PegaVisaoApi.Data;
using PegaVisaoApi.DTO_s;
using PegaVisaoApi.Models;
using PegaVisaoApi.Services;
using System.Security.Claims;

namespace PegaVisaoApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PedidoController : ControllerBase
    {
        private readonly PegaVisaoContext _context;
        private readonly IMapper _mapper;
        private readonly MercadoPagoService _mercadoPagoService;

        public PedidoController(
            PegaVisaoContext context,
            IMapper mapper,
            MercadoPagoService mercadoPagoService)
        {
            _context = context;
            _mapper = mapper;
            _mercadoPagoService = mercadoPagoService;
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CriarPedido(CreatePedidoDto dto)
        {
            // Pega o ID do usuário diretamente do JWT
            var usuarioIdClaim =
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!int.TryParse(usuarioIdClaim, out int usuarioId))
            {
                return Unauthorized("Usuário não identificado.");
            }

            // Verifica se o usuário realmente existe
            var usuarioExiste = await _context.Usuarios
                .AnyAsync(u => u.Id == usuarioId);

            if (!usuarioExiste)
            {
                return Unauthorized("Usuário não encontrado.");
            }

            // Verifica se existem itens
            if (dto.Itens == null || dto.Itens.Count == 0)
            {
                return BadRequest(
                    "O pedido precisa possuir pelo menos um item."
                );
            }

            // Cria o pedido
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

            decimal valorTotal = 0;

            foreach (var itemDto in dto.Itens)
            {
                if (itemDto.Quantidade <= 0)
                {
                    return BadRequest(
                        "A quantidade deve ser maior que zero."
                    );
                }

                var variacao = await _context.VariacaoProdutos
                    .Include(v => v.Produto)
                    .FirstOrDefaultAsync(v =>
                        v.Id == itemDto.VariacaoProdutoId
                    );

                if (variacao == null)
                {
                    return BadRequest(
                        $"Variação de produto com ID {itemDto.VariacaoProdutoId} não encontrada."
                    );
                }

                if (variacao.Produto == null)
                {
                    return BadRequest(
                        $"Produto da variação {variacao.Id} não encontrado."
                    );
                }

                var itemPedido = new ItemPedido
                {
                    VariacaoProdutoId = variacao.Id,
                    Quantidade = itemDto.Quantidade,
                    PrecoUnitario = variacao.Produto.Preco
                };

                pedido.Itens.Add(itemPedido);

                valorTotal +=
                    itemPedido.PrecoUnitario *
                    itemPedido.Quantidade;
            }

            // O preço é calculado pelo backend
            pedido.ValorTotal = valorTotal;

            // Salva primeiro para gerar o ID do pedido
            _context.Pedidos.Add(pedido);

            await _context.SaveChangesAsync();

            // Recarrega o pedido com todos os relacionamentos
            // necessários para criar a Order no Mercado Pago
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
                // Cria a Order no Mercado Pago
                var order =
                    await _mercadoPagoService.CriarOrderAsync(
                        pedidoComItens
                    );

                // Pega os dados retornados pelo Mercado Pago
                var orderId = order
                    .GetProperty("id")
                    .GetString();

                var mercadoPagoStatus = order
                    .GetProperty("status")
                    .GetString();

                var checkoutUrl = order
                    .GetProperty("checkout_url")
                    .GetString();

                // Salva os dados do Mercado Pago no pedido
                pedidoComItens.MercadoPagoOrderId = orderId;
                pedidoComItens.MercadoPagoStatus = mercadoPagoStatus;
                pedidoComItens.MercadoPagoCheckoutUrl = checkoutUrl;

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // O pedido continua salvo como Pendente,
                // mas avisamos o frontend que o pagamento não foi criado.
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

            var pedidoDto =
                _mapper.Map<ReadPedidoDto>(pedidoComItens);

            return CreatedAtAction(
                nameof(RecuperarPedidoPorId),
                new { id = pedidoComItens.Id },
                pedidoDto
            );
        }

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
                .Where(p => p.UsuarioId == usuarioId)
                .OrderByDescending(p => p.DataPedido)
                .ToListAsync();

            var pedidosDto =
                _mapper.Map<List<ReadPedidoDto>>(pedidos);

            return Ok(pedidosDto);
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

            pedido.Status = Status.Cancelado;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                mensagem = "Pedido cancelado com sucesso.",
                pedidoId = pedido.Id,
                status = pedido.Status.ToString()
            });
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