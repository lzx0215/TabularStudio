# Issue #31 多格式 UI 验收记录

来源：#31；依赖 #34 已合并 a74240c；Owner 本轮授权 Codex 独立实现、审核、合并与验证。
范围：两页三格式选择/拖拽、CSV 无 Sheet、表头与预览、同格式输出、已有输出与输入保护。不得在本 Issue 实现批量或 Core 读写。

实现前 Functional Test Cases（全部初始 NOT RUN）：
- FT31-01：两页分别加载 xlsx/xls/csv，预览显示合成值及正确表头；Excel 有 Sheet，CSV 无 Sheet。
- FT31-02：三格式改变表头行重新预览；匹配列绑定真实物理列。
- FT31-03：三格式格式统一写出原格式；九种匹配组合输出跟主表；输入不变。
- FT31-04：csv 主表不允许同文件不同 Sheet 模式；换回 Excel 后可用。
- FT31-05：拒绝 txt/ods、坏文件、非法表头；换文件后旧预览/筛选列不可继续使用。
- FT31-06：打开/保存文件对话框及拖拽接受三格式，CSV 下拉实际不可见；已有输出确认/取消/另存及 WPS 占用恢复。

自动化覆盖 ViewModel/Core；桌面人工部分单列 NOT RUN，不用自动化结果替代。

执行记录：Release build PASS（0 warnings/errors）；245 Core + 16 App tests PASS，新增三格式格式统一及九种组合匹配的真实 ViewModel→Core→输出验证。FT31-01～05 的 ViewModel 可执行部分通过；FT31-06 和整个桌面人工 Functional QA 为 NOT RUN。本次未启用 GUI 自动化。代码实现及静态绑定检查完成，界面验收未冒充完成。
