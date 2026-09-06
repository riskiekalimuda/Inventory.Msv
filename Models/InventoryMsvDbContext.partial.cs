using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Msv.Models
{
    public partial class InventoryMsvDbContext : DbContext
    {
        partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
        {
            modelBuilder.AddTransactionalOutboxEntities();
        }   
    }
}
