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

#[tauri::command]
async fn pick_workspace_folder() -> Result<Option<String>, String> {
    #[cfg(target_os = "windows")]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x08000000;

        tauri::async_runtime::spawn_blocking(|| {
            let script = concat!(
                "$code = @'\n",
                "using System;\n",
                "using System.Runtime.InteropServices;\n",
                "public static class ModernFolderPicker {\n",
                "    [DllImport(\"user32.dll\")] private static extern IntPtr GetForegroundWindow();\n",
                "    [ComImport, Guid(\"DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7\")] private class FileOpenDialog {}\n",
                "    [ComImport, Guid(\"42f85136-db7e-439c-85f1-e4075d135fc8\"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]\n",
                "    private interface IFileDialog {\n",
                "        [PreserveSig] int Show(IntPtr hwndOwner);\n",
                "        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);\n",
                "        void SetFileTypeIndex(uint iFileType);\n",
                "        void GetFileTypeIndex(out uint piFileType);\n",
                "        void Advise(IntPtr pfde, out uint pdwCookie);\n",
                "        void Unadvise(uint dwCookie);\n",
                "        void SetOptions(uint fos);\n",
                "        void GetOptions(out uint pfos);\n",
                "        void SetDefaultFolder(IShellItem psi);\n",
                "        void SetFolder(IShellItem psi);\n",
                "        void GetFolder(out IShellItem ppsi);\n",
                "        void GetCurrentSelection(out IShellItem ppsi);\n",
                "        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);\n",
                "        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);\n",
                "        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);\n",
                "        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);\n",
                "        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);\n",
                "        void GetResult(out IShellItem ppsi);\n",
                "        void AddPlace(IShellItem psi, int fdap);\n",
                "        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);\n",
                "        void Close(int hr);\n",
                "        void SetClientGuid(ref Guid guid);\n",
                "        void ClearClientData();\n",
                "        void SetFilter(IntPtr pFilter);\n",
                "    }\n",
                "    [ComImport, Guid(\"43826D1E-E718-42EE-BC55-A1E261C37BFE\"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]\n",
                "    private interface IShellItem {\n",
                "        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);\n",
                "        void GetParent(out IShellItem ppsi);\n",
                "        void GetDisplayName(uint sigdnName, out IntPtr ppszName);\n",
                "        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);\n",
                "        void Compare(IShellItem psi, uint hint, out int piOrder);\n",
                "    }\n",
                "    public static string Pick() {\n",
                "        var dlg = (IFileDialog)new FileOpenDialog();\n",
                "        try {\n",
                "            uint opts;\n",
                "            dlg.GetOptions(out opts);\n",
                "            dlg.SetOptions(opts | 0x20u | 0x40u | 0x08u);\n",
                "            dlg.SetTitle(\"Select a local project folder for Guildhall\");\n",
                "            int hr = dlg.Show(GetForegroundWindow());\n",
                "            if (hr != 0) return null;\n",
                "            IShellItem item;\n",
                "            dlg.GetResult(out item);\n",
                "            try {\n",
                "                IntPtr psz;\n",
                "                item.GetDisplayName(0x80058000u, out psz);\n",
                "                try { return Marshal.PtrToStringUni(psz); }\n",
                "                finally { Marshal.FreeCoTaskMem(psz); }\n",
                "            } finally { Marshal.ReleaseComObject(item); }\n",
                "        } finally { Marshal.ReleaseComObject(dlg); }\n",
                "    }\n",
                "}\n",
                "'@\n",
                "Add-Type -TypeDefinition $code\n",
                "$path = [ModernFolderPicker]::Pick()\n",
                "if ($path) { [Console]::Out.Write($path) }"
            );
            let output = std::process::Command::new("powershell")
                .args(["-NoProfile", "-STA", "-WindowStyle", "Hidden", "-Command", script])
                .creation_flags(CREATE_NO_WINDOW)
                .output()
                .map_err(|_| "Could not open folder picker".to_string())?;
            if !output.status.success() {
                return Err("Folder picker failed".to_string());
            }
            let selected = String::from_utf8_lossy(&output.stdout).trim().to_string();
            Ok(if selected.is_empty() { None } else { Some(selected) })
        })
        .await
        .map_err(|_| "Folder picker task failed".to_string())?
    }
    #[cfg(not(target_os = "windows"))]
    {
        Ok(None)
    }
}

fn main() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![
            bootstrap,
            open_external_url,
            pick_workspace_folder
        ])
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
