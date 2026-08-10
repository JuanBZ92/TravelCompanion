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
        if (!progress) {
            return;
        }

        const submitter = event.submitter;
        const loadingMessage = submitter?.dataset.loadingMessage ?? "Procesando...";

        progress.hidden = false;
        form.setAttribute("aria-busy", "true");
        if (message) {
            message.textContent = loadingMessage;
        }

        requestAnimationFrame(() => {
            form.querySelectorAll("button[type='submit']").forEach((button) => {
                button.disabled = true;
            });
        });
    });
});
