// 原生通知（设计 §5.2 / 视觉稿 ⑥）：到期 Memo 轮询在前端 /capture 页面（复用登录态），
// 检出后调本命令转系统通知。节流（每日 ≤5）与去重由前端按天计数。
// 说明：Tauri 通知插件 M1 不支持点击回调/内联按钮，点击行为依赖系统默认（激活应用），
// 「点击聚焦主窗」由 Windows 对应用的激活兜底，任务级深链留待 M2（设计 §5.2 备注）。
use tauri::AppHandle;
use tauri_plugin_notification::NotificationExt;

use crate::prefs;

/// 暂停提醒 / 通知总开关关闭时静默跳过（双重保险：前端也判一次）
#[tauri::command]
pub fn show_task_notification(app: AppHandle, title: String, body: String) -> Result<(), String> {
    if prefs::reminders_paused(&app) || !prefs::notifications_enabled(&app) {
        return Ok(());
    }
    app.notification()
        .builder()
        .title(title)
        .body(body)
        .show()
        .map_err(|e| e.to_string())
}
