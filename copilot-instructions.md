# GitHub Copilot Instructions: BankingApp.BlazorWasm

## Project Context
- **Framework:** .NET 9.0 Blazor WebAssembly.
- **Domain:** Banking/Fintech (Precision and security are critical).
- **Service Pattern:** Custom **AppService** pattern (Interface-driven business logic).
- **API Client:** **Refit** for type-safe REST communication.

## API & Integration Patterns (Custom AppService + Refit)
- **Interface-First:** Every API must have a corresponding interface (e.g., `IAccountAppService`).
- **Refit Implementation:**
    - Define Refit interfaces in a shared `Contracts` or `Services` namespace.
    - Use Refit attributes: `[Post("/api/accounts/transfer")]`, `[Get("/api/transactions/{id}")]`.
    - Methods should return `Task<T>` or `Task<ApiResponse<T>>` for better error handling.
- **DTOs:** 
    - Use strict DTOs for requests and responses (e.g., `TransferRequestDto`, `AccountResponseDto`).
    - **Validation:** Use `System.ComponentModel.DataAnnotations` for `EditForm` integration.
- **Naming:** Follow the convention `[Entity]AppService` and `[Action][Entity]Dto`.

## Coding Standards (C# & .NET)
- **Financial Precision:** Always use `decimal` for money. Never use `float` or `double`.
- **Async/Await:** Always use `async` suffixes for methods. Use `CancellationToken` for all API calls.
- **Naming:** PascalCase for methods/properties; `_camelCase` for private fields.
- **Dependency Injection:** Inject AppService interfaces into Razor components: `@inject IAccountAppService AccountService`.

## Blazor UI Guidelines
- **State Management:** Use `bool _isProcessing` to disable buttons during Refit calls.
- **Error Handling:** Use `try-catch` blocks around Refit calls to handle network or API failures gracefully.
- **Components:** Logic belongs in `@code {}` blocks or partial classes; UI belongs in Razor markup.
- **Security:** Mask sensitive data (e.g., Full Account Numbers) in the UI unless explicitly toggled.

 
