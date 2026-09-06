using BillingControl.Services;
using BillingControl.Models;
namespace BillingControl.Tests;

public class FinanceTests
{
    [Fact] public void StandardAllocation() => Assert.Equal(new[] { 350m, 250m, 400m }, Finance.Split(1000m, 35m, 25m, 40m));
    [Theory]
    [InlineData(35, 25, 39)]
    [InlineData(-5, 25, 80)]
    [InlineData(35, 25, 41)]
    public void InvalidPercentages(decimal a, decimal b, decimal c) => Assert.Throws<BusinessException>(() => Finance.Split(1000m, a, b, c));
    [Theory]
    [InlineData(40, 160)]
    [InlineData(50, 200)]
    [InlineData(70, 280)]
    [InlineData(80, 320)]
    public void WorkerUsesLcmShare(decimal p, decimal expected) => Assert.Equal(expected, Finance.WorkerEntitlement(Finance.Split(1000m, 35, 25, 40)[2], p));
    [Fact] public void CurrencyRounding() { Assert.Equal(1.01m, Finance.Money(1.005m)); Assert.Equal(.01m, Finance.WorkerEntitlement(.01m, 50)); }
    [Theory]
    [InlineData(.01)]
    [InlineData(.02)]
    [InlineData(.03)]
    [InlineData(123.47)]
    public void SplitsReconcileWithoutNegativeResidual(decimal amount) { var split = Finance.Split(amount, 35, 25, 40); Assert.Equal(amount, split.Sum()); Assert.All(split, x => Assert.True(x >= 0)); }
    [Fact] public void PercentagesAreConfigurable() => Assert.Equal(new[] { 100m, 200m, 700m }, Finance.Split(1000m, 10, 20, 70));
    [Fact] public void RejectFractionalCentInput() => Assert.Throws<BusinessException>(() => Finance.Split(1.001m, 35, 25, 40));
    [Fact] public void PartialPayment() { Finance.ValidateAllocation(100, 280, 0); Finance.ValidateAllocation(180, 280, 100); Assert.Throws<BusinessException>(() => Finance.ValidateAllocation(180.01m, 280, 100)); }
    [Fact] public void EndOfMonthAnchorSurvivesFebruary() { var feb = Finance.Next(new(2026, 1, 31), Frequency.Monthly, 31); Assert.Equal(new DateOnly(2026, 2, 28), feb); Assert.Equal(new DateOnly(2026, 3, 31), Finance.Next(feb, Frequency.Monthly, 31)); }
}