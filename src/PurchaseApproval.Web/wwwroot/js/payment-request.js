// Dynamic expense rows for the request form: add/remove rows, keep the posted field names in
// order (Input.Lines[n].Property) so model binding works, and show a running estimated total.
// The estimate is a convenience only; the server recalculates the authoritative total.
// Western digits are forced with "en-US" so the preview matches the saved value in both languages.
(() => {
    const container = document.querySelector("#expense-lines");
    const template = document.querySelector("#expense-line-template");
    const addButton = document.querySelector("#add-line");
    const total = document.querySelector("#request-total");
    const submitButton = document.querySelector("#submit-request");
    const form = document.querySelector("#request-form");
    if (!container || !template || !addButton || !total) return;

    function reindex() {
        [...container.querySelectorAll(".expense-line")].forEach((row, index) => {
            row.querySelector(".line-number")?.replaceChildren(document.createTextNode(String(index + 1)));
            [[".line-description", "Description"], [".line-amount", "AmountRials"],
             [".line-remarks", "Remarks"], [".line-invoice", "HasInvoice"]]
                .forEach(([selector, property]) => {
                    const element = row.querySelector(selector);
                    if (element) {
                        element.name = `Input.Lines[${index}].${property}`;
                        element.id = `line-${property.toLowerCase()}-${index}`;
                        const label = row.querySelector(`label[data-for="${property}"]`);
                        if (label) label.htmlFor = element.id;
                    }
                });
        });
    }

    function updateTotal() {
        const value = [...container.querySelectorAll(".line-amount")]
            .reduce((sum, input) => sum + (Number.parseInt(input.value, 10) || 0), 0);
        total.textContent = value.toLocaleString("en-US");
    }

    addButton.addEventListener("click", () => {
        if (container.querySelectorAll(".expense-line").length >= 50) return;
        container.append(template.content.cloneNode(true));
        reindex();
        container.querySelector(".expense-line:last-child .line-description")?.focus();
    });
    container.addEventListener("click", event => {
        const button = event.target.closest(".remove-line");
        if (!button || container.querySelectorAll(".expense-line").length <= 1) return;
        button.closest(".expense-line").remove();
        reindex();
        updateTotal();
    });
    container.addEventListener("input", event => {
        if (event.target.matches(".line-amount")) updateTotal();
    });
    // Disable the submit button once the form is really being sent so a double click cannot
    // post twice; the server-side submission key is the real protection.
    form?.addEventListener("submit", () => {
        if (submitButton) {
            submitButton.disabled = true;
            submitButton.textContent = submitButton.dataset.busyText || submitButton.textContent;
        }
    });
    reindex();
    updateTotal();
})();
