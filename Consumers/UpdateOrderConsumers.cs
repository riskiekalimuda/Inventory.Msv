using Inventory.Msv.Services;
using MassTransit;
using MessageMQCommon.MQ.Messages.OrderMsv;

namespace Inventory.Msv.Consumers
{
    public class UpdateOrderConsumers:IConsumer<UpdateOrderMessage>
    {
        private readonly ILogger<UpdateOrderConsumers> _logger;
        private readonly InventoryService _inventoryService;

        public UpdateOrderConsumers(ILogger<UpdateOrderConsumers> logger, InventoryService inventoryService)
        {
            _logger = logger;
            _inventoryService = inventoryService;
        }
        public async Task Consume(ConsumeContext<UpdateOrderMessage>context)
        {
            var updateOrder = context.Message;
            if(updateOrder == null)
            {
                _logger.LogInformation("Message is empty");
            }
            else
            {
                var result = await _inventoryService.UpdateInventoryAsync(updateOrder);
                if (result.ErrorCode == "DATABASE_ERROR")
                {
                    throw new Exception(result.ErrorMessage);
                }
                _logger.LogError($"Business rule violation for OrderNumber: {updateOrder.OrderNumber}. Error: {result.ErrorMessage}");
            }
        }
    }
}
