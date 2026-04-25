(() => {
    const STORAGE_KEYS = {
        pendingPhotoSync: "guideLite.pendingPhotoSync"
    };

    const state = {
        selectedDate: "",
        selectedMonth: "",
        dateViewMode: "day",
        selectedGuideFilter: "all",
        guides: [],
        tours: [],
        selectedTour: null,
        activeTab: 0,
        contactDialogBookingId: null,
        report: null,
        pendingPhotoSync: null
    };

    const refs = {};

    document.addEventListener("DOMContentLoaded", init);

    function init() {
        cacheRefs();
        bindEvents();

        const today = new Date();
        state.selectedDate = formatDateInput(today);
        state.selectedMonth = formatMonthInput(today);
        state.dateViewMode = "day";
        state.selectedGuideFilter = "all";
        refs.dateInput.value = state.selectedDate;
        refs.monthInput.value = state.selectedMonth;
        refs.guideFilterSelect.value = state.selectedGuideFilter;

        switchToTab(0);
        loadTours();
        restorePendingPhotoSync();
    }

    function cacheRefs() {
        refs.globalStatus = document.getElementById("globalStatus");
        refs.dateInput = document.getElementById("dateInput");
        refs.monthInput = document.getElementById("monthInput");
        refs.guideFilterSelect = document.getElementById("guideFilterSelect");
        refs.refreshToursButton = document.getElementById("refreshToursButton");
        refs.toursLabel = document.getElementById("toursLabel");
        refs.toursList = document.getElementById("toursList");
        refs.reportHeaderCard = document.getElementById("reportHeaderCard");
        refs.stepTabs = Array.from(document.querySelectorAll("[data-tab-index]"));
        refs.stepPanels = [
            document.getElementById("stepPanel0"),
            document.getElementById("stepPanel1"),
            document.getElementById("stepPanel2"),
            document.getElementById("stepPanel3"),
            document.getElementById("stepPanel4")
        ];
        refs.reportTitle = document.getElementById("reportTitle");
        refs.reportMeta = document.getElementById("reportMeta");
        refs.reportStatusBadge = document.getElementById("reportStatusBadge");
        refs.copyGalleryButton = document.getElementById("copyGalleryButton");
        refs.openGalleryLink = document.getElementById("openGalleryLink");
        refs.notesInput = document.getElementById("notesInput");
        refs.walkersTableBody = document.getElementById("walkersTableBody");
        refs.submitReportButton = document.getElementById("submitReportButton");
        refs.submitReviewTableBody = document.getElementById("submitReviewTableBody");
        refs.submitReviewPhotos = document.getElementById("submitReviewPhotos");
        refs.submitMissingPhoneWarnings = document.getElementById("submitMissingPhoneWarnings");
        refs.photoInput = document.getElementById("photoInput");
        refs.uploadPhotosButton = document.getElementById("uploadPhotosButton");
        refs.photoStatus = document.getElementById("photoStatus");
        refs.photosGrid = document.getElementById("photosGrid");
        refs.retrySyncButton = document.getElementById("retrySyncButton");
        refs.walkupNameInput = document.getElementById("walkupNameInput");
        refs.walkupPhoneInput = document.getElementById("walkupPhoneInput");
        refs.walkupEmailInput = document.getElementById("walkupEmailInput");
        refs.walkupAdultsInput = document.getElementById("walkupAdultsInput");
        refs.walkupChildrenInput = document.getElementById("walkupChildrenInput");
        refs.walkupNotesInput = document.getElementById("walkupNotesInput");
        refs.addWalkupButton = document.getElementById("addWalkupButton");
        refs.goNotesButton = document.getElementById("goNotesButton");
        refs.goSubmitTabButton = document.getElementById("goSubmitTabButton");
        refs.goGalleryButton = document.getElementById("goGalleryButton");
        refs.galleryTabActions = document.getElementById("galleryTabActions");
        refs.galleryTabCopyButton = document.getElementById("galleryTabCopyButton");
        refs.galleryTabOpenLink = document.getElementById("galleryTabOpenLink");
        refs.galleryWalkersTableBody = document.getElementById("galleryWalkersTableBody");
        refs.summaryGalleryLinks = document.getElementById("summaryGalleryLinks");
        refs.stepBackButtons = Array.from(document.querySelectorAll("[data-go-tab]"));
        refs.photoDialog = document.getElementById("photoDialog");
        refs.photoDialogImage = document.getElementById("photoDialogImage");
        refs.photoDialogCloseButton = document.getElementById("photoDialogCloseButton");
        refs.photoDialogBackdrop = document.querySelector("[data-close-photo-dialog]");
        refs.walkerContactDialog = document.getElementById("walkerContactDialog");
        refs.walkerContactDialogTitle = document.getElementById("walkerContactDialogTitle");
        refs.walkerContactCloseButton = document.getElementById("walkerContactCloseButton");
        refs.walkerContactBackdrop = document.querySelector("[data-close-contact-dialog]");
        refs.walkerContactCopyPhoneButton = document.getElementById("walkerContactCopyPhoneButton");
        refs.walkerContactPhoneText = document.getElementById("walkerContactPhoneText");
        refs.walkerContactCallButton = document.getElementById("walkerContactCallButton");
        refs.walkerContactWhatsAppButton = document.getElementById("walkerContactWhatsAppButton");
        refs.walkerContactSmsButton = document.getElementById("walkerContactSmsButton");
        refs.walkerContactGalleryWhatsAppButton = document.getElementById("walkerContactGalleryWhatsAppButton");
        refs.walkerContactGallerySmsButton = document.getElementById("walkerContactGallerySmsButton");
        refs.walkerContactPublicLink = document.getElementById("walkerContactPublicLink");
    }

    function bindEvents() {
        refs.stepTabs.forEach(tabButton => {
            tabButton.addEventListener("click", () => {
                const target = parseInt(tabButton.getAttribute("data-tab-index") || "0", 10);
                if (!Number.isNaN(target)) {
                    switchToTab(target);
                }
            });
        });

        refs.stepBackButtons.forEach(backButton => {
            backButton.addEventListener("click", () => {
                const target = parseInt(backButton.getAttribute("data-go-tab") || "0", 10);
                if (!Number.isNaN(target)) {
                    switchToTab(target);
                }
            });
        });

        refs.dateInput?.addEventListener("change", () => {
            const value = refs.dateInput.value;
            if (!value) {
                return;
            }

            state.selectedDate = value;
            state.selectedMonth = value.slice(0, 7);
            state.dateViewMode = "day";
            refs.monthInput.value = state.selectedMonth;
            clearSelectedTourAndReport();
            loadTours();
        });

        refs.monthInput?.addEventListener("change", () => {
            const value = refs.monthInput.value;
            if (!/^\d{4}-\d{2}$/.test(value)) {
                return;
            }

            state.selectedMonth = value;
            state.selectedDate = `${value}-01`;
            state.dateViewMode = "month";
            refs.dateInput.value = state.selectedDate;
            clearSelectedTourAndReport();
            loadTours();
        });

        refs.guideFilterSelect?.addEventListener("change", () => {
            state.selectedGuideFilter = refs.guideFilterSelect.value || "all";
            renderTours();
        });

        refs.refreshToursButton?.addEventListener("click", loadTours);
        refs.copyGalleryButton?.addEventListener("click", copyMainGalleryLink);
        refs.notesInput?.addEventListener("input", () => {
            if (state.report) {
                state.report.generalNotes = refs.notesInput.value || "";
            }
        });

        refs.walkersTableBody?.addEventListener("change", markRowDirty);
        refs.walkersTableBody?.addEventListener("input", markRowDirty);
        refs.walkersTableBody?.addEventListener("click", onWalkerActionClick);

        refs.submitReportButton?.addEventListener("click", submitReport);
        refs.uploadPhotosButton?.addEventListener("click", uploadPhotos);
        refs.retrySyncButton?.addEventListener("click", () => retryPendingPhotoSync(false));
        refs.addWalkupButton?.addEventListener("click", addWalkup);
        refs.goNotesButton?.addEventListener("click", () => switchToTab(2));
        refs.goSubmitTabButton?.addEventListener("click", () => switchToTab(3));
        refs.goGalleryButton?.addEventListener("click", () => switchToTab(4));
        refs.galleryTabCopyButton?.addEventListener("click", copyMainGalleryLink);
        refs.galleryWalkersTableBody?.addEventListener("click", onGalleryWalkerActionClick);
        refs.submitMissingPhoneWarnings?.addEventListener("click", event => {
            if (event.target.closest("[data-open-walkers-tab]")) {
                switchToTab(1);
            }
        });

        refs.photoDialogCloseButton?.addEventListener("click", closePhotoDialog);
        refs.photoDialogBackdrop?.addEventListener("click", closePhotoDialog);
        refs.walkerContactCloseButton?.addEventListener("click", closeWalkerContactDialog);
        refs.walkerContactBackdrop?.addEventListener("click", closeWalkerContactDialog);
        refs.walkerContactCopyPhoneButton?.addEventListener("click", async () => {
            const phone = refs.walkerContactCopyPhoneButton.getAttribute("data-phone") || "";
            if (!phone) {
                return;
            }

            await copyText(phone);
            setStatus("Phone copied.", "success");
        });
        document.addEventListener("keydown", event => {
            if (event.key === "Escape") {
                closePhotoDialog();
                closeWalkerContactDialog();
            }
        });
    }

    function canOpenTab(index) {
        if (index === 0) {
            return true;
        }

        if (index === 1 || index === 2 || index === 3) {
            return !!state.report;
        }

        if (index === 4) {
            return !!state.report && !!state.report.isSubmitted;
        }

        return false;
    }

    function updateStepAvailability() {
        if (!canOpenTab(state.activeTab)) {
            state.activeTab = 0;
        }

        refs.stepTabs.forEach(tabButton => {
            const index = parseInt(tabButton.getAttribute("data-tab-index") || "0", 10);
            const enabled = canOpenTab(index);
            tabButton.disabled = !enabled;
            tabButton.classList.toggle("active", index === state.activeTab);
        });

        refs.stepPanels.forEach((panel, index) => {
            panel.classList.toggle("d-none", index !== state.activeTab);
        });

        if (refs.goGalleryButton) {
            refs.goGalleryButton.disabled = !canOpenTab(4);
        }
    }

    function switchToTab(index) {
        if (!canOpenTab(index)) {
            return;
        }

        state.activeTab = index;
        updateStepAvailability();
    }

    async function loadTours() {
        const useMonth = state.dateViewMode === "month";
        if (useMonth && !/^\d{4}-\d{2}$/.test(state.selectedMonth)) {
            setStatus("Month is required.", "error");
            return;
        }

        if (!useMonth && !state.selectedDate) {
            setStatus("Date is required.", "error");
            return;
        }

        setStatus("Loading tours...", "info");
        if (refs.refreshToursButton) {
            refs.refreshToursButton.disabled = true;
        }
        try {
            const rangeStart = useMonth ? `${state.selectedMonth}-01` : state.selectedDate;
            const rangeEnd = useMonth ? getMonthEndDate(state.selectedMonth) : state.selectedDate;
            const query = useMonth
                ? `startDate=${encodeURIComponent(rangeStart)}&endDate=${encodeURIComponent(rangeEnd)}`
                : `date=${encodeURIComponent(state.selectedDate)}`;
            const url = `/guide-lite/api/tours?${query}`;
            const payload = await apiGet(url);

            state.selectedDate = payload.rangeStart || state.selectedDate;
            state.selectedMonth = state.selectedDate.slice(0, 7);
            state.tours = Array.isArray(payload.tours) ? payload.tours : [];
            state.guides = Array.isArray(payload.guides) ? payload.guides : [];

            refs.dateInput.value = state.selectedDate;
            refs.monthInput.value = state.selectedMonth;

            renderGuideFilterOptions();
            renderTours();
            setStatus(`Loaded ${getVisibleTours().length} tour(s).`, "success");
        } catch (err) {
            setStatus(`Failed to load tours: ${err.message}`, "error");
        } finally {
            if (refs.refreshToursButton) {
                refs.refreshToursButton.disabled = false;
            }
        }
    }

    function renderGuideFilterOptions() {
        const baseOptions = [
            { value: "all", label: "All Guides" },
            { value: "unassigned", label: "Unassigned" }
        ];

        const guideOptions = state.guides.map(guide => ({
            value: `guide-${guide.id}`,
            label: guide.displayName || `Guide ${guide.id}`
        }));

        const allOptions = baseOptions.concat(guideOptions);
        refs.guideFilterSelect.innerHTML = allOptions
            .map(option => `<option value="${escapeAttribute(option.value)}">${escapeHtml(option.label)}</option>`)
            .join("");

        const hasSelection = allOptions.some(option => option.value === state.selectedGuideFilter);
        if (!hasSelection) {
            state.selectedGuideFilter = "all";
        }

        refs.guideFilterSelect.value = state.selectedGuideFilter;
    }

    function getVisibleTours() {
        if (!Array.isArray(state.tours) || state.tours.length === 0) {
            return [];
        }

        if (state.selectedGuideFilter === "all") {
            return state.tours;
        }

        if (state.selectedGuideFilter === "unassigned") {
            return state.tours.filter(tour => !tour.guideId);
        }

        if (state.selectedGuideFilter.startsWith("guide-")) {
            const raw = state.selectedGuideFilter.slice("guide-".length);
            const guideId = parseInt(raw, 10);
            if (!Number.isNaN(guideId) && guideId > 0) {
                return state.tours.filter(tour => tour.guideId === guideId);
            }
        }

        return state.tours;
    }

    function updateToursLabel() {
        if (!refs.toursLabel) {
            return;
        }

        if (state.dateViewMode === "month") {
            refs.toursLabel.textContent = `Tours in ${formatMonthLabel(state.selectedMonth)}:`;
            return;
        }

        refs.toursLabel.textContent = `Tours on ${formatDateLabel(state.selectedDate)}:`;
    }

    function clearSelectedTourAndReport() {
        state.selectedTour = null;
        hideReport();
    }

    function renderTours(loadSelectedReport = true) {
        const visibleTours = getVisibleTours();
        updateToursLabel();

        if (!visibleTours.length) {
            refs.toursList.innerHTML = "<div class=\"text-muted small\">No tours found for this date.</div>";
            state.selectedTour = null;
            hideReport();
            return;
        }

        if (!state.selectedTour || !visibleTours.some(t => tourKey(t) === tourKey(state.selectedTour))) {
            state.selectedTour = visibleTours[0];
        }

        refs.toursList.innerHTML = visibleTours.map(tour => {
            const active = state.selectedTour && tourKey(state.selectedTour) === tourKey(tour);
            const badge = tour.isSubmitted
                ? "<span class=\"badge bg-success\">Submitted</span>"
                : "<span class=\"badge bg-secondary\">Draft</span>";
            const dateText = state.dateViewMode === "month"
                ? `<div class="tour-date text-muted">${escapeHtml(formatDateLabel(tour.tourDate))}</div>`
                : "";
            return `
                <button type="button"
                        class="list-group-item list-group-item-action ${active ? "active" : ""}"
                        data-tour-key="${escapeHtml(tourKey(tour))}">
                    <div class="d-flex justify-content-between align-items-start">
                        <div>
                            <div class="tour-time">${escapeHtml(tour.displayTime || tour.tourTime || "")}</div>
                            ${dateText}
                            <div class="tour-name">${escapeHtml(tour.listDisplayName || tour.tourName || "")}</div>
                            <div class="tour-meta">${escapeHtml(String(tour.totalGuests || 0))} guests</div>
                        </div>
                        ${badge}
                    </div>
                </button>
            `;
        }).join("");

        Array.from(refs.toursList.querySelectorAll("button[data-tour-key]")).forEach(btn => {
            btn.addEventListener("click", () => {
                const key = btn.getAttribute("data-tour-key") || "";
                const found = visibleTours.find(t => tourKey(t) === key);
                if (!found) {
                    return;
                }
                state.selectedTour = found;
                renderTours(false);
                loadReport(true);
            });
        });

        if (loadSelectedReport && state.selectedTour) {
            loadReport(false);
        }
    }

    async function loadReport(userInitiated, keepViewOnError = false) {
        if (!state.selectedTour) {
            hideReport();
            return;
        }

        setStatus("Loading report...", "info");
        try {
            const tour = state.selectedTour;
            const url = `/guide-lite/api/report?tourDate=${encodeURIComponent(tour.tourDate)}&tourName=${encodeURIComponent(tour.tourName)}&tourTime=${encodeURIComponent(tour.tourTime || "")}`;
            state.report = await apiGet(url);
            renderReport();
            if (userInitiated && state.activeTab === 0) {
                switchToTab(1);
            }
            setStatus("Report loaded.", "success");
        } catch (err) {
            if (!keepViewOnError) {
                hideReport();
            }
            setStatus(`Failed to load report: ${err.message}`, "error");
        }
    }

    function renderReport() {
        if (!state.report) {
            hideReport();
            return;
        }

        refs.reportHeaderCard.classList.remove("d-none");
        refs.reportTitle.textContent = `${state.report.tourName} (${state.report.displayTime || state.report.tourTime || ""})`;
        refs.reportMeta.textContent = `Date: ${state.report.tourDate}`;
        refs.notesInput.value = state.report.generalNotes || "";

        const submitted = !!state.report.isSubmitted;
        refs.reportStatusBadge.textContent = submitted ? "Submitted" : "Draft";
        refs.reportStatusBadge.className = `badge ${submitted ? "bg-success" : "bg-secondary"}`;

        const canShare = !!state.report.canShareLinks && !!state.report.galleryUrl;
        refs.copyGalleryButton.classList.toggle("d-none", !canShare);
        refs.openGalleryLink.classList.toggle("d-none", !canShare);
        refs.openGalleryLink.href = canShare ? state.report.galleryUrl : "#";
        if (refs.galleryTabActions) {
            refs.galleryTabActions.classList.toggle("d-none", !canShare);
        }
        if (refs.galleryTabOpenLink) {
            refs.galleryTabOpenLink.href = canShare ? state.report.galleryUrl : "#";
        }

        renderWalkersTable();
        renderPhotos();
        renderSubmitPreview();
        renderSummary();
        renderGalleryWalkers();
        updateStepAvailability();
    }

    function hideReport() {
        refs.reportHeaderCard.classList.add("d-none");
        refs.walkersTableBody.innerHTML = "";
        refs.photosGrid.innerHTML = "";
        refs.submitReviewTableBody.innerHTML = "";
        refs.submitReviewPhotos.innerHTML = "";
        refs.submitMissingPhoneWarnings.innerHTML = "";
        refs.summaryGalleryLinks.innerHTML = "";
        refs.galleryWalkersTableBody.innerHTML = "";
        if (refs.galleryTabActions) {
            refs.galleryTabActions.classList.add("d-none");
        }
        closeWalkerContactDialog();
        state.report = null;
        updateStepAvailability();
    }

    function renderWalkersTable() {
        const walkers = Array.isArray(state.report.walkers) ? state.report.walkers : [];
        refs.walkersTableBody.innerHTML = walkers.map(walker => `${walkerRowHtml(walker)}${walkerDetailRowHtml(walker)}`).join("");
        Array.from(refs.walkersTableBody.querySelectorAll("tr[data-booking-id]")).forEach(row => {
            syncNotesActionState(row);
            updateNoContactNotesRequirement(row);
            updatePhoneDependentActions(row);
            updateMissingPhoneIndicator(row);
        });
    }

    function normalizeReviewStatus(rawStatus) {
        const status = (rawStatus || "").trim();
        if (status === "NoContact" || status === "Negative" || status === "Positive") {
            return status;
        }

        return "Positive";
    }

    function walkerRowHtml(walker) {
        const status = normalizeReviewStatus(walker.reviewStatus);
        const reviewNotes = (walker.reviewNotes || "").trim();
        const phone = (walker.phone || "").trim();
        const actions = walkerActionsHtml(walker, phone, reviewNotes);
        return `
            <tr data-booking-id="${walker.bookingId}" data-review-notes="${escapeAttribute(reviewNotes)}" data-phone="${escapeAttribute(phone)}">
                <td class="walker-expand-cell">
                    <button type="button" class="btn btn-outline-secondary btn-sm row-expand-toggle" title="Show details" aria-expanded="false">
                        <i class="bi bi-chevron-right"></i>
                    </button>
                </td>
                <td>
                    <div class="d-flex align-items-center gap-1">
                        <div class="fw-semibold">${escapeHtml(walker.customerName || "")}</div>
                        <span class="text-danger row-no-phone-indicator ${phone ? "d-none" : ""}" title="No phone number">
                            <i class="bi bi-exclamation-circle-fill"></i>
                        </span>
                    </div>
                    <div class="small text-muted">${escapeHtml(walker.vendorName || "")}</div>
                </td>
                <td class="text-center">
                    <input type="checkbox" class="form-check-input row-checkin" ${walker.isCheckedIn ? "checked" : ""} />
                </td>
                <td><input type="number" class="form-control row-adults" min="0" value="${walker.actualAdults || 0}" /></td>
                <td><input type="number" class="form-control row-children" min="0" value="${walker.actualChildren || 0}" /></td>
                <td>
                    <select class="form-select row-status row-status-icons" title="Review status">
                        <option value="Positive" ${status === "Positive" ? "selected" : ""}>&#128077;</option>
                        <option value="NoContact" ${status === "NoContact" ? "selected" : ""}>&#128277;</option>
                        <option value="Negative" ${status === "Negative" ? "selected" : ""}>&#128078;</option>
                    </select>
                </td>
                <td class="walker-actions">${actions}</td>
            </tr>
        `;
    }

    function walkerDetailRowHtml(walker) {
        const reviewNotes = (walker.reviewNotes || "").trim();
        const phone = (walker.phone || "").trim();
        return `
            <tr class="walker-detail-row d-none" data-detail-for="${walker.bookingId}">
                <td colspan="7">
                    <div class="walker-detail-card">
                        <ul class="nav nav-tabs walker-inline-tabs" role="tablist">
                            <li class="nav-item" role="presentation">
                                <button type="button" class="nav-link active walker-detail-tab" data-detail-tab="notes" aria-selected="true">Notes</button>
                            </li>
                            <li class="nav-item" role="presentation">
                                <button type="button" class="nav-link walker-detail-tab" data-detail-tab="phone" aria-selected="false" title="Phone">
                                    <i class="bi bi-telephone"></i>
                                </button>
                            </li>
                        </ul>
                        <div class="walker-detail-pane" data-detail-pane="notes">
                            <textarea class="form-control row-review-notes" rows="2" placeholder="Add notes about this walker...">${escapeHtml(reviewNotes)}</textarea>
                        </div>
                        <div class="walker-detail-pane d-none" data-detail-pane="phone">
                            <input type="text" class="form-control row-phone-input walker-detail-phone-input" value="${escapeAttribute(phone)}" placeholder="Enter phone number" />
                        </div>
                    </div>
                </td>
            </tr>
        `;
    }

    function walkerActionsHtml(walker, phone, reviewNotes) {
        const phoneDigits = normalizePhoneDigits(phone);
        const waLink = phoneDigits ? `https://wa.me/${phoneDigits}` : "";
        const smsLink = phone ? `sms:${phone}` : "";
        const callLink = phone ? `tel:${phone}` : "";
        const wa = `<a class="btn btn-outline-success btn-sm row-wa-link${waLink ? "" : " disabled"}" href="${escapeAttribute(waLink || "#")}" target="_blank" rel="noopener" aria-disabled="${waLink ? "false" : "true"}" tabindex="${waLink ? "0" : "-1"}" title="WhatsApp"><i class="bi bi-whatsapp"></i></a>`;
        const sms = `<a class="btn btn-outline-primary btn-sm row-sms-link${smsLink ? "" : " disabled"}" href="${escapeAttribute(smsLink || "#")}" aria-disabled="${smsLink ? "false" : "true"}" tabindex="${smsLink ? "0" : "-1"}" title="SMS"><i class="bi bi-chat-dots"></i></a>`;
        const copyPhone = `<button type="button" class="btn btn-outline-secondary btn-sm row-copy-phone ${phone ? "" : "disabled"}" title="Copy phone"${phone ? "" : " disabled"}><i class="bi bi-clipboard"></i></button>`;
        const call = `<a class="btn btn-outline-secondary btn-sm row-call-link${callLink ? "" : " disabled"}" href="${escapeAttribute(callLink || "#")}" aria-disabled="${callLink ? "false" : "true"}" tabindex="${callLink ? "0" : "-1"}" title="Call"><i class="bi bi-telephone"></i></a>`;
        const notesStyle = reviewNotes ? "btn-primary" : "btn-outline-secondary";
        const notes = `<button type="button" class="btn ${notesStyle} btn-sm row-notes" title="Notes"><i class="bi bi-journal-text"></i></button>`;
        const del = walker.isWalkUp ? "<button type=\"button\" class=\"btn btn-outline-danger btn-sm row-delete-walkup\" title=\"Delete walk-up\"><i class=\"bi bi-trash\"></i></button>" : "";
        return `${wa}${sms}${copyPhone}${call}${notes}${del}`;
    }

    function markRowDirty(event) {
        const row = findMainRowFromElement(event.target);
        if (!row) {
            return;
        }
        row.classList.add("walker-row-dirty");

        if (event.target.classList.contains("row-status")) {
            const status = normalizeReviewStatus(event.target.value);
            updateNoContactNotesRequirement(row);
            if (status === "NoContact") {
                openNotesForNoContact(row);
            }
            return;
        }

        if (event.target.classList.contains("row-review-notes")) {
            const notes = event.target.value || "";
            row.setAttribute("data-review-notes", notes);
            syncNotesActionState(row);
            updateNoContactNotesRequirement(row);
            return;
        }

        if (event.target.classList.contains("row-phone-input")) {
            const phone = event.target.value || "";
            row.setAttribute("data-phone", phone);
            updatePhoneDependentActions(row);
            updateMissingPhoneIndicator(row);
        }
    }

    async function onWalkerActionClick(event) {
        const row = findMainRowFromElement(event.target);
        if (!row) {
            return;
        }

        if (event.target.closest(".row-expand-toggle")) {
            toggleWalkerDetail(row);
            return;
        }

        if (event.target.closest(".row-notes")) {
            openWalkerDetail(row, "notes");
            return;
        }

        if (event.target.closest(".walker-detail-tab")) {
            const tabButton = event.target.closest(".walker-detail-tab");
            const tab = tabButton?.getAttribute("data-detail-tab") || "notes";
            setWalkerDetailTab(row, tab);
            return;
        }

        if (event.target.closest(".row-delete-walkup")) {
            await deleteWalkup(row);
            return;
        }

        if (event.target.closest(".row-copy-phone")) {
            const phone = getRowPhone(row);
            if (!phone) {
                return;
            }
            await copyText(phone);
            setStatus("Phone copied.", "success");
        }
    }

    function findMainRowFromElement(element) {
        if (!element || typeof element.closest !== "function") {
            return null;
        }

        const mainRow = element.closest("tr[data-booking-id]");
        if (mainRow) {
            return mainRow;
        }

        const detailRow = element.closest("tr.walker-detail-row[data-detail-for]");
        if (!detailRow) {
            return null;
        }

        const bookingId = parseInt(detailRow.getAttribute("data-detail-for") || "0", 10);
        return findRowByBookingId(bookingId);
    }

    function findWalkerByRow(row) {
        if (!state.report || !Array.isArray(state.report.walkers)) {
            return null;
        }
        const bookingId = parseInt(row.getAttribute("data-booking-id") || "0", 10);
        return state.report.walkers.find(w => w.bookingId === bookingId) || null;
    }

    function collectRowState(row) {
        const bookingId = parseInt(row.getAttribute("data-booking-id") || "0", 10);
        const status = normalizeReviewStatus((row.querySelector(".row-status")?.value || "").trim());
        return {
            bookingId,
            actualAdults: Math.max(0, parseInt(row.querySelector(".row-adults")?.value || "0", 10) || 0),
            actualChildren: Math.max(0, parseInt(row.querySelector(".row-children")?.value || "0", 10) || 0),
            isCheckedIn: !!row.querySelector(".row-checkin")?.checked,
            reviewStatus: status,
            reviewNotes: getRowNotes(row),
            phone: getRowPhone(row),
            doNotContact: status.toLowerCase() === "nocontact"
        };
    }

    function getWalkerSnapshotForUi() {
        if (!state.report || !Array.isArray(state.report.walkers)) {
            return [];
        }

        const walkers = state.report.walkers.map(walker => ({ ...walker }));
        const byBookingId = new Map(walkers.map(walker => [walker.bookingId, walker]));
        const rows = Array.from(refs.walkersTableBody?.querySelectorAll("tr[data-booking-id]") || []);

        rows.forEach(row => {
            const rowState = collectRowState(row);
            const walker = byBookingId.get(rowState.bookingId);
            if (!walker) {
                return;
            }

            walker.actualAdults = rowState.actualAdults;
            walker.actualChildren = rowState.actualChildren;
            walker.actualAttendees = rowState.actualAdults + rowState.actualChildren;
            walker.isCheckedIn = rowState.isCheckedIn;
            walker.reviewStatus = rowState.reviewStatus;
            walker.reviewNotes = rowState.reviewNotes;
            walker.doNotContact = rowState.doNotContact;
            walker.phone = rowState.phone;
        });

        return walkers;
    }

    function findRowByBookingId(bookingId) {
        if (!bookingId) {
            return null;
        }

        return refs.walkersTableBody.querySelector(`tr[data-booking-id="${bookingId}"]`);
    }

    function findDetailRowByBookingId(bookingId) {
        if (!bookingId) {
            return null;
        }
        return refs.walkersTableBody.querySelector(`tr.walker-detail-row[data-detail-for="${bookingId}"]`);
    }

    function getDetailRowForMainRow(row) {
        if (!row) {
            return null;
        }
        const bookingId = parseInt(row.getAttribute("data-booking-id") || "0", 10);
        return findDetailRowByBookingId(bookingId);
    }

    function openWalkerDetail(row, tab) {
        const detailRow = getDetailRowForMainRow(row);
        if (!detailRow) {
            return;
        }

        detailRow.classList.remove("d-none");
        setExpandState(row, true);
        setWalkerDetailTab(row, tab || "notes");
    }

    function toggleWalkerDetail(row) {
        const detailRow = getDetailRowForMainRow(row);
        if (!detailRow) {
            return;
        }

        const isExpanded = !detailRow.classList.contains("d-none");
        if (isExpanded) {
            detailRow.classList.add("d-none");
            setExpandState(row, false);
            return;
        }

        openWalkerDetail(row, "notes");
    }

    function setWalkerDetailTab(row, tab) {
        const detailRow = getDetailRowForMainRow(row);
        if (!detailRow) {
            return;
        }

        const activeTab = tab === "phone" ? "phone" : "notes";
        const tabButtons = Array.from(detailRow.querySelectorAll(".walker-detail-tab"));
        tabButtons.forEach(button => {
            const current = button.getAttribute("data-detail-tab") || "notes";
            const active = current === activeTab;
            button.classList.toggle("active", active);
            button.setAttribute("aria-selected", active ? "true" : "false");
        });

        const panes = Array.from(detailRow.querySelectorAll("[data-detail-pane]"));
        panes.forEach(pane => {
            const current = pane.getAttribute("data-detail-pane") || "notes";
            pane.classList.toggle("d-none", current !== activeTab);
        });

        if (activeTab === "notes") {
            detailRow.querySelector(".row-review-notes")?.focus();
            return;
        }
        detailRow.querySelector(".row-phone-input")?.focus();
    }

    function setExpandState(row, isExpanded) {
        const expandButton = row.querySelector(".row-expand-toggle");
        if (!expandButton) {
            return;
        }
        expandButton.setAttribute("aria-expanded", isExpanded ? "true" : "false");
        expandButton.setAttribute("title", isExpanded ? "Hide details" : "Show details");
    }

    function openNotesForNoContact(row) {
        openWalkerDetail(row, "notes");
        updateNoContactNotesRequirement(row);
    }

    function getRowNotes(row) {
        const detailRow = getDetailRowForMainRow(row);
        const notesInput = detailRow?.querySelector(".row-review-notes");
        const notes = notesInput ? notesInput.value : row.getAttribute("data-review-notes");
        return (notes || "").trim();
    }

    function getRowPhone(row) {
        const detailRow = getDetailRowForMainRow(row);
        const phoneInput = detailRow?.querySelector(".row-phone-input");
        const phone = phoneInput ? phoneInput.value : row.getAttribute("data-phone");
        return (phone || "").trim();
    }

    function syncNotesActionState(row) {
        const notes = getRowNotes(row);
        row.setAttribute("data-review-notes", notes);
        const notesButton = row.querySelector(".row-notes");
        if (!notesButton) {
            return;
        }
        notesButton.classList.remove("btn-primary", "btn-outline-secondary");
        notesButton.classList.add(notes ? "btn-primary" : "btn-outline-secondary");
    }

    function updateNoContactNotesRequirement(row) {
        const detailRow = getDetailRowForMainRow(row);
        const notesInput = detailRow?.querySelector(".row-review-notes");
        if (!notesInput) {
            return;
        }

        const status = normalizeReviewStatus(row.querySelector(".row-status")?.value || "");
        const notes = (notesInput.value || "").trim();
        const isNoContact = status === "NoContact";

        notesInput.placeholder = isNoContact
            ? "Walkers with a no contact status needs a note"
            : "Add notes about this walker...";

        notesInput.classList.toggle("no-contact-note-required", isNoContact && !notes);
    }

    function updateMissingPhoneIndicator(row) {
        const indicator = row.querySelector(".row-no-phone-indicator");
        if (!indicator) {
            return;
        }

        const phone = getRowPhone(row);
        indicator.classList.toggle("d-none", !!(phone && phone.trim()));
    }

    function updatePhoneDependentActions(row) {
        const walker = findWalkerByRow(row);
        if (!walker) {
            return;
        }

        const phone = getRowPhone(row);
        row.setAttribute("data-phone", phone);
        const phoneDigits = normalizePhoneDigits(phone);
        const waLink = phoneDigits ? `https://wa.me/${phoneDigits}` : "";
        const smsLink = phone ? `sms:${phone}` : "";
        const callLink = phone ? `tel:${phone}` : "";
        setActionLink(row.querySelector(".row-wa-link"), waLink);
        setActionLink(row.querySelector(".row-sms-link"), smsLink);
        setActionLink(row.querySelector(".row-call-link"), callLink);

        const copyPhoneButton = row.querySelector(".row-copy-phone");
        if (copyPhoneButton) {
            copyPhoneButton.disabled = !phone;
            copyPhoneButton.classList.toggle("disabled", !phone);
        }
    }

    function getWhatsAppLinkForPhone(walker, phone) {
        const digits = normalizePhoneDigits(phone);
        if (!digits) {
            return "";
        }

        const message = extractWhatsAppMessage(walker?.whatsAppLink || "");
        if (!message) {
            return `https://wa.me/${digits}`;
        }

        return `https://wa.me/${digits}?text=${encodeURIComponent(message)}`;
    }

    function getSmsLinkForPhone(walker, phone) {
        if (!phone) {
            return "";
        }

        const body = extractSmsBody(walker?.smsLink || "");
        if (!body) {
            return `sms:${phone}`;
        }

        return `sms:${phone}?body=${encodeURIComponent(body)}`;
    }

    function extractWhatsAppMessage(link) {
        if (!link) {
            return "";
        }

        try {
            const parsed = new URL(link);
            return parsed.searchParams.get("text") || "";
        } catch {
            const match = link.match(/[?&]text=([^&]+)/i);
            if (!match || !match[1]) {
                return "";
            }
            try {
                return decodeURIComponent(match[1]);
            } catch {
                return match[1];
            }
        }
    }

    function extractSmsBody(link) {
        if (!link) {
            return "";
        }

        const match = link.match(/[?&]body=([^&]+)/i);
        if (!match || !match[1]) {
            return "";
        }

        try {
            return decodeURIComponent(match[1]);
        } catch {
            return match[1];
        }
    }

    function openWalkerContactDialog(row) {
        const walker = findWalkerByRow(row);
        if (!walker) {
            return;
        }

        const phone = getRowPhone(row);
        const phoneDigits = normalizePhoneDigits(phone);
        const publicLink = walker.galleryUrl || state.report?.galleryUrl || "";
        const galleryWhatsAppLink = getWhatsAppLinkForPhone(walker, phone);
        const gallerySmsLink = getSmsLinkForPhone(walker, phone);

        state.contactDialogBookingId = walker.bookingId;
        refs.walkerContactDialogTitle.textContent = walker.customerName || "Walker Contact";
        refs.walkerContactPhoneText.textContent = phone || "No phone";
        refs.walkerContactCopyPhoneButton.setAttribute("data-phone", phone);
        refs.walkerContactCopyPhoneButton.disabled = !phone;

        setActionLink(refs.walkerContactCallButton, phone ? `tel:${phone}` : "");
        setActionLink(refs.walkerContactWhatsAppButton, phoneDigits ? `https://wa.me/${phoneDigits}` : "");
        setActionLink(refs.walkerContactSmsButton, phone ? `sms:${phone}` : "");
        setActionLink(refs.walkerContactGalleryWhatsAppButton, galleryWhatsAppLink);
        setActionLink(refs.walkerContactGallerySmsButton, gallerySmsLink);
        setActionLink(refs.walkerContactPublicLink, publicLink);
        refs.walkerContactPublicLink.textContent = publicLink || "Unavailable";

        refs.walkerContactDialog.classList.remove("d-none");
        refs.walkerContactDialog.setAttribute("aria-hidden", "false");
    }

    function closeWalkerContactDialog() {
        state.contactDialogBookingId = null;
        refs.walkerContactDialog.classList.add("d-none");
        refs.walkerContactDialog.setAttribute("aria-hidden", "true");
        refs.walkerContactPhoneText.textContent = "No phone";
        refs.walkerContactCopyPhoneButton.setAttribute("data-phone", "");
    }

    function setActionLink(element, url) {
        if (!element) {
            return;
        }

        const hasUrl = !!url;
        element.classList.toggle("disabled", !hasUrl);
        element.setAttribute("aria-disabled", hasUrl ? "false" : "true");
        element.tabIndex = hasUrl ? 0 : -1;
        element.href = hasUrl ? url : "#";
    }

    function normalizePhoneDigits(phone) {
        if (!phone) {
            return "";
        }

        return String(phone).replace(/\D/g, "");
    }

    async function deleteWalkup(row) {
        const walker = findWalkerByRow(row);
        if (!walker || !walker.isWalkUp) {
            return;
        }

        if (!window.confirm("Delete this walk-up guest?")) {
            return;
        }

        try {
            await apiDelete(`/guide-lite/api/walkups/${walker.bookingId}`);
            setStatus("Walk-up deleted.", "success");
            await loadReport(false);
        } catch (err) {
            setStatus(`Delete failed: ${err.message}`, "error");
        }
    }

    async function addWalkup() {
        if (!state.selectedTour) {
            return;
        }

        const name = refs.walkupNameInput.value.trim();
        const phone = refs.walkupPhoneInput.value.trim();
        if (!name || !phone) {
            setStatus("Walk-up name and phone are required.", "error");
            return;
        }

        const payload = {
            tourDate: state.selectedTour.tourDate,
            tourName: state.selectedTour.tourName,
            tourTime: state.selectedTour.tourTime || "",
            customerName: name,
            phone,
            email: refs.walkupEmailInput.value.trim() || null,
            actualAdults: Math.max(0, parseInt(refs.walkupAdultsInput.value || "1", 10) || 1),
            actualChildren: Math.max(0, parseInt(refs.walkupChildrenInput.value || "0", 10) || 0),
            reviewNotes: refs.walkupNotesInput.value.trim() || null
        };

        try {
            await apiJson("/guide-lite/api/walkups", "POST", payload);
            refs.walkupNameInput.value = "";
            refs.walkupPhoneInput.value = "";
            refs.walkupEmailInput.value = "";
            refs.walkupAdultsInput.value = "1";
            refs.walkupChildrenInput.value = "0";
            refs.walkupNotesInput.value = "";
            setStatus("Walk-up added.", "success");
            await loadReport(false);
        } catch (err) {
            setStatus(`Could not add walk-up: ${err.message}`, "error");
        }
    }

    async function uploadPhotos() {
        if (!state.selectedTour) {
            return;
        }

        const files = Array.from(refs.photoInput.files || []);
        if (!files.length) {
            setStatus("Choose photo files first.", "error");
            return;
        }

        const form = new FormData();
        form.append("tourDate", state.selectedTour.tourDate);
        form.append("tourName", state.selectedTour.tourName);
        form.append("tourTime", state.selectedTour.tourTime || "");
        files.forEach(file => form.append("files", file, file.name));

        refs.uploadPhotosButton.disabled = true;
        refs.photoStatus.textContent = "Uploading...";
        try {
            const payload = await apiForm("/guide-lite/api/photos/upload-and-sync", form);
            refs.photoInput.value = "";
            if (payload.syncSucceeded) {
                refs.photoStatus.textContent = `Saved ${payload.savedCount || files.length} photo(s).`;
                clearPendingPhotoSync();
                setStatus("Photos uploaded and synced.", "success");
            } else {
                refs.photoStatus.textContent = payload.error || "Upload completed, sync pending.";
                if (payload.retry) {
                    setPendingPhotoSync(payload.retry);
                }
                setStatus("Upload completed. Sync retry queued.", "warn");
            }
            await loadReport(false);
        } catch (err) {
            refs.photoStatus.textContent = `Upload failed: ${err.message}`;
            setStatus(`Upload failed: ${err.message}`, "error");
        } finally {
            refs.uploadPhotosButton.disabled = false;
        }
    }

    function setPendingPhotoSync(payload) {
        state.pendingPhotoSync = payload;
        localStorage.setItem(STORAGE_KEYS.pendingPhotoSync, JSON.stringify(payload));
        refs.retrySyncButton.classList.remove("d-none");
    }

    function clearPendingPhotoSync() {
        state.pendingPhotoSync = null;
        localStorage.removeItem(STORAGE_KEYS.pendingPhotoSync);
        refs.retrySyncButton.classList.add("d-none");
    }

    function restorePendingPhotoSync() {
        const raw = localStorage.getItem(STORAGE_KEYS.pendingPhotoSync);
        if (!raw) {
            return;
        }
        try {
            state.pendingPhotoSync = JSON.parse(raw);
            refs.retrySyncButton.classList.remove("d-none");
            setTimeout(() => {
                retryPendingPhotoSync(true);
            }, 1500);
        } catch {
            clearPendingPhotoSync();
        }
    }

    async function retryPendingPhotoSync(silent) {
        if (!state.pendingPhotoSync) {
            return;
        }

        try {
            const payload = await apiJson("/guide-lite/api/photos/sync", "POST", state.pendingPhotoSync);
            clearPendingPhotoSync();
            refs.photoStatus.textContent = `Synced ${payload.photoCount || 0} photo(s).`;
            if (!silent) {
                setStatus("Photo sync complete.", "success");
            }
            await loadReport(false);
        } catch (err) {
            if (!silent) {
                setStatus(`Retry failed: ${err.message}`, "error");
            }
        }
    }

    async function submitReport() {
        if (!state.report || !state.selectedTour) {
            return;
        }

        const rowStates = Array.from(refs.walkersTableBody.querySelectorAll("tr[data-booking-id]")).map(row => collectRowState(row));
        const missingNoContactNotes = rowStates.find(row => row.doNotContact && !row.reviewNotes);
        if (missingNoContactNotes) {
            setStatus("No Contact requires notes. Add notes and submit again.", "warn");
            const row = findRowByBookingId(missingNoContactNotes.bookingId);
            if (row) {
                openNotesForNoContact(row);
            }
            return;
        }

        const changedPhones = rowStates.filter(rowState => {
            if (!rowState.phone) {
                return false;
            }
            const walker = state.report.walkers.find(w => w.bookingId === rowState.bookingId);
            if (!walker) {
                return false;
            }
            return normalizePhoneDigits(rowState.phone) !== normalizePhoneDigits(walker.phone || "");
        });

        const payload = {
            tourDate: state.report.tourDate,
            tourName: state.report.tourName,
            tourTime: state.report.tourTime,
            guideId: state.report.guideId,
            publicId: state.report.publicId,
            generalNotes: refs.notesInput.value || "",
            imagePaths: state.report.imagePaths || "",
            walkers: rowStates.map(row => ({
                bookingId: row.bookingId,
                actualAdults: row.actualAdults,
                actualChildren: row.actualChildren,
                isCheckedIn: row.isCheckedIn,
                doNotContact: row.doNotContact,
                reviewStatus: row.reviewStatus || null,
                reviewNotes: row.reviewNotes || null
            }))
        };

        const originalSubmitMarkup = refs.submitReportButton.innerHTML;
        refs.submitReportButton.disabled = true;
        refs.submitReportButton.innerHTML = "<span class=\"spinner-border spinner-border-sm me-2\" role=\"status\" aria-hidden=\"true\"></span>Sending Report...";
        setStatus("Submitting report...", "info");
        try {
            const preservedDate = state.selectedDate;
            const preservedMonth = state.selectedMonth;
            const preservedMode = state.dateViewMode;

            for (const rowState of changedPhones) {
                await apiJson(`/guide-lite/api/bookings/${rowState.bookingId}/phone`, "PATCH", {
                    phone: rowState.phone
                });
            }

            await apiJson("/guide-lite/api/reports/submit", "POST", payload);
            setStatus("Report submitted.", "success");
            await loadReport(false, true);
            state.selectedDate = preservedDate;
            state.selectedMonth = preservedMonth;
            state.dateViewMode = preservedMode;
            if (refs.dateInput) {
                refs.dateInput.value = state.selectedDate;
            }
            if (refs.monthInput) {
                refs.monthInput.value = state.selectedMonth;
            }
            switchToTab(4);
        } catch (err) {
            setStatus(`Submit failed: ${err.message}`, "error");
        } finally {
            refs.submitReportButton.innerHTML = originalSubmitMarkup;
            refs.submitReportButton.disabled = false;
        }
    }

    async function copyMainGalleryLink() {
        if (!state.report || !state.report.galleryUrl) {
            return;
        }
        await copyText(state.report.galleryUrl);
        setStatus("Gallery link copied.", "success");
    }

    function renderPhotos() {
        const photos = Array.isArray(state.report.photos) ? state.report.photos : [];
        if (!photos.length) {
            refs.photosGrid.innerHTML = "<div class=\"col-12 small text-muted\">No photos yet.</div>";
            return;
        }

        refs.photosGrid.innerHTML = photos.map(path => `
            <div class="col-6 col-md-3 col-lg-2">
                <button type="button" class="photo-thumb-btn" data-photo-path="${escapeAttribute(path)}" title="Preview photo">
                    <img src="${escapeAttribute(path)}" class="w-100 photo-thumb" alt="Tour photo" loading="lazy" />
                </button>
            </div>
        `).join("");

        Array.from(refs.photosGrid.querySelectorAll("[data-photo-path]")).forEach(button => {
            button.addEventListener("click", () => {
                const path = button.getAttribute("data-photo-path") || "";
                if (path) {
                    openPhotoDialog(path);
                }
            });
        });
    }

    function renderSubmitPreview() {
        if (!state.report) {
            refs.submitReviewTableBody.innerHTML = "";
            refs.submitReviewPhotos.innerHTML = "";
            refs.submitMissingPhoneWarnings.innerHTML = "";
            return;
        }

        const walkers = getWalkerSnapshotForUi();
        refs.submitReviewTableBody.innerHTML = walkers.map(w => `
            <tr>
                <td>${escapeHtml(w.customerName || "")}</td>
                <td>${escapeHtml(w.vendorName || "")}</td>
                <td>${escapeHtml(String(w.actualAdults || 0))}</td>
                <td>${escapeHtml(String(w.actualChildren || 0))}</td>
                <td>${escapeHtml(normalizeReviewStatus(w.reviewStatus || ""))}</td>
                <td>${escapeHtml(w.phone || "")}</td>
                <td>${escapeHtml(w.reviewNotes || "")}</td>
            </tr>
        `).join("");

        const missingPhone = walkers.filter(w => !(w.phone || "").trim());
        if (!missingPhone.length) {
            refs.submitMissingPhoneWarnings.innerHTML = "";
        } else {
            refs.submitMissingPhoneWarnings.innerHTML = missingPhone.map(w => `
                <div class="alert alert-warning py-2 mb-2 d-flex justify-content-between align-items-center">
                    <div>
                        <i class="bi bi-exclamation-triangle-fill me-1"></i>
                        <strong>${escapeHtml(w.customerName || "Walker")}</strong> has no phone number.
                    </div>
                    <button type="button" class="btn btn-sm btn-outline-warning" data-open-walkers-tab>Update Walker</button>
                </div>
            `).join("");
        }

        const photos = Array.isArray(state.report.photos) ? state.report.photos : [];
        if (!photos.length) {
            refs.submitReviewPhotos.innerHTML = "<div class=\"col-12 small text-muted\">No photos uploaded yet.</div>";
            return;
        }

        refs.submitReviewPhotos.innerHTML = photos.map(path => `
            <div class="col-6 col-md-3 col-lg-2">
                <button type="button" class="photo-thumb-btn" data-photo-path="${escapeAttribute(path)}" title="Preview photo">
                    <img src="${escapeAttribute(path)}" class="w-100 photo-thumb" alt="Tour photo" loading="lazy" />
                </button>
            </div>
        `).join("");

        Array.from(refs.submitReviewPhotos.querySelectorAll("[data-photo-path]")).forEach(button => {
            button.addEventListener("click", () => {
                const path = button.getAttribute("data-photo-path") || "";
                if (path) {
                    openPhotoDialog(path);
                }
            });
        });
    }

    function renderSummary() {
        if (!state.report) {
            refs.summaryGalleryLinks.innerHTML = "";
            return;
        }

        if (!state.report.isSubmitted) {
            refs.summaryGalleryLinks.innerHTML = "<div class=\"text-muted\">Submit report to generate gallery links.</div>";
            return;
        }

        const linkRows = [];
        if (state.report.galleryUrl) {
            linkRows.push({ label: "Main Gallery", url: state.report.galleryUrl });
        }

        const vendorLinks = state.report.vendorGalleryLinks || {};
        Object.keys(vendorLinks).sort((a, b) => a.localeCompare(b)).forEach(vendor => {
            const url = vendorLinks[vendor];
            if (url) {
                linkRows.push({ label: `${vendor} Gallery`, url });
            }
        });

        if (!linkRows.length) {
            refs.summaryGalleryLinks.innerHTML = "<div class=\"text-muted\">No gallery links available yet.</div>";
            return;
        }

        refs.summaryGalleryLinks.innerHTML = linkRows.map(link => `
            <div class="d-flex flex-wrap align-items-center gap-2 mb-2">
                <div class="fw-semibold">${escapeHtml(link.label)}</div>
                <a class="small" href="${escapeAttribute(link.url)}" target="_blank" rel="noopener">${escapeHtml(link.url)}</a>
                <button type="button" class="btn btn-outline-primary btn-sm summary-copy-link" data-link="${escapeAttribute(link.url)}">Copy</button>
            </div>
        `).join("");

        Array.from(refs.summaryGalleryLinks.querySelectorAll(".summary-copy-link")).forEach(button => {
            button.addEventListener("click", async () => {
                const url = button.getAttribute("data-link") || "";
                if (!url) {
                    return;
                }
                await copyText(url);
                setStatus("Gallery link copied.", "success");
            });
        });
    }

    function renderGalleryWalkers() {
        if (!state.report) {
            refs.galleryWalkersTableBody.innerHTML = "";
            return;
        }

        const walkers = Array.isArray(state.report.walkers) ? state.report.walkers : [];
        refs.galleryWalkersTableBody.innerHTML = walkers.map(walker => {
            const status = normalizeReviewStatus(walker.reviewStatus || "");
            const isNoContact = status === "NoContact" || !!walker.doNotContact;
            const phone = (walker.phone || "").trim();
            const hasPhone = !!phone;
            const waLink = hasPhone && !isNoContact ? (walker.whatsAppLink || getWhatsAppLinkForPhone(walker, phone)) : "";
            const smsLink = hasPhone && !isNoContact ? (walker.smsLink || getSmsLinkForPhone(walker, phone)) : "";
            const copyButton = walker.galleryUrl
                ? `<button type="button" class="btn btn-outline-secondary btn-sm" data-copy-gallery-link="${escapeAttribute(walker.galleryUrl)}" title="Copy gallery link"><i class="bi bi-clipboard"></i></button>`
                : "<button type=\"button\" class=\"btn btn-outline-secondary btn-sm\" disabled title=\"Copy unavailable\"><i class=\"bi bi-clipboard\"></i></button>";
            const actionButtons = (!isNoContact && hasPhone)
                ? `
                    <a class="btn btn-outline-success btn-sm" href="${escapeAttribute(waLink || "#")}" target="_blank" rel="noopener" title="WhatsApp"><i class="bi bi-whatsapp"></i></a>
                    <a class="btn btn-outline-primary btn-sm" href="${escapeAttribute(smsLink || "#")}" title="SMS"><i class="bi bi-chat-dots"></i></a>
                    ${copyButton}
                `
                : "";
            const statusWarning = isNoContact
                ? `<span class="text-danger small"><i class="bi bi-exclamation-circle-fill me-1"></i>No Contact</span>`
                : (!hasPhone ? `<span class="text-danger small"><i class="bi bi-exclamation-circle-fill me-1"></i>No Phone</span>` : "");
            const updateWalker = !hasPhone
                ? `<button type="button" class="btn btn-sm btn-outline-warning ms-2" data-open-walkers-tab>Update Walker</button>`
                : "";

            return `
                <tr>
                    <td>${escapeHtml(walker.customerName || "")}</td>
                    <td>${escapeHtml(walker.vendorName || "")}</td>
                    <td>${statusWarning || escapeHtml(status)}</td>
                    <td>${escapeHtml(phone)}</td>
                    <td>
                        <div class="walker-actions d-flex flex-wrap gap-1 align-items-center">
                            ${actionButtons}
                            ${updateWalker}
                        </div>
                    </td>
                </tr>
            `;
        }).join("");
    }

    async function onGalleryWalkerActionClick(event) {
        const updateButton = event.target.closest("[data-open-walkers-tab]");
        if (updateButton) {
            switchToTab(1);
            return;
        }

        const copyButton = event.target.closest("[data-copy-gallery-link]");
        if (!copyButton) {
            return;
        }

        const link = copyButton.getAttribute("data-copy-gallery-link") || "";
        if (!link) {
            return;
        }

        await copyText(link);
        setStatus("Gallery link copied.", "success");
    }

    async function apiGet(url) {
        const response = await fetch(url, { method: "GET", credentials: "include" });
        const payload = await readJson(response);
        if (!response.ok) {
            throw new Error(payload && payload.error ? payload.error : `HTTP ${response.status}`);
        }
        return payload;
    }

    async function apiDelete(url) {
        const response = await fetch(url, { method: "DELETE", credentials: "include" });
        const payload = await readJson(response);
        if (!response.ok) {
            throw new Error(payload && payload.error ? payload.error : `HTTP ${response.status}`);
        }
        return payload;
    }

    async function apiJson(url, method, body) {
        const response = await fetch(url, {
            method,
            credentials: "include",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify(body)
        });
        const payload = await readJson(response);
        if (!response.ok) {
            throw new Error(payload && payload.error ? payload.error : `HTTP ${response.status}`);
        }
        return payload;
    }

    async function apiForm(url, formData) {
        const response = await fetch(url, {
            method: "POST",
            credentials: "include",
            body: formData
        });
        const payload = await readJson(response);
        if (!response.ok) {
            throw new Error(payload && payload.error ? payload.error : `HTTP ${response.status}`);
        }
        return payload;
    }

    async function readJson(response) {
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

    function setStatus(message, type) {
        refs.globalStatus.textContent = message;
        refs.globalStatus.classList.remove("alert-info", "alert-success", "alert-danger", "alert-warning");
        if (type === "success") {
            refs.globalStatus.classList.add("alert-success");
            return;
        }
        if (type === "error") {
            refs.globalStatus.classList.add("alert-danger");
            return;
        }
        if (type === "warn") {
            refs.globalStatus.classList.add("alert-warning");
            return;
        }
        refs.globalStatus.classList.add("alert-info");
    }

    function tourKey(tour) {
        return `${tour.tourDate || ""}|${tour.groupFamilyKey || ""}|${tour.groupTimeKey || ""}`;
    }

    function formatDateInput(value) {
        const year = value.getFullYear();
        const month = String(value.getMonth() + 1).padStart(2, "0");
        const day = String(value.getDate()).padStart(2, "0");
        return `${year}-${month}-${day}`;
    }

    function formatMonthInput(value) {
        const year = value.getFullYear();
        const month = String(value.getMonth() + 1).padStart(2, "0");
        return `${year}-${month}`;
    }

    function getMonthEndDate(monthValue) {
        const match = /^(\d{4})-(\d{2})$/.exec(monthValue || "");
        if (!match) {
            return `${monthValue}-01`;
        }

        const year = parseInt(match[1], 10);
        const month = parseInt(match[2], 10);
        const day = new Date(year, month, 0).getDate();
        return `${match[1]}-${match[2]}-${String(day).padStart(2, "0")}`;
    }

    function formatMonthLabel(value) {
        const match = /^(\d{4})-(\d{2})$/.exec(value || "");
        if (!match) {
            return value || "";
        }

        const year = parseInt(match[1], 10);
        const month = parseInt(match[2], 10);
        const parsed = new Date(year, month - 1, 1);
        return parsed.toLocaleDateString(undefined, { month: "long", year: "numeric" });
    }

    function formatDateLabel(value) {
        if (!value || typeof value !== "string") {
            return "";
        }

        const parts = value.split("-");
        if (parts.length !== 3) {
            return value;
        }

        const year = parseInt(parts[0], 10);
        const month = parseInt(parts[1], 10);
        const day = parseInt(parts[2], 10);
        if (!year || !month || !day) {
            return value;
        }

        const parsed = new Date(year, month - 1, day);
        return parsed.toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" });
    }

    function openPhotoDialog(path) {
        if (!path) {
            return;
        }

        refs.photoDialogImage.src = path;
        refs.photoDialog.classList.remove("d-none");
        refs.photoDialog.setAttribute("aria-hidden", "false");
    }

    function closePhotoDialog() {
        refs.photoDialog.classList.add("d-none");
        refs.photoDialog.setAttribute("aria-hidden", "true");
        refs.photoDialogImage.src = "";
    }

    async function copyText(value) {
        if (!value) {
            return;
        }

        try {
            await navigator.clipboard.writeText(value);
            return;
        } catch {
            // Fallback below.
        }

        try {
            const textArea = document.createElement("textarea");
            textArea.value = value;
            textArea.setAttribute("readonly", "readonly");
            textArea.style.position = "fixed";
            textArea.style.top = "-9999px";
            document.body.appendChild(textArea);
            textArea.select();
            document.execCommand("copy");
            document.body.removeChild(textArea);
            return;
        } catch {
            window.prompt("Copy this link:", value);
        }
    }

    function escapeHtml(value) {
        return String(value)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/\"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function escapeAttribute(value) {
        return escapeHtml(value).replace(/`/g, "&#96;");
    }
})();
