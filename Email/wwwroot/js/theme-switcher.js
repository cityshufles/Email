// 2025-11-22 00:00 UTC - Bootswatch Theme Switcher (JS-only)
(function () {
  const THEME_LINK_ID = 'bootswatch-theme-link';
  const KEY_THEME = 'bootswatchTheme';
  const KEY_MODE = 'colorMode';
  const DEFAULT_THEME = 'spacelab';
  const DEFAULT_MODE = 'dark';
  const SYNCFUSION_LINK_ID = 'syncfusion-theme-link';

  function ensureThemeLink() {
    let link = document.getElementById(THEME_LINK_ID);
    if (!link) {
      link = document.createElement('link');
      link.id = THEME_LINK_ID;
      link.rel = 'stylesheet';
      document.head.appendChild(link);
    }
    return link;
  }

  // Change Bootswatch theme CSS
  window.changeBootswatchTheme = function (themeName) {
    try {
      const name = (themeName || '').trim() || DEFAULT_THEME;
      const link = ensureThemeLink();
      
      // Handle default Bootstrap theme - use standard Bootstrap CDN
      if (name === 'default') {
        // Ensure link exists
        if (!link.parentNode) {
          document.head.appendChild(link);
        }
        // Use standard Bootstrap instead of Bootswatch
        const href = 'https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/css/bootstrap.min.css';
        if (link.href !== href) {
          link.href = href;
        }
        return;
      }
      
      // Ensure link exists for Bootswatch themes
      if (!link.parentNode) {
        document.head.appendChild(link);
      }
      
      const href = `https://cdn.jsdelivr.net/npm/bootswatch@5/dist/${name}/bootstrap.min.css`;
      if (link.href !== href) {
        link.href = href;
      }
    } catch (e) {
      // no-op
    }
  };

  // Change color mode (light/dark)
  window.changeColorMode = function (mode) {
    try {
      const m = (mode === 'light' || mode === 'dark') ? mode : DEFAULT_MODE;
      document.documentElement.setAttribute('data-bs-theme', m);

      // Also switch Syncfusion theme to match mode
      const sLink = document.getElementById(SYNCFUSION_LINK_ID);
      if (sLink) {
        const current = sLink.getAttribute('href') || '';
        const desired = m === 'dark'
          ? '_content/Syncfusion.Blazor.Themes/bootstrap5-dark.css'
          : '_content/Syncfusion.Blazor.Themes/bootstrap5.css';
        if (current !== desired) {
          sLink.setAttribute('href', desired);
        }
      }
    } catch (e) {
      // no-op
    }
  };

  // LocalStorage persistence
  window.saveThemePreference = function (themeName, colorMode) {
    try {
      if (themeName) localStorage.setItem(KEY_THEME, themeName);
      if (colorMode) localStorage.setItem(KEY_MODE, colorMode);
    } catch (e) {
      // no-op
    }
  };

  window.loadThemePreference = function () {
    try {
      return {
        theme: localStorage.getItem(KEY_THEME) || DEFAULT_THEME,
        colorMode: localStorage.getItem(KEY_MODE) || DEFAULT_MODE
      };
    } catch (e) {
      return { theme: DEFAULT_THEME, colorMode: DEFAULT_MODE };
    }
  };

  // Dialog helpers for whole-page live preview with revert-on-cancel
  const dialogStateById = {}; // modalId -> { originalTheme, originalMode, applied }

  function selectRadioByValue(container, name, value) {
    const radios = container.querySelectorAll(`input[type="radio"][name="${name}"]`);
    for (const r of radios) {
      r.checked = (r.value === value);
    }
  }

  function getSelectedRadioValue(container, name) {
    const sel = container.querySelector(`input[type="radio"][name="${name}"]:checked`);
    return sel ? sel.value : null;
  }

  window.initThemeDialog = function (modalId) {
    try {
      const modalEl = document.getElementById(modalId);
      if (!modalEl || !window.bootstrap) return;

      dialogStateById[modalId] = { originalTheme: null, originalMode: null, applied: false };

      // On show: capture original prefs and prefill UI
      modalEl.addEventListener('show.bs.modal', function () {
        const prefs = window.loadThemePreference();
        const state = dialogStateById[modalId];
        state.originalTheme = prefs.theme;
        state.originalMode = prefs.colorMode;
        state.applied = false;

        // Prefill UI
        selectRadioByValue(modalEl, 'themeOption', prefs.theme);
        const modeSwitch = modalEl.querySelector('#colorModeSwitch');
        if (modeSwitch) {
          modeSwitch.checked = (prefs.colorMode === 'dark');
        }
      });

      // Change handlers for live preview
      modalEl.addEventListener('change', function (ev) {
        const target = ev.target;
        if (!target) return;
        if (target.matches('input[type="radio"][name="themeOption"]')) {
          const theme = target.value;
          window.changeBootswatchTheme(theme);
        } else if (target.matches('#colorModeSwitch')) {
          const mode = target.checked ? 'dark' : 'light';
          window.changeColorMode(mode);
        }
      });

      // On hide: revert if not applied
      modalEl.addEventListener('hide.bs.modal', function () {
        const state = dialogStateById[modalId];
        if (!state || state.applied) return;
        // revert to original
        window.changeBootswatchTheme(state.originalTheme);
        window.changeColorMode(state.originalMode);
      });
    } catch (e) {
      // no-op
    }
  };

  window.applyThemeFromDialog = function (modalId) {
    try {
      const modalEl = document.getElementById(modalId);
      if (!modalEl) return;

      const selectedTheme = getSelectedRadioValue(modalEl, 'themeOption') || DEFAULT_THEME;
      const modeSwitch = modalEl.querySelector('#colorModeSwitch');
      const selectedMode = modeSwitch && modeSwitch.checked ? 'dark' : 'light';

      // Persist and keep applied
      window.saveThemePreference(selectedTheme, selectedMode);
      window.changeBootswatchTheme(selectedTheme);
      window.changeColorMode(selectedMode);

      const state = dialogStateById[modalId];
      if (state) state.applied = true;

      // Dismiss modal
      if (window.bootstrap) {
        const instance = window.bootstrap.Modal.getOrCreateInstance(modalEl);
        instance.hide();
      }
    } catch (e) {
      // no-op
    }
  };

  // Programmatic open to avoid data-api edge cases
  window.openThemeDialog = function () {
    try {
      const modalEl = document.getElementById('themeSwitcherDialog');
      if (!modalEl) {
        if (window.cslog) window.cslog('openThemeDialog: modal element not found');
        return false;
      }
      if (window.bootstrap && window.bootstrap.Modal) {
        const instance = window.bootstrap.Modal.getOrCreateInstance(modalEl);
        instance.show();
        return false;
      }
      if (window.cslog) window.cslog('openThemeDialog: bootstrap.Modal not available');
      return false;
    } catch (e) {
      if (window.cslog) window.cslog('openThemeDialog error', e);
      return false;
    }
  };

  // Fallback delegated handler when inline onchange is used on modal body
  window.themeDialogOnChange = function (ev) {
    try {
      if (!ev || !ev.target) return;
      const t = ev.target;
      if (t.matches && t.matches('input[type="radio"][name="themeOption"]')) {
        window.changeBootswatchTheme(t.value);
        return;
      }
      if (t.id === 'colorModeSwitch') {
        window.changeColorMode(t.checked ? 'dark' : 'light');
        return;
      }
    } catch (e) {
      // no-op
    }
  };
})();


