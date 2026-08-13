define(['baseView', 'loading', 'emby-checkbox', 'emby-button'], function (BaseView, loading) {
    'use strict';

    // 插件 Id：与 AssemblyInfo.cs 中的程序集级 Guid 保持一致
    var pluginId = 'e628094a-b105-40f0-a815-b7a4f593bb88';

    function loadPage(page, config) {
        page.querySelector('.chkEnabled').checked = config.Enabled !== false;
        page.querySelector('.chkResetPosition').checked = config.ResetPosition !== false;
        page.querySelector('.chkBlockManualToggle').checked = config.BlockManualToggle !== false;
        loading.hide();
    }

    function onSubmit(e) {
        e.preventDefault();
        loading.show();
        var form = this;
        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            config.Enabled = form.querySelector('.chkEnabled').checked;
            config.ResetPosition = form.querySelector('.chkResetPosition').checked;
            config.BlockManualToggle = form.querySelector('.chkBlockManualToggle').checked;
            ApiClient.updatePluginConfiguration(pluginId, config).then(Dashboard.processPluginConfigurationUpdateResult);
        });
        return false;
    }

    function getConfig() {
        return ApiClient.getPluginConfiguration(pluginId);
    }

    function View(view, params) {
        BaseView.apply(this, arguments);
        view.querySelector('form').addEventListener('submit', onSubmit);
    }

    Object.assign(View.prototype, BaseView.prototype);

    View.prototype.onResume = function (options) {
        BaseView.prototype.onResume.apply(this, arguments);
        loading.show();
        var page = this.view;
        getConfig().then(function (response) {
            loadPage(page, response);
        });
    };

    return View;
});
