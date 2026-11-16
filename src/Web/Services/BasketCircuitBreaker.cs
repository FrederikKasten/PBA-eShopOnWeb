using MassTransit;
using Polly;
using Polly.CircuitBreaker;

namespace Microsoft.eShopWeb.Web.Services;

/// <summary>
/// Circuit Breaker (krav 7) om alle kald fra Web til Basket.API.
///  - Lukket: kaldene går igennem som normalt.
///  - Åben: har mindst halvdelen af kaldene fejlet (og mindst 2 kald inden for 30 sekunder), afvises nye kald
///    med det samme i 15 sekunder. Så venter hver side ikke 5 sekunder på en service, der er nede.
///  - Halvåben: når pausen er gået, får ét kald lov at prøve. Lykkes det, lukkes kredsløbet igen.
/// Registreres som singleton, så alle requests deler samme tilstand.
/// </summary>
public class BasketCircuitBreaker
{
    private static readonly TimeSpan SamplingDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(15);
    private const double FailureRatio = 0.5;
    private const int MinimumThroughput = 2;

    public BasketCircuitBreaker(ILogger<BasketCircuitBreaker> logger)
    {
        Pipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = FailureRatio,
                MinimumThroughput = MinimumThroughput,
                SamplingDuration = SamplingDuration,
                BreakDuration = BreakDuration,
                ShouldHandle = args => ValueTask.FromResult(IsBasketApiFailure(args.Outcome.Exception, args.Context.CancellationToken)),
                OnOpened = args =>
                {
                    logger.LogWarning(args.Outcome.Exception,
                        "Circuit to Basket.API opened for {BreakSeconds} s; basket calls fail fast until then",
                        args.BreakDuration.TotalSeconds);
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = _ =>
                {
                    logger.LogInformation("Circuit to Basket.API is half-open; the next call tests whether Basket.API answers");
                    return ValueTask.CompletedTask;
                },
                OnClosed = _ =>
                {
                    logger.LogInformation("Circuit to Basket.API closed; Basket.API answers again");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public ResiliencePipeline Pipeline { get; }

    // Tæller som fejl: Basket.API svarede ikke i tide, fejlede, eller RabbitMQ kunne ikke nås (alle er MassTransitException),
    // og en timeout vi ikke selv har bedt om.
    // Tæller ikke: brugeren afbrød selv (fx lukkede fanen). En afvisning fra Basket.API er et svar, ikke en exception.
    private static bool IsBasketApiFailure(Exception? exception, CancellationToken callerToken) => exception switch
    {
        MassTransitException => true,
        OperationCanceledException => !callerToken.IsCancellationRequested,
        _ => false
    };
}
