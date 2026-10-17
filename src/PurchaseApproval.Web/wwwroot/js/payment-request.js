(() => {
    const container = document.querySelector("#expense-lines");
    const template = document.querySelector("#expense-line-template");
    const addButton = document.querySelector("#add-line");
    const total = document.querySelector("#request-total");
    if (!container || !template || !addButton || !total) return;

    function reindex() {
        [...container.querySelectorAll(".expense-line")].forEach((row, index) => {
            [[".line-description", "Description"], [".line-amount", "AmountRials"],
             [".line-remarks", "Remarks"], [".line-invoice", "HasInvoice"]]
                .forEach(([selector, property]) => {
                    const element = row.querySelector(selector);
                    if (element) {
                        element.name = `Input.Lines[${index}].${property}`;
                        if (property === "HasInvoice") {
                            element.id = `line-invoice-${index}`;
                            const label = row.querySelector("label.form-check-label");
                            if (label) label.htmlFor = element.id;
                        }
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
    reindex();
    updateTotal();
})();
