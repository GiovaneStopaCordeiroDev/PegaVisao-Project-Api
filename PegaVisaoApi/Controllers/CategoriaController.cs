using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using PegaVisaoApi.Data;
using PegaVisaoApi.DTO_s;
using PegaVisaoApi.Models;

namespace PegaVisaoApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class CategoriaController : ControllerBase
    {
        private PegaVisaoContext _context;
        private readonly IMapper _mapper;


        public CategoriaController(PegaVisaoContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public IActionResult CriarCategoria(CreateCategoriaDto dto)
        {
            Categoria Categoria = _mapper.Map<Categoria>(dto);
            _context.Categorias.Add(Categoria);
            _context.SaveChanges();
            return CreatedAtAction(nameof(RecuperarCategoriaPorId), new { id = Categoria.Id }, Categoria);
        }

        [HttpGet("{id}")]
        public ActionResult<Categoria> RecuperarCategoriaPorId(int id)
        {
            var categoria = _context.Categorias.Include(p => p.Produtos).ThenInclude(v => v.Variacoes).FirstOrDefault(c => c.Id == id);
            if (categoria == null)
            {
                return NotFound();
            }

            var categoriaDto = _mapper.Map<ReadCategoriaDto>(categoria);
            return Ok(categoriaDto);
        }

        [HttpGet]
        public ActionResult<IEnumerable<ReadCategoriaDto>> RecuperarCategorias()
        {
            var categorias = _context.Categorias.Include(p => p.Produtos).ThenInclude(v => v.Variacoes).ToList();
            var categoriasDto = categorias.Select(c => _mapper.Map<ReadCategoriaDto>(c));
            return Ok(categoriasDto);
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public IActionResult AtualizaCategoria(int id, UpdateCategoriaDto dto)
        {
            var categoria = _context.Categorias.FirstOrDefault(c => c.Id == id);
            if (categoria == null) return NotFound();
            _mapper.Map(dto, categoria);
            _context.SaveChanges();
            return NoContent();
        }


        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public IActionResult DeletarCategoria(int id)
        {
            var categoria = _context.Categorias.FirstOrDefault(c => c.Id == id);
            if (categoria == null) return NotFound();
            _context.Categorias.Remove(categoria);
            _context.SaveChanges();
            return NoContent();
        }
    }
}

