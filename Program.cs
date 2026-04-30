using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// ═══════════════════════════════════════════════════════════════════
//  SOAP SINGLETON VALIDATOR — Console App
//  Proves that ONE SoapClient instance handles ALL parallel requests
// ═══════════════════════════════════════════════════════════════════

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
Console.WriteLine("║       SOAP INSTANCE COUNT VALIDATOR                     ║");
Console.WriteLine("║       ASP.NET Core — Singleton vs Transient Test        ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

// ── Run all three test scenarios ─────────────────────────────────
await RunTest("SINGLETON (Correct Approach)",
    services => services.AddSingleton<ICardSoapClient, CardSoapClient>(),
    ConsoleColor.Green);

Console.WriteLine();

await RunTest("SCOPED (Wrong — creates per-request)",
    services => services.AddScoped<ICardSoapClient, CardSoapClient>(),
    ConsoleColor.Yellow);

Console.WriteLine();

await RunTest("TRANSIENT (Wrong — worst case)",
    services => services.AddTransient<ICardSoapClient, CardSoapClient>(),
    ConsoleColor.Red);

Console.WriteLine();
PrintFinalSummary();

// ═══════════════════════════════════════════════════════════════════
//  TEST RUNNER
// ═══════════════════════════════════════════════════════════════════
static async Task RunTest(
    string label,
    Action<IServiceCollection> register,
    ConsoleColor color)
{
    const int PARALLEL_REQUESTS = 20;

    // Reset static counters before each test
    CardSoapClient.Reset();

    // ── Build DI container ────────────────────────────────────────
    var services = new ServiceCollection();
    services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
    register(services);
    var provider = services.BuildServiceProvider();

    // ── Header ────────────────────────────────────────────────────
    Console.ForegroundColor = color;
    Console.WriteLine($"┌─ TEST: {label}");
    Console.ResetColor();
    Console.WriteLine($"│  Firing {PARALLEL_REQUESTS} parallel requests...");
    Console.WriteLine("│");

    var results  = new ConcurrentBag<RequestResult>();
    var sw       = Stopwatch.StartNew();

    // ── Fire N parallel requests ──────────────────────────────────
    var tasks = Enumerable.Range(1, PARALLEL_REQUESTS).Select(async i =>
    {
        // Simulate per-request scope (like ASP.NET Core does)
        await using var scope = provider.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<ICardSoapClient>();

        var reqSw = Stopwatch.StartNew();
        var balance = await client.GetCardBalanceAsync($"4111-1111-1111-{i:0000}");
        reqSw.Stop();

        results.Add(new RequestResult(
            RequestId:  i,
            InstanceId: balance.FetchedByInstanceId,
            ElapsedMs:  reqSw.ElapsedMilliseconds,
            CardNumber: balance.MaskedCard
        ));
    });

    await Task.WhenAll(tasks);
    sw.Stop();

    // ── Analyse results ───────────────────────────────────────────
    var sorted         = results.OrderBy(r => r.RequestId).ToList();
    var uniqueInstances = sorted.Select(r => r.InstanceId).Distinct().Count();
    var totalCreated   = CardSoapClient.InstanceCount;
    var totalCalls     = CardSoapClient.TotalCalls;
    bool isCorrect     = totalCreated == 1;

    // ── Print per-request table ───────────────────────────────────
    Console.WriteLine("│  Request │ Instance │ Card               │ Time  │");
    Console.WriteLine("│  ────────┼──────────┼────────────────────┼───────│");
    foreach (var r in sorted)
    {
        Console.Write("│  ");
        Console.Write($"  Req #{r.RequestId,-4}");
        Console.Write($"  Inst #{r.InstanceId,-4}  ");
        Console.Write($"  {r.CardNumber,-20}");
        Console.Write($"  {r.ElapsedMs}ms");
        Console.WriteLine();
    }

    Console.WriteLine("│");

    // ── Print summary ─────────────────────────────────────────────
    Console.ForegroundColor = color;
    Console.WriteLine("│  ── RESULTS ──────────────────────────────────────");
    Console.ResetColor();

    PrintResult("│  Total requests fired",   $"{PARALLEL_REQUESTS}");
    PrintResult("│  SoapClient instances CREATED", $"{totalCreated}",
        totalCreated == 1 ? ConsoleColor.Green : ConsoleColor.Red);
    PrintResult("│  Unique instances USED",  $"{uniqueInstances}",
        uniqueInstances == 1 ? ConsoleColor.Green : ConsoleColor.Red);
    PrintResult("│  Total SOAP calls made",  $"{totalCalls}");
    PrintResult("│  Total elapsed time",     $"{sw.ElapsedMilliseconds}ms");

    Console.WriteLine("│");

    if (isCorrect)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("│  ✅ PASS — ONE instance served ALL requests (correct Singleton behaviour)");
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"│  ❌ FAIL — {totalCreated} instances created for {PARALLEL_REQUESTS} requests");
        Console.WriteLine("│       This causes high CPU, memory waste, and no connection reuse!");
    }

    Console.ResetColor();
    Console.ForegroundColor = color;
    Console.WriteLine($"└{'─' + new string('─', label.Length + 8)}");
    Console.ResetColor();
}

// ── Helper: coloured result line ──────────────────────────────────
static void PrintResult(string label, string value,
    ConsoleColor valueColor = ConsoleColor.White)
{
    Console.Write($"{label,-45}: ");
    Console.ForegroundColor = valueColor;
    Console.WriteLine(value);
    Console.ResetColor();
}

// ── Final comparison summary ──────────────────────────────────────
static void PrintFinalSummary()
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
    Console.WriteLine("║                   FINAL SUMMARY                        ║");
    Console.WriteLine("╠══════════════════════════════════════════════════════════╣");
    Console.WriteLine("║  Registration     │ Instances Created │ CPU Impact      ║");
    Console.WriteLine("║  ─────────────────┼───────────────────┼──────────────── ║");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("║  AddSingleton     │        1          │ ✅ Minimal       ║");
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("║  AddScoped        │  1 per request    │ ⚠️  High         ║");
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("║  AddTransient     │  1 per request    │ ❌ Very High     ║");
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("╠══════════════════════════════════════════════════════════╣");
    Console.WriteLine("║  Use AddSingleton<ICardSoapClient, CardSoapClient>()    ║");
    Console.WriteLine("║  in your Program.cs to fix the 70-80% CPU issue.       ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
    Console.ResetColor();
}


// ═══════════════════════════════════════════════════════════════════
//  MOCK SOAP CLIENT  (simulates your real CardSoapClient behaviour)
//  Replace this with your actual client to test against real API
// ═══════════════════════════════════════════════════════════════════

public interface ICardSoapClient
{
    Task<MockCardBalance> GetCardBalanceAsync(string cardNumber);
}

public class CardSoapClient : ICardSoapClient
{
    // ── Static counters — shared across ALL instances ─────────────
    private static int  _instanceCount = 0;
    private static long _totalCalls    = 0;
    private static int  _myInstanceId;

    public static int  InstanceCount => _instanceCount;
    public static long TotalCalls    => Interlocked.Read(ref _totalCalls);

    private readonly int _instanceId;

    public CardSoapClient()
    {
        // Every time this constructor runs = a new instance created
        _instanceId  = Interlocked.Increment(ref _instanceCount);
        _myInstanceId = _instanceId;

        // Simulate the expensive cost of creating a SOAP ChannelFactory
        // In real code: new ChannelFactory<IExternalCardService>(binding, endpoint)
        Thread.Sleep(15); // 15ms startup cost per instance
    }

    public async Task<MockCardBalance> GetCardBalanceAsync(string cardNumber)
    {
        Interlocked.Increment(ref _totalCalls);

        // Simulate real SOAP network latency (50–120ms)
        var latency = Random.Shared.Next(50, 120);
        await Task.Delay(latency);

        return new MockCardBalance
        {
            CardNumber          = cardNumber,
            MaskedCard          = $"**** **** **** {cardNumber[^4..]}",
            AvailableBalance    = Math.Round(Random.Shared.NextDouble() * 10000, 2),
            FetchedByInstanceId = _instanceId,
            FetchedAt           = DateTime.UtcNow
        };
    }

    // Reset for clean test runs
    public static void Reset()
    {
        Interlocked.Exchange(ref _instanceCount, 0);
        Interlocked.Exchange(ref _totalCalls, 0L);
    }
}

// ── Result models ─────────────────────────────────────────────────
public record RequestResult(
    int    RequestId,
    int    InstanceId,
    long   ElapsedMs,
    string CardNumber);

public class MockCardBalance
{
    public string   CardNumber          { get; set; } = string.Empty;
    public string   MaskedCard          { get; set; } = string.Empty;
    public double   AvailableBalance    { get; set; }
    public int      FetchedByInstanceId { get; set; }
    public DateTime FetchedAt           { get; set; }
}
