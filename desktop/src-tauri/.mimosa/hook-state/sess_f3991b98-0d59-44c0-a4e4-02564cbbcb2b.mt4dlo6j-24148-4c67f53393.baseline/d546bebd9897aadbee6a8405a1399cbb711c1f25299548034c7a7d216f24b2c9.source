// 系统托盘（设计 §5.2）：常驻图标 + 菜单（打开主窗/快速捕获/今日概览/暂停提醒/退出）。
// 左键单击 = 显隐主窗；主窗关闭按钮 = 最小化到托盘（见 lib.rs CloseRequested）。
use tauri::menu::{CheckMenuItem, Menu, MenuItem, PredefinedMenuItem};
use tauri::tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent};
use tauri::{AppHandle, Emitter, Manager};

use crate::{capture, prefs};

pub const TRAY_ID: &str = "mirai-tray";

/// "ctrl+shift+space" → "Ctrl+Shift+Space"（菜单展示用）
fn humanize(hotkey: &str) -> String {
    hotkey
        .split('+')
        .map(|part| {
            let mut chars = part.chars();
            match chars.next() {
                Some(first) => first.to_uppercase().collect::<String>() + chars.as_str(),
                None => String::new(),
            }
        })
        .collect::<Vec<_>>()
        .join("+")
}

fn build_menu(app: &AppHandle) -> tauri::Result<Menu<tauri::Wry>> {
    let hotkey_text = humanize(&prefs::hotkey(app));
    let open = MenuItem::with_id(app, "open_main", "打开主窗", true, None::<&str>)?;
    let quick = MenuItem::with_id(
        app,
        "quick_capture",
        format!("快速捕获（{hotkey_text}）"),
        true,
        None::<&str>,
    )?;
    let today = MenuItem::with_id(app, "today_overview", "今日概览", true, None::<&str>)?;
    let pause = CheckMenuItem::with_id(
        app,
        "pause_reminders",
        "暂停提醒",
        true,
        prefs::reminders_paused(app),
        None::<&str>,
    )?;
    let separator = PredefinedMenuItem::separator(app)?;
    let quit = MenuItem::with_id(app, "quit", "退出 Mirai", true, None::<&str>)?;
    Menu::with_items(app, &[&open, &quick, &today, &separator, &pause, &quit])
}

fn on_menu_event(app: &AppHandle, event: tauri::menu::MenuEvent) {
    match event.id.as_ref() {
        "open_main" => capture::show_main(app, "/"),
        "quick_capture" => capture::toggle(app),
        "today_overview" => capture::show_main(app, "/"),
        "pause_reminders" => {
            let paused = !prefs::reminders_paused(app);
            if prefs::store_reminders_paused(app, paused).is_ok() {
                let _ = app.emit("mirai:reminders-changed", paused);
                refresh(app); // 同步菜单勾选态
            }
        }
        "quit" => app.exit(0),
        _ => {}
    }
}

/// 热键/暂停状态变化后重建菜单
pub fn refresh(app: &AppHandle) {
    if let Some(tray) = app.tray_by_id(TRAY_ID) {
        if let Ok(menu) = build_menu(app) {
            let _ = tray.set_menu(Some(menu));
        }
    }
}

fn toggle_main(app: &AppHandle) {
    if let Some(win) = app.get_webview_window("main") {
        if win.is_visible().unwrap_or(false) {
            let _ = win.hide();
        } else {
            let _ = win.show();
            let _ = win.unminimize();
            let _ = win.set_focus();
        }
    }
}

pub fn setup(app: &AppHandle) -> tauri::Result<()> {
    let menu = build_menu(app)?;
    let mut builder = TrayIconBuilder::with_id(TRAY_ID)
        .tooltip("Mirai · MiraiNote（关闭窗口即最小化到托盘）")
        .menu(&menu)
        .show_menu_on_left_click(false)
        .on_menu_event(on_menu_event)
        .on_tray_icon_event(|tray, event| {
            if let TrayIconEvent::Click {
                button: MouseButton::Left,
                button_state: MouseButtonState::Up,
                ..
            } = event
            {
                toggle_main(tray.app_handle());
            }
        });
    if let Some(icon) = app.default_window_icon() {
        builder = builder.icon(icon.clone());
    }
    builder.build(app)?;
    Ok(())
}
