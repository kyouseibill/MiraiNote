// 捕获小窗（设计 §5.2 / 视觉稿 ④）：无边框 720×120、常驻（隐藏），
// 热键/托盘唤起，失焦自动隐藏（见 lib.rs 的窗口事件），Esc 由前端隐藏。
// 注意：该窗同时是到期通知轮询的常驻宿主（前端 /capture 页面负责 30s 轮询）。
use std::sync::Mutex;
use tauri::{AppHandle, Emitter, Manager, WebviewUrl, WebviewWindowBuilder};
use tauri_plugin_global_shortcut::Shortcut;

pub const CAPTURE_LABEL: &str = "capture";
pub const CAPTURE_W: f64 = 720.0;
pub const CAPTURE_H: f64 = 120.0;

/// 顶部居中坐标（物理像素）
fn top_center_position(app: &AppHandle) -> Option<(f64, f64)> {
    let monitor = app.primary_monitor().ok().flatten()?;
    let scale = monitor.scale_factor();
    let size = monitor.size();
    let x = (size.width as f64 - CAPTURE_W * scale) / 2.0;
    let y = 88.0 * scale;
    Some((x.max(0.0), y))
}

/// 创建捕获窗（幂等）。show=false 时隐藏常驻（自启动场景下不惊扰用户）。
pub fn ensure_window(app: &AppHandle, show: bool) -> tauri::Result<()> {
    if app.get_webview_window(CAPTURE_LABEL).is_some() {
        return Ok(());
    }
    let mut builder = WebviewWindowBuilder::new(
        app,
        CAPTURE_LABEL,
        WebviewUrl::App("capture".into()),
    )
    .title("Mirai 快速捕获")
    .inner_size(CAPTURE_W, CAPTURE_H)
    .resizable(false)
    .maximizable(false)
    .decorations(false)
    .always_on_top(true)
    .skip_taskbar(true)
    .shadow(true)
    .visible(show);

    if let Some((x, y)) = top_center_position(app) {
        builder = builder.position(x, y);
    }

    builder.build().map(|_| ())
}

/// 唤起（每次重新顶部居中定位，适配显示器变化）并通知前端重置输入
pub fn show(app: &AppHandle) {
    if let Some(win) = app.get_webview_window(CAPTURE_LABEL) {
        if let Some((x, y)) = top_center_position(app) {
            let _ = win.set_position(tauri::PhysicalPosition::new(x as i32, y as i32));
        }
        let _ = win.show();
        let _ = win.set_focus();
        let _ = app.emit_to(CAPTURE_LABEL, "mirai:capture-shown", ());
    }
}

pub fn hide(app: &AppHandle) {
    if let Some(win) = app.get_webview_window(CAPTURE_LABEL) {
        let _ = win.hide();
    }
}

pub fn toggle(app: &AppHandle) {
    let visible = app
        .get_webview_window(CAPTURE_LABEL)
        .map(|w| w.is_visible().unwrap_or(false))
        .unwrap_or(false);
    if visible {
        hide(app);
    } else {
        show(app);
    }
}

/// 当前已注册的全局热键（setup 时 app.manage 注入）。换键 = 注销旧 + 注册新，失败回滚旧键。
#[derive(Default)]
pub struct HotkeyState(pub Mutex<Option<Shortcut>>);

/// 注册/更换全局热键（幂等：与当前键相同则直接成功）。
/// 失败（格式无效/被其他应用占用）时回滚保持旧键，返回错误由调用方降级处理。
pub fn apply_hotkey(app: &AppHandle, hotkey: &str) -> Result<(), String> {
    use tauri_plugin_global_shortcut::GlobalShortcutExt;

    let new: Shortcut = hotkey
        .parse()
        .map_err(|_| format!("热键格式无效：{hotkey}（示例：Ctrl+Shift+Space）"))?;
    let gs = app.global_shortcut();
    let state = app.state::<HotkeyState>();
    let mut current = state
        .0
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner());
    if current.as_ref() == Some(&new) {
        return Ok(()); // 已是该键（store://change 与 set_hotkey 双路径幂等）
    }
    let old = current.take();
    if let Some(old) = &old {
        let _ = gs.unregister(*old);
    }
    match gs.register(new) {
        Ok(()) => {
            *current = Some(new);
            Ok(())
        }
        Err(e) => {
            if let Some(old) = &old {
                let _ = gs.register(*old); // 回滚，保持旧键可用
                *current = Some(*old);
            }
            Err(format!("注册失败（可能被其他应用占用）：{e}"))
        }
    }
}

// ---------------- Tauri commands ----------------

#[tauri::command]
pub fn toggle_capture(app: AppHandle) {
    toggle(&app);
}

#[tauri::command]
pub fn hide_capture(app: AppHandle) {
    hide(&app);
}

/// 打开主窗并导航（path 白名单防注入）。前端若有 window.__miraiNavigate 钩子则走前端路由，
/// 否则整页 location.assign 兜底（SPA 重新加载到目标路由）。
pub fn show_main(app: &AppHandle, path: &str) {
    let path = match path {
        "/inbox" => "/inbox",
        "/tasks" => "/tasks",
        _ => "/",
    };
    if let Some(win) = app.get_webview_window("main") {
        let _ = win.show();
        let _ = win.unminimize();
        let _ = win.set_focus();
        let js = format!(
            "if(window.__miraiNavigate){{window.__miraiNavigate('{path}')}}else{{location.assign('{path}')}}"
        );
        let _ = win.eval(&js);
    }
}

#[tauri::command]
pub fn open_main(app: AppHandle, path: Option<String>) {
    show_main(&app, path.as_deref().unwrap_or("/"));
}
