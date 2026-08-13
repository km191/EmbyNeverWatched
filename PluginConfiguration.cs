using MediaBrowser.Model.Plugins;

namespace EmbyNeverWatched
{
    /// <summary>
    /// 插件配置。序列化为 plugins/configurations/EmbyNeverWatched.xml，
    /// 可在 Web 端 设置→我的插件→Never Watched 里修改。
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>总开关：false 时插件完全不干预，条目可正常被标记为已观看。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 是否在撤销已观看的同时清空播放进度（PlaybackPositionTicks=0）。
        /// true：看完后"继续观看"也不会出现该条目；false：保留播放进度但仅撤销已观看标记。
        /// </summary>
        public bool ResetPosition { get; set; } = true;

        /// <summary>
        /// 是否同时拦截"手动标记已观看"（用户在界面上点击标记已观看）。
        /// true：任何已观看标记都会立即被撤销，媒体库永远显示未观看；
        /// false：仅撤销播放结束后自动产生的已观看标记。
        /// </summary>
        public bool BlockManualToggle { get; set; } = true;
    }
}
