using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PegaVisaoApi.Data;
using PegaVisaoApi.DTO_s;
using PegaVisaoApi.Models;
using System.Security.Claims;

namespace PegaVisaoApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PedidoController : ControllerBase
    {
        private readonly PegaVisaoContext _context;
        private readonly IMapper _mapper;

        public PedidoController(PegaVisaoContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        [HttpPost]
        [Authorize]
        public IActionResult CriarPedido(CreatePedidoDto dto)
        {
            // Pega o ID do usuário diretamente do JWT
            var usuarioIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!int.TryParse(usuarioIdClaim, out int usuarioId))
            {
                return Unauthorized("Usuário não identificado.");
            }

            // Diagnóstico temporário
            Console.WriteLine($"UsuarioId vindo do JWT: {usuarioId}");

            // Verifica se o usuário realmente existe
            var usuarioExiste = _context.Usuarios
                .Any(u => u.Id == usuarioId);

            Console.WriteLine($"Usuário encontrado no banco: {usuarioExiste}");

            if (!usuarioExiste)
            {
                return Unauthorized("Usuário não encontrado.");
            }

            if (dto.Itens == null || dto.Itens.Count == 0)
            {
                return BadRequest("O pedido precisa possuir pelo menos um item.");
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

                var variacao = _context.VariacaoProdutos
                    .Include(v => v.Produto)
                    .FirstOrDefault(v =>
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

            // O valor é calculado pelo servidor.
            // Nunca confiamos no preço enviado pelo frontend.
            pedido.ValorTotal = valorTotal;

            _context.Pedidos.Add(pedido);
            _context.SaveChanges();

            var pedidoDto = _mapper.Map<ReadPedidoDto>(pedido);

            return CreatedAtAction(
                nameof(RecuperarPedidoPorId),
                new { id = pedido.Id },
                pedidoDto
            );
        }

        [HttpGet("{id}")]
        [Authorize]
        public ActionResult<ReadPedidoDto> RecuperarPedidoPorId(int id)
        {
            var usuarioIdClaim =
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!int.TryParse(usuarioIdClaim, out int usuarioId))
            {
                return Unauthorized();
            }

            var pedido = _context.Pedidos
                .Include(p => p.Itens)
                    .ThenInclude(i => i.VariacaoProduto)
                        .ThenInclude(v => v.Produto)
                .FirstOrDefault(p =>
                    p.Id == id &&
                    p.UsuarioId == usuarioId
                );

            if (pedido == null)
            {
                return NotFound();
            }

            var pedidoDto = _mapper.Map<ReadPedidoDto>(pedido);

            return Ok(pedidoDto);
        }

        [HttpGet]
        [Authorize]
        public ActionResult<IEnumerable<ReadPedidoDto>> RecuperarPedidos()
        {
            var usuarioIdClaim =
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!int.TryParse(usuarioIdClaim, out int usuarioId))
            {
                return Unauthorized();
            }

            var pedidos = _context.Pedidos
                .Include(p => p.Itens)
                    .ThenInclude(i => i.VariacaoProduto)
                        .ThenInclude(v => v.Produto)
                .Where(p => p.UsuarioId == usuarioId)
                .OrderByDescending(p => p.DataPedido)
                .ToList();

            var pedidosDto =
                _mapper.Map<List<ReadPedidoDto>>(pedidos);

            return Ok(pedidosDto);
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public IActionResult AtualizaPedido(
            int id,
            UpdatePedidoDto dto)
        {
            var pedido = _context.Pedidos
                .FirstOrDefault(p => p.Id == id);

            if (pedido == null)
            {
                return NotFound();
            }

            _mapper.Map(dto, pedido);

            _context.SaveChanges();

            return NoContent();
        }

        [HttpPut("{id}/cancelar")]
        [Authorize]
        public IActionResult CancelarPedido(int id)
        {
            var usuarioIdClaim =
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!int.TryParse(usuarioIdClaim, out int usuarioId))
            {
                return Unauthorized();
            }

            var pedido = _context.Pedidos
                .FirstOrDefault(p =>
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
                    mensagem = "Este pedido não pode mais ser cancelado."
                });
            }

            pedido.Status = Status.Cancelado;

            _context.SaveChanges();

            return Ok(new
            {
                mensagem = "Pedido cancelado com sucesso.",
                pedidoId = pedido.Id,
                status = pedido.Status.ToString()
            });
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public IActionResult DeletarPedido(int id)
        {
            var pedido = _context.Pedidos
                .FirstOrDefault(p => p.Id == id);

            if (pedido == null)
            {
                return NotFound();
            }

            _context.Pedidos.Remove(pedido);

            _context.SaveChanges();

            return NoContent();
        }
    }
}