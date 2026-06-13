// 2026-06-04 - Resources direct-HTTP uploader.
// Replaces the old in-component HttpClient path that cast IJSRuntime to IServiceProvider
// (invalid in Blazor Server) and pushed large files through the SignalR circuit.
// This posts the file straight to /resources/upload via XHR; the browser sends the auth
// cookie automatically (same-origin), so no service resolution or cookie juggling is needed.
(() => {
    window.resourcesUploader = {
        // Reads the chosen file from the given <input type=file> id and uploads it.
        // Returns { ok: bool, error: string }.
        upload: function (inputId, description, tourIdsCsv) {
            return new Promise((resolve) => {
                const input = document.getElementById(inputId);
                if (!input || !input.files || input.files.length === 0) {
                    resolve({ ok: false, error: "No file selected." });
                    return;
                }
                const file = input.files[0];
                const fd = new FormData();
                fd.append("file", file, file.name);
                if (description) fd.append("description", description);
                if (tourIdsCsv) fd.append("tourIds", tourIdsCsv);

                const xhr = new XMLHttpRequest();
                xhr.open("POST", "/resources/upload", true);
                xhr.timeout = 10 * 60 * 1000;
                xhr.onload = () => {
                    if (xhr.status >= 200 && xhr.status < 300) {
                        resolve({ ok: true, error: "" });
                    } else {
                        const text = (xhr.responseText || "").slice(0, 300);
                        resolve({ ok: false, error: `HTTP ${xhr.status} ${text}` });
                    }
                };
                xhr.onerror = () => resolve({ ok: false, error: "Network error" });
                xhr.ontimeout = () => resolve({ ok: false, error: "Upload timed out" });
                try { xhr.send(fd); } catch (e) { resolve({ ok: false, error: String(e) }); }
            });
        },
        // Clears the native file input after a successful upload.
        clear: function (inputId) {
            const input = document.getElementById(inputId);
            if (input) { try { input.value = ""; } catch (e) { /* no-op */ } }
        }
    };
})();
