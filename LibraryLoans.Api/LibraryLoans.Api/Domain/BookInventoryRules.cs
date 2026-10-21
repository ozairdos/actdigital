namespace LibraryLoans.Api.Domain;

public static class BookInventoryRules
{
    public static int CalculateAvailableCopies(int totalCopies, int availableCopies, int newTotalCopies)
    {
        if (totalCopies < 0 || availableCopies < 0 || availableCopies > totalCopies)
        {
            throw new InvalidOperationException("The current book inventory is inconsistent.");
        }

        if (newTotalCopies < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newTotalCopies));
        }

        var borrowedCopies = totalCopies - availableCopies;
        if (newTotalCopies < borrowedCopies)
        {
            throw new BookInventoryConflictException("TotalCopies cannot be lower than the number of borrowed copies.");
        }

        return newTotalCopies - borrowedCopies;
    }
}

public sealed class BookInventoryConflictException(string message) : Exception(message);