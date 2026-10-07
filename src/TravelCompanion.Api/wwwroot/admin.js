const reservationTypeSelect = document.querySelector("[data-reservation-type]");

function updateReservationSections() {
    if (!reservationTypeSelect) {
        return;
    }

    const selectedType = reservationTypeSelect.value;
    document.querySelectorAll("[data-reservation-section]").forEach((section) => {
        const visibleTypes = section.dataset.reservationSection
            .split(",")
            .map((value) => value.trim());

        section.hidden = !visibleTypes.includes(selectedType);
    });
}

if (reservationTypeSelect) {
    reservationTypeSelect.addEventListener("change", updateReservationSections);
    updateReservationSections();
}

const planningKindSelect = document.querySelector("[data-planning-kind]");
const linkedRecommendationSelect = document.querySelector("[data-recommendation-select]");

function updatePlanningKind() {
    if (!reservationTypeSelect || !planningKindSelect) {
        return;
    }

    if (reservationTypeSelect.value === "Flight" || reservationTypeSelect.value === "Lodging") {
        planningKindSelect.value = "ConfirmedReservation";
        return;
    }

    if (linkedRecommendationSelect?.value && planningKindSelect.value === "ManualEvent") {
        planningKindSelect.value = "Recommendation";
    }
}

if (planningKindSelect) {
    reservationTypeSelect?.addEventListener("change", updatePlanningKind);
    linkedRecommendationSelect?.addEventListener("change", updatePlanningKind);
    updatePlanningKind();
}

const recommendationAccessSelect = document.querySelector("[data-recommendation-access]");
const recommendationPackageField = document.querySelector("[data-recommendation-package-field]");

function updateRecommendationPackageField() {
    if (!recommendationAccessSelect || !recommendationPackageField) {
        return;
    }

    const isPackageAccess = recommendationAccessSelect.value === "Paid";
    recommendationPackageField.hidden = !isPackageAccess;
}

if (recommendationAccessSelect) {
    recommendationAccessSelect.addEventListener("change", updateRecommendationPackageField);
    updateRecommendationPackageField();
}

const tripUserSelect = document.querySelector("[data-trip-user-select]");
const tripTravelerNameInput = document.querySelector("[data-trip-traveler-name]");

function inferTravelerNameFromSelectedUser() {
    if (!tripUserSelect || !tripTravelerNameInput || tripTravelerNameInput.value.trim().length > 0) {
        return;
    }

    const selectedOption = tripUserSelect.options[tripUserSelect.selectedIndex];
    if (!selectedOption) {
        return;
    }

    tripTravelerNameInput.value = selectedOption.text.replace(/\s+\([^)]*\)\s*$/, "").trim();
}

if (tripUserSelect && tripTravelerNameInput) {
    tripUserSelect.addEventListener("change", inferTravelerNameFromSelectedUser);
    inferTravelerNameFromSelectedUser();
}

document.querySelectorAll("[data-progress-form]").forEach((form) => {
    const progress = form.querySelector("[data-submit-progress]");
    const message = form.querySelector("[data-submit-progress-message]");

    form.addEventListener("submit", (event) => {
        if (!progress || event.defaultPrevented || !form.checkValidity()) {
            return;
        }

        const submitter = event.submitter;
        const loadingMessage = submitter?.dataset.loadingMessage ?? "Procesando...";

        // Page-specific publish/discard confirmations run before progress disables any controls.
        requestAnimationFrame(() => {
            if (event.defaultPrevented) return;
            progress.hidden = false;
            form.setAttribute("aria-busy", "true");
            if (message) message.textContent = loadingMessage;
            form.querySelectorAll("button[type='submit']").forEach((button) => {
                button.disabled = true;
            });
        });
    });
});

// Native HTML validation augments server validation; the server remains authoritative.
function initializeAdminValidation(root = document) {
    root.querySelectorAll("input[data-val], select[data-val], textarea[data-val]").forEach((control) => {
        if (!control.form) return;
        // Non-nullable booleans receive data-val-required even though false is valid.
        if (control.dataset.valRequired && !["hidden", "checkbox", "radio"].includes(control.type)) control.required = true;
        if (control.dataset.valRegexPattern && !control.pattern) control.pattern = control.dataset.valRegexPattern;
        if (control.dataset.valLengthMax && !control.hasAttribute("maxlength"))
            control.maxLength = Number(control.dataset.valLengthMax);
        const message = [...control.form.querySelectorAll("[data-valmsg-for]")]
            .find((candidate) => candidate.dataset.valmsgFor === control.name);
        if (message) {
            const previousMessageId = message.id;
            message.id = `${control.id || control.name}-validation`;
            const describedBy = (control.getAttribute("aria-describedby") ?? "").split(/\s+/)
                .filter((id) => id && id !== previousMessageId && id !== message.id);
            control.setAttribute("aria-describedby", [...describedBy, message.id].join(" "));
        }
        if (control.dataset.adminValidationBound) return;
        control.dataset.adminValidationBound = "true";
        control.addEventListener("invalid", () => {
            control.setAttribute("aria-invalid", "true");
            control.classList.add("input-validation-error");
            if (message) {
                message.textContent = control.validity.valueMissing ? control.dataset.valRequired
                    : control.validity.patternMismatch ? control.dataset.valRegex : control.validationMessage;
                message.className = "field-validation-error";
            }
        });
        control.addEventListener("input", () => {
            if (!control.validity.valid) return;
            control.removeAttribute("aria-invalid");
            control.classList.remove("input-validation-error");
            if (message) { message.textContent = ""; message.className = "field-validation-valid"; }
        });
    });
}
initializeAdminValidation();

const firstValidationError = document.querySelector(".input-validation-error:not([type='hidden'])")
    ?? document.querySelector(".validation-summary-errors");
if (firstValidationError) {
    if (!firstValidationError.matches("input, select, textarea, button")) firstValidationError.setAttribute("tabindex", "-1");
    firstValidationError.focus();
    firstValidationError.scrollIntoView({ block: "center", behavior: "auto" });
}
