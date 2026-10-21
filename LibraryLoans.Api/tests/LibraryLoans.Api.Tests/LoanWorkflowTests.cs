using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryLoans.Api.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using LibraryLoans.Api.Infrastructure;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace LibraryLoans.Api.Tests;

public sealed class LoanWorkflowFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("library_loans_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RedisContainer redis = new RedisBuilder("redis:7-alpine")
        .Build();

    private WebApplicationFactory<Program> factory = null!;
    public HttpClient Client { get; private set; } = null!;
    public IServiceProvider Services => factory.Services;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await redis.StartAsync();

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = postgres.GetConnectionString(),
                    ["Redis:ConnectionString"] = $"{redis.Hostname}:{redis.GetMappedPublicPort(6379)}"
                }));
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Warning);
            });
        });

        Client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LibraryLoans.Api.Infrastructure.LibraryDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
        await redis.DisposeAsync();
        await postgres.DisposeAsync();
    }
}

public sealed class LoanWorkflowTests : IClassFixture<LoanWorkflowFixture>
{
    private readonly LoanWorkflowFixture fixture;
    private readonly HttpClient client;

    public LoanWorkflowTests(LoanWorkflowFixture fixture)
    {
        this.fixture = fixture;
        client = fixture.Client;
    }

    [Fact]
    public async Task Development_seed_exposes_demo_book_and_reader()
    {
        var book = await client.GetFromJsonAsync<BookResponse>($"/books/{DevelopmentDataSeeder.DemoBookId}");
        var reader = await client.GetFromJsonAsync<ReaderResponse>($"/users/{DevelopmentDataSeeder.DemoReaderId}");

        Assert.Equal("9780000000009", book!.Isbn);
        Assert.Equal("Leitor de demonstração 1", reader!.Name);
    }

    [Fact]
    public async Task Development_seed_covers_cancelled_and_unavailable_scenarios()
    {
        var unavailable = await client.GetFromJsonAsync<AvailabilityResponse>(
            $"/books/{DevelopmentDataSeeder.DemoUnavailableBookId}/availability");
        var noStock = await client.GetFromJsonAsync<AvailabilityResponse>(
            $"/books/{DevelopmentDataSeeder.DemoNoStockBookId}/availability");
        var canceledHistory = await client.GetFromJsonAsync<LoanResponse[]>(
            $"/books/{DevelopmentDataSeeder.DemoCanceledBookId}/history");
        var readerLoans = await client.GetFromJsonAsync<LoanResponse[]>(
            $"/users/{DevelopmentDataSeeder.DemoReaderFourId}/loans");

        Assert.Equal(0, unavailable!.AvailableCopies);
        Assert.Equal(0, noStock!.AvailableCopies);
        Assert.Contains(canceledHistory!, loan => loan.Status == "Canceled");
        Assert.Contains(readerLoans!, loan => loan.Status == "Canceled");

        var request = CreateLoanRequest(
            DevelopmentDataSeeder.DemoUnavailableBookId,
            DevelopmentDataSeeder.DemoReaderId,
            DateTimeOffset.UtcNow.AddDays(14),
            CreateKey("unavailable-demo"));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Development_seed_is_idempotent()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LibraryLoans.Api.Infrastructure.LibraryDbContext>();
        var booksBefore = await dbContext.Books.CountAsync();
        var readersBefore = await dbContext.Readers.CountAsync();
        var loansBefore = await dbContext.Loans.CountAsync();
        var auditEventsBefore = await dbContext.AuditEvents.CountAsync();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DevelopmentDataSeeder");

        await DevelopmentDataSeeder.SeedAsync(fixture.Services, logger);

        Assert.Equal(booksBefore, await dbContext.Books.CountAsync());
        Assert.Equal(readersBefore, await dbContext.Readers.CountAsync());
        Assert.Equal(loansBefore, await dbContext.Loans.CountAsync());
        Assert.Equal(auditEventsBefore, await dbContext.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task Collection_gets_include_demo_users_and_loan_statuses()
    {
        var users = await client.GetFromJsonAsync<ReaderResponse[]>("/users");
        var loans = await client.GetFromJsonAsync<LoanResponse[]>("/loans");

        Assert.Contains(users!, reader => reader.Id == DevelopmentDataSeeder.DemoReaderId);
        Assert.Contains(users!, reader => reader.Id == DevelopmentDataSeeder.DemoReaderFourId);
        Assert.Contains(loans!, loan => loan.Status == "Active");
        Assert.Contains(loans!, loan => loan.Status == "Returned");
        Assert.Contains(loans!, loan => loan.Status == "Canceled");
    }

    [Fact]
    public async Task Book_and_loan_mutations_invalidate_cache_and_write_audit_events()
    {
        _ = await client.GetFromJsonAsync<BookResponse[]>("/books");
        var book = await CreateBookAsync(2);
        var booksAfterCreate = await client.GetFromJsonAsync<BookResponse[]>("/books");
        Assert.Contains(booksAfterCreate!, item => item.Id == book.Id);

        var reader = await CreateReaderAsync();
        var availability = await client.GetFromJsonAsync<AvailabilityResponse>($"/books/{book.Id}/availability");
        Assert.Equal(2, availability!.AvailableCopies);

        var dueAtUtc = DateTimeOffset.UtcNow.AddDays(14);
        var returnedLoan = await CreateLoanAsync(book.Id, reader.Id, dueAtUtc, CreateKey("audit-return"));
        availability = await client.GetFromJsonAsync<AvailabilityResponse>($"/books/{book.Id}/availability");
        Assert.Equal(1, availability!.AvailableCopies);
        var catalogAfterLoan = await client.GetFromJsonAsync<BookResponse[]>("/books");
        Assert.Equal(1, Assert.Single(catalogAfterLoan!, item => item.Id == book.Id).AvailableCopies);

        var returned = await client.PostAsync($"/loans/{returnedLoan.Id}/return", content: null);
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        availability = await client.GetFromJsonAsync<AvailabilityResponse>($"/books/{book.Id}/availability");
        Assert.Equal(2, availability!.AvailableCopies);
        var catalogAfterReturn = await client.GetFromJsonAsync<BookResponse[]>("/books");
        Assert.Equal(2, Assert.Single(catalogAfterReturn!, item => item.Id == book.Id).AvailableCopies);

        var canceledLoan = await CreateLoanAsync(book.Id, reader.Id, dueAtUtc, CreateKey("audit-cancel"));
        var canceled = await client.PostAsync($"/loans/{canceledLoan.Id}/cancel", content: null);
        Assert.Equal(HttpStatusCode.OK, canceled.StatusCode);

        _ = await client.GetFromJsonAsync<AvailabilityResponse>($"/books/{book.Id}/availability");
        var updated = await client.PatchAsJsonAsync(
            $"/books/{book.Id}",
            new UpdateBookRequest("Título atualizado para auditoria", null, null, 3));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        availability = await client.GetFromJsonAsync<AvailabilityResponse>($"/books/{book.Id}/availability");
        Assert.Equal(3, availability!.AvailableCopies);
        var bookEvents = await client.GetFromJsonAsync<AuditEventResponse[]>(
            $"/audit-events?entityName=Book&entityId={book.Id}");
        Assert.Contains(bookEvents!, item => item.Action == "BookCreated");
        Assert.Contains(bookEvents!, item => item.Action == "BookUpdated");
        Assert.All(bookEvents!, item =>
        {
            Assert.Equal("api", item.Actor);
            Assert.False(string.IsNullOrWhiteSpace(item.CorrelationId));
            Assert.Equal(DateTimeKind.Utc, item.TimestampUtc.Kind);
        });

        var returnedEvents = await client.GetFromJsonAsync<AuditEventResponse[]>(
            $"/audit-events?entityName=Loan&entityId={returnedLoan.Id}");
        var canceledEvents = await client.GetFromJsonAsync<AuditEventResponse[]>(
            $"/audit-events?entityName=Loan&entityId={canceledLoan.Id}");
        Assert.Contains(returnedEvents!, item => item.Action == "LoanReturned");
        Assert.Contains(canceledEvents!, item => item.Action == "LoanCanceled");

        var deactivated = await client.DeleteAsync($"/books/{book.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);
        var availabilityAfterDeactivate = await client.GetAsync($"/books/{book.Id}/availability");
        Assert.Equal(HttpStatusCode.NotFound, availabilityAfterDeactivate.StatusCode);
        var finalBookEvents = await client.GetFromJsonAsync<AuditEventResponse[]>(
            $"/audit-events?entityName=Book&entityId={book.Id}");
        Assert.Contains(finalBookEvents!, item => item.Action == "BookDeactivated");

        var deactivatedBook = await client.GetFromJsonAsync<BookResponse>($"/books/{book.Id}");
        var historyAfterDeactivate = await client.GetFromJsonAsync<LoanResponse[]>($"/books/{book.Id}/history");
        var catalogAfterDeactivate = await client.GetFromJsonAsync<BookResponse[]>("/books");
        Assert.False(deactivatedBook!.IsActive);
        Assert.Equal(2, historyAfterDeactivate!.Length);
        Assert.DoesNotContain(catalogAfterDeactivate!, item => item.Id == book.Id);
    }

    [Fact]
    public async Task Duplicate_isbn_is_rejected()
    {
        var first = await CreateBookAsync(1);
        var duplicate = await client.PostAsJsonAsync("/books", new CreateBookRequest(
            "Duplicate ISBN test",
            first.Isbn,
            "Test author",
            1));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Blank_book_title_and_reader_name_are_rejected()
    {
        var blankBook = await client.PostAsJsonAsync("/books", new CreateBookRequest(
            "   ",
            CreateIsbn(),
            "Test author",
            1));
        var blankReader = await client.PostAsJsonAsync("/users", new CreateReaderRequest("   "));

        Assert.Equal(HttpStatusCode.BadRequest, blankBook.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blankReader.StatusCode);
    }

    [Fact]
    public async Task Health_checks_and_problem_responses_include_expected_status_and_correlation()
    {
        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        const string correlationId = "manual-correlation-check";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/users?limit=0");
        request.Headers.Add("X-Correlation-ID", correlationId);
        var invalidLimit = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, invalidLimit.StatusCode);
        Assert.Equal(correlationId, Assert.Single(invalidLimit.Headers.GetValues("X-Correlation-ID")));
        Assert.StartsWith("application/problem+json", invalidLimit.Content.Headers.ContentType!.MediaType);
        using var problem = JsonDocument.Parse(await invalidLimit.Content.ReadAsStringAsync());
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Concurrent_requests_for_last_copy_create_exactly_one_loan()
    {
        var book = await CreateBookAsync(1);
        var reader = await CreateReaderAsync();
        var dueAtUtc = DateTimeOffset.UtcNow.AddDays(14);

        var firstRequest = CreateLoanRequest(book.Id, reader.Id, dueAtUtc, CreateKey("concurrent-a"));
        var secondRequest = CreateLoanRequest(book.Id, reader.Id, dueAtUtc, CreateKey("concurrent-b"));
        var responses = await Task.WhenAll(client.SendAsync(firstRequest), client.SendAsync(secondRequest));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);

        var availability = await client.GetFromJsonAsync<AvailabilityResponse>($"/books/{book.Id}/availability");
        Assert.Equal(0, availability!.AvailableCopies);

        var history = await client.GetFromJsonAsync<LoanResponse[]>($"/books/{book.Id}/history");
        Assert.Single(history!);
    }

    [Fact]
    public async Task Repeating_idempotency_key_returns_same_loan_without_decrementing_twice()
    {
        var book = await CreateBookAsync(1);
        var reader = await CreateReaderAsync();
        var dueAtUtc = DateTimeOffset.UtcNow.AddDays(14);
        var key = CreateKey("same-key");
        var first = await client.SendAsync(CreateLoanRequest(book.Id, reader.Id, dueAtUtc, key));
        var repeated = await client.SendAsync(CreateLoanRequest(book.Id, reader.Id, dueAtUtc, key));

        var firstLoan = await first.Content.ReadFromJsonAsync<LoanResponse>();
        var replayedLoan = await repeated.Content.ReadFromJsonAsync<LoanResponse>();

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(firstLoan!.Id, replayedLoan!.Id);

        var history = await client.GetFromJsonAsync<LoanResponse[]>($"/books/{book.Id}/history");
        Assert.Single(history!);
        var availability = await client.GetFromJsonAsync<AvailabilityResponse>($"/books/{book.Id}/availability");
        Assert.Equal(0, availability!.AvailableCopies);
    }

    [Fact]
    public async Task Concurrent_requests_with_same_idempotency_key_return_same_loan()
    {
        var book = await CreateBookAsync(1);
        var reader = await CreateReaderAsync();
        var dueAtUtc = DateTimeOffset.UtcNow.AddDays(14);
        var key = CreateKey("concurrent-same-key");
        var firstRequest = CreateLoanRequest(book.Id, reader.Id, dueAtUtc, key);
        var secondRequest = CreateLoanRequest(book.Id, reader.Id, dueAtUtc, key);

        var responses = await Task.WhenAll(client.SendAsync(firstRequest), client.SendAsync(secondRequest));
        var loans = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<LoanResponse>()));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.All(loans, loan => Assert.Equal(loans[0]!.Id, loan!.Id));

        var history = await client.GetFromJsonAsync<LoanResponse[]>($"/books/{book.Id}/history");
        Assert.Single(history!);
    }

    [Fact]
    public async Task Return_and_cancel_keep_loans_in_history()
    {
        var book = await CreateBookAsync(2);
        var reader = await CreateReaderAsync();
        var dueAtUtc = DateTimeOffset.UtcNow.AddDays(14);
        var first = await CreateLoanAsync(book.Id, reader.Id, dueAtUtc, CreateKey("return-key"));
        var second = await CreateLoanAsync(book.Id, reader.Id, dueAtUtc, CreateKey("cancel-key"));

        var returned = await client.PostAsync($"/loans/{first.Id}/return", content: null);
        var canceled = await client.PostAsync($"/loans/{second.Id}/cancel", content: null);
        var history = await client.GetFromJsonAsync<LoanResponse[]>($"/books/{book.Id}/history");

        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        Assert.Equal(HttpStatusCode.OK, canceled.StatusCode);
        Assert.Equal(2, history!.Length);
        Assert.Contains(history, loan => loan.Status == "Returned");
        Assert.Contains(history, loan => loan.Status == "Canceled");
    }

    [Fact]
    public async Task Reusing_idempotency_key_for_different_request_is_rejected()
    {
        var firstBook = await CreateBookAsync(1);
        var secondBook = await CreateBookAsync(1);
        var reader = await CreateReaderAsync();
        var dueAtUtc = DateTimeOffset.UtcNow.AddDays(14);

        var key = CreateKey("conflict-key");
        var created = await client.SendAsync(CreateLoanRequest(firstBook.Id, reader.Id, dueAtUtc, key));
        var conflict = await client.SendAsync(CreateLoanRequest(secondBook.Id, reader.Id, dueAtUtc, key));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    private async Task<BookResponse> CreateBookAsync(int copies)
    {
        var request = new CreateBookRequest(
            $"Test book {Guid.NewGuid():N}",
            CreateIsbn(),
            "Test author",
            copies);
        var response = await client.PostAsJsonAsync("/books", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BookResponse>())!;
    }

    private async Task<ReaderResponse> CreateReaderAsync()
    {
        var response = await client.PostAsJsonAsync("/users", new CreateReaderRequest("Test reader"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReaderResponse>())!;
    }

    private async Task<LoanResponse> CreateLoanAsync(Guid bookId, Guid readerId, DateTimeOffset dueAtUtc, string key)
    {
        var response = await client.SendAsync(CreateLoanRequest(bookId, readerId, dueAtUtc, key));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoanResponse>())!;
    }

    private static HttpRequestMessage CreateLoanRequest(Guid bookId, Guid readerId, DateTimeOffset dueAtUtc, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/loans")
        {
            Content = JsonContent.Create(new CreateLoanRequest(bookId, readerId, dueAtUtc))
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static string CreateIsbn() => $"978{Random.Shared.Next(1000000000, int.MaxValue):D10}"[..13];

    private static string CreateKey(string purpose) => $"{purpose}-{Guid.NewGuid():N}";
    private sealed record AvailabilityResponse(Guid BookId, int AvailableCopies);
}