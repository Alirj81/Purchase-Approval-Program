// Execution outcome form helpers:
//  1. detect the device IANA timezone and prefill the reported occurrence time (both editable);
//  2. show only the detail fields that the chosen method needs;
//  3. confirm an Unsuccessful outcome before posting, because it needs a reason.
// The server resolves the offset for the *entered* date (see TransactionTimeRules); this script
// never sends the device's current offset as if it applied to the past.
(() => {
    const localInput = document.getElementById("reported-local-time");
    const zoneInput = document.getElementById("transaction-time-zone");
    const zoneStatus = document.getElementById("transaction-time-zone-status");

    if (zoneInput && !zoneInput.value) {
        try {
            zoneInput.value = Intl.DateTimeFormat().resolvedOptions().timeZone || "";
        } catch {
            zoneInput.value = "";
        }
    }
    if (zoneStatus) {
        const update = () => {
            zoneStatus.textContent = zoneInput && zoneInput.value
                ? zoneStatus.dataset.detected.replace("{0}", zoneInput.value)
                : zoneStatus.dataset.missing;
        };
        zoneInput?.addEventListener("input", update);
        update();
    }

    if (localInput && !localInput.value) {
        const now = new Date();
        const pad = value => String(value).padStart(2, "0");
        localInput.value = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}T${pad(now.getHours())}:${pad(now.getMinutes())}`;
    }

    const method = document.getElementById("transfer-method");
    const groups = Array.from(document.querySelectorAll(".method-fields"));
    const updateMethodFields = () => {
        for (const group of groups) {
            const methods = group.dataset.methods?.split(",") ?? [group.dataset.method];
            group.hidden = !methods.includes(method?.value);
        }
    };
    method?.addEventListener("change", updateMethodFields);
    updateMethodFields();

    const unsuccessfulButton = document.getElementById("record-unsuccessful");
    unsuccessfulButton?.addEventListener("click", event => {
        const reason = document.getElementById("failure-reason");
        if (reason && !reason.value.trim()) {
            event.preventDefault();
            reason.focus();
            reason.setAttribute("aria-invalid", "true");
            document.getElementById("failure-reason-help")?.classList.add("text-danger");
        }
    });
})();
