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
        Assert.Equal("不限制主表行", matchVm.FilterSummary);
        Assert.False(matchVm.IsFilterExpanded);

        matchVm.IsMasterFilterEnabled = true;
        Assert.True(matchVm.IsFilterExpanded);
        Assert.Equal("仅匹配：请选择主表列", matchVm.FilterSummary);

        matchVm.MasterAvailableColumns.Add(new AvailableColumnItem(new ColumnReference(1, "客户编号")));
        matchVm.SelectedMasterFilterColumn = matchVm.MasterAvailableColumns[0];
        Assert.Equal("仅匹配：客户编号 (A列)（请选择值）", matchVm.FilterSummary);

        var opt = new ColumnValueOption("C001", new ColumnFilterValue(ColumnValueKind.Text, "C001"));
        matchVm.MasterFilterValues.Add(opt);
        matchVm.SelectedMasterFilterValue = opt;
        Assert.Equal("仅匹配：客户编号 (A列) = C001", matchVm.FilterSummary);

        matchVm.IsMasterFilterEnabled = false;
        Assert.Equal("不限制主表行", matchVm.FilterSummary);

        // 2. Options summary
        Assert.Contains("已处理常见差异", matchVm.OptionsSummary);
        Assert.Contains("结果含状态列：匹配状态", matchVm.OptionsSummary);

        matchVm.NormalizeComparisonKeys = false;
        Assert.DoesNotContain("已处理常见差异", matchVm.OptionsSummary);
        Assert.Contains("结果含状态列：匹配状态", matchVm.OptionsSummary);

        matchVm.StatusColumnName = "处理标志";
        Assert.Contains("结果含状态列：处理标志", matchVm.OptionsSummary);

        matchVm.IsStatusColumnEnabled = false;
        Assert.Equal("保持原始匹配设置", matchVm.OptionsSummary);
    }
}
