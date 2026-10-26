namespace Pedidos.Worker;

using Azure.Messaging.ServiceBus;
using Dapper;
using Microsoft.Data.SqlClient;

public class Worker : BackgroundService
{
    private readonly ServiceBusClient _client;
    private readonly string _connectionString;
    private readonly ILogger<Worker> _logger;

    public Worker(ServiceBusClient client, IConfiguration config, ILogger<Worker> logger)
    {
        _client = client;
        _connectionString = config.GetConnectionString("Default")!;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var processor = _client.CreateProcessor("pedidos-criados",
            new ServiceBusProcessorOptions { AutoCompleteMessages = false, MaxConcurrentCalls = 1 });

        processor.ProcessMessageAsync += ProcessarMensagemAsync;
        processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "Erro ao processar mensagem");
            return Task.CompletedTask;
        };

        await processor.StartProcessingAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }

        await processor.StopProcessingAsync();
    }

    private async Task ProcessarMensagemAsync(ProcessMessageEventArgs args)
    {
        var pedido = args.Message.Body.ToObjectFromJson<PedidoCriado>()
            ?? throw new InvalidOperationException("Mensagem sem corpo válido");

        await using var db = new SqlConnection(_connectionString);
        await db.ExecuteAsync(
            "UPDATE Pedidos SET Status = 'Processado' WHERE Id = @Id",
            new { pedido.Id });

        await args.CompleteMessageAsync(args.Message);
        _logger.LogInformation("Pedido {Id} processado", pedido.Id);
    }
}

public record PedidoCriado(int Id, string Cliente, decimal Valor);
