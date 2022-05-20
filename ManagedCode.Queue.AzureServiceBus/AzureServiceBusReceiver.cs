using System.Runtime.CompilerServices;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ManagedCode.Queue.AzureServiceBus.Options;
using ManagedCode.Queue.Core.Abstractions;
using ManagedCode.Queue.Core.Models;

namespace ManagedCode.Queue.AzureServiceBus;

public class AzureServiceBusReceiver : IQueueReceiver
{
    private readonly AzureServiceBusOptions _options;
    private readonly ServiceBusClient _client;
    private readonly ServiceBusAdministrationClient _adminClient;

    public AzureServiceBusReceiver(AzureServiceBusOptions options)
    {
        _options = options;
        _client = new ServiceBusClient(options.ConnectionString);
        _adminClient = new ServiceBusAdministrationClient(options.ConnectionString);
    }

    public async IAsyncEnumerable<Message> ReceiveMessages(string topic, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var subscriptionName = topic + "Subscription";

        await CreateSubscriptionIfNotExist(topic, subscriptionName, cancellationToken);

        await using var processor = _client.CreateProcessor(
            topic,
            subscriptionName,
            new ServiceBusProcessorOptions {ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete});

        await foreach (var message in ProcessMessagesAsync(cancellationToken, processor))
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            yield return message;
        }
    }

    public async IAsyncEnumerable<Message> ReceiveMessages([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var processor = _client.CreateProcessor(_options.Queue,
            new ServiceBusProcessorOptions {ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete});

        await foreach (var message in ProcessMessagesAsync(cancellationToken, processor))
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            yield return message;
        }
    }

    private static async IAsyncEnumerable<Message> ProcessMessagesAsync([EnumeratorCancellation] CancellationToken cancellationToken,
        ServiceBusProcessor processor)
    {
        var completionSource = new TaskCompletionSource<Message>();

        cancellationToken.Register(() =>
        {
            // this callback will be executed when token is cancelled
            completionSource.TrySetCanceled();
        });

        processor.ProcessMessageAsync += OnProcessMessageAsync;
        processor.ProcessErrorAsync += OnProcessErrorAsync;

        await processor.StartProcessingAsync(cancellationToken);

        while (processor.IsProcessing)
        {
            var message = await completionSource.Task;
            yield return message;
        }

        Task OnProcessMessageAsync(ProcessMessageEventArgs args)
        {
            completionSource.SetResult(new Message(
                Id: new MessageId(
                    Id: args.Message.MessageId,
                    ReceiptHandle: args.Message.To),
                Body: args.Message.Body.ToString()));

            return Task.CompletedTask;
        }

        Task OnProcessErrorAsync(ProcessErrorEventArgs args)
        {
            completionSource.SetResult(new Message(
                Id: new MessageId(string.Empty),
                Body: string.Empty,
                Error: new Error(args.Exception)));

            return Task.CompletedTask;
        }
    }

    private async Task CreateSubscriptionIfNotExist(string topic, string subscriptionName, CancellationToken cancellationToken = default)
    {
        if (!await _adminClient.SubscriptionExistsAsync(topic, subscriptionName, cancellationToken))
        {
            await _adminClient.CreateSubscriptionAsync(topic, subscriptionName, cancellationToken);
        }
    }

    public Task<int> GetMessageCountAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task CleanQueue(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}