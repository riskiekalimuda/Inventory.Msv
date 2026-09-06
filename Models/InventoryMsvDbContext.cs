using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;


namespace Inventory.Msv.Models
{

    public partial class InventoryMsvDbContext : DbContext
    {
        public InventoryMsvDbContext()
        {
        }

        public InventoryMsvDbContext(DbContextOptions<InventoryMsvDbContext> options)
            : base(options)
        {
        }

        public virtual DbSet<TrxProductStock> TrxProductStocks { get; set; }    

        public virtual DbSet<TrxStockMutation> TrxStockMutations { get; set; }    

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql("Name=ConnectionStrings:InventoryMsvDBConnection");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TrxProductStock>(entity =>
            {
                entity.HasKey(e => e.ProductId).HasName("product_stock_pkey");

                entity.ToTable("trx_product_stock");

                entity.Property(e => e.ProductId)
                .ValueGeneratedNever()
                .HasColumnName("product_id");
                entity.Property(e => e.CurrentStock).HasColumnName("current_stock");
                entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            });

            modelBuilder.Entity<TrxStockMutation>(entity =>
            {
                entity.HasKey(e => e.Id).HasName("trx_stock_mutation_pkey");

                entity.ToTable("trx_stock_mutation");

                entity.HasIndex(e => e.ProductId, "idx_stock_mutation_product");

                entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
                entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
                entity.Property(e => e.ProductId).HasColumnName("product_id");
                entity.Property(e => e.QtyIn).HasColumnName("qty_in");
                entity.Property(e => e.QtyOut).HasColumnName("qty_out");
                entity.Property(e => e.ReferenceId).HasColumnName("reference_id");
                entity.Property(e => e.ReferenceType)
                .HasMaxLength(50)
                .HasColumnName("reference_type");
            });

            this.OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
