(() => {
    const form = document.querySelector("[data-create-trip-form]");
    if (!form) {
        return;
    }

    const container = form.querySelector("[data-city-segments]");
    const template = form.querySelector("[data-city-segment-template]");
    const addButton = form.querySelector("[data-add-city]");

    const addDays = (value, days) => {
        if (!value) {
            return "";
        }
        const date = new Date(`${value}T00:00:00Z`);
        date.setUTCDate(date.getUTCDate() + days);
        return date.toISOString().slice(0, 10);
    };

    function rows() {
        return [...container.querySelectorAll("[data-city-segment]")];
    }

    function reindex() {
        rows().forEach((row, index) => {
            row.querySelectorAll("[name]").forEach((input) => {
                input.name = input.name.replace(/CitySegments\[\d+\]/, `CitySegments[${index}]`);
            });
        });
        rows().forEach((row) => {
            row.querySelector("[data-remove-city]").disabled = rows().length === 1;
        });
    }

    function bindRow(row) {
        row.querySelector("[data-remove-city]").addEventListener("click", () => {
            if (rows().length === 1) {
                return;
            }
            row.remove();
            reindex();
        });
    }

    rows().forEach(bindRow);
    reindex();

    addButton.addEventListener("click", () => {
        const previous = rows().at(-1);
        const previousEnd = previous?.querySelector("[name$='.EndsOn']")?.value ?? "";
        const index = rows().length;
        const wrapper = document.createElement("div");
        wrapper.innerHTML = template.innerHTML.replaceAll("__index__", String(index)).trim();
        const row = wrapper.firstElementChild;
        const startsOn = row.querySelector("[name$='.StartsOn']");
        const endsOn = row.querySelector("[name$='.EndsOn']");
        startsOn.value = addDays(previousEnd, 1);
        endsOn.value = addDays(startsOn.value, 2);
        container.append(row);
        bindRow(row);
        reindex();
        row.querySelector("[name$='.City']").focus();
    });
})();
