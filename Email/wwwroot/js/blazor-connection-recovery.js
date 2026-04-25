(function () {
    "use strict";

    if (window.__emailBlazorConnectionRecoveryInstalled) {
        return;
    }
    window.__emailBlazorConnectionRecoveryInstalled = true;

    var defaults = {
        enableWebLock: true,
        maxRetries: 30,
        retryIntervalMilliseconds: 2000,
        modalShowDelayMilliseconds: 1200
    };

    var config = Object.assign({}, defaults, window.__emailConnectionRecoveryConfig || {});
    var reconnectModal = document.getElementById("components-reconnect-modal");
    var titleEl = reconnectModal ? reconnectModal.querySelector("[data-reconnect-title]") : null;
    var messageEl = reconnectModal ? reconnectModal.querySelector("[data-reconnect-message]") : null;
    var showTimer = null;

    function delay(ms) {
        return new Promise(function (resolve) {
            window.setTimeout(resolve, ms);
        });
    }

    function setModalText(title, message) {
        if (titleEl) {
            titleEl.textContent = title;
        }
        if (messageEl) {
            messageEl.textContent = message;
        }
    }

    function clearShowTimer() {
        if (showTimer) {
            window.clearTimeout(showTimer);
            showTimer = null;
        }
    }

    function showModalWithDelay() {
        if (!reconnectModal) {
            return;
        }

        clearShowTimer();
        showTimer = window.setTimeout(function () {
            reconnectModal.classList.add("email-reconnect-show");
        }, Math.max(0, config.modalShowDelayMilliseconds | 0));
    }

    function hideModal() {
        clearShowTimer();
        if (!reconnectModal) {
            return;
        }

        reconnectModal.classList.remove("email-reconnect-show");
    }

    function reloadPageSoon() {
        window.setTimeout(function () {
            window.location.reload();
        }, 350);
    }

    function startReconnectionProcess() {
        showModalWithDelay();
        setModalText("Reconnecting...", "Trying to restore your session.");

        var canceled = false;
        var maxRetries = Math.max(1, config.maxRetries | 0);
        var retryInterval = Math.max(500, config.retryIntervalMilliseconds | 0);

        (async function () {
            for (var attempt = 1; attempt <= maxRetries; attempt++) {
                if (canceled) {
                    return;
                }

                setModalText(
                    "Reconnecting...",
                    "Connection lost. Retrying " + attempt + " of " + maxRetries + "."
                );

                await delay(retryInterval);
                if (canceled) {
                    return;
                }

                try {
                    var reconnected = await window.Blazor.reconnect();
                    if (reconnected) {
                        return;
                    }

                    setModalText("Refreshing...", "Your session expired. Reloading now.");
                    reloadPageSoon();
                    return;
                } catch (_error) {
                    // No-op. Continue retrying.
                }
            }

            setModalText("Refreshing...", "Unable to reconnect. Reloading now.");
            reloadPageSoon();
        })();

        return {
            cancel: function () {
                canceled = true;
                hideModal();
            }
        };
    }

    function enableWebLock() {
        if (!config.enableWebLock || !window.isSecureContext) {
            return;
        }
        if (!navigator.locks || typeof navigator.locks.request !== "function") {
            return;
        }

        try {
            var controller = new AbortController();
            var released = false;

            function release() {
                if (released) {
                    return;
                }
                released = true;
                controller.abort();
            }

            window.addEventListener("pagehide", release, { passive: true });
            window.addEventListener("beforeunload", release, { passive: true });

            navigator.locks.request(
                "email-blazor-signalr-keepalive",
                { mode: "shared", signal: controller.signal },
                async function () {
                    await new Promise(function (resolve) {
                        controller.signal.addEventListener("abort", function () {
                            resolve();
                        }, { once: true });
                    });
                }
            ).catch(function () {
                // Best effort only.
            });
        } catch (_error) {
            // Best effort only.
        }
    }

    function bootBlazor() {
        if (!window.Blazor || typeof window.Blazor.start !== "function") {
            return;
        }

        var currentReconnectionProcess = null;
        window.Blazor.start({
            reconnectionHandler: {
                onConnectionDown: function () {
                    if (!currentReconnectionProcess) {
                        currentReconnectionProcess = startReconnectionProcess();
                    }
                },
                onConnectionUp: function () {
                    if (currentReconnectionProcess) {
                        currentReconnectionProcess.cancel();
                        currentReconnectionProcess = null;
                    }
                }
            }
        }).catch(function () {
            setModalText("Unable to start session", "Please refresh this page.");
            if (reconnectModal) {
                reconnectModal.classList.add("email-reconnect-show");
            }
        });
    }

    enableWebLock();
    bootBlazor();
})();
