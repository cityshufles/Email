// Simple helper to download a base64-encoded payload as a file
window.downloadBase64File = (mimeType, fileName, base64Data) => {
    try {
        const link = document.createElement('a');
        link.href = `data:${mimeType};base64,${base64Data}`;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    } catch (e) {
        console.error('downloadBase64File error', e);
    }
};

// replaced 12/18/25  Helper to download text content as a file
//window.downloadTextFile = (textContent, fileName, mimeType) => {
//    try {
//        const type = mimeType || 'text/plain';
//        const blob = new Blob([textContent], { type: type });
//        const url = window.URL.createObjectURL(blob);
//        const link = document.createElement('a');
//        link.href = url;
//        link.download = fileName;
//        document.body.appendChild(link);
//        link.click();
//        document.body.removeChild(link);
//        window.URL.revokeObjectURL(url);
//    } catch (e) {
//        console.error('downloadTextFile error', e);
//    }
//};

window.downloadTextFile = (textContent, fileName, mimeType) => {
    try {
        const type = fileName.endsWith('.vcf') ? 'text/vcard' : (mimeType || 'text/plain');
        const blob = new Blob([textContent], { type: type });
        const url = window.URL.createObjectURL(blob);

        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;

        // style.display = 'none' is totally safe. 
        // We append it so Safari recognizes it as a valid document element.
        link.style.display = 'none';
        document.body.appendChild(link);

        link.click();

        // 2-second timeout is the "magic number" for Blazor Server + iOS.
        // It ensures the Contacts App has time to read the Blob from Safari's memory.
        setTimeout(() => {
            if (document.body.contains(link)) {
                document.body.removeChild(link);
            }
            window.URL.revokeObjectURL(url);
        }, 2000);

    } catch (e) {
        console.error('vCard Download Failed:', e);
    }
};

// 2026-05-31 - Hand a protocol URL (sms:, whatsapp://, tel:) to the OS app without opening a new tab.
// For these schemes the browser does NOT navigate the document, so the Blazor page/circuit stays intact.
window.openProtocolLink = (url) => {
    try { window.location.href = url; } catch (e) { console.error('openProtocolLink error', e); }
};

// 2026-05-31 - Fetch a same-origin URL and download its content as a file (no extra tab).
// Used for per-row vCard download on the Bookings page (reuses /vcards/walker endpoint).
window.downloadUrlAsFile = async (url, fileName) => {
    try {
        const resp = await fetch(url, { credentials: 'same-origin' });
        if (!resp.ok) { console.error('downloadUrlAsFile HTTP', resp.status); return false; }
        const blob = await resp.blob();
        const objUrl = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = objUrl;
        link.download = fileName || 'download';
        link.style.display = 'none';
        document.body.appendChild(link);
        link.click();
        setTimeout(() => {
            if (document.body.contains(link)) { document.body.removeChild(link); }
            window.URL.revokeObjectURL(objUrl);
        }, 2000);
        return true;
    } catch (e) {
        console.error('downloadUrlAsFile error', e);
        return false;
    }
};

// Helper to insert text at cursor position in a textarea
window.insertTextAtCursor = (textareaId, textToInsert) => {
    try {
        console.log('insertTextAtCursor called with:', textareaId, textToInsert);
        
        const textarea = document.getElementById(textareaId);
        console.log('Found textarea:', textarea);
        
        if (textarea) {
            const start = textarea.selectionStart;
            const end = textarea.selectionEnd;
            const currentValue = textarea.value;
            
            console.log('Current textarea state:', { start, end, currentValue });
            
            // Insert the text at cursor position
            const newValue = currentValue.substring(0, start) + textToInsert + currentValue.substring(end);
            textarea.value = newValue;
            
            // Set cursor position after the inserted text
            textarea.selectionStart = start + textToInsert.length;
            textarea.selectionEnd = start + textToInsert.length;
            
            // Trigger the input event to update Blazor binding
            textarea.dispatchEvent(new Event('input', { bubbles: true }));
            
            // Focus the textarea
            textarea.focus();
            
            console.log('Text inserted successfully');
        } else {
            console.error('Textarea not found with id:', textareaId);
        }
    } catch (e) {
        console.error('insertTextAtCursor error:', e);
    }
};


