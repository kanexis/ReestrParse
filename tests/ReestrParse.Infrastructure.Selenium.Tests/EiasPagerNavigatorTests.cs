using ReestrParse.Infrastructure.Selenium.Eias;
using Xunit;

namespace ReestrParse.Infrastructure.Selenium.Tests;

public sealed class EiasPagerNavigatorTests
{
    [Fact]
    public void SelectVisibleHop_TargetHiddenForward_ChoosesNearestVisibleBeforeTarget()
    {
        var page = EiasPagerNavigator.SelectVisibleHop(
            currentPage: 1,
            targetPage: 8,
            visiblePages: new[] { 1, 2, 3, 4, 5, 6, 7 });

        Assert.Equal(7, page);
    }

    [Fact]
    public void SelectVisibleHop_TargetBecomesVisible_ChoosesTarget()
    {
        var page = EiasPagerNavigator.SelectVisibleHop(
            currentPage: 7,
            targetPage: 8,
            visiblePages: new[] { 1, 2, 3, 6, 7, 8, 9, 10, 11, 12 });

        Assert.Equal(8, page);
    }

    [Fact]
    public void SelectVisibleHop_TargetHiddenBackward_ChoosesNearestVisibleAfterTarget()
    {
        var page = EiasPagerNavigator.SelectVisibleHop(
            currentPage: 12,
            targetPage: 3,
            visiblePages: new[] { 1, 6, 7, 8, 9, 10, 11, 12 });

        Assert.Equal(6, page);
    }

    [Fact]
    public void SelectVisibleHop_NoProgress_ReturnsNull()
    {
        var page = EiasPagerNavigator.SelectVisibleHop(
            currentPage: 1,
            targetPage: 8,
            visiblePages: new[] { 1 });

        Assert.Null(page);
    }

    [Fact]
    public void BuildSequentialPages_Forward_VisitsEveryIntermediatePage()
    {
        var pages = EiasPagerNavigator.BuildSequentialPages(1, 8);

        Assert.Equal(new[] { 2, 3, 4, 5, 6, 7, 8 }, pages);
    }

    [Fact]
    public void BuildSequentialPages_Backward_VisitsEveryIntermediatePage()
    {
        var pages = EiasPagerNavigator.BuildSequentialPages(8, 3);

        Assert.Equal(new[] { 7, 6, 5, 4, 3 }, pages);
    }

    [Fact]
    public void BuildSequentialPages_SamePage_IsEmpty()
    {
        var pages = EiasPagerNavigator.BuildSequentialPages(4, 4);

        Assert.Empty(pages);
    }
}
