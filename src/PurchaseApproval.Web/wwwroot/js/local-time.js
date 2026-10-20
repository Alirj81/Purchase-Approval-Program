// Rewrites every <time data-local-time datetime="...Z"> element into the viewer's own timezone,
// labelled with its UTC offset and IANA zone, e.g. "2026-10-19 15:30 (UTC+03:30, Asia/Tehran)".
// The server-rendered text is a labelled UTC fallback, so nothing is wrong if this never runs.
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
        const local = `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
        element.textContent = `${local} (${offsetLabel(date)}${zone ? ", " + zone : ""})`;
        element.setAttribute("title", `${value} (UTC)`);
    }
})();
