using Dapper;
using Microsoft.Data.SqlClient;
using Azure.Messaging.ServiceBus;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped(_ =>
    new SqlConnection(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton(_ =>
    new ServiceBusClient(builder.Configuration["ServiceBus:ConnectionString"]));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<ServiceBusClient>().CreateSender("pedidos-criados"));
    
var app = builder.Build();

app.MapPost("/pedidos", async (NovoPedido novo, SqlConnection db, ServiceBusSender sender) =>
{
    var id = await db.ExecuteScalarAsync<int>(
        "INSERT INTO Pedidos (Cliente, Valor) OUTPUT INSERTED.Id VALUES (@Cliente, @Valor)",
        novo);

    var mensagem = new ServiceBusMessage(
        BinaryData.FromObjectAsJson(new { Id = id, novo.Cliente, novo.Valor }));
    
    await sender.SendMessageAsync(mensagem);

    return Results.Created($"/pedidos/{id}", new { id });
});

app.MapGet("/pedidos/{id:int}", async (int id, SqlConnection db) =>
{
    var pedido = await db.QuerySingleOrDefaultAsync<Pedido>(
        "SELECT Id, Cliente, Valor, Status, CriadoEm FROM Pedidos WHERE Id = @id", 
        new { id });
    
    return pedido is null ? Results.NotFound() : Results.Ok(pedido);
});

app.MapGet("/pedidos", async (SqlConnection db) =>
    Results.Ok(await db.QueryAsync<Pedido>(
        "SELECT Id, Cliente, Valor, Status, CriadoEm FROM Pedidos ORDER BY Id DESC")));

app.Run();

record NovoPedido(string Cliente, decimal Valor);
record Pedido(int Id, string Cliente, decimal Valor, string Status, DateTime CriadoEm);
