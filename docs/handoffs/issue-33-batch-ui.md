# Issue #33 批量界面验收记录

来源 #33；依赖 #31/#32 已合并；Owner 本轮授权 Codex 完成 UI 与审核合并。

实现前用例（初始 NOT RUN）：FT33-01 选择/拖入三格式多文件，列表逐文件配置与预览；FT33-02 六项规则默认关闭，一套勾选作用整批，保护项锁定；FT33-03 每项不同 Sheet/表头，CSV 无 Sheet；FT33-04 混合成功/失败逐项展示原因且后续继续；FT33-05 共用记忆目录和每项原格式文件名，已有输出覆盖/取消/另存且保护整批输入；FT33-06 执行时禁用配置/重复开始，完成后可重试并发送单个成功结果到匹配；FT33-07 匹配保持一组，无批量入口。

UI 方案：保留格式统一导航；原页面内使用文件列表、所选文件配置/预览、整批规则与执行区。每项复用既有单文件 ViewModel 的 inspection/preview，批量执行只调用 IBatchFormatStandardizationService。保留逐项输出路径修改与单个结果转匹配能力。用户取消某项覆盖时保留该输出并显示失败/未覆盖，其它项继续。

验证：Release build PASS，0 warnings/errors；250 Core + 20 App tests PASS。新增回归覆盖混合失败、共同规则、独立表头、目录跨实例记忆、覆盖取消/另存、独占锁释放后重试、运行期间禁止重新加载/重复开始、整批源保护。独立 tools/Issue33.FunctionalQA 实际执行通过：三格式批量→重开预览/核对字段与输入哈希→所选 CSV 结果送条件匹配→对照 XLS→CSV 结果，再次执行成功。

FT33-01～06 的 ViewModel/Core 可执行部分 PASS；FT33-07 静态入口检查 PASS。实际文件对话框、拖拽、WPF 可视布局、WPS 操作仍 NOT RUN。未启用桌面 GUI 自动化，不将上述集成工具认作人工 UI Functional QA。实现完成，完整 UI 验收尚未完成。

## 启动缺陷修复（2026-09-08，Owner 请求打开程序时发现）

实际启动初次 FAIL：Windows .NET Runtime 日志显示 XamlParseException，BatchFormatView 缺少 BoolToVisibilityConverter 静态资源。此前 build/ViewModel tests 未捕获此运行时资源问题，不能作为启动成功证据。已补齐局部资源，增加不显示窗口的 STA WPF 组件加载/布局回归。App 21 tests PASS；修复后实际程序启动，有主窗口句柄且 Responding=True。未自动点击控件，不代表完整 WPF/WPS Functional QA 通过。
