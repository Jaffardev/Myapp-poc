# GitHub Copilot Instructions: BankingApp.BlazorWasm

## 1. Project Context & Architecture
- **Framework:** .NET 9.0 Blazor WebAssembly (Client) & .NET 9.0 Web API (Server).
- **Domain:** Banking/Fintech (Precision, safety, and performance are critical).
- **Service Pattern:** Custom **AppService** pattern (Interface-driven logic).
- **API Client:** **Refit** for type-safe REST communication.
- **Data Access:** **Entity Framework Core 9.0** with SQL Server.

## 2. Strict Nullability & Object Checks
- **NRT:** `<Nullable>enable</Nullable>` is global. Respect all nullability warnings.
- **Required Data:** Use the `required` keyword for non-nullable DTO/Entity properties.
- **Defensive Guards:** 
    - Always check for null before accessing AppService/API results: `if (response is { IsSuccessStatusCode: true, Content: not null })`.
    - Use the null-conditional operator (`?.`) and null-coalescing operator (`??`) in Razor components.
    - Entities: Navigation properties must be nullable (e.g., `public virtual Transaction? Transaction { get; set; }`) unless eager-loaded.

## 3. EF Core & High-Performance Data Access
- **Read-Only Paths:** Always use `.AsNoTracking()` for queries that do not result in updates to reduce memory overhead.
- **Projection:** Use `.Select()` to project only required fields into DTOs; avoid returning whole entities to the client.
- **Loading Strategies:**
    - Prefer **Eager Loading** (`.Include()`) for small related sets to avoid N+1 query problems.
    - Use **Split Queries** (`.AsSplitQuery()`) for complex joins with multiple collections to avoid Cartesian explosion.
- **Batching:** Use `AddRange()` or specialized bulk libraries for large inserts/updates to minimize database roundtrips.
- **Indexing:** Ensure columns used in `Where`, `OrderBy`, and `Join` (e.g., AccountId, TransactionDate) are indexed in the database.

## 4. High-Performance C# & .NET 9 Guidelines
- **Memory Efficiency:**
    - Use `Span<T>` and `ReadOnlySpan<char>` for high-frequency string parsing or data manipulation.
    - Avoid boxing/unboxing; use generic collections (`List<T>`) over `ArrayList`.
- **Financial Precision:** 
    - **Mandatory:** Use `decimal` for all currency amounts. Never use `float` or `double`.
- **Async Efficiency:** 
    - Pass `CancellationToken` to all Refit and EF Core async methods (e.g., `ToListAsync(ct)`) to allow request cancellation.
    - Prefer `ValueTask` for methods that often return synchronously to reduce heap allocations.
- **String Handling:** Use `StringBuilder` for multiple concatenations and `StringComparison.OrdinalIgnoreCase` for all security-sensitive string comparisons.

## 5. API & UI Integration (Refit + Blazor)
- **Refit Patterns:** 
    - Define interfaces in the `Contracts` project (e.g., `IAccountAppService`).
    - Use attributes: `[Get("/api/accounts/{id}")]`. Returns `Task<ApiResponse<T>>`.
- **UI Safety:** 
    - Implement `bool _isLoading` flags to disable UI elements during async calls.
    - Mask PII (Personally Identifiable Information) such as full account numbers by default.

## 6. Testing
- **Frameworks:** `xUnit` for logic, `bUnit` for components, `Moq` for service mocking.
- **Standard:** Follow **Arrange-Act-Assert (AAA)**. Test for both null and valid data responses.



public abstract class BaseAppService
{
    protected readonly MyBankingDbContext DbContext;
    protected readonly ILogger Logger;

    protected BaseAppService(MyBankingDbContext dbContext, ILogger logger)
    {
        DbContext = dbContext;
        Logger = logger;
    }

    // Helper for high-performance read-only queries
    protected IQueryable<T> ReadOnly<T>() where T : class 
        => DbContext.Set<T>().AsNoTracking();
}


public interface IAccountAppService
{
    [Get("/api/accounts/{id}")]
    Task<ApiResponse<ServiceResult<AccountDto>>> GetAccountAsync(Guid id, CancellationToken ct);
}

public class AccountAppService : BaseAppService, IAccountAppService
{
    public AccountAppService(MyBankingDbContext db, ILogger<AccountAppService> log) : base(db, log) { }

    public async Task<ServiceResult<AccountDto>> GetAccountDetailAsync(Guid id, CancellationToken ct)
    {
        // Performance: Projection avoids fetching the whole entity
        var account = await ReadOnly<Account>()
            .Where(x => x.Id == id)
            .Select(x => new AccountDto 
            { 
                Id = x.Id, 
                Balance = x.Balance, // decimal
                OwnerName = x.OwnerName 
            })
            .FirstOrDefaultAsync(ct);

        // Strict Nullability Check
        if (account is null)
        {
            Logger.LogWarning("Account {Id} not found", id);
            return ServiceResult<AccountDto>.Failure("Account not found.");
        }

        return ServiceResult<AccountDto>.Success(account);
    }
}

