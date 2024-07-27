using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Pipeline;
using Rebus.Retry.Simple;

namespace TestConsole.HostedServices;

public class ErrorTrackerTestHostedService(IBus bus, ILogger<ErrorTrackerTestHostedService> logging) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logging.LogInformation("ErrorTrackerTestHostedService Started");

        Console.WriteLine("Would you like to publish messages? (y/n)");
        var readLine = Console.ReadLine();

        if (!string.Equals(readLine, "y", StringComparison.InvariantCultureIgnoreCase))
            return;

        for (var i = 0; i < 2; i++)
        {
            await bus.Publish(new MyEvent());
            logging.LogInformation("MyEvent published {Counter}", i);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public class MyEvent;


    public class MyEventHandler(ILogger<MyEventHandler> logger)
        : IHandleMessages<MyEvent>, IHandleMessages<IFailed<MyEvent>>
    {
        public static int Counter;

        public async Task Handle(MyEvent message)
        {
            var messageId = MessageContext.Current.Message.GetMessageId();

            Counter++;

            logger.LogInformation("MyEvent received {MessageId}", messageId);
            await Task.Delay(1000);
            throw new InvalidOperationException($"MyEvent exception {Counter} {messageId}");
        }

        public Task Handle(IFailed<MyEvent> message)
        {
            var messageId = MessageContext.Current.Message.GetMessageId();
            foreach (var messageException in message.Exceptions)
            {
                logger.LogError("MyEvent failed {MessageId} {ErrorMessage}", messageId, messageException.Message);
            }

            return Task.CompletedTask;
        }
    }
}