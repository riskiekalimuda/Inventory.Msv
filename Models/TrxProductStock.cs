using System;
using System.Collections.Generic;


namespace Inventory.Msv.Models
{

    public partial class TrxProductStock
    {
        public Guid ProductId { get; set; }

        public int CurrentStock { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}