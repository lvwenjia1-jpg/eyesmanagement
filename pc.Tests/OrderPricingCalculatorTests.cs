using MainApi.Domain;
using MainApi.Services;
using Xunit;

namespace pc.Tests;

public sealed class OrderPricingCalculatorTests
{
    [Fact]
    public void Calculate_ShouldUseBasePriceBySpecification()
    {
        var result = OrderPricingCalculator.Calculate(
            new[]
            {
                new OrderPricingCalculator.OrderPricingInputItem
                {
                    SpecificationToken = "半年抛",
                    ModelToken = "星河蓝",
                    Quantity = 2
                }
            },
            new[]
            {
                new PriceRuleRecord
                {
                    Id = 1,
                    RuleType = PriceRuleTypes.Base,
                    SpecificationToken = "半年抛",
                    PriceName = "单副 / 半年抛",
                    PriceValue = 30,
                    IsActive = true
                }
            });

        var line = Assert.Single(result);
        Assert.Equal(60, line.LineAmount);
        Assert.Equal(30, line.UnitPrice);
        Assert.Equal(1, line.PriceRuleId);
    }

    [Fact]
    public void Calculate_ShouldApplyBulkPriceInFullBlocks_AndFallbackToBase()
    {
        var result = OrderPricingCalculator.Calculate(
            new[]
            {
                new OrderPricingCalculator.OrderPricingInputItem
                {
                    SpecificationToken = "半年抛",
                    ModelToken = "星河蓝",
                    Quantity = 6
                }
            },
            new PriceRuleRecord[]
            {
                new()
                {
                    Id = 1,
                    RuleType = PriceRuleTypes.Base,
                    SpecificationToken = "半年抛",
                    PriceName = "单副 / 半年抛",
                    PriceValue = 30,
                    IsActive = true
                },
                new()
                {
                    Id = 2,
                    RuleType = PriceRuleTypes.Bulk,
                    SpecificationToken = "半年抛",
                    RequiredQuantity = 4,
                    PriceName = "多付 / 半年抛 / 4",
                    PriceValue = 88,
                    IsActive = true
                }
            });

        var line = Assert.Single(result);
        Assert.Equal(148, line.LineAmount);
        Assert.Contains("多付 / 半年抛 / 4", line.PriceName);
        Assert.Contains("单副 / 半年抛", line.PriceName);
        Assert.Null(line.PriceRuleId);
    }

    [Fact]
    public void Calculate_ShouldApplyClearanceThresholdBeforeBulk_WhenThresholdIsMet()
    {
        var result = OrderPricingCalculator.Calculate(
            new[]
            {
                new OrderPricingCalculator.OrderPricingInputItem
                {
                    SpecificationToken = "半年抛",
                    ModelToken = "星河蓝",
                    Quantity = 5
                },
                new OrderPricingCalculator.OrderPricingInputItem
                {
                    SpecificationToken = "半年抛",
                    ModelToken = "月影灰",
                    Quantity = 5
                },
                new OrderPricingCalculator.OrderPricingInputItem
                {
                    SpecificationToken = "半年抛",
                    ModelToken = "常规棕",
                    Quantity = 2
                }
            },
            new PriceRuleRecord[]
            {
                new()
                {
                    Id = 1,
                    RuleType = PriceRuleTypes.Base,
                    SpecificationToken = "半年抛",
                    PriceName = "单副 / 半年抛",
                    PriceValue = 30,
                    IsActive = true
                },
                new()
                {
                    Id = 2,
                    RuleType = PriceRuleTypes.Bulk,
                    SpecificationToken = "半年抛",
                    RequiredQuantity = 4,
                    PriceName = "多付 / 半年抛 / 4",
                    PriceValue = 88,
                    IsActive = true
                },
                new()
                {
                    Id = 3,
                    RuleType = PriceRuleTypes.ClearanceThreshold,
                    SpecificationToken = "半年抛",
                    RequiredQuantity = 10,
                    PriceName = "清仓门槛 / 半年抛 / 10",
                    PriceValue = 50,
                    IsActive = true
                },
                new()
                {
                    Id = 4,
                    RuleType = PriceRuleTypes.Clearance,
                    SpecificationToken = "半年抛",
                    ModelToken = "星河蓝",
                    PriceName = "清仓 / 半年抛 / 星河蓝",
                    PriceValue = 0,
                    IsActive = true
                },
                new()
                {
                    Id = 5,
                    RuleType = PriceRuleTypes.Clearance,
                    SpecificationToken = "半年抛",
                    ModelToken = "月影灰",
                    PriceName = "清仓 / 半年抛 / 月影灰",
                    PriceValue = 0,
                    IsActive = true
                }
            });

        Assert.Equal(25, result[0].LineAmount);
        Assert.Equal(25, result[1].LineAmount);
        Assert.Equal(60, result[2].LineAmount);
        Assert.Equal(110, result.Sum(line => line.LineAmount));
        Assert.Equal(3, result[0].PriceRuleId);
        Assert.Equal(3, result[1].PriceRuleId);
    }
}
