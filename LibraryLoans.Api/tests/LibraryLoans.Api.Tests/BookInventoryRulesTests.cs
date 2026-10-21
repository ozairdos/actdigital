using LibraryLoans.Api.Domain;
using Xunit;

namespace LibraryLoans.Api.Tests;

public sealed class BookInventoryRulesTests
{
    [Fact]
    public void Increasing_total_copies_preserves_borrowed_copy_count()
    {
        var availableCopies = BookInventoryRules.CalculateAvailableCopies(
            totalCopies: 5,
            availableCopies: 3,
            newTotalCopies: 7);

        Assert.Equal(5, availableCopies);
    }

    [Fact]
    public void Reducing_total_to_borrowed_copy_count_leaves_no_available_copies()
    {
        var availableCopies = BookInventoryRules.CalculateAvailableCopies(
            totalCopies: 5,
            availableCopies: 3,
            newTotalCopies: 2);

        Assert.Equal(0, availableCopies);
    }

    [Fact]
    public void Reducing_total_below_borrowed_copy_count_is_rejected()
    {
        Assert.Throws<BookInventoryConflictException>(() =>
            BookInventoryRules.CalculateAvailableCopies(
                totalCopies: 5,
                availableCopies: 3,
                newTotalCopies: 1));
    }
}