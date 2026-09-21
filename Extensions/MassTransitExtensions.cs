using Inventory.Msv.Consumers;
using Inventory.Msv.Models;
using MassTransit;
using MessageMQCommon.MQ.Names;
using MessageMQCommon.Parameters;

namespace Inventory.Msv.Extensions
{
    public static class MassTransitExtensions
    {
        public static IServiceCollection AddCustomMassTransit(this IServiceCollection services, IConfiguration configuration) 
        {
            var rabbitMQSetting = configuration.GetSection("RabbitMqSettings").Get<RabbitMQParameter>() ?? new RabbitMQParameter();

            services.AddMassTransit(x =>
            {
                x.AddEntityFrameworkOutbox<InventoryMsvDbContext>(o =>
                {
                    o.UsePostgres();
                    o.UseBusOutbox();
                    o.QueryDelay = TimeSpan.FromSeconds(10);
                });

                x.AddConsumersFromNamespaceContaining<NewOrderConsumers>();
                x.AddConsumersFromNamespaceContaining<NewPurchaseConsumers>();
                x.AddConsumersFromNamespaceContaining<UpdateOrderConsumers>();
                x.AddConsumersFromNamespaceContaining<DeleteOrderConsumer>();

                x.UsingRabbitMq((context, cfg) =>
                {

                    cfg.Host(rabbitMQSetting.Host, rabbitMQSetting.VirtualHost, h =>
                    {
                        h.Username(rabbitMQSetting.Username);
                        h.Password(rabbitMQSetting.Password);
                    });

                    cfg.ReceiveEndpoint(QueueNames.OrderQueue.AddOrderQueue, e =>
                    {
                        e.Durable = true;
                        e.UseMessageRetry(r => r.Interval(20, 10));
                        e.ConfigureConsumer<NewOrderConsumers>(context);
                    });
                    cfg.ReceiveEndpoint(QueueNames.PurchaseQueue.PurchaseCreatedQueue, e =>
                    {
                        e.Durable = true;
                        e.UseMessageRetry(r => r.Interval(20, 10));
                        e.ConfigureConsumer<NewPurchaseConsumers>(context);
                    });
                    cfg.ReceiveEndpoint(QueueNames.OrderQueue.UpdateOrderQueue, e =>
                    {
                        e.Durable = true;
                        e.UseMessageRetry(r => r.Interval(20, 10));
                        e.ConfigureConsumer<UpdateOrderConsumers>(context);
                    });
                    cfg.ReceiveEndpoint(QueueNames.OrderQueue.DeleteOrderQueue, e =>
                    {
                        e.Durable = true;
                        e.UseMessageRetry(r => r.Interval(20, 10));
                        e.ConfigureConsumer<DeleteOrderConsumer>(context);
                    });
                });
            });

            return services;   
        }
    }
}
