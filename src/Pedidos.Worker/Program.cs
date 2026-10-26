using Pedidos.Worker;
using Azure.Messaging.ServiceBus;
using Dapper;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(_ =>
    new ServiceBusClient(builder.Configuration["ServiceBus:ConnectionString"]));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
