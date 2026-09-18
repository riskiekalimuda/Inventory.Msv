using AutoMapper;
using Inventory.Msv.Helper;
using Inventory.Msv.Models;
using MassTransit;
using MessageMQCommon.MQ.Messages.OrderMsv;
using MessageMQCommon.MQ.Messages.PurchaseMsv;
using MessageMQCommon.MQ.Names;
using MessageMQCommon.Respones;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace Inventory.Msv.Services
{
    public class InventoryService
    {
        private readonly InventoryMsvDbContext _dbContext;
        private readonly IMapper _mapper;
        private readonly ISendEndpointProvider _sendEndpointProvider;
        public InventoryService(InventoryMsvDbContext dbContext, IMapper mapper, ISendEndpointProvider sendEndpointProvider)
        {
            _dbContext = dbContext;
            _mapper = mapper;
            _sendEndpointProvider = sendEndpointProvider;
        }

        public async Task<ServiceResult<TrxStockMutation>> PuchaseInventoryAsync(PurchaseMessage purchaseMessage)
        {
            if (purchaseMessage == null && purchaseMessage.Details == null)
            {
                return new ServiceResult<TrxStockMutation>(false)
                {
                    IsSuccess = false,
                    Data = new TrxStockMutation(),
                    ErrorMessage = "PurchaseMessage or TrxPurchaseDetails is null",
                    ErrorCode = "INVALID PAYLOAD"
                };
            }
            //run explicit transaction to ensure all inserts are successful or rollback if any fails
            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                foreach (var purchaseDetail in purchaseMessage.Details)
                {
                    var stock = await _dbContext.TrxProductStocks
                                .FromSqlRaw("SELECT * FROM trx_product_stock WHERE product_id = {0} FOR UPDATE", purchaseDetail.ProductId)
                                .SingleOrDefaultAsync();
                    if (stock == null)
                    {
                        stock = new TrxProductStock
                        {
                            ProductId = purchaseDetail.ProductId,
                            CurrentStock = 0,
                            UpdatedAt = DateTime.Now
                        };
                        await _dbContext.TrxProductStocks.AddAsync(stock);
                    }
                    stock.CurrentStock += purchaseDetail.Quantity;
                    stock.UpdatedAt = DateTime.Now;
                    var inventory = _mapper.Map<TrxStockMutation>(purchaseDetail);
                    inventory.ReferenceType = "Purchase";
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

        public async Task<ServiceResult<TrxStockMutation>> InsertInventoryAsync(OrderMessage orderMessage)
        {
            if (orderMessage == null && orderMessage.TrxOrdersDetails == null)
            {
                var payloadMsg = new ServiceResult<TrxStockMutation>(false)
                {
                    IsSuccess = false,
                    Data = new TrxStockMutation(),
                    ErrorMessage = "OrderMessage or TrxOrdersDetails is null",
                    ErrorCode = "INVALID PAYLOAD"
                };
                if (Activity.Current != null)
                {
                    Activity.Current.SetTag("biz.inventory.status", payloadMsg.ErrorMessage);
                }
                return payloadMsg;
            }

            //run explicit transaction to ensure all inserts are successful or rollback if any fails
            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            var sendEndpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri($"queue:{QueueNames.OrderQueue.AddOrderResultQueue}"));

            try
            {
                //Idempotensi process
                var incomingOrdrDtlIds = orderMessage.TrxOrdersDetails
                                        .Select(x => x.Id)
                                        .ToList();

                //Check if transaction is already input
                var isAlreadyProcess = await _dbContext.TrxStockMutations
                                       .AnyAsync(x => x.ReferenceType == "Order" && incomingOrdrDtlIds.Contains(x.ReferenceId));

                if (isAlreadyProcess)
                {
                    await sendEndpoint.Send(new OrderResultMessage
                    {
                        OrderNumber = orderMessage.OrderNumber,
                        OrderResult = "CREATED"
                    });

                    await _dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return IdempotentHelper.GetIdempotentSuccessResult();
                }


                foreach (var orderDetail in orderMessage.TrxOrdersDetails)
                { 
                    //locking the stock row for update to prevent race
                    var stock = await _dbContext.TrxProductStocks
                                .FromSqlRaw("SELECT * FROM trx_product_stock WHERE product_id = {0} FOR UPDATE", orderDetail.ProductId)
                                .SingleOrDefaultAsync();

                    if (stock == null)
                    {
                        await sendEndpoint.Send(new OrderResultMessage { OrderNumber = orderMessage.OrderNumber, OrderResult = "REJECTED" });
                        await _dbContext.SaveChangesAsync();
                        await transaction.CommitAsync();

                        var errMsg1 = new ServiceResult<TrxStockMutation>(false)
                        {
                            IsSuccess = false,
                            ErrorMessage = $"Product Id = {orderDetail.ProductId} not found.",
                            ErrorCode = "PRODUCT_NOT_FOUND"
                        };

                        if (Activity.Current !=null)
                        {
                            Activity.Current.SetTag("biz.inventory.status", errMsg1.ErrorMessage );
                        }
                        return errMsg1;
                    }

                    if (stock.CurrentStock < orderDetail.Quantity)
                    {
                        await sendEndpoint.Send(new OrderResultMessage { OrderNumber = orderMessage.OrderNumber, OrderResult = "REJECTED" });
                        await _dbContext.SaveChangesAsync();
                        await transaction.CommitAsync();

                        var errMsg2 = new ServiceResult<TrxStockMutation>(false)
                        {
                            IsSuccess = false,
                            ErrorMessage = $"Insufficient stock for Product Id = {orderDetail.ProductId}. Available: {stock.CurrentStock}, Requested: {orderDetail.Quantity}",
                            ErrorCode = "INSUFFICIENT_STOCK"
                        };
                        if (Activity.Current != null)
                        {
                            Activity.Current.SetTag("biz.inventory.status", errMsg2.ErrorMessage);
                        }
                        return errMsg2;
                    }

                    stock.CurrentStock -= orderDetail.Quantity;
                    stock.UpdatedAt = DateTime.Now;

                    var inventory = _mapper.Map<TrxStockMutation>(orderDetail);
                    inventory.ReferenceType = "Order";
                    inventory.CreatedAt = DateTime.Now;
                    await _dbContext.TrxStockMutations.AddAsync(inventory);
                }

                await sendEndpoint.Send(new OrderResultMessage
                {
                    OrderNumber = orderMessage.OrderNumber,
                    OrderResult = "CREATED"
                });


                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();


               var errMsg = new ServiceResult<TrxStockMutation>(true)
                {
                    IsSuccess = true,
                    Data = new TrxStockMutation(),
                    ErrorMessage = "Inventory inserted successfully",
                    ErrorCode = string.Empty
                };

                if (Activity.Current != null)
                {
                    Activity.Current.SetTag("biz.inventory.status", errMsg.ErrorMessage);
                }
                return errMsg;
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync();

                // Deteksi apakah error disebabkan oleh pelanggaran Unique Constraint (Contoh untuk PostgreSQL/MySQL/SQL Server)
                if (IdempotentHelper.IsUniqueConstraintViolation(ex))
                {
                    // Jika balapan lolos AnyAsync tapi tertangkap Unique Constraint, kirim pesan sukses via transaksi baru
                    using var retryTransaction = await _dbContext.Database.BeginTransactionAsync();
                    await sendEndpoint.Send(new OrderResultMessage { OrderNumber = orderMessage.OrderNumber, OrderResult = "CREATED" });
                    await _dbContext.SaveChangesAsync();
                    await retryTransaction.CommitAsync();

                    return IdempotentHelper.GetIdempotentSuccessResult();
                }

                // Jika DbUpdateException disebabkan oleh hal lain (misal: constraint foreign key, dll)
                return IdempotentHelper.GetDatabaseErrorResult(ex.Message);
            }
            catch (Exception ex)
            {
                var errMsg = new ServiceResult<TrxStockMutation>(false)
                {
                    IsSuccess = false,
                    Data = new TrxStockMutation(),
                    ErrorMessage = $"Error inserting inventory: {ex.Message}",
                    ErrorCode = "DATABASE_ERROR"
                };

                if (Activity.Current != null)
                {
                    Activity.Current.SetTag("biz.inventory.status", errMsg.ErrorMessage);
                }
                return errMsg;

            }
        }
    }
}
