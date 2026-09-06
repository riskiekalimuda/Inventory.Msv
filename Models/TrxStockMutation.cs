using System;
using System.Collections.Generic;


namespace Inventory.Msv.Models
{

    public partial class TrxStockMutation
    {
        public Guid Id { get; set; }

        public Guid ProductId { get; set; }

        public string ReferenceType { get; set; } = null!;

        public Guid ReferenceId { get; set; }

        public int QtyIn { get; set; }

        public int QtyOut { get; set; }

        public DateTime? CreatedAt { get; set; }
    }
}