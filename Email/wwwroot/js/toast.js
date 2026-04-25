window.showToast = (type, title, message) => {
    // Remove any existing toasts first
    const existingToasts = document.querySelectorAll('.toast-notification');
    existingToasts.forEach(toast => toast.remove());

    // Create toast container if it doesn't exist
    let container = document.getElementById('toast-container');
    if (!container) {
        container = document.createElement('div');
        container.id = 'toast-container';
        container.className = 'toast-container';
        document.body.appendChild(container);
    }

    // Create toast element
    const toast = document.createElement('div');
    toast.className = `toast-notification toast-${type}`;
    
    // Get icon based on type
    const icons = {
        success: '✓',
        error: '✕',
        warning: '⚠',
        info: 'ℹ'
    };
    
    const icon = icons[type] || 'ℹ';
    
    toast.innerHTML = `
        <div class="toast-content">
            <div class="toast-icon">${icon}</div>
            <div class="toast-text">
                <div class="toast-title">${title}</div>
                <div class="toast-message">${message}</div>
            </div>
            <div class="toast-close" onclick="this.parentElement.parentElement.remove()">×</div>
        </div>
    `;
    
    // Add toast to container
    container.appendChild(toast);
    
    // Trigger animation
    setTimeout(() => {
        toast.classList.add('show');
    }, 10);
    
    // Auto-remove after 5 seconds
    setTimeout(() => {
        if (toast.parentElement) {
            toast.classList.remove('show');
            setTimeout(() => {
                if (toast.parentElement) {
                    toast.remove();
                }
            }, 300);
        }
    }, 5000);
};

window.__hasPageUnsavedChanges = false;
window.setPageUnsavedChanges = (hasUnsavedChanges) => {
    window.__hasPageUnsavedChanges = !!hasUnsavedChanges;
};

if (!window.__pageUnsavedChangesBeforeUnloadHooked) {
    window.addEventListener('beforeunload', (event) => {
        if (!window.__hasPageUnsavedChanges) {
            return;
        }

        event.preventDefault();
        event.returnValue = '';
    });
    window.__pageUnsavedChangesBeforeUnloadHooked = true;
}

// File download helper function
window.downloadFile = (filename, contentType, base64Data) => {
    const byteCharacters = atob(base64Data);
    const byteNumbers = new Array(byteCharacters.length);
    for (let i = 0; i < byteCharacters.length; i++) {
        byteNumbers[i] = byteCharacters.charCodeAt(i);
    }
    const byteArray = new Uint8Array(byteNumbers);
    const blob = new Blob([byteArray], { type: contentType });
    
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    window.URL.revokeObjectURL(url);
};

// Click outside handler for dropdowns
window.addClickOutsideHandler = (containerId, dotNetRef) => {
    const container = document.getElementById(containerId);
    if (!container) return;

    document.addEventListener('click', (event) => {
        if (!container.contains(event.target)) {
            // Close all dropdowns when clicking outside
            const dropdowns = container.querySelectorAll('.dropdown-menu.show');
            dropdowns.forEach(dropdown => {
                dropdown.classList.remove('show');
            });
            
            // Trigger Blazor event to update state
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('CloseDropdowns');
            }
        }
    });
};


