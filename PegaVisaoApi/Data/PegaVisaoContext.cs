        using Microsoft.EntityFrameworkCore;
        using PegaVisaoApi.Models;

        namespace PegaVisaoApi.Data
        {
            public class PegaVisaoContext : DbContext
            {
                public PegaVisaoContext(DbContextOptions<PegaVisaoContext> options) : base(options)
                {

                }

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity<Cupom>().HasIndex(c => c.Codigo).IsUnique();
                    modelBuilder.Entity<Cupom>().ToTable(t => t.HasCheckConstraint("CK_Cupom_Valores",
                        "\"Valor\" > 0 AND \"ValorMinimo\" >= 0 AND (\"Tipo\" = 'Fixo' OR (\"Tipo\" = 'Percentual' AND \"Valor\" <= 100))"));
                    modelBuilder.Entity<Pedido>().HasIndex(p => p.CheckoutSessionId).IsUnique();
                    modelBuilder.Entity<Pedido>().HasIndex(p => p.PaymentIdempotencyKey).IsUnique();
                    modelBuilder.Entity<Pedido>().HasOne(p => p.Cupom).WithMany().HasForeignKey(p => p.CupomId).OnDelete(DeleteBehavior.Restrict);
                    modelBuilder.Entity<Pedido>().Property(p => p.CheckoutHash).HasMaxLength(64);
                    modelBuilder.Entity<Pedido>().Property(p => p.PaymentIdempotencyKey).HasMaxLength(100);
                    modelBuilder.Entity<TentativaPagamento>().HasOne(t => t.Pedido).WithMany().HasForeignKey(t => t.PedidoId);
                    modelBuilder.Entity<AvaliacaoProduto>(entity =>
                    {
                        entity.HasIndex(a => new { a.ProdutoId, a.UsuarioId }).IsUnique();
                        entity.ToTable(t => t.HasCheckConstraint("CK_Avaliacao_Nota", "\"Nota\" BETWEEN 1 AND 5"));
                        entity.HasOne(a => a.Produto).WithMany().HasForeignKey(a => a.ProdutoId).OnDelete(DeleteBehavior.Cascade);
                        entity.HasOne(a => a.Usuario).WithMany().HasForeignKey(a => a.UsuarioId).OnDelete(DeleteBehavior.Cascade);
                    });
                    modelBuilder.Entity<VariacaoProduto>()
                        .ToTable(t => t.HasCheckConstraint("CK_Variacao_Estoque",
                            "\"Estoque\" >= 0 AND \"EstoqueReservado\" >= 0 AND \"EstoqueReservado\" <= \"Estoque\""));
                    modelBuilder.Entity<VariacaoProduto>().Property(v => v.Estoque).IsConcurrencyToken();
                    modelBuilder.Entity<VariacaoProduto>().Property(v => v.EstoqueReservado).IsConcurrencyToken();
                    modelBuilder.Entity<Pedido>().HasIndex(p => new { p.EstadoEstoque, p.ProximaConsultaEstoqueEm });
                    modelBuilder.Entity<Produto>()
                        .HasOne(produto => produto.Categoria)
                        .WithMany(categoria => categoria.Produtos)
                        .HasForeignKey(produto => produto.CategoriaId)
                        .OnDelete(DeleteBehavior.Cascade);

                    modelBuilder.Entity<VariacaoProduto>()
                        .HasOne(variacao => variacao.Produto)
                        .WithMany(produto => produto.Variacoes)
                        .HasForeignKey(variacao => variacao.ProdutoId)
                        .OnDelete(DeleteBehavior.Cascade);

                    modelBuilder.Entity<ItemPedido>()
                        .HasOne(item => item.Pedido)
                        .WithMany(pedido => pedido.Itens)
                        .HasForeignKey(item => item.PedidoId)
                        .OnDelete(DeleteBehavior.Cascade);

                    modelBuilder.Entity<ItemPedido>()
                        .HasOne(item => item.VariacaoProduto)
                        .WithMany()
                        .HasForeignKey(item => item.VariacaoProdutoId)
                        .OnDelete(DeleteBehavior.Restrict);

                    modelBuilder.Entity<Usuario>()
                        .HasOne(usuario => usuario.Endereco)
                        .WithOne(endereco => endereco.Usuario)
                        .HasForeignKey<Endereco>(endereco => endereco.UsuarioId)
                        .OnDelete(DeleteBehavior.Cascade);
                    modelBuilder.Entity<Usuario>()
                        .HasIndex(u => u.Email)
                        .IsUnique();
                    modelBuilder.Entity<Pedido>()
                        .HasOne(p => p.Usuario)
                        .WithMany()
                        .HasForeignKey(p => p.UsuarioId)
                        .OnDelete(DeleteBehavior.Restrict);


            modelBuilder.Entity<MelhorEnvioConexao>(entity =>
            {
                entity.ToTable("MelhorEnvioConexoes");
                entity.HasKey(p => p.Id);
                entity.Property(p => p.Id).HasMaxLength(100);
                entity.Property(p => p.StateHash).HasMaxLength(64);
                entity.Property(p => p.NavegadorHash).HasMaxLength(64);
            });
            modelBuilder.Entity<CotacaoFrete>(entity =>
            {
                entity.HasKey(c => c.Id);
                entity.Property(c => c.CepDestino).HasMaxLength(8);
                entity.Property(c => c.CarrinhoHash).HasMaxLength(64);
                entity.Property(c => c.ConexaoId).HasMaxLength(100);
                entity.HasIndex(c => c.ExpiraEm);
            });
            modelBuilder.Entity<Pedido>().HasIndex(p => p.FreteCotacaoId).IsUnique();
            modelBuilder.Entity<Usuario>().HasIndex(u => u.GoogleSubject).IsUnique();
            modelBuilder.Entity<Usuario>().Property(u => u.GoogleSubject).HasMaxLength(255);
            modelBuilder.Entity<Usuario>().Property(u => u.RecuperacaoHash).HasMaxLength(64);
            base.OnModelCreating(modelBuilder);
        }


        public DbSet<MelhorEnvioConexao> MelhorEnvioConexoes { get; set; }
        public DbSet<CotacaoFrete> CotacoesFrete { get; set; }
        public DbSet<Produto> Produtos { get; set; }
        public DbSet<Cupom> Cupons { get; set; }
        public DbSet<TentativaPagamento> TentativasPagamento { get; set; }
        public DbSet<AvaliacaoProduto> AvaliacoesProdutos { get; set; }
        public DbSet<Pedido> Pedidos  { get; set; }
        public DbSet<Categoria> Categorias { get; set; }
        public DbSet<ItemPedido> ItemPedidos { get; set; }
        public DbSet<VariacaoProduto> VariacaoProdutos { get; set; }
        public DbSet<Usuario> Usuarios { get; set; }
        public DbSet<Endereco> Enderecos { get; set; }

    }
        }


