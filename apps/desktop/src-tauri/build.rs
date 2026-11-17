fn main() {
    tauri_build::try_build(
        tauri_build::Attributes::new()
            .app_manifest(tauri_build::AppManifest::new().commands(&[
                "bootstrap",
                "open_external_url",
                "pick_workspace_folder",
            ])),
    )
    .expect("Tauri build failed");
}
