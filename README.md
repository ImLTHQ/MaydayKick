# RoomKit

> **本模组由 大肥鱼（DeepSeek） 与 DSH 辅助开发完成。**

**[English README → README.en.md](README.en.md)**

给《Mayday Airways》Demo 的联机房主加一点管理手段：**踢人、黑名单、按 Steam 等级下限筛选玩家、拦截纯数字昵称、机组列表滚动、自定义房间名、机组列表显示玩家名**。

不修改游戏本体的任何文件，插件就是一个 dll，删掉即可完全还原。

> **参考模组**：本插件的界面布局与打包方式参考了 CHmadoka 的 [MaydayUnlock](https://www.nexusmods.com/maydayairways/mods/1)（[源码](https://github.com/CHmadoka/more_players)），它把联机人数上限从 4 提到 16。两者改的是游戏的不同部分，可以一起安装。

---

## 功能

| 功能 | 说明 | 谁能用 |
| --- | --- | --- |
| 踢出玩家 | 机长菜单「机组」列表每行右侧多一个红色按钮，点了把人踢出本局 | 仅房主 |
| 踢出并拉黑 | 每行右侧的锁形按钮，踢人同时把对方 SteamID 写进黑名单，之后他再来会被挡在认证阶段 | 仅房主 |
| 白名单 | 机长菜单可将成员加入/移出白名单；主菜单可查看名单；名单成员跳过黑名单、昵称和 Steam 等级自动踢出规则 | 所有人（管理房主名单文件） |
| 玩家条件拦截 | 后台检查 Steam 等级是否达到配置下限，并分别检查纯数字 Steam 昵称、纯数字游戏昵称；Steam 等级支持单独记录或踢出 | 仅房主 |
| 机组列表滚动 | 人数超过一屏时可以滚动查看，不会再有看不到的最后几行 | 所有人 |
| 显示玩家名 | 「值班中 / 驾驶中 / 出局」后面直接接上玩家名 | 所有人 |
| 自定义房间名 | 房主可以在机长菜单里直接改大厅名字，也可以改配置文件 | 仅房主 |
| 黑名单管理 | 主界面「黑名单」按钮（在 Discord 下面），点开可以看全部条目并逐条移除（解封即时生效） | 所有人（改的是本机文件） |

---

## 安装

1. 下载 `RoomKit-v1.0.0.zip`
2. 全部解压到**游戏根目录**（和 `MaydayAirways.exe` 同一层）
3. 启动游戏

包内已经带了 BepInEx 6 加载器和它需要的 .NET 运行时，不需要另外装 BepInEx。

Steam 用户可以在库里右键游戏 → 管理 → 浏览本地文件，直接打开游戏目录。

### 已经装过其他 BepInEx 插件？

直接把 `BepInEx\plugins\RoomKit.dll` 放进现有的 `BepInEx\plugins\` 即可，不用覆盖 `BepInEx\core` 和 `dotnet`。

和 MaydayUnlock 这类同样基于 BepInEx 6 的插件**可以共存**，两者互不干涉。

---

## 使用

### 踢人

作为房主打开机长菜单（暂停菜单），右侧「机组」列表里，除自己以外每一行的最右边会有两个按钮：

- **红色出口图标**：踢出本局
- **灰色锁图标**：踢出并加入黑名单

点完之后对方的连接会被断开，他那边会回到主菜单。

按钮点击时才去读「当前这一行是谁」，所以中途有人进出、列表行被复用时也不会踢错人。

### 黑名单

黑名单是一个纯文本文件，第一次运行游戏后出现在：

```
BepInEx\config\RoomKit\blacklist.txt

升级时，如果旧版配置在 `BepInEx\config\MaydayKick`，请将其移动到 `BepInEx\config\RoomKit`。
```

格式是一行一个，SteamID64 加一个 Tab，后面写备注（只给自己看）：

```
7656119xxxxxxxxxx	捣乱的
7656119yyyyyyyyyy	抽烟的
```

- `#` 开头的行当作注释
- 文件改动会**自动热重载**，不用重启游戏
- 被拉黑的人不需要装这个插件，他连进来的时候会被游戏原生的「你已被移出本局」挡掉

不想手动编辑文件的话，主界面 Discord 按钮下面有个**「黑名单」按钮**，点开就是管理面板：每条显示备注名和 SteamID，右侧「移除」一点就解封，不用重启游戏，也不用去改 txt。条目多了可以在面板里直接滚动。

### 白名单

房主在机长菜单的机组成员行点击勾选按钮即可添加/移除玩家；主菜单 Discord/黑名单按钮附近的**「白名单」**按钮可以查看全部条目并移除。文件路径：

```
BepInEx\config\RoomKit\whitelist.txt
```

每行依次为 SteamID64、Tab、昵称、可选备注（备注前也用 Tab 分隔）：

```
7656119xxxxxxxxxx	好友昵称	认识多年的队友
7656119yyyyyyyyyy	常驻玩家
```

`#` 开头的行是注释，文件修改后会热重载。白名单优先于黑名单，并跳过纯数字昵称和 Steam 等级自动踢出规则；加入白名单时会自动从黑名单移除。白名单成员行不显示拉黑按钮；如需拉黑，先取消白名单，再使用拉黑按钮。房主仍可手动踢出白名单成员。

### 玩家条件拦截

Steam 等级通过异步访问 Steam 社区公开资料页 `https://steamcommunity.com/profiles/{SteamID64}` 查询，不要求玩家是房主好友。玩家先正常进入游戏，随后进入后台查询队列；`SteamLevelQueryConcurrency` 配置同时查询数，默认 5，范围 1-32。`SteamLevelMinimum` 设置 Steam 等级最低要求，默认 1，最小为 1；查询到的等级低于此值（包括 0 级）时视为不合格。仅当页面包含 `profile_private_info` 标记时识别为私密资料；公开页缺少等级标记也属于已识别的资料结果，不是查询意外，`KickZeroSteamLevel` 开启时会按等级资格规则处理。只有超时、请求失败或其他无法完成查询的错误由 `KickOnSteamLevelLookupFailure` 控制是否断开（默认关闭，即保留玩家）。`LogSteamLevel` 独立控制是否打印查询信息和失败原因。查询结果缓存 10 分钟，失败结果缓存 1 分钟。Steam 昵称、游戏昵称数字检查仍分别由 `BlockNumericSteamName`、`BlockNumericInGameName` 控制，昵称首尾空格不影响数字判断。

### 房间名

两种方式，任选：

- 机长菜单左边的输入框，改完回车立即生效
- 配置文件 `BepInEx\config\local.mayday.kick.cfg` 里的 `[Room] Name`，支持 `{host}` 占位符（会替换成房主的 Steam 昵称），改完几秒内自动同步

大厅名会自动追加 ` | 下限Steam公开Lv<等级>`，等级取 `SteamLevelMinimum`（最低按 1 处理）；房间名总长度遵守 Steam 的 63 字符限制。

---

## 配置文件

路径：`BepInEx\config\local.mayday.kick.cfg`

```ini
[Room]

## 自定义大厅名。留空则用游戏默认名字。支持 {host} 占位符（房主 Steam 昵称）。
# Setting type: String
# Default value:
Name =

[Moderation]

## 打印 Steam 等级查询结果，不会踢人。
# Setting type: Boolean
# Default value: true
LogSteamLevel = true

## 查询后踢出等级不合格的玩家（玩家先进场）；查询失败由 KickOnSteamLevelLookupFailure 控制。
# Setting type: Boolean
# Default value: false
KickZeroSteamLevel = false

## Steam 等级查询超时、请求失败或发生意外错误时是否踢出玩家；false 时保留玩家。
# Setting type: Boolean
# Default value: false
KickOnSteamLevelLookupFailure = false

## Steam 等级最低要求；低于此等级的玩家在踢出开关开启时会被踢出（最小值 1）。
# Setting type: Int32
# Default value: 1
SteamLevelMinimum = 1

## Steam 等级公开资料查询最大并发数（范围 1-32）。
# Setting type: Int32
# Default value: 5
SteamLevelQueryConcurrency = 5

## 拒绝 Steam 昵称仅由数字组成的玩家。
# Setting type: Boolean
# Default value: true
BlockNumericSteamName = true

## 拒绝游戏内昵称仅由数字组成的玩家。
# Setting type: Boolean
# Default value: true
BlockNumericInGameName = true
```

---

## 装好了吗？

游戏启动后，窗口标题会变成：

```
BepInEx 6.0.0-be.793 - MaydayAirways
```

看到 `BepInEx` 字样就说明加载器生效了。再打开 `BepInEx\LogOutput.log`，正常应该有：

```
[RoomKit] loaded v1.0.0 (kick + blacklist + whitelist + queued Steam checks + player filters + crew scroll + room name)
```

---

## 已知限制

- **只有房主能踢人**。客户端看不到这两个按钮，这是故意的，不是没装好。
- **Steam 大厅层面移除不了成员**。Steamworks 没有提供「房主把人移出大厅」的接口，所以踢人是在游戏连接层断开他；他客户端收到断线后会自己退出大厅，但这一步是对方客户端做的，不是房主强制。
- **黑名单只拦新连接**。已经在局里的人不会被立刻踢，需要手动踢一次。
- 房间名最长 63 字符，超了会被截断（Steam 的限制）。

---

## 更新日志

### v1.0.0

首个公开版本。

- 踢人：机长菜单「机组」列表每行右侧红色按钮，只有房主看得到
- 踢人并拉黑：锁形按钮，踢的同时把 SteamID 写进黑名单，之后对方连进来会在认证阶段被挡掉
- 黑名单管理：主界面 Discord 下方的「黑名单」按钮，面板里可以看全部条目并逐条移除，解封即时生效
- 机组列表滚动：人数超过一屏时可以滚动查看
- 显示玩家名：「值班中 / 驾驶中 / 出局」后面接上玩家名
- 自定义房间名：机长菜单输入框或配置文件，支持 {host} 占位符

---

## 整合与授权

- 本模组代码采用 **MIT** 协议，见 `LICENSE`。**欢迎整合进整合包、录视频、开直播、放到自己的服务器，注明出处即可，不用另行申请。**
- 唯一的请求：**请勿用于付费销售**（比如把它塞进需要付费购买的整合包或付费会员里）。MIT 本身不禁止商用，这条是作者的请求，不属于许可证条款。
- 发布包里带了 BepInEx、Unity Doorstop 和 .NET 运行时，它们各有各的许可证，详见 `THIRD-PARTY-NOTICES.md` 和 `LICENSE-BepInEx.txt`。

---

## 卸载

删掉游戏目录下这几项即可，游戏文件本来就是原样的：

```
winhttp.dll
doorstop_config.ini
.doorstop_version
dotnet\
BepInEx\
```

如果只想停用插件但保留加载器，删掉 `BepInEx\plugins\RoomKit.dll` 就行。

---

## 从源码构建

需要 .NET SDK 6.0 或更高。

当前模组的 C# 源码和项目文件位于 `src/RoomKit/`；`src/` 下可以按模组名继续添加其它模组目录，每个目录独立构建。

```
dotnet build src\RoomKit\RoomKit.csproj -c Release
```

产物在 `src/RoomKit/bin/Release/RoomKit.dll`。项目文件里的 `GameDir` 属性指向游戏目录，用来引用 BepInEx 和 IL2CPP 互操作程序集，按自己的路径改一下。

---

## 风险提示

修改游戏联机行为可能违反服务条款。建议只和熟人开房时使用，被举报或封号的风险自行承担。