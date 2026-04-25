(function () {
    "use strict";

    if (window.__signalrDiagnosticsProbeInstalled) {
        return;
    }
    window.__signalrDiagnosticsProbeInstalled = true;

    var endpoint = "/diagnostics/signalr/browser-event";

    function getTabId() {
        try {
            var existing = sessionStorage.getItem("signalrDiagnosticsTabId");
            if (existing) {
                return existing;
            }

            var created = "tab-" + Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 10);
            sessionStorage.setItem("signalrDiagnosticsTabId", created);
            return created;
        } catch (_err) {
            return "tab-unknown";
        }
    }

    function buildRoute() {
        try {
            var path = window.location.pathname || "/";
            var search = window.location.search || "";
            return path + search;
        } catch (_err) {
            return "/";
        }
    }

    function sendEvent(eventType, details) {
        try {
            var payload = {
                eventType: eventType,
                route: buildRoute(),
                pageUrl: window.location.href || "",
                tabId: getTabId(),
                visibilityState: document.visibilityState || "",
                hidden: !!document.hidden,
                online: typeof navigator.onLine === "boolean" ? navigator.onLine : null,
                details: details || "",
                clientTimestampUtc: new Date().toISOString()
            };

            var body = JSON.stringify(payload);
            var sent = false;

            if (typeof navigator.sendBeacon === "function") {
                try {
                    sent = navigator.sendBeacon(endpoint, new Blob([body], { type: "application/json" }));
                } catch (_err) {
                    sent = false;
                }
            }

            if (!sent && typeof fetch === "function") {
                fetch(endpoint, {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    credentials: "same-origin",
                    keepalive: true,
                    body: body
                }).catch(function () {
                    // Diagnostics should never break user flow.
                });
            }
        } catch (_err) {
            // Diagnostics should never break user flow.
        }
    }

    function hookRouteChanges() {
        if (!window.history) {
            return;
        }

        var originalPushState = window.history.pushState;
        var originalReplaceState = window.history.replaceState;

        if (typeof originalPushState === "function") {
            window.history.pushState = function () {
                var result = originalPushState.apply(this, arguments);
                sendEvent("route_change", "source=pushState");
                return result;
            };
        }

        if (typeof originalReplaceState === "function") {
            window.history.replaceState = function () {
                var result = originalReplaceState.apply(this, arguments);
                sendEvent("route_change", "source=replaceState");
                return result;
            };
        }

        window.addEventListener("popstate", function () {
            sendEvent("route_change", "source=popstate");
        }, { passive: true });
    }

    document.addEventListener("visibilitychange", function () {
        if (document.visibilityState === "hidden") {
            sendEvent("visibility_hidden", "visibilitychange");
            return;
        }

        if (document.visibilityState === "visible") {
            sendEvent("visibility_visible", "visibilitychange");
        }
    }, { passive: true });

    window.addEventListener("pagehide", function (event) {
        var persisted = event && event.persisted ? "true" : "false";
        sendEvent("pagehide", "persisted=" + persisted);
    }, { passive: true });

    window.addEventListener("beforeunload", function () {
        sendEvent("beforeunload", "window-beforeunload");
    }, { passive: true });

    window.addEventListener("online", function () {
        sendEvent("online", "navigator-online");
    }, { passive: true });

    window.addEventListener("offline", function () {
        sendEvent("offline", "navigator-offline");
    }, { passive: true });

    hookRouteChanges();
    sendEvent("init", "probe-started");
})();
