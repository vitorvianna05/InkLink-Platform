using InkLink.Finance.Domain;
namespace InkLink.Finance.UnitTests;

public sealed class LedgerTests
{
    [Fact]
    public void Indicators_distinguish_received_cost_and_estimates()
    {
        Assert.Equal(new FinancialIndicators(4000, 940, 3060, 76.5m), FinancialIndicators.Calculate(4000, 940));
        Assert.Equal(0, FinancialIndicators.Calculate(0, 100).EstimatedMargin);
        Assert.Equal(-100, FinancialIndicators.Calculate(0, 100).EstimatedProfit);
        Assert.Equal(-50, FinancialIndicators.Calculate(100, 150).EstimatedMargin);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.001)]
    [InlineData(10000000)]
    public void Invalid_amount_is_rejected(decimal amount) => Assert.Throws<InvalidFinanceData>(() => LedgerEntry.Money(amount));
    [Fact]
    public void Reversal_preserves_original_context_without_mutating_it()
    {
        var date = new DateOnly(2026, 1, 1); var context = Guid.NewGuid();
        var original = LedgerEntry.Record(Guid.NewGuid(), Guid.NewGuid(), "f", EntryKind.Receipt, "Parcela", 450, date, context, "America/Sao_Paulo", date, DateTimeOffset.UtcNow);
        var reversed = original.Reverse(Guid.NewGuid(), "r", "Correção", DateTimeOffset.UtcNow);
        Assert.Equal(original.Id, reversed.OriginalEntryId); Assert.Equal(context, reversed.EngagementId);
        Assert.Equal(date, reversed.EffectiveDate); Assert.Equal(original.Amount, reversed.Amount); Assert.Null(original.OriginalEntryId);
        Assert.Throws<InvalidFinanceData>(() => reversed.Reverse(Guid.NewGuid(), "x", "Não permitido", DateTimeOffset.UtcNow));
    }
}
