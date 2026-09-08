using Inventory.Msv.Consumers;
using Inventory.Msv.Models;
using Inventory.Msv.Profiles;
using Inventory.Msv.Services;
using MassTransit;
using MessageMQCommon.MQ.Names;
using MessageMQCommon.Parameters;
using Microsoft.EntityFrameworkCore;

// Pengaturan ini memaksa .NET dan Npgsql menyelaraskan format DateTime lama/lokal menjadi kompatibel dengan pemformatan database
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers(); 

builder.Services.AddAutoMapper(x => { },typeof(MappingProfile).Assembly);  

builder.Services.AddDbContext<InventoryMsvDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("InventoryMsvDbConnection"));
}); 

builder.Services.AddScoped<InventoryService>();

var rabbitMQSetting = builder.Configuration.GetSection("RabbitMqSettings").Get<RabbitMQParameter>()??new RabbitMQParameter();

builder.Services.AddMassTransit(x =>
{
    x.AddEntityFrameworkOutbox<InventoryMsvDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
        o.QueryDelay = TimeSpan.FromSeconds(10);
    });
    
    x.AddConsumersFromNamespaceContaining<NewOrderConsumers>();
    x.AddConsumersFromNamespaceContaining<NewPurchaseConsumers>();

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
    });
}); 


var app = builder.Build();
app.MapControllers();


app.Run();

