#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct Bootstrap {
    host_url: String,
    token: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    window_title: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    instance_tag: Option<String>,
}

fn is_loopback_http_origin(uri: &url::Url) -> bool {
    uri.scheme() == "http"
        && uri.host_str() == Some("127.0.0.1")
        && uri.port().is_some()
        && uri.path() == "/"
        && uri.query().is_none()
        && uri.fragment().is_none()
        && uri.username().is_empty()
        && uri.password().is_none()
}

fn sanitize_label(value: Option<String>, max_len: usize) -> Option<String> {
    let trimmed = value?.trim().to_string();
    if trimmed.is_empty() || trimmed.len() > max_len || trimmed.chars().any(char::is_control) {
        return None;
    }
    Some(trimmed)
}

fn validate(
    host_url: String,
    token: String,
    window_title: Option<String>,
    instance_tag: Option<String>,
) -> Result<Bootstrap, String> {
    let uri = url::Url::parse(&host_url).map_err(|_| "Invalid host configuration")?;
    if !is_loopback_http_origin(&uri)
        || token.len() < 32
        || token.len() > 128
        || token.chars().any(char::is_whitespace)
    {
        return Err("Invalid session configuration".into());
    }
    Ok(Bootstrap {
        host_url: uri.to_string(),
        token,
        window_title: sanitize_label(window_title, 120),
        instance_tag: sanitize_label(instance_tag, 80),
    })
}

#[tauri::command]
fn bootstrap() -> Result<Bootstrap, String> {
    validate(
        std::env::var("GUILDHALL_HOST_URL").unwrap_or_else(|_| "http://127.0.0.1:43120".into()),
        std::env::var("GUILDHALL_SESSION_TOKEN")
            .map_err(|_| "Start Guildhall with scripts/startup/Start-Dev.ps1")?,
        std::env::var("GUILDHALL_WINDOW_TITLE").ok(),
        std::env::var("GUILDHALL_INSTANCE_TAG").ok(),
    )
}

fn validate_external_url(raw: &str) -> Result<url::Url, String> {
    let parsed = url::Url::parse(raw).map_err(|_| "Invalid URL".to_string())?;
    if !matches!(parsed.scheme(), "http" | "https") || parsed.host_str().is_none() {
        return Err("Only http and https URLs can be opened externally".into());
    }
    Ok(parsed)
}

#[tauri::command]
fn open_external_url(url: String) -> Result<(), String> {
    let parsed = validate_external_url(&url)?;
    #[cfg(target_os = "windows")]
    {
        std::process::Command::new("rundll32")
            .args(["url.dll,FileProtocolHandler", parsed.as_str()])
            .spawn()
            .map_err(|_| "Could not open link in default browser".to_string())?;
    }
    #[cfg(target_os = "macos")]
    {
        std::process::Command::new("open")
            .arg(parsed.as_str())
            .spawn()
            .map_err(|_| "Could not open link in default browser".to_string())?;
    }
    #[cfg(all(unix, not(target_os = "macos")))]
    {
        std::process::Command::new("xdg-open")
            .arg(parsed.as_str())
            .spawn()
            .map_err(|_| "Could not open link in default browser".to_string())?;
    }
    Ok(())
}

fn main() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![bootstrap, open_external_url])
        .setup(|app| {
            let mut window_config = app.config().app.windows[0].clone();
            #[cfg(debug_assertions)]
            if let Ok(frontend) = std::env::var("GUILDHALL_FRONTEND_URL") {
                if let Ok(parsed) = url::Url::parse(&frontend) {
                    if is_loopback_http_origin(&parsed) {
                        window_config.url = tauri::WebviewUrl::External(parsed);
                    }
                }
            }
            let mut builder = tauri::WebviewWindowBuilder::from_config(app, &window_config)?;
            if let Some(title) = sanitize_label(std::env::var("GUILDHALL_WINDOW_TITLE").ok(), 120) {
                builder = builder.title(title);
            }
            builder
                .on_navigation(|url| {
                    matches!(url.as_str(), "http://tauri.localhost/" | "tauri://localhost/")
                        || is_loopback_http_origin(url)
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
        assert!(validate("https://example.com/".into(), "a".repeat(64), None, None).is_err());
        assert!(validate("http://127.0.0.1/".into(), "a".repeat(64), None, None).is_err());
        assert!(validate("http://127.0.0.1:43120/path".into(), "a".repeat(64), None, None).is_err());
        assert!(validate("http://127.0.0.1:43120/".into(), "".into(), None, None).is_err());
        assert!(validate("http://127.0.0.1:43120/".into(), "a".repeat(64), None, None).is_ok());
        let tagged = validate(
            "http://127.0.0.1:43121/".into(),
            "a".repeat(64),
            Some("Guildhall v0.1.0 [feature-branch :43121]".into()),
            Some("v0.1.0 · feature-branch :43121".into()),
        )
        .unwrap();
        assert_eq!(
            tagged.window_title.as_deref(),
            Some("Guildhall v0.1.0 [feature-branch :43121]")
        );
        assert_eq!(
            tagged.instance_tag.as_deref(),
            Some("v0.1.0 · feature-branch :43121")
        );
    }

    #[test]
    fn external_url_allows_only_http_and_https_with_host() {
        assert!(validate_external_url("https://example.com/docs?q=1").is_ok());
        assert!(validate_external_url("http://127.0.0.1:8080/health").is_ok());
        assert!(validate_external_url("file:///C:/Windows/System32/cmd.exe").is_err());
        assert!(validate_external_url("javascript:alert(1)").is_err());
        assert!(validate_external_url("data:text/html,<h1>hi</h1>").is_err());
        assert!(validate_external_url("mailto:user@example.com").is_err());
        assert!(validate_external_url("https://").is_err());
    }
}
