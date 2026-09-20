using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PegaVisaoApi.Data;
using PegaVisaoApi.Data.Dtos.AuthDtos;
using PegaVisaoApi.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace PegaVisaoApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly PegaVisaoContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(
            PegaVisaoContext context,
            IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpPost("registrar")]
        public async Task<IActionResult> Registrar(CreateUsuarioDto dto)
        {
            var emailExiste = await _context.Usuarios
                .AnyAsync(u => u.Email == dto.Email);

            if (emailExiste)
            {
                return BadRequest(new
                {
                    mensagem = "Este e-mail já está cadastrado."
                });
            }

            var usuario = new Usuario
            {
                Nome = dto.Nome,
                Email = dto.Email,
                SenhaHash = BCrypt.Net.BCrypt.HashPassword(dto.Senha),
                IsAdmin = false
            };

            _context.Usuarios.Add(usuario);

            await _context.SaveChangesAsync();

            return Created("", new
            {
                mensagem = "Usuário cadastrado com sucesso."
            });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Email == dto.Email);

            if (usuario == null)
            {
                return Unauthorized(new
                {
                    mensagem = "E-mail ou senha inválidos."
                });
            }

            var senhaValida = BCrypt.Net.BCrypt.Verify(
                dto.Senha,
                usuario.SenhaHash
            );

            if (!senhaValida)
            {
                return Unauthorized(new
                {
                    mensagem = "E-mail ou senha inválidos."
                });
            }

            var token = GerarToken(usuario);

            return Ok(new UsuarioLogadoDto
            {
                Id = usuario.Id,
                Nome = usuario.Nome,
                Email = usuario.Email,
                IsAdmin = usuario.IsAdmin,
                Token = token
            });
        }

        // TEMPORÁRIO: usado para recuperar a senha do administrador.
        // Remova este endpoint depois de recuperar o acesso.
        [HttpPut("resetar-senha-admin")]
        public async Task<IActionResult> ResetarSenhaAdmin()
        {
            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u =>
                    u.Email == "admin@pegavisao.com");

            if (usuario == null)
            {
                return NotFound(new
                {
                    mensagem = "Administrador não encontrado."
                });
            }

            var novaSenha = "Admin@123456";

            usuario.SenhaHash =
                BCrypt.Net.BCrypt.HashPassword(novaSenha);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                mensagem = "Senha do administrador alterada com sucesso."
            });
        }

        private string GerarToken(Usuario usuario)
        {
            var claims = new List<Claim>
            {
                new Claim(
                    ClaimTypes.NameIdentifier,
                    usuario.Id.ToString()
                ),

                new Claim(
                    ClaimTypes.Name,
                    usuario.Nome
                ),

                new Claim(
                    ClaimTypes.Email,
                    usuario.Email
                ),

                new Claim(
                    ClaimTypes.Role,
                    usuario.IsAdmin ? "Admin" : "Cliente"
                )
            };

            var chave = _configuration["Jwt:Key"];

            if (string.IsNullOrEmpty(chave))
            {
                throw new InvalidOperationException(
                    "A chave JWT não foi configurada."
                );
            }

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(chave)
            );

            var credentials = new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256
            );

            var token = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.UtcNow.AddHours(8),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler()
                .WriteToken(token);
        }
    }
}