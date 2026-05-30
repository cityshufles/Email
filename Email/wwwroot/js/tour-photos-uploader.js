// 2026-05-31 - Desktop /tour-photos direct-HTTP uploader.
// Bypasses the Blazor SignalR circuit (root cause of upload timeouts on iPhone/large files)
// by POSTing each file straight to the /tour-photos/upload-alt controller via XHR.
// This module fully owns its container's DOM subtree so Blazor's diffing never fights it.
(() => {
    const UPLOAD_TIMEOUT_MS = 10 * 60 * 1000; // 10 min hard cap per file
    const STALL_TIMEOUT_MS = 120 * 1000;      // abort if no progress for 2 min
    const STALL_CHECK_MS = 5 * 1000;
    const BETWEEN_FILES_DELAY_MS = 150;
    const MAX_FILES = 12;                      // per upload session (per requirement)
    const UPLOAD_URL = "/tour-photos/upload-alt";
    const SYNC_URL = "/tour-photos/sync-report";

    const state = {
        host: null,
        dotNet: null,
        tourDate: "",
        tourName: "",
        tourTime: "",
        items: [],
        isUploading: false,
        els: {}
    };

    function wait(ms) { return new Promise(r => setTimeout(r, ms)); }

    function init(containerId, dotNetRef, meta) {
        const host = document.getElementById(containerId);
        if (!host) return;
        // If already initialized for this host, just refresh metadata.
        if (state.host === host && host.dataset.tpuInit === "1") {
            setMeta(meta && meta.tourDate, meta && meta.tourName, meta && meta.tourTime);
            return;
        }
        state.host = host;
        state.dotNet = dotNetRef;
        state.items = [];
        state.isUploading = false;
        if (meta) { state.tourDate = meta.tourDate || ""; state.tourName = meta.tourName || ""; state.tourTime = meta.tourTime || ""; }

        host.innerHTML = `
            <div class="tpu-dropzone" data-role="dropzone">
                <input type="file" multiple accept="image/*" class="tpu-file-input" data-role="input" />
                <div class="tpu-dz-content">
                    <i class="bi bi-cloud-arrow-up fs-1 text-primary"></i>
                    <p class="mt-2 mb-1 fw-semibold">Drag &amp; drop photos here</p>
                    <p class="small text-muted mb-0">or click to browse &middot; up to ${MAX_FILES} photos, full resolution</p>
                </div>
            </div>
            <div class="tpu-bar d-none" data-role="bar">
                <span class="fw-semibold small" data-role="count"></span>
                <div class="d-flex gap-2">
                    <button type="button" class="btn btn-sm btn-outline-secondary" data-role="clear">Clear All</button>
                    <button type="button" class="btn btn-sm btn-primary" data-role="upload">
                        <i class="bi bi-upload me-1"></i>Upload All
                    </button>
                </div>
            </div>
            <div class="tpu-grid" data-role="grid"></div>
            <div class="small mt-2" data-role="status"></div>`;

        const q = sel => host.querySelector(`[data-role="${sel}"]`);
        state.els = {
            dropzone: q("dropzone"), input: q("input"), bar: q("bar"), count: q("count"),
            clear: q("clear"), upload: q("upload"), grid: q("grid"), status: q("status")
        };

        state.els.input.addEventListener("change", onFilesPicked);
        state.els.upload.addEventListener("click", uploadAll);
        state.els.clear.addEventListener("click", clearAll);
        state.els.dropzone.addEventListener("dragover", e => { e.preventDefault(); state.els.dropzone.classList.add("tpu-active"); });
        state.els.dropzone.addEventListener("dragleave", () => state.els.dropzone.classList.remove("tpu-active"));
        state.els.dropzone.addEventListener("drop", e => {
            e.preventDefault();
            state.els.dropzone.classList.remove("tpu-active");
            addFiles(e.dataTransfer && e.dataTransfer.files);
        });

        host.dataset.tpuInit = "1";
        render();
    }

    function setMeta(date, name, time) {
        if (typeof date === "string") state.tourDate = date;
        if (typeof name === "string") state.tourName = name;
        if (typeof time === "string") state.tourTime = time;
    }

    function onFilesPicked(e) {
        addFiles(e.target.files);
        // reset native input so picking the same file again re-fires change
        try { e.target.value = ""; } catch { /* no-op */ }
    }

    function addFiles(fileList) {
        if (state.isUploading || !fileList) return;
        const incoming = Array.from(fileList).filter(f => f.type && f.type.startsWith("image/"));
        for (const file of incoming) {
            if (state.items.length >= MAX_FILES) {
                setStatus(`Maximum ${MAX_FILES} photos per upload. Extra files were skipped.`, "error");
                break;
            }
            if (state.items.some(i => i.name === file.name && i.size === file.size)) continue;
            state.items.push({
                id: `${state.items.length}-${file.name}-${file.size}`,
                name: file.name, size: file.size, blob: file,
                previewUrl: URL.createObjectURL(file), progress: 0, status: "pending", error: ""
            });
        }
        render();
    }

    function clearAll() {
        if (state.isUploading) return;
        state.items.forEach(i => i.previewUrl && URL.revokeObjectURL(i.previewUrl));
        state.items = [];
        setStatus("", "idle");
        render();
    }

    function removeItem(id) {
        if (state.isUploading) return;
        const idx = state.items.findIndex(i => i.id === id);
        if (idx === -1) return;
        if (state.items[idx].previewUrl) URL.revokeObjectURL(state.items[idx].previewUrl);
        state.items.splice(idx, 1);
        render();
    }

    function fmtSize(bytes) {
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1048576) return `${(bytes / 1024).toFixed(1)} KB`;
        return `${(bytes / 1048576).toFixed(1)} MB`;
    }

    function setStatus(msg, kind) {
        const s = state.els.status;
        if (!s) return;
        s.textContent = msg || "";
        s.classList.remove("text-muted", "text-success", "text-danger");
        s.classList.add(kind === "done" ? "text-success" : kind === "error" ? "text-danger" : "text-muted");
    }

    function render() {
        const { grid, bar, count, upload } = state.els;
        if (!grid) return;

        bar.classList.toggle("d-none", state.items.length === 0);
        count.textContent = `${state.items.length} of ${MAX_FILES} photo(s) selected`;
        upload.disabled = state.isUploading || state.items.length === 0;

        if (state.items.length === 0) { grid.innerHTML = ""; return; }

        grid.innerHTML = state.items.map(i => {
            const showBar = i.status === "uploading" || i.status === "done" || i.status === "error";
            const barClass = i.status === "error" ? "bg-danger" : i.status === "done" ? "bg-success" : "bg-primary";
            const badge = i.status === "done"
                ? `<span class="badge text-bg-success"><i class="bi bi-check"></i></span>`
                : i.status === "error"
                    ? `<span class="badge text-bg-danger" title="${i.error || "Failed"}"><i class="bi bi-x"></i></span>`
                    : i.status === "uploading"
                        ? `<span class="small text-muted">${i.progress}%</span>`
                        : `<button type="button" class="btn btn-sm btn-outline-danger p-0 px-1" data-remove="${i.id}" title="Remove"><i class="bi bi-x"></i></button>`;
            return `
                <div class="tpu-item">
                    <img class="tpu-thumb" src="${i.previewUrl}" alt="" />
                    <div class="tpu-meta">
                        <div class="small text-truncate fw-semibold" title="${i.name}">${i.name}</div>
                        <div class="small text-muted">${fmtSize(i.size)}</div>
                        ${showBar ? `<div class="progress mt-1" style="height:4px;"><div class="progress-bar ${barClass}" style="width:${i.progress}%"></div></div>` : ""}
                    </div>
                    <div class="tpu-action">${badge}</div>
                </div>`;
        }).join("");

        grid.querySelectorAll("[data-remove]").forEach(btn =>
            btn.addEventListener("click", () => removeItem(btn.getAttribute("data-remove"))));
    }

    function uploadFile(item) {
        return new Promise((resolve, reject) => {
            const xhr = new XMLHttpRequest();
            let lastProgressAt = Date.now();
            let stallTimer = null, settled = false;

            const cleanup = () => {
                if (stallTimer) { clearInterval(stallTimer); stallTimer = null; }
                xhr.upload.onprogress = xhr.onload = xhr.onerror = xhr.ontimeout = xhr.onabort = null;
            };
            const ok = () => { if (settled) return; settled = true; cleanup(); resolve(); };
            const err = (m) => { if (settled) return; settled = true; cleanup(); reject(new Error(m)); };

            xhr.open("POST", UPLOAD_URL, true);
            xhr.timeout = UPLOAD_TIMEOUT_MS;
            xhr.upload.onprogress = e => {
                lastProgressAt = Date.now();
                if (e.lengthComputable) { item.progress = Math.round((e.loaded / e.total) * 100); render(); }
            };
            xhr.onload = () => (xhr.status >= 200 && xhr.status < 300) ? ok() : err(`HTTP ${xhr.status}`);
            xhr.onerror = () => err("Network error");
            xhr.ontimeout = () => err("Upload timed out");
            xhr.onabort = () => err("Upload aborted");
            stallTimer = setInterval(() => {
                if (Date.now() - lastProgressAt >= STALL_TIMEOUT_MS) { err("Upload stalled"); try { xhr.abort(); } catch { /* no-op */ } }
            }, STALL_CHECK_MS);

            const data = new FormData();
            data.append("tourDate", state.tourDate);
            data.append("tourName", state.tourName);
            data.append("tourTime", state.tourTime);
            data.append("files", item.blob, item.name || "photo.jpg");
            xhr.send(data);
        });
    }

    async function syncReport() {
        const resp = await fetch(SYNC_URL, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ tourDate: state.tourDate, tourName: state.tourName, tourTime: state.tourTime })
        });
        if (!resp.ok) {
            let msg = `HTTP ${resp.status}`;
            try { const j = await resp.json(); if (j && j.error) msg = j.error; } catch { /* no-op */ }
            throw new Error(msg);
        }
    }

    async function uploadAll() {
        if (state.isUploading) return;
        if (!state.tourTime || !state.tourTime.trim()) { setStatus("Tour time is required. Tour time must match actual tour time.", "error"); return; }
        const pending = state.items.filter(i => i.status !== "done");
        if (pending.length === 0) { setStatus("No files selected.", "error"); return; }

        state.isUploading = true;
        state.els.input.disabled = true;
        setStatus(`Uploading ${pending.length} photo(s)...`, "idle");
        render();

        let saved = 0, failed = 0;
        for (const item of pending) {
            item.status = "uploading"; item.progress = 0; render();
            try { await uploadFile(item); item.status = "done"; item.progress = 100; saved++; }
            catch (e) { item.status = "error"; item.error = e.message || "Upload failed"; failed++; }
            render();
            if (BETWEEN_FILES_DELAY_MS) await wait(BETWEEN_FILES_DELAY_MS);
        }

        let syncOk = true;
        if (saved > 0) { try { await syncReport(); } catch { syncOk = false; } }

        state.isUploading = false;
        state.els.input.disabled = false;
        render();

        if (failed === 0 && syncOk) setStatus(`Upload complete. ${saved} photo(s) saved.`, "done");
        else if (failed === 0) setStatus(`Uploaded ${saved}, but report sync failed. Use Refresh on the gallery.`, "error");
        else setStatus(`Done. Saved ${saved}, failed ${failed}.`, "error");

        if (state.dotNet) {
            try { await state.dotNet.invokeMethodAsync("OnUploadComplete", saved, failed); } catch { /* no-op */ }
        }
    }

    function dispose() {
        state.items.forEach(i => i.previewUrl && URL.revokeObjectURL(i.previewUrl));
        state.items = [];
        if (state.host) delete state.host.dataset.tpuInit;
        state.host = null; state.dotNet = null; state.els = {};
    }

    window.tourPhotosUploader = { init, setMeta, uploadAll, dispose };
})();
