# 第三方组件声明

本发布包（`MaydayKick-v1.0.0.zip`）里除了 `MaydayKick.dll` 之外，还带了运行它所需的第三方组件。
这些组件的版权归各自作者所有，按各自的许可证分发，以下是清单。

| 组件 | 版本 | 许可证 | 项目地址 |
| --- | --- | --- | --- |
| BepInEx | 6.0.0-be.793 | LGPL-2.1 | https://github.com/BepInEx/BepInEx |
| Unity Doorstop（`winhttp.dll`、`doorstop_config.ini`、`.doorstop_version`） | 4.6.0 | LGPL-2.1 | https://github.com/NeighTools/UnityDoorstop |
| .NET 运行时（`dotnet\` 目录） | 6.0.7 | MIT | https://github.com/dotnet/runtime |
| Harmony / MonoMod 运行时（`BepInEx\core` 内） | 随 BepInEx 分发 | MIT | https://github.com/BepInEx/HarmonyX |
| Il2CppInterop（`BepInEx\core` 内） | 随 BepInEx 分发 | MIT | https://github.com/BepInEx/Il2CppInterop |

## 说明

- **BepInEx 与 Unity Doorstop 采用 LGPL-2.1**，完整许可证文本见同目录下的 `LICENSE-BepInEx.txt`。
  它们是以独立程序集形式随包分发的，插件本身通过运行时加载，属于动态链接，不构成对 BepInEx 源码的修改。
  如果需要 BepInEx 的源代码，可在上面的项目地址获取；如需重新链接 BepInEx，本包中的二进制文件即为对应的完整可替换文件。
- **.NET 运行时采用 MIT 许可证**，版权归 .NET Foundation and Contributors 所有。
- **本包不包含游戏本体、游戏资源或游戏程序集的任何文件。**
  游戏程序集（`Assembly-CSharp.dll`、`Mirror.dll` 等）仅在本机编译插件时被引用，不会被分发。
- MaydayKick 自身的代码采用 MIT 许可证，见 `LICENSE`。
