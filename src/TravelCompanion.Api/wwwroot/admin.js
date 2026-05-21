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
