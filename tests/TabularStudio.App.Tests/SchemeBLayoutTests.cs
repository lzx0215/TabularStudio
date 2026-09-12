using TabularStudio.App.Models;
using TabularStudio.App.ViewModels;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

namespace TabularStudio.App.Tests;

public sealed class SchemeBLayoutTests
{
    [Fact]
    public void DataMatchingViewModelSchemeBPropertiesWork()
    {
        var inspection = new WorkbookInspectionService();
        var matchService = new DataMatchingService();
        var matchVm = new DataMatchingViewModel(inspection, matchService);

        // 1. Collapsible Filter properties
        Assert.Equal("未启用", matchVm.FilterSummary);
        Assert.False(matchVm.IsFilterExpanded);

        matchVm.IsMasterFilterEnabled = true;
        Assert.True(matchVm.IsFilterExpanded);
        Assert.Equal("待选择", matchVm.FilterSummary);

        matchVm.MasterAvailableColumns.Add(new AvailableColumnItem(new ColumnReference(1, "客户编号")));
        matchVm.SelectedMasterFilterColumn = matchVm.MasterAvailableColumns[0];
        Assert.Equal("客户编号 (A列) = 待选择", matchVm.FilterSummary);

        var opt = new ColumnValueOption("C001", new ColumnFilterValue(ColumnValueKind.Text, "C001"));
        matchVm.MasterFilterValues.Add(opt);
        matchVm.SelectedMasterFilterValue = opt;
        Assert.Equal("客户编号 (A列) = C001", matchVm.FilterSummary);

        matchVm.IsMasterFilterEnabled = false;
        Assert.Equal("未启用", matchVm.FilterSummary);

        // 2. Options summary
        Assert.Contains("自动修复差异", matchVm.OptionsSummary);
        Assert.Contains("状态列: 匹配状态", matchVm.OptionsSummary);

        matchVm.NormalizeComparisonKeys = false;
        Assert.DoesNotContain("自动修复差异", matchVm.OptionsSummary);
        Assert.Contains("状态列: 匹配状态", matchVm.OptionsSummary);

        matchVm.StatusColumnName = "处理标志";
        Assert.Contains("状态列: 处理标志", matchVm.OptionsSummary);

        matchVm.IsStatusColumnEnabled = false;
        Assert.Equal("默认设置", matchVm.OptionsSummary);
    }
}
