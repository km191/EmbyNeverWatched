using System;
using System.Collections.Generic;
using System.Threading;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace EmbyNeverWatched
{
    /// <summary>
    /// Never Watched —— 永不标记已观看。
    ///
    /// 原理：Emby 在播放结束时会把条目标记为"已观看"（Played=true）并写入数据库，
    /// 随后触发 <see cref="IUserDataManager.UserDataSaved"/> 事件。本插件监听该事件
    /// （以及 <see cref="ISessionManager.PlaybackStopped"/>），一旦发现任何条目被标记为
    /// 已观看，立即把 Played/PlayCount/LastPlayedDate/PlaybackPositionTicks 重置为未观看，
    /// 再以 UserDataSaveReason.Import 重新保存。由于重置保存的 Played=false，不会再触发
    /// 二次重置，无递归风险。整个过程同步完成，秒级生效，不依赖任何定时脚本。
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IDisposable
    {
        private readonly IUserDataManager _userDataManager;
        private readonly ISessionManager _sessionManager;
        private readonly ILogger _logger;

        // 重入保护：防止"重置保存"再次触发本插件逻辑造成循环写库
        private readonly object _lock = new object();
        private readonly HashSet<string> _resetting = new HashSet<string>();

        public Plugin(
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            IServerApplicationHost appHost)
            : base(applicationPaths, xmlSerializer)
        {
            _logger = appHost.Resolve<ILogManager>().GetLogger(GetType().Name);
            _userDataManager = appHost.Resolve<IUserDataManager>();
            _sessionManager = appHost.Resolve<ISessionManager>();

            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            _userDataManager.UserDataSaved += OnUserDataSaved;

            // 注意：此处不能读取 Configuration！Emby 在插件实例化完成后才调用
            // SetAttributes 设置 AssemblyFilePath，若此刻访问 Configuration 会导致
            // Path.Combine(PluginConfigurationsPath, null) 抛 ArgumentNullException。
            _logger.Info("EmbyNeverWatched 插件已加载。");
        }

        /// <summary>
        /// 安全读取配置：仅在事件处理器中调用（此时插件属性已由 Emby 初始化完成）。
        /// 读取失败时回退到默认配置，绝不拖垮服务端。
        /// </summary>
        private PluginConfiguration GetConfig()
        {
            try
            {
                return Configuration ?? new PluginConfiguration();
            }
            catch (Exception ex)
            {
                _logger.Error("EmbyNeverWatched: 读取配置失败，使用默认配置。", ex);
                return new PluginConfiguration();
            }
        }

        public override string Name => "Never Watched";

        public override string Description =>
            "自动把条目已观看状态重置为未观看（Played=false），并可选清空播放进度，" +
            "让媒体库永远显示未观看，秒级生效，无需定时脚本。";

        /// <summary>用户在界面上手动标记已观看，或任意来源把 Played 置 true 时兜底重置。</summary>
        private void OnUserDataSaved(object sender, UserDataSaveEventArgs e)
        {
            try
            {
                PluginConfiguration config = GetConfig();
                if (!config.Enabled) return;
                if (e == null || e.User == null || e.Item == null || e.UserData == null) return;
                if (!e.UserData.Played) return;
                if (!ShouldInterceptReason(config, e.SaveReason)) return;

                ResetUserData(config, e.User, e.Item, e.CollectionFolders, "UserDataSaved:" + e.SaveReason);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("EmbyNeverWatched: 处理 UserDataSaved 事件出错", ex);
            }
        }

        /// <summary>播放停止后兜底：确保已观看被撤销；若配置了清空进度，同时清空播放位置。</summary>
        private void OnPlaybackStopped(object sender, PlaybackStopEventArgs e)
        {
            try
            {
                PluginConfiguration config = GetConfig();
                if (!config.Enabled) return;
                if (e == null || e.Item == null || e.Users == null) return;

                foreach (User user in e.Users)
                {
                    if (user == null) continue;
                    ResetUserData(config, user, e.Item, e.CollectionFolders, "PlaybackStopped");

                    if (config.ResetPosition)
                    {
                        ClearPlaybackPosition(user, e.Item, e.CollectionFolders);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorException("EmbyNeverWatched: 处理 PlaybackStopped 事件出错", ex);
            }
        }

        private bool ShouldInterceptReason(PluginConfiguration config, UserDataSaveReason reason)
        {
            switch (reason)
            {
                case UserDataSaveReason.PlaybackFinished:
                case UserDataSaveReason.PlaybackProgress:
                    return true;
                case UserDataSaveReason.TogglePlayed:
                    return config.BlockManualToggle;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 把单个条目的已观看状态重置为未观看。
        /// </summary>
        private void ResetUserData(PluginConfiguration config, User user, BaseItem item, BaseItem[] collectionFolders, string source)
        {
            string key = GetKey(user, item);
            lock (_lock)
            {
                if (_resetting.Contains(key)) return;
                _resetting.Add(key);
            }

            try
            {
                UserItemData userData = _userDataManager.GetUserData(user, item);
                if (userData == null) return;

                bool positionClearNeeded = config.ResetPosition && userData.PlaybackPositionTicks != 0;
                bool needSave = userData.Played
                    || userData.PlayCount != 0
                    || userData.LastPlayedDate.HasValue
                    || positionClearNeeded;

                if (!needSave) return;

                _logger.Info("EmbyNeverWatched: 重置已观看 [{0}] user={1} item={2}({3}) Played:{4}->false",
                    source, user.Name, item.Name, item.GetType().Name, userData.Played);

                userData.Played = false;
                userData.PlayCount = 0;
                userData.LastPlayedDate = null;
                if (config.ResetPosition)
                {
                    userData.PlaybackPositionTicks = 0;
                }

                // 用 Import 原因保存：不会再次被判定为"播放完成已观看"，避免无限循环；
                // 同时该保存会触发 UserDataChanged 广播，客户端秒级刷新为未观看。
                _userDataManager.SaveUserData(
                    user, item, collectionFolders, userData,
                    UserDataSaveReason.Import, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("EmbyNeverWatched: 重置 UserData 失败 user={0} item={1}",
                    ex, user.Name, item.Name);
            }
            finally
            {
                lock (_lock)
                {
                    _resetting.Remove(key);
                }
            }
        }

        /// <summary>仅清空播放进度（用于"清空进度"开关打开时，部分播放也立即清空）。</summary>
        private void ClearPlaybackPosition(User user, BaseItem item, BaseItem[] collectionFolders)
        {
            string key = GetKey(user, item);
            lock (_lock)
            {
                if (_resetting.Contains(key)) return;
                _resetting.Add(key);
            }

            try
            {
                UserItemData userData = _userDataManager.GetUserData(user, item);
                if (userData == null || userData.PlaybackPositionTicks == 0) return;

                _logger.Info("EmbyNeverWatched: 清空播放进度 user={0} item={1}", user.Name, item.Name);
                userData.PlaybackPositionTicks = 0;
                _userDataManager.SaveUserData(
                    user, item, collectionFolders, userData,
                    UserDataSaveReason.Import, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("EmbyNeverWatched: 清空播放进度失败 user={0} item={1}",
                    ex, user.Name, item.Name);
            }
            finally
            {
                lock (_lock)
                {
                    _resetting.Remove(key);
                }
            }
        }

        private static string GetKey(User user, BaseItem item)
        {
            return user.Id.ToString() + "|" + item.Id.ToString();
        }

        /// <summary>提供 Web 端配置页（设置 → 我的插件 → Never Watched）。</summary>
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "neverwatched",
                    DisplayName = "Never Watched",
                    EnableInMainMenu = true,
                    MenuSection = "server",
                    MenuIcon = "visibility_off",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
                },
                new PluginPageInfo
                {
                    Name = "neverwatchedjs",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.js"
                }
            };
        }

        public void Dispose()
        {
            _sessionManager.PlaybackStopped -= OnPlaybackStopped;
            _userDataManager.UserDataSaved -= OnUserDataSaved;
            GC.SuppressFinalize(this);
        }
    }
}
