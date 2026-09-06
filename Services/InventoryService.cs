using AutoMapper;
using Inventory.Msv.Models;
using MessageMQCommon.MQ.Messages.OrderMsv;
using MessageMQCommon.Respones;
using Microsoft.EntityFrameworkCore;

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
            if (orderMessage == null && orderMessage.TrxOrdersDetails == null)
            {
                return new ServiceResult<TrxStockMutation>(false)
                {
                    IsSuccess = false,
                    Data = new TrxStockMutation(),
                    ErrorMessage = "OrderMessage or TrxOrdersDetails is null",
                    ErrorCode = "INVALID PAYLOAD"
                };
            }

            //run explicit transaction to ensure all inserts are successful or rollback if any fails
            using var transaction = await _dbContext.Database.BeginTransactionAsync();  

            try
            {
                foreach (var orderDetail in orderMessage.TrxOrdersDetails)
                { 
                    //locking the stock row for update to prevent race
                    var stock = await _dbContext.TrxProductStocks
                                .FromSqlRaw("SELECT * FROM trx_product_stock WHERE product_id = {0} FOR UPDATE", orderDetail.ProductId)
                                .SingleOrDefaultAsync();

                    if (stock == null)
                    {
                        await transaction.RollbackAsync();
                        return new ServiceResult<TrxStockMutation>(false)
                        {
                            IsSuccess = false,
                            ErrorMessage = $"Product Id = {orderDetail.ProductId} not found.",
                            ErrorCode = "PRODUCT_NOT_FOUND"
                        };
                    }

                    if (stock.CurrentStock < orderDetail.Quantity)
                    {
                        await transaction.RollbackAsync();
                        return new ServiceResult<TrxStockMutation>(false)
                        {
                            IsSuccess = false,
                            ErrorMessage = $"Insufficient stock for Product Id = {orderDetail.ProductId}. Available: {stock.CurrentStock}, Requested: {orderDetail.Quantity}",
                            ErrorCode = "INSUFFICIENT_STOCK"
                        };
                    }

                    stock.CurrentStock -= orderDetail.Quantity;
                    stock.UpdatedAt = DateTime.Now;

                    var inventory = _mapper.Map<TrxStockMutation>(orderDetail);
                    inventory.ReferenceType = "Order";
                    inventory.CreatedAt = DateTime.Now;
                    await _dbContext.TrxStockMutations.AddAsync(inventory);
                }
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();


                return new ServiceResult<TrxStockMutation>(true)
                {
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
