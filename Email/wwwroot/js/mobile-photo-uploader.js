(() => {
    const state = {
        tourDate: "",
        tourName: "",
        tourTime: "",
        returnUrl: "/guide-report",
        isUploading: false,
        items: [],
        photos: []
    };

    const refs = {};
    const UPLOAD_TIMEOUT_MS = 10 * 60 * 1000;
    const STALL_TIMEOUT_MS = 120 * 1000;
    const STALL_CHECK_MS = 5 * 1000;
    const BETWEEN_FILES_DELAY_MS = 150;

    function wait(ms) {
        return new Promise(resolve => setTimeout(resolve, ms));
    }

    function init() {
        const qp = new URLSearchParams(window.location.search);
        state.tourDate = (qp.get("tourDate") || "").trim();
        state.tourName = (qp.get("tourName") || "").trim();
        state.tourTime = (qp.get("tourTime") || "").trim();
        state.returnUrl = buildReturnUrl((qp.get("returnUrl") || "").trim());

        refs.fileInput = document.getElementById("fileInput");
        refs.uploadButton = document.getElementById("uploadButton");
        refs.refreshButton = document.getElementById("refreshButton");
        refs.clearButton = document.getElementById("clearButton");
        refs.queue = document.getElementById("queue");
        refs.status = document.getElementById("status");
        refs.gallery = document.getElementById("gallery");
        refs.returnLink = document.getElementById("returnLink");

        if (refs.returnLink) {
            refs.returnLink.href = state.returnUrl;
        }

        if (refs.fileInput) {
            refs.fileInput.addEventListener("change", onFilesSelected);
        }
        if (refs.uploadButton) {
            refs.uploadButton.addEventListener("click", uploadAll);
        }
        if (refs.refreshButton) {
            refs.refreshButton.addEventListener("click", loadPhotos);
        }
        if (refs.clearButton) {
            refs.clearButton.addEventListener("click", clearQueue);
        }
        if (refs.queue) {
            refs.queue.addEventListener("click", onQueueAction);
        }

        if (!state.tourDate || !state.tourName) {
            setStatus("Missing upload context.", "error");
            disableControls(true, false);
            return;
        }

        setStatus("Ready.", "info");
        loadPhotos();
    }

    function buildReturnUrl(explicitReturnUrl) {
        if (explicitReturnUrl) {
            return explicitReturnUrl;
        }

        const parts = [];
        if (state.tourDate) {
            parts.push(`date=${encodeURIComponent(state.tourDate)}`);
        }
        if (state.tourName) {
            parts.push(`tourName=${encodeURIComponent(state.tourName)}`);
        }
        if (state.tourTime) {
            parts.push(`tourTime=${encodeURIComponent(state.tourTime)}`);
        }

        if (!parts.length) {
            return "/guide-report";
        }

        return `/guide-report?${parts.join("&")}`;
    }

    function onFilesSelected(event) {
        const files = Array.from(event.target.files || []);
        if (!files.length) {
            return;
        }

        for (const file of files) {
            if (!file.type.startsWith("image/")) {
                continue;
            }

            state.items.push({
                id: `${Date.now()}-${Math.random().toString(16).slice(2)}`,
                name: file.name,
                blob: file,
                sizeBytes: file.size,
                previewUrl: URL.createObjectURL(file),
                progress: 0,
                status: "pending",
                error: "",
                isRotating: false
            });
        }

        renderQueue();
        refs.fileInput.value = "";
    }

    function clearQueue() {
        if (state.isUploading) {
            return;
        }

        cleanupItems(state.items);
        state.items = [];
        renderQueue();
        setStatus("Cleared.", "info");
    }

    function onQueueAction(event) {
        const button = event.target.closest("button[data-action]");
        if (!button) {
            return;
        }

        if (state.isUploading) {
            return;
        }

        const card = button.closest("[data-id]");
        if (!card) {
            return;
        }

        const itemId = card.getAttribute("data-id");
        const action = button.getAttribute("data-action");
        const item = state.items.find(x => x.id === itemId);
        if (!item) {
            return;
        }

        if (action === "remove") {
            removeItem(item);
            return;
        }
        if (action === "rotate-left") {
            rotateItem(item, -90);
            return;
        }
        if (action === "rotate-right") {
            rotateItem(item, 90);
        }
    }

    function removeItem(item) {
        const index = state.items.indexOf(item);
        if (index < 0) {
            return;
        }

        if (item.previewUrl) {
            URL.revokeObjectURL(item.previewUrl);
        }

        state.items.splice(index, 1);
        renderQueue();
    }

    function rotateItem(item, degrees) {
        if (item.isRotating) {
            return;
        }

        item.isRotating = true;
        renderQueue();

        const img = new Image();
        img.onload = () => {
            const canvas = document.createElement("canvas");
            const ctx = canvas.getContext("2d");
            if (!ctx) {
                item.isRotating = false;
                renderQueue();
                return;
            }

            const width = img.width;
            const height = img.height;
            const swap = Math.abs(degrees) === 90 || Math.abs(degrees) === 270;
            canvas.width = swap ? height : width;
            canvas.height = swap ? width : height;

            ctx.translate(canvas.width / 2, canvas.height / 2);
            ctx.rotate((degrees * Math.PI) / 180);
            ctx.drawImage(img, -width / 2, -height / 2);

            if (canvas.toBlob) {
                canvas.toBlob(blob => applyRotatedBlob(item, blob), "image/jpeg", 0.92);
            } else {
                const dataUrl = canvas.toDataURL("image/jpeg", 0.92);
                applyRotatedBlob(item, dataUrlToBlob(dataUrl));
            }
        };
        img.onerror = () => {
            item.isRotating = false;
            renderQueue();
        };
        img.src = item.previewUrl;
    }

    function applyRotatedBlob(item, blob) {
        if (!blob) {
            item.isRotating = false;
            renderQueue();
            return;
        }

        if (item.previewUrl) {
            URL.revokeObjectURL(item.previewUrl);
        }

        item.blob = blob;
        item.previewUrl = URL.createObjectURL(blob);
        item.sizeBytes = blob.size || item.sizeBytes;
        item.isRotating = false;
        item.status = "pending";
        item.progress = 0;
        item.error = "";
        renderQueue();
    }

    function dataUrlToBlob(dataUrl) {
        const parts = dataUrl.split(",");
        if (parts.length < 2) {
            return null;
        }
        const mimeMatch = parts[0].match(/data:(.*?);base64/);
        const mime = mimeMatch ? mimeMatch[1] : "image/jpeg";
        const binary = atob(parts[1]);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return new Blob([bytes], { type: mime });
    }

    function renderQueue() {
        if (!refs.queue) {
            return;
        }

        if (!state.items.length) {
            refs.queue.innerHTML = "";
            return;
        }

        const html = state.items.map(item => {
            const progress = Math.max(0, Math.min(100, item.progress || 0));
            const progressClass = item.status === "error" ? "bg-danger" : "bg-success";
            const statusText = item.status === "done"
                ? "Uploaded"
                : item.status === "error"
                    ? escapeHtml(item.error || "Failed")
                    : item.status === "uploading"
                        ? `${progress}%`
                        : "Ready";

            const disabledAttr = state.isUploading || item.isRotating ? "disabled" : "";
            const rotatingOverlay = item.isRotating
                ? `<div class="position-absolute top-0 start-0 w-100 h-100 d-flex align-items-center justify-content-center" style="background: rgba(0,0,0,0.35); border-radius: 0.375rem;"><div class="spinner-border text-light"></div></div>`
                : "";

            return `
                <div class="border rounded p-2 mb-2" data-id="${item.id}">
                    <div class="position-relative mb-2">
                        <img src="${escapeAttribute(item.previewUrl)}" class="queue-preview rounded border" alt="Selected photo" />
                        ${rotatingOverlay}
                    </div>
                    <div class="d-flex justify-content-between align-items-center gap-2 mb-2">
                        <div class="small text-break fw-semibold">${escapeHtml(item.name)}</div>
                        <div class="small text-muted">${formatSize(item.sizeBytes)}</div>
                    </div>
                    <div class="d-flex gap-2 mb-2">
                        <button type="button" class="btn btn-sm btn-outline-secondary" data-action="rotate-left" ${disabledAttr} title="Rotate left">
                            <i class="bi bi-arrow-counterclockwise"></i>
                        </button>
                        <button type="button" class="btn btn-sm btn-outline-secondary" data-action="rotate-right" ${disabledAttr} title="Rotate right">
                            <i class="bi bi-arrow-clockwise"></i>
                        </button>
                        <button type="button" class="btn btn-sm btn-outline-danger ms-auto" data-action="remove" ${disabledAttr} title="Remove">
                            <i class="bi bi-trash"></i>
                        </button>
                    </div>
                    <div class="progress" style="height: 8px;">
                        <div class="progress-bar ${progressClass}" role="progressbar" style="width: ${progress}%;"></div>
                    </div>
                    <div class="small text-muted mt-1">${statusText}</div>
                </div>
            `;
        }).join("");

        refs.queue.innerHTML = html;
    }

    async function uploadAll() {
        if (state.isUploading) {
            return;
        }
        if (!state.items.length) {
            setStatus("No files selected.", "error");
            return;
        }

        state.isUploading = true;
        disableControls(true, true);
        setStatus("Uploading...", "info");

        let successCount = 0;
        let failCount = 0;

        for (const item of state.items) {
            if (item.status === "done") {
                continue;
            }

            item.status = "uploading";
            item.progress = 0;
            item.error = "";
            renderQueue();

            try {
                await uploadItem(item);
                item.status = "done";
                item.progress = 100;
                successCount++;
            } catch (err) {
                item.status = "error";
                item.error = err && err.message ? err.message : "Upload failed";
                failCount++;
            }

            renderQueue();
            if (BETWEEN_FILES_DELAY_MS > 0) {
                await wait(BETWEEN_FILES_DELAY_MS);
            }
        }

        try {
            if (successCount > 0) {
                setStatus("Syncing...", "info");
                const syncPayload = await syncReport();
                await loadPhotos();

                if (failCount > 0) {
                    setStatus(`Uploaded ${successCount}, failed ${failCount}, synced ${syncPayload.photoCount || 0}.`, "warn");
                } else {
                    setStatus(`Uploaded and synced ${syncPayload.photoCount || successCount}.`, "success");
                }
            } else {
                setStatus("No files uploaded.", "error");
            }
        } catch (err) {
            const syncError = err && err.message ? err.message : "Sync failed";
            setStatus(`Upload done, sync failed: ${syncError}`, "error");
        } finally {
            state.isUploading = false;
            disableControls(false, false);
            renderQueue();
        }
    }

    function disableControls(isDisabled, isBusy) {
        if (refs.uploadButton) {
            refs.uploadButton.disabled = isDisabled;
            refs.uploadButton.innerHTML = isBusy
                ? "<span class=\"spinner-border spinner-border-sm me-1\"></span>Uploading"
                : "<i class=\"bi bi-cloud-upload me-1\"></i>Upload";
        }
        if (refs.refreshButton) {
            refs.refreshButton.disabled = isDisabled;
        }
        if (refs.clearButton) {
            refs.clearButton.disabled = isDisabled;
        }
        if (refs.fileInput) {
            refs.fileInput.disabled = isDisabled;
        }
    }

    function uploadItem(item) {
        return new Promise((resolve, reject) => {
            const xhr = new XMLHttpRequest();
            const startedAt = Date.now();
            let lastProgressAt = startedAt;
            let stallTimer = null;
            let settled = false;

            const cleanup = () => {
                if (stallTimer) {
                    clearInterval(stallTimer);
                    stallTimer = null;
                }
                xhr.upload.onprogress = null;
                xhr.onload = null;
                xhr.onerror = null;
                xhr.ontimeout = null;
                xhr.onabort = null;
            };

            const finishOk = () => {
                if (settled) {
                    return;
                }
                settled = true;
                cleanup();
                resolve();
            };

            const finishErr = (err) => {
                if (settled) {
                    return;
                }
                settled = true;
                cleanup();
                reject(err);
            };

            xhr.open("POST", "/tour-photos/upload-alt", true);
            xhr.timeout = UPLOAD_TIMEOUT_MS;

            xhr.upload.onprogress = event => {
                lastProgressAt = Date.now();
                if (event.lengthComputable) {
                    item.progress = Math.round((event.loaded / event.total) * 100);
                    renderQueue();
                }
            };

            xhr.onload = () => {
                if (xhr.status >= 200 && xhr.status < 300) {
                    finishOk();
                    return;
                }
                finishErr(new Error(`HTTP ${xhr.status}`));
            };

            xhr.onerror = () => finishErr(new Error("Network error"));
            xhr.ontimeout = () => finishErr(new Error("Upload timed out"));
            xhr.onabort = () => finishErr(new Error("Upload aborted"));

            stallTimer = setInterval(() => {
                const idleMs = Date.now() - lastProgressAt;
                if (idleMs >= STALL_TIMEOUT_MS) {
                    finishErr(new Error("Upload stalled"));
                    try {
                        xhr.abort();
                    } catch {
                        // no-op
                    }
                }
            }, STALL_CHECK_MS);

            const data = new FormData();
            data.append("tourDate", state.tourDate);
            data.append("tourName", state.tourName);
            data.append("tourTime", state.tourTime);

            const uploadBlob = item.blob instanceof Blob ? item.blob : new Blob([item.blob], { type: "image/jpeg" });
            const uploadFileName = item.name || `photo-${item.id}.jpg`;
            data.append("files", uploadBlob, uploadFileName);

            xhr.send(data);
        });
    }

    async function syncReport() {
        const response = await fetch("/tour-photos/sync-report", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                tourDate: state.tourDate,
                tourName: state.tourName,
                tourTime: state.tourTime
            })
        });

        const payload = await tryReadJson(response);
        if (!response.ok) {
            throw new Error(payload && payload.error ? payload.error : `HTTP ${response.status}`);
        }

        return payload || {};
    }

    async function loadPhotos() {
        if (!state.tourDate || !state.tourName) {
            return;
        }

        if (refs.refreshButton) {
            refs.refreshButton.disabled = true;
        }
        try {
            const url = `/tour-photos/list?tourDate=${encodeURIComponent(state.tourDate)}&tourName=${encodeURIComponent(state.tourName)}&tourTime=${encodeURIComponent(state.tourTime)}`;
            const response = await fetch(url, { method: "GET" });
            const payload = await tryReadJson(response);

            if (!response.ok) {
                throw new Error(payload && payload.error ? payload.error : `HTTP ${response.status}`);
            }

            state.photos = Array.isArray(payload && payload.photos) ? payload.photos : [];
            renderGallery();
        } catch (err) {
            setStatus(`Failed to load photos: ${err && err.message ? err.message : "Unknown error"}`, "warn");
        } finally {
            if (!state.isUploading && refs.refreshButton) {
                refs.refreshButton.disabled = false;
            }
        }
    }

    function renderGallery() {
        if (!refs.gallery) {
            return;
        }

        if (!state.photos.length) {
            refs.gallery.innerHTML = "";
            return;
        }

        const html = state.photos.map(path => `
            <div class="col-6 col-md-4 col-lg-3">
                <a href="${escapeAttribute(path)}" target="_blank" rel="noopener">
                    <img src="${escapeAttribute(path)}" class="img-fluid rounded border" alt="Photo" style="height: 120px; width: 100%; object-fit: cover;" loading="lazy" />
                </a>
            </div>
        `).join("");

        refs.gallery.innerHTML = `<div class="row g-2">${html}</div>`;
    }

    function setStatus(message, type) {
        if (!refs.status) {
            return;
        }

        refs.status.classList.remove("alert-success", "alert-danger", "alert-info", "alert-warning");
        refs.status.classList.add("alert");
        refs.status.textContent = message;

        if (type === "success") {
            refs.status.classList.add("alert-success");
        } else if (type === "error") {
            refs.status.classList.add("alert-danger");
        } else if (type === "warn") {
            refs.status.classList.add("alert-warning");
        } else {
            refs.status.classList.add("alert-info");
        }
    }

    async function tryReadJson(response) {
        let text = "";
        try {
            text = await response.text();
        } catch {
            return null;
        }

        if (!text) {
            return null;
        }

        try {
            return JSON.parse(text);
        } catch {
            return null;
        }
    }

    function cleanupItems(items) {
        for (const item of items) {
            if (item.previewUrl) {
                URL.revokeObjectURL(item.previewUrl);
            }
        }
    }

    function formatSize(bytes) {
        if (!Number.isFinite(bytes) || bytes <= 0) {
            return "0 B";
        }
        if (bytes < 1024) {
            return `${bytes} B`;
        }
        if (bytes < 1024 * 1024) {
            return `${(bytes / 1024).toFixed(1)} KB`;
        }
        return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
    }

    function escapeHtml(value) {
        return String(value)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function escapeAttribute(value) {
        return escapeHtml(value).replace(/`/g, "&#96;");
    }

    window.addEventListener("beforeunload", () => cleanupItems(state.items));
    document.addEventListener("DOMContentLoaded", init);
})();
