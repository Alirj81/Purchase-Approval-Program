// Detects the administrator's IANA timezone; the editable field is the required fallback.
// Status texts come from data attributes so they are localized by the page, not by this script.
(() => {
    const input = document.getElementById("report-timezone");
    const status = document.getElementById("report-timezone-status");
    if (!input || !status) return;

    if (!input.value) {
        try {
            input.value = Intl.DateTimeFormat().resolvedOptions().timeZone || "";
        } catch {
            input.value = "";
        }
    }

    const updateStatus = () => {
        status.textContent = input.value
            ? (status.dataset.ready || "{0}").replace("{0}", input.value)
            : (status.dataset.missing || "");
    };
    input.addEventListener("input", updateStatus);
    updateStatus();
})();
