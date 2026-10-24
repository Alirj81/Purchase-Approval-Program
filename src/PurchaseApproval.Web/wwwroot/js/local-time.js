// Rewrites every timestamp into a compact viewer-local value. The full offset and IANA zone are
// kept in the tooltip and accessible label instead of making every table cell excessively wide.
// Digits stay Western on purpose: amounts and times must read the same in both languages.
(() => {
    const zone = (() => {
        try {
            return Intl.DateTimeFormat().resolvedOptions().timeZone || "";
        } catch {
            return "";
        }
    })();

    const pad = value => String(value).padStart(2, "0");
    const offsetLabel = date => {
        const minutes = -date.getTimezoneOffset();
        const sign = minutes < 0 ? "-" : "+";
        const absolute = Math.abs(minutes);
        return `UTC${sign}${pad(Math.floor(absolute / 60))}:${pad(absolute % 60)}`;
    };

    for (const element of document.querySelectorAll("time[data-local-time]")) {
        const value = element.getAttribute("datetime");
        if (!value) continue;
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) continue;
        const local = `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} · ${pad(date.getHours())}:${pad(date.getMinutes())}`;
        const detail = `${local.replace(" · ", " ")} (${offsetLabel(date)}${zone ? ", " + zone : ""})`;
        element.textContent = local;
        element.setAttribute("title", detail);
        element.setAttribute("aria-label", detail);
    }
})();
