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

        public async Task<ServiceResult<TrxStockMutation>> InsertInventoryAsync(OrderMessage orderMessage)
        {
            if(orderMessage == null && orderMessage.TrxOrdersDetails == null    )
            {
                return new ServiceResult<TrxStockMutation>(false)
                {
                    IsSuccess = false,
                    Data = new TrxStockMutation(),
                    ErrorMessage = "OrderMessage or TrxOrdersDetails is null",
                    ErrorCode = "INVALID PAYLOAD"
                };  
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
                return new ServiceResult<TrxStockMutation>(true) {
                    IsSuccess = true,
                    Data = new TrxStockMutation(),
                    ErrorMessage = "Inventory inserted successfully",
                    ErrorCode = string.Empty
                };
            }
            catch (Exception ex)
            {
                return new ServiceResult<TrxStockMutation>(false)
                {
                    IsSuccess = false,
                    Data = new TrxStockMutation(),
                    ErrorMessage = $"Error inserting inventory: {ex.Message}",
                    ErrorCode = "DATABASE_ERROR"
                };  
            }
        }   
    }
}
