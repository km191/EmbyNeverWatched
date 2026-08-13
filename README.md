# Emby Never Watched（永不标记已观看）

> **解决 Emby 播放器看完视频后条目被标记为"已播放"、导致媒体库中已播放的剧集无法正常显示的问题。**
>
> 装上它之后，无论怎么播放，Emby 都不会把任何电影/剧集标记为"已观看"——
> 已播放/未播放的剧集始终正常显示，媒体库不再因为"已播放"而隐藏或错乱条目。
>
> **Solves the problem where Emby marks videos as "played" after watching, which breaks the
> display of played episodes in the library. With this plugin, items are never marked as
> watched, so all movies & episodes always show correctly.**

| | |
|---|---|
| 兼容 | Emby **4.9.x**（Windows 原生应用版 / **Docker 版** / Linux / macOS） |
| 运行时 | .NET 8（Emby 4.9 自带） |
| 插件 Id | `e628094a-b105-40f0-a815-b7a4f593bb88` |
| 生效方式 | **秒级**，播放停止后立即撤销"已观看"标记，无需定时脚本 |

---

## 它是做什么的

默认情况下，Emby 在用户把一部电影 / 一集剧集**完整看完**后，会自动把条目标记为
**"已观看 / 已播放"**。如果你的媒体库开启了"隐藏已观看内容"，这些条目就会从列表中
消失；即使不隐藏，已播放的状态也会影响"继续观看""接下来播放"等模块的展示，甚至出现
"已播放的剧集无法正常显示"的情况。

**Never Watched 插件监听 Emby 的播放结束事件，一旦发现任何条目被标记为已观看，
立即把 `Played` 重置为 `false`**（同时清空播放次数、播放位置、最后播放时间）。
因此：

- 电影 / 剧集 / 单集**永远显示为"未观看"**，看完也不会消失；
- 无需任何定时任务，**播放停止的瞬间即生效**；
- 同时可配置**清空播放进度**，让条目不再进入"继续观看"。

---

## 工作原理

1. 插件订阅两个事件（接口均来自反编译 Emby 4.9.5 真实 SDK 核实）：
   - `IUserDataManager.UserDataSaved`——Emby 每次把条目 UserData 写入数据库后触发；
     当 `Played == true`（原因 `PlaybackFinished` / `PlaybackProgress` / `TogglePlayed`）时立即重置；
   - `ISessionManager.PlaybackStopped`——播放停止后的兜底重置。
2. 重置内容：`Played=false`、`PlayCount=0`、`LastPlayedDate=null`，
   可选 `PlaybackPositionTicks=0`。
3. 用 `UserDataSaveReason.Import` 保存重置结果 + 重入锁，**防递归、防循环写库**；
4. 所有回调 try/catch，**异常安全**，不影响服务端任何其它功能（收藏、刮削、转码等）。

> 时序说明：Emby 在 `OnPlaybackStopped` 中**先写库**标记已观看，**随后**才触发事件。
> 插件在事件里重置，保证最终落库为 `Played=false`，并触发 `UserDataChanged` 广播，
> 客户端秒级刷新。

---

## 安装（Docker 版 Emby）

插件 DLL 在 `release/EmbyNeverWatched.dll`（本仓库已附带，也可自行编译，见下文）。

```bash
# 1. 找到你的 Emby 数据目录（Docker 卷挂载目录，通常是 /config）
#    例如：~/emby/config/plugins
mkdir -p ~/emby/config/plugins

# 2. 把插件 DLL 复制到 plugins 根目录
cp release/EmbyNeverWatched.dll ~/emby/config/plugins/EmbyNeverWatched.dll

# 3. 重启 Emby 容器
docker restart <emby容器名或ID>
#    或 docker-compose up -d 重新创建容器
```

加载成功后，日志会出现 `Loading EmbyNeverWatched, Version=1.0.0.0` 与
`EmbyNeverWatched 插件已加载。`。

> ⚠️ **务必放在 `plugins/` 根目录**：Emby 4.9 扫描插件时使用
> `SearchOption.TopDirectoryOnly`，**不扫描子目录**，放子目录里不会被加载。

## 安装（Windows 原生应用版）

```powershell
# 1. 停止 Emby
Stop-Process -Name EmbyServer -Force

# 2. 复制 DLL 到 plugins 根目录
Copy-Item "release\EmbyNeverWatched.dll" `
          "$env:APPDATA\Emby-Server\programdata\plugins\EmbyNeverWatched.dll" -Force

# 3. 启动 Emby
Start-Process -FilePath "$env:APPDATA\Emby-Server\system\EmbyServer.exe"
```

## 安装（Linux / macOS 原生）

找到你的 Emby **数据目录**下的 `plugins` 文件夹（Linux 常见 `/var/lib/emby/plugins` 或
`/var/lib/emby-server/plugins`，macOS 常见 `~/.config/emby-server/plugins`），
把 `release/EmbyNeverWatched.dll` 复制进去，然后 `systemctl restart emby-server`
（或 launchctl / 重开应用）。

---

## 配置

安装后打开 Emby Web → **设置 → 我的插件 → Never Watched**：

| 配置项 | 默认 | 说明 |
|---|---|---|
| 启用插件 | 开 | 总开关。关闭后插件不干预。 |
| 同时清空播放进度 | 开 | 开启后看完/播放过的条目不进"继续观看"。 |
| 拦截手动标记已观看 | 开 | 开启后界面上手动点"标记已观看"也会被立即撤销。 |

也可以直接编辑配置文件：
`<数据目录>/plugins/configurations/EmbyNeverWatched.xml`。

---

## 自行编译

需要 [.NET SDK](https://dotnet.microsoft.com/download)（8.0 或更高，实测 10.x 可编译）。

```bash
# 用 -p:EmbySystemDir 指定你的 Emby 程序目录（含 MediaBrowser.*.dll 的 /system 目录）
dotnet build -c Release -p:EmbySystemDir="/path/to/emby/system"

# 产物：bin/Release/net8.0/EmbyNeverWatched.dll
```

- Windows 原生版程序目录通常为 `C:\Program Files\Emby Server\system`
  或 `%APPDATA%\Emby-Server\system`；
- Docker 版可从容器内取出 `/system` 下的 `MediaBrowser.Common.dll` 等三个程序集
  （或用任意 4.9 版本的 Emby 程序目录）；
- 不传 `EmbySystemDir` 时，csproj 内置默认路径（本机 Windows 路径）。

> 引用的是 **Emby 4.9.x 真实程序集**（`MediaBrowser.Common/Controller/Model`），
> 编译时 `Private=false`，运行时由 Emby 自身提供，插件 DLL 仅约 15KB。

---

## 验证

以下为本插件在 Windows 4.9.5 实测结果（Emby API）：

| 场景 | 结果 |
|---|---|
| 手动标记已观看（TogglePlayed） | ✅ 2 秒后 Played=false、PlayCount=0 |
| 模拟真实播放完成（PlaybackFinished） | ✅ 2 秒后 Played=false |
| 禁用插件后标记 | ✅ 保持 Played=true（证明重置者是插件） |
| 重新启用后再标记 | ✅ 再次重置为 false |
| 剧集聚合 | ✅ 父级 Series 保持未观看 |
| 服务端日志 | ✅ 无错误，重置记录可查 |

---

## 卸载

1. 停止 Emby；
2. 删除 `<数据目录>/plugins/EmbyNeverWatched.dll` 与
   `<数据目录>/plugins/configurations/EmbyNeverWatched.xml`；
3. 重启 Emby。
（或 Web 端 设置 → 我的插件 → Never Watched → 卸载。）

---

## 许可

MIT License。请勿用于商业闭源分发。
