using Inventory.Msv.Services;
using MassTransit;
using MessageMQCommon.MQ.Messages.OrderMsv;

namespace Inventory.Msv.Consumers
{
    public class NewOrderConsumers : IConsumer<OrderMessage>
    {
        private readonly InventoryService _inventoryService;
        private readonly ILogger<NewOrderConsumers> _logger;
        public NewOrderConsumers(InventoryService inventoryService, ILogger<NewOrderConsumers> logger)
        {
            _inventoryService = inventoryService;
            _logger = logger;
        }

        public async Task Consume(ConsumeContext<OrderMessage> context)
        {
            var orderMessage = context.Message;
            var result = await _inventoryService.InsertInventoryAsync(orderMessage);    
            if (result.IsSuccess)
            {
                _logger.LogInformation($"Inventory updated successfully for OrderNumber: {orderMessage.OrderNumber}");
            }
            else
            {
                if(result.ErrorCode == "DATABASE_ERROR")
                {
                    throw new Exception(result.ErrorMessage); 
                }
                _logger.LogError($"Failed to update inventory for OrderNumber: {orderMessage.OrderNumber}. Error: {result.ErrorMessage}");
            }   
        }   
    }
}
