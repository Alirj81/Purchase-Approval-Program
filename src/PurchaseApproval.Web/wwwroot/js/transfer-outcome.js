(() => {
    const localInput = document.getElementById("reported-local-time");
    const offsetInput = document.getElementById("utc-offset-minutes");
    if (localInput && offsetInput) {
        offsetInput.value = String(-new Date().getTimezoneOffset());
        if (!localInput.value) {
            const now = new Date();
            const local = new Date(now.getTime() - now.getTimezoneOffset() * 60000);
            localInput.value = local.toISOString().slice(0, 16);
        }
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
})();
