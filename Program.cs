using Inventory.Msv.Consumers;
using Inventory.Msv.Extensions;
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

builder.Services.AddInventoryTelemetry(builder.Configuration);

builder.Services.AddControllers(); 

builder.Services.AddAutoMapper(x => { },typeof(MappingProfile).Assembly);  

builder.Services.AddDbContext<InventoryMsvDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("InventoryMsvDbConnection"));
}); 

builder.Services.AddScoped<InventoryService>();

builder.Services.AddCustomMassTransit(builder.Configuration);

var app = builder.Build();
app.MapControllers();


app.Run();

