using Inventory.Msv.Models;
using Inventory.Msv.Services;
using MassTransit;
using MessageMQCommon.MQ.Messages.OrderMsv;
using MessageMQCommon.MQ.Names;

namespace Inventory.Msv.Consumers
{
    public class NewOrderConsumers : IConsumer<OrderMessage>
    {
        private readonly InventoryService _inventoryService;
        private readonly ILogger<NewOrderConsumers> _logger;
        private readonly ISendEndpointProvider _sendEndpointProvider;
        private readonly InventoryMsvDbContext _context;

        public NewOrderConsumers(InventoryService inventoryService, ILogger<NewOrderConsumers> logger, ISendEndpointProvider sendEndpointProvider, InventoryMsvDbContext context)
        {
            _inventoryService = inventoryService;
            _logger = logger;
            _sendEndpointProvider = sendEndpointProvider;
            _context = context;
        }

        public async Task Consume(ConsumeContext<OrderMessage> context)
        {
            var orderMessage = context.Message;

            var result = await _inventoryService.InsertInventoryAsync(orderMessage);

            if (result.IsSuccess)
            {
                _logger.LogInformation($"Inventory processed successfully for OrderNumber: {orderMessage.OrderNumber}");
            }
            else
            {
                if (result.ErrorCode == "DATABASE_ERROR")
                {
                    throw new Exception(result.ErrorMessage);
                }
                _logger.LogError($"Business rule violation for OrderNumber: {orderMessage.OrderNumber}. Error: {result.ErrorMessage}");
            }
        }   
    }
}
