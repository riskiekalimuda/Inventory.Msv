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

        public async Task<ServiceResult<DeleteOrderMessage>> DeleteOrderAsync(DeleteOrderMessage deleteOrderMessage)
        {
            if (deleteOrderMessage == null)
            {
                return new ServiceResult<DeleteOrderMessage>(false) { IsSuccess = false, ErrorMessage = "Payload is null" };
            }

            using var transaction = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                foreach (var orderItem in deleteOrderMessage.ListDeleteOrderDetails)
                {
                    var prodStock = await _dbContext.TrxProductStocks.FirstOrDefaultAsync(x => x.ProductId == orderItem.ProductId);
                    if (prodStock == null)
                    {
                        var newStok = _mapper.Map<TrxProductStock>(orderItem);
                        await _dbContext.TrxProductStocks.AddAsync(newStok);
                    }
                    else
                    {
                        prodStock?.CurrentStock += orderItem.Qty;
                        prodStock?.UpdatedAt = DateTime.Now;
                        _dbContext.TrxProductStocks.Update(prodStock);
                    }

                    var mutation = await _dbContext.TrxStockMutations.FirstOrDefaultAsync(x => x.ProductId == orderItem.ProductId
                    && x.ReferenceType == "Order"
                    && x.ReferenceId == orderItem.Id
                    && x.QtyOut == orderItem.Qty);

                    if (mutation != null)
                    {
                        _dbContext.TrxStockMutations.Remove(mutation);
                    }
                }

                await _dbContext.SaveChangesAsync();
                await _dbContext.Database.CommitTransactionAsync();

                return new ServiceResult<DeleteOrderMessage>(true)
                {
                    IsSuccess = true,
                    Data = deleteOrderMessage
                };
            }
            catch (Exception ex)
            {
                await _dbContext.Database.RollbackTransactionAsync();
                return new ServiceResult<DeleteOrderMessage>(false)
                {
                    IsSuccess = false,
                    Data = deleteOrderMessage,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<ServiceResult<TrxStockMutation>> PuchaseInventoryAsync(PurchaseMessage purchaseMessage)
        {
            if (purchaseMessage == null && purchaseMessage.Details == null)
            {
                var payloadMsg = new ServiceResult<TrxStockMutation>(false)
                {
                    IsSuccess = false,
                    Data = new TrxStockMutation(),
                    ErrorMessage = "PurchaseMessage or TrxPurchaseDetails is null",
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

            var sendEndpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri($"queue:{QueueNames.PurchaseQueue.PurchaseCreatedResultQueue}"));

            try
            {
                var incomingIds = purchaseMessage.Details
                                  .Select(x => x.Id)
                                  .ToList();

                var isAlreadyProccess = await _dbContext.TrxStockMutations
                                        .AnyAsync(x => x.ReferenceType == "Purchase" && incomingIds.Contains(x.ReferenceId));

                if (isAlreadyProccess)
                {
                    await sendEndpoint.Send(new PurchaseResultMessage
                    {
                        PurchaseNumber = purchaseMessage.PurchaseNumber,
                        PurchaseResult = "CREATED"
                    });

                    await _dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return IdempotentHelper.GetIdempotentSuccessResult("Purchase");
                }

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

                await sendEndpoint.Send(new PurchaseResultMessage
                {
                    PurchaseNumber = purchaseMessage.PurchaseNumber,
                    PurchaseResult = "CREATED"
                });

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
                var resultMsg = new ServiceResult<TrxStockMutation>(true)
                {
                    IsSuccess = true,
                    Data = new TrxStockMutation(),
                    ErrorMessage = "Inventory inserted successfully",
                    ErrorCode = string.Empty
                };
                if (Activity.Current != null)
                {
                    Activity.Current.SetTag("biz.inventory.status", resultMsg.ErrorMessage);
                }
                return resultMsg;
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync();

                // Deteksi apakah error disebabkan oleh pelanggaran Unique Constraint (Contoh untuk PostgreSQL/MySQL/SQL Server)
                if (IdempotentHelper.IsUniqueConstraintViolation(ex))
                {
                    // Jika balapan lolos AnyAsync tapi tertangkap Unique Constraint, kirim pesan sukses via transaksi baru
                    using var retryTransaction = await _dbContext.Database.BeginTransactionAsync();
                    await sendEndpoint.Send(new PurchaseResultMessage { PurchaseNumber = purchaseMessage.PurchaseNumber, PurchaseResult = "CREATED" });
                    await _dbContext.SaveChangesAsync();
                    await retryTransaction.CommitAsync();

                    return IdempotentHelper.GetIdempotentSuccessResult("Order");
                }

                // Jika DbUpdateException disebabkan oleh hal lain (misal: constraint foreign key, dll)
                return IdempotentHelper.GetDatabaseErrorResult(ex.Message);
            }
            catch (Exception ex)
            {
                var exceptionMsg = new ServiceResult<TrxStockMutation>(false)
                {
                    IsSuccess = false,
                    Data = new TrxStockMutation(),
                    ErrorMessage = $"Error inserting inventory: {ex.Message}",
                    ErrorCode = "DATABASE_ERROR"
                };
                if (Activity.Current != null)
                {
                    Activity.Current.SetTag("biz.inventory.status", exceptionMsg.ErrorMessage);
                }
                return exceptionMsg;
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
                    return IdempotentHelper.GetIdempotentSuccessResult("Order");
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

                        if (Activity.Current != null)
                        {
                            Activity.Current.SetTag("biz.inventory.status", errMsg1.ErrorMessage);
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

                    return IdempotentHelper.GetIdempotentSuccessResult("Order");
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

        public async Task<ServiceResult<TrxStockMutation>> UpdateInventoryAsync(UpdateOrderMessage updateOrderMessage)
        {
            if (updateOrderMessage == null || updateOrderMessage.TrxOrdersDetails == null)
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

            using var transaction = await _dbContext.Database.BeginTransactionAsync();
            var sendEndpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri($"queue:{QueueNames.OrderQueue.UpdateOrderResultQueue}"));

            try
            {
                // 1. Ambil mutasi stok lama berdasarkan Detail Order ID
                var incomingOrdrDtlIds = updateOrderMessage.TrxOrdersDetails.Select(x => x.Id).ToList();

                var oldMutations = await _dbContext.TrxStockMutations
                    .Where(x => x.ReferenceType == "Order" && incomingOrdrDtlIds.Contains(x.ReferenceId))
                    .ToListAsync();

                // 2. Loop setiap detail order baru untuk penyesuaian stok produk
                foreach (var orderDetail in updateOrderMessage.TrxOrdersDetails)
                {
                    // Kunci baris stok produk untuk menghindari race condition
                    var stock = await _dbContext.TrxProductStocks
                                .FromSqlRaw("SELECT * FROM trx_product_stock WHERE product_id = {0} FOR UPDATE", orderDetail.ProductId)
                                .SingleOrDefaultAsync();

                    if (stock == null)
                    {
                        await sendEndpoint.Send(new UpdateOrderResultMessage { OrderNumber = updateOrderMessage.OrderNumber, UpdateOrderResult = "REJECTED" });
                        await transaction.RollbackAsync();

                        return new ServiceResult<TrxStockMutation>(false)
                        {
                            IsSuccess = false,
                            ErrorMessage = $"Product Id = {orderDetail.ProductId} not found.",
                            ErrorCode = "PRODUCT_NOT_FOUND"
                        };
                    }

                    // Cari tahu apakah item ini sudah ada di mutasi lama (berdasarkan ReferenceId)
                    var oldMutation = oldMutations.FirstOrDefault(x => x.ReferenceId == orderDetail.Id);
                    int quantityDelta = 0;

                    if (oldMutation != null)
                    {
                        // JIKA ITEM SUDAH ADA: Delta = Qty Baru - QtyOut Lama
                        quantityDelta = orderDetail.Quantity - oldMutation.QtyOut;

                        // Update kolom QtyOut dengan kuantitas yang baru
                        oldMutation.QtyOut = orderDetail.Quantity;
                        oldMutation.QtyIn = 0; // Tetap 0 karena ini transaksi pengeluaran stok (Order)
                        oldMutation.CreatedAt = DateTime.Now;
                    }
                    else
                    {
                        // JIKA ITEM BARU TAMBAHAN: Delta adalah seluruh kuantitas item baru tersebut
                        quantityDelta = orderDetail.Quantity;

                        // Buat baris mutasi stok baru menggunakan properti QtyIn & QtyOut
                        var newInventory = _mapper.Map<TrxStockMutation>(orderDetail);
                        newInventory.ReferenceType = "Order";
                        newInventory.QtyOut = orderDetail.Quantity;
                        newInventory.QtyIn = 0;
                        newInventory.CreatedAt = DateTime.Now;
                        await _dbContext.TrxStockMutations.AddAsync(newInventory);
                    }

                    // 3. Validasi kecukupan stok berdasarkan nilai Delta
                    if (quantityDelta > 0 && stock.CurrentStock < quantityDelta)
                    {
                        await sendEndpoint.Send(new UpdateOrderResultMessage { OrderNumber = updateOrderMessage.OrderNumber, UpdateOrderResult = "REJECTED" });
                        await transaction.RollbackAsync();

                        return new ServiceResult<TrxStockMutation>(false)
                        {
                            IsSuccess = false,
                            ErrorMessage = $"Insufficient stock for Product Id = {orderDetail.ProductId}. Available: {stock.CurrentStock}, Requested Extra: {quantityDelta}",
                            ErrorCode = "INSUFFICIENT_STOCK"
                        };
                    }

                    // 4. Potong stok master di tabel produk menggunakan nilai Delta
                    stock.CurrentStock -= quantityDelta;
                    stock.UpdatedAt = DateTime.Now;
                }

                // 5. Hapus mutasi jika ada item yang sengaja dihapus dari list order oleh user
                var missingMutations = oldMutations.Where(old => !incomingOrdrDtlIds.Contains(old.ReferenceId)).ToList();
                foreach (var removedMutation in missingMutations)
                {
                    var stockToRestore = await _dbContext.TrxProductStocks
                        .FromSqlRaw("SELECT * FROM trx_product_stock WHERE product_id = {0} FOR UPDATE", removedMutation.ProductId)
                        .SingleOrDefaultAsync();

                    if (stockToRestore != null)
                    {
                        // Kembalikan stok master sebesar QtyOut yang dibatalkan
                        stockToRestore.CurrentStock += removedMutation.QtyOut;
                        stockToRestore.UpdatedAt = DateTime.Now;
                    }
                    _dbContext.TrxStockMutations.Remove(removedMutation);
                }

                // 6. Beritahu sistem bahwa proses update berhasil
                await sendEndpoint.Send(new UpdateOrderResultMessage
                {
                    OrderNumber = updateOrderMessage.OrderNumber,
                    UpdateOrderResult = "UPDATED"
                });

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                var successMsg = new ServiceResult<TrxStockMutation>(true)
                {
                    IsSuccess = true,
                    Data = new TrxStockMutation(),
                    ErrorMessage = "Inventory updated successfully",
                    ErrorCode = string.Empty
                };

                if (Activity.Current != null)
                {
                    Activity.Current.SetTag("biz.inventory.status", successMsg.ErrorMessage);
                }
                return successMsg;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                var errMsg = new ServiceResult<TrxStockMutation>(false)
                {
                    IsSuccess = false,
                    Data = new TrxStockMutation(),
                    ErrorMessage = $"Error updating inventory: {ex.Message}",
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
