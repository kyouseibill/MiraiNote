// 偏好持久化（任务卡 SHELL-6）：热键 / 通知开关 / 自启动 → tauri-plugin-store。
//
// 【与 UI 流的键名契约】（见 desktop/src/stores/settings.ts 头部注释，SHELL 侧只读对齐）：
//   store 文件：mirai-settings.json
//     pref.captureHotkey    string   全局捕获热键（默认 Ctrl+Shift+Space，人类可读格式）
//     pref.dueNotification  boolean  到期原生通知总开关
//     pref.autostart        boolean  开机自启
//   SHELL 私有键（同文件，UI 不读写，互不干扰）：
//     pref.remindersPaused  boolean  托盘「暂停提醒」
//
// 设置页（UI 流）直接经 plugin:store|set 写入上述键，不走本模块命令；
// 实时生效（换键注册/自启动开关/托盘勾选态）由 lib.rs 的 store://change 监听驱动。
// 自启动的持久状态由 autostart 插件自身（注册表/启动项）管理，store 值仅作意图标记。
use serde::Serialize;
use tauri::AppHandle;
use tauri_plugin_autostart::ManagerExt;
use tauri_plugin_store::StoreExt;

use crate::tray;

pub const STORE_FILE: &str = "mirai-settings.json";
const KEY_HOTKEY: &str = "pref.captureHotkey";
const KEY_NOTIFICATIONS: &str = "pref.dueNotification";
const KEY_REMINDERS_PAUSED: &str = "pref.remindersPaused";
const KEY_AUTOSTART: &str = "pref.autostart";

pub const DEFAULT_HOTKEY: &str = "Ctrl+Shift+Space";

#[derive(Serialize, Clone, Debug)]
#[serde(rename_all = "camelCase")] // 前端（CaptureWindow.vue get_prefs）按 camelCase 读取
pub struct Prefs {
    pub hotkey: String,
    pub notifications_enabled: bool,
    pub reminders_paused: bool,
}

/// 创建/加载偏好存储（setup 阶段调用一次，之后 get_store 均可命中；
/// UI 流前端 plugin:store|load 同路径会命中同一 store 实例）
pub fn init(app: &AppHandle) -> tauri_plugin_store::Result<()> {
    app.store(STORE_FILE).map(|_| ())
}

fn store(app: &AppHandle) -> Option<std::sync::Arc<tauri_plugin_store::Store<tauri::Wry>>> {
    app.get_store(STORE_FILE)
}

fn read_bool(app: &AppHandle, key: &str, default: bool) -> bool {
    store(app)
        .and_then(|s| s.get(key))
        .and_then(|v| v.as_bool())
        .unwrap_or(default)
}

pub fn hotkey(app: &AppHandle) -> String {
    store(app)
        .and_then(|s| s.get(KEY_HOTKEY))
        .and_then(|v| v.as_str().map(str::to_owned))
        .unwrap_or_else(|| DEFAULT_HOTKEY.to_string())
}

pub fn notifications_enabled(app: &AppHandle) -> bool {
    read_bool(app, KEY_NOTIFICATIONS, true)
}

pub fn reminders_paused(app: &AppHandle) -> bool {
    read_bool(app, KEY_REMINDERS_PAUSED, false)
}

fn write<V: Into<serde_json::Value>>(app: &AppHandle, key: &str, value: V) -> Result<(), String> {
    let s = store(app).ok_or_else(|| "偏好存储未初始化".to_string())?;
    s.set(key.to_string(), value);
    s.save().map_err(|e| e.to_string())
}

pub fn store_reminders_paused(app: &AppHandle, paused: bool) -> Result<(), String> {
    write(app, KEY_REMINDERS_PAUSED, paused)
}

/// 把 autostart 插件（注册表/启动项）同步为 store 里的意图值。
/// store 无显式值时不动作（尊重用户在系统层面的手动配置，避免首启即改系统）。
pub fn sync_autostart(app: &AppHandle) {
    let Some(wanted) = store(app).and_then(|s| s.get(KEY_AUTOSTART)).and_then(|v| v.as_bool())
    else {
        return;
    };
    let autolaunch = app.autolaunch();
    let current = autolaunch.is_enabled().unwrap_or(false);
    let result = if wanted && !current {
        autolaunch.enable()
    } else if !wanted && current {
        autolaunch.disable()
    } else {
        Ok(())
    };
    if let Err(e) = result {
        eprintln!("[mirai-shell] 自启动同步失败：{e}");
    }
}

// ---------------- Tauri commands（捕获窗 / 程序化调用；设置页走 store://change 路径） ----------------

#[tauri::command]
pub fn get_prefs(app: AppHandle) -> Prefs {
    Prefs {
        hotkey: hotkey(&app),
        notifications_enabled: notifications_enabled(&app),
        reminders_paused: reminders_paused(&app),
    }
}

/// 更换全局热键：注册成功后落库（store://change 监听发现已是当前键则跳过）。
/// 失败返回可展示的错误，旧键保持注册（回滚在 capture::apply_hotkey 内完成）。
#[tauri::command]
pub fn set_hotkey(app: AppHandle, new_hotkey: String) -> Result<String, String> {
    crate::capture::apply_hotkey(&app, &new_hotkey)?;
    write(&app, KEY_HOTKEY, new_hotkey.clone())?;
    tray::refresh(&app); // 托盘菜单同步显示新热键（store://change 只处理 UI 流写入）
    Ok(new_hotkey)
}

#[tauri::command]
pub fn set_notification_enabled(app: AppHandle, enabled: bool) -> Result<(), String> {
    write(&app, KEY_NOTIFICATIONS, enabled)
}

#[tauri::command]
pub fn set_reminders_paused(app: AppHandle, paused: bool) -> Result<(), String> {
    write(&app, KEY_REMINDERS_PAUSED, paused)
}

#[tauri::command]
pub fn set_autostart(app: AppHandle, enabled: bool) -> Result<(), String> {
    write(&app, KEY_AUTOSTART, enabled)?; // 先落意图，再同步插件（失败时下次启动会重试）
    sync_autostart(&app);
    Ok(())
}
