using Inventory.Msv.Services;
using MassTransit;
using MessageMQCommon.MQ.Messages.OrderMsv;
using MessageMQCommon.MQ.Messages.PurchaseMsv;
using MessageMQCommon.MQ.Names;
using static MassTransit.ValidationResultExtensions;

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
            var result = await _inventoryService.PuchaseInventoryAsync(addPurchhase);
            if (result.IsSuccess)
            {
                _logger.LogInformation("NewPurchase message processed successfully for PurchaseNumber: {PurchaseNumber}", addPurchhase.PurchaseNumber);
            }
            else
            {
                _logger.LogError("Failed to process NewPurchase message for PurchaseNumber: {PurchaseNumber}. Error: {ErrorMessage}", addPurchhase.PurchaseNumber, result.ErrorMessage);
            }
            if (result.ErrorCode == "DATABASE_ERROR")
            {
                throw new Exception(result.ErrorMessage);
            }
            _logger.LogError($"Business rule violation for PurchaseNumber: {addPurchhase.PurchaseNumber}. Error: {result.ErrorMessage}");
        }
    }
}
