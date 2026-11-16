#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct Bootstrap {
    host_url: String,
    token: String,
}

fn validate(host_url: String, token: String) -> Result<Bootstrap, String> {
    let uri = url::Url::parse(&host_url).map_err(|_| "Invalid host configuration")?;
    // CSP and the host contract intentionally fix the Phase 0 desktop endpoint.
    if uri.as_str() != "http://127.0.0.1:43120/"
        || token.len() < 32
        || token.len() > 128
        || token.chars().any(char::is_whitespace)
    {
        return Err("Invalid session configuration".into());
    }
    Ok(Bootstrap {
        host_url: uri.to_string(),
        token,
    })
}

#[tauri::command]
fn bootstrap() -> Result<Bootstrap, String> {
    validate(
        std::env::var("GUILDHALL_HOST_URL").unwrap_or_else(|_| "http://127.0.0.1:43120".into()),
        std::env::var("GUILDHALL_SESSION_TOKEN")
            .map_err(|_| "Start Guildhall with scripts/startup/Start-Dev.ps1")?,
    )
}

fn main() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![bootstrap])
        .setup(|app| {
            tauri::WebviewWindowBuilder::from_config(app, &app.config().app.windows[0])?
                .on_navigation(|url| {
                    matches!(
                        url.as_str(),
                        "http://127.0.0.1:1420/" | "http://tauri.localhost/" | "tauri://localhost/"
                    )
                })
                .build()?;
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("Could not start Guildhall desktop");
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn bootstrap_rejects_remote_urls_and_missing_tokens() {
        assert!(validate("https://example.com/".into(), "a".repeat(64)).is_err());
        assert!(validate("http://127.0.0.1:43120/".into(), "".into()).is_err());
        assert!(validate("http://127.0.0.1:43120/".into(), "a".repeat(64)).is_ok());
    }
}
