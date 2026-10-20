using System.Diagnostics.Metrics;

namespace LibraryLoans.Api.Infrastructure;

public static class LoanMetrics
{
    private static readonly Meter Meter = new("LibraryLoans.Api");
    private static readonly Counter<long> LoansCreated = Meter.CreateCounter<long>("library.loans.created");
    private static readonly Counter<long> UnavailableRejections = Meter.CreateCounter<long>("library.loans.unavailable_rejections");
    private static readonly Counter<long> IdempotentReplays = Meter.CreateCounter<long>("library.loans.idempotent_replays");
    private static readonly Histogram<double> LoanRequestDuration = Meter.CreateHistogram<double>("library.loans.request.duration", "ms");

    public static void RecordCreated() => LoansCreated.Add(1);
    public static void RecordUnavailable() => UnavailableRejections.Add(1);
    public static void RecordReplay() => IdempotentReplays.Add(1);
    public static void RecordDuration(double milliseconds) => LoanRequestDuration.Record(milliseconds);
}