using System.Reflection;
using System.Runtime.InteropServices;

// 插件 Id：Emby 加载器读取程序集级 Guid 特性作为插件唯一 Id，
// 配置文件、API 路由（/Plugins/{Id}/Configuration）均依赖它。
[assembly: Guid("e628094a-b105-40f0-a815-b7a4f593bb88")]

[assembly: AssemblyTitle("EmbyNeverWatched")]
[assembly: AssemblyDescription("永远不把媒体标记为已观看")]
[assembly: AssemblyProduct("EmbyNeverWatched")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0.0")]
[assembly: ComVisible(false)]
