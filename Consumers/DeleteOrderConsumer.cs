using Inventory.Msv.Services;
using MassTransit;
using MessageMQCommon.MQ.Messages.OrderMsv;

namespace Inventory.Msv.Consumers
{
    public class DeleteOrderConsumer:IConsumer<DeleteOrderMessage>
    {
        private readonly InventoryService _inventoryService;
        private readonly ILogger<DeleteOrderConsumer> _logger;
        public DeleteOrderConsumer(InventoryService inventoryService,
           ILogger<DeleteOrderConsumer> logger)
        {
            _inventoryService = inventoryService;
            _logger = logger;
        }
        public async Task Consume(ConsumeContext<DeleteOrderMessage> context)
        {
            var deleteOrderMessage = context.Message;
            var result = await _inventoryService.DeleteOrderAsync(deleteOrderMessage);
            if(result.IsSuccess)
            {
                _logger.LogInformation($"Delete order id: {result.Data.id} was succesfully.");
            }
            else
            {
                _logger.LogError($"Delete order failled with error: {result.ErrorMessage}.");
            }
        }
    }
}
