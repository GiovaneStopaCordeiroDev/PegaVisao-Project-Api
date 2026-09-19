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
                        .OnDelete(DeleteBehavior.Cascade);

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


            base.OnModelCreating(modelBuilder);
        }


        public DbSet<Produto> Produtos { get; set; }
        public DbSet<Pedido> Pedidos  { get; set; }
        public DbSet<Categoria> Categorias { get; set; }
        public DbSet<ItemPedido> ItemPedidos { get; set; }
        public DbSet<VariacaoProduto> VariacaoProdutos { get; set; }
        public DbSet<Usuario> Usuarios { get; set; }
        public DbSet<Endereco> Enderecos { get; set; }

    }
        }


