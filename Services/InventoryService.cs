using AutoMapper;
using Inventory.Msv.Models;
using MessageMQCommon.MQ.Messages.OrderMsv;
using MessageMQCommon.Respones;

namespace Inventory.Msv.Services
{
    public class InventoryService
    {
        private readonly InventoryMsvDbContext _dbContext;
        private readonly IMapper _mapper;
        public InventoryService(InventoryMsvDbContext dbContext, IMapper mapper)
        {
            _dbContext = dbContext;
            _mapper = mapper;
        }

        public async Task<ServiceResult> InsertInventoryAsync(OrderMessage orderMessage)
        {
            if(orderMessage == null && orderMessage.TrxOrdersDetails == null    )
            {
                return ServiceResult.Failure("OrderMessage or TrxOrdersDetails is null","INVALID PAYLOAD");    
            }
            try
            {
                foreach (var orderDetail in orderMessage.TrxOrdersDetails)
                {
                    var inventory = _mapper.Map<TrxStockMutation>(orderDetail);
                    inventory.ReferenceType = "Order";
                    inventory.CreatedAt = DateTime.Now;  
                    await _dbContext.TrxStockMutations.AddAsync(inventory);
                }
                await _dbContext.SaveChangesAsync();
                return ServiceResult.Success();

            }
            catch (Exception ex)
            {
                return ServiceResult.Failure($"Error inserting inventory: {ex.Message}", "DB_ERROR");
            }
        }   
    }
}
