// Created: 10/8/2025 
// Centralized clipboard function to handle iOS security requirements.
// The Clipboard API on iOS requires the call to be initiated by a direct user action (like a 'click' or 'tap' event).
// This function is called directly from Blazor C# to ensure an unbroken chain from the user's tap to the clipboard write.
function blazorCopyText(text) {
    if (navigator.clipboard && window.isSecureContext) {
        return navigator.clipboard.writeText(text);
    } else {
        // Fallback for older browsers or insecure contexts
        let textArea = document.createElement("textarea");
        textArea.value = text;
        textArea.style.position = "fixed";  // Prevent scrolling to bottom of page in MS Edge.
        textArea.style.top = "0";
        textArea.style.left = "0";
        textArea.style.width = "2em";
        textArea.style.height = "2em";
        textArea.style.padding = "0";
        textArea.style.border = "none";
        textArea.style.outline = "none";
        textArea.style.boxShadow = "none";
        textArea.style.background = "transparent";
        document.body.appendChild(textArea);
        textArea.focus();
        textArea.select();
        try {
            document.execCommand('copy');
            return Promise.resolve();
        } catch (err) {
            console.error('Fallback: Oops, unable to copy', err);
            return Promise.reject(err);
        } finally {
            document.body.removeChild(textArea);
        }
    }
}

// Focuses and selects all text in a textarea (best effort for iOS/Android).
function blazorFocusAndSelectTextArea(textArea) {
    if (!textArea) {
        return;
    }

    const selectAll = () => {
        try {
            if (typeof textArea.focus === "function") {
                textArea.focus({ preventScroll: true });
            }
        } catch (_) {
            try { textArea.focus(); } catch (_) { }
        }

        try {
            if (typeof textArea.select === "function") {
                textArea.select();
            }
            const length = (textArea.value || "").length;
            if (typeof textArea.setSelectionRange === "function") {
                textArea.setSelectionRange(0, length);
            }
        } catch (_) {
            // Ignore selection failures on strict browsers.
        }
    };

    // Immediate + delayed attempts improve reliability on iOS Safari.
    selectAll();
    setTimeout(selectAll, 0);
    setTimeout(selectAll, 50);
}


