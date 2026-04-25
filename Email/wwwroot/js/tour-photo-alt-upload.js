(() => {
    const state = {
        items: [],
        input: null,
        list: null,
        status: null,
        button: null,
        buttonText: null,
        modal: null,
        closeButton: null,
        inputWrapper: null,
        uploadUrl: "",
        syncUrl: "",
        tourDate: "",
        tourName: "",
        tourTime: "",
        isUploading: false,
        isComplete: false,
        isCompleteSuccess: false,
        syncSucceeded: false,
        syncError: "",
        syncPhotoCount: 0,
        listHandlerBound: false
    };

    const UPLOAD_TIMEOUT_MS = 10 * 60 * 1000;
    const STALL_TIMEOUT_MS = 120 * 1000;
    const STALL_CHECK_MS = 5 * 1000;
    const BETWEEN_FILES_DELAY_MS = 150;

    function wait(ms) {
        return new Promise(resolve => setTimeout(resolve, ms));
    }

    function init(options) {
        state.input = document.getElementById(options.inputId);
        state.list = document.getElementById(options.listId);
        state.status = document.getElementById(options.statusId);
        state.button = document.getElementById(options.buttonId);
        state.buttonText = document.getElementById(options.buttonTextId);
        state.modal = options.modalId ? document.getElementById(options.modalId) : null;
        state.closeButton = options.closeButtonId ? document.getElementById(options.closeButtonId) : null;
        state.inputWrapper = state.input ? state.input.closest(".mb-3") : null;
        state.uploadUrl = options.uploadUrl || "/tour-photos/upload-alt";
        state.syncUrl = options.syncUrl || "/tour-photos/sync-report";
        state.tourDate = options.tourDate || "";
        state.tourName = options.tourName || "";
        state.tourTime = options.tourTime || "";

        reset();

        if (state.input) {
            state.input.value = "";
            state.input.onchange = handleFilesSelected;
        }

        if (state.list) {
            state.list.removeEventListener("click", handleListClick);
            state.list.addEventListener("click", handleListClick);
        }

        if (state.button) {
            state.button.removeEventListener("click", handleButtonClickFallback);
            state.button.addEventListener("click", handleButtonClickFallback);
        }

        if (state.closeButton) {
            state.closeButton.removeEventListener("click", handleCloseClickFallback);
            state.closeButton.addEventListener("click", handleCloseClickFallback);
        }

        render();
    }

    function reset() {
        state.items.forEach(item => {
            if (item.previewUrl) {
                URL.revokeObjectURL(item.previewUrl);
            }
        });
        state.items = [];
        state.isUploading = false;
        state.isComplete = false;
        state.isCompleteSuccess = false;
        state.syncSucceeded = false;
        state.syncError = "";
        state.syncPhotoCount = 0;
        setButtonState("idle");
        updateCompleteUi();
        updateStatus("", "idle");
        showDialogClientSide();
        syncInputFiles();
        render();
    }

    function updateStatus(message, stateName) {
        if (!state.status) return;
        state.status.textContent = message || "";
        state.status.classList.remove("text-muted", "text-success", "text-danger");
        if (stateName === "done") {
            state.status.classList.add("text-success");
        } else if (stateName === "error") {
            state.status.classList.add("text-danger");
        } else {
            state.status.classList.add("text-muted");
        }
    }

    function setButtonState(mode) {
        if (!state.button || !state.buttonText) return;

        state.button.classList.remove("btn-primary", "btn-success", "btn-warning", "btn-outline-warning");

        if (mode === "uploading") {
            state.button.disabled = true;
            state.buttonText.textContent = "Uploading";
            state.button.classList.add("btn-primary");
            return;
        }

        state.button.disabled = false;
        if (mode === "done-success") {
            state.buttonText.textContent = "Done";
            state.button.classList.add("btn-success");
            return;
        }

        if (mode === "done-failed") {
            state.buttonText.textContent = "Done";
            state.button.classList.add("btn-outline-warning");
            return;
        }

        state.buttonText.textContent = "Upload";
        state.button.classList.add("btn-primary");
    }

    function handleButtonClickFallback() {
        if (state.isUploading || !state.isComplete) {
            return;
        }

        closeDialogClientSide();
    }

    function handleCloseClickFallback() {
        closeDialogClientSide();
    }

    function closeDialogClientSide() {
        if (!state.modal) return;
        state.modal.classList.remove("show", "d-block");
        state.modal.classList.add("d-none");
        state.modal.style.display = "none";
        document.body.classList.remove("modal-open");
    }

    function showDialogClientSide() {
        if (!state.modal) return;
        state.modal.classList.remove("d-none");
        state.modal.classList.add("show", "d-block");
        state.modal.style.display = "";
    }

    function updateCompleteUi() {
        if (state.input) {
            state.input.disabled = state.isComplete || state.isUploading;
        }
        if (state.inputWrapper) {
            state.inputWrapper.style.display = state.isComplete ? "none" : "";
        }
        if (state.list) {
            state.list.classList.toggle("is-complete", state.isComplete);
        }
    }

    function handleFilesSelected(event) {
        const files = Array.from(event.target.files || []);
        if (!files.length) {
            render();
            return;
        }

        files.forEach(file => {
            if (!file.type.startsWith("image/")) {
                return;
            }
            const item = {
                id: `${Date.now()}-${Math.random().toString(16).slice(2)}`,
                name: file.name,
                blob: file,
                previewUrl: URL.createObjectURL(file),
                progress: 0,
                status: "pending",
                isRotating: false,
                error: ""
            };
            state.items.push(item);
        });

        syncInputFiles();
        setButtonState("idle");
        updateCompleteUi();
        render();
    }

    function handleListClick(event) {
        const button = event.target.closest("button[data-action]");
        if (!button) return;

        const card = button.closest("[data-id]");
        if (!card) return;

        const id = card.getAttribute("data-id");
        const action = button.getAttribute("data-action");
        const item = state.items.find(x => x.id === id);
        if (!item || state.isUploading) return;

        if (action === "remove") {
            removeItem(item);
        } else if (action === "rotate-left") {
            rotateItem(item, -90);
        } else if (action === "rotate-right") {
            rotateItem(item, 90);
        }
    }

    function removeItem(item) {
        const index = state.items.indexOf(item);
        if (index === -1) return;
        if (item.previewUrl) {
            URL.revokeObjectURL(item.previewUrl);
        }
        state.items.splice(index, 1);
        syncInputFiles();
        render();
    }

    function rotateItem(item, degrees) {
        if (item.isRotating) return;
        item.isRotating = true;
        render();

        const img = new Image();
        img.onload = () => {
            const canvas = document.createElement("canvas");
            const ctx = canvas.getContext("2d");
            if (!ctx) {
                item.isRotating = false;
                render();
                return;
            }

            const rad = degrees * Math.PI / 180;
            const width = img.width;
            const height = img.height;
            const swap = Math.abs(degrees) === 90 || Math.abs(degrees) === 270;
            canvas.width = swap ? height : width;
            canvas.height = swap ? width : height;

            ctx.translate(canvas.width / 2, canvas.height / 2);
            ctx.rotate(rad);
            ctx.drawImage(img, -width / 2, -height / 2);

            if (canvas.toBlob) {
                canvas.toBlob(blob => {
                    if (!blob) {
                        const dataUrl = canvas.toDataURL("image/jpeg", 0.92);
                        const fallbackBlob = dataUrlToBlob(dataUrl);
                        applyRotatedBlob(item, fallbackBlob);
                        return;
                    }
                    applyRotatedBlob(item, blob);
                }, "image/jpeg", 0.92);
            } else {
                const dataUrl = canvas.toDataURL("image/jpeg", 0.92);
                const fallbackBlob = dataUrlToBlob(dataUrl);
                applyRotatedBlob(item, fallbackBlob);
            }
        };
        img.onerror = () => {
            item.isRotating = false;
            render();
        };
        img.src = item.previewUrl;
    }

    function applyRotatedBlob(item, blob) {
        if (!blob) return;
        if (item.previewUrl) {
            URL.revokeObjectURL(item.previewUrl);
        }
        item.blob = blob;
        item.previewUrl = URL.createObjectURL(blob);
        item.isRotating = false;
        syncInputFiles();
        render();
    }

    function dataUrlToBlob(dataUrl) {
        const parts = dataUrl.split(",");
        if (parts.length < 2) {
            return null;
        }
        const mimeMatch = parts[0].match(/data:(.*?);base64/);
        const mime = mimeMatch ? mimeMatch[1] : "image/jpeg";
        const binary = atob(parts[1]);
        const len = binary.length;
        const bytes = new Uint8Array(len);
        for (let i = 0; i < len; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return new Blob([bytes], { type: mime });
    }

    function syncInputFiles() {
        if (!state.input) return;

        try {
            if (!window.DataTransfer) {
                state.input.value = "";
                return;
            }

            const dt = new DataTransfer();
            state.items.forEach(item => {
                let file;
                if (item.blob instanceof File) {
                    file = item.blob;
                } else {
                    file = new File([item.blob], item.name || "photo.jpg", {
                        type: item.blob && item.blob.type ? item.blob.type : "image/jpeg"
                    });
                }
                dt.items.add(file);
            });
            state.input.files = dt.files;
        } catch (err) {
            try {
                state.input.value = "";
            } catch (e) {
                // no-op
            }
        }
    }

    function render() {
        if (!state.list) return;

        if (!state.items.length) {
            state.list.innerHTML = "<div class=\"text-muted\">No files selected.</div>";
            return;
        }

        const itemsHtml = state.items.map(item => {
            const progressVisible = item.status === "uploading" || item.status === "done" || item.status === "error";
            const progressClass = item.status === "error" ? "bg-danger" : "bg-primary";
            const statusText = item.status === "done"
                ? "Uploaded"
                : item.status === "error"
                    ? item.error || "Upload failed"
                    : item.status === "uploading"
                        ? `${item.progress}%`
                        : "Pending";

            const rotatingOverlay = item.isRotating
                ? `
                    <div class="position-absolute top-0 start-0 w-100 h-100 d-flex align-items-center justify-content-center"
                         style="background: rgba(0,0,0,0.35); border-radius: 0.375rem; z-index: 3;">
                        <div class="spinner-border text-light" role="status" aria-label="Rotating"></div>
                    </div>
                `
                : "";

            const controlsClass = state.isComplete ? "d-none" : "";

            return `
                <div class="alt-photo-card border rounded p-2 position-relative mb-3" data-id="${item.id}">
                    <img src="${item.previewUrl}" alt="Selected photo" class="img-fluid rounded" style="width: 100%; height: 150px; object-fit: cover; display: block; image-orientation: from-image;" />
                    <div class="photo-actions position-absolute top-0 start-0 m-1 d-flex gap-1 ${controlsClass}" style="z-index: 2;">
                        <button type="button" class="btn btn-sm btn-light" data-action="rotate-left" title="Rotate left" ${item.isRotating ? "disabled" : ""}>
                            <i class="bi bi-arrow-counterclockwise"></i>
                        </button>
                        <button type="button" class="btn btn-sm btn-light" data-action="rotate-right" title="Rotate right" ${item.isRotating ? "disabled" : ""}>
                            <i class="bi bi-arrow-clockwise"></i>
                        </button>
                    </div>
                    <button type="button" class="btn btn-sm btn-danger position-absolute top-0 end-0 m-1 ${controlsClass}" data-action="remove" title="Remove" ${item.isRotating ? "disabled" : ""}>
                        <i class="bi bi-trash"></i>
                    </button>
                    ${rotatingOverlay}
                    ${progressVisible ? `
                        <div class="mt-2">
                            <div class="progress" style="height: 6px;">
                                <div class="progress-bar ${progressClass}" role="progressbar" style="width: ${item.progress}%;"></div>
                            </div>
                            <div class="small text-muted mt-1">${statusText}</div>
                        </div>
                    ` : ""}
                </div>
            `;
        }).join("");

        const overlayHtml = state.isComplete
            ? `
                <div class="alt-upload-complete-overlay">
                    <div class="alt-upload-complete-icon">
                        <i class="bi bi-check-lg"></i>
                    </div>
                </div>
            `
            : "";

        state.list.innerHTML = `
            <div class="alt-upload-grid">${itemsHtml}</div>
            ${overlayHtml}
        `;
    }

    async function uploadAll() {
        if (state.isUploading) return;
        if (!state.tourTime || !state.tourTime.trim()) {
            updateStatus("Tour time is required. Tour time must match actual tour time.", "error");
            return;
        }
        if (!state.items.length) {
            updateStatus("No files selected.", "error");
            return;
        }

        state.isUploading = true;
        state.isComplete = false;
        state.isCompleteSuccess = false;
        setButtonState("uploading");
        updateCompleteUi();
        updateStatus("Uploading...", "uploading");

        let successCount = 0;
        let failCount = 0;
        const failures = [];

        for (const item of state.items) {
            if (item.status === "done") continue;
            item.status = "uploading";
            item.progress = 0;
            render();

            try {
                await uploadItem(item);
                item.status = "done";
                item.progress = 100;
                successCount++;
            } catch (err) {
                item.status = "error";
                item.error = err && err.message ? err.message : "Upload failed";
                failCount++;

                const durationMs = err && err.durationMs ? err.durationMs : 0;
                const status = err && err.status ? err.status : 0;
                const responseText = trimResponseText(err && err.responseText ? err.responseText : "");
                const fileSizeBytes = item.blob && item.blob.size ? item.blob.size : 0;
                const fileName = item.name || `photo-${item.id}.jpg`;

                failures.push({
                    fileName,
                    fileSizeBytes,
                    durationMs,
                    status,
                    error: item.error,
                    responseText
                });
            }
            render();
            if (BETWEEN_FILES_DELAY_MS > 0) {
                await wait(BETWEEN_FILES_DELAY_MS);
            }
        }

        let syncResult = null;
        if (successCount > 0) {
            updateStatus("Upload complete. Syncing report photos...", "uploading");
            try {
                syncResult = await syncReport();
                state.syncSucceeded = true;
                state.syncError = "";
                state.syncPhotoCount = syncResult && syncResult.photoCount ? syncResult.photoCount : successCount;
            } catch (err) {
                state.syncSucceeded = false;
                state.syncError = err && err.message ? err.message : "Report sync failed";
                state.syncPhotoCount = 0;
            }
        } else {
            state.syncSucceeded = false;
            state.syncError = "No files were uploaded successfully.";
            state.syncPhotoCount = 0;
        }

        state.isUploading = false;
        state.isComplete = true;
        state.isCompleteSuccess = failCount === 0 && state.syncSucceeded;

        if (failCount === 0 && state.syncSucceeded) {
            updateStatus(
                `Upload complete. Synced ${state.syncPhotoCount} photos. Tap Done to close.`,
                "done"
            );
            setButtonState("done-success");
        } else if (failCount > 0 && state.syncSucceeded) {
            updateStatus(
                `Upload complete. Success ${successCount}, failed ${failCount}. Synced saved photos. Tap Done to close.`,
                "error"
            );
            setButtonState("done-failed");
        } else if (failCount === 0) {
            updateStatus(
                `Upload complete, but sync failed. Tap Done to close, then use Sync. (${state.syncError})`,
                "error"
            );
            setButtonState("done-failed");
        } else {
            updateStatus(
                `Upload complete. Success ${successCount}, failed ${failCount}. Sync failed. Tap Done to close, then use Sync.`,
                "error"
            );
            setButtonState("done-failed");
        }
        updateCompleteUi();

        // 2026-02-21/22: Keep upload + report sync fully API/JS driven.
        // This avoids relying on Blazor circuit callbacks that can drop on mobile during long uploads.
        console.log("tourPhotoAlt: upload complete", {
            successCount,
            failCount,
            failures: failures.length,
            syncSucceeded: state.syncSucceeded,
            syncPhotoCount: state.syncPhotoCount
        });
    }

    function uploadItem(item) {
        return new Promise((resolve, reject) => {
            const startedAt = performance.now();
            const xhr = new XMLHttpRequest();
            let lastProgressAt = Date.now();
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
                if (settled) return;
                settled = true;
                cleanup();
                resolve({ durationMs: Math.round(performance.now() - startedAt) });
            };

            const finishErr = (message) => {
                if (settled) return;
                settled = true;
                const durationMs = Math.round(performance.now() - startedAt);
                const status = xhr.status || 0;
                const responseText = xhr.responseText || "";
                cleanup();
                reject({
                    message,
                    status,
                    durationMs,
                    responseText
                });
            };

            xhr.open("POST", state.uploadUrl, true);
            xhr.timeout = UPLOAD_TIMEOUT_MS;

            xhr.upload.onprogress = (event) => {
                lastProgressAt = Date.now();
                if (event.lengthComputable) {
                    item.progress = Math.round((event.loaded / event.total) * 100);
                    render();
                }
            };

            xhr.onload = () => {
                if (xhr.status >= 200 && xhr.status < 300) {
                    finishOk();
                } else {
                    finishErr(`Upload failed (${xhr.status})`);
                }
            };

            xhr.onerror = () => finishErr("Network error");
            xhr.ontimeout = () => finishErr("Upload timed out");
            xhr.onabort = () => finishErr("Upload aborted");

            stallTimer = setInterval(() => {
                const idleMs = Date.now() - lastProgressAt;
                if (idleMs >= STALL_TIMEOUT_MS) {
                    finishErr("Upload stalled");
                    try {
                        xhr.abort();
                    } catch {
                        // no-op
                    }
                }
            }, STALL_CHECK_MS);

            const formData = new FormData();
            formData.append("tourDate", state.tourDate);
            formData.append("tourName", state.tourName);
            formData.append("tourTime", state.tourTime);
            const fileName = item.name || `photo-${item.id}.jpg`;
            const file = item.blob instanceof Blob ? item.blob : new Blob([item.blob], { type: "image/jpeg" });
            formData.append("files", file, fileName);

            xhr.send(formData);
        });
    }

    function trimResponseText(text) {
        if (!text) return "";
        const maxLen = 500;
        if (text.length <= maxLen) return text;
        return text.slice(0, maxLen) + "...";
    }

    async function syncReport() {
        const body = {
            tourDate: state.tourDate,
            tourName: state.tourName,
            tourTime: state.tourTime
        };

        let response;
        try {
            response = await fetch(state.syncUrl, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(body)
            });
        } catch (err) {
            throw new Error("Network error while syncing report.");
        }

        const payload = await tryReadJson(response);
        if (!response.ok) {
            const errorMessage = payload && payload.error
                ? payload.error
                : `Sync failed (${response.status})`;
            throw new Error(errorMessage);
        }

        return payload || {};
    }

    async function tryReadJson(response) {
        let text = "";
        try {
            text = await response.text();
        } catch (err) {
            return null;
        }

        if (!text) {
            return null;
        }

        try {
            return JSON.parse(text);
        } catch (err) {
            return null;
        }
    }

    function getState() {
        const successCount = state.items.filter(item => item.status === "done").length;
        const failCount = state.items.filter(item => item.status === "error").length;
        return {
            isUploading: state.isUploading,
            isComplete: state.isComplete,
            isCompleteSuccess: state.isCompleteSuccess,
            syncSucceeded: state.syncSucceeded,
            syncError: state.syncError,
            syncPhotoCount: state.syncPhotoCount,
            totalCount: state.items.length,
            successCount,
            failCount
        };
    }

    window.tourPhotoAlt = {
        init,
        reset,
        uploadAll,
        getState
    };
})();
