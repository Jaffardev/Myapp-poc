using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;

// ──────────────────────────────────────────────
//  CONFIG — tweak these before running
// ──────────────────────────────────────────────
const string TARGET_URL      = "https://localhost:7001/api/your-endpoint"; // ← change me
const int    VIRTUAL_USERS   = 100;
const int    REQUESTS_PER_USER = 10;
const int    MAX_CONNECTIONS = 200;   // ServicePointManager / SocketsHttpHandler limit
const int    TIMEOUT_SECONDS = 30;
const int    STATS_INTERVAL_MS = 1000; // how often the live dashboard refreshes

// ──────────────────────────────────────────────
//  GRACEFUL CANCELLATION  (Ctrl+C)
// ──────────────────────────────────────────────
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;   // prevent hard kill; let us flush stats first
    Console.WriteLine("\n[!] Ctrl+C received — stopping after in-flight requests finish…");
    cts.Cancel();
};

// ──────────────────────────────────────────────
//  HTTP CLIENT  (single static instance — best practice)
// ──────────────────────────────────────────────
using var handler = new SocketsHttpHandler
{
    PooledConnectionLifetime    = TimeSpan.FromMinutes(5),
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
    MaxConnectionsPerServer     = MAX_CONNECTIONS,
    EnableMultipleHttp2Connections = true,
};
using var httpClient = new HttpClient(handler)
{
    BaseAddress = new Uri(TARGET_URL),
    Timeout     = TimeSpan.FromSeconds(TIMEOUT_SECONDS),
};
httpClient.DefaultRequestHeaders.Add("User-Agent", "StressTest/1.0 (.NET 10)");

// ──────────────────────────────────────────────
//  SHARED COUNTERS  (lock-free)
// ──────────────────────────────────────────────
long totalCompleted = 0;
long totalSucceeded = 0;
long totalFailed    = 0;
long totalElapsedMs = 0; // sum of all response times → used for avg

var errors = new ConcurrentDictionary<string, int>(); // error message → count

// ──────────────────────────────────────────────
//  LIVE STATS TASK
// ──────────────────────────────────────────────
var statsTask = Task.Run(async () =>
{
    while (!cts.Token.IsCancellationRequested)
    {
        await Task.Delay(STATS_INTERVAL_MS, cts.Token).ContinueWith(_ => { }); // swallow cancel
        PrintStats();
    }
}, cts.Token);

// ──────────────────────────────────────────────
//  MAIN STRESS TEST
// ──────────────────────────────────────────────
Console.WriteLine("╔══════════════════════════════════════════════════╗");
Console.WriteLine("║        .NET 10 API Stress Tester  v1.0          ║");
Console.WriteLine("╚══════════════════════════════════════════════════╝");
Console.WriteLine($"  Target URL    : {TARGET_URL}");
Console.WriteLine($"  Virtual Users : {VIRTUAL_USERS}");
Console.WriteLine($"  Requests/User : {REQUESTS_PER_USER}");
Console.WriteLine($"  Total Requests: {VIRTUAL_USERS * REQUESTS_PER_USER}");
Console.WriteLine($"  Timeout       : {TIMEOUT_SECONDS}s per request");
Console.WriteLine("  Press Ctrl+C to stop gracefully.\n");

var overallSw = Stopwatch.StartNew();

// ParallelOptions controls the outer "100 virtual users" concurrency.
// Each user still executes its 10 calls SEQUENTIALLY inside the body.
var parallelOpts = new ParallelOptions
{
    MaxDegreeOfParallelism = VIRTUAL_USERS,
    CancellationToken      = cts.Token,
};

try
{
    await Parallel.ForEachAsync(
        Enumerable.Range(1, VIRTUAL_USERS),
        parallelOpts,
        async (userId, ct) =>
        {
            // ── INNER SEQUENTIAL LOOP ──────────────────────────
            for (int reqIndex = 1; reqIndex <= REQUESTS_PER_USER; reqIndex++)
            {
                if (ct.IsCancellationRequested) break;

                var sw = Stopwatch.StartNew();
                try
                {
                    // Fire the actual HTTP call.
                    // Replace GetAsync with PostAsJsonAsync / PutAsJsonAsync as needed.
                    using var response = await httpClient.GetAsync(
                        string.Empty,   // BaseAddress already points at the endpoint
                        HttpCompletionOption.ResponseHeadersRead, // don't buffer body
                        ct);

                    sw.Stop();

                    // Treat any non-2xx as a failure
                    if (response.IsSuccessStatusCode)
                        Interlocked.Increment(ref totalSucceeded);
                    else
                    {
                        Interlocked.Increment(ref totalFailed);
                        errors.AddOrUpdate(
                            $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                            1, (_, old) => old + 1);
                    }
                }
                catch (OperationCanceledException)
                {
                    sw.Stop();
                    Interlocked.Increment(ref totalFailed);
                    errors.AddOrUpdate("Cancelled", 1, (_, old) => old + 1);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    Interlocked.Increment(ref totalFailed);
                    // Store only the exception type + first 80 chars to avoid noise
                    var key = $"{ex.GetType().Name}: {ex.Message[..Math.Min(80, ex.Message.Length)]}";
                    errors.AddOrUpdate(key, 1, (_, old) => old + 1);
                }
                finally
                {
                    Interlocked.Increment(ref totalCompleted);
                    Interlocked.Add(ref totalElapsedMs, sw.ElapsedMilliseconds);
                }
            }
        });
}
catch (OperationCanceledException)
{
    // Ctrl+C — normal exit path
}

overallSw.Stop();
await statsTask; // let the stats printer drain

// ──────────────────────────────────────────────
//  FINAL REPORT
// ──────────────────────────────────────────────
Console.WriteLine("\n╔══════════════════════════════════════════════════╗");
Console.WriteLine("║                  FINAL REPORT                   ║");
Console.WriteLine("╚══════════════════════════════════════════════════╝");

long completed = Interlocked.Read(ref totalCompleted);
long succeeded = Interlocked.Read(ref totalSucceeded);
long failed    = Interlocked.Read(ref totalFailed);
long elapsed   = Interlocked.Read(ref totalElapsedMs);
double avgMs   = completed > 0 ? (double)elapsed / completed : 0;
double rps     = overallSw.Elapsed.TotalSeconds > 0
                 ? completed / overallSw.Elapsed.TotalSeconds
                 : 0;

Console.WriteLine($"  Wall-clock time   : {overallSw.Elapsed:mm\\:ss\\.fff}");
Console.WriteLine($"  Total Completed   : {completed}");
Console.WriteLine($"  ✓  Successes      : {succeeded}  ({Pct(succeeded, completed)}%)");
Console.WriteLine($"  ✗  Failures       : {failed}  ({Pct(failed, completed)}%)");
Console.WriteLine($"  Avg Response Time : {avgMs:F1} ms");
Console.WriteLine($"  Throughput        : {rps:F1} req/s");

if (!errors.IsEmpty)
{
    Console.WriteLine("\n  ── Error Breakdown ────────────────────────────");
    foreach (var (msg, count) in errors.OrderByDescending(e => e.Value))
        Console.WriteLine($"  [{count,5}x]  {msg}");
}

Console.WriteLine("\n  Test complete.");

// ──────────────────────────────────────────────
//  HELPERS
// ──────────────────────────────────────────────
void PrintStats()
{
    long c = Interlocked.Read(ref totalCompleted);
    long s = Interlocked.Read(ref totalSucceeded);
    long f = Interlocked.Read(ref totalFailed);
    long e = Interlocked.Read(ref totalElapsedMs);
    double avg = c > 0 ? (double)e / c : 0;

    // Overwrite the current line for a ticker-style display
    Console.Write(
        $"\r  Completed: {c,6} | ✓ {s,6} | ✗ {f,5} | Avg: {avg,7:F1} ms   ");
}

static string Pct(long part, long total) =>
    total == 0 ? "0.0" : (part * 100.0 / total).ToString("F1");
