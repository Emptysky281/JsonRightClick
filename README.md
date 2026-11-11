# JSON 解析助手 (JsonRightClick)

在任意程序里选中一段文本（日志、接口返回、聊天记录、普通文字……），右键即可在独立窗口打开并尝试解析其中的 JSON。能解析的内容格式化展示，无法转换的内容保留原文，仍可查看、编辑、搜索、复制。窗口内容不会写回原文件。

[English](#english)

![深色主题 + 搜索高亮](docs/dark.png)

| 树形视图（浅色） | 敏感字段打码 |
| --- | --- |
| ![树形视图](docs/light-tree.png) | ![打码](docs/mask.png) |

## 功能

- **选中文本即用**：选中文字后右键时出现悬浮的「解析 JSON」按钮；也可用全局快捷键（默认 `Ctrl+Alt+J`，可自定义）或托盘菜单解析剪贴板。复制时异步等待剪贴板格式稳定并重试暂时占用/尚未就绪的文本，不会拿旧剪贴板充当选区。
- **任意选区均可打开**：悬浮按钮仅确认存在文字选区，不预判选中内容是否为合法 JSON。普通文字、不完整 JSON、无可解析片段的日志等，点击后也打开查看窗口；无法转换时保留全部原文并提示，可继续编辑、搜索和重试。只选择空白也不会被当成“未取到文本”；没有实际文字选区时仍不显示按钮，避免游戏误弹。
- **先取词再关菜单**：点击悬浮按钮后优先直接读取原控件的完整选中文字，再关闭菜单并将查看窗口带到前面；避免 Esc 先取消选区，导致随后 Ctrl+C 复制不到内容而只出现提示。直接读取不可用时才回退复制，慢提供程序稍后返回的完整读数也会用于兜底；普通文字和无效 JSON 一视同仁。不会把旧剪贴板冒充新选区，密码框排除与终端不自动 Ctrl+C 的规则不变。
- **转义选区兼容**：支持选中完整的 JSON 字符串字面量、无外层引号的转义 JSON 及最多 4 层外层编码；仅在解码后整个内容是合法对象/数组时采用，并明确提示。不会全局删除反斜杠，也不会擅自展开普通 JSON 内的字符串字段。
- **资源管理器右键菜单**：右键 .log / .json / .txt 等文件「用 JSON 解析助手打开」。Win11 下可放在第一层菜单（需开发人员模式），也可用经典菜单。
- **混合文本**：从日志里提取多段 JSON，非 JSON 内容原样保留；「只看 JSON」模式下的修改会合并回全文。
- **查看工具**：树形视图、JSON 路径、全部匹配高亮搜索（支持正则 / 大小写）、敏感字段打码、JWT 解码、时间戳提示、展开嵌套 JSON 字符串、宽松解析（单引号、尾逗号、注释、Python 字面量）、压缩 / 格式化。
- **括号配对**：将输入光标放在 `{` / `}` / `[` / `]` 的任意一侧（括号前或后），即可同时高亮对应的一对括号；鼠标点击与方向键移动均可触发，无需选中文字，并在左侧折叠栏及文本缩进处显示该层级的竖向范围线；括号在屏幕外时范围线会裁剪到可见区域。另一端超出可视区域时，编辑区顶部或底部显示对应行预览，点击预览即可跳转。展开嵌套、压缩、撤销、只看 JSON、打码及大文件模式下均可使用；字符串内的括号不会误匹配。
- **代码折叠**：编辑区左侧点击 `− / +` 手动折叠或展开对象 / 数组；收起后点击 `{…}` / `[…]` 可展开。工具栏「折叠」及右键「代码折叠」支持当前层级、全部折叠 / 展开；子层级的独立折叠状态会保留。折叠只改变显示，复制、格式化和撤销使用的仍是完整原文，搜索或树形定位会自动展开目标。
- **悬停提示**：嵌套 JSON 字符串、JWT、时间戳的提示跟随鼠标，在同一段长文本内移动也会更新位置；靠近屏幕边缘自动调整，滚动、编辑或离开编辑区后隐藏。
- **轻量渐变紫色输入光标**：加粗的紫色渐变亮芯与窄柔光，深浅主题分别调整对比度，仍由 Windows 原生 caret 负责实际位置和闪烁。移动时出现短暂的紫色渐变缓动拖尾，真实输入位置不滞后；只绘制最多 8 段、最长 96 像素的局部彩带，不使用粒子、全屏模糊或全文重绘。动画间隔 25 ms，每次移动后的收尾不超过 160 ms，空闲时定时器完全停止；滚动、失焦、选区、输入法组词、字号变化和远距离跳转会取消拖尾。设置中的「霓虹光标（轻量）」和「光标灵动拖尾（短暂）」可分别开关并记忆，也可只保留无动画的渐变紫色光标。遵循 Windows 关闭界面动画的设置，高对比度和输入法组词时保留系统光标；不改变文本、选区、撤销或滚动位置。
- **界面**：深色 / 浅色主题、导入字体（保存在程序目录 `Fonts\`）、字号记忆、中英文切换、开机自启。
- 单个 exe，只依赖 Windows 自带的 .NET Framework 4.x，**不联网**。

### 无引号 Map 展示文本

设置中启用「宽松解析」后，支持 `{nodes_data: {node: {ip: 192.0.2.1, country: CN, rate: 4Mbps, core: 0}}}` 这类 Map 展示文本：无引号的键和字符串值会补成标准 JSON；`[com.example.app]` 这类数组里的字符串也支持（位于该 Map 内）。IP、数字开头的字母数字 ID 按完整 token 判断，不会只截取数字前缀，真正的数字、布尔值、null、对象和数组保留其类型。原有合法内层 JSON 不变。

此转换仅在严格解析与原有宽松规则均失败、且已经检测到无引号属性名时启用；普通 `[INFO]` 日志标签与严格模式不会被改成字符串数组。状态栏会明确提示推断，不能承诺恢复已丢失的原始类型（例如 `true` / `123` 原来是否带引号）。带空格、逗号或结构分隔符等歧义的字符串仍需加引号；不会靠删反斜杠、猜分隔符或执行代码来“修复”。无法整体解析时，若只能提取合法片段，仍会提示并保留原文。

若能控制数据来源，优先输出标准 JSON，而不要用 Map 的展示字符串代替序列化结果。

### 悬浮按钮的多通道选区识别

- 只有原文字区域提供真实非空选区证据，才显示「解析 JSON」按钮：标准 Edit / RichEdit / Scintilla 使用原生接口；其他控件依次使用 MSAA + IAccessible2、UI Automation TextPattern。兼容没有 TextPattern 的 Chromium / Electron / Firefox 文本提供程序，不依赖软件进程名白名单。
- 右键按下时就开始读取选区，短时间保留同一次手势的真实证据，避免菜单弹出后遮住文字或改变焦点。冷启动提供程序可从保存的原控件再确认；无选区叶节点不会提前截断父 Document/编辑器的选区查询。
- UIA 在原命中点所在的可见分支中有界寻找虚拟文档；父文档里其他位置的选区不能让画布/图片点击也触发。密码框、普通光标和仅图片占位符仍不算文本选区。
- 拖动、双击、Shift、Ctrl+A 或剪贴板变化本身都不是证据，不恢复游戏操作误弹的手势推测，也不查询其他窗口的旧选区。键盘、左键、滚轮或切换窗口会作废待显示结果。
- 所有查询在后台进行，同一时间最多一项；只读取选区位置和少量选中文字样本，不模拟 Ctrl+C、不改剪贴板。不提供任何可靠选区接口的软件仍不自动弹出，可手动复制后从托盘解析；终端快捷键只读取已有剪贴板，不发送 Ctrl+C。

### 滚动与大文本性能

- 滚动期间不移动光标或反复着色；停下后按约 6 ms 的小时间片补色，优先处理可见内容。
- 词法结果、字体尺寸与文档长度复用缓存，纯着色不会使搜索、DOM 或括号缓存失效。
- 长搜索匹配只计算屏幕内的显示行；编辑区与侧栏合成呈现以减少闪烁。
- 超长文件也按真实像素平滑滚动，不再退回整行跳动；首个小位移在滚轮输入时立即发生，反向输入会立刻反向。
- 使用 Windows 原生高精度等待计时器，避免 .NET Timer 将 16 ms 请求量化为约 31 ms。根据屏幕刷新率选择约 7–16 ms 的帧间隔，UI 忙时只保留一个待处理帧，空闲时不轮询；不可用时自动回退。滚动条、键盘、失焦或窗口关闭会取消动画。
- 路径/DOM、搜索、树形重建及长字符串悬停解码在滚动动画结束后处理，避免后台辅助功能抢占滚动帧。
- 失焦后悬停滚动，再激活或点击编辑区时保持当前浏览位置；恢复焦点及输入法光标位置查询不再把视口拉回旧光标。真实输入、输入法组合及显式搜索/树形跳转仍可正常定位。
- 折叠、复制、撤销及敏感字段打码的语义不变。

### 折叠快捷键

| 操作 | 快捷键 |
| --- | --- |
| 折叠当前层级 | `Ctrl+Shift+[` |
| 展开当前层级 | `Ctrl+Shift+]` |
| 全部折叠 | `Ctrl+Alt+[` |
| 全部展开 | `Ctrl+Alt+]` |

在折叠块边界按 Backspace / Delete 会先展开，避免误删隐藏内容；再次按键才进行普通删除。

## 系统要求

- Windows 10 / 11（Win11 第一层右键菜单需要打开「开发人员模式」）
- .NET Framework 4.7.2 或更高（Win10 1803+ 自带）

## 使用

1. 从 [Releases](../../releases) 下载，或自己编译（见下）。
2. 把 `JsonRightClick.exe` 和 `JsonRightClickMenu.exe` 放在同一个**固定的、可写的**文件夹里（不要放在 Program Files，设置文件要写在程序旁边）。
3. 运行 `JsonRightClick.exe`，程序驻留托盘并自动注册右键菜单。

> 目标程序以管理员身份运行时，Windows 不允许普通程序读取它的选中内容，可在托盘菜单里选择「以管理员身份重新启动」。

## 编译

不需要 Visual Studio：

```bat
build.bat
```

使用系统自带的 `csc.exe` 生成两个 exe：

| 文件 | 作用 |
| --- | --- |
| `JsonRightClick.exe` | 主程序：托盘、悬浮按钮、查看窗口 |
| `JsonRightClickMenu.exe` | Win11 第一层右键菜单的 COM 服务（带稀疏包身份，manifest 见 `appmenu.manifest`） |

### 不打扰桌面的光标测试

```powershell
./tests/run-caret-headless-tests.ps1
# 可选：在 .test-output/purple-caret-preview 中生成离屏示意帧
./tests/run-caret-headless-tests.ps1 -Preview
```

此入口只测试纯几何模型和 Bitmap 绘制，不创建编辑窗口、不抢焦点、不模拟输入、不使用剪贴板或写入用户设置。检查渐变颜色、XOR 编码、长度与寿命上限、密集移动重定向、绘图裁剪、原生 caret 区域避让和 GDI 资源释放；离屏耗时不等同于真实桌面帧率。`run-tests.ps1` 和滚动基准会打开交互窗口，需在电脑空闲或 CI 环境运行。

## 卸载

1. 托盘菜单 →「资源管理器右键菜单」→ 关闭（会同时移除稀疏包和经典菜单注册表项）。
2. 关闭「开机自动启动」，然后退出。
3. 删除程序文件夹。

## 数据与隐私

- 设置保存在程序目录的 `settings.ini`（删除即恢复默认），目录不可写时回退到 `HKCU\Software\JsonRightClick`。
- 程序不发起任何网络请求，解析的内容只在内存中，不会保存。

## 自检

```bat
JsonRightClick.exe --selftest samples\sample.log out.txt
JsonRightClick.exe --uitest samples\sample.log out.txt
```

`samples/sample.log` 中的账号、令牌均为虚构数据。

编辑器交互回归测试（Windows，使用系统自带的编译器，无需额外测试依赖）：

```powershell
.\tests\run-tests.ps1
```

测试产物位于 `.test-output/`，覆盖括号匹配、代码折叠与父子状态、折叠复制及撤销、搜索 / 树形定位、嵌套展开、视图切换、长文本提示定位、屏幕边界和大文件着色；不改写现有程序的显示设置。

滚动性能基准（可选，用同一份 5000 条嵌套记录及 12.5 万字符搜索匹配对比本地旧提交）：

```powershell
.\tests\run-scroll-benchmark.ps1 -BaselineRef 88b0707
```

基准会确认文档确实滚动，报告帧时钟真实投递间隔 P50 / P95、补色总耗时 / 最长时间片、选区事件数、滚动绘制 P50 / P95 和长匹配重绘耗时。结果写入 `.test-output/`；不同机器、字体和文档的结果会有差异。

## License

[MIT](LICENSE)

---

## English

**JsonRightClick** formats JSON embedded in any selected text (logs, API responses, chat messages) and shows it in a separate editable window. Nothing is written back to the source.

- Select text → right-click → floating **Parse JSON** button, or a global hotkey (default `Ctrl+Alt+J`), or parse the clipboard from the tray. The button requires a confirmed nonempty text selection, using native Edit/RichEdit/Scintilla, MSAA/IAccessible2 and UIA TextPattern providers. It starts probing before context menus obscure the text; unknown, unsupported or timed-out providers stay hidden. Mouse gestures and clipboard changes are never treated as proof. Copy manually and use the tray if an application does not expose its text selection.
- File Explorer context menu for .log / .json / .txt files (Windows 11 top-level menu via a sparse package with Developer Mode, or the classic menu).
- Keeps non-JSON text intact; edits made in *JSON only* mode are merged back.
- Tree view, JSON path, highlight-all search with regex, sensitive-field masking, JWT decoding, timestamp hints, nested-JSON expansion, lenient parsing.
- Matching-bracket highlights, with a clickable line preview when the other bracket is off screen; works after expanding nested JSON strings.
- Scope guides connect matching brackets in the left gutter and indentation area, clipped to the visible viewport.
- Cached native layout and tokenization, budgeted non-selection coloring, and viewport-bounded search highlights reduce scrolling stalls and text flicker.
- IDEA-style code folding with a left gutter, clickable placeholders, current/all-level menu actions and keyboard shortcuts; the complete source text and editing undo history are retained.
- Hover hints follow the pointer, including within very long wrapped strings, and stay within the screen.
- Dark / light theme, custom fonts, remembered font size, Chinese / English UI, start with Windows.
- Lightweight purple-gradient insertion caret: cached lavender-to-violet core and narrow glow, native Windows position/blinking, plus a brief movement-only ribbon (8 segments, at most 96 px, 25 ms timer interval, at most 160 ms to settle). The real insertion point never lags. No particles, full-screen blur or document scan; the timer stops while idle. Scrolling, selection, focus loss, IME composition and layout changes cancel the trail. Settings remember separate caret/trail toggles; disabling just the trail retains a static purple-gradient caret. Windows reduced-animation preferences are respected; high-contrast mode and IME composition keep the system caret.
- Single exe on the built-in .NET Framework 4.x. No network access.

Build with `build.bat` (no Visual Studio needed). Keep both exes in the same writable folder and run `JsonRightClick.exe`. To uninstall, turn off the context menu and autostart from the tray menu, exit, and delete the folder.
