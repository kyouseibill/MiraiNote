// Mirai 桌面壳（任务卡 SHELL）：托盘 / 全局热键 / 捕获小窗 / 原生通知 / 自启动 / 偏好持久化。
// 模块：prefs（plugin-store 偏好）、capture（捕获窗与主窗导航）、tray（托盘菜单）、notify（系统通知）。
//
// 偏好流转：设置页（UI 流）经 plugin:store|set 直写 mirai-settings.json 的 pref.* 键，
// 本壳监听 store://change 实时生效（换键注册 / 自启动 / 提醒暂停态）；托盘与命令路径写入同键。
mod capture;
mod notify;
mod prefs;
mod tray;

use tauri::{AppHandle, Emitter, Listener, Manager};
use tauri_plugin_global_shortcut::{Builder as ShortcutBuilder, Shortcut, ShortcutState};
use tauri_plugin_notification::NotificationExt;

/// 自启动追加参数：开机启动时隐藏主窗、仅驻留托盘（见 setup）
const AUTOSTART_ARG: &str = "--mirai-minimized";

/// 设置页直写 store 后的实时生效逻辑（热键 / 自启动 / 提醒暂停态与托盘菜单）
fn on_store_change(app: &AppHandle, payload: &serde_json::Value) {
    if payload.get("path").and_then(|v| v.as_str()) != Some(prefs::STORE_FILE) {
        return; // 其他 store 文件（UI 流另有使用）不归本壳管
    }
    match payload.get("key").and_then(|v| v.as_str()) {
        Some("pref.captureHotkey") => {
            let hotkey = prefs::hotkey(app);
            if let Err(message) = capture::apply_hotkey(app, &hotkey) {
                eprintln!("[mirai-shell] 换键失败：{message}");
                let _ = app.emit("mirai:hotkey-error", &message);
                let _ = app
                    .notification()
                    .builder()
                    .title("Mirai 全局热键更换失败")
                    .body(&message)
                    .show();
            }
            tray::refresh(app);
        }
        Some("pref.autostart") => prefs::sync_autostart(app),
        // 通知开关在 notify 命令内即时读取；暂停态需同步捕获窗轮询与托盘勾选
        Some("pref.dueNotification") | Some("pref.remindersPaused") => {
            let _ = app.emit("mirai:reminders-changed", prefs::reminders_paused(app));
            tray::refresh(app);
        }
        _ => {}
    }
}

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_autostart::init(
            tauri_plugin_autostart::MacosLauncher::LaunchAgent,
            Some(vec![AUTOSTART_ARG]),
        ))
        .plugin(tauri_plugin_notification::init())
        .plugin(tauri_plugin_store::Builder::new().build())
        .plugin(
            ShortcutBuilder::new()
                .with_handler(|app, shortcut, event| {
                    // 只在按下沿触发，且只响应当前注册的热键（换键后旧快捷对象不匹配）
                    if event.state == ShortcutState::Pressed {
                        if let Ok(current) = prefs::hotkey(app).parse::<Shortcut>() {
                            if *shortcut == current {
                                capture::toggle(app);
                            }
                        }
                    }
                })
                .build(),
        )
        .setup(|app| {
            let handle = app.handle().clone();
            // 1) 偏好存储（先于托盘/热键：都要读 pref.* 键）
            prefs::init(&handle)?;
            // 2) 托盘常驻
            tray::setup(&handle)?;
            // 3) 捕获窗隐藏常驻（兼作到期通知轮询宿主，保证 JS 定时器存活）
            capture::ensure_window(&handle, false)?;
            // 4) 全局热键状态 + 首次注册：冲突/失败 → 优雅降级（托盘菜单仍可捕获，并提示用户改键）
            app.manage(capture::HotkeyState::default());
            let hotkey = prefs::hotkey(&handle);
            if let Err(message) = capture::apply_hotkey(&handle, &hotkey) {
                eprintln!("[mirai-shell] {message}");
                let _ = handle.emit("mirai:hotkey-error", &message);
                let _ = handle
                    .notification()
                    .builder()
                    .title("Mirai 全局热键注册失败")
                    .body(&message)
                    .show();
            }
            // 5) 设置页直写 pref.* 键的实时生效
            let listener_handle = handle.clone();
            handle.listen_any("store://change", move |event| {
                let payload: serde_json::Value =
                    serde_json::from_str(event.payload()).unwrap_or_default();
                on_store_change(&listener_handle, &payload);
            });
            // 6) 自启动同步（store 无显式值时不动作）；开机自启 → 主窗驻留托盘
            prefs::sync_autostart(&handle);
            if std::env::args().any(|a| a == AUTOSTART_ARG) {
                if let Some(main) = handle.get_webview_window("main") {
                    let _ = main.hide();
                }
            }
            Ok(())
        })
        .on_window_event(|window, event| match event {
            // 主窗关闭按钮 = 最小化到托盘（退出走托盘菜单）
            tauri::WindowEvent::CloseRequested { api, .. } if window.label() == "main" => {
                api.prevent_close();
                let _ = window.hide();
            }
            // 捕获窗失焦自动隐藏（Esc 由前端隐藏）
            tauri::WindowEvent::Focused(false) if window.label() == capture::CAPTURE_LABEL => {
                let _ = window.hide();
            }
            _ => {}
        })
        .invoke_handler(tauri::generate_handler![
            prefs::get_prefs,
            prefs::set_hotkey,
            prefs::set_notification_enabled,
            prefs::set_reminders_paused,
            prefs::set_autostart,
            capture::toggle_capture,
            capture::hide_capture,
            capture::open_main,
            notify::show_task_notification,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
