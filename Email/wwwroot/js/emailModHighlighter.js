// 2025-11-30 00:00 UTC - EmailPipeline plaintext highlighter (mirrors FreeTourModificationParser + BaseParser patterns)
(function () {
	"use strict";
	function esc(s) {
		return String(s)
			.replace(/&/g, "&amp;")
			.replace(/</g, "&lt;")
			.replace(/>/g, "&gt;")
			.replace(/"/g, "&quot;")
			.replace(/'/g, "&#39;");
	}

	function highlightFreeTour(text) {
		let html = text;

		// Exact patterns from Email.Services.GmailProcessing.VendorParsers.FreeTourModificationParser
		html = html.replace(/(Booking Reference Number:\s*)([^\n\r]+)/ig, (_, a, b) => a + '<span class="hl-field hl-code">' + esc(b.trim()) + '</span>');
		html = html.replace(/(Booking Name:\s*)([^\n\r]+)/ig,          (_, a, b) => a + '<span class="hl-field">' + esc(b.trim()) + '</span>');
		html = html.replace(/(Booking E-mail:\s*)([^\n\r]+)/ig,        (_, a, b) => a + '<span class="hl-field">' + esc(b.trim()) + '</span>');
		html = html.replace(/(Booking phone:\s*)([^\n\r]+)/ig,         (_, a, b) => a + '<span class="hl-field">' + esc(b.trim()) + '</span>');
		html = html.replace(/(Language:\s*)([^\n\r]+)/ig,              (_, a, b) => a + '<span class="hl-field">' + esc(b.trim()) + '</span>');
		html = html.replace(/(Guests:\s*)([^\n\r]+)/ig,                (_, a, b) => a + '<span class="hl-field">' + esc(b.trim()) + '</span>');
		html = html.replace(/(Children\s*\(≤?15\s*y\.?o\.\):\s*)([^\n\r]+)/ig, (_, a, b) => a + '<span class="hl-field">' + esc(b.trim()) + '</span>');
		html = html.replace(/(Adults:\s*)([^\n\r]+)/ig,                (_, a, b) => a + '<span class="hl-field">' + esc(b.trim()) + '</span>');
		html = html.replace(/(Children\s*\(Age\s*0-15\):\s*)([^\n\r]+)/ig, (_, a, b) => a + '<span class="hl-field">' + esc(b.trim()) + '</span>');
		html = html.replace(/(New Date of the Tour:\s*)([^\n\r]+)/ig,       (_, a, b) => a + '<span class="hl-field">' + esc(b.trim()) + '</span>');
		html = html.replace(/(Previous Date of the Tour:\s*)([^\n\r]+)/ig,  (_, a, b) => a + '<span class="hl-field">' + esc(b.trim()) + '</span>');
		html = html.replace(/(Date of the Tour:\s*)([^\n\r]+)/ig,       (_, a, b) => a + '<span class="hl-field">' + esc(b.trim()) + '</span>');

		// Phone pattern used by the parser
		html = html.replace(/(\+?\d{10,15})/g, "<span class=\"hl-field\">$1</span>");

		// Optional: BaseParser.TryParseFreeTourAttendees fallback form (guests: N people)
		html = html.replace(/(guests:\s*)(\d+)(\s*people)/ig, (_, a, num, tail) => a + '<span class="hl-field">' + num + '</span>' + tail);

		return html;
	}

	function highlightFreeTourCancellation(text) {
		let html = text;
		// Your customer: NAME has cancelled
		html = html.replace(/(Your customer:\s*)(.*?)(\s+has cancelled)/ig, (_, a, b, c) => a + '<span class="hl-field">' + esc(b.trim()) + '</span>' + c);
		// Booking code (#CODE)
		html = html.replace(/\(#([^)]+)\)/g, '(<span class="hl-field hl-code">$1</span>)');
		// for TOUR at TIME on DATE
		html = html.replace(/(for\s+)(.*?)(\s+at\s+)/ig, (_, a, b, c) => a + '<span class="hl-field">' + esc(b.trim()) + '</span>' + c);
		html = html.replace(/(\sat\s+)([^\s]+)(\s+on\s+)/ig, (_, a, b, c) => a + '<span class="hl-field">' + esc(b.trim()) + '</span>' + c);
		html = html.replace(/(\son\s+)(\d{4}-\d{2}-\d{2})/ig, (_, a, b) => a + '<span class="hl-field">' + b + '</span>');
		return html;
	}

	function highlightGuruWalk(text) {
		let html = text;

		// Headings
		html = html.replace(/(Details of the new booking:)/ig, '<span class="hl-section hl-new">$1</span>');
		html = html.replace(/(Details of the previous booking:)/ig, '<span class="hl-section hl-prev">$1</span>');

		// New booking section field value wraps (mirrors EmailContentExtractor flexible patterns)
		html = html.replace(/(Walker:\s*)([^&\n<]+?)(?=\s+Booking code:|$)/ig, (_, a, b) => a + '<span class="hl-field">' + esc(b) + '</span>');
		html = html.replace(/(Booking code:\s*)([^&\n<]+?)(?=\s+Phone:|$)/ig, (_, a, b) => a + '<span class="hl-field hl-code">' + esc(b) + '</span>');
		html = html.replace(/(Phone:\s*)([^&\n<]+?)(?=\s+Attendees:|$)/ig, (_, a, b) => a + '<span class="hl-field">' + esc(b) + '</span>');
		html = html.replace(/(Attendees:\s*)(\d+)/ig, (_, a, b) => a + '<span class="hl-field">' + b + '</span>');
		html = html.replace(/(Language:\s*)([^&\n<]+?)(?=\s+Date:|$)/ig, (_, a, b) => a + '<span class="hl-field">' + esc(b) + '</span>');
		html = html.replace(/(Date:\s*)([^&\n<]+?)(?=\s+Time:|$)/ig,    (_, a, b) => a + '<span class="hl-field">' + esc(b) + '</span>');
		html = html.replace(/(Time:\s*)([^&\n<]+?)(?=\s+Guruwalk:|$)/ig, (_, a, b) => a + '<span class="hl-field">' + esc(b) + '</span>');

		// Previous booking section highlights
		html = html.replace(/(Booking code:\s*)([^&\n<]+)/ig, (_, a, b) => a + '<span class="hl-field hl-code">' + esc(b) + '</span>');
		html = html.replace(/(Date:\s*)([^&\n<]+)/ig, (_, a, b) => a + '<span class="hl-field">' + esc(b) + '</span>');
		html = html.replace(/(Time:\s*)([^&\n<]+)/ig, (_, a, b) => a + '<span class="hl-field">' + esc(b) + '</span>');

		return html;
	}

	window.emailModHighlighter = window.emailModHighlighter || {
		highlight(el, vendor) {
			try {
				if (!el) return;
				const text = el.textContent || "";
				if (!text) return;

				let html = text; // pre has plain text
				const v = (vendor || "").toLowerCase();
				if (v.includes("freetour")) {
					html = highlightFreeTour(html);
					html = highlightFreeTourCancellation(html);
				}
				else if (v.includes("guru")) {
					html = highlightGuruWalk(html);
				}
				else {
					// Unknown vendor: try both patterns
					html = highlightFreeTour(html);
					html = highlightFreeTourCancellation(html);
					html = highlightGuruWalk(html);
				}

				el.innerHTML = html;
			} catch { /* no-op */ }
		},
		setIframeSrcdoc(iframeEl, html) {
			try {
				if (!iframeEl) return;
				iframeEl.setAttribute("srcdoc", html || "");
			} catch { /* no-op */ }
		}
	};
})();


