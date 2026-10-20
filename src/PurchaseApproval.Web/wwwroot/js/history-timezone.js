// Makes the browser's IANA timezone explicit and editable before local-date boundaries are submitted.
(() => {
    const input = document.getElementById("history-timezone");
    if (input && !input.value) {
        input.value = Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
    }
})();
