namespace PegaVisaoApi.Models
{
    public class Usuario
    {
        public int Id { get; set; }

        public string Nome { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string SenhaHash { get; set; } = string.Empty;

        public bool IsAdmin { get; set; }
        public string? GoogleSubject { get; set; }
        public string? RecuperacaoHash { get; set; }
        public DateTime? RecuperacaoExpiraEm { get; set; }
        public DateTime? RecuperacaoEnviadaEm { get; set; }
        public int VersaoSessao { get; set; }

        public Endereco? Endereco { get; set; }
    }
}