using Inventory.Msv.Services;
using MassTransit;
using MessageMQCommon.MQ.Messages.PurchaseMsv;
using MessageMQCommon.MQ.Names;

namespace Inventory.Msv.Consumers
{
    public class NewPurchaseConsumers : IConsumer<PurchaseMessage>
    {
        private readonly ILogger<NewPurchaseConsumers> _logger;
        private readonly ISendEndpointProvider _sendEndpointProvider;
        private readonly InventoryService _inventoryService;
        public NewPurchaseConsumers(ILogger<NewPurchaseConsumers> logger, ISendEndpointProvider sendEndpointProvider, InventoryService inventoryService)
        {
            _logger = logger;
            _sendEndpointProvider = sendEndpointProvider;
            _inventoryService = inventoryService;
        }
        public async Task Consume(ConsumeContext<PurchaseMessage> context)
        {
            var addPurchhase = context.Message;
            try
            {
                var result = await _inventoryService.PuchaseInventoryAsync(addPurchhase);
                if (result.IsSuccess)
                {
                    var sendEndpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri($"queue:{QueueNames.PurchaseQueue.PurchaseCreatedResultQueue}"));
                    await sendEndpoint.Send(
                        new PurchaseResultMessage
                        {
                            PurchaseNumber = addPurchhase.PurchaseNumber,
                            PurchaseResult = "CREATED"
                        });
                    _logger.LogInformation("NewPurchase message processed successfully for PurchaseNumber: {PurchaseNumber}", addPurchhase.PurchaseNumber);
                }
                else
                {
                    _logger.LogError("Failed to process NewPurchase message for PurchaseNumber: {PurchaseNumber}. Error: {ErrorMessage}", addPurchhase.PurchaseNumber, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling NewPurchase message");
                throw;
            }
        }
    }
}
